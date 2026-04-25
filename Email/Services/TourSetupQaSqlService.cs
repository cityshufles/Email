using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;

namespace Email.Services
{
    /// <summary>
    /// SQL implementation for manager QA review of TourSetup staging rows.
    /// </summary>
    public sealed partial class TourSetupQaSqlService : ITourSetupQaService
    {
        private readonly SqlConnectionFactory _connectionFactory;
        private readonly IWebHostEnvironment _environment;
        private static readonly Regex TimeHintRegex = new(@"\b(?<h>\d{1,2}):(?<m>\d{2})\s*(?<ampm>AM|PM|am|pm)?\b", RegexOptions.Compiled);
        private static readonly Regex DurationHintRegex = new(@"\b(?<num>\d{1,2}(?:\.\d+)?)\s*(?<unit>hours?|hrs?|hr|minutes?|mins?|min)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private readonly object _hintsLock = new();
        private HintCache? _hintCache;

        public TourSetupQaSqlService(SqlConnectionFactory connectionFactory, IWebHostEnvironment environment)
        {
            _connectionFactory = connectionFactory;
            _environment = environment;
        }

        public async Task<List<TourSetupQaBatchSummary>> GetRecentBatchSummariesAsync(int maxBatches = 20, CancellationToken ct = default)
        {
            const string sql = @"
SELECT TOP (@TopN)
    s.BatchId,
    MIN(s.CreatedAt) AS CreatedAtUtc,
    COUNT(1) AS TotalRows,
    SUM(CASE WHEN s.GapTypeComputed <> N'OK' THEN 1 ELSE 0 END) AS NonOkRows,
    SUM(CASE WHEN s.GapTypeComputed <> N'OK' AND s.ReviewedAt IS NULL THEN 1 ELSE 0 END) AS PendingReviewRows,
    SUM(CASE WHEN s.AppliedAt IS NOT NULL THEN 1 ELSE 0 END) AS AppliedRows
FROM dbo.TourSetupQaStaging s
GROUP BY
    s.BatchId
ORDER BY
    MIN(s.CreatedAt) DESC,
    s.BatchId DESC;";

            var result = new List<TourSetupQaBatchSummary>();
            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.Add(new SqlParameter("@TopN", SqlDbType.Int) { Value = maxBatches <= 0 ? 20 : maxBatches });

                using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    result.Add(new TourSetupQaBatchSummary
                    {
                        BatchId = reader.GetGuid(reader.GetOrdinal("BatchId")),
                        CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("CreatedAtUtc")),
                        TotalRows = reader.GetInt32(reader.GetOrdinal("TotalRows")),
                        NonOkRows = reader.GetInt32(reader.GetOrdinal("NonOkRows")),
                        PendingReviewRows = reader.GetInt32(reader.GetOrdinal("PendingReviewRows")),
                        AppliedRows = reader.GetInt32(reader.GetOrdinal("AppliedRows"))
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TourSetupQaSqlService] GetRecentBatchSummariesAsync error: {ex.Message}");
                throw;
            }

            return result;
        }

        public async Task<List<TourSetupQaRow>> GetBatchRowsAsync(Guid batchId, bool onlyNonOk = true, bool onlyPendingReview = false, CancellationToken ct = default)
        {
            const string sql = @"
SELECT
    s.Id,
    s.BatchId,
    s.SourceType,
    s.VendorName,
    s.VendorKey,
    s.RawTourName,
    s.NormalizedTourKey,
    s.RecordCount,
    s.TourDate,
    s.ProposedTourId,
    s.ProposedMasterTourName,
    s.MeetingPlaceExpected,
    s.MeetingTimeExpected,
    s.MeetingPlaceProposed,
    s.MeetingTimeProposed,
    s.GapTypeComputed,
    s.GapTypeFinal,
    s.Notes,
    s.ReviewedBy,
    s.ReviewedAt,
    s.AppliedAt,
    s.CreatedAt,
    s.UpdatedAt
FROM dbo.TourSetupQaStaging s
WHERE s.BatchId = @BatchId
  AND (@OnlyNonOk = 0 OR s.GapTypeComputed <> N'OK')
  AND (@OnlyPendingReview = 0 OR s.ReviewedAt IS NULL)
ORDER BY
    CASE s.GapTypeComputed
        WHEN N'MissingTour' THEN 0
        WHEN N'MissingAlias' THEN 1
        WHEN N'MissingSchedule' THEN 2
        WHEN N'DetailDrift' THEN 3
        WHEN N'OK' THEN 9
        ELSE 8
    END,
    s.SourceType,
    s.RecordCount DESC,
    s.TourDate DESC,
    s.Id DESC;";

            var rows = new List<TourSetupQaRow>();
            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.Add(new SqlParameter("@BatchId", SqlDbType.UniqueIdentifier) { Value = batchId });
                cmd.Parameters.Add(new SqlParameter("@OnlyNonOk", SqlDbType.Bit) { Value = onlyNonOk });
                cmd.Parameters.Add(new SqlParameter("@OnlyPendingReview", SqlDbType.Bit) { Value = onlyPendingReview });

                using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    rows.Add(MapQaRow(reader));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TourSetupQaSqlService] GetBatchRowsAsync error: {ex.Message}");
                throw;
            }

            return rows;
        }

        public async Task<bool> SaveReviewAsync(
            long stagingId,
            string? gapTypeFinal,
            string? meetingPlaceExpected,
            string? meetingTimeExpected,
            string? notes,
            string reviewedBy,
            bool markApplied,
            CancellationToken ct = default)
        {
            const string sql = @"
UPDATE dbo.TourSetupQaStaging
SET
    GapTypeFinal = CASE
        WHEN NULLIF(LTRIM(RTRIM(@GapTypeFinal)), N'') IS NULL THEN GapTypeComputed
        ELSE @GapTypeFinal
    END,
    MeetingPlaceExpected = NULLIF(LTRIM(RTRIM(@MeetingPlaceExpected)), N''),
    MeetingTimeExpected = NULLIF(LTRIM(RTRIM(@MeetingTimeExpected)), N''),
    Notes = @Notes,
    ReviewedBy = @ReviewedBy,
    ReviewedAt = SYSUTCDATETIME(),
    AppliedAt = CASE
        WHEN @MarkApplied = 1 THEN COALESCE(AppliedAt, SYSUTCDATETIME())
        ELSE AppliedAt
    END,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id;";

            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.BigInt) { Value = stagingId });
                cmd.Parameters.Add(new SqlParameter("@GapTypeFinal", SqlDbType.NVarChar, 40) { Value = (object?)gapTypeFinal ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@MeetingPlaceExpected", SqlDbType.NVarChar, 255) { Value = (object?)meetingPlaceExpected ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@MeetingTimeExpected", SqlDbType.NVarChar, 50) { Value = (object?)meetingTimeExpected ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@Notes", SqlDbType.NVarChar, 2000) { Value = (object?)notes ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@ReviewedBy", SqlDbType.NVarChar, 128) { Value = string.IsNullOrWhiteSpace(reviewedBy) ? "unknown" : reviewedBy.Trim() });
                cmd.Parameters.Add(new SqlParameter("@MarkApplied", SqlDbType.Bit) { Value = markApplied });

                var affectedRows = await cmd.ExecuteNonQueryAsync(ct);
                return affectedRows > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TourSetupQaSqlService] SaveReviewAsync error: {ex.Message}");
                throw;
            }
        }

        public async Task<int> ApproveAllNonOkAsync(Guid batchId, string reviewedBy, CancellationToken ct = default)
        {
            const string sql = @"
UPDATE s
SET
    GapTypeFinal = CASE
        WHEN NULLIF(LTRIM(RTRIM(ISNULL(s.GapTypeFinal, N''))), N'') IS NULL THEN s.GapTypeComputed
        ELSE s.GapTypeFinal
    END,
    ReviewedBy = @ReviewedBy,
    ReviewedAt = SYSUTCDATETIME(),
    UpdatedAt = SYSUTCDATETIME()
FROM dbo.TourSetupQaStaging s
WHERE s.BatchId = @BatchId
  AND s.GapTypeComputed <> N'OK'
  AND s.ReviewedAt IS NULL;";

            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.Add(new SqlParameter("@BatchId", SqlDbType.UniqueIdentifier) { Value = batchId });
                cmd.Parameters.Add(new SqlParameter("@ReviewedBy", SqlDbType.NVarChar, 128) { Value = string.IsNullOrWhiteSpace(reviewedBy) ? "unknown" : reviewedBy.Trim() });
                var affectedRows = await cmd.ExecuteNonQueryAsync(ct);
                return affectedRows;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TourSetupQaSqlService] ApproveAllNonOkAsync error: {ex.Message}");
                throw;
            }
        }

        private static TourSetupQaRow MapQaRow(SqlDataReader reader)
        {
            return new TourSetupQaRow
            {
                Id = reader.GetInt64(reader.GetOrdinal("Id")),
                BatchId = reader.GetGuid(reader.GetOrdinal("BatchId")),
                SourceType = reader.GetString(reader.GetOrdinal("SourceType")),
                VendorName = reader.IsDBNull(reader.GetOrdinal("VendorName")) ? null : reader.GetString(reader.GetOrdinal("VendorName")),
                VendorKey = reader.IsDBNull(reader.GetOrdinal("VendorKey")) ? null : reader.GetString(reader.GetOrdinal("VendorKey")),
                RawTourName = reader.GetString(reader.GetOrdinal("RawTourName")),
                NormalizedTourKey = reader.GetString(reader.GetOrdinal("NormalizedTourKey")),
                RecordCount = reader.GetInt32(reader.GetOrdinal("RecordCount")),
                TourDate = reader.IsDBNull(reader.GetOrdinal("TourDate")) ? null : reader.GetDateTime(reader.GetOrdinal("TourDate")),
                ProposedTourId = reader.IsDBNull(reader.GetOrdinal("ProposedTourId")) ? null : reader.GetInt32(reader.GetOrdinal("ProposedTourId")),
                ProposedMasterTourName = reader.IsDBNull(reader.GetOrdinal("ProposedMasterTourName")) ? null : reader.GetString(reader.GetOrdinal("ProposedMasterTourName")),
                MeetingPlaceExpected = reader.IsDBNull(reader.GetOrdinal("MeetingPlaceExpected")) ? null : reader.GetString(reader.GetOrdinal("MeetingPlaceExpected")),
                MeetingTimeExpected = reader.IsDBNull(reader.GetOrdinal("MeetingTimeExpected")) ? null : reader.GetString(reader.GetOrdinal("MeetingTimeExpected")),
                MeetingPlaceProposed = reader.IsDBNull(reader.GetOrdinal("MeetingPlaceProposed")) ? null : reader.GetString(reader.GetOrdinal("MeetingPlaceProposed")),
                MeetingTimeProposed = reader.IsDBNull(reader.GetOrdinal("MeetingTimeProposed")) ? null : reader.GetString(reader.GetOrdinal("MeetingTimeProposed")),
                GapTypeComputed = reader.GetString(reader.GetOrdinal("GapTypeComputed")),
                GapTypeFinal = reader.IsDBNull(reader.GetOrdinal("GapTypeFinal")) ? null : reader.GetString(reader.GetOrdinal("GapTypeFinal")),
                Notes = reader.IsDBNull(reader.GetOrdinal("Notes")) ? null : reader.GetString(reader.GetOrdinal("Notes")),
                ReviewedBy = reader.IsDBNull(reader.GetOrdinal("ReviewedBy")) ? null : reader.GetString(reader.GetOrdinal("ReviewedBy")),
                ReviewedAt = reader.IsDBNull(reader.GetOrdinal("ReviewedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("ReviewedAt")),
                AppliedAt = reader.IsDBNull(reader.GetOrdinal("AppliedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("AppliedAt")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt"))
            };
        }
    }
}
