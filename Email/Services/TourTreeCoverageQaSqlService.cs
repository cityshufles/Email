using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;

namespace Email.Services
{
    /// <summary>
    /// Read-only SQL diagnostics service for Tour Tree Coverage QA.
    /// </summary>
    public sealed class TourTreeCoverageQaSqlService : ITourTreeCoverageQaService
    {
        private const string SourceEmailsCurrentWindow = "emails_current_window";
        private const string SourceEmailsHistory = "emails_history";
        private const string SourceActiveMappings = "active_mappings";
        private const string SourceActiveTours = "active_tours";
        private const string SourceQaInputTables = "qa_input_tables";
        private const string SourceManagerNotes = "manager_notes";

        private static readonly Regex MultiWhitespaceRegex = new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly SqlConnectionFactory _connectionFactory;
        private readonly IWebHostEnvironment _environment;

        public TourTreeCoverageQaSqlService(SqlConnectionFactory connectionFactory, IWebHostEnvironment environment)
        {
            _connectionFactory = connectionFactory;
            _environment = environment;
        }

        public async Task<TourTreeCoverageQaSnapshot> GetCoverageSnapshotAsync(TourTreeCoverageQaQuery? query = null, CancellationToken ct = default)
        {
            return await BuildSnapshotAsync(query, ct);
        }

        public async Task<List<TourTreeCoverageQaRow>> GetCoverageRowsAsync(TourTreeCoverageQaQuery? query = null, CancellationToken ct = default)
        {
            var snapshot = await BuildSnapshotAsync(query, ct);
            return snapshot.Rows;
        }

        public async Task<TourTreeCoverageQaSummary> GetCoverageSummaryAsync(TourTreeCoverageQaQuery? query = null, CancellationToken ct = default)
        {
            var snapshot = await BuildSnapshotAsync(query, ct);
            return snapshot.Summary;
        }

        public async Task<List<TourTreeCoverageQaSourceBreakdownItem>> GetCoverageSourceBreakdownAsync(TourTreeCoverageQaQuery? query = null, CancellationToken ct = default)
        {
            var snapshot = await BuildSnapshotAsync(query, ct);
            return snapshot.SourceBreakdown;
        }

        private async Task<TourTreeCoverageQaSnapshot> BuildSnapshotAsync(TourTreeCoverageQaQuery? incomingQuery, CancellationToken ct)
        {
            var effectiveQuery = NormalizeQuery(incomingQuery);
            using var conn = _connectionFactory.CreateOpenConnection();

            var currentWindowRows = await LoadEmailCurrentWindowSourceRowsAsync(conn, effectiveQuery, ct);
            var emailHistoryRows = await LoadEmailHistorySourceRowsAsync(conn, effectiveQuery, ct);
            var activeMappingRows = await LoadActiveMappingSourceRowsAsync(conn, effectiveQuery, ct);
            var activeTourRows = await LoadActiveTourSourceRowsAsync(conn, ct);
            var qaInputRows = await LoadQaInputSourceRowsAsync(conn, effectiveQuery, ct);
            var managerParse = ParseManagerNotesSourceRows();

            var allMappingDiagnostics = await LoadAllMappingDiagnosticsAsync(conn, ct);
            var activeTourNameLookup = await LoadActiveTourNameLookupAsync(conn, ct);

            var allRows = BuildRows(
                currentWindowRows,
                emailHistoryRows,
                activeMappingRows,
                activeTourRows,
                qaInputRows,
                managerParse,
                allMappingDiagnostics,
                activeTourNameLookup);

            var filteredRows = ApplyRowFilters(allRows, effectiveQuery);

            return new TourTreeCoverageQaSnapshot
            {
                GeneratedAtUtc = DateTime.UtcNow,
                EffectiveQuery = effectiveQuery,
                Rows = filteredRows,
                Summary = BuildSummary(filteredRows),
                SourceBreakdown = BuildSourceBreakdown(filteredRows, effectiveQuery),
                CanonicalGroups = BuildCanonicalGroups(filteredRows),
                NormalizedGroups = BuildNormalizedGroups(filteredRows)
            };
        }

        private static TourTreeCoverageQaQuery NormalizeQuery(TourTreeCoverageQaQuery? query)
        {
            var normalized = query is null
                ? new TourTreeCoverageQaQuery()
                : new TourTreeCoverageQaQuery
                {
                    DateFrom = query.DateFrom,
                    DateTo = query.DateTo,
                    Vendor = TrimOrNull(query.Vendor),
                    SearchText = TrimOrNull(query.SearchText),
                    IncludeEmailsCurrentWindow = query.IncludeEmailsCurrentWindow,
                    IncludeEmailsHistory = query.IncludeEmailsHistory,
                    IncludeActiveMappings = query.IncludeActiveMappings,
                    IncludeActiveTours = query.IncludeActiveTours,
                    IncludeQaInputTables = query.IncludeQaInputTables,
                    IncludeManagerNotes = query.IncludeManagerNotes
                };

            if (!normalized.DateFrom.HasValue && !normalized.DateTo.HasValue)
            {
                normalized.DateFrom = DateTime.Today;
                normalized.DateTo = DateTime.Today.AddDays(2);
            }
            else if (normalized.DateFrom.HasValue && !normalized.DateTo.HasValue)
            {
                normalized.DateTo = normalized.DateFrom.Value.Date.AddDays(2);
            }
            else if (!normalized.DateFrom.HasValue && normalized.DateTo.HasValue)
            {
                normalized.DateFrom = normalized.DateTo.Value.Date.AddDays(-2);
            }

            if (normalized.DateFrom.HasValue)
            {
                normalized.DateFrom = normalized.DateFrom.Value.Date;
            }

            if (normalized.DateTo.HasValue)
            {
                normalized.DateTo = normalized.DateTo.Value.Date;
            }

            if (normalized.DateFrom.HasValue && normalized.DateTo.HasValue && normalized.DateTo.Value <= normalized.DateFrom.Value)
            {
                normalized.DateTo = normalized.DateFrom.Value.AddDays(1);
            }

            return normalized;
        }

        private async Task<List<SourceRow>> LoadEmailCurrentWindowSourceRowsAsync(SqlConnection conn, TourTreeCoverageQaQuery query, CancellationToken ct)
        {
            var sql = new StringBuilder(@"
SELECT
    NULLIF(LTRIM(RTRIM(VendorName)), N'') AS VendorName,
    LTRIM(RTRIM(TourName)) AS RawTourName,
    COUNT(1) AS RecordCount
FROM dbo.AutomaticGmail_ProcessedEmails
WHERE NULLIF(LTRIM(RTRIM(TourName)), N'') IS NOT NULL
  AND (IsModification = 1 OR EmailType LIKE '%booking%' OR EmailType = 'Booking Confirmation')
  AND (IsCancellation = 0 AND (ProcessingStatus IS NULL OR ProcessingStatus != 'cancelled') AND EmailType != 'cancellation')
  AND NOT EXISTS (
      SELECT 1
      FROM dbo.Bookings b
      WHERE b.BookingCode = AutomaticGmail_ProcessedEmails.BookingCode
        AND (b.IsCancellation = 1 OR b.IsActive = 0 OR UPPER(b.BookingStatus) = 'CANCELLED')
  )");

            using var cmd = new SqlCommand();
            cmd.Connection = conn;

            if (query.DateFrom.HasValue)
            {
                sql.Append(" AND TourDate >= @StartDate");
                cmd.Parameters.Add(new SqlParameter("@StartDate", SqlDbType.NVarChar, 10) { Value = query.DateFrom.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
            }

            if (query.DateTo.HasValue)
            {
                sql.Append(" AND TourDate < @EndDate");
                cmd.Parameters.Add(new SqlParameter("@EndDate", SqlDbType.NVarChar, 10) { Value = query.DateTo.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
            }

            if (!string.IsNullOrWhiteSpace(query.Vendor))
            {
                sql.Append(" AND VendorName = @Vendor");
                cmd.Parameters.Add(new SqlParameter("@Vendor", SqlDbType.NVarChar, 255) { Value = query.Vendor! });
            }

            sql.Append(@"
GROUP BY
    NULLIF(LTRIM(RTRIM(VendorName)), N''),
    LTRIM(RTRIM(TourName));");

            cmd.CommandText = sql.ToString();
            return await ReadSourceRowsAsync(cmd, hasVendorColumn: true, ct);
        }

        private async Task<List<SourceRow>> LoadEmailHistorySourceRowsAsync(SqlConnection conn, TourTreeCoverageQaQuery query, CancellationToken ct)
        {
            var sql = new StringBuilder(@"
SELECT
    NULLIF(LTRIM(RTRIM(VendorName)), N'') AS VendorName,
    LTRIM(RTRIM(TourName)) AS RawTourName,
    COUNT(1) AS RecordCount
FROM dbo.AutomaticGmail_ProcessedEmails
WHERE NULLIF(LTRIM(RTRIM(TourName)), N'') IS NOT NULL");

            using var cmd = new SqlCommand();
            cmd.Connection = conn;

            if (!string.IsNullOrWhiteSpace(query.Vendor))
            {
                sql.Append(" AND VendorName = @Vendor");
                cmd.Parameters.Add(new SqlParameter("@Vendor", SqlDbType.NVarChar, 255) { Value = query.Vendor! });
            }

            sql.Append(@"
GROUP BY
    NULLIF(LTRIM(RTRIM(VendorName)), N''),
    LTRIM(RTRIM(TourName));");

            cmd.CommandText = sql.ToString();
            return await ReadSourceRowsAsync(cmd, hasVendorColumn: true, ct);
        }

        private async Task<List<SourceRow>> LoadActiveMappingSourceRowsAsync(SqlConnection conn, TourTreeCoverageQaQuery query, CancellationToken ct)
        {
            var sql = new StringBuilder(@"
SELECT
    NULLIF(LTRIM(RTRIM(VendorName)), N'') AS VendorName,
    LTRIM(RTRIM(IncomingTourName)) AS RawTourName,
    COUNT(1) AS RecordCount
FROM dbo.TourNameMappings
WHERE IsActive = 1
  AND NULLIF(LTRIM(RTRIM(IncomingTourName)), N'') IS NOT NULL");

            using var cmd = new SqlCommand();
            cmd.Connection = conn;

            if (!string.IsNullOrWhiteSpace(query.Vendor))
            {
                sql.Append(" AND VendorName = @Vendor");
                cmd.Parameters.Add(new SqlParameter("@Vendor", SqlDbType.NVarChar, 255) { Value = query.Vendor! });
            }

            sql.Append(@"
GROUP BY
    NULLIF(LTRIM(RTRIM(VendorName)), N''),
    LTRIM(RTRIM(IncomingTourName));");

            cmd.CommandText = sql.ToString();
            return await ReadSourceRowsAsync(cmd, hasVendorColumn: true, ct);
        }

        private async Task<List<SourceRow>> LoadActiveTourSourceRowsAsync(SqlConnection conn, CancellationToken ct)
        {
            const string sql = @"
SELECT RawTourName, SUM(RecordCount) AS RecordCount
FROM
(
    SELECT LTRIM(RTRIM(TourName)) AS RawTourName, COUNT(1) AS RecordCount
    FROM dbo.Tours
    WHERE IsActive = 1 AND NULLIF(LTRIM(RTRIM(TourName)), N'') IS NOT NULL
    GROUP BY LTRIM(RTRIM(TourName))

    UNION ALL

    SELECT LTRIM(RTRIM(MasterTourName)) AS RawTourName, COUNT(1) AS RecordCount
    FROM dbo.Tours
    WHERE IsActive = 1 AND NULLIF(LTRIM(RTRIM(MasterTourName)), N'') IS NOT NULL
    GROUP BY LTRIM(RTRIM(MasterTourName))
) x
GROUP BY RawTourName;";

            using var cmd = new SqlCommand(sql, conn);
            return await ReadSourceRowsAsync(cmd, hasVendorColumn: false, ct);
        }

        private async Task<List<SourceRow>> LoadQaInputSourceRowsAsync(SqlConnection conn, TourTreeCoverageQaQuery query, CancellationToken ct)
        {
            var rows = new List<SourceRow>();
            rows.AddRange(await LoadQaPipeRowsAsync(conn, query, ct));
            rows.AddRange(await LoadQaManagerRowsAsync(conn, ct));
            return rows;
        }

        private static async Task<List<SourceRow>> LoadQaPipeRowsAsync(SqlConnection conn, TourTreeCoverageQaQuery query, CancellationToken ct)
        {
            var sql = new StringBuilder(@"
IF OBJECT_ID(N'dbo.TourSetupQaPipeListInput', N'U') IS NOT NULL
BEGIN
    SELECT
        NULLIF(LTRIM(RTRIM(VendorName)), N'') AS VendorName,
        LTRIM(RTRIM(RawTourName)) AS RawTourName,
        SUM(COALESCE(RecordCount, 1)) AS RecordCount
    FROM dbo.TourSetupQaPipeListInput
    WHERE NULLIF(LTRIM(RTRIM(RawTourName)), N'') IS NOT NULL");

            using var cmd = new SqlCommand();
            cmd.Connection = conn;

            if (!string.IsNullOrWhiteSpace(query.Vendor))
            {
                sql.Append(" AND VendorName = @Vendor");
                cmd.Parameters.Add(new SqlParameter("@Vendor", SqlDbType.NVarChar, 255) { Value = query.Vendor! });
            }

            sql.Append(@"
    GROUP BY
        NULLIF(LTRIM(RTRIM(VendorName)), N''),
        LTRIM(RTRIM(RawTourName));
END");

            cmd.CommandText = sql.ToString();
            return await ReadSourceRowsAsync(cmd, hasVendorColumn: true, ct);
        }

        private static async Task<List<SourceRow>> LoadQaManagerRowsAsync(SqlConnection conn, CancellationToken ct)
        {
            const string sql = @"
IF OBJECT_ID(N'dbo.TourSetupQaManagerListInput', N'U') IS NOT NULL
BEGIN
    SELECT
        LTRIM(RTRIM(CanonicalTourName)) AS RawTourName,
        COUNT(1) AS RecordCount
    FROM dbo.TourSetupQaManagerListInput
    WHERE NULLIF(LTRIM(RTRIM(CanonicalTourName)), N'') IS NOT NULL
    GROUP BY LTRIM(RTRIM(CanonicalTourName));
END";

            using var cmd = new SqlCommand(sql, conn);
            return await ReadSourceRowsAsync(cmd, hasVendorColumn: false, ct);
        }

        private static async Task<List<SourceRow>> ReadSourceRowsAsync(SqlCommand cmd, bool hasVendorColumn, CancellationToken ct)
        {
            var rows = new List<SourceRow>();
            using var reader = await cmd.ExecuteReaderAsync(ct);
            if (reader.FieldCount <= 0)
            {
                return rows;
            }

            var vendorOrdinal = hasVendorColumn ? TryGetOrdinal(reader, "VendorName") : -1;
            var rawOrdinal = TryGetOrdinal(reader, "RawTourName");
            var countOrdinal = TryGetOrdinal(reader, "RecordCount");

            if (rawOrdinal < 0 || countOrdinal < 0)
            {
                return rows;
            }

            while (await reader.ReadAsync(ct))
            {
                var rawTourName = reader.IsDBNull(rawOrdinal) ? null : reader.GetString(rawOrdinal);
                var cleanRaw = TrimOrNull(rawTourName);
                if (cleanRaw is null)
                {
                    continue;
                }

                var count = reader.IsDBNull(countOrdinal) ? 0 : reader.GetInt32(countOrdinal);
                if (count <= 0)
                {
                    count = 1;
                }

                string? vendor = null;
                if (vendorOrdinal >= 0 && !reader.IsDBNull(vendorOrdinal))
                {
                    vendor = TrimOrNull(reader.GetString(vendorOrdinal));
                }

                rows.Add(new SourceRow
                {
                    VendorName = vendor,
                    RawTourName = cleanRaw,
                    RecordCount = count
                });
            }

            return rows;
        }

        private ManagerNotesParseResult ParseManagerNotesSourceRows()
        {
            var result = new ManagerNotesParseResult();
            var path = Path.Combine(_environment.ContentRootPath, "Email_Docs", "3_1tours from emails and db and vendors.txt");
            if (!File.Exists(path))
            {
                return result;
            }

            var inVendorSection = false;
            var inDurationSection = false;

            foreach (var rawLine in File.ReadLines(path))
            {
                var line = TrimOrNull(rawLine);
                if (line is null)
                {
                    continue;
                }

                if (line.StartsWith("VendorName|TourName|RecordCount", StringComparison.OrdinalIgnoreCase))
                {
                    inVendorSection = true;
                    inDurationSection = false;
                    continue;
                }

                if (line.StartsWith("tourname|duration|starttime|meeting_time", StringComparison.OrdinalIgnoreCase))
                {
                    inVendorSection = false;
                    inDurationSection = true;
                    continue;
                }

                if (line.StartsWith("--", StringComparison.OrdinalIgnoreCase) || line.StartsWith("-----", StringComparison.OrdinalIgnoreCase))
                {
                    inVendorSection = false;
                    inDurationSection = false;
                    continue;
                }

                if (inVendorSection)
                {
                    var parts = line.Split('|', StringSplitOptions.None);
                    if (parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[1]))
                    {
                        _ = int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count);
                        if (count <= 0)
                        {
                            count = 1;
                        }

                        result.Rows.Add(new SourceRow
                        {
                            VendorName = TrimOrNull(parts[0]),
                            RawTourName = parts[1].Trim(),
                            RecordCount = count
                        });
                    }

                    continue;
                }

                if (inDurationSection)
                {
                    var parts = line.Split('|', StringSplitOptions.None);
                    if (parts.Length >= 4 && !string.IsNullOrWhiteSpace(parts[0]))
                    {
                        var hintKey = TourNameNormalization.NormalizeTourNameForGrouping(string.Empty, parts[0]);
                        if (!string.IsNullOrWhiteSpace(hintKey))
                        {
                            result.Hints[hintKey] = (
                                Duration: TrimOrNull(parts[1]),
                                Times: TrimOrNull(parts[3]));
                        }
                    }
                }
            }

            return result;
        }

        private static async Task<List<MappingDiagnosticRow>> LoadAllMappingDiagnosticsAsync(SqlConnection conn, CancellationToken ct)
        {
            const string sql = @"
SELECT
    m.Id,
    m.TourId,
    m.IncomingTourName,
    m.VendorName,
    m.IsActive,
    m.UpdatedAt,
    t.Id AS JoinedTourId,
    t.IsActive AS JoinedTourIsActive,
    t.MasterTourName
FROM dbo.TourNameMappings m
LEFT JOIN dbo.Tours t ON t.Id = m.TourId
WHERE NULLIF(LTRIM(RTRIM(m.IncomingTourName)), N'') IS NOT NULL;";

            var rows = new List<MappingDiagnosticRow>();
            using var cmd = new SqlCommand(sql, conn);
            using var reader = await cmd.ExecuteReaderAsync(ct);

            var idOrdinal = reader.GetOrdinal("Id");
            var incomingOrdinal = reader.GetOrdinal("IncomingTourName");
            var vendorOrdinal = reader.GetOrdinal("VendorName");
            var isActiveOrdinal = reader.GetOrdinal("IsActive");
            var updatedOrdinal = reader.GetOrdinal("UpdatedAt");
            var joinedTourIdOrdinal = reader.GetOrdinal("JoinedTourId");
            var joinedTourIsActiveOrdinal = reader.GetOrdinal("JoinedTourIsActive");
            var joinedMasterOrdinal = reader.GetOrdinal("MasterTourName");

            while (await reader.ReadAsync(ct))
            {
                rows.Add(new MappingDiagnosticRow
                {
                    Id = reader.GetInt32(idOrdinal),
                    IncomingTourName = reader.GetString(incomingOrdinal),
                    VendorName = reader.IsDBNull(vendorOrdinal) ? null : reader.GetString(vendorOrdinal),
                    IsActive = reader.GetBoolean(isActiveOrdinal),
                    UpdatedAt = reader.IsDBNull(updatedOrdinal) ? DateTime.MinValue : reader.GetDateTime(updatedOrdinal),
                    JoinedTourId = reader.IsDBNull(joinedTourIdOrdinal) ? null : reader.GetInt32(joinedTourIdOrdinal),
                    JoinedTourIsActive = reader.IsDBNull(joinedTourIsActiveOrdinal) ? null : reader.GetBoolean(joinedTourIsActiveOrdinal),
                    JoinedMasterTourName = reader.IsDBNull(joinedMasterOrdinal) ? null : reader.GetString(joinedMasterOrdinal)
                });
            }

            return rows;
        }

        private static async Task<Dictionary<string, List<ActiveTourNameMatch>>> LoadActiveTourNameLookupAsync(SqlConnection conn, CancellationToken ct)
        {
            const string sql = @"
SELECT
    Id,
    TourName,
    MasterTourName,
    MasterTourNameDesktop,
    MasterTourNameMobile
FROM dbo.Tours
WHERE IsActive = 1;";

            var lookup = new Dictionary<string, List<ActiveTourNameMatch>>(StringComparer.OrdinalIgnoreCase);
            using var cmd = new SqlCommand(sql, conn);
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var tourId = reader.GetInt32(reader.GetOrdinal("Id"));
                var master = reader.GetString(reader.GetOrdinal("MasterTourName"));
                var names = new[]
                {
                    master,
                    ReadNullableString(reader, "TourName"),
                    ReadNullableString(reader, "MasterTourNameDesktop"),
                    ReadNullableString(reader, "MasterTourNameMobile")
                };

                foreach (var name in names.Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    var normalized = TourNameNormalization.NormalizeTourNameForGrouping(string.Empty, name!);
                    if (string.IsNullOrWhiteSpace(normalized))
                    {
                        continue;
                    }

                    if (!lookup.TryGetValue(normalized, out var list))
                    {
                        list = new List<ActiveTourNameMatch>();
                        lookup[normalized] = list;
                    }

                    if (!list.Any(x => x.TourId == tourId))
                    {
                        list.Add(new ActiveTourNameMatch
                        {
                            TourId = tourId,
                            MasterTourName = master
                        });
                    }
                }
            }

            foreach (var key in lookup.Keys.ToList())
            {
                lookup[key] = lookup[key]
                    .OrderBy(x => x.TourId)
                    .ToList();
            }

            return lookup;
        }

        private static List<TourTreeCoverageQaRow> BuildRows(
            List<SourceRow> currentWindowRows,
            List<SourceRow> emailHistoryRows,
            List<SourceRow> activeMappingRows,
            List<SourceRow> activeTourRows,
            List<SourceRow> qaInputRows,
            ManagerNotesParseResult managerParse,
            List<MappingDiagnosticRow> mappingDiagnostics,
            Dictionary<string, List<ActiveTourNameMatch>> activeTourNameLookup)
        {
            var buckets = new Dictionary<string, CoverageBucket>(StringComparer.OrdinalIgnoreCase);

            AddSourceRows(buckets, currentWindowRows, SourceEmailsCurrentWindow);
            AddSourceRows(buckets, emailHistoryRows, SourceEmailsHistory);
            AddSourceRows(buckets, activeMappingRows, SourceActiveMappings);
            AddSourceRows(buckets, activeTourRows, SourceActiveTours);
            AddSourceRows(buckets, qaInputRows, SourceQaInputTables);
            AddSourceRows(buckets, managerParse.Rows, SourceManagerNotes);

            var mappingLookup = mappingDiagnostics
                .GroupBy(x => NormalizeLookupName(x.IncomingTourName), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var rows = new List<TourTreeCoverageQaRow>(buckets.Count);

            foreach (var bucket in buckets.Values)
            {
                if (managerParse.Hints.TryGetValue(bucket.NormalizedKey, out var hint))
                {
                    bucket.HintDuration = hint.Duration;
                    bucket.HintTimes = hint.Times;
                }

                ClassifyBucket(bucket, mappingLookup, activeTourNameLookup);

                var row = new TourTreeCoverageQaRow
                {
                    RowKey = bucket.RowKey,
                    RawTourName = bucket.RawTourName,
                    NormalizedKey = bucket.NormalizedKey,
                    Vendors = bucket.Vendors.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                    SourceEmailsCurrentWindow = bucket.SourceEmailsCurrentWindow,
                    SourceEmailsHistory = bucket.SourceEmailsHistory,
                    SourceActiveMappings = bucket.SourceActiveMappings,
                    SourceActiveTours = bucket.SourceActiveTours,
                    SourceQaInputTables = bucket.SourceQaInputTables,
                    SourceManagerNotes = bucket.SourceManagerNotes,
                    CurrentWindowCount = bucket.CurrentWindowCount,
                    EmailHistoryCount = bucket.EmailHistoryCount,
                    ActiveMappingCount = bucket.ActiveMappingCount,
                    ActiveToursCount = bucket.ActiveToursCount,
                    QaInputCount = bucket.QaInputCount,
                    ManagerNotesCount = bucket.ManagerNotesCount,
                    IsInCurrentTreeInputWindow = bucket.IsInCurrentTreeInputWindow,
                    HasActiveAliasMapping = bucket.HasActiveAliasMapping,
                    HasAnyAliasMapping = bucket.HasAnyAliasMapping,
                    ResolvedTourActive = bucket.ResolvedTourActive,
                    HasBrokenActiveMapping = bucket.HasBrokenActiveMapping,
                    HasActiveTourNameMatch = bucket.HasActiveTourNameMatch,
                    ResolvedTourId = bucket.ResolvedTourId,
                    MasterTourName = bucket.MasterTourName,
                    SuggestedActiveTourId = bucket.SuggestedActiveTourId,
                    SuggestedActiveTourName = bucket.SuggestedActiveTourName,
                    RenderMode = bucket.RenderMode,
                    CoverageStatus = bucket.CoverageStatus,
                    Notes = bucket.Notes
                };

                row.SourcesPresent = BuildSourceList(row);
                rows.Add(row);
            }

            return rows;
        }

        private static void AddSourceRows(Dictionary<string, CoverageBucket> buckets, IEnumerable<SourceRow> rows, string sourceKey)
        {
            foreach (var source in rows)
            {
                var rawName = TrimOrNull(source.RawTourName);
                if (rawName is null)
                {
                    continue;
                }

                var vendor = TrimOrNull(source.VendorName);
                var rowKey = BuildVariantRowKey(vendor, rawName);
                if (!buckets.TryGetValue(rowKey, out var bucket))
                {
                    bucket = new CoverageBucket
                    {
                        RowKey = rowKey,
                        VendorName = vendor,
                        RawTourName = rawName,
                        NormalizedKey = TourNameNormalization.NormalizeTourNameForGrouping(vendor ?? string.Empty, rawName)
                    };
                    buckets[rowKey] = bucket;
                }

                if (!string.IsNullOrWhiteSpace(vendor))
                {
                    bucket.Vendors.Add(vendor);
                }

                var count = source.RecordCount > 0 ? source.RecordCount : 1;

                switch (sourceKey)
                {
                    case SourceEmailsCurrentWindow:
                        bucket.SourceEmailsCurrentWindow = true;
                        bucket.CurrentWindowCount += count;
                        break;
                    case SourceEmailsHistory:
                        bucket.SourceEmailsHistory = true;
                        bucket.EmailHistoryCount += count;
                        break;
                    case SourceActiveMappings:
                        bucket.SourceActiveMappings = true;
                        bucket.ActiveMappingCount += count;
                        break;
                    case SourceActiveTours:
                        bucket.SourceActiveTours = true;
                        bucket.ActiveToursCount += count;
                        break;
                    case SourceQaInputTables:
                        bucket.SourceQaInputTables = true;
                        bucket.QaInputCount += count;
                        break;
                    case SourceManagerNotes:
                        bucket.SourceManagerNotes = true;
                        bucket.ManagerNotesCount += count;
                        break;
                }
            }
        }

        private static void ClassifyBucket(
            CoverageBucket bucket,
            Dictionary<string, List<MappingDiagnosticRow>> mappingLookup,
            Dictionary<string, List<ActiveTourNameMatch>> activeTourNameLookup)
        {
            bucket.IsInCurrentTreeInputWindow = bucket.SourceEmailsCurrentWindow && bucket.CurrentWindowCount > 0;

            var mappingCandidates = GetMappingCandidates(bucket.RawTourName, mappingLookup);
            bucket.HasAnyAliasMapping = mappingCandidates.Count > 0;
            bucket.HasBrokenActiveMapping = mappingCandidates.Any(m => m.IsActive && (!m.JoinedTourId.HasValue || m.JoinedTourIsActive != true));

            var incomingVendorKey = NormalizeVendorKey(bucket.VendorName);
            var bestActive = mappingCandidates
                .OrderBy(m => GetVendorMatchPriority(incomingVendorKey, NormalizeVendorKey(m.VendorName)))
                .ThenByDescending(m => m.UpdatedAt)
                .ThenByDescending(m => m.Id)
                .FirstOrDefault(m => m.IsActive && m.JoinedTourId.HasValue && m.JoinedTourIsActive == true);

            if (bestActive is not null)
            {
                bucket.HasActiveAliasMapping = true;
                bucket.ResolvedTourActive = true;
                bucket.ResolvedTourId = bestActive.JoinedTourId;
                bucket.MasterTourName = bestActive.JoinedMasterTourName;
            }

            if (activeTourNameLookup.TryGetValue(bucket.NormalizedKey, out var activeTourMatches) && activeTourMatches.Count > 0)
            {
                bucket.HasActiveTourNameMatch = true;
                var preferred = activeTourMatches[0];
                bucket.SuggestedActiveTourId = preferred.TourId;
                bucket.SuggestedActiveTourName = preferred.MasterTourName;
            }

            if (bucket.IsInCurrentTreeInputWindow)
            {
                bucket.RenderMode = bucket.HasActiveAliasMapping ? "Mapped" : "Fallback";
            }
            else
            {
                bucket.RenderMode = "NotCurrentInput";
            }

            if (bucket.HasActiveAliasMapping)
            {
                bucket.CoverageStatus = "OK";
            }
            else if (bucket.HasBrokenActiveMapping)
            {
                bucket.CoverageStatus = "Mapping Broken (Inactive/Missing Tour)";
            }
            else if (bucket.IsInCurrentTreeInputWindow)
            {
                bucket.CoverageStatus = bucket.HasActiveTourNameMatch ? "Needs Alias" : "Needs Active Tour";
            }
            else
            {
                bucket.CoverageStatus = "Reference Only";
            }

            var notes = new List<string>();
            if (bucket.HasActiveAliasMapping)
            {
                notes.Add("Active alias mapping resolves to an active tour.");
            }
            else if (bucket.HasBrokenActiveMapping)
            {
                notes.Add("At least one active alias mapping points to an inactive or missing tour.");
            }
            else if (bucket.HasAnyAliasMapping)
            {
                notes.Add("Alias mapping exists, but no active alias currently resolves this variant.");
            }
            else
            {
                notes.Add("No alias mapping found.");
            }

            if (!bucket.IsInCurrentTreeInputWindow)
            {
                notes.Add("Not in current tree-input window.");
            }

            if (bucket.HasActiveTourNameMatch && !bucket.HasActiveAliasMapping && bucket.SuggestedActiveTourId.HasValue)
            {
                notes.Add($"Active tour name exists: {bucket.SuggestedActiveTourName} (ID {bucket.SuggestedActiveTourId.Value}).");
            }

            if (!string.IsNullOrWhiteSpace(bucket.HintDuration))
            {
                notes.Add($"Manager hint duration: {bucket.HintDuration}.");
            }

            if (!string.IsNullOrWhiteSpace(bucket.HintTimes))
            {
                notes.Add($"Manager hint times: {bucket.HintTimes}.");
            }

            bucket.Notes = string.Join(" ", notes);
        }

        private static List<MappingDiagnosticRow> GetMappingCandidates(string rawTourName, Dictionary<string, List<MappingDiagnosticRow>> mappingLookup)
        {
            var candidates = BuildLookupCandidates(rawTourName);
            var results = new List<MappingDiagnosticRow>();
            var seenIds = new HashSet<int>();

            foreach (var candidate in candidates)
            {
                if (!mappingLookup.TryGetValue(candidate, out var mapped))
                {
                    continue;
                }

                foreach (var row in mapped)
                {
                    if (seenIds.Add(row.Id))
                    {
                        results.Add(row);
                    }
                }
            }

            return results;
        }

        private static List<TourTreeCoverageQaRow> ApplyRowFilters(List<TourTreeCoverageQaRow> rows, TourTreeCoverageQaQuery query)
        {
            var search = TrimOrNull(query.SearchText);
            var vendor = TrimOrNull(query.Vendor);

            return rows
                .Where(row => IsSourceIncluded(row, query))
                .Where(row => vendor is null || row.Vendors.Any(v => ContainsIgnoreCase(v, vendor)))
                .Where(row =>
                    search is null
                    || ContainsIgnoreCase(row.RawTourName, search)
                    || ContainsIgnoreCase(row.NormalizedKey, search)
                    || ContainsIgnoreCase(row.MasterTourName, search)
                    || ContainsIgnoreCase(row.SuggestedActiveTourName, search)
                    || ContainsIgnoreCase(row.Notes, search)
                    || row.Vendors.Any(v => ContainsIgnoreCase(v, search)))
                .OrderBy(row => CoverageStatusSort(row.CoverageStatus))
                .ThenBy(row => RenderModeSort(row.RenderMode))
                .ThenByDescending(row => row.CurrentWindowCount)
                .ThenByDescending(row => row.EmailHistoryCount)
                .ThenBy(row => row.RawTourName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.Vendors.FirstOrDefault() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsSourceIncluded(TourTreeCoverageQaRow row, TourTreeCoverageQaQuery query)
        {
            return (query.IncludeEmailsCurrentWindow && row.SourceEmailsCurrentWindow)
                   || (query.IncludeEmailsHistory && row.SourceEmailsHistory)
                   || (query.IncludeActiveMappings && row.SourceActiveMappings)
                   || (query.IncludeActiveTours && row.SourceActiveTours)
                   || (query.IncludeQaInputTables && row.SourceQaInputTables)
                   || (query.IncludeManagerNotes && row.SourceManagerNotes);
        }

        private static TourTreeCoverageQaSummary BuildSummary(List<TourTreeCoverageQaRow> rows)
        {
            return new TourTreeCoverageQaSummary
            {
                TotalUniqueVariants = rows.Count,
                MappedCount = rows.Count(x => x.HasActiveAliasMapping),
                FallbackCount = rows.Count(x => string.Equals(x.RenderMode, "Fallback", StringComparison.OrdinalIgnoreCase)),
                MissingActiveTourCount = rows.Count(x => string.Equals(x.CoverageStatus, "Needs Active Tour", StringComparison.OrdinalIgnoreCase)),
                MappingBrokenCount = rows.Count(x => string.Equals(x.CoverageStatus, "Mapping Broken (Inactive/Missing Tour)", StringComparison.OrdinalIgnoreCase)),
                NotCurrentInputCount = rows.Count(x => string.Equals(x.RenderMode, "NotCurrentInput", StringComparison.OrdinalIgnoreCase)),
                NeedsAliasCount = rows.Count(x => string.Equals(x.CoverageStatus, "Needs Alias", StringComparison.OrdinalIgnoreCase)),
                ReferenceOnlyCount = rows.Count(x => string.Equals(x.CoverageStatus, "Reference Only", StringComparison.OrdinalIgnoreCase))
            };
        }

        private static List<TourTreeCoverageQaSourceBreakdownItem> BuildSourceBreakdown(List<TourTreeCoverageQaRow> rows, TourTreeCoverageQaQuery query)
        {
            var definitions = new List<(string Key, string Label, bool Included, Func<TourTreeCoverageQaRow, bool> IsPresent)>
            {
                (SourceEmailsCurrentWindow, "Emails current window", query.IncludeEmailsCurrentWindow, row => row.SourceEmailsCurrentWindow),
                (SourceEmailsHistory, "Emails history", query.IncludeEmailsHistory, row => row.SourceEmailsHistory),
                (SourceActiveMappings, "Active mappings", query.IncludeActiveMappings, row => row.SourceActiveMappings),
                (SourceActiveTours, "Active tours", query.IncludeActiveTours, row => row.SourceActiveTours),
                (SourceQaInputTables, "QA input tables", query.IncludeQaInputTables, row => row.SourceQaInputTables),
                (SourceManagerNotes, "Manager notes", query.IncludeManagerNotes, row => row.SourceManagerNotes)
            };

            var items = new List<TourTreeCoverageQaSourceBreakdownItem>(definitions.Count);
            foreach (var source in definitions)
            {
                var subset = rows.Where(source.IsPresent).ToList();
                items.Add(new TourTreeCoverageQaSourceBreakdownItem
                {
                    SourceKey = source.Key,
                    SourceLabel = source.Label,
                    Included = source.Included,
                    VariantCount = subset.Count,
                    MappedCount = subset.Count(x => x.HasActiveAliasMapping),
                    FallbackCount = subset.Count(x => string.Equals(x.RenderMode, "Fallback", StringComparison.OrdinalIgnoreCase)),
                    NotCurrentInputCount = subset.Count(x => string.Equals(x.RenderMode, "NotCurrentInput", StringComparison.OrdinalIgnoreCase)),
                    MappingBrokenCount = subset.Count(x => string.Equals(x.CoverageStatus, "Mapping Broken (Inactive/Missing Tour)", StringComparison.OrdinalIgnoreCase)),
                    NeedsAliasCount = subset.Count(x => string.Equals(x.CoverageStatus, "Needs Alias", StringComparison.OrdinalIgnoreCase)),
                    NeedsActiveTourCount = subset.Count(x => string.Equals(x.CoverageStatus, "Needs Active Tour", StringComparison.OrdinalIgnoreCase))
                });
            }

            return items;
        }

        private static List<TourTreeCoverageQaGroup> BuildCanonicalGroups(List<TourTreeCoverageQaRow> rows)
        {
            return rows
                .GroupBy(row => row.ResolvedTourId.HasValue ? $"tour:{row.ResolvedTourId.Value}" : $"name:{row.NormalizedKey}", StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var top = group.First();
                    var label = group
                        .Where(x => !string.IsNullOrWhiteSpace(x.MasterTourName))
                        .Select(x => x.MasterTourName)
                        .FirstOrDefault()
                        ?? (!string.IsNullOrWhiteSpace(top.SuggestedActiveTourName) ? top.SuggestedActiveTourName : ToDisplayLabel(top.NormalizedKey, top.RawTourName));

                    return BuildGroup("Canonical", group.Key, label ?? top.RawTourName, group.ToList());
                })
                .OrderBy(group => GroupStatusSort(group))
                .ThenByDescending(group => group.RowCount)
                .ThenBy(group => group.GroupLabel, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<TourTreeCoverageQaGroup> BuildNormalizedGroups(List<TourTreeCoverageQaRow> rows)
        {
            return rows
                .GroupBy(row => row.NormalizedKey, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var top = group.First();
                    var label = ToDisplayLabel(group.Key, top.RawTourName);
                    return BuildGroup("Normalized", $"normalized:{group.Key}", label, group.ToList());
                })
                .OrderBy(group => GroupStatusSort(group))
                .ThenByDescending(group => group.RowCount)
                .ThenBy(group => group.GroupLabel, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static TourTreeCoverageQaGroup BuildGroup(string groupType, string groupKey, string groupLabel, List<TourTreeCoverageQaRow> rows)
        {
            return new TourTreeCoverageQaGroup
            {
                GroupType = groupType,
                GroupKey = groupKey,
                GroupLabel = groupLabel,
                RowCount = rows.Count,
                MappedCount = rows.Count(x => x.HasActiveAliasMapping),
                FallbackCount = rows.Count(x => string.Equals(x.RenderMode, "Fallback", StringComparison.OrdinalIgnoreCase)),
                NotCurrentInputCount = rows.Count(x => string.Equals(x.RenderMode, "NotCurrentInput", StringComparison.OrdinalIgnoreCase)),
                MappingBrokenCount = rows.Count(x => string.Equals(x.CoverageStatus, "Mapping Broken (Inactive/Missing Tour)", StringComparison.OrdinalIgnoreCase)),
                NeedsAliasCount = rows.Count(x => string.Equals(x.CoverageStatus, "Needs Alias", StringComparison.OrdinalIgnoreCase)),
                NeedsActiveTourCount = rows.Count(x => string.Equals(x.CoverageStatus, "Needs Active Tour", StringComparison.OrdinalIgnoreCase)),
                ReferenceOnlyCount = rows.Count(x => string.Equals(x.CoverageStatus, "Reference Only", StringComparison.OrdinalIgnoreCase)),
                Variants = rows
                    .OrderBy(x => CoverageStatusSort(x.CoverageStatus))
                    .ThenBy(x => RenderModeSort(x.RenderMode))
                    .ThenByDescending(x => x.CurrentWindowCount)
                    .ThenBy(x => x.RawTourName, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new TourTreeCoverageQaGroupVariant
                    {
                        RowKey = x.RowKey,
                        RawTourName = x.RawTourName,
                        NormalizedKey = x.NormalizedKey,
                        Vendors = x.Vendors,
                        RenderMode = x.RenderMode,
                        CoverageStatus = x.CoverageStatus,
                        ResolvedTourId = x.ResolvedTourId,
                        MasterTourName = x.MasterTourName,
                        Notes = x.Notes
                    })
                    .ToList()
            };
        }

        private static int GroupStatusSort(TourTreeCoverageQaGroup group)
        {
            if (group.MappingBrokenCount > 0)
            {
                return 0;
            }

            if (group.NeedsActiveTourCount > 0)
            {
                return 1;
            }

            if (group.NeedsAliasCount > 0)
            {
                return 2;
            }

            if (group.FallbackCount > 0)
            {
                return 3;
            }

            if (group.NotCurrentInputCount > 0)
            {
                return 4;
            }

            return 9;
        }

        private static List<string> BuildSourceList(TourTreeCoverageQaRow row)
        {
            var list = new List<string>(6);
            if (row.SourceEmailsCurrentWindow) list.Add("Emails current window");
            if (row.SourceEmailsHistory) list.Add("Emails history");
            if (row.SourceActiveMappings) list.Add("Active mappings");
            if (row.SourceActiveTours) list.Add("Active tours");
            if (row.SourceQaInputTables) list.Add("QA input tables");
            if (row.SourceManagerNotes) list.Add("Manager notes");
            return list;
        }

        private static List<string> BuildLookupCandidates(string incomingTourName)
        {
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddNameVariant(candidates, incomingTourName);

            var trimmed = incomingTourName?.Trim();
            AddNameVariant(candidates, trimmed);

            var decoded = WebUtility.HtmlDecode(trimmed ?? string.Empty);
            AddNameVariant(candidates, decoded);

            var compactWhitespace = NormalizeWhitespace(decoded);
            AddNameVariant(candidates, compactWhitespace);

            return candidates
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.ToLowerInvariant())
                .ToList();
        }

        private static void AddNameVariant(HashSet<string> candidates, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var trimmed = value.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return;
            }

            candidates.Add(trimmed);

            if (trimmed.Contains("&amp;", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(trimmed.Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase));
            }

            if (trimmed.Contains('&'))
            {
                candidates.Add(trimmed.Replace("&", "&amp;", StringComparison.Ordinal));
            }
        }

        private static string NormalizeWhitespace(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var prepared = value
                .Replace('\t', ' ')
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Replace('\u00A0', ' ');

            return MultiWhitespaceRegex.Replace(prepared, " ").Trim();
        }

        private static string NormalizeVendorKey(string? vendorName)
        {
            if (string.IsNullOrWhiteSpace(vendorName))
            {
                return string.Empty;
            }

            var compact = new string(vendorName
                .Trim()
                .ToUpperInvariant()
                .Where(char.IsLetterOrDigit)
                .ToArray());

            if (compact.Contains("VIATOR", StringComparison.Ordinal))
            {
                return "VIATOR";
            }

            if (compact.Contains("GURUWALK", StringComparison.Ordinal) || compact.StartsWith("GURU", StringComparison.Ordinal))
            {
                return "GURUWALK";
            }

            if (compact.Contains("FREETOUR", StringComparison.Ordinal))
            {
                return "FREETOUR";
            }

            if (compact.Contains("GETYOURGUIDE", StringComparison.Ordinal) || compact.Equals("GYG", StringComparison.Ordinal))
            {
                return "GETYOURGUIDE";
            }

            if (compact.Contains("AIRBNB", StringComparison.Ordinal))
            {
                return "AIRBNB";
            }

            if (compact.Contains("CIVITATIS", StringComparison.Ordinal) || compact.Contains("CIVATASIS", StringComparison.Ordinal))
            {
                return "CIVITATIS";
            }

            if (compact.Contains("CITYSHUFFLES", StringComparison.Ordinal) || compact.Contains("WEBSITE", StringComparison.Ordinal))
            {
                return "WEBSITE";
            }

            return compact;
        }

        private static int GetVendorMatchPriority(string incomingVendorKey, string mappedVendorKey)
        {
            if (!string.IsNullOrWhiteSpace(incomingVendorKey))
            {
                if (string.Equals(incomingVendorKey, mappedVendorKey, StringComparison.OrdinalIgnoreCase))
                {
                    return 0;
                }

                if (string.IsNullOrWhiteSpace(mappedVendorKey))
                {
                    return 1;
                }

                return 2;
            }

            return string.IsNullOrWhiteSpace(mappedVendorKey) ? 0 : 1;
        }

        private static int CoverageStatusSort(string? status)
        {
            return status?.ToLowerInvariant() switch
            {
                "mapping broken (inactive/missing tour)" => 0,
                "needs active tour" => 1,
                "needs alias" => 2,
                "reference only" => 3,
                "ok" => 4,
                _ => 9
            };
        }

        private static int RenderModeSort(string? renderMode)
        {
            return renderMode?.ToLowerInvariant() switch
            {
                "fallback" => 0,
                "mapped" => 1,
                "notcurrentinput" => 2,
                _ => 9
            };
        }

        private static string ToDisplayLabel(string? normalizedKey, string fallback)
        {
            var normalized = TrimOrNull(normalizedKey);
            if (normalized is null)
            {
                return fallback;
            }

            var label = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(normalized);
            return string.IsNullOrWhiteSpace(label) ? fallback : label;
        }

        private static int TryGetOrdinal(SqlDataReader reader, string column)
        {
            for (var i = 0; i < reader.FieldCount; i++)
            {
                if (string.Equals(reader.GetName(i), column, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string BuildVariantRowKey(string? vendorName, string rawTourName)
        {
            return $"{NormalizeLookupName(vendorName)}|{NormalizeLookupName(rawTourName)}";
        }

        private static string NormalizeLookupName(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToLowerInvariant();
        }

        private static bool ContainsIgnoreCase(string? source, string value)
        {
            return !string.IsNullOrWhiteSpace(source)
                   && source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string? ReadNullableString(SqlDataReader reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }

        private static string? TrimOrNull(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private sealed class SourceRow
        {
            public string? VendorName { get; set; }
            public string RawTourName { get; set; } = string.Empty;
            public int RecordCount { get; set; }
        }

        private sealed class ManagerNotesParseResult
        {
            public List<SourceRow> Rows { get; set; } = new();
            public Dictionary<string, (string? Duration, string? Times)> Hints { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class MappingDiagnosticRow
        {
            public int Id { get; set; }
            public string IncomingTourName { get; set; } = string.Empty;
            public string? VendorName { get; set; }
            public bool IsActive { get; set; }
            public DateTime UpdatedAt { get; set; }
            public int? JoinedTourId { get; set; }
            public bool? JoinedTourIsActive { get; set; }
            public string? JoinedMasterTourName { get; set; }
        }

        private sealed class ActiveTourNameMatch
        {
            public int TourId { get; set; }
            public string MasterTourName { get; set; } = string.Empty;
        }

        private sealed class CoverageBucket
        {
            public string RowKey { get; set; } = string.Empty;
            public string? VendorName { get; set; }
            public string RawTourName { get; set; } = string.Empty;
            public string NormalizedKey { get; set; } = string.Empty;
            public HashSet<string> Vendors { get; } = new(StringComparer.OrdinalIgnoreCase);

            public bool SourceEmailsCurrentWindow { get; set; }
            public bool SourceEmailsHistory { get; set; }
            public bool SourceActiveMappings { get; set; }
            public bool SourceActiveTours { get; set; }
            public bool SourceQaInputTables { get; set; }
            public bool SourceManagerNotes { get; set; }

            public int CurrentWindowCount { get; set; }
            public int EmailHistoryCount { get; set; }
            public int ActiveMappingCount { get; set; }
            public int ActiveToursCount { get; set; }
            public int QaInputCount { get; set; }
            public int ManagerNotesCount { get; set; }

            public bool IsInCurrentTreeInputWindow { get; set; }
            public bool HasActiveAliasMapping { get; set; }
            public bool HasAnyAliasMapping { get; set; }
            public bool ResolvedTourActive { get; set; }
            public bool HasBrokenActiveMapping { get; set; }
            public bool HasActiveTourNameMatch { get; set; }

            public int? ResolvedTourId { get; set; }
            public string? MasterTourName { get; set; }
            public int? SuggestedActiveTourId { get; set; }
            public string? SuggestedActiveTourName { get; set; }

            public string RenderMode { get; set; } = "NotCurrentInput";
            public string CoverageStatus { get; set; } = "Reference Only";
            public string Notes { get; set; } = string.Empty;
            public string? HintDuration { get; set; }
            public string? HintTimes { get; set; }
        }
    }
}
