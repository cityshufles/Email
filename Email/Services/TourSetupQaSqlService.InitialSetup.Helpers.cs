using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Email.Services
{
    public sealed partial class TourSetupQaSqlService
    {
        private sealed class StagingFamilyRow
        {
            public long Id { get; set; }
            public Guid BatchId { get; set; }
            public string SourceType { get; set; } = string.Empty;
            public string? VendorName { get; set; }
            public string? VendorKey { get; set; }
            public string RawTourName { get; set; } = string.Empty;
            public string NormalizedTourKey { get; set; } = string.Empty;
            public int RecordCount { get; set; }
            public DateTime? TourDate { get; set; }
            public int? ProposedTourId { get; set; }
            public string? ProposedMasterTourName { get; set; }
            public string? MeetingPlaceExpected { get; set; }
            public string? MeetingTimeExpected { get; set; }
            public string? MeetingPlaceProposed { get; set; }
            public string? MeetingTimeProposed { get; set; }
            public string GapTypeComputed { get; set; } = "OK";
            public string? GapTypeFinal { get; set; }
            public string? Notes { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime UpdatedAt { get; set; }
            public string? FamilyKey { get; set; }
            public string? DecisionStatus { get; set; }
            public string? DecisionType { get; set; }
            public int? SelectedTourId { get; set; }
            public string? SelectedMasterTourName { get; set; }
            public string? SelectedMasterTourNameMobile { get; set; }
            public string? SelectedTourDuration { get; set; }
            public string? SelectedMeetingPlace { get; set; }
            public string? VendorLinkStatus { get; set; }
            public string? VendorScheduleLinkStatus { get; set; }
            public string? ReviewLinkStatus { get; set; }
            public string? SelectedVendorLink { get; set; }
            public string? SelectedVendorScheduleLink { get; set; }
            public string? SelectedReviewLink { get; set; }
            public bool? IsAliasSelected { get; set; }
            public bool HasExactAliasMatch { get; set; }
        }

        private sealed class ActiveTourRow
        {
            public int TourId { get; set; }
            public string TourName { get; set; } = string.Empty;
            public string MasterTourName { get; set; } = string.Empty;
            public string? MasterTourNameDesktop { get; set; }
            public string? MasterTourNameMobile { get; set; }
            public string? Duration { get; set; }
            public string? MeetingPlace { get; set; }
            public string? VendorLink { get; set; }
            public string? VendorScheduleLink { get; set; }
            public string? ReviewLink { get; set; }
        }

        private sealed class HintCache
        {
            public List<string> DurationHints { get; init; } = new();
            public List<string> MeetingPlaceHints { get; init; } = new();
        }

        private async Task<Guid> ResolveActiveRunIdAsync(SqlConnection conn, CancellationToken ct)
        {
            await EnsureInitialSetupColumnsAsync(conn, ct);

            const string sql = @"
SELECT TOP (1)
    s.BatchId
FROM dbo.TourSetupQaStaging s
GROUP BY
    s.BatchId
ORDER BY
    MAX(COALESCE(s.UpdatedAt, s.CreatedAt)) DESC,
    MAX(s.Id) DESC;";

            using var cmd = new SqlCommand(sql, conn);
            var value = await cmd.ExecuteScalarAsync(ct);
            if (value is Guid runId)
            {
                return runId;
            }

            return Guid.NewGuid();
        }

        private static async Task EnsureInitialSetupColumnsAsync(SqlConnection conn, CancellationToken ct)
        {
            var alterStatements = new[]
            {
                "IF COL_LENGTH('dbo.TourSetupQaStaging', 'FamilyKey') IS NULL ALTER TABLE dbo.TourSetupQaStaging ADD FamilyKey NVARCHAR(255) NULL;",
                "IF COL_LENGTH('dbo.TourSetupQaStaging', 'DecisionStatus') IS NULL ALTER TABLE dbo.TourSetupQaStaging ADD DecisionStatus NVARCHAR(30) NULL;",
                "IF COL_LENGTH('dbo.TourSetupQaStaging', 'DecisionType') IS NULL ALTER TABLE dbo.TourSetupQaStaging ADD DecisionType NVARCHAR(30) NULL;",
                "IF COL_LENGTH('dbo.TourSetupQaStaging', 'SelectedTourId') IS NULL ALTER TABLE dbo.TourSetupQaStaging ADD SelectedTourId INT NULL;",
                "IF COL_LENGTH('dbo.TourSetupQaStaging', 'SelectedMasterTourName') IS NULL ALTER TABLE dbo.TourSetupQaStaging ADD SelectedMasterTourName NVARCHAR(255) NULL;",
                "IF COL_LENGTH('dbo.TourSetupQaStaging', 'SelectedMasterTourNameMobile') IS NULL ALTER TABLE dbo.TourSetupQaStaging ADD SelectedMasterTourNameMobile NVARCHAR(50) NULL;",
                "IF COL_LENGTH('dbo.TourSetupQaStaging', 'SelectedTourDuration') IS NULL ALTER TABLE dbo.TourSetupQaStaging ADD SelectedTourDuration NVARCHAR(50) NULL;",
                "IF COL_LENGTH('dbo.TourSetupQaStaging', 'SelectedTourStartTime') IS NULL ALTER TABLE dbo.TourSetupQaStaging ADD SelectedTourStartTime NVARCHAR(50) NULL;",
                "IF COL_LENGTH('dbo.TourSetupQaStaging', 'SelectedMeetingTime') IS NULL ALTER TABLE dbo.TourSetupQaStaging ADD SelectedMeetingTime NVARCHAR(50) NULL;",
                "IF COL_LENGTH('dbo.TourSetupQaStaging', 'SelectedMeetingPlace') IS NULL ALTER TABLE dbo.TourSetupQaStaging ADD SelectedMeetingPlace NVARCHAR(255) NULL;",
                "IF COL_LENGTH('dbo.TourSetupQaStaging', 'VendorLinkStatus') IS NULL ALTER TABLE dbo.TourSetupQaStaging ADD VendorLinkStatus NVARCHAR(20) NULL;",
                "IF COL_LENGTH('dbo.TourSetupQaStaging', 'VendorScheduleLinkStatus') IS NULL ALTER TABLE dbo.TourSetupQaStaging ADD VendorScheduleLinkStatus NVARCHAR(20) NULL;",
                "IF COL_LENGTH('dbo.TourSetupQaStaging', 'ReviewLinkStatus') IS NULL ALTER TABLE dbo.TourSetupQaStaging ADD ReviewLinkStatus NVARCHAR(20) NULL;",
                "IF COL_LENGTH('dbo.TourSetupQaStaging', 'SelectedVendorLink') IS NULL ALTER TABLE dbo.TourSetupQaStaging ADD SelectedVendorLink NVARCHAR(1000) NULL;",
                "IF COL_LENGTH('dbo.TourSetupQaStaging', 'SelectedVendorScheduleLink') IS NULL ALTER TABLE dbo.TourSetupQaStaging ADD SelectedVendorScheduleLink NVARCHAR(1000) NULL;",
                "IF COL_LENGTH('dbo.TourSetupQaStaging', 'SelectedReviewLink') IS NULL ALTER TABLE dbo.TourSetupQaStaging ADD SelectedReviewLink NVARCHAR(1000) NULL;",
                "IF COL_LENGTH('dbo.TourSetupQaStaging', 'IsAliasSelected') IS NULL ALTER TABLE dbo.TourSetupQaStaging ADD IsAliasSelected BIT NULL;"
            };

            foreach (var statement in alterStatements)
            {
                using var alterCommand = new SqlCommand(statement, conn);
                await alterCommand.ExecuteNonQueryAsync(ct);
            }

            const string normalizeSql = @"
UPDATE s
SET
    FamilyKey = COALESCE(NULLIF(LTRIM(RTRIM(s.FamilyKey)), N''), NULLIF(LTRIM(RTRIM(s.SelectedMasterTourName)), N''), NULLIF(LTRIM(RTRIM(s.ProposedMasterTourName)), N''), NULLIF(LTRIM(RTRIM(s.NormalizedTourKey)), N''), NULLIF(LTRIM(RTRIM(s.RawTourName)), N'')),
    DecisionType = COALESCE(NULLIF(LTRIM(RTRIM(s.DecisionType)), N''), CASE WHEN s.SelectedTourId IS NOT NULL OR s.ProposedTourId IS NOT NULL THEN N'SameTour' ELSE N'CreateNewTour' END),
    DecisionStatus = COALESCE(NULLIF(LTRIM(RTRIM(s.DecisionStatus)), N''), N'Pending')
FROM dbo.TourSetupQaStaging s
WHERE NULLIF(LTRIM(RTRIM(ISNULL(s.FamilyKey, N''))), N'') IS NULL
   OR NULLIF(LTRIM(RTRIM(ISNULL(s.DecisionType, N''))), N'') IS NULL
   OR NULLIF(LTRIM(RTRIM(ISNULL(s.DecisionStatus, N''))), N'') IS NULL;";

            using var normalizeCommand = new SqlCommand(normalizeSql, conn);
            await normalizeCommand.ExecuteNonQueryAsync(ct);
        }

        private async Task<List<StagingFamilyRow>> LoadFamilyRowsAsync(SqlConnection conn, Guid runId, CancellationToken ct)
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
    s.CreatedAt,
    s.UpdatedAt,
    s.FamilyKey,
    s.DecisionStatus,
    s.DecisionType,
    s.SelectedTourId,
    s.SelectedMasterTourName,
    s.SelectedMasterTourNameMobile,
    s.SelectedTourDuration,
    s.SelectedMeetingPlace,
    s.VendorLinkStatus,
    s.VendorScheduleLinkStatus,
    s.ReviewLinkStatus,
    s.SelectedVendorLink,
    s.SelectedVendorScheduleLink,
    s.SelectedReviewLink,
    s.IsAliasSelected,
    CAST(CASE WHEN aliasMatch.MappingId IS NULL THEN 0 ELSE 1 END AS bit) AS HasExactAliasMatch
FROM dbo.TourSetupQaStaging s
OUTER APPLY
(
    SELECT TOP (1)
        m.Id AS MappingId
    FROM dbo.TourNameMappings m
    WHERE m.IsActive = 1
      AND COALESCE(s.SelectedTourId, s.ProposedTourId) IS NOT NULL
      AND m.TourId = COALESCE(s.SelectedTourId, s.ProposedTourId)
      AND LOWER(LTRIM(RTRIM(ISNULL(m.IncomingTourName, N'')))) = LOWER(LTRIM(RTRIM(ISNULL(s.RawTourName, N''))))
      AND ((NULLIF(LTRIM(RTRIM(ISNULL(s.VendorName, N''))), N'') IS NULL AND NULLIF(LTRIM(RTRIM(ISNULL(m.VendorName, N''))), N'') IS NULL)
           OR LOWER(LTRIM(RTRIM(ISNULL(m.VendorName, N'')))) = LOWER(LTRIM(RTRIM(ISNULL(s.VendorName, N'')))))
    ORDER BY
        m.UpdatedAt DESC,
        m.Id DESC
) aliasMatch
WHERE s.BatchId = @BatchId
ORDER BY
    COALESCE(NULLIF(LTRIM(RTRIM(s.FamilyKey)), N''), NULLIF(LTRIM(RTRIM(s.SelectedMasterTourName)), N''), NULLIF(LTRIM(RTRIM(s.ProposedMasterTourName)), N''), s.NormalizedTourKey, s.RawTourName),
    s.RecordCount DESC,
    s.Id DESC;";

            var rows = new List<StagingFamilyRow>();
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@BatchId", SqlDbType.UniqueIdentifier) { Value = runId });
            using var reader = await cmd.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                rows.Add(new StagingFamilyRow
                {
                    Id = reader.GetInt64(reader.GetOrdinal("Id")),
                    BatchId = reader.GetGuid(reader.GetOrdinal("BatchId")),
                    SourceType = reader.GetString(reader.GetOrdinal("SourceType")),
                    VendorName = ReadNullableString(reader, "VendorName"),
                    VendorKey = ReadNullableString(reader, "VendorKey"),
                    RawTourName = reader.GetString(reader.GetOrdinal("RawTourName")),
                    NormalizedTourKey = reader.GetString(reader.GetOrdinal("NormalizedTourKey")),
                    RecordCount = reader.GetInt32(reader.GetOrdinal("RecordCount")),
                    TourDate = ReadNullableDate(reader, "TourDate"),
                    ProposedTourId = ReadNullableInt(reader, "ProposedTourId"),
                    ProposedMasterTourName = ReadNullableString(reader, "ProposedMasterTourName"),
                    MeetingPlaceExpected = ReadNullableString(reader, "MeetingPlaceExpected"),
                    MeetingTimeExpected = ReadNullableString(reader, "MeetingTimeExpected"),
                    MeetingPlaceProposed = ReadNullableString(reader, "MeetingPlaceProposed"),
                    MeetingTimeProposed = ReadNullableString(reader, "MeetingTimeProposed"),
                    GapTypeComputed = ReadNullableString(reader, "GapTypeComputed") ?? "OK",
                    GapTypeFinal = ReadNullableString(reader, "GapTypeFinal"),
                    Notes = ReadNullableString(reader, "Notes"),
                    CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                    UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt")),
                    FamilyKey = ReadNullableString(reader, "FamilyKey"),
                    DecisionStatus = ReadNullableString(reader, "DecisionStatus"),
                    DecisionType = ReadNullableString(reader, "DecisionType"),
                    SelectedTourId = ReadNullableInt(reader, "SelectedTourId"),
                    SelectedMasterTourName = ReadNullableString(reader, "SelectedMasterTourName"),
                    SelectedMasterTourNameMobile = ReadNullableString(reader, "SelectedMasterTourNameMobile"),
                    SelectedTourDuration = ReadNullableString(reader, "SelectedTourDuration"),
                    SelectedMeetingPlace = ReadNullableString(reader, "SelectedMeetingPlace"),
                    VendorLinkStatus = ReadNullableString(reader, "VendorLinkStatus"),
                    VendorScheduleLinkStatus = ReadNullableString(reader, "VendorScheduleLinkStatus"),
                    ReviewLinkStatus = ReadNullableString(reader, "ReviewLinkStatus"),
                    SelectedVendorLink = ReadNullableString(reader, "SelectedVendorLink"),
                    SelectedVendorScheduleLink = ReadNullableString(reader, "SelectedVendorScheduleLink"),
                    SelectedReviewLink = ReadNullableString(reader, "SelectedReviewLink"),
                    IsAliasSelected = ReadNullableBool(reader, "IsAliasSelected"),
                    HasExactAliasMatch = reader.GetBoolean(reader.GetOrdinal("HasExactAliasMatch"))
                });
            }

            return rows;
        }

        private static TourSetupQaFamilyCard BuildFamilyCard(string familyKey, List<StagingFamilyRow> rows)
        {
            var latest = rows.OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.Id).First();
            var selectedTourId = latest.SelectedTourId ?? latest.ProposedTourId;
            var selectedMaster = NormalizeOptionalText(latest.SelectedMasterTourName) ?? NormalizeOptionalText(latest.ProposedMasterTourName);
            var selectedDuration = NormalizeOptionalText(latest.SelectedTourDuration);
            var selectedMeetingPlace = NormalizeOptionalText(latest.SelectedMeetingPlace) ?? NormalizeOptionalText(latest.MeetingPlaceExpected) ?? NormalizeOptionalText(latest.MeetingPlaceProposed);
            var decisionStatus = NormalizeDecisionStatus(latest.DecisionStatus);

            var familyLabel = selectedMaster
                ?? rows.OrderByDescending(x => x.RecordCount).Select(x => NormalizeOptionalText(x.RawTourName)).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                ?? familyKey;

            return new TourSetupQaFamilyCard
            {
                FamilyKey = familyKey,
                FamilyLabel = familyLabel,
                DecisionStatus = decisionStatus,
                ManagerStateLabel = ResolveManagerStateLabel(rows, decisionStatus, selectedTourId, selectedMaster, selectedMeetingPlace),
                VariantCount = rows.Count,
                TotalSourceRows = rows.Sum(x => x.RecordCount),
                SelectedTourId = selectedTourId,
                SelectedMasterTourName = selectedMaster,
                SelectedTourDuration = selectedDuration,
                SelectedMeetingPlace = selectedMeetingPlace,
                UpdatedAtUtc = rows.Max(x => x.UpdatedAt)
            };
        }

        private static string ResolveManagerStateLabel(
            List<StagingFamilyRow> rows,
            string decisionStatus,
            int? selectedTourId,
            string? selectedMaster,
            string? selectedMeetingPlace)
        {
            if (string.Equals(decisionStatus, "Approved", StringComparison.OrdinalIgnoreCase))
            {
                return "Approved";
            }

            if (!selectedTourId.HasValue && string.IsNullOrWhiteSpace(selectedMaster))
            {
                return "Needs Tour Decision";
            }

            var aliasPending = rows.Any(row =>
            {
                if (!string.Equals(row.SourceType, "IncomingEmail", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(row.SourceType, "PipeList", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return !(row.IsAliasSelected ?? row.HasExactAliasMatch);
            });
            if (aliasPending)
            {
                return "Needs Alias Confirmation";
            }

            if (string.IsNullOrWhiteSpace(selectedMeetingPlace)
                || IsPlaceholderValue(selectedMeetingPlace))
            {
                return "Needs Meeting Place Confirmation";
            }

            return "Ready to Approve";
        }

        private static string MapManagerFilter(string? statusFilter)
        {
            var normalized = NormalizeOptionalText(statusFilter);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "All";
            }

            if (normalized.Equals("ready", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("ready to approve", StringComparison.OrdinalIgnoreCase))
            {
                return "Ready";
            }

            if (normalized.Equals("approved", StringComparison.OrdinalIgnoreCase))
            {
                return "Approved";
            }

            if (normalized.Contains("need", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("decision", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("alias", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("meeting", StringComparison.OrdinalIgnoreCase))
            {
                return "NeedsDecision";
            }

            return "All";
        }

        private static bool MatchesManagerFilter(TourSetupQaFamilyCard card, string filter)
        {
            return filter switch
            {
                "Ready" => string.Equals(card.ManagerStateLabel, "Ready to Approve", StringComparison.OrdinalIgnoreCase),
                "Approved" => string.Equals(card.ManagerStateLabel, "Approved", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(card.DecisionStatus, "Approved", StringComparison.OrdinalIgnoreCase),
                "NeedsDecision" => card.ManagerStateLabel.StartsWith("Needs", StringComparison.OrdinalIgnoreCase),
                _ => true
            };
        }

        private static string ComputeFamilyKey(StagingFamilyRow row)
        {
            var preferred = NormalizeOptionalText(row.FamilyKey)
                ?? NormalizeOptionalText(row.SelectedMasterTourName)
                ?? NormalizeOptionalText(row.ProposedMasterTourName)
                ?? NormalizeOptionalText(row.NormalizedTourKey)
                ?? NormalizeOptionalText(row.RawTourName);

            if (string.IsNullOrWhiteSpace(preferred))
            {
                return $"family-{row.Id}";
            }

            var normalized = NormalizeFamilyKey(preferred);
            return string.IsNullOrWhiteSpace(normalized) ? $"family-{row.Id}" : normalized;
        }

        private static string NormalizeFamilyKey(string value)
        {
            var text = value.Trim().ToLowerInvariant();
            text = text.Replace("&amp;", " and ", StringComparison.OrdinalIgnoreCase);
            text = text.Replace("&", " and ");
            text = text.Replace("tour reservation", " ", StringComparison.OrdinalIgnoreCase);
            text = text.Replace("reservation", " ", StringComparison.OrdinalIgnoreCase);
            text = text.Replace("tour!", "tour", StringComparison.OrdinalIgnoreCase);
            text = text.Replace("tour.", "tour", StringComparison.OrdinalIgnoreCase);
            text = Regex.Replace(text, "[^a-z0-9/ ]", " ", RegexOptions.CultureInvariant);
            text = Regex.Replace(text, "\\s+", " ", RegexOptions.CultureInvariant).Trim();
            return text;
        }

        private async Task<List<ActiveTourRow>> LoadActiveToursAsync(SqlConnection conn, CancellationToken ct)
        {
            const string sql = @"
SELECT
    t.Id,
    t.TourName,
    t.MasterTourName,
    t.MasterTourNameDesktop,
    t.MasterTourNameMobile,
    t.Duration,
    t.MeetingPlace,
    t.VendorLink,
    t.VendorScheduleLink,
    t.ReviewLink
FROM dbo.Tours t
WHERE t.IsActive = 1
ORDER BY
    t.MasterTourName,
    t.Id;";

            var tours = new List<ActiveTourRow>();
            using var cmd = new SqlCommand(sql, conn);
            using var reader = await cmd.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                tours.Add(new ActiveTourRow
                {
                    TourId = reader.GetInt32(reader.GetOrdinal("Id")),
                    TourName = reader.GetString(reader.GetOrdinal("TourName")),
                    MasterTourName = reader.GetString(reader.GetOrdinal("MasterTourName")),
                    MasterTourNameDesktop = ReadNullableString(reader, "MasterTourNameDesktop"),
                    MasterTourNameMobile = ReadNullableString(reader, "MasterTourNameMobile"),
                    Duration = NormalizeOptionalText(ReadNullableString(reader, "Duration")),
                    MeetingPlace = NormalizeOptionalText(ReadNullableString(reader, "MeetingPlace")),
                    VendorLink = NormalizeOptionalText(ReadNullableString(reader, "VendorLink")),
                    VendorScheduleLink = NormalizeOptionalText(ReadNullableString(reader, "VendorScheduleLink")),
                    ReviewLink = NormalizeOptionalText(ReadNullableString(reader, "ReviewLink"))
                });
            }

            return tours;
        }

        private async Task<ActiveTourRow?> LoadSingleTourAsync(SqlConnection conn, int tourId, CancellationToken ct)
        {
            const string sql = @"
SELECT
    t.Id,
    t.TourName,
    t.MasterTourName,
    t.MasterTourNameDesktop,
    t.MasterTourNameMobile,
    t.Duration,
    t.MeetingPlace,
    t.VendorLink,
    t.VendorScheduleLink,
    t.ReviewLink
FROM dbo.Tours t
WHERE t.IsActive = 1
  AND t.Id = @TourId;";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@TourId", SqlDbType.Int) { Value = tourId });
            using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                return null;
            }

            return new ActiveTourRow
            {
                TourId = reader.GetInt32(reader.GetOrdinal("Id")),
                TourName = reader.GetString(reader.GetOrdinal("TourName")),
                MasterTourName = reader.GetString(reader.GetOrdinal("MasterTourName")),
                MasterTourNameDesktop = ReadNullableString(reader, "MasterTourNameDesktop"),
                MasterTourNameMobile = ReadNullableString(reader, "MasterTourNameMobile"),
                Duration = NormalizeOptionalText(ReadNullableString(reader, "Duration")),
                MeetingPlace = NormalizeOptionalText(ReadNullableString(reader, "MeetingPlace")),
                VendorLink = NormalizeOptionalText(ReadNullableString(reader, "VendorLink")),
                VendorScheduleLink = NormalizeOptionalText(ReadNullableString(reader, "VendorScheduleLink")),
                ReviewLink = NormalizeOptionalText(ReadNullableString(reader, "ReviewLink"))
            };
        }

        private static TourSetupQaTourCandidate BuildCandidate(ActiveTourRow tour, IReadOnlyCollection<string> familyTokens, int? selectedTourId)
        {
            if (selectedTourId.HasValue && tour.TourId == selectedTourId.Value)
            {
                return new TourSetupQaTourCandidate
                {
                    TourId = tour.TourId,
                    TourName = tour.TourName,
                    MasterTourName = tour.MasterTourName,
                    MasterTourNameDesktop = tour.MasterTourNameDesktop,
                    MasterTourNameMobile = tour.MasterTourNameMobile,
                    Duration = tour.Duration,
                    MeetingPlace = tour.MeetingPlace,
                    VendorLink = tour.VendorLink,
                    VendorScheduleLink = tour.VendorScheduleLink,
                    ReviewLink = tour.ReviewLink,
                    Score = 100m,
                    MatchReason = "Currently selected"
                };
            }

            var candidateTokens = TokenizeForMatch(tour.MasterTourName)
                .Union(TokenizeForMatch(tour.TourName), StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            decimal bestScore = 0m;
            var bestReason = "Name similarity";

            foreach (var familyTokenRaw in familyTokens)
            {
                var familyToken = NormalizeOptionalText(familyTokenRaw);
                if (string.IsNullOrWhiteSpace(familyToken))
                {
                    continue;
                }

                var sourceTokens = TokenizeForMatch(familyToken);
                if (sourceTokens.Count == 0)
                {
                    continue;
                }

                var overlap = sourceTokens.Intersect(candidateTokens, StringComparer.OrdinalIgnoreCase).Count();
                var union = sourceTokens.Union(candidateTokens, StringComparer.OrdinalIgnoreCase).Count();
                var score = union == 0 ? 0m : Math.Round(((decimal)overlap / union) * 80m, 2);

                if (ContainsIgnoreCase(tour.MasterTourName, familyToken))
                {
                    score += 15m;
                }

                if (ContainsIgnoreCase(tour.TourName, familyToken))
                {
                    score += 10m;
                }

                if (overlap >= 3)
                {
                    score += 8m;
                }

                if (score > bestScore)
                {
                    bestScore = Math.Min(99m, score);
                    bestReason = overlap >= 3 ? "Strong keyword overlap" : "Name similarity";
                }
            }

            return new TourSetupQaTourCandidate
            {
                TourId = tour.TourId,
                TourName = tour.TourName,
                MasterTourName = tour.MasterTourName,
                MasterTourNameDesktop = tour.MasterTourNameDesktop,
                MasterTourNameMobile = tour.MasterTourNameMobile,
                Duration = tour.Duration,
                MeetingPlace = tour.MeetingPlace,
                VendorLink = tour.VendorLink,
                VendorScheduleLink = tour.VendorScheduleLink,
                ReviewLink = tour.ReviewLink,
                Score = bestScore,
                MatchReason = bestReason
            };
        }

        private HintCache GetHintCache()
        {
            lock (_hintsLock)
            {
                if (_hintCache is not null)
                {
                    return _hintCache;
                }

                _hintCache = BuildHintCache();
                return _hintCache;
            }
        }

        private HintCache BuildHintCache()
        {
            var durations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var places = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var candidates = new[]
            {
                Path.Combine(_environment.ContentRootPath, "Email_Docs", "3_1tours from emails and db and vendors.txt"),
                Path.Combine(_environment.ContentRootPath, "Email_Docs", "tours.txt")
            };

            foreach (var path in candidates)
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                foreach (var line in File.ReadLines(path))
                {
                    var trimmed = NormalizeOptionalText(line);
                    if (string.IsNullOrWhiteSpace(trimmed))
                    {
                        continue;
                    }

                    var segments = trimmed.Split('|', StringSplitOptions.None);
                    if (!trimmed.StartsWith("tourname|", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var segment in segments)
                        {
                            var normalizedSegmentDuration = NormalizeDurationHint(segment);
                            if (!string.IsNullOrWhiteSpace(normalizedSegmentDuration))
                            {
                                durations.Add(normalizedSegmentDuration);
                            }
                        }
                    }

                    foreach (Match match in DurationHintRegex.Matches(trimmed))
                    {
                        var normalized = NormalizeDurationHint(match.Value);
                        if (!string.IsNullOrWhiteSpace(normalized))
                        {
                            durations.Add(normalized);
                        }
                    }

                    var place = ExtractMeetingPlaceHint(trimmed);
                    if (!string.IsNullOrWhiteSpace(place))
                    {
                        places.Add(place);
                    }
                }
            }

            return new HintCache
            {
                DurationHints = durations.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                MeetingPlaceHints = places.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()
            };
        }

        private static string? ExtractMeetingPlaceHint(string line)
        {
            var lowered = line.ToLowerInvariant();
            if (!lowered.Contains("will see you")
                && !lowered.Contains("will meet you")
                && !lowered.Contains("see you at")
                && !lowered.Contains("meet you at"))
            {
                return null;
            }

            var match = Regex.Match(line, @"(?:see|meet)\s+you\s+(?:at|outside|in front of)\s+(?<place>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                return null;
            }

            var place = match.Groups["place"].Value;
            place = Regex.Replace(place, @"\bat\s+\{time\}.*$", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            place = Regex.Replace(place, @"\bat\s+\d{1,2}:\d{2}\s*(am|pm)?\b.*$", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            place = place.Trim().TrimEnd('.').Trim();
            return place.Length < 6 ? null : place;
        }

        private static string? NormalizeDurationHint(string? input)
        {
            var value = NormalizeOptionalText(input);
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var compact = Regex.Replace(value, @"\s+", " ", RegexOptions.CultureInvariant).Trim();
            var match = DurationHintRegex.Match(compact);
            if (!match.Success)
            {
                return null;
            }

            if (!decimal.TryParse(match.Groups["num"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
                || number <= 0)
            {
                return null;
            }

            var unit = match.Groups["unit"].Value.ToLowerInvariant();
            if (unit.StartsWith("h", StringComparison.Ordinal))
            {
                var suffix = number == 1m ? "hour" : "hours";
                return $"{number.ToString("0.##", CultureInfo.InvariantCulture)} {suffix}";
            }

            if (unit.StartsWith("m", StringComparison.Ordinal))
            {
                var minuteValue = decimal.Truncate(number) == number
                    ? number.ToString("0", CultureInfo.InvariantCulture)
                    : number.ToString("0.##", CultureInfo.InvariantCulture);
                return $"{minuteValue} min";
            }

            return null;
        }

        private async Task<List<TourSetupQaVendorLinkSnapshot>> LoadVendorLinkSnapshotsAsync(SqlConnection conn, int tourId, CancellationToken ct)
        {
            const string sql = @"
SELECT TOP (200)
    tl.Id,
    tl.Vendor,
    tl.TourName,
    tl.ProductId,
    tl.TourLink,
    tl.ReviewLink,
    tl.TourDay,
    tl.TourTime
FROM dbo.TourLinks tl
WHERE tl.TourId = @TourId
   OR EXISTS
      (
          SELECT 1
          FROM dbo.TourNameMappings m
          WHERE m.IsActive = 1
            AND m.TourId = @TourId
            AND LOWER(LTRIM(RTRIM(ISNULL(m.IncomingTourName, N'')))) = LOWER(LTRIM(RTRIM(ISNULL(tl.TourName, N''))))
      )
ORDER BY
    tl.UpdatedAt DESC,
    tl.Id DESC;";

            var items = new List<TourSetupQaVendorLinkSnapshot>();
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@TourId", SqlDbType.Int) { Value = tourId });
            using var reader = await cmd.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                items.Add(new TourSetupQaVendorLinkSnapshot
                {
                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                    Vendor = reader.GetString(reader.GetOrdinal("Vendor")),
                    TourName = reader.GetString(reader.GetOrdinal("TourName")),
                    ProductId = ReadNullableString(reader, "ProductId"),
                    TourLink = ReadNullableString(reader, "TourLink"),
                    ReviewLink = ReadNullableString(reader, "ReviewLink"),
                    TourDay = ReadNullableInt(reader, "TourDay"),
                    TourTime = NormalizeTime24h(ReadNullableString(reader, "TourTime"))
                });
            }

            return items;
        }

        private static string? NormalizeOptionalText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Trim();
        }

        private static string NormalizeDecisionType(string? decisionType, int? selectedTourId)
        {
            var normalized = NormalizeOptionalText(decisionType);
            if (string.Equals(normalized, "CreateNewTour", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "NewTour", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "New", StringComparison.OrdinalIgnoreCase))
            {
                return "CreateNewTour";
            }

            if (selectedTourId.HasValue && selectedTourId.Value > 0)
            {
                return "SameTour";
            }

            if (string.Equals(normalized, "SameTour", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "ExistingTour", StringComparison.OrdinalIgnoreCase))
            {
                return "SameTour";
            }

            return string.IsNullOrWhiteSpace(normalized) ? "SameTour" : normalized;
        }

        private static string NormalizeLinkStatus(string? linkStatus)
        {
            var normalized = NormalizeOptionalText(linkStatus);
            if (string.Equals(normalized, "Needs Update", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "NeedsUpdate", StringComparison.OrdinalIgnoreCase))
            {
                return "Needs Update";
            }

            if (string.Equals(normalized, "N/A", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "NA", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "Not Applicable", StringComparison.OrdinalIgnoreCase))
            {
                return "N/A";
            }

            return "Confirmed";
        }

        private static string NormalizeDecisionStatus(string? decisionStatus)
        {
            var normalized = NormalizeOptionalText(decisionStatus);
            if (string.Equals(normalized, "Approved", StringComparison.OrdinalIgnoreCase))
            {
                return "Approved";
            }

            if (string.Equals(normalized, "Ready", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "ReadyToApprove", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "Ready to Approve", StringComparison.OrdinalIgnoreCase))
            {
                return "Ready";
            }

            return "Pending";
        }

        private static string? NormalizeTime24h(string? input)
        {
            var value = NormalizeOptionalText(input);
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (TimeSpan.TryParseExact(value, new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out var span))
            {
                return span.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
            }

            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date))
            {
                return date.ToString("HH:mm", CultureInfo.InvariantCulture);
            }

            var digitsOnly = Regex.Match(value, @"^(?<h>\d{1,2})(?<m>\d{2})$");
            if (digitsOnly.Success
                && int.TryParse(digitsOnly.Groups["h"].Value, out var simpleHour)
                && int.TryParse(digitsOnly.Groups["m"].Value, out var simpleMinute)
                && simpleHour >= 0 && simpleHour <= 23
                && simpleMinute >= 0 && simpleMinute <= 59)
            {
                return $"{simpleHour:00}:{simpleMinute:00}";
            }

            var match = TimeHintRegex.Match(value);
            if (!match.Success)
            {
                return value;
            }

            if (!int.TryParse(match.Groups["h"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var hour)
                || !int.TryParse(match.Groups["m"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minute))
            {
                return value;
            }

            var ampm = NormalizeOptionalText(match.Groups["ampm"].Value);
            if (!string.IsNullOrWhiteSpace(ampm))
            {
                if (ampm.Equals("PM", StringComparison.OrdinalIgnoreCase) && hour < 12)
                {
                    hour += 12;
                }
                else if (ampm.Equals("AM", StringComparison.OrdinalIgnoreCase) && hour == 12)
                {
                    hour = 0;
                }
            }

            return (hour < 0 || hour > 23 || minute < 0 || minute > 59) ? value : $"{hour:00}:{minute:00}";
        }

        private static bool ContainsIgnoreCase(string? source, string? value)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsPlaceholderValue(string? value)
        {
            var normalized = NormalizeOptionalText(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return true;
            }

            var lowered = normalized.ToLowerInvariant();
            return lowered is "tbd" or "tba" or "na" or "n/a";
        }

        private static HashSet<string> TokenizeForMatch(string? value)
        {
            var normalized = NormalizeOptionalText(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            normalized = normalized.ToLowerInvariant();
            normalized = normalized.Replace("&amp;", " and ", StringComparison.OrdinalIgnoreCase);
            normalized = normalized.Replace("&", " and ");
            normalized = Regex.Replace(normalized, "[^a-z0-9/ ]", " ", RegexOptions.CultureInvariant);
            normalized = Regex.Replace(normalized, "\\s+", " ", RegexOptions.CultureInvariant).Trim();

            var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "the",
                "tour",
                "of",
                "and",
                "new",
                "york",
                "walking",
                "free",
                "reservation",
                "from"
            };

            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var token in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (token.Length <= 1 || stopWords.Contains(token))
                {
                    continue;
                }

                result.Add(token);
            }

            return result;
        }

        private static string EscapeSqlLiteral(string value)
        {
            return value.Replace("'", "''", StringComparison.Ordinal);
        }

        private static string ToSqlNVarCharLiteral(string? value)
        {
            var normalized = NormalizeOptionalText(value);
            return string.IsNullOrWhiteSpace(normalized) ? "NULL" : $"N'{EscapeSqlLiteral(normalized)}'";
        }

        private static string? ReadNullableString(SqlDataReader reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }

        private static int? ReadNullableInt(SqlDataReader reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
        }

        private static bool? ReadNullableBool(SqlDataReader reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? null : reader.GetBoolean(ordinal);
        }

        private static DateTime? ReadNullableDate(SqlDataReader reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
        }
    }
}
