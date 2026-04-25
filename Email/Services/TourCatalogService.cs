using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Email.Models;
using Microsoft.Data.SqlClient;

namespace Email.Services
{
    /// <summary>
    /// 2025-12-05 00:00 UTC - Builds a runtime catalog of canonical tours (names/times/default guides).
    /// Modified: 2026-02-11 - Added DB-driven tour name mapping support
    /// </summary>
    public sealed class TourCatalogService : ITourCatalogService
    {
        private readonly SqlConnectionFactory _connectionFactory;
        private readonly ITourNameMappingService _mappingService;

        public TourCatalogService(SqlConnectionFactory connectionFactory, ITourNameMappingService mappingService)
        {
            _connectionFactory = connectionFactory;
            _mappingService = mappingService;
        }

        public async Task<TourCatalogSnapshot> BuildCatalogAsync(CancellationToken ct = default)
        {
            var snapshot = new TourCatalogSnapshot();
            var entryByName = new Dictionary<string, TourCatalogEntry>(StringComparer.OrdinalIgnoreCase);

            using var conn = _connectionFactory.CreateOpenConnection();

            // Load all data
            await LoadToursAsync(conn, entryByName, ct);
            await LoadBookingsAsync(conn, entryByName, ct);
            await LoadProcessedEmailsAsync(conn, entryByName, ct);
            await LoadGuideDefaultsAsync(conn, snapshot, ct);

            snapshot.Entries = entryByName.Values
                .DistinctBy(e => e.CanonicalName, StringComparer.OrdinalIgnoreCase)
                .Select(e =>
                {
                    // Deduplicate and sort times for stable dropdowns
                    e.Times = e.Times
                        .Where(t => !string.IsNullOrWhiteSpace(t))
                        .Select(NormalizeTime)
                        .Where(t => !string.IsNullOrWhiteSpace(t) && !string.Equals(t, "00:00", StringComparison.OrdinalIgnoreCase))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(t => t)
                        .ToList();

                    if (string.IsNullOrWhiteSpace(e.DisplayName))
                    {
                        e.DisplayName = TourNameNormalization.NormalizeTourNameForDisplay(e.CanonicalName);
                    }

                    return e;
                })
                .OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return snapshot;
        }

        public string NormalizeName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName)) return string.Empty;
            return TourNameNormalization.NormalizeTourNameForGrouping(string.Empty, rawName).Trim();
        }

        public string NormalizeTime(string rawTime)
        {
            if (string.IsNullOrWhiteSpace(rawTime)) return string.Empty;

            var trimmed = rawTime.Trim();
            if (DateTime.TryParse(trimmed, out var dt))
            {
                return dt.ToString("HH:mm");
            }

            if (TimeSpan.TryParse(trimmed, out var ts))
            {
                var dt2 = DateTime.Today.Add(ts);
                return dt2.ToString("HH:mm");
            }

            return trimmed;
        }

        private async Task LoadToursAsync(SqlConnection conn, Dictionary<string, TourCatalogEntry> entryByName, CancellationToken ct)
        {
            const string sql = @"
SELECT Id, TourName, TourNameAlias, MeetingPlace, MeetingTime, DefaultGuideId, TourStartTime,
       MasterTourName, MasterTourNameDesktop
FROM dbo.Tours WITH (NOLOCK)
WHERE IsActive = 1;";

            using var cmd = new SqlCommand(sql, conn);
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var tourName = reader.GetString(reader.GetOrdinal("TourName"));
                var masterTourName = reader.IsDBNull(reader.GetOrdinal("MasterTourName")) ? null : reader.GetString(reader.GetOrdinal("MasterTourName"));
                var masterTourNameDesktop = reader.IsDBNull(reader.GetOrdinal("MasterTourNameDesktop")) ? null : reader.GetString(reader.GetOrdinal("MasterTourNameDesktop"));
                var displayName = !string.IsNullOrWhiteSpace(masterTourNameDesktop)
                    ? masterTourNameDesktop.Trim()
                    : (!string.IsNullOrWhiteSpace(masterTourName) ? masterTourName.Trim() : tourName.Trim());

                var canonical = NormalizeName(displayName);
                if (string.IsNullOrWhiteSpace(canonical)) continue;

                if (!entryByName.TryGetValue(canonical, out var entry))
                {
                    entry = new TourCatalogEntry
                    {
                        CanonicalName = canonical,
                        DisplayName = displayName
                    };
                    entryByName[canonical] = entry;
                }
                else if (string.IsNullOrWhiteSpace(entry.DisplayName))
                {
                    entry.DisplayName = displayName;
                }

                entry.TourId ??= reader.GetInt32(reader.GetOrdinal("Id"));
                entry.DefaultGuideId ??= reader.IsDBNull(reader.GetOrdinal("DefaultGuideId")) ? null : reader.GetInt32(reader.GetOrdinal("DefaultGuideId"));
                entry.MeetingPlace ??= reader.IsDBNull(reader.GetOrdinal("MeetingPlace")) ? null : reader.GetString(reader.GetOrdinal("MeetingPlace"));
                entry.MeetingTime ??= reader.IsDBNull(reader.GetOrdinal("MeetingTime")) ? null : reader.GetString(reader.GetOrdinal("MeetingTime"));

                var startTime = reader.IsDBNull(reader.GetOrdinal("TourStartTime")) ? null : reader.GetString(reader.GetOrdinal("TourStartTime"));
                if (!string.IsNullOrWhiteSpace(startTime))
                {
                    entry.Times.Add(NormalizeTime(startTime));
                }

                var meetingTime = entry.MeetingTime;
                if (!string.IsNullOrWhiteSpace(meetingTime))
                {
                    entry.Times.Add(NormalizeTime(meetingTime));
                }

                // Keep aliases for lookup while showing master/display names in picker UIs.
                RegisterTourAlias(entryByName, entry, tourName);
                RegisterTourAlias(entryByName, entry, masterTourName);
                RegisterTourAlias(entryByName, entry, masterTourNameDesktop);

                var aliasRaw = reader.IsDBNull(reader.GetOrdinal("TourNameAlias")) ? null : reader.GetString(reader.GetOrdinal("TourNameAlias"));
                if (!string.IsNullOrWhiteSpace(aliasRaw))
                {
                    var aliases = aliasRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var alias in aliases)
                    {
                        RegisterTourAlias(entryByName, entry, alias);
                    }
                }
            }
        }

        private async Task LoadBookingsAsync(SqlConnection conn, Dictionary<string, TourCatalogEntry> entryByName, CancellationToken ct)
        {
            const string sql = @"
SELECT TourName, TourTime
FROM dbo.Bookings WITH (NOLOCK)
WHERE TourName IS NOT NULL AND TourTime IS NOT NULL AND LTRIM(RTRIM(TourName)) <> '';";

            using var cmd = new SqlCommand(sql, conn);
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var tourName = reader.GetString(reader.GetOrdinal("TourName"));
                var time = reader.IsDBNull(reader.GetOrdinal("TourTime")) ? null : reader.GetString(reader.GetOrdinal("TourTime"));

                var canonical = NormalizeName(tourName);
                if (string.IsNullOrWhiteSpace(canonical)) continue;
                if (!entryByName.TryGetValue(canonical, out var entry))
                {
                    // Ignore unknown historical variants for picker quality.
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(time))
                {
                    entry.Times.Add(NormalizeTime(time));
                }
            }
        }

        private async Task LoadProcessedEmailsAsync(SqlConnection conn, Dictionary<string, TourCatalogEntry> entryByName, CancellationToken ct)
        {
            const string sql = @"
SELECT TourName, TourTime
FROM dbo.AutomaticGmail_ProcessedEmails WITH (NOLOCK)
WHERE TourName IS NOT NULL AND TourTime IS NOT NULL AND LTRIM(RTRIM(TourName)) <> '';";

            using var cmd = new SqlCommand(sql, conn);
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var tourName = reader.GetString(reader.GetOrdinal("TourName"));
                var time = reader.IsDBNull(reader.GetOrdinal("TourTime")) ? null : reader.GetString(reader.GetOrdinal("TourTime"));

                var canonical = NormalizeName(tourName);
                if (string.IsNullOrWhiteSpace(canonical)) continue;
                if (!entryByName.TryGetValue(canonical, out var entry))
                {
                    // Ignore unknown historical variants for picker quality.
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(time))
                {
                    entry.Times.Add(NormalizeTime(time));
                }
            }
        }

        private void RegisterTourAlias(Dictionary<string, TourCatalogEntry> entryByName, TourCatalogEntry entry, string? alias)
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                return;
            }

            var trimmedAlias = alias.Trim();
            if (string.IsNullOrWhiteSpace(trimmedAlias))
            {
                return;
            }

            entry.Aliases.Add(trimmedAlias);

            var normalizedAlias = NormalizeName(trimmedAlias);
            if (string.IsNullOrWhiteSpace(normalizedAlias))
            {
                return;
            }

            if (!entryByName.ContainsKey(normalizedAlias))
            {
                entryByName[normalizedAlias] = entry;
            }
        }

        private async Task LoadGuideDefaultsAsync(SqlConnection conn, TourCatalogSnapshot snapshot, CancellationToken ct)
        {
            const string sql = @"
SELECT TourId, DayOfWeek, StartTime, GuideId
FROM dbo.TourGuideDefaults WITH (NOLOCK)
WHERE IsActive = 1;";

            using var cmd = new SqlCommand(sql, conn);
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var tourId = reader.GetInt32(reader.GetOrdinal("TourId"));
                var dow = reader.GetInt32(reader.GetOrdinal("DayOfWeek"));
                var startTime = reader.GetString(reader.GetOrdinal("StartTime"));
                var guideId = reader.GetInt32(reader.GetOrdinal("GuideId"));

                if (!snapshot.GuideDefaultsByTourId.TryGetValue(tourId, out var list))
                {
                    list = new List<TourGuideDefault>();
                    snapshot.GuideDefaultsByTourId[tourId] = list;
                }

                list.Add(new TourGuideDefault
                {
                    TourId = tourId,
                    DayOfWeek = dow,
                    StartTime = NormalizeTime(startTime),
                    GuideId = guideId,
                    IsActive = true
                });
            }
        }
    }

    public sealed class TourCatalogSnapshot
    {
        public List<TourCatalogEntry> Entries { get; set; } = new();

        public Dictionary<int, List<TourGuideDefault>> GuideDefaultsByTourId { get; set; } = new();
    }

    public sealed class TourCatalogEntry
    {
        public string CanonicalName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;

        public int? TourId { get; set; }

        public string? MeetingPlace { get; set; }

        public string? MeetingTime { get; set; }

        public int? DefaultGuideId { get; set; }

        public List<string> Times { get; set; } = new();

        public HashSet<string> Aliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}


