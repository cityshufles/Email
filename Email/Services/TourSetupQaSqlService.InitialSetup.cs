using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Email.Services
{
    public sealed partial class TourSetupQaSqlService
    {
        public async Task<TourSetupQaInitialSetupSummary> GetInitialSetupSummaryAsync(CancellationToken ct = default)
        {
            using var conn = _connectionFactory.CreateOpenConnection();
            var runId = await ResolveActiveRunIdAsync(conn, ct);
            var rows = await LoadFamilyRowsAsync(conn, runId, ct);

            var families = rows
                .GroupBy(r => ComputeFamilyKey(r))
                .Select(group => BuildFamilyCard(group.Key, group.ToList()))
                .ToList();

            return new TourSetupQaInitialSetupSummary
            {
                ActiveRunId = runId,
                FamilyCount = families.Count,
                VariantCount = rows.Count,
                NeedsDecisionCount = families.Count(x =>
                    string.Equals(x.ManagerStateLabel, "Needs Tour Decision", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.ManagerStateLabel, "Needs Alias Confirmation", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.ManagerStateLabel, "Needs Meeting Place Confirmation", StringComparison.OrdinalIgnoreCase)),
                ReadyCount = families.Count(x => string.Equals(x.ManagerStateLabel, "Ready to Approve", StringComparison.OrdinalIgnoreCase)),
                ApprovedCount = families.Count(x => string.Equals(x.ManagerStateLabel, "Approved", StringComparison.OrdinalIgnoreCase)),
                LastUpdatedAtUtc = rows.Count == 0 ? null : rows.Max(x => x.UpdatedAt)
            };
        }

        public async Task<List<TourSetupQaFamilyCard>> GetFamilyCardsAsync(
            string? statusFilter = null,
            string? search = null,
            CancellationToken ct = default)
        {
            using var conn = _connectionFactory.CreateOpenConnection();
            var runId = await ResolveActiveRunIdAsync(conn, ct);
            var rows = await LoadFamilyRowsAsync(conn, runId, ct);

            var cards = rows
                .GroupBy(r => ComputeFamilyKey(r))
                .Select(group => BuildFamilyCard(group.Key, group.ToList()))
                .OrderBy(x => x.ManagerStateLabel, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(x => x.TotalSourceRows)
                .ThenBy(x => x.FamilyLabel, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var normalizedFilter = MapManagerFilter(statusFilter);
            if (!string.Equals(normalizedFilter, "All", StringComparison.OrdinalIgnoreCase))
            {
                cards = cards
                    .Where(card => MatchesManagerFilter(card, normalizedFilter))
                    .ToList();
            }

            var normalizedSearch = NormalizeOptionalText(search);
            if (!string.IsNullOrWhiteSpace(normalizedSearch))
            {
                cards = cards
                    .Where(card =>
                        ContainsIgnoreCase(card.FamilyLabel, normalizedSearch) ||
                        ContainsIgnoreCase(card.SelectedMasterTourName, normalizedSearch) ||
                        rows.Any(r =>
                            string.Equals(ComputeFamilyKey(r), card.FamilyKey, StringComparison.OrdinalIgnoreCase) &&
                            (ContainsIgnoreCase(r.RawTourName, normalizedSearch) || ContainsIgnoreCase(r.VendorName, normalizedSearch))))
                    .ToList();
            }

            return cards;
        }

        public async Task<TourSetupQaFamilyDetail?> GetFamilyDetailAsync(string familyKey, CancellationToken ct = default)
        {
            var desiredFamilyKey = NormalizeOptionalText(familyKey);
            if (string.IsNullOrWhiteSpace(desiredFamilyKey))
            {
                return null;
            }

            using var conn = _connectionFactory.CreateOpenConnection();
            var runId = await ResolveActiveRunIdAsync(conn, ct);
            var allRows = await LoadFamilyRowsAsync(conn, runId, ct);
            var familyRows = allRows
                .Where(x => string.Equals(ComputeFamilyKey(x), desiredFamilyKey, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.UpdatedAt)
                .ThenByDescending(x => x.Id)
                .ToList();

            if (familyRows.Count == 0)
            {
                return null;
            }

            var card = BuildFamilyCard(desiredFamilyKey, familyRows);
            var latest = familyRows[0];

            var detail = new TourSetupQaFamilyDetail
            {
                FamilyKey = card.FamilyKey,
                FamilyLabel = card.FamilyLabel,
                ManagerStateLabel = card.ManagerStateLabel,
                DecisionStatus = card.DecisionStatus,
                DecisionType = NormalizeDecisionType(latest.DecisionType, latest.SelectedTourId),
                SelectedTourId = latest.SelectedTourId,
                SelectedMasterTourName = NormalizeOptionalText(latest.SelectedMasterTourName) ?? NormalizeOptionalText(latest.ProposedMasterTourName),
                SelectedMasterTourNameMobile = NormalizeOptionalText(latest.SelectedMasterTourNameMobile),
                SelectedTourDuration = NormalizeOptionalText(latest.SelectedTourDuration),
                SelectedMeetingPlace = NormalizeOptionalText(latest.SelectedMeetingPlace) ?? NormalizeOptionalText(latest.MeetingPlaceExpected) ?? NormalizeOptionalText(latest.MeetingPlaceProposed),
                VendorLinkStatus = NormalizeLinkStatus(latest.VendorLinkStatus),
                VendorScheduleLinkStatus = NormalizeLinkStatus(latest.VendorScheduleLinkStatus),
                ReviewLinkStatus = NormalizeLinkStatus(latest.ReviewLinkStatus),
                SelectedVendorLink = NormalizeOptionalText(latest.SelectedVendorLink),
                SelectedVendorScheduleLink = NormalizeOptionalText(latest.SelectedVendorScheduleLink),
                SelectedReviewLink = NormalizeOptionalText(latest.SelectedReviewLink),
                Notes = NormalizeOptionalText(latest.Notes)
            };

            var tourCandidates = await LoadActiveToursAsync(conn, ct);
            detail.TourOptions = tourCandidates
                .Select(t => new TourSetupQaTourOption
                {
                    TourId = t.TourId,
                    Label = $"{t.MasterTourName} (ID {t.TourId})"
                })
                .OrderBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var familyTokens = familyRows
                .Select(x => x.RawTourName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Append(card.FamilyLabel)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            detail.Candidates = tourCandidates
                .Select(t => BuildCandidate(t, familyTokens, detail.SelectedTourId))
                .Where(x => x.Score > 0m || x.TourId == detail.SelectedTourId)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.MasterTourName, StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList();

            detail.Variants = familyRows
                .OrderByDescending(x => x.RecordCount)
                .ThenBy(x => x.VendorName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.RawTourName, StringComparer.OrdinalIgnoreCase)
                .Select(x => new TourSetupQaFamilyVariant
                {
                    StagingId = x.Id,
                    SourceType = x.SourceType,
                    VendorName = x.VendorName,
                    RawTourName = x.RawTourName,
                    RecordCount = x.RecordCount,
                    IsAliasSelected = x.IsAliasSelected ?? x.HasExactAliasMatch,
                    HasExactAliasMatch = x.HasExactAliasMatch,
                    GapTypeComputed = x.GapTypeComputed,
                    ProposedTourId = x.ProposedTourId,
                    ProposedMasterTourName = x.ProposedMasterTourName,
                    ProposedMeetingPlace = x.MeetingPlaceProposed,
                    ProposedMeetingTime = x.MeetingTimeProposed
                })
                .ToList();

            var hints = GetHintCache();

            detail.MasterOptions = tourCandidates
                .Select(x => NormalizeOptionalText(x.MasterTourName))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList()!;

            detail.MobileOptions = tourCandidates
                .Select(x => NormalizeOptionalText(x.MasterTourNameMobile))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList()!;

            detail.TourDurationOptions = familyRows
                .Select(x => NormalizeOptionalText(x.SelectedTourDuration))
                .Concat(tourCandidates.Select(x => NormalizeOptionalText(x.Duration)))
                .Concat(hints.DurationHints)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList()!;

            detail.MeetingPlaceOptions = familyRows
                .Select(x => NormalizeOptionalText(x.SelectedMeetingPlace))
                .Concat(familyRows.Select(x => NormalizeOptionalText(x.MeetingPlaceExpected)))
                .Concat(familyRows.Select(x => NormalizeOptionalText(x.MeetingPlaceProposed)))
                .Concat(tourCandidates.Select(x => NormalizeOptionalText(x.MeetingPlace)))
                .Concat(hints.MeetingPlaceHints)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList()!;

            var selectedTourId = detail.SelectedTourId ?? familyRows.Select(x => x.SelectedTourId ?? x.ProposedTourId).FirstOrDefault(x => x.HasValue);
            if (selectedTourId.HasValue)
            {
                detail.VendorLinkSnapshots = await LoadVendorLinkSnapshotsAsync(conn, selectedTourId.Value, ct);
            }

            return detail;
        }

        public async Task<bool> SaveFamilyDecisionAsync(
            TourSetupQaFamilyDecisionInput input,
            string reviewedBy,
            CancellationToken ct = default)
        {
            if (input is null)
            {
                return false;
            }

            var familyKey = NormalizeOptionalText(input.FamilyKey);
            if (string.IsNullOrWhiteSpace(familyKey))
            {
                return false;
            }

            using var conn = _connectionFactory.CreateOpenConnection();
            var runId = await ResolveActiveRunIdAsync(conn, ct);
            var familyRows = await LoadFamilyRowsAsync(conn, runId, ct);
            var targets = familyRows
                .Where(x => string.Equals(ComputeFamilyKey(x), familyKey, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (targets.Count == 0)
            {
                return false;
            }

            var targetIds = targets.Select(x => x.Id).Distinct().ToList();
            if (targetIds.Count == 0)
            {
                return false;
            }

            var safeDecisionType = NormalizeDecisionType(input.DecisionType, input.SelectedTourId);
            var safeMaster = NormalizeOptionalText(input.SelectedMasterTourName);
            var safeMobile = NormalizeOptionalText(input.SelectedMasterTourNameMobile);
            var safeDuration = NormalizeOptionalText(input.SelectedTourDuration);
            var safeMeetingPlace = NormalizeOptionalText(input.SelectedMeetingPlace);
            var safeVendorLink = NormalizeOptionalText(input.SelectedVendorLink);
            var safeVendorScheduleLink = NormalizeOptionalText(input.SelectedVendorScheduleLink);
            var safeReviewLink = NormalizeOptionalText(input.SelectedReviewLink);
            var safeVendorLinkStatus = NormalizeLinkStatus(input.VendorLinkStatus);
            var safeVendorScheduleLinkStatus = NormalizeLinkStatus(input.VendorScheduleLinkStatus);
            var safeReviewLinkStatus = NormalizeLinkStatus(input.ReviewLinkStatus);
            var safeNotes = NormalizeOptionalText(input.Notes);
            var safeReviewedBy = string.IsNullOrWhiteSpace(reviewedBy) ? "unknown" : reviewedBy.Trim();

            if (input.SelectedTourId.HasValue && input.SelectedTourId.Value > 0)
            {
                var selectedTour = await LoadSingleTourAsync(conn, input.SelectedTourId.Value, ct);
                if (selectedTour is not null)
                {
                    safeMaster ??= selectedTour.MasterTourName;
                    safeMobile ??= NormalizeOptionalText(selectedTour.MasterTourNameMobile);
                    safeDuration ??= NormalizeOptionalText(selectedTour.Duration);
                    safeMeetingPlace ??= NormalizeOptionalText(selectedTour.MeetingPlace);
                    safeVendorLink ??= NormalizeOptionalText(selectedTour.VendorLink);
                    safeVendorScheduleLink ??= NormalizeOptionalText(selectedTour.VendorScheduleLink);
                    safeReviewLink ??= NormalizeOptionalText(selectedTour.ReviewLink);
                }
            }

            var selectedAliasMap = input.VariantSelections
                .GroupBy(x => x.StagingId)
                .ToDictionary(g => g.Key, g => g.Last().IsAliasSelected);

            var aliasPending = targets.Any(row =>
            {
                if (!string.Equals(row.SourceType, "IncomingEmail", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(row.SourceType, "PipeList", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (selectedAliasMap.TryGetValue(row.Id, out var picked))
                {
                    return !picked;
                }

                return !(row.IsAliasSelected ?? row.HasExactAliasMatch);
            });

            var needsTour = (input.SelectedTourId ?? 0) <= 0 && string.IsNullOrWhiteSpace(safeMaster);
            var needsMeetingPlace = string.IsNullOrWhiteSpace(safeMeetingPlace) || IsPlaceholderValue(safeMeetingPlace);
            var decisionStatus = (needsTour || aliasPending || needsMeetingPlace) ? "Pending" : "Ready";

            using var tx = conn.BeginTransaction();
            try
            {
                const string updateSql = @"
UPDATE dbo.TourSetupQaStaging
SET
    DecisionType = @DecisionType,
    SelectedTourId = @SelectedTourId,
    SelectedMasterTourName = @SelectedMasterTourName,
    SelectedMasterTourNameMobile = @SelectedMasterTourNameMobile,
    SelectedTourDuration = @SelectedTourDuration,
    SelectedMeetingPlace = @SelectedMeetingPlace,
    VendorLinkStatus = @VendorLinkStatus,
    VendorScheduleLinkStatus = @VendorScheduleLinkStatus,
    ReviewLinkStatus = @ReviewLinkStatus,
    SelectedVendorLink = @SelectedVendorLink,
    SelectedVendorScheduleLink = @SelectedVendorScheduleLink,
    SelectedReviewLink = @SelectedReviewLink,
    Notes = @Notes,
    DecisionStatus = @DecisionStatus,
    ReviewedBy = @ReviewedBy,
    ReviewedAt = SYSUTCDATETIME(),
    UpdatedAt = SYSUTCDATETIME()
WHERE BatchId = @BatchId
  AND Id = @Id;";

                var updatedRows = 0;
                foreach (var targetId in targetIds)
                {
                    using var cmd = new SqlCommand(updateSql, conn, tx);
                    cmd.Parameters.Add(new SqlParameter("@DecisionType", SqlDbType.NVarChar, 30) { Value = safeDecisionType });
                    cmd.Parameters.Add(new SqlParameter("@SelectedTourId", SqlDbType.Int) { Value = (object?)input.SelectedTourId ?? DBNull.Value });
                    cmd.Parameters.Add(new SqlParameter("@SelectedMasterTourName", SqlDbType.NVarChar, 255) { Value = (object?)safeMaster ?? DBNull.Value });
                    cmd.Parameters.Add(new SqlParameter("@SelectedMasterTourNameMobile", SqlDbType.NVarChar, 50) { Value = (object?)safeMobile ?? DBNull.Value });
                    cmd.Parameters.Add(new SqlParameter("@SelectedTourDuration", SqlDbType.NVarChar, 50) { Value = (object?)safeDuration ?? DBNull.Value });
                    cmd.Parameters.Add(new SqlParameter("@SelectedMeetingPlace", SqlDbType.NVarChar, 255) { Value = (object?)safeMeetingPlace ?? DBNull.Value });
                    cmd.Parameters.Add(new SqlParameter("@VendorLinkStatus", SqlDbType.NVarChar, 20) { Value = (object?)safeVendorLinkStatus ?? DBNull.Value });
                    cmd.Parameters.Add(new SqlParameter("@VendorScheduleLinkStatus", SqlDbType.NVarChar, 20) { Value = (object?)safeVendorScheduleLinkStatus ?? DBNull.Value });
                    cmd.Parameters.Add(new SqlParameter("@ReviewLinkStatus", SqlDbType.NVarChar, 20) { Value = (object?)safeReviewLinkStatus ?? DBNull.Value });
                    cmd.Parameters.Add(new SqlParameter("@SelectedVendorLink", SqlDbType.NVarChar, 1000) { Value = (object?)safeVendorLink ?? DBNull.Value });
                    cmd.Parameters.Add(new SqlParameter("@SelectedVendorScheduleLink", SqlDbType.NVarChar, 1000) { Value = (object?)safeVendorScheduleLink ?? DBNull.Value });
                    cmd.Parameters.Add(new SqlParameter("@SelectedReviewLink", SqlDbType.NVarChar, 1000) { Value = (object?)safeReviewLink ?? DBNull.Value });
                    cmd.Parameters.Add(new SqlParameter("@Notes", SqlDbType.NVarChar, 2000) { Value = (object?)safeNotes ?? DBNull.Value });
                    cmd.Parameters.Add(new SqlParameter("@DecisionStatus", SqlDbType.NVarChar, 30) { Value = decisionStatus });
                    cmd.Parameters.Add(new SqlParameter("@ReviewedBy", SqlDbType.NVarChar, 128) { Value = safeReviewedBy });
                    cmd.Parameters.Add(new SqlParameter("@BatchId", SqlDbType.UniqueIdentifier) { Value = runId });
                    cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.BigInt) { Value = targetId });
                    updatedRows += await cmd.ExecuteNonQueryAsync(ct);
                }

                if (updatedRows <= 0)
                {
                    tx.Rollback();
                    return false;
                }

                const string aliasSql = @"
UPDATE dbo.TourSetupQaStaging
SET
    IsAliasSelected = @IsAliasSelected,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id;";

                foreach (var selection in selectedAliasMap)
                {
                    using var aliasCmd = new SqlCommand(aliasSql, conn, tx);
                    aliasCmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.BigInt) { Value = selection.Key });
                    aliasCmd.Parameters.Add(new SqlParameter("@IsAliasSelected", SqlDbType.Bit) { Value = selection.Value });
                    await aliasCmd.ExecuteNonQueryAsync(ct);
                }

                tx.Commit();
                return true;
            }
            catch (Exception ex)
            {
                tx.Rollback();
                Console.WriteLine($"[TourSetupQaSqlService] SaveFamilyDecisionAsync error: {ex.Message}");
                throw;
            }
        }

        public async Task<bool> ApproveFamilyAsync(string familyKey, string reviewedBy, CancellationToken ct = default)
        {
            var safeFamilyKey = NormalizeOptionalText(familyKey);
            if (string.IsNullOrWhiteSpace(safeFamilyKey))
            {
                return false;
            }

            using var conn = _connectionFactory.CreateOpenConnection();
            var runId = await ResolveActiveRunIdAsync(conn, ct);
            var rows = await LoadFamilyRowsAsync(conn, runId, ct);
            var targetIds = rows
                .Where(x => string.Equals(ComputeFamilyKey(x), safeFamilyKey, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Id)
                .Distinct()
                .ToList();

            if (targetIds.Count == 0)
            {
                return false;
            }

            var idParameters = targetIds.Select((_, index) => $"@Id{index}").ToArray();
            var sql = $@"
UPDATE dbo.TourSetupQaStaging
SET
    DecisionStatus = N'Approved',
    ReviewedBy = @ReviewedBy,
    ReviewedAt = SYSUTCDATETIME(),
    UpdatedAt = SYSUTCDATETIME()
WHERE BatchId = @BatchId
  AND Id IN ({string.Join(", ", idParameters)});";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@ReviewedBy", SqlDbType.NVarChar, 128) { Value = string.IsNullOrWhiteSpace(reviewedBy) ? "unknown" : reviewedBy.Trim() });
            cmd.Parameters.Add(new SqlParameter("@BatchId", SqlDbType.UniqueIdentifier) { Value = runId });
            for (var i = 0; i < targetIds.Count; i++)
            {
                cmd.Parameters.Add(new SqlParameter($"@Id{i}", SqlDbType.BigInt) { Value = targetIds[i] });
            }

            var updated = await cmd.ExecuteNonQueryAsync(ct);
            return updated > 0;
        }

        public async Task<TourSetupQaApplyPreview> GetApplyPreviewAsync(CancellationToken ct = default)
        {
            using var conn = _connectionFactory.CreateOpenConnection();
            var runId = await ResolveActiveRunIdAsync(conn, ct);
            var rows = await LoadFamilyRowsAsync(conn, runId, ct);

            var approvedGroups = rows
                .GroupBy(x => ComputeFamilyKey(x))
                .Select(group => new
                {
                    FamilyKey = group.Key,
                    Rows = group.ToList(),
                    Latest = group.OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.Id).First()
                })
                .Where(x => x.Rows.All(r => string.Equals(NormalizeOptionalText(r.DecisionStatus), "Approved", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var preview = new TourSetupQaApplyPreview
            {
                ActiveRunId = runId,
                FamiliesApproved = approvedGroups.Count
            };

            foreach (var family in approvedGroups)
            {
                var latest = family.Latest;
                var decisionType = NormalizeDecisionType(latest.DecisionType, latest.SelectedTourId);
                if (string.Equals(decisionType, "CreateNewTour", StringComparison.OrdinalIgnoreCase) &&
                    !latest.SelectedTourId.HasValue &&
                    !string.IsNullOrWhiteSpace(latest.SelectedMasterTourName))
                {
                    preview.ProposedTourInserts += 1;
                    preview.Actions.Add(new TourSetupQaApplyAction
                    {
                        ActionType = "ToursInsert",
                        FamilyKey = family.FamilyKey,
                        Description = $"Insert new canonical tour '{latest.SelectedMasterTourName}'.",
                        SqlPreview = $"-- INSERT preview for family '{family.FamilyKey}'{Environment.NewLine}INSERT INTO dbo.Tours (TourName, MasterTourName, MasterTourNameMobile, Duration, MeetingPlace, IsActive) VALUES (N'{EscapeSqlLiteral(latest.SelectedMasterTourName!)}', N'{EscapeSqlLiteral(latest.SelectedMasterTourName!)}', {ToSqlNVarCharLiteral(latest.SelectedMasterTourNameMobile)}, {ToSqlNVarCharLiteral(latest.SelectedTourDuration)}, {ToSqlNVarCharLiteral(latest.SelectedMeetingPlace)}, 1);"
                    });
                }
                else if (latest.SelectedTourId.HasValue)
                {
                    preview.ProposedTourUpdates += 1;
                    preview.Actions.Add(new TourSetupQaApplyAction
                    {
                        ActionType = "ToursUpdate",
                        FamilyKey = family.FamilyKey,
                        Description = $"Update Tour ID {latest.SelectedTourId.Value} master/duration/meeting place/link values.",
                        SqlPreview = $"-- UPDATE preview for family '{family.FamilyKey}'{Environment.NewLine}UPDATE dbo.Tours SET MasterTourName = {ToSqlNVarCharLiteral(latest.SelectedMasterTourName)}, MasterTourNameMobile = {ToSqlNVarCharLiteral(latest.SelectedMasterTourNameMobile)}, Duration = {ToSqlNVarCharLiteral(latest.SelectedTourDuration)}, MeetingPlace = {ToSqlNVarCharLiteral(latest.SelectedMeetingPlace)}, VendorLink = {ToSqlNVarCharLiteral(latest.SelectedVendorLink)}, VendorScheduleLink = {ToSqlNVarCharLiteral(latest.SelectedVendorScheduleLink)}, ReviewLink = {ToSqlNVarCharLiteral(latest.SelectedReviewLink)} WHERE Id = {latest.SelectedTourId.Value};"
                    });
                }

                if (latest.SelectedTourId.HasValue)
                {
                    var aliasRows = family.Rows
                        .Where(r => r.IsAliasSelected == true)
                        .ToList();

                    foreach (var alias in aliasRows)
                    {
                        preview.ProposedMappingUpserts += 1;
                        preview.Actions.Add(new TourSetupQaApplyAction
                        {
                            ActionType = "MappingUpsert",
                            FamilyKey = family.FamilyKey,
                            Description = $"Ensure alias '{alias.RawTourName}' for vendor '{alias.VendorName ?? "NULL"}' points to Tour ID {latest.SelectedTourId.Value}.",
                            SqlPreview = $"-- Mapping upsert preview{Environment.NewLine}MERGE dbo.TourNameMappings AS t USING (SELECT {latest.SelectedTourId.Value} AS TourId, N'{EscapeSqlLiteral(alias.RawTourName)}' AS IncomingTourName, {ToSqlNVarCharLiteral(alias.VendorName)} AS VendorName) AS s ON t.TourId = s.TourId AND t.IncomingTourName = s.IncomingTourName AND ((t.VendorName IS NULL AND s.VendorName IS NULL) OR t.VendorName = s.VendorName) WHEN MATCHED THEN UPDATE SET IsActive = 1, UpdatedAt = SYSUTCDATETIME() WHEN NOT MATCHED THEN INSERT (TourId, IncomingTourName, VendorName, IsActive, CreatedAt, UpdatedAt) VALUES (s.TourId, s.IncomingTourName, s.VendorName, 1, SYSUTCDATETIME(), SYSUTCDATETIME());"
                        });
                    }
                }
            }

            return preview;
        }
    }
}
