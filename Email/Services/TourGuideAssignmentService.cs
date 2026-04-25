using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Email.Models;

namespace Email.Services
{
    /// <summary>
    /// Created: 2026-01-08 - Direct SQL implementation for TourGuideAssignment.
    /// </summary>
    public sealed class TourGuideAssignmentService : ITourGuideAssignmentService
    {
        private readonly SqlConnectionFactory _connectionFactory;

        public TourGuideAssignmentService(SqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<List<TourGuideAssignment>> GetAssignmentsForDateAsync(DateTime date, CancellationToken ct = default)
        {
            const string sql = @"
SELECT Id, TourDate, TourName, TourTime, GuideId, CreatedAt, UpdatedAt
FROM dbo.TourGuideAssignment
WHERE TourDate = @TourDate
ORDER BY TourTime, TourName;";

            var list = new List<TourGuideAssignment>();
            using var conn = _connectionFactory.CreateOpenConnection();
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@TourDate", SqlDbType.Date) { Value = date.Date });
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(Map(reader));
            }
            return list;
        }

        public async Task<TourGuideAssignment?> GetAssignmentAsync(DateTime date, string tourName, string tourTime, CancellationToken ct = default)
        {
            const string sql = @"
SELECT TOP 1 Id, TourDate, TourName, TourTime, GuideId, CreatedAt, UpdatedAt
FROM dbo.TourGuideAssignment
WHERE TourDate = @TourDate AND TourName = @TourName AND TourTime = @TourTime;";

            using var conn = _connectionFactory.CreateOpenConnection();
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@TourDate", SqlDbType.Date) { Value = date.Date });
            cmd.Parameters.Add(new SqlParameter("@TourName", SqlDbType.NVarChar, 255) { Value = tourName });
            cmd.Parameters.Add(new SqlParameter("@TourTime", SqlDbType.NVarChar, 50) { Value = tourTime });
            using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                return Map(reader);
            }
            return null;
        }

        public async Task<TourGuideAssignment> SaveAssignmentAsync(TourGuideAssignment assignment, CancellationToken ct = default)
        {
            // Use MERGE (upsert) pattern to insert or update
            const string sql = @"
MERGE dbo.TourGuideAssignment AS target
USING (SELECT @TourDate AS TourDate, @TourName AS TourName, @TourTime AS TourTime) AS source
ON target.TourDate = source.TourDate AND target.TourName = source.TourName AND target.TourTime = source.TourTime
WHEN MATCHED THEN
    UPDATE SET GuideId = @GuideId, UpdatedAt = GETUTCDATE()
WHEN NOT MATCHED THEN
    INSERT (TourDate, TourName, TourTime, GuideId, CreatedAt)
    VALUES (@TourDate, @TourName, @TourTime, @GuideId, GETUTCDATE())
OUTPUT inserted.Id, inserted.TourDate, inserted.TourName, inserted.TourTime, inserted.GuideId, inserted.CreatedAt, inserted.UpdatedAt;";

            using var conn = _connectionFactory.CreateOpenConnection();
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@TourDate", SqlDbType.Date) { Value = assignment.TourDate.Date });
            cmd.Parameters.Add(new SqlParameter("@TourName", SqlDbType.NVarChar, 255) { Value = assignment.TourName });
            cmd.Parameters.Add(new SqlParameter("@TourTime", SqlDbType.NVarChar, 50) { Value = assignment.TourTime });
            cmd.Parameters.Add(new SqlParameter("@GuideId", SqlDbType.Int) { Value = assignment.GuideId.HasValue ? assignment.GuideId.Value : DBNull.Value });

            using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                return Map(reader);
            }
            // Fallback: return what was passed in (should not happen with OUTPUT clause)
            return assignment;
        }

        private static TourGuideAssignment Map(SqlDataReader r)
        {
            return new TourGuideAssignment
            {
                Id = r.GetInt32(r.GetOrdinal("Id")),
                TourDate = r.GetDateTime(r.GetOrdinal("TourDate")),
                TourName = r.GetString(r.GetOrdinal("TourName")),
                TourTime = r.GetString(r.GetOrdinal("TourTime")),
                GuideId = r.IsDBNull(r.GetOrdinal("GuideId")) ? null : r.GetInt32(r.GetOrdinal("GuideId")),
                CreatedAt = r.IsDBNull(r.GetOrdinal("CreatedAt")) ? null : r.GetDateTime(r.GetOrdinal("CreatedAt")),
                UpdatedAt = r.IsDBNull(r.GetOrdinal("UpdatedAt")) ? null : r.GetDateTime(r.GetOrdinal("UpdatedAt"))
            };
        }
    }
}
