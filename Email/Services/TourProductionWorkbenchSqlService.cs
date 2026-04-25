using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Email.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;

namespace Email.Services
{
    /// <summary>
    /// Direct SQL implementation for production workbench.
    /// </summary>
    public sealed class TourProductionWorkbenchSqlService : ITourProductionWorkbenchService
    {
        private readonly SqlConnectionFactory _connectionFactory;
        private readonly IWebHostEnvironment _environment;
        private static readonly Regex MeetingHintRegex = new(@"(?:see|meet)\s+you\s+(?:at|outside|in front of)\s+(?<place>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public TourProductionWorkbenchSqlService(SqlConnectionFactory connectionFactory, IWebHostEnvironment environment)
        {
            _connectionFactory = connectionFactory;
            _environment = environment;
        }

        public async Task<TourProductionSourceCoverageResult> GetSourceCoverageAsync(TourProductionSourceCoverageQuery? query = null, CancellationToken ct = default)
        {
            using var conn = _connectionFactory.CreateOpenConnection();
            var rows = await LoadCoverageRowsAsync(conn, ct);
            rows = ApplyCoverageFilter(rows, query);

            return new TourProductionSourceCoverageResult
            {
                TotalRows = rows.Sum(x => x.RecordCount),
                MappedRows = rows.Where(x => x.Status == "Mapped").Sum(x => x.RecordCount),
                NeedsTourRows = rows.Where(x => x.Status == "Needs Tour").Sum(x => x.RecordCount),
                NeedsMeetingPlaceRows = rows.Where(x => x.Status == "Needs Meeting Place").Sum(x => x.RecordCount),
                NeedsAliasRows = rows.Where(x => x.Status == "Needs Alias").Sum(x => x.RecordCount),
                Items = rows
                    .OrderBy(x => StatusSort(x.Status))
                    .ThenByDescending(x => x.RecordCount)
                    .ThenBy(x => x.RawTourName, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new TourProductionSourceCoverageItem
                    {
                        SourceType = x.SourceType,
                        VendorName = x.VendorName,
                        RawTourName = x.RawTourName,
                        RecordCount = x.RecordCount,
                        SuggestedMeetingPlace = x.SuggestedMeetingPlace,
                        SuggestedDuration = x.SuggestedDuration,
                        SuggestedTimes = x.SuggestedTimes,
                        TourId = x.TourId,
                        MasterTourName = x.MasterTourName,
                        Status = x.Status
                    })
                    .ToList()
            };
        }

        public async Task<List<TourProductionGroupCard>> GetGroupCardsAsync(TourProductionGroupQuery? query = null, CancellationToken ct = default)
        {
            using var conn = _connectionFactory.CreateOpenConnection();
            var rows = await LoadCoverageRowsAsync(conn, ct);
            rows = ApplyGroupFilter(rows, query);

            var cards = rows
                .GroupBy(BuildGroupKey)
                .Select(g => BuildGroupCard(g.Key, g.ToList()))
                .ToList();

            if (!string.IsNullOrWhiteSpace(query?.StatusFilter) && !string.Equals(query.StatusFilter, "All", StringComparison.OrdinalIgnoreCase))
            {
                cards = cards.Where(x => string.Equals(x.Status, query.StatusFilter, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            return cards
                .OrderBy(x => StatusSort(x.Status))
                .ThenByDescending(x => x.TotalSourceRows)
                .ThenBy(x => x.GroupLabel, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public async Task<TourProductionGroupDetail?> GetGroupDetailAsync(string groupKey, CancellationToken ct = default)
        {
            var safeGroupKey = TrimOrNull(groupKey);
            if (safeGroupKey is null)
            {
                return null;
            }

            using var conn = _connectionFactory.CreateOpenConnection();
            var rows = await LoadCoverageRowsAsync(conn, ct);
            var groupRows = rows.Where(x => string.Equals(BuildGroupKey(x), safeGroupKey, StringComparison.OrdinalIgnoreCase)).ToList();
            if (groupRows.Count == 0)
            {
                return null;
            }

            var card = BuildGroupCard(safeGroupKey, groupRows);
            var tours = await LoadToursInternalAsync(conn, includeInactive: false, search: null, ct);
            var nameTokens = groupRows.Select(x => x.RawTourName).Append(card.GroupLabel).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var detail = new TourProductionGroupDetail
            {
                GroupKey = card.GroupKey,
                GroupLabel = card.GroupLabel,
                Status = card.Status,
                TourId = card.TourId,
                MasterTourName = card.MasterTourName,
                ManualGroupKey = card.ManualGroupKey,
                Variants = groupRows
                    .OrderByDescending(x => x.RecordCount)
                    .Select(x => new TourProductionGroupVariant
                    {
                        SourceType = x.SourceType,
                        VendorName = x.VendorName,
                        RawTourName = x.RawTourName,
                        RecordCount = x.RecordCount,
                        IsMapped = x.TourId.HasValue,
                        TourId = x.TourId,
                        VariantKey = x.RowSignature,
                        ManualGroupKey = x.ManualGroupKey
                    })
                    .ToList(),
                Candidates = tours
                    .Select(t => BuildCandidate(t, nameTokens, card.TourId))
                    .Where(x => x.Score > 0m || (card.TourId.HasValue && x.TourId == card.TourId.Value))
                    .OrderByDescending(x => x.Score)
                    .ThenBy(x => x.MasterTourName, StringComparer.OrdinalIgnoreCase)
                    .Take(15)
                    .ToList()
            };

            if (card.TourId.HasValue)
            {
                detail.ScheduleTimes = await GetScheduleTimesAsync(card.TourId.Value, ct);
            }

            return detail;
        }

        public async Task<List<TourProductionMeetingPlaceItem>> GetMeetingPlaceLibraryAsync(string? search = null, CancellationToken ct = default)
        {
            using var conn = _connectionFactory.CreateOpenConnection();
            const string sql = @"
SELECT N'Tours' AS SourceType, LTRIM(RTRIM(MeetingPlace)) AS MeetingPlace, COUNT(1) AS UsageCount
FROM dbo.Tours
WHERE NULLIF(LTRIM(RTRIM(MeetingPlace)), N'') IS NOT NULL
GROUP BY LTRIM(RTRIM(MeetingPlace))
UNION ALL
SELECT N'TourSchedules' AS SourceType, LTRIM(RTRIM(MeetingPlace)) AS MeetingPlace, COUNT(1) AS UsageCount
FROM dbo.TourSchedules
WHERE NULLIF(LTRIM(RTRIM(MeetingPlace)), N'') IS NOT NULL
GROUP BY LTRIM(RTRIM(MeetingPlace));";

            var list = new List<TourProductionMeetingPlaceItem>();
            using (var cmd = new SqlCommand(sql, conn))
            using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    list.Add(new TourProductionMeetingPlaceItem
                    {
                        SourceType = reader.GetString(reader.GetOrdinal("SourceType")),
                        MeetingPlace = reader.GetString(reader.GetOrdinal("MeetingPlace")),
                        UsageCount = reader.GetInt32(reader.GetOrdinal("UsageCount"))
                    });
                }
            }

            foreach (var place in ParseManagerMeetingPlaces())
            {
                list.Add(new TourProductionMeetingPlaceItem { SourceType = "ManagerNotes", MeetingPlace = place, UsageCount = 1 });
            }

            var safeSearch = TrimOrNull(search);
            return list
                .Where(x => safeSearch is null || ContainsIgnoreCase(x.MeetingPlace, safeSearch))
                .GroupBy(x => $"{x.SourceType}|{x.MeetingPlace}", StringComparer.OrdinalIgnoreCase)
                .Select(g => new TourProductionMeetingPlaceItem
                {
                    SourceType = g.First().SourceType,
                    MeetingPlace = g.First().MeetingPlace,
                    UsageCount = g.Sum(x => x.UsageCount)
                })
                .OrderByDescending(x => x.UsageCount)
                .ThenBy(x => x.MeetingPlace, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public async Task<List<TourProductionScheduleTimeItem>> GetScheduleTimesAsync(int tourId, CancellationToken ct = default)
        {
            if (tourId <= 0)
            {
                return new List<TourProductionScheduleTimeItem>();
            }

            using var conn = _connectionFactory.CreateOpenConnection();
            const string sql = @"
SELECT Id, MeetingTime, MeetingPlace, TimeSlotsJson
FROM dbo.TourSchedules
WHERE TourId = @TourId AND IsActive = 1
ORDER BY UpdatedAt DESC, Id DESC;";

            var items = new List<TourProductionScheduleTimeItem>();
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@TourId", SqlDbType.Int) { Value = tourId });
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var scheduleId = reader.GetInt32(reader.GetOrdinal("Id"));
                var meetingTime = ReadNullableString(reader, "MeetingTime");
                var meetingPlace = ReadNullableString(reader, "MeetingPlace");
                var timeSlotsJson = ReadNullableString(reader, "TimeSlotsJson");

                var parsed = ParseScheduleSlots(timeSlotsJson, meetingTime, meetingPlace);
                if (parsed.Count == 0 && !string.IsNullOrWhiteSpace(meetingTime))
                {
                    parsed.Add(new TourProductionScheduleTimeItem
                    {
                        TourScheduleId = scheduleId,
                        TourTime = NormalizeTime(meetingTime) ?? meetingTime,
                        MeetingTime = NormalizeTime(meetingTime),
                        MeetingPlace = TrimOrNull(meetingPlace)
                    });
                }
                else
                {
                    foreach (var p in parsed)
                    {
                        p.TourScheduleId = scheduleId;
                    }
                }

                items.AddRange(parsed);
            }

            return items
                .Where(x => !string.IsNullOrWhiteSpace(x.TourTime))
                .GroupBy(x => $"{x.TourScheduleId}|{x.TourTime}|{x.MeetingTime}|{x.MeetingPlace}", StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(x => x.TourTime, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public async Task<List<DbTour>> GetToursAsync(bool includeInactive = false, string? search = null, CancellationToken ct = default)
        {
            using var conn = _connectionFactory.CreateOpenConnection();
            return await LoadToursInternalAsync(conn, includeInactive, search, ct);
        }

        public Task<DbTour?> CreateTourAsync(TourProductionTourUpsertInput input, CancellationToken ct = default)
        {
            return CreateTourInternalAsync(input, ct);
        }

        public Task<bool> UpdateTourAsync(int tourId, TourProductionTourUpsertInput input, CancellationToken ct = default)
        {
            return UpdateTourInternalAsync(tourId, input, ct);
        }

        public Task<bool> DeactivateTourAsync(int tourId, CancellationToken ct = default)
        {
            return DeactivateTourInternalAsync(tourId, ct);
        }

        public Task<List<TourNameMapping>> GetAliasesAsync(int tourId, string? vendorName = null, bool includeInactive = false, CancellationToken ct = default)
        {
            return GetAliasesInternalAsync(tourId, vendorName, includeInactive, ct);
        }

        public Task<int> CreateAliasAsync(TourProductionAliasCreateInput input, CancellationToken ct = default)
        {
            return CreateAliasInternalAsync(input, ct);
        }

        public Task<bool> UpdateAliasAsync(int aliasId, TourProductionAliasUpdateInput input, CancellationToken ct = default)
        {
            return UpdateAliasInternalAsync(aliasId, input, ct);
        }

        public Task<bool> ReassignAliasAsync(int aliasId, int targetTourId, CancellationToken ct = default)
        {
            return ReassignAliasInternalAsync(aliasId, targetTourId, ct);
        }

        public Task<bool> SetAliasActiveAsync(int aliasId, bool isActive, CancellationToken ct = default)
        {
            return SetAliasActiveInternalAsync(aliasId, isActive, ct);
        }

        public Task<int> ApplyGroupOverrideToGroupAsync(string groupKey, string overrideGroupKey, CancellationToken ct = default)
        {
            return ApplyGroupOverrideToGroupInternalAsync(groupKey, overrideGroupKey, ct);
        }

        public Task<bool> ApplyGroupOverrideToVariantAsync(TourProductionVariantOverrideInput input, string overrideGroupKey, CancellationToken ct = default)
        {
            return ApplyGroupOverrideToVariantInternalAsync(input, overrideGroupKey, ct);
        }

        public Task<int> ClearGroupOverridesForGroupAsync(string groupKey, CancellationToken ct = default)
        {
            return ClearGroupOverridesForGroupInternalAsync(groupKey, ct);
        }

        public Task<bool> ClearGroupOverrideForVariantAsync(TourProductionVariantOverrideInput input, CancellationToken ct = default)
        {
            return ClearGroupOverrideForVariantInternalAsync(input, ct);
        }

        private sealed class CoverageRow
        {
            public string SourceType { get; set; } = string.Empty;
            public string? VendorName { get; set; }
            public string RawTourName { get; set; } = string.Empty;
            public int RecordCount { get; set; }
            public string? SuggestedMeetingPlace { get; set; }
            public string? SuggestedDuration { get; set; }
            public string? SuggestedTimes { get; set; }
            public int? TourId { get; set; }
            public string? MasterTourName { get; set; }
            public string Status { get; set; } = "Needs Tour";
            public string NormalizedKey { get; set; } = string.Empty;
            public bool AliasExact { get; set; }
            public string RowSignature { get; set; } = string.Empty;
            public string? ManualGroupKey { get; set; }
        }

        private sealed class AliasMapRow
        {
            public int TourId { get; set; }
            public string IncomingTourName { get; set; } = string.Empty;
            public string? VendorName { get; set; }
        }

        private static List<CoverageRow> ApplyCoverageFilter(List<CoverageRow> rows, TourProductionSourceCoverageQuery? query)
        {
            if (query is null)
            {
                return rows;
            }

            var search = TrimOrNull(query.Search);
            var vendor = TrimOrNull(query.VendorName);
            var master = TrimOrNull(query.MasterTourName);
            return rows.Where(x =>
                    (search is null || ContainsIgnoreCase(x.RawTourName, search) || ContainsIgnoreCase(x.VendorName, search) || ContainsIgnoreCase(x.MasterTourName, search))
                 && (vendor is null || ContainsIgnoreCase(x.VendorName, vendor))
                 && (master is null || ContainsIgnoreCase(x.MasterTourName, master)))
                .ToList();
        }

        private static List<CoverageRow> ApplyGroupFilter(List<CoverageRow> rows, TourProductionGroupQuery? query)
        {
            if (query is null)
            {
                return rows;
            }

            var search = TrimOrNull(query.Search);
            var vendor = TrimOrNull(query.VendorName);
            return rows.Where(x =>
                    (search is null || ContainsIgnoreCase(x.RawTourName, search) || ContainsIgnoreCase(x.MasterTourName, search))
                 && (vendor is null || ContainsIgnoreCase(x.VendorName, vendor)))
                .ToList();
        }

        private async Task<List<CoverageRow>> LoadCoverageRowsAsync(SqlConnection conn, CancellationToken ct)
        {
            var tours = await LoadToursInternalAsync(conn, includeInactive: true, search: null, ct);
            var toursById = tours.ToDictionary(x => x.Id);
            var activeTours = tours.Where(x => x.IsActive).ToList();
            var aliases = await LoadAliasLookupRowsAsync(conn, ct);
            var manager = ParseManagerCoverageRows();
            var manualOverrides = await LoadGroupOverridesLookupAsync(conn, ct);

            var aliasByVendorAndName = aliases
                .GroupBy(a => $"{(TrimOrNull(a.VendorName) ?? string.Empty).ToLowerInvariant()}|{NormalizeTourKey(a.IncomingTourName)}")
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var aliasByName = aliases
                .GroupBy(a => NormalizeTourKey(a.IncomingTourName))
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var tourByName = activeTours
                .SelectMany(t => new[]
                {
                    new { Key = NormalizeTourKey(t.MasterTourName), TourId = t.Id },
                    new { Key = NormalizeTourKey(t.TourName), TourId = t.Id }
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .GroupBy(x => x.Key)
                .ToDictionary(g => g.Key, g => g.First().TourId, StringComparer.OrdinalIgnoreCase);

            var rows = new List<CoverageRow>();

            const string incomingSql = @"
SELECT NULLIF(LTRIM(RTRIM(VendorName)), N'') AS VendorName, LTRIM(RTRIM(TourName)) AS RawTourName, COUNT(1) AS RecordCount
FROM dbo.AutomaticGmail_ProcessedEmails
WHERE NULLIF(LTRIM(RTRIM(TourName)), N'') IS NOT NULL
GROUP BY NULLIF(LTRIM(RTRIM(VendorName)), N''), LTRIM(RTRIM(TourName));";
            using (var cmd = new SqlCommand(incomingSql, conn))
            using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    rows.Add(new CoverageRow
                    {
                        SourceType = "IncomingEmail",
                        VendorName = ReadNullableString(reader, "VendorName"),
                        RawTourName = reader.GetString(reader.GetOrdinal("RawTourName")),
                        RecordCount = reader.GetInt32(reader.GetOrdinal("RecordCount"))
                    });
                }
            }

            const string mappingSql = @"
SELECT m.VendorName, m.IncomingTourName AS RawTourName, CAST(1 AS int) AS RecordCount, m.TourId, t.MasterTourName, t.MeetingPlace
FROM dbo.TourNameMappings m
LEFT JOIN dbo.Tours t ON t.Id = m.TourId
WHERE m.IsActive = 1;";
            using (var cmd = new SqlCommand(mappingSql, conn))
            using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    rows.Add(new CoverageRow
                    {
                        SourceType = "Mapping",
                        VendorName = ReadNullableString(reader, "VendorName"),
                        RawTourName = reader.GetString(reader.GetOrdinal("RawTourName")),
                        RecordCount = reader.GetInt32(reader.GetOrdinal("RecordCount")),
                        TourId = ReadNullableInt(reader, "TourId"),
                        MasterTourName = ReadNullableString(reader, "MasterTourName"),
                        SuggestedMeetingPlace = ReadNullableString(reader, "MeetingPlace"),
                        AliasExact = true
                    });
                }
            }

            const string qaPipeSql = @"
IF OBJECT_ID(N'dbo.TourSetupQaPipeListInput', N'U') IS NOT NULL
BEGIN
    SELECT NULLIF(LTRIM(RTRIM(VendorName)), N'') AS VendorName, LTRIM(RTRIM(RawTourName)) AS RawTourName, SUM(RecordCount) AS RecordCount
    FROM dbo.TourSetupQaPipeListInput
    WHERE NULLIF(LTRIM(RTRIM(RawTourName)), N'') IS NOT NULL
    GROUP BY NULLIF(LTRIM(RTRIM(VendorName)), N''), LTRIM(RTRIM(RawTourName));
END";
            using (var cmd = new SqlCommand(qaPipeSql, conn))
            using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    rows.Add(new CoverageRow
                    {
                        SourceType = "QaPipeList",
                        VendorName = ReadNullableString(reader, "VendorName"),
                        RawTourName = reader.GetString(reader.GetOrdinal("RawTourName")),
                        RecordCount = reader.GetInt32(reader.GetOrdinal("RecordCount"))
                    });
                }
            }

            const string qaManagerSql = @"
IF OBJECT_ID(N'dbo.TourSetupQaManagerListInput', N'U') IS NOT NULL
BEGIN
    SELECT LTRIM(RTRIM(CanonicalTourName)) AS RawTourName, COUNT(1) AS RecordCount, MAX(NULLIF(LTRIM(RTRIM(MeetingPlaceExpected)), N'')) AS SuggestedMeetingPlace
    FROM dbo.TourSetupQaManagerListInput
    WHERE NULLIF(LTRIM(RTRIM(CanonicalTourName)), N'') IS NOT NULL
    GROUP BY LTRIM(RTRIM(CanonicalTourName));
END";
            using (var cmd = new SqlCommand(qaManagerSql, conn))
            using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    rows.Add(new CoverageRow
                    {
                        SourceType = "QaManagerList",
                        RawTourName = reader.GetString(reader.GetOrdinal("RawTourName")),
                        RecordCount = reader.GetInt32(reader.GetOrdinal("RecordCount")),
                        SuggestedMeetingPlace = ReadNullableString(reader, "SuggestedMeetingPlace")
                    });
                }
            }

            rows.AddRange(manager.Rows);

            foreach (var row in rows)
            {
                row.NormalizedKey = NormalizeTourKey(row.RawTourName);
                row.RowSignature = BuildRowSignature(row.SourceType, row.VendorName, row.RawTourName);
                if (manualOverrides.TryGetValue(row.RowSignature, out var manualKey))
                {
                    row.ManualGroupKey = manualKey;
                }

                if (manager.Hints.TryGetValue(row.NormalizedKey, out var hint))
                {
                    row.SuggestedDuration ??= hint.Duration;
                    row.SuggestedTimes ??= hint.Times;
                }

                if (!row.TourId.HasValue)
                {
                    var exactKey = $"{(TrimOrNull(row.VendorName) ?? string.Empty).ToLowerInvariant()}|{row.NormalizedKey}";
                    if (aliasByVendorAndName.TryGetValue(exactKey, out var exact))
                    {
                        row.TourId = exact.TourId;
                        row.AliasExact = true;
                    }
                    else if (aliasByName.TryGetValue(row.NormalizedKey, out var byName))
                    {
                        row.TourId = byName.TourId;
                        row.AliasExact = true;
                    }
                    else if (tourByName.TryGetValue(row.NormalizedKey, out var byTourName))
                    {
                        row.TourId = byTourName;
                    }
                }

                if (row.TourId.HasValue && toursById.TryGetValue(row.TourId.Value, out var t))
                {
                    row.MasterTourName = t.MasterTourName;
                    row.SuggestedMeetingPlace = TrimOrNull(t.MeetingPlace) ?? row.SuggestedMeetingPlace;
                }

                row.Status = row.TourId.HasValue
                    ? (IsMissingMeetingPlace(row.SuggestedMeetingPlace)
                        ? "Needs Meeting Place"
                        : (!string.Equals(row.SourceType, "Mapping", StringComparison.OrdinalIgnoreCase) && !row.AliasExact ? "Needs Alias" : "Mapped"))
                    : "Needs Tour";
            }

            return rows.Where(x => !string.IsNullOrWhiteSpace(x.RawTourName)).ToList();
        }

        private (List<CoverageRow> Rows, Dictionary<string, (string? Duration, string? Times)> Hints) ParseManagerCoverageRows()
        {
            var result = new List<CoverageRow>();
            var hints = new Dictionary<string, (string? Duration, string? Times)>(StringComparer.OrdinalIgnoreCase);
            var path = Path.Combine(_environment.ContentRootPath, "Email_Docs", "3_1tours from emails and db and vendors.txt");
            if (!File.Exists(path))
            {
                return (result, hints);
            }

            var inVendor = false;
            var inDuration = false;
            foreach (var raw in File.ReadLines(path))
            {
                var line = TrimOrNull(raw);
                if (line is null)
                {
                    continue;
                }

                if (line.StartsWith("VendorName|TourName|RecordCount", StringComparison.OrdinalIgnoreCase))
                {
                    inVendor = true;
                    inDuration = false;
                    continue;
                }

                if (line.StartsWith("tourname|duration|starttime|meeting_time", StringComparison.OrdinalIgnoreCase))
                {
                    inVendor = false;
                    inDuration = true;
                    continue;
                }

                if (line.StartsWith("--", StringComparison.OrdinalIgnoreCase) || line.StartsWith("-----", StringComparison.OrdinalIgnoreCase))
                {
                    inVendor = false;
                    inDuration = false;
                    continue;
                }

                if (inVendor)
                {
                    var parts = line.Split('|', StringSplitOptions.None);
                    if (parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[1]))
                    {
                        _ = int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count);
                        if (count <= 0)
                        {
                            count = 1;
                        }

                        result.Add(new CoverageRow
                        {
                            SourceType = "ManagerNotes",
                            VendorName = TrimOrNull(parts[0]),
                            RawTourName = parts[1].Trim(),
                            RecordCount = count
                        });
                    }

                    continue;
                }

                if (inDuration)
                {
                    var parts = line.Split('|', StringSplitOptions.None);
                    if (parts.Length >= 4 && !string.IsNullOrWhiteSpace(parts[0]))
                    {
                        hints[NormalizeTourKey(parts[0])] = (TrimOrNull(parts[1]), TrimOrNull(parts[3]));
                    }
                }
            }

            return (result, hints);
        }

        private List<string> ParseManagerMeetingPlaces()
        {
            var places = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var path = Path.Combine(_environment.ContentRootPath, "Email_Docs", "3_1tours from emails and db and vendors.txt");
            if (!File.Exists(path))
            {
                return new List<string>();
            }

            foreach (var raw in File.ReadLines(path))
            {
                var line = TrimOrNull(raw);
                if (line is null || (!line.Contains("see you", StringComparison.OrdinalIgnoreCase) && !line.Contains("meet you", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var match = MeetingHintRegex.Match(line);
                if (!match.Success)
                {
                    continue;
                }

                var place = match.Groups["place"].Value;
                place = Regex.Replace(place, @"\bat\s+\{time\}.*$", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                place = Regex.Replace(place, @"\bat\s+\d{1,2}:\d{2}\s*(am|pm)?\b.*$", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                var clean = TrimOrNull(place?.Trim().TrimEnd('.'));
                if (clean is not null)
                {
                    places.Add(clean);
                }
            }

            return places.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private async Task<List<AliasMapRow>> LoadAliasLookupRowsAsync(SqlConnection conn, CancellationToken ct)
        {
            const string sql = @"
SELECT TourId, IncomingTourName, VendorName
FROM dbo.TourNameMappings
WHERE IsActive = 1;";
            var list = new List<AliasMapRow>();
            using var cmd = new SqlCommand(sql, conn);
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(new AliasMapRow
                {
                    TourId = reader.GetInt32(reader.GetOrdinal("TourId")),
                    IncomingTourName = reader.GetString(reader.GetOrdinal("IncomingTourName")),
                    VendorName = ReadNullableString(reader, "VendorName")
                });
            }

            return list;
        }

        private async Task<List<DbTour>> LoadToursInternalAsync(SqlConnection conn, bool includeInactive, string? search, CancellationToken ct)
        {
            const string sql = @"
SELECT
    Id, TourName, TourNameAlias, VendorNames, MeetingPlace, MeetingTime, MeetingInstructions,
    DefaultGuideId, Duration, MaxCapacity, IsActive, Notes, CreatedAt, UpdatedAt, TourStartTime, VendorLink,
    MasterTourName, MasterTourNameDesktop, MasterTourNameMobile, VendorTourId, VendorScheduleLink, ReviewLink
FROM dbo.Tours
WHERE (@IncludeInactive = 1 OR IsActive = 1)
  AND (@Search IS NULL OR LOWER(TourName) LIKE LOWER(N'%' + @Search + N'%') OR LOWER(MasterTourName) LIKE LOWER(N'%' + @Search + N'%'))
ORDER BY IsActive DESC, MasterTourName, TourName;";

            var list = new List<DbTour>();
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@IncludeInactive", SqlDbType.Bit) { Value = includeInactive });
            cmd.Parameters.Add(new SqlParameter("@Search", SqlDbType.NVarChar, 255) { Value = (object?)TrimOrNull(search) ?? DBNull.Value });
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(new DbTour
                {
                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                    TourName = reader.GetString(reader.GetOrdinal("TourName")),
                    TourNameAlias = ReadNullableString(reader, "TourNameAlias"),
                    VendorNames = ReadNullableString(reader, "VendorNames"),
                    MeetingPlace = ReadNullableString(reader, "MeetingPlace"),
                    MeetingTime = ReadNullableString(reader, "MeetingTime"),
                    MeetingInstructions = ReadNullableString(reader, "MeetingInstructions"),
                    DefaultGuideId = ReadNullableInt(reader, "DefaultGuideId"),
                    Duration = ReadNullableString(reader, "Duration"),
                    MaxCapacity = ReadNullableInt(reader, "MaxCapacity"),
                    IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive")),
                    Notes = ReadNullableString(reader, "Notes"),
                    CreatedAt = ReadNullableDate(reader, "CreatedAt"),
                    UpdatedAt = ReadNullableDate(reader, "UpdatedAt"),
                    TourStartTime = ReadNullableString(reader, "TourStartTime"),
                    VendorLink = ReadNullableString(reader, "VendorLink"),
                    MasterTourName = reader.GetString(reader.GetOrdinal("MasterTourName")),
                    MasterTourNameDesktop = ReadNullableString(reader, "MasterTourNameDesktop"),
                    MasterTourNameMobile = ReadNullableString(reader, "MasterTourNameMobile"),
                    VendorTourId = ReadNullableString(reader, "VendorTourId"),
                    VendorScheduleLink = ReadNullableString(reader, "VendorScheduleLink"),
                    ReviewLink = ReadNullableString(reader, "ReviewLink")
                });
            }

            return list;
        }

        private static string BuildGroupKey(CoverageRow row)
        {
            var manualKey = TrimOrNull(row.ManualGroupKey);
            if (manualKey is not null)
            {
                return $"manual:{manualKey.ToLowerInvariant()}";
            }

            return row.TourId.HasValue ? $"tour:{row.TourId.Value}" : $"name:{row.NormalizedKey}";
        }

        private static TourProductionGroupCard BuildGroupCard(string key, List<CoverageRow> rows)
        {
            var top = rows.OrderByDescending(x => x.RecordCount).First();
            var mappedId = rows.Where(x => x.TourId.HasValue).GroupBy(x => x.TourId!.Value).OrderByDescending(g => g.Sum(x => x.RecordCount)).Select(g => g.Key).FirstOrDefault();
            var master = rows.Where(x => !string.IsNullOrWhiteSpace(x.MasterTourName)).GroupBy(x => x.MasterTourName!).OrderByDescending(g => g.Sum(x => x.RecordCount)).Select(g => g.Key).FirstOrDefault();
            var manual = rows.Select(x => TrimOrNull(x.ManualGroupKey)).FirstOrDefault(x => x is not null);

            var status = !rows.Any(x => x.TourId.HasValue)
                ? "Needs Tour"
                : (!rows.Any(x => !IsMissingMeetingPlace(x.SuggestedMeetingPlace))
                    ? "Needs Meeting Place"
                    : (rows.Any(x => x.TourId.HasValue && !x.AliasExact && !string.Equals(x.SourceType, "Mapping", StringComparison.OrdinalIgnoreCase)) ? "Needs Alias" : "Mapped"));

            return new TourProductionGroupCard
            {
                GroupKey = key,
                GroupLabel = master ?? top.RawTourName,
                Status = status,
                VariantCount = rows.Count,
                TotalSourceRows = rows.Sum(x => x.RecordCount),
                TourId = mappedId == 0 ? null : mappedId,
                MasterTourName = master,
                MeetingPlace = rows.Select(x => x.SuggestedMeetingPlace).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
                ManualGroupKey = manual
            };
        }

        private static TourProductionCandidateTour BuildCandidate(DbTour tour, IReadOnlyCollection<string> names, int? selectedTourId)
        {
            if (selectedTourId.HasValue && selectedTourId.Value == tour.Id)
            {
                return new TourProductionCandidateTour { TourId = tour.Id, TourName = tour.TourName, MasterTourName = tour.MasterTourName, MeetingPlace = tour.MeetingPlace, Duration = tour.Duration, Score = 100m };
            }

            var tokens = ScoreTokens(tour.MasterTourName).Union(ScoreTokens(tour.TourName), StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
            decimal best = 0m;
            foreach (var name in names)
            {
                var source = ScoreTokens(name);
                if (source.Count == 0)
                {
                    continue;
                }

                var overlap = source.Intersect(tokens, StringComparer.OrdinalIgnoreCase).Count();
                var union = source.Union(tokens, StringComparer.OrdinalIgnoreCase).Count();
                var score = union == 0 ? 0m : ((decimal)overlap / union) * 85m;
                if (ContainsIgnoreCase(tour.MasterTourName, name)) score += 10m;
                if (ContainsIgnoreCase(tour.TourName, name)) score += 5m;
                best = Math.Max(best, Math.Round(Math.Min(score, 99m), 2));
            }

            return new TourProductionCandidateTour { TourId = tour.Id, TourName = tour.TourName, MasterTourName = tour.MasterTourName, MeetingPlace = tour.MeetingPlace, Duration = tour.Duration, Score = best };
        }

        private static HashSet<string> ScoreTokens(string? value)
        {
            var key = NormalizeTourKey(value);
            var stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "the", "tour", "and", "of", "new", "york", "free", "reservation", "walking" };
            return key.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(x => x.Length > 1 && !stop.Contains(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static List<TourProductionScheduleTimeItem> ParseScheduleSlots(string? payload, string? fallbackMeetingTime, string? fallbackPlace)
        {
            var list = new List<TourProductionScheduleTimeItem>();
            var text = TrimOrNull(payload);
            if (text is null)
            {
                return list;
            }

            var fallbackTime = NormalizeTime(fallbackMeetingTime);
            var fallbackMeetingPlace = TrimOrNull(fallbackPlace);

            if (text.StartsWith("[", StringComparison.Ordinal))
            {
                try
                {
                    using var doc = JsonDocument.Parse(text);
                    foreach (var el in doc.RootElement.EnumerateArray())
                    {
                        if (el.ValueKind == JsonValueKind.String)
                        {
                            var time = NormalizeTime(el.GetString());
                            if (time is not null)
                            {
                                list.Add(new TourProductionScheduleTimeItem { TourTime = time, MeetingTime = fallbackTime, MeetingPlace = fallbackMeetingPlace });
                            }
                            continue;
                        }

                        if (el.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        var tourTime = el.TryGetProperty("TourTime", out var t) ? NormalizeTime(t.GetString()) : null;
                        if (tourTime is null)
                        {
                            continue;
                        }

                        var meetingTime = el.TryGetProperty("MeetingTime", out var mt) ? NormalizeTime(mt.GetString()) : fallbackTime;
                        var meetingPlace = el.TryGetProperty("MeetingPlace", out var mp) ? TrimOrNull(mp.GetString()) : fallbackMeetingPlace;
                        list.Add(new TourProductionScheduleTimeItem { TourTime = tourTime, MeetingTime = meetingTime, MeetingPlace = meetingPlace });
                    }
                }
                catch
                {
                    // fall through
                }
            }

            if (list.Count == 0)
            {
                foreach (var part in text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var t = NormalizeTime(part);
                    if (t is not null)
                    {
                        list.Add(new TourProductionScheduleTimeItem { TourTime = t, MeetingTime = fallbackTime, MeetingPlace = fallbackMeetingPlace });
                    }
                }
            }

            return list;
        }

        private async Task<DbTour?> CreateTourInternalAsync(TourProductionTourUpsertInput input, CancellationToken ct)
        {
            var clean = ValidateTourInput(input);
            using var conn = _connectionFactory.CreateOpenConnection();
            using var tx = conn.BeginTransaction();
            try
            {
                const string sql = @"
INSERT INTO dbo.Tours
(TourName, MeetingPlace, Duration, IsActive, MasterTourName, MasterTourNameDesktop, MasterTourNameMobile, VendorLink, VendorScheduleLink, ReviewLink, UpdatedAt)
VALUES
(@TourName, @MeetingPlace, @Duration, @IsActive, @MasterTourName, @MasterTourNameDesktop, @MasterTourNameMobile, @VendorLink, @VendorScheduleLink, @ReviewLink, SYSUTCDATETIME());
SELECT CAST(SCOPE_IDENTITY() AS int);";

                using var cmd = new SqlCommand(sql, conn, tx);
                AddTourParams(cmd, clean);
                var idObj = await cmd.ExecuteScalarAsync(ct);
                var id = idObj is int i ? i : Convert.ToInt32(idObj, CultureInfo.InvariantCulture);
                tx.Commit();
                return await LoadTourByIdAsync(id, ct);
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        private async Task<bool> UpdateTourInternalAsync(int tourId, TourProductionTourUpsertInput input, CancellationToken ct)
        {
            if (tourId <= 0)
            {
                return false;
            }

            var clean = ValidateTourInput(input);
            using var conn = _connectionFactory.CreateOpenConnection();
            using var tx = conn.BeginTransaction();
            try
            {
                const string sql = @"
UPDATE dbo.Tours
SET TourName = @TourName, MeetingPlace = @MeetingPlace, Duration = @Duration, IsActive = @IsActive,
    MasterTourName = @MasterTourName, MasterTourNameDesktop = @MasterTourNameDesktop, MasterTourNameMobile = @MasterTourNameMobile,
    VendorLink = @VendorLink, VendorScheduleLink = @VendorScheduleLink, ReviewLink = @ReviewLink, UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id;";
                using var cmd = new SqlCommand(sql, conn, tx);
                AddTourParams(cmd, clean);
                cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = tourId });
                var rows = await cmd.ExecuteNonQueryAsync(ct);
                tx.Commit();
                return rows > 0;
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        private async Task<bool> DeactivateTourInternalAsync(int tourId, CancellationToken ct)
        {
            if (tourId <= 0)
            {
                return false;
            }

            using var conn = _connectionFactory.CreateOpenConnection();
            using var tx = conn.BeginTransaction();
            try
            {
                const string sql = "UPDATE dbo.Tours SET IsActive = 0, UpdatedAt = SYSUTCDATETIME() WHERE Id = @Id;";
                using var cmd = new SqlCommand(sql, conn, tx);
                cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = tourId });
                var rows = await cmd.ExecuteNonQueryAsync(ct);
                tx.Commit();
                return rows > 0;
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        private async Task<List<TourNameMapping>> GetAliasesInternalAsync(int tourId, string? vendorName, bool includeInactive, CancellationToken ct)
        {
            if (tourId <= 0)
            {
                return new List<TourNameMapping>();
            }

            using var conn = _connectionFactory.CreateOpenConnection();
            const string sql = @"
SELECT Id, TourId, IncomingTourName, VendorName, IsActive, CreatedAt, UpdatedAt
FROM dbo.TourNameMappings
WHERE TourId = @TourId
  AND (@IncludeInactive = 1 OR IsActive = 1)
  AND (@VendorName IS NULL OR LOWER(LTRIM(RTRIM(ISNULL(VendorName, N'')))) = LOWER(LTRIM(RTRIM(@VendorName))))
ORDER BY IsActive DESC, VendorName, IncomingTourName;";

            var list = new List<TourNameMapping>();
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@TourId", SqlDbType.Int) { Value = tourId });
            cmd.Parameters.Add(new SqlParameter("@IncludeInactive", SqlDbType.Bit) { Value = includeInactive });
            cmd.Parameters.Add(new SqlParameter("@VendorName", SqlDbType.NVarChar, 100) { Value = (object?)TrimOrNull(vendorName) ?? DBNull.Value });
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(new TourNameMapping
                {
                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                    TourId = reader.GetInt32(reader.GetOrdinal("TourId")),
                    IncomingTourName = reader.GetString(reader.GetOrdinal("IncomingTourName")),
                    VendorName = ReadNullableString(reader, "VendorName"),
                    IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive")),
                    CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                    UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt"))
                });
            }

            return list;
        }

        private async Task<int> CreateAliasInternalAsync(TourProductionAliasCreateInput input, CancellationToken ct)
        {
            if (input is null || input.TourId <= 0 || string.IsNullOrWhiteSpace(input.IncomingTourName))
            {
                return -1;
            }

            var incoming = input.IncomingTourName.Trim();
            var vendor = TrimOrNull(input.VendorName);
            using var conn = _connectionFactory.CreateOpenConnection();
            using var tx = conn.BeginTransaction();
            try
            {
                const string findSql = @"
SELECT TOP 1 Id, IsActive
FROM dbo.TourNameMappings
WHERE TourId = @TourId
  AND LOWER(LTRIM(RTRIM(IncomingTourName))) = LOWER(LTRIM(RTRIM(@IncomingTourName)))
  AND ((@VendorName IS NULL AND NULLIF(LTRIM(RTRIM(ISNULL(VendorName, N''))), N'') IS NULL)
       OR LOWER(LTRIM(RTRIM(ISNULL(VendorName, N'')))) = LOWER(LTRIM(RTRIM(ISNULL(@VendorName, N'')))))
ORDER BY UpdatedAt DESC, Id DESC;";
                using (var findCmd = new SqlCommand(findSql, conn, tx))
                {
                    findCmd.Parameters.Add(new SqlParameter("@TourId", SqlDbType.Int) { Value = input.TourId });
                    findCmd.Parameters.Add(new SqlParameter("@IncomingTourName", SqlDbType.NVarChar, 500) { Value = incoming });
                    findCmd.Parameters.Add(new SqlParameter("@VendorName", SqlDbType.NVarChar, 100) { Value = (object?)vendor ?? DBNull.Value });
                    using var reader = await findCmd.ExecuteReaderAsync(ct);
                    if (await reader.ReadAsync(ct))
                    {
                        var id = reader.GetInt32(reader.GetOrdinal("Id"));
                        var isActive = reader.GetBoolean(reader.GetOrdinal("IsActive"));
                        reader.Close();
                        if (!isActive)
                        {
                            using var reactivateCmd = new SqlCommand("UPDATE dbo.TourNameMappings SET IsActive = 1, UpdatedAt = SYSDATETIME() WHERE Id = @Id;", conn, tx);
                            reactivateCmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
                            await reactivateCmd.ExecuteNonQueryAsync(ct);
                        }

                        tx.Commit();
                        return id;
                    }
                }

                const string insertSql = @"
INSERT INTO dbo.TourNameMappings (TourId, IncomingTourName, VendorName, IsActive, CreatedAt, UpdatedAt)
VALUES (@TourId, @IncomingTourName, @VendorName, 1, SYSDATETIME(), SYSDATETIME());
SELECT CAST(SCOPE_IDENTITY() AS int);";
                using var cmd = new SqlCommand(insertSql, conn, tx);
                cmd.Parameters.Add(new SqlParameter("@TourId", SqlDbType.Int) { Value = input.TourId });
                cmd.Parameters.Add(new SqlParameter("@IncomingTourName", SqlDbType.NVarChar, 500) { Value = incoming });
                cmd.Parameters.Add(new SqlParameter("@VendorName", SqlDbType.NVarChar, 100) { Value = (object?)vendor ?? DBNull.Value });
                var idObj = await cmd.ExecuteScalarAsync(ct);
                tx.Commit();
                return idObj is int i ? i : Convert.ToInt32(idObj, CultureInfo.InvariantCulture);
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        private async Task<bool> UpdateAliasInternalAsync(int aliasId, TourProductionAliasUpdateInput input, CancellationToken ct)
        {
            if (aliasId <= 0 || input is null || string.IsNullOrWhiteSpace(input.IncomingTourName))
            {
                return false;
            }

            using var conn = _connectionFactory.CreateOpenConnection();
            using var tx = conn.BeginTransaction();
            try
            {
                using var cmd = new SqlCommand("UPDATE dbo.TourNameMappings SET IncomingTourName = @IncomingTourName, VendorName = @VendorName, UpdatedAt = SYSDATETIME() WHERE Id = @Id;", conn, tx);
                cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = aliasId });
                cmd.Parameters.Add(new SqlParameter("@IncomingTourName", SqlDbType.NVarChar, 500) { Value = input.IncomingTourName.Trim() });
                cmd.Parameters.Add(new SqlParameter("@VendorName", SqlDbType.NVarChar, 100) { Value = (object?)TrimOrNull(input.VendorName) ?? DBNull.Value });
                var rows = await cmd.ExecuteNonQueryAsync(ct);
                tx.Commit();
                return rows > 0;
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        private async Task<bool> ReassignAliasInternalAsync(int aliasId, int targetTourId, CancellationToken ct)
        {
            if (aliasId <= 0 || targetTourId <= 0)
            {
                return false;
            }

            using var conn = _connectionFactory.CreateOpenConnection();
            using var tx = conn.BeginTransaction();
            try
            {
                using var cmd = new SqlCommand("UPDATE dbo.TourNameMappings SET TourId = @TourId, UpdatedAt = SYSDATETIME() WHERE Id = @Id;", conn, tx);
                cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = aliasId });
                cmd.Parameters.Add(new SqlParameter("@TourId", SqlDbType.Int) { Value = targetTourId });
                var rows = await cmd.ExecuteNonQueryAsync(ct);
                tx.Commit();
                return rows > 0;
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        private async Task<bool> SetAliasActiveInternalAsync(int aliasId, bool isActive, CancellationToken ct)
        {
            if (aliasId <= 0)
            {
                return false;
            }

            using var conn = _connectionFactory.CreateOpenConnection();
            using var tx = conn.BeginTransaction();
            try
            {
                using var cmd = new SqlCommand("UPDATE dbo.TourNameMappings SET IsActive = @IsActive, UpdatedAt = SYSDATETIME() WHERE Id = @Id;", conn, tx);
                cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = aliasId });
                cmd.Parameters.Add(new SqlParameter("@IsActive", SqlDbType.Bit) { Value = isActive });
                var rows = await cmd.ExecuteNonQueryAsync(ct);
                tx.Commit();
                return rows > 0;
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        private async Task<int> ApplyGroupOverrideToGroupInternalAsync(string groupKey, string overrideGroupKey, CancellationToken ct)
        {
            var safeGroupKey = TrimOrNull(groupKey);
            var safeOverrideKey = NormalizeOverrideGroupKey(overrideGroupKey);
            if (safeGroupKey is null || safeOverrideKey is null)
            {
                return 0;
            }

            using var conn = _connectionFactory.CreateOpenConnection();
            await EnsureGroupOverridesTableAsync(conn, tx: null, ct);
            var rows = await LoadCoverageRowsAsync(conn, ct);
            var targets = rows
                .Where(x => string.Equals(BuildGroupKey(x), safeGroupKey, StringComparison.OrdinalIgnoreCase))
                .GroupBy(x => x.RowSignature, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            if (targets.Count == 0)
            {
                return 0;
            }

            using var tx = conn.BeginTransaction();
            try
            {
                var changed = 0;
                foreach (var target in targets)
                {
                    if (await UpsertGroupOverrideAsync(conn, tx, target.SourceType, target.VendorName, target.RawTourName, safeOverrideKey, ct))
                    {
                        changed++;
                    }
                }

                tx.Commit();
                return changed;
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        private async Task<bool> ApplyGroupOverrideToVariantInternalAsync(TourProductionVariantOverrideInput input, string overrideGroupKey, CancellationToken ct)
        {
            if (input is null)
            {
                return false;
            }

            var safeOverrideKey = NormalizeOverrideGroupKey(overrideGroupKey);
            if (safeOverrideKey is null)
            {
                return false;
            }

            using var conn = _connectionFactory.CreateOpenConnection();
            using var tx = conn.BeginTransaction();
            try
            {
                await EnsureGroupOverridesTableAsync(conn, tx, ct);
                var changed = await UpsertGroupOverrideAsync(conn, tx, input.SourceType, input.VendorName, input.RawTourName, safeOverrideKey, ct);
                tx.Commit();
                return changed;
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        private async Task<int> ClearGroupOverridesForGroupInternalAsync(string groupKey, CancellationToken ct)
        {
            var safeGroupKey = TrimOrNull(groupKey);
            if (safeGroupKey is null)
            {
                return 0;
            }

            using var conn = _connectionFactory.CreateOpenConnection();
            await EnsureGroupOverridesTableAsync(conn, tx: null, ct);
            var rows = await LoadCoverageRowsAsync(conn, ct);
            var targets = rows
                .Where(x => string.Equals(BuildGroupKey(x), safeGroupKey, StringComparison.OrdinalIgnoreCase))
                .GroupBy(x => x.RowSignature, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            if (targets.Count == 0)
            {
                return 0;
            }

            using var tx = conn.BeginTransaction();
            try
            {
                var changed = 0;
                foreach (var target in targets)
                {
                    if (await DeactivateGroupOverrideAsync(conn, tx, target.SourceType, target.VendorName, target.RawTourName, ct))
                    {
                        changed++;
                    }
                }

                tx.Commit();
                return changed;
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        private async Task<bool> ClearGroupOverrideForVariantInternalAsync(TourProductionVariantOverrideInput input, CancellationToken ct)
        {
            if (input is null)
            {
                return false;
            }

            using var conn = _connectionFactory.CreateOpenConnection();
            using var tx = conn.BeginTransaction();
            try
            {
                await EnsureGroupOverridesTableAsync(conn, tx, ct);
                var changed = await DeactivateGroupOverrideAsync(conn, tx, input.SourceType, input.VendorName, input.RawTourName, ct);
                tx.Commit();
                return changed;
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        private async Task<Dictionary<string, string>> LoadGroupOverridesLookupAsync(SqlConnection conn, CancellationToken ct)
        {
            await EnsureGroupOverridesTableAsync(conn, tx: null, ct);
            const string sql = @"
SELECT SourceTypeKey, VendorNameKey, RawTourNameKey, OverrideGroupKey
FROM dbo.TourProductionGroupOverrides
WHERE IsActive = 1;";

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using var cmd = new SqlCommand(sql, conn);
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var sourceKey = reader.GetString(reader.GetOrdinal("SourceTypeKey"));
                var vendorKey = reader.GetString(reader.GetOrdinal("VendorNameKey"));
                var rawKey = reader.GetString(reader.GetOrdinal("RawTourNameKey"));
                var overrideKey = reader.GetString(reader.GetOrdinal("OverrideGroupKey"));
                var signature = BuildRowSignature(sourceKey, vendorKey, rawKey);
                result[signature] = overrideKey;
            }

            return result;
        }

        private static async Task EnsureGroupOverridesTableAsync(SqlConnection conn, SqlTransaction? tx, CancellationToken ct)
        {
            const string sql = @"
IF OBJECT_ID(N'dbo.TourProductionGroupOverrides', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TourProductionGroupOverrides
    (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_TourProductionGroupOverrides PRIMARY KEY,
        SourceTypeKey nvarchar(30) NOT NULL,
        VendorNameKey nvarchar(255) NOT NULL,
        RawTourNameKey nvarchar(500) NOT NULL,
        OverrideGroupKey nvarchar(200) NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_TourProductionGroupOverrides_IsActive DEFAULT (1),
        CreatedAt datetime2(7) NOT NULL CONSTRAINT DF_TourProductionGroupOverrides_CreatedAt DEFAULT (sysutcdatetime()),
        UpdatedAt datetime2(7) NOT NULL CONSTRAINT DF_TourProductionGroupOverrides_UpdatedAt DEFAULT (sysutcdatetime())
    );

    CREATE UNIQUE INDEX UX_TourProductionGroupOverrides_Row
        ON dbo.TourProductionGroupOverrides(SourceTypeKey, VendorNameKey, RawTourNameKey);
END";

            using var cmd = tx is null ? new SqlCommand(sql, conn) : new SqlCommand(sql, conn, tx);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        private static async Task<bool> UpsertGroupOverrideAsync(SqlConnection conn, SqlTransaction tx, string sourceType, string? vendorName, string rawTourName, string overrideGroupKey, CancellationToken ct)
        {
            var sourceKey = NormalizeSignaturePart(sourceType);
            var vendorKey = NormalizeSignaturePart(vendorName);
            var rawKey = NormalizeSignaturePart(rawTourName);
            if (string.IsNullOrWhiteSpace(sourceKey) || string.IsNullOrWhiteSpace(rawKey))
            {
                return false;
            }

            const string updateSql = @"
UPDATE dbo.TourProductionGroupOverrides
SET OverrideGroupKey = @OverrideGroupKey, IsActive = 1, UpdatedAt = sysutcdatetime()
WHERE SourceTypeKey = @SourceTypeKey AND VendorNameKey = @VendorNameKey AND RawTourNameKey = @RawTourNameKey;";

            using (var updateCmd = new SqlCommand(updateSql, conn, tx))
            {
                AddGroupOverrideIdentityParams(updateCmd, sourceKey, vendorKey, rawKey);
                updateCmd.Parameters.Add(new SqlParameter("@OverrideGroupKey", SqlDbType.NVarChar, 200) { Value = overrideGroupKey });
                var rows = await updateCmd.ExecuteNonQueryAsync(ct);
                if (rows > 0)
                {
                    return true;
                }
            }

            const string insertSql = @"
INSERT INTO dbo.TourProductionGroupOverrides (SourceTypeKey, VendorNameKey, RawTourNameKey, OverrideGroupKey, IsActive, CreatedAt, UpdatedAt)
VALUES (@SourceTypeKey, @VendorNameKey, @RawTourNameKey, @OverrideGroupKey, 1, sysutcdatetime(), sysutcdatetime());";

            try
            {
                using var insertCmd = new SqlCommand(insertSql, conn, tx);
                AddGroupOverrideIdentityParams(insertCmd, sourceKey, vendorKey, rawKey);
                insertCmd.Parameters.Add(new SqlParameter("@OverrideGroupKey", SqlDbType.NVarChar, 200) { Value = overrideGroupKey });
                await insertCmd.ExecuteNonQueryAsync(ct);
                return true;
            }
            catch (SqlException ex) when (ex.Number == 2601 || ex.Number == 2627)
            {
                using var retryCmd = new SqlCommand(updateSql, conn, tx);
                AddGroupOverrideIdentityParams(retryCmd, sourceKey, vendorKey, rawKey);
                retryCmd.Parameters.Add(new SqlParameter("@OverrideGroupKey", SqlDbType.NVarChar, 200) { Value = overrideGroupKey });
                var rows = await retryCmd.ExecuteNonQueryAsync(ct);
                return rows > 0;
            }
        }

        private static async Task<bool> DeactivateGroupOverrideAsync(SqlConnection conn, SqlTransaction tx, string sourceType, string? vendorName, string rawTourName, CancellationToken ct)
        {
            var sourceKey = NormalizeSignaturePart(sourceType);
            var vendorKey = NormalizeSignaturePart(vendorName);
            var rawKey = NormalizeSignaturePart(rawTourName);
            if (string.IsNullOrWhiteSpace(sourceKey) || string.IsNullOrWhiteSpace(rawKey))
            {
                return false;
            }

            const string sql = @"
UPDATE dbo.TourProductionGroupOverrides
SET IsActive = 0, UpdatedAt = sysutcdatetime()
WHERE SourceTypeKey = @SourceTypeKey AND VendorNameKey = @VendorNameKey AND RawTourNameKey = @RawTourNameKey AND IsActive = 1;";
            using var cmd = new SqlCommand(sql, conn, tx);
            AddGroupOverrideIdentityParams(cmd, sourceKey, vendorKey, rawKey);
            var rows = await cmd.ExecuteNonQueryAsync(ct);
            return rows > 0;
        }

        private static void AddGroupOverrideIdentityParams(SqlCommand cmd, string sourceTypeKey, string vendorNameKey, string rawTourNameKey)
        {
            cmd.Parameters.Add(new SqlParameter("@SourceTypeKey", SqlDbType.NVarChar, 30) { Value = sourceTypeKey });
            cmd.Parameters.Add(new SqlParameter("@VendorNameKey", SqlDbType.NVarChar, 255) { Value = vendorNameKey });
            cmd.Parameters.Add(new SqlParameter("@RawTourNameKey", SqlDbType.NVarChar, 500) { Value = rawTourNameKey });
        }

        private async Task<DbTour?> LoadTourByIdAsync(int id, CancellationToken ct)
        {
            using var conn = _connectionFactory.CreateOpenConnection();
            var tours = await LoadToursInternalAsync(conn, includeInactive: true, search: null, ct);
            return tours.FirstOrDefault(x => x.Id == id);
        }

        private static TourProductionTourUpsertInput ValidateTourInput(TourProductionTourUpsertInput input)
        {
            if (input is null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            var clean = new TourProductionTourUpsertInput
            {
                MasterTourName = TrimOrNull(input.MasterTourName) ?? string.Empty,
                TourName = TrimOrNull(input.TourName) ?? string.Empty,
                MasterTourNameDesktop = TrimOrNull(input.MasterTourNameDesktop),
                MasterTourNameMobile = TrimOrNull(input.MasterTourNameMobile),
                Duration = TrimOrNull(input.Duration),
                MeetingPlace = TrimOrNull(input.MeetingPlace) ?? string.Empty,
                VendorLink = TrimOrNull(input.VendorLink),
                VendorScheduleLink = TrimOrNull(input.VendorScheduleLink),
                ReviewLink = TrimOrNull(input.ReviewLink),
                IsActive = input.IsActive
            };

            if (string.IsNullOrWhiteSpace(clean.MasterTourName))
            {
                throw new InvalidOperationException("MasterTourName is required.");
            }

            if (string.IsNullOrWhiteSpace(clean.TourName))
            {
                clean.TourName = clean.MasterTourName;
            }

            if (string.IsNullOrWhiteSpace(clean.MeetingPlace))
            {
                throw new InvalidOperationException("MeetingPlace is required by dbo.Tours schema.");
            }

            return clean;
        }

        private static void AddTourParams(SqlCommand cmd, TourProductionTourUpsertInput input)
        {
            cmd.Parameters.Add(new SqlParameter("@TourName", SqlDbType.NVarChar, 200) { Value = input.TourName });
            cmd.Parameters.Add(new SqlParameter("@MeetingPlace", SqlDbType.NVarChar, 200) { Value = input.MeetingPlace });
            cmd.Parameters.Add(new SqlParameter("@Duration", SqlDbType.NVarChar, 50) { Value = (object?)input.Duration ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@IsActive", SqlDbType.Bit) { Value = input.IsActive });
            cmd.Parameters.Add(new SqlParameter("@MasterTourName", SqlDbType.NVarChar, 200) { Value = input.MasterTourName });
            cmd.Parameters.Add(new SqlParameter("@MasterTourNameDesktop", SqlDbType.NVarChar, 100) { Value = (object?)input.MasterTourNameDesktop ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@MasterTourNameMobile", SqlDbType.NVarChar, 50) { Value = (object?)input.MasterTourNameMobile ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@VendorLink", SqlDbType.NVarChar, 500) { Value = (object?)input.VendorLink ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@VendorScheduleLink", SqlDbType.NVarChar, 1000) { Value = (object?)input.VendorScheduleLink ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@ReviewLink", SqlDbType.NVarChar, 1000) { Value = (object?)input.ReviewLink ?? DBNull.Value });
        }

        private static string BuildRowSignature(string sourceType, string? vendorName, string rawTourName)
        {
            var sourceKey = NormalizeSignaturePart(sourceType);
            var vendorKey = NormalizeSignaturePart(vendorName);
            var rawKey = NormalizeSignaturePart(rawTourName);
            return $"{sourceKey}|{vendorKey}|{rawKey}";
        }

        private static string NormalizeSignaturePart(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = Regex.Replace(value.Trim().ToLowerInvariant(), @"\s+", " ", RegexOptions.CultureInvariant);
            return normalized;
        }

        private static string? NormalizeOverrideGroupKey(string? value)
        {
            var clean = TrimOrNull(value);
            if (clean is null)
            {
                return null;
            }

            var normalized = Regex.Replace(clean.ToLowerInvariant(), @"\s+", "-", RegexOptions.CultureInvariant).Trim('-');
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            return normalized.Length <= 200 ? normalized : normalized[..200];
        }

        private static string NormalizeTourKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var text = WebUtility.HtmlDecode(value).Trim();
            text = text.Replace("tour reservation", " ", StringComparison.OrdinalIgnoreCase);
            text = text.Replace("reservation", " ", StringComparison.OrdinalIgnoreCase);
            text = text.TrimEnd('!', '.', ',', ';', ':');
            text = Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9 ]+", " ", RegexOptions.CultureInvariant);
            text = Regex.Replace(text, @"\s+", " ", RegexOptions.CultureInvariant).Trim();
            return text;
        }

        private static string? NormalizeTime(string? input)
        {
            var value = TrimOrNull(input);
            if (value is null)
            {
                return null;
            }

            if (TimeSpan.TryParseExact(value, new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out var ts))
            {
                return ts.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
            }

            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dt))
            {
                return dt.ToString("HH:mm", CultureInfo.InvariantCulture);
            }

            return value;
        }

        private static bool IsMissingMeetingPlace(string? value)
        {
            var place = TrimOrNull(value);
            if (place is null)
            {
                return true;
            }

            var lowered = place.ToLowerInvariant();
            return lowered is "tbd" or "tba" or "na" or "n/a";
        }

        private static int StatusSort(string? status)
        {
            return status?.ToLowerInvariant() switch
            {
                "needs tour" => 0,
                "needs meeting place" => 1,
                "needs alias" => 2,
                "mapped" => 3,
                _ => 9
            };
        }

        private static bool ContainsIgnoreCase(string? source, string? value)
        {
            return !string.IsNullOrWhiteSpace(source)
                   && !string.IsNullOrWhiteSpace(value)
                   && source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string? TrimOrNull(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string? ReadNullableString(SqlDataReader reader, string column)
        {
            var o = reader.GetOrdinal(column);
            return reader.IsDBNull(o) ? null : reader.GetString(o);
        }

        private static int? ReadNullableInt(SqlDataReader reader, string column)
        {
            var o = reader.GetOrdinal(column);
            return reader.IsDBNull(o) ? null : reader.GetInt32(o);
        }

        private static DateTime? ReadNullableDate(SqlDataReader reader, string column)
        {
            var o = reader.GetOrdinal(column);
            return reader.IsDBNull(o) ? null : reader.GetDateTime(o);
        }
    }
}
