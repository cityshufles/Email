using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Email.Services.GmailProcessing;
using Email.Services.GmailProcessing.Dtos;
using Microsoft.Extensions.Logging;

namespace Email.Services
{
    /// <summary>
    /// Created: 2026-04-04 00:00 UTC
    /// Materializes latest CheckfrontV4 snapshot rows into canonical tree tables
    /// through existing GmailProcessingV2 Checkfront merge path.
    /// </summary>
    public sealed class CheckfrontV4TreeMaterializerService
    {
        private readonly CheckfrontV4BookingsSqlService _checkfrontV4BookingsSqlService;
        private readonly CheckfrontV4SnapshotMapper _checkfrontV4SnapshotMapper;
        private readonly GmailProcessingV2Service _gmailProcessingV2Service;
        private readonly ILogger<CheckfrontV4TreeMaterializerService> _logger;

        public CheckfrontV4TreeMaterializerService(
            CheckfrontV4BookingsSqlService checkfrontV4BookingsSqlService,
            CheckfrontV4SnapshotMapper checkfrontV4SnapshotMapper,
            GmailProcessingV2Service gmailProcessingV2Service,
            ILogger<CheckfrontV4TreeMaterializerService> logger)
        {
            _checkfrontV4BookingsSqlService = checkfrontV4BookingsSqlService;
            _checkfrontV4SnapshotMapper = checkfrontV4SnapshotMapper;
            _gmailProcessingV2Service = gmailProcessingV2Service;
            _logger = logger;
        }

        /// <summary>
        /// Created: 2026-04-04 00:00 UTC
        /// Loads latest snapshot per booking code and materializes to canonical tree tables.
        /// </summary>
        public async Task<CheckfrontV4MaterializationResultDto> MaterializeLatestSnapshotsAsync(
            int daysBack,
            int limit,
            CancellationToken ct = default)
        {
            var result = new CheckfrontV4MaterializationResultDto
            {
                RunId = Guid.NewGuid().ToString("N"),
                StartedAtUtc = DateTime.UtcNow,
                Success = true
            };

            try
            {
                var safeDaysBack = Math.Clamp(daysBack <= 0 ? 90 : daysBack, 1, 3650);
                var safeLimit = Math.Clamp(limit <= 0 ? 500 : limit, 1, 5000);
                var snapshots = await _checkfrontV4BookingsSqlService.GetLatestSnapshotsByBookingCodeAsync(
                    safeDaysBack,
                    safeLimit,
                    ct);

                result.SnapshotsRead = snapshots.Count;

                foreach (var snapshot in snapshots)
                {
                    ct.ThrowIfCancellationRequested();

                    var bookingCode = string.IsNullOrWhiteSpace(snapshot.BookingCode)
                        ? snapshot.CheckfrontBookingId
                        : snapshot.BookingCode;

                    var mapped = new Email.Models.CheckfrontBooking();
                    var mapSucceeded = _checkfrontV4SnapshotMapper.TryMapToLegacyBooking(snapshot, out mapped, out var mapReason);
                    if (!mapSucceeded &&
                        IsMissingTourNameReason(mapReason) &&
                        !string.IsNullOrWhiteSpace(bookingCode))
                    {
                        var fallbackItemSummary = await _checkfrontV4BookingsSqlService
                            .GetLatestNonEmptyItemSummaryByBookingCodeAsync(bookingCode, ct);
                        if (!string.IsNullOrWhiteSpace(fallbackItemSummary))
                        {
                            snapshot.ItemSummary = fallbackItemSummary;
                            mapSucceeded = _checkfrontV4SnapshotMapper.TryMapToLegacyBooking(snapshot, out mapped, out mapReason);
                        }
                    }

                    if (!mapSucceeded)
                    {
                        result.SkippedCount++;
                        result.Issues.Add(new CheckfrontV4MaterializationIssueDto
                        {
                            Stage = "MapSnapshot",
                            BookingCode = bookingCode ?? string.Empty,
                            Reason = string.IsNullOrWhiteSpace(mapReason) ? "Mapping rejected." : mapReason
                        });
                        continue;
                    }

                    try
                    {
                        var writeResult = await _gmailProcessingV2Service.SyncCheckfrontSnapshotBookingAsync(mapped, ct);
                        if (writeResult.Success)
                        {
                            if (writeResult.Action == CheckfrontCanonicalWriteAction.Created)
                            {
                                result.CreatedCount++;
                                result.MaterializedCount++;
                            }
                            else if (writeResult.Action == CheckfrontCanonicalWriteAction.Updated)
                            {
                                result.UpdatedCount++;
                                result.MaterializedCount++;
                            }
                            else
                            {
                                result.SkippedCount++;
                                if (!string.IsNullOrWhiteSpace(writeResult.Message))
                                {
                                    result.Issues.Add(new CheckfrontV4MaterializationIssueDto
                                    {
                                        Stage = "Materialize",
                                        BookingCode = writeResult.BookingCode,
                                        Reason = writeResult.Message
                                    });
                                }
                            }
                        }
                        else
                        {
                            result.ErrorCount++;
                            result.Issues.Add(new CheckfrontV4MaterializationIssueDto
                            {
                                Stage = "Materialize",
                                BookingCode = writeResult.BookingCode,
                                Reason = string.IsNullOrWhiteSpace(writeResult.Message)
                                    ? "Sync failed."
                                    : writeResult.Message
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        result.ErrorCount++;
                        result.Issues.Add(new CheckfrontV4MaterializationIssueDto
                        {
                            Stage = "Materialize",
                            BookingCode = mapped.Code ?? string.Empty,
                            Reason = ex.Message
                        });
                        _logger.LogError(
                            ex,
                            "MaterializeLatestSnapshotsAsync failed for booking {BookingCode}. RunId={RunId}",
                            mapped.Code,
                            result.RunId);
                    }
                }

                result.Message =
                    $"Run {result.RunId}: snapshots={result.SnapshotsRead}, materialized={result.MaterializedCount} (created={result.CreatedCount}, updated={result.UpdatedCount}), skipped={result.SkippedCount}, errors={result.ErrorCount}.";
                result.Success = result.ErrorCount == 0;
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorCount++;
                result.Message = ex.Message;
                result.Issues.Add(new CheckfrontV4MaterializationIssueDto
                {
                    Stage = "Run",
                    BookingCode = string.Empty,
                    Reason = ex.Message
                });
                _logger.LogError(
                    ex,
                    "MaterializeLatestSnapshotsAsync failed. RunId={RunId}",
                    result.RunId);
                return result;
            }
            finally
            {
                result.CompletedAtUtc = DateTime.UtcNow;
                if (string.IsNullOrWhiteSpace(result.Message))
                {
                    result.Message =
                        $"Run {result.RunId}: snapshots={result.SnapshotsRead.ToString(CultureInfo.InvariantCulture)}, materialized={result.MaterializedCount.ToString(CultureInfo.InvariantCulture)}, skipped={result.SkippedCount.ToString(CultureInfo.InvariantCulture)}, errors={result.ErrorCount.ToString(CultureInfo.InvariantCulture)}.";
                }
            }
        }

        private static bool IsMissingTourNameReason(string? reason)
        {
            return !string.IsNullOrWhiteSpace(reason) &&
                   reason.Contains("tour name/item summary", StringComparison.OrdinalIgnoreCase);
        }
    }
}
