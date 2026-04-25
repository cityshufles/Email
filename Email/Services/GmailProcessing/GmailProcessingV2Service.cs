using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Reflection;
using Email.Services.GmailCollection.Models;
using Email.Services.GmailProcessing.Dtos;
using Email.Services.GmailProcessing.Extraction;
using Email.Services.GmailProcessing.Logging;
using Email.Services.GmailProcessing.Models;
using Email.Services.GmailProcessing.Normalization;
using Email.Services.GmailProcessing.Repositories;
using Email.Services.GmailProcessing.VendorParsers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Email.Calendar.Services;
using CheckfrontBooking = Email.Models.CheckfrontBooking;

namespace Email.Services.GmailProcessing
{
    /// <summary>
    /// Created: 2025-11-07 00:00 UTC
    /// Updated: 2025-11-26 00:00 UTC - Added booking existence validation, content storage, and booking code field population
    /// v2 Gmail processing orchestrator: classify, normalize, parse, persist processed records and bookings with latest-action flags.
    /// </summary>
    public sealed class GmailProcessingV2Service
    {
        private readonly ILogger<GmailProcessingV2Service> _logger;
        private readonly IGmailProcessingRepository _repository;
        private readonly IConfiguration _configuration;
        private readonly Repositories.IProcessedEmailRepository _processedEmailRepository;
        private readonly Repositories.IClassificationRepository _classificationRepository;
        private readonly Repositories.IBookingRepository _bookingRepository;
        private readonly Repositories.ICustomerRepository _customerRepository;
        private readonly CustomerDataExtractionService _extractionService = new CustomerDataExtractionService();
        private readonly IGoogleCalendarService _googleCalendarService;
        private readonly Email.Services.CheckfrontService _checkfrontService;
        private readonly bool _viatorInboxOnlyMode;
        private readonly bool _enableCheckfrontEnrichmentForViator;
        private readonly bool _dryRunWritesViator;
        private readonly bool _dryRunWritesCheckfrontOnly;
        private readonly bool _hasDryRunWritesViatorSetting;
        private readonly bool _hasDryRunWritesCheckfrontOnlySetting;
        private readonly bool _dryRunWritesCheckfrontViatorOnly;
        private readonly bool _dryRunVerboseFieldDiff;
        private const decimal PriceFallbackPerPersonDivisor = 5.00m;
        private const decimal CheckfrontPlausibleUnscaledMaxUsd = 500m;
        private const int MaxReasonableAttendeeCount = 100;
        private static int _dryRunSyntheticIdentity = 0;
        private static long _lastNotificationSyncTicksUtc;
        private static int _notificationSyncRunning;

        public GmailProcessingV2Service(
            ILogger<GmailProcessingV2Service> logger,
            IGmailProcessingRepository repository,
            IConfiguration configuration,
            Repositories.IProcessedEmailRepository processedEmailRepository,
            Repositories.IClassificationRepository classificationRepository,
            Repositories.IBookingRepository bookingRepository,
            Repositories.ICustomerRepository customerRepository,
            IGoogleCalendarService googleCalendarService,
            Email.Services.CheckfrontService checkfrontService)
        {
            _logger = logger;
            _repository = repository;
            _configuration = configuration;
            _processedEmailRepository = processedEmailRepository;
            _classificationRepository = classificationRepository;
            _bookingRepository = bookingRepository;
            _customerRepository = customerRepository;
            _googleCalendarService = googleCalendarService;
            _checkfrontService = checkfrontService;
            _viatorInboxOnlyMode = _configuration.GetValue<bool?>("GmailProcessing:ViatorInboxOnlyMode") ?? true;
            _enableCheckfrontEnrichmentForViator = _configuration.GetValue<bool?>("GmailProcessing:EnableCheckfrontEnrichmentForViator") ?? false;
            _dryRunWritesViator = _configuration.GetValue<bool?>("GmailProcessing:DryRunWritesViator") ?? false;
            _dryRunWritesCheckfrontOnly = _configuration.GetValue<bool?>("GmailProcessing:DryRunWritesCheckfrontOnly") ?? false;
            _hasDryRunWritesViatorSetting = !string.IsNullOrWhiteSpace(_configuration["GmailProcessing:DryRunWritesViator"]);
            _hasDryRunWritesCheckfrontOnlySetting = !string.IsNullOrWhiteSpace(_configuration["GmailProcessing:DryRunWritesCheckfrontOnly"]);
            _dryRunWritesCheckfrontViatorOnly = _configuration.GetValue<bool>("GmailProcessing:DryRunWritesCheckfrontViatorOnly");
            _dryRunVerboseFieldDiff = _configuration.GetValue<bool>("GmailProcessing:DryRunVerboseFieldDiff");
        }

        public async Task<ProcessingResultDto> ProcessCollectedAsync(CancellationToken ct)
        {
            var started = DateTime.UtcNow;
            var result = new ProcessingResultDto { StartedAt = started, Success = true };
            try
            {
                var rules = await _classificationRepository.LoadActiveClassificationRulesAsync(ct);

                // Pass 1: Bookings/Confirmations (global) - Created: 2025-11-10 00:00 UTC
                result.ProcessedCount += await ProcessByTypePassAsync(rules,
                    includeType: type => type.Equals("Booking", StringComparison.OrdinalIgnoreCase) ||
                                          type.Equals("Confirmation", StringComparison.OrdinalIgnoreCase),
                    ct);

                // Pass 2: Modifications (global) - Created: 2025-11-10 00:00 UTC
                result.ProcessedCount += await ProcessByTypePassAsync(rules,
                    includeType: type => type.Equals("Modification", StringComparison.OrdinalIgnoreCase),
                    ct);

                // Pass 3: Cancellations (global) - Created: 2025-11-10 00:00 UTC
                result.ProcessedCount += await ProcessByTypePassAsync(rules,
                    includeType: type => type.Equals("Cancellation", StringComparison.OrdinalIgnoreCase),
                    ct);

                result.Message = $"Processed {result.ProcessedCount} emails with {result.ErrorCount} errors.";
                return Finish(result, started);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ProcessCollectedAsync failed");
                result.Success = false;
                result.Message = ex.Message;
                return Finish(result, started);
            }
        }

        public async Task<ProcessingResultDto> ProcessSpecificAsync(List<int> inboxIds, CancellationToken ct)
        {
            var started = DateTime.UtcNow;
            var result = new ProcessingResultDto { StartedAt = started, Success = true };
            try
            {
                if (inboxIds == null || inboxIds.Count == 0)
                {
                    result.Message = "No inbox ids provided.";
                    return Finish(result, started);
                }

                var rules = await _classificationRepository.LoadActiveClassificationRulesAsync(ct);
                var inboxEmails = await _repository.GetInboxEmailsByIdsAsync(inboxIds, ct);

				// Global priority: Booking/Confirmation → Modification → Cancellation (no cross-thread mixing)
				// Added: 2025-11-09 00:00 UTC
                var ordered = BucketInboxByGlobalPriority(inboxEmails, rules);
                var total = ordered.Count;
                foreach (var inbox in ordered)
                {
                    if (ct.IsCancellationRequested)
                    {
                        result.Message = $"Cancelled after {result.ProcessedCount}/{total} emails.";
                        return Finish(result, started);
                    }
                    try
                    {
                        await ProcessOneAsync(inbox, rules, ct);
                        result.ProcessedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Processing failed for InboxEmailId={InboxId}", inbox.Id);
                        result.ErrorCount++;
                        result.Errors.Add($"InboxEmailId={inbox.Id}: {ex.Message}");
                    }
                }

                result.Message = $"Processed {result.ProcessedCount} emails with {result.ErrorCount} errors.";
                return Finish(result, started);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ProcessSpecificAsync failed");
                result.Success = false;
                result.Message = ex.Message;
                return Finish(result, started);
            }
        }
        /// <summary>
        /// Created: 2025-11-10 00:00 UTC
        /// Multi-pass processing helper: streams unprocessed inbox rows in small pages (no global caps),
        /// filters by type predicate, and processes matching rows immediately. Resets its own high-watermark
        /// at the start of each pass so we guarantee global ordering for that pass.
        /// </summary>
        private async Task<int> ProcessByTypePassAsync(
            IReadOnlyList<ClassificationRuleRecord> rules,
            Func<string, bool> includeType,
            CancellationToken ct)
        {
            var processed = 0;

            // High-watermark (bookmark) for this pass
            DateTime? afterDate = null;
            int? afterId = null;

            //todo explain and fix this
            const int PageSize = 25; // small page to avoid parameter limits while streaming

            while (!ct.IsCancellationRequested)
            {

                //todo why are we paging
                var page = await _repository.GetUnprocessedInboxPageAsync(afterDate, afterId, PageSize, ct);
                if (page.Count == 0) break;

                foreach (var inbox in page)
                {
                    // update bookmark as we walk
                    var rd = inbox.ReceivedDate == default ? (inbox.CollectedAt == default ? DateTime.UtcNow : inbox.CollectedAt) : inbox.ReceivedDate;
                    afterDate = rd;
                    afterId = inbox.Id;

					// Defensive status check: only process Pending
					if (inbox.ProcessingStatus != TourEmailInboxProcessingStatus.Pending) continue;

                    var r = FindBestRule(inbox, rules);
                    var type = r?.EmailType ?? string.Empty;
                    if (!includeType(type)) continue;

                    try
                    {
                        await ProcessOneAsync(inbox, rules, ct);
                        var dryRunWrites = ShouldDryRunWrites(
                            r?.VendorName,
                            inbox.FromEmail,
                            inbox.Subject,
                            inbox.TextBody);
						await UpdateInboxProcessingStatusAsyncWithDryRun(inbox.Id, TourEmailInboxProcessingStatus.Processed, dryRunWrites, ct);
                        processed++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Processing failed for InboxEmailId={InboxId}", inbox.Id);
						try
						{
                            var dryRunWrites = ShouldDryRunWrites(
                                r?.VendorName,
                                inbox.FromEmail,
                                inbox.Subject,
                                inbox.TextBody);
							await UpdateInboxProcessingStatusAsyncWithDryRun(inbox.Id, TourEmailInboxProcessingStatus.Failed, dryRunWrites, ct);
						}
						catch { /* swallow status update errors */ }
                    }
                }
            }

            return processed;
        }

        /// <summary>
        /// Created: 2025-11-10 00:00 UTC
        /// Explicit method to process all bookings/confirmations using the streaming pass logic.
        /// </summary>
        public async Task<int> ProcessAllBookingsAsync(CancellationToken ct)
        {
            var rules = await _classificationRepository.LoadActiveClassificationRulesAsync(ct);
            return await ProcessByTypePassAsync(rules, type =>
                type.Equals("Booking", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("Confirmation", StringComparison.OrdinalIgnoreCase), ct);
        }

        /// <summary>
        /// Created: 2025-11-10 00:00 UTC
        /// Explicit method to process all modifications using the streaming pass logic.
        /// </summary>
        public async Task<int> ProcessAllModificationsAsync(CancellationToken ct)
        {
            var rules = await _classificationRepository.LoadActiveClassificationRulesAsync(ct);
            return await ProcessByTypePassAsync(rules, type =>
                type.Equals("Modification", StringComparison.OrdinalIgnoreCase), ct);
        }

        /// <summary>
        /// Created: 2025-11-10 00:00 UTC
        /// Explicit method to process all cancellations using the streaming pass logic.
        /// </summary>
        public async Task<int> ProcessAllCancellationsAsync(CancellationToken ct)
        {
            var rules = await _classificationRepository.LoadActiveClassificationRulesAsync(ct);
            return await ProcessByTypePassAsync(rules, type =>
                type.Equals("Cancellation", StringComparison.OrdinalIgnoreCase), ct);
        }

        public Task<GmailProcessingStatusDto> GetStatusAsync(CancellationToken ct)
            => _repository.GetStatusAsync(ct);

        public Task<IReadOnlyList<UnprocessedEmailRowDto>> GetUnprocessedAsync(int limit, CancellationToken ct)
            => _repository.GetUnprocessedInboxEmailsAsync(limit, ct);

        public Task<ProcessedEmailDisplayDto?> GetProcessedEmailAsync(int processedId, CancellationToken ct)
            => _repository.GetProcessedEmailByIdAsync(processedId, ct);

        public Task<IReadOnlyList<ProcessedEmailDisplayDto>> GetRecentSkippedAsync(int limit, CancellationToken ct)
            => _repository.GetRecentSkippedAsync(limit, ct);

        public Task<IReadOnlyList<ProcessedEmailDisplayDto>> GetRecentProcessedAsync(int limit, CancellationToken ct)
            => _repository.GetRecentProcessedAsync(limit, ct);

        public Task<IReadOnlyList<Booking>> GetRecentBookingsAsync(int limit, CancellationToken ct)
            => _bookingRepository.GetRecentAsync(limit, ct);

        public Task<IReadOnlyList<Customer>> GetRecentCustomersAsync(int limit, CancellationToken ct)
            => _customerRepository.GetRecentAsync(limit, ct);

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// Updated: 2026-03-26 00:00 UTC - Pulls recent Checkfront bookings and reconciles against historical email-derived records.
        /// </summary>
        public async Task<ProcessingResultDto> SyncCheckfrontBookingsAsync(int daysBack, int limitPerPage, int maxPages, CancellationToken ct)
        {
            var started = DateTime.UtcNow;
            var result = new ProcessingResultDto { StartedAt = started, Success = true };
            try
            {
                var safeDaysBack = Math.Clamp(daysBack <= 0 ? 30 : daysBack, 1, 365);
                var safeLimitPerPage = Math.Clamp(limitPerPage <= 0 ? 50 : limitPerPage, 1, 200);
                var safeMaxPages = Math.Clamp(maxPages <= 0 ? 5 : maxPages, 1, 50);
                var startDate = DateTime.UtcNow.Date.AddDays(-safeDaysBack);

                var dedupedBookings = new Dictionary<string, CheckfrontBooking>(StringComparer.OrdinalIgnoreCase);
                for (var page = 1; page <= safeMaxPages; page++)
                {
                    ct.ThrowIfCancellationRequested();
                    var pageBookings = await _checkfrontService.GetBookingsAsync(
                        startDate: startDate,
                        endDate: null,
                        status: null,
                        limit: safeLimitPerPage,
                        page: page);

                    if (pageBookings.Count == 0)
                    {
                        break;
                    }

                    foreach (var booking in pageBookings)
                    {
                        var key = FirstNonEmpty(booking.Code, booking.BookingId > 0 ? booking.BookingId.ToString() : null);
                        if (string.IsNullOrWhiteSpace(key))
                        {
                            continue;
                        }

                        dedupedBookings[key!] = booking;
                    }

                    if (pageBookings.Count < safeLimitPerPage)
                    {
                        break;
                    }
                }

                foreach (var booking in dedupedBookings.Values
                    .OrderByDescending(b => b.CreatedDateTimestamp)
                    .ThenByDescending(b => b.BookingId))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var synced = await SyncSingleCheckfrontBookingAsync(booking, ct);
                        if (synced)
                        {
                            result.ProcessedCount++;
                        }
                    }
                    catch (Exception bookingEx)
                    {
                        var code = FirstNonEmpty(booking.Code, booking.BookingId.ToString()) ?? "unknown";
                        _logger.LogError(bookingEx, "Checkfront sync failed for booking {BookingCode}", code);
                        result.ErrorCount++;
                        result.Errors.Add($"Booking {code}: {bookingEx.Message}");
                    }
                }

                result.Message = $"Checkfront sync pulled {dedupedBookings.Count} booking(s) from last {safeDaysBack} day(s). Updated/created {result.ProcessedCount} booking(s) with {result.ErrorCount} error(s).";
                return Finish(result, started);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SyncCheckfrontBookingsAsync failed");
                result.Success = false;
                result.Message = ex.Message;
                return Finish(result, started);
            }
        }

        /// <summary>
        /// Created: 2026-04-04 00:00 UTC
        /// Materializer entrypoint for one mapped Checkfront snapshot row.
        /// Reuses existing Checkfront booking merge logic and forces canonical vendor to "Checkfront".
        /// </summary>
        public async Task<CheckfrontSyncWriteResult> SyncCheckfrontSnapshotBookingAsync(CheckfrontBooking sourceBooking, CancellationToken ct)
        {
            if (sourceBooking == null)
            {
                throw new ArgumentNullException(nameof(sourceBooking));
            }

            var bookingCode = FirstNonEmpty(sourceBooking.Code, sourceBooking.BookingReference, sourceBooking.BookingId > 0 ? sourceBooking.BookingId.ToString() : null);
            if (string.IsNullOrWhiteSpace(bookingCode))
            {
                return new CheckfrontSyncWriteResult
                {
                    Success = true,
                    Action = CheckfrontCanonicalWriteAction.Skipped,
                    Message = "Missing booking code.",
                    BookingCode = string.Empty
                };
            }

            sourceBooking.Code = bookingCode;
            var existingBefore = await _bookingRepository.FindByCodeAsync(bookingCode, ct);
            var synced = await SyncSingleCheckfrontBookingAsync(
                sourceBooking,
                ct,
                vendorOverride: "Checkfront",
                skipDetailLookup: true);

            if (!synced)
            {
                return new CheckfrontSyncWriteResult
                {
                    Success = true,
                    Action = CheckfrontCanonicalWriteAction.Skipped,
                    BookingCode = bookingCode,
                    Message = "No canonical write performed."
                };
            }

            return new CheckfrontSyncWriteResult
            {
                Success = true,
                Action = existingBefore == null ? CheckfrontCanonicalWriteAction.Created : CheckfrontCanonicalWriteAction.Updated,
                BookingCode = bookingCode,
                Message = existingBefore == null ? "Created booking row." : "Updated booking row."
            };
        }

        /// <summary>
        /// Created: 2025-11-28 00:00 UTC
        /// Returns aggregated counts of inbox emails by ProcessingStatus (SMALLINT).
        /// </summary>
        public Task<IReadOnlyList<InboxProcessingStatusCountDto>> GetInboxProcessingStatusCountsAsync(CancellationToken ct)
            => _repository.GetInboxProcessingStatusCountsAsync(ct);

        /// <summary>
        /// Created: 2025-11-28 00:00 UTC
        /// Returns inbox id by MessageId for UI selections.
        /// </summary>
        public Task<int?> GetInboxIdByMessageIdAsync(string messageId, CancellationToken ct)
            => _repository.GetInboxEmailIdByMessageIdAsync(messageId, ct);

		/// <summary>
		/// Created: 2025-11-29 00:00 UTC
		/// Diagnostic hydration for a single email by MessageId.
		/// </summary>
		public Task<HydratedEmailDiagnosticContext?> GetHydratedDiagnosticByMessageIdAsync(string messageId, CancellationToken ct)
			=> _repository.GetHydratedDiagnosticByMessageIdAsync(messageId, ct);

		/// <summary>
		/// Created: 2025-11-29 00:00 UTC
		/// Returns SELECT * full processed email row by MessageId (latest).
		/// </summary>
		public Task<ProcessedEmailRecord?> GetProcessedFullByMessageIdAsync(string messageId, CancellationToken ct)
			=> _repository.GetProcessedFullByMessageIdAsync(messageId, ct);

        /// <summary>
        /// Created: 2025-11-28 00:00 UTC
        /// Convenience to load full inbox record by MessageId.
        /// </summary>
        public async Task<InboxEmailRecord?> GetInboxByMessageIdAsync(string messageId, CancellationToken ct)
        {
            var id = await _repository.GetInboxEmailIdByMessageIdAsync(messageId, ct);
            if (!id.HasValue) return null;
            var list = await _repository.GetInboxEmailsByIdsAsync(new[] { id.Value }, ct);
            return list.Count > 0 ? list[0] : null;
        }

        /// <summary>
        /// Created: 2025-11-28 00:00 UTC
        /// Returns all processed email display rows for a booking code.
        /// </summary>
        public Task<IReadOnlyList<ProcessedEmailDisplayDto>> GetProcessedByBookingCodeAsync(string bookingCode, CancellationToken ct)
            => _repository.GetProcessedByBookingCodeAsync(bookingCode, ct);

        /// <summary>
        /// Created: 2025-11-28 00:00 UTC
        /// Gets latest booking by booking code.
        /// </summary>
        public Task<Booking?> GetBookingByCodeAsync(string bookingCode, CancellationToken ct)
            => _bookingRepository.FindByCodeAsync(bookingCode, ct);

        /// <summary>
        /// Created: 2025-11-29 00:00 UTC
        /// Returns the Customer for a given booking code, if a booking exists.
        /// </summary>
        public async Task<Customer?> GetCustomerForBookingCodeAsync(string bookingCode, CancellationToken ct)
        {
            var booking = await _bookingRepository.FindByCodeAsync(bookingCode, ct);
            if (booking == null) return null;
            return await _customerRepository.GetByIdAsync(booking.CustomerId, ct);
        }
        public async Task<ProcessingResultDto> ReprocessProcessedAsync(List<int> processedIds, CancellationToken ct)
        {
            var started = DateTime.UtcNow;
            var result = new ProcessingResultDto { StartedAt = started, Success = true };
            if (processedIds == null || processedIds.Count == 0)
            {
                result.Message = "No processed ids provided.";
                return Finish(result, started);
            }

            try
            {
                var processedRecords = await _repository.GetProcessedByIdsAsync(processedIds, ct);
                if (processedRecords.Count == 0)
                {
                    result.Message = "No matching processed emails found.";
                    return Finish(result, started);
                }

                var rules = await _classificationRepository.LoadActiveClassificationRulesAsync(ct);
                // Order selected processed by priority Booking/Confirmation → Modification → Cancellation, keeping thread grouping
                static int P(string type)
                {
                    if (type.Equals("Booking", StringComparison.OrdinalIgnoreCase) ||
                        type.Equals("Confirmation", StringComparison.OrdinalIgnoreCase)) return 0;
                    if (type.Equals("Modification", StringComparison.OrdinalIgnoreCase)) return 1;
                    if (type.Equals("Cancellation", StringComparison.OrdinalIgnoreCase)) return 2;
                    return 3;
                }
                var orderedProcessed = processedRecords
                    .OrderBy(p => $"{p.VendorName}|{p.BookingCode ?? ""}".ToLowerInvariant())
                    .ThenBy(p => P(p.EmailType ?? string.Empty))
                    .ThenBy(p => p.ProcessingCompletedAt ?? p.ExtractedAt ?? DateTime.UtcNow)
                    .ToList();

                var inboxIds = orderedProcessed.Select(p => p.InboxEmailId).Distinct().ToList();
                var inboxEmails = await _repository.GetInboxEmailsByIdsAsync(inboxIds, ct);
                var lookup = inboxEmails.ToDictionary(x => x.Id);

                foreach (var processed in orderedProcessed)
                {
                    if (!lookup.TryGetValue(processed.InboxEmailId, out var inbox))
                    {
                        result.ErrorCount++;
                        result.Errors.Add($"ProcessedId={processed.Id}: Inbox email not found.");
                        continue;
                    }

                    try
                    {
                        await ProcessOneAsync(inbox, rules, ct);
                        result.ProcessedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Reprocessing failed for ProcessedEmailId={ProcessedId}", processed.Id);
                        result.ErrorCount++;
                        result.Errors.Add($"ProcessedId={processed.Id}: {ex.Message}");
                    }
                }

                result.Message = $"Reprocessed {result.ProcessedCount} emails with {result.ErrorCount} errors.";
                return Finish(result, started);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ReprocessProcessedAsync failed");
                result.Success = false;
                result.Message = ex.Message;
                return Finish(result, started);
            }
        }

        /// <summary>
        /// Added: 2025-12-02 00:00 UTC
        /// Finds Modification processed rows with missing tour fields and reprocesses them.
        /// </summary>
        public async Task<ProcessingResultDto> ReprocessModificationsMissingTourFieldsAsync(int limit, CancellationToken ct)
        {
            try
            {
                var ids = await _repository.GetProcessedIdsForModificationsMissingTourFieldsAsync(Math.Max(1, limit), ct);
                if (ids.Count == 0)
                {
                    return new ProcessingResultDto
                    {
                        StartedAt = DateTime.UtcNow,
                        CompletedAt = DateTime.UtcNow,
                        Success = true,
                        ProcessedCount = 0,
                        ErrorCount = 0,
                        Message = "No modification rows missing tour fields were found."
                    };
                }
                return await ReprocessProcessedAsync(ids.ToList(), ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ReprocessModificationsMissingTourFieldsAsync failed");
                return new ProcessingResultDto
                {
                    StartedAt = DateTime.UtcNow,
                    CompletedAt = DateTime.UtcNow,
                    Success = false,
                    Message = ex.Message
                };
            }
        }

        public async Task DeleteProcessedAsync(List<int> processedIds, CancellationToken ct)
        {
            if (processedIds == null || processedIds.Count == 0) return;
            await _processedEmailRepository.DeleteAsync(processedIds, ct);
        }

        public async Task DeleteBookingsAsync(List<int> bookingIds, CancellationToken ct)
        {
            if (bookingIds == null || bookingIds.Count == 0) return;
            await _bookingRepository.DeleteAsync(bookingIds, ct);
        }

        public async Task DeleteCustomersAsync(List<int> customerIds, CancellationToken ct)
        {
            if (customerIds == null || customerIds.Count == 0) return;
            await _customerRepository.DeleteAsync(customerIds, ct);
        }

		/// <summary>
		/// Added: 2025-11-30 00:00 UTC
		/// Repairs historical ProcessedEmails.BookingCode for modification/cancellation rows
		/// to align with current selection rules.
		/// </summary>
		public Task<int> RepairProcessedBookingCodesAsync(int limit, CancellationToken ct)
			=> _repository.RepairProcessedBookingCodesAsync(limit, ct);

		/// <summary>
		/// Added: 2025-12-01 00:00 UTC
		/// Resolve the canonical root/original booking code for a given effective booking code by
		/// walking backward through earliest events and reverse edges (NewBookingCode).
		/// </summary>
		public async Task<string> ResolveRootBookingCodeAsync(string bookingCode, CancellationToken ct)
		{
			if (string.IsNullOrWhiteSpace(bookingCode)) return bookingCode;

			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			string? current = bookingCode;

			for (int i = 0; i < 20 && !string.IsNullOrWhiteSpace(current); i++)
			{
				if (!seen.Add(current!)) break;

				var earliest = await _repository.GetEarliestEventByBookingCodeAsync(current!, ct);
				if (earliest == null) break;

				if (string.Equals(earliest.EmailType, "Booking", StringComparison.OrdinalIgnoreCase) ||
					string.Equals(earliest.EmailType, "Confirmation", StringComparison.OrdinalIgnoreCase))
				{
					return current!;
				}

				if (string.Equals(earliest.EmailType, "Modification", StringComparison.OrdinalIgnoreCase) &&
					!string.IsNullOrWhiteSpace(earliest.PreviousBookingCode))
				{
					current = earliest.PreviousBookingCode;
					continue;
				}

				var prevMod = await _repository.GetEarliestModificationProducingNewCodeAsync(current!, ct);
				// 2025-12-01 00:00 UTC - Use PreviousBookingCode to walk back to the prior code,
				// not BookingCode (which for modifications usually equals the NEW code)
				if (prevMod != null && !string.IsNullOrWhiteSpace(prevMod.PreviousBookingCode))
				{
					current = prevMod.PreviousBookingCode;
					continue;
				}

				break;
			}

			return bookingCode;
		}

		/// <summary>
		/// Added: 2025-12-01 00:00 UTC
		/// Given an inbox id and effective booking code, compute the canonical root and override (no COALESCE)
		/// the inbox OriginalBooking* pointer.
		/// </summary>
		public async Task OverrideInboxOriginalPointerAsync(int inboxId, string effectiveBookingCode, CancellationToken ct)
		{
			if (inboxId <= 0 || string.IsNullOrWhiteSpace(effectiveBookingCode)) return;

			var rootCode = await ResolveRootBookingCodeAsync(effectiveBookingCode, ct);
			var rootBookingRow = await _repository.GetEarliestBookingEventByBookingCodeAsync(rootCode, ct);
			await _repository.OverrideInboxOriginalBookingPointerAsync(
				inboxId,
				rootBookingRow?.Id,
				rootCode,
				rootBookingRow?.MessageId,
				ct);
		}

		/// <summary>
		/// Added: 2025-12-01 00:00 UTC
		/// Build a full processed thread starting from the canonical root booking code by traversing
		/// forward along NewBookingCode edges. Returns display DTOs ordered chronologically.
		/// </summary>
		public async Task<IReadOnlyList<ProcessedEmailDisplayDto>> GetProcessedThreadByRootBookingCodeAsync(string rootBookingCode, CancellationToken ct)
		{
			if (string.IsNullOrWhiteSpace(rootBookingCode)) return Array.Empty<ProcessedEmailDisplayDto>();

			var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootBookingCode };
			var queue = new Queue<string>();
			queue.Enqueue(rootBookingCode);

			var collected = new List<ProcessedEmailRecord>(256);

			while (queue.Count > 0)
			{
				ct.ThrowIfCancellationRequested();
				var code = queue.Dequeue();

				// Pull all events explicitly tagged with this effective code
				var page = await _repository.GetProcessedByBookingCodesAsync(new[] { code }, ct);
				collected.AddRange(page);

				// Also pull modifications whose PreviousBookingCode is this code,
				// so we can discover forward hops even when the mod row's BookingCode is the NEW code.
				var forwardMods = await _repository.GetModificationsByPreviousCodeAsync(code, ct);
				collected.AddRange(forwardMods);

				foreach (var evt in page.Concat(forwardMods))
				{
					if (!string.IsNullOrWhiteSpace(evt.NewBookingCode) && visited.Add(evt.NewBookingCode))
					{
						queue.Enqueue(evt.NewBookingCode);
					}
				}
			}

			// De-duplicate by Id to avoid any accidental repeats
			var unique = collected
				.GroupBy(e => e.Id)
				.Select(g => g.First())
				.ToList();

			// Map with display-aware booking code selection per email type
			static string? SelectDisplayBookingCode(ProcessedEmailRecord e)
			{
				var type = e.EmailType ?? string.Empty;
				if (string.Equals(type, "Modification", StringComparison.OrdinalIgnoreCase))
				{
					return e.NewBookingCode
						?? (string.IsNullOrWhiteSpace(e.BookingCode) ? null : e.BookingCode)
						?? e.PreviousBookingCode
						?? e.ExtractedBookingCode;
				}
				if (string.Equals(type, "Cancellation", StringComparison.OrdinalIgnoreCase))
				{
					return e.PreviousBookingCode
						?? (string.IsNullOrWhiteSpace(e.BookingCode) ? null : e.BookingCode)
						?? e.ExtractedBookingCode;
				}
				// Booking/Confirmation/Other
				return (string.IsNullOrWhiteSpace(e.BookingCode) ? null : e.BookingCode)
					?? e.ExtractedBookingCode;
			}

			var ordered = unique
				.OrderBy(e => e.ProcessingCompletedAt ?? e.ExtractedAt ?? e.CreatedAt)
				.ThenBy(e => e.Id)
				.Select(e => new ProcessedEmailDisplayDto
				{
					Id = e.Id,
					InboxEmailId = e.InboxEmailId,
					MessageId = e.MessageId,
					VendorName = e.VendorName,
					EmailType = e.EmailType,
					CustomerName = e.CustomerName,
					CustomerEmail = e.CustomerEmail,
					CustomerPhone = e.CustomerPhone,
					BookingCode = SelectDisplayBookingCode(e),
					ProcessingStatus = e.ProcessingStatus,
					ExtractedAt = e.ExtractedAt,
					IsLatestAction = e.IsLatestAction,
					ProcessingCompletedAt = e.ProcessingCompletedAt,
					ProcessingError = e.ProcessingError
				})
				.ToList();

			return ordered;
		}

		/// <summary>
		/// Added: 2025-12-01 00:00 UTC
		/// Admin job to rebuild OriginalBooking* pointers for inbox rows in batches. Uses override (no COALESCE).
		/// </summary>
		public async Task<int> RebuildInboxOriginalPointersAsync(int batchSize, CancellationToken ct)
		{
			if (batchSize <= 0) return 0;
			int updated = 0;
			int lastId = 0;

			while (true)
			{
				ct.ThrowIfCancellationRequested();
				var batch = await _repository.GetInboxBatchNeedingOriginRebuildAsync(lastId, batchSize, ct);
				if (batch.Count == 0) break;

				foreach (var inbox in batch)
				{
					ct.ThrowIfCancellationRequested();
					var effectiveCode = await _repository.GetEffectiveBookingCodeForMessageAsync(inbox.MessageId, ct)
					                    ?? inbox.OriginalBookingCode;
					if (string.IsNullOrWhiteSpace(effectiveCode)) continue;

					var rootCode = await ResolveRootBookingCodeAsync(effectiveCode!, ct);
					var rootBooking = await _repository.GetEarliestBookingEventByBookingCodeAsync(rootCode, ct);
					await _repository.OverrideInboxOriginalBookingPointerAsync(
						inbox.Id,
						rootBooking?.Id,
						rootCode,
						rootBooking?.MessageId,
						ct);
					updated++;
				}

				lastId = batch[^1].Id;
			}

			return updated;
		}
		/// <summary>
		/// Added: 2025-11-30 00:00 UTC
		/// Admin backfill to populate OriginalBooking* fields on inbox rows for historical emails.
		/// Processes the most recent 'limit' processed emails.
		/// </summary>
		public async Task<int> BackfillOriginalBookingsAsync(int limit, CancellationToken ct)
		{
			if (limit <= 0) return 0;
			var processed = await _repository.GetRecentProcessedAsync(limit, ct);
			var updated = 0;

			foreach (var p in processed)
			{
				ct.ThrowIfCancellationRequested();

				// Load inbox with current Original* values
				var inboxList = await _repository.GetInboxEmailsByIdsAsync(new[] { p.InboxEmailId }, ct);
				var inbox = inboxList.FirstOrDefault();
				if (inbox == null) continue;

				// Skip if already populated
				if (inbox.OriginalBookingId.HasValue ||
				    !string.IsNullOrWhiteSpace(inbox.OriginalBookingCode) ||
				    !string.IsNullOrWhiteSpace(inbox.OriginalBookingMessageId))
				{
					continue;
				}

				// Load full processed row to access code fields
				var processedFull = await _repository.GetProcessedFullByMessageIdAsync(p.MessageId, ct);
				if (processedFull == null) continue;

				string emailType = p.EmailType ?? string.Empty;
				string? originCode = null;
				Booking? originBooking = null;
				string? originMessageId = null;

				if (string.Equals(emailType, "Booking", StringComparison.OrdinalIgnoreCase) ||
					string.Equals(emailType, "Confirmation", StringComparison.OrdinalIgnoreCase))
				{
					originCode = string.IsNullOrWhiteSpace(processedFull.BookingCode) ? processedFull.ExtractedBookingCode : processedFull.BookingCode;
					if (!string.IsNullOrWhiteSpace(originCode))
					{
						originBooking = await _bookingRepository.FindByCodeAsync(originCode!, ct);
					}
					originMessageId = originBooking?.MessageId ?? inbox.MessageId;
				}
				else if (string.Equals(emailType, "Modification", StringComparison.OrdinalIgnoreCase))
				{
					originCode = processedFull.PreviousBookingCode
								  ?? (string.IsNullOrWhiteSpace(processedFull.BookingCode) ? processedFull.ExtractedBookingCode : processedFull.BookingCode);
					if (!string.IsNullOrWhiteSpace(originCode))
					{
						originBooking = await _bookingRepository.FindByCodeAsync(originCode!, ct);
						originMessageId = originBooking?.MessageId;
					}
				}
				else if (string.Equals(emailType, "Cancellation", StringComparison.OrdinalIgnoreCase))
				{
					originCode = processedFull.PreviousBookingCode
								  ?? (string.IsNullOrWhiteSpace(processedFull.BookingCode) ? processedFull.ExtractedBookingCode : processedFull.BookingCode);
					if (!string.IsNullOrWhiteSpace(originCode))
					{
						originBooking = await _bookingRepository.FindByCodeAsync(originCode!, ct);
						originMessageId = originBooking?.MessageId;
					}
				}

				if (!string.IsNullOrWhiteSpace(originCode) || originBooking != null || !string.IsNullOrWhiteSpace(originMessageId))
				{
					await _repository.UpdateInboxOriginalBookingAsync(
						inbox.Id,
						originBooking?.Id,
						originCode,
						originMessageId,
						ct);
					updated++;
				}
			}

			return updated;
		}

        private async Task ProcessOneAsync(InboxEmailRecord inbox, IReadOnlyList<ClassificationRuleRecord> rules, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var (plainText, html) = ForwardedEmailUnwrapper.Unwrap(inbox.HtmlBody, inbox.TextBody);
            var normalizedText = string.IsNullOrWhiteSpace(plainText) ? PlainTextBuilder.FromHtml(html) : plainText;

            // Classification by domain + subject phrase
            var rule = FindBestRule(inbox, rules);
            var classifiedVendorName = rule?.VendorName ?? "General";
            var emailType = rule?.EmailType ?? string.Empty;

            // Vendor-aware parsing with plaintext fallback
            var parseResult = ParseVendor(classifiedVendorName, emailType, inbox.Subject, html, normalizedText);
            var data = parseResult.Data;

            if (!string.IsNullOrWhiteSpace(parseResult.EmailType))
            {
                emailType = parseResult.EmailType;
            }

            var vendorName = string.IsNullOrWhiteSpace(parseResult.VendorName)
                ? classifiedVendorName
                : parseResult.VendorName;
            var isViatorContext = IsViatorContext(vendorName, inbox.FromEmail, inbox.Subject, normalizedText, data);
            if (isViatorContext)
            {
                vendorName = "Viator";
            }

            CheckfrontBooking? enrichedBooking = null;
            var isCheckfrontContext = !isViatorContext &&
                                      (IsCheckfrontVendor(classifiedVendorName) ||
                                       IsCheckfrontVendor(vendorName) ||
                                       IsCheckfrontSender(inbox.FromEmail));
            if (isViatorContext && _viatorInboxOnlyMode && !_enableCheckfrontEnrichmentForViator)
            {
                _logger.LogInformation(
                    "Viator inbox-only mode active. Skipping Checkfront enrichment/sync for MessageId={MessageId}.",
                    inbox.MessageId);
            }
            else if (isCheckfrontContext || (isViatorContext && _enableCheckfrontEnrichmentForViator))
            {
                enrichedBooking = await TryEnrichFromCheckfrontAsync(data, inbox.Subject, normalizedText, ct);
                vendorName = InferCheckfrontVendor(inbox.Subject, normalizedText, data, enrichedBooking);
                if (enrichedBooking == null)
                {
                    _logger.LogInformation(
                        "Checkfront enrichment skipped or unavailable. Subject={Subject}. MissingFields={MissingFields}",
                        inbox.Subject ?? string.Empty,
                        string.Join(",", GetMissingEssentialFields(data)));
                }
            }

            var lockedVendor = await TryResolveLockedVendorForAutomationAsync(inbox.MessageId, data, emailType, ct);
            if (!string.IsNullOrWhiteSpace(lockedVendor))
            {
                vendorName = lockedVendor;
            }

            var dryRunWrites = ShouldDryRunWrites(vendorName, inbox.FromEmail, inbox.Subject, normalizedText, data);
            var isCheckfrontNotificationOnly = isCheckfrontContext &&
                                               IsViatorBookingNotification(inbox.Subject, normalizedText) &&
                                               string.IsNullOrWhiteSpace(data.BookingCode) &&
                                               string.IsNullOrWhiteSpace(data.ExtractedBookingCode);

            // Normalize email/phone
            data.CustomerEmail = EmailNormalizationService.NormalizeEmail(data.CustomerEmail);
            data.CustomerPhone = PhoneNormalizationService.NormalizePhone(data.CustomerPhone);
            if (data.NumberOfAttendees == 0 && (data.NumberOfAdults > 0 || data.NumberOfChildren > 0))
            {
                data.NumberOfAttendees = data.NumberOfAdults + data.NumberOfChildren;
            }

            ApplyPriceBasedAttendeeFallback(
                data,
                vendorName,
                isViatorContext,
                normalizedText,
                enrichedBooking,
                diagnosticContext: "inbox");
            ApplyAttendeeSplitFallback(data, vendorName, "inbox");

            // Country extraction (best-effort)
            string? countryName = null;
            if (!string.IsNullOrWhiteSpace(data.CustomerPhone))
            {
                if (Extraction.CountryExtractor.TryGetCountryNameFromPhone(data.CustomerPhone, out var cn))
                {
                    countryName = cn;
                }
            }

            // Customer lookup/creation with Fix A (try all codes) and fuzzy fallback
            Customer? customer = null;
            if (!isCheckfrontNotificationOnly)
            {
                customer = await ResolveCustomerAsync(data, emailType, dryRunWrites, ct);
            }

            // Build processed email record with all fields including content and booking codes
            // Added: 2025-11-26 00:00 UTC - Full field population
            var customerIdentifierForProcessed = ComputeCustomerIdentifier(customer, data, vendorName, inbox.FromEmail);
            var rec = new ProcessedEmailRecord
            {
                InboxEmailId = inbox.Id,
                MessageId = inbox.MessageId,
                VendorName = vendorName,
                EmailType = emailType,
                IsTourBookingEmail = string.Equals(emailType, "Booking", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(emailType, "Confirmation", StringComparison.OrdinalIgnoreCase),
                ClassificationRuleId = rule?.Id,
                ProcessingStatus = "completed",
                ProcessingStartedAt = now,
                ProcessingCompletedAt = now,
                ProcessingError = null,
                ProcessingAttempts = 1,
                NextProcessingAttempt = null,
                RateLimitResetAt = null,
                CustomerName = data.CustomerName,
                // Choose processed-row booking code per flow to avoid cross-thread bleed:
                // - Booking/Confirmation: BookingCode (fallback ExtractedBookingCode)
                // - Modification: NewBookingCode (fallback BookingCode, then PreviousBookingCode, then ExtractedBookingCode)
                // - Cancellation: PreviousBookingCode (fallback BookingCode, then ExtractedBookingCode)
                BookingCode = SelectBookingCodeForProcessed(data, emailType),
                CustomerPhone = data.CustomerPhone,
                CustomerEmail = data.CustomerEmail,
                NumberOfAttendees = data.NumberOfAttendees,
                Language = data.Language,
                TourDate = data.TourDate?.ToString("yyyy-MM-dd"),
                TourTime = data.TourTime,
                TourName = data.TourName,
                TourLocation = data.TourLocation,
                ActionRequired = null,
                ExtractedAt = now,
                CustomerIdentifier = customerIdentifierForProcessed,
                RelatedEmailIds = null,
                IsLatestAction = true,
                CreatedAt = now,
                UpdatedAt = now,
                // Added 2025-11-26: Content storage
                PlainTextContent = normalizedText,
                HtmlContent = html ?? string.Empty,
                // Added 2025-11-26: Booking code tracking
                ExtractedBookingCode = data.ExtractedBookingCode ?? data.BookingCode ?? string.Empty,
                NewBookingCode = data.NewBookingCode ?? string.Empty,
                PreviousBookingCode = data.PreviousBookingCode ?? string.Empty,
                // Added 2025-11-26: Email type flags
                IsCancellation = data.IsCancellation,
                IsModification = data.IsModification,
                IsBooking = data.IsBooking,
                // Added 2025-11-26: Attendee breakdown
                NumberOfAdults = data.NumberOfAdults,
                NumberOfChildren = data.NumberOfChildren
            };

            // Handle booking flows with validation - may update rec.ProcessingStatus/ProcessingError
            // Added: 2025-11-26 00:00 UTC - Booking existence validation for mod/cancel
            BookingFlowResult bookingFlowResult;
            if (isCheckfrontNotificationOnly)
            {
                var activityResult = await TryResolveCheckfrontNotificationActivityAsync(data, normalizedText, ct);
                var syncResult = await TryRunNotificationDrivenCheckfrontSyncAsync(ct);
                var syncMessage = syncResult == null
                    ? "Checkfront sync skipped (throttled or already running)."
                    : $"Checkfront sync triggered. Processed={syncResult.ProcessedCount}; Errors={syncResult.ErrorCount}.";
                var activityMessage = BuildCheckfrontNotificationActivityMessage(activityResult);
                rec.ActionRequired = string.IsNullOrWhiteSpace(activityMessage)
                    ? syncMessage
                    : $"{syncMessage} {activityMessage}";
                bookingFlowResult = BookingFlowResult.Ok();
            }
            else
            {
                bookingFlowResult = await HandleBookingFlowsAsync(vendorName, emailType, data, customer, rec, inbox.MessageId, countryName, dryRunWrites, ct);
            }

            // Update record status based on booking flow result
            if (!bookingFlowResult.Success)
            {
                rec.ProcessingStatus = "failed";
                rec.ProcessingError = bookingFlowResult.ErrorMessage;
            }

            var processedId = await UpsertProcessedEmailAsyncWithDryRun(rec, dryRunWrites, ct);

            // Latest action flag for the thread
            await SetLatestActionForThreadAsyncWithDryRun(vendorName, rec.BookingCode, processedId, dryRunWrites, ct);

			// Added: 2025-12-01 00:00 UTC - Persist canonical root/original booking pointer on inbox (idempotent)
			// Use the effective processed-row BookingCode as starting point, then resolve the true root.
			var effectiveProcessedCode = rec.BookingCode;
			if (!string.IsNullOrWhiteSpace(effectiveProcessedCode))
			{
				var rootCode = await ResolveRootBookingCodeAsync(effectiveProcessedCode!, ct);
				var rootBookingRow = await _repository.GetEarliestBookingEventByBookingCodeAsync(rootCode, ct);
				var rootMessageId = rootBookingRow?.MessageId ?? inbox.MessageId;
				await UpdateInboxOriginalBookingAsyncWithDryRun(
					inbox.Id,
					rootBookingRow?.Id,
					rootCode,
					rootMessageId,
                    dryRunWrites,
					ct);
			}
        }

        /// <summary>
        /// Added: 2025-12-16
        /// Manually process a booking from the UI, simulating the email pipeline.
        /// </summary>
        public async Task<int> ProcessManualBookingAsync(ManualBookingDto data, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var messageId = $"MANUAL-{Guid.NewGuid()}";
            var vendorName = !string.IsNullOrWhiteSpace(data.VendorName) ? data.VendorName : "Manual";
            var emailType = "booking"; // Manual entries are always treated as new bookings
            var dryRunWrites = ShouldDryRunWrites(vendorName, "manual@system.local", messageId, null);
            
            // 1. Create a dummy Inbox record to maintain referential integrity
            var inbox = new InboxEmailRecord
            {
                MessageId = messageId,
                Uid = 0, // Virtual
                Subject = $"Manual Booking: {data.TourName} ({data.TourDate:yyyy-MM-dd})",
                FromEmail = "manual@system.local",
                FromName = "System User",
                ToEmail = "system@local",
                ReceivedDate = now,
                CollectedAt = now,
                TextBody = $"Manual Booking Entry - {now}",
                HtmlBody = string.Empty,
                CollectionBatchId = "MANUAL",
                IsRead = true,
                CreatedAt = now,
                UpdatedAt = now,
                ProcessingStatus = TourEmailInboxProcessingStatus.Processed
            };
            var inboxId = await CreateInboxEmailAsyncWithDryRun(inbox, dryRunWrites, ct);
            inbox.Id = inboxId;

            // 2. Map to CustomerData
            // Auto-detect country if not provided
            string? countryName = data.CountryName;
            if (string.IsNullOrWhiteSpace(countryName) && !string.IsNullOrWhiteSpace(data.CustomerPhone))
            {
                 CountryExtractor.TryGetCountryNameFromPhone(data.CustomerPhone, out countryName);
            }

            // Ensure booking code
            var bookingCode = !string.IsNullOrWhiteSpace(data.BookingCode) 
                ? data.BookingCode 
                : $"MANUAL-{DateTime.UtcNow.Ticks % 1000000:D6}";

            var customerData = new CustomerData
            {
                CustomerName = data.CustomerName,
                CustomerEmail = data.CustomerEmail,
                CustomerPhone = data.CustomerPhone,
                TourName = data.TourName,
                TourDate = data.TourDate,
                TourTime = data.TourTime,
                NumberOfAdults = data.NumberOfAdults,
                NumberOfChildren = data.NumberOfChildren,
                NumberOfAttendees = data.NumberOfAdults + data.NumberOfChildren,
                Language = data.Language,
                BookingCode = bookingCode,
                IsBooking = true,
                IsCancellation = false,
                IsModification = false
            };

            // 3. Resolve Customer
            // Normalize before resolve
            customerData.CustomerEmail = EmailNormalizationService.NormalizeEmail(customerData.CustomerEmail);
            customerData.CustomerPhone = PhoneNormalizationService.NormalizePhone(customerData.CustomerPhone);
            
            var customer = await ResolveCustomerAsync(customerData, emailType, dryRunWrites, ct);

            // 4. Create Processed Email Record
            var customerIdentifier = ComputeCustomerIdentifier(customer, customerData, vendorName, inbox.FromEmail);
            var rec = new ProcessedEmailRecord
            {
                InboxEmailId = inboxId,
                MessageId = messageId,
                VendorName = vendorName,
                EmailType = emailType,
                IsTourBookingEmail = true,
                ProcessingStatus = "completed",
                ProcessingStartedAt = now,
                ProcessingCompletedAt = now,
                ProcessingAttempts = 1,
                CustomerName = customerData.CustomerName,
                BookingCode = bookingCode,
                CustomerPhone = customerData.CustomerPhone,
                CustomerEmail = customerData.CustomerEmail,
                NumberOfAttendees = customerData.NumberOfAttendees,
                NumberOfAdults = customerData.NumberOfAdults,
                NumberOfChildren = customerData.NumberOfChildren,
                Language = customerData.Language,
                TourDate = customerData.TourDate?.ToString("yyyy-MM-dd"),
                TourTime = customerData.TourTime,
                TourName = customerData.TourName,
                CustomerIdentifier = customerIdentifier,
                IsLatestAction = true,
                CreatedAt = now,
                UpdatedAt = now,
                PlainTextContent = inbox.TextBody,
                IsBooking = true,
                ExtractedBookingCode = bookingCode
            };

            // 5. Handle Booking Creation (using generic flow logic)
            // Note: HandleBookingFlowsAsync handles the DB creation of the booking and setting the ID on 'rec' if needed? 
            // Actually HandleBookingFlowsAsync uses 'rec.Id' so we must insert rec first.
            
            var processedId = await UpsertProcessedEmailAsyncWithDryRun(rec, dryRunWrites, ct);
            rec.Id = processedId;

            var bookingResult = await HandleBookingFlowsAsync(vendorName, emailType, customerData, customer, rec, messageId, countryName, dryRunWrites, ct);

            if (!bookingResult.Success)
            {
                rec.ProcessingStatus = "failed";
                rec.ProcessingError = bookingResult.ErrorMessage;
                await UpsertProcessedEmailAsyncWithDryRun(rec, dryRunWrites, ct);
                throw new InvalidOperationException($"Failed to create booking: {bookingResult.ErrorMessage}");
            }

            await SetLatestActionForThreadAsyncWithDryRun(vendorName, bookingCode, processedId, dryRunWrites, ct);
            
            return processedId;
        }

        private async Task<ProcessingResultDto?> TryRunNotificationDrivenCheckfrontSyncAsync(CancellationToken ct)
        {
            var minIntervalMinutes = Math.Clamp(_configuration.GetValue<int?>("CheckfrontSync:MinIntervalMinutes") ?? 20, 1, 1440);
            var now = DateTime.UtcNow;
            var lastTicks = Interlocked.Read(ref _lastNotificationSyncTicksUtc);
            if (lastTicks > 0)
            {
                var last = new DateTime(lastTicks, DateTimeKind.Utc);
                if (now - last < TimeSpan.FromMinutes(minIntervalMinutes))
                {
                    _logger.LogInformation(
                        "Skipping notification-driven Checkfront sync due to throttle window ({Minutes} minute(s)).",
                        minIntervalMinutes);
                    return null;
                }
            }

            if (Interlocked.CompareExchange(ref _notificationSyncRunning, 1, 0) != 0)
            {
                _logger.LogInformation("Skipping notification-driven Checkfront sync because another sync is running.");
                return null;
            }

            try
            {
                var daysBack = Math.Clamp(_configuration.GetValue<int?>("CheckfrontSync:DaysBack") ?? 30, 1, 365);
                var limitPerPage = Math.Clamp(_configuration.GetValue<int?>("CheckfrontSync:LimitPerPage") ?? 50, 1, 200);
                var maxPages = Math.Clamp(_configuration.GetValue<int?>("CheckfrontSync:MaxPages") ?? 5, 1, 50);
                var result = await SyncCheckfrontBookingsAsync(daysBack, limitPerPage, maxPages, ct);
                Interlocked.Exchange(ref _lastNotificationSyncTicksUtc, DateTime.UtcNow.Ticks);
                return result;
            }
            finally
            {
                Interlocked.Exchange(ref _notificationSyncRunning, 0);
            }
        }

        private async Task<CheckfrontNotificationActivityResult?> TryResolveCheckfrontNotificationActivityAsync(
            CustomerData data,
            string? normalizedText,
            CancellationToken ct)
        {
            try
            {
                var extractedFromBody = CheckfrontNotificationActivityResolver.ExtractWalkerName(normalizedText);
                var effectiveName = FirstNonEmpty(data.CustomerName, extractedFromBody);
                if (string.IsNullOrWhiteSpace(effectiveName))
                {
                    return null;
                }

                data.CustomerName = effectiveName!;
                var resolver = new CheckfrontNotificationActivityResolver(_checkfrontService, _logger);
                return await resolver.ResolveByNameOrRecentActivityAsync(effectiveName, recentDaysBack: 14, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TryResolveCheckfrontNotificationActivityAsync failed.");
                return null;
            }
        }

        private static string BuildCheckfrontNotificationActivityMessage(CheckfrontNotificationActivityResult? activity)
        {
            if (activity == null || string.IsNullOrWhiteSpace(activity.WalkerName))
            {
                return string.Empty;
            }

            return $"NameLookup Walker='{activity.WalkerName}', Customers={activity.CustomerMatchCount}, Matches={activity.MatchedBookingCount}, BookingOrConfirmation={activity.BookingOrConfirmationCount}, Modifications={activity.ModificationCount}, Cancellations={activity.CancellationCount}.";
        }

        private static IReadOnlyList<string> GetMissingEssentialFields(CustomerData data)
        {
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(data.BookingCode)) missing.Add(nameof(data.BookingCode));
            if (string.IsNullOrWhiteSpace(data.CustomerName)) missing.Add(nameof(data.CustomerName));
            if (string.IsNullOrWhiteSpace(data.CustomerEmail)) missing.Add(nameof(data.CustomerEmail));
            if (string.IsNullOrWhiteSpace(data.CustomerPhone)) missing.Add(nameof(data.CustomerPhone));
            if (string.IsNullOrWhiteSpace(data.TourName)) missing.Add(nameof(data.TourName));
            if (!data.TourDate.HasValue) missing.Add(nameof(data.TourDate));
            if (string.IsNullOrWhiteSpace(data.TourTime)) missing.Add(nameof(data.TourTime));
            return missing;
        }

        private static int NextDryRunSyntheticId()
            => -Interlocked.Increment(ref _dryRunSyntheticIdentity);

        private Dictionary<string, object?> CaptureObjectFields(object? entity)
        {
            var fields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (entity == null)
            {
                return fields;
            }

            foreach (var prop in entity.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                object? value;
                try
                {
                    value = prop.GetValue(entity);
                }
                catch
                {
                    continue;
                }

                fields[prop.Name] = NormalizeFieldValue(value);
            }

            return fields;
        }

        private static object? NormalizeFieldValue(object? value)
        {
            if (value == null)
            {
                return null;
            }

            if (value is DateTime dateTime)
            {
                return dateTime.ToString("O");
            }

            if (value is DateTimeOffset dateTimeOffset)
            {
                return dateTimeOffset.ToString("O");
            }

            if (value is IEnumerable<string> stringEnumerable)
            {
                return string.Join(",", stringEnumerable);
            }

            if (value is System.Collections.IEnumerable enumerable && value is not string)
            {
                var values = new List<string>();
                foreach (var item in enumerable)
                {
                    values.Add(item?.ToString() ?? string.Empty);
                }

                return string.Join(",", values);
            }

            return value;
        }

        private Dictionary<string, object?> BuildFieldChanges(string entity, Dictionary<string, object?>? before, object? after)
        {
            var afterFields = CaptureObjectFields(after);
            if (!_dryRunVerboseFieldDiff || before == null || before.Count == 0)
            {
                return BuildCompactFieldChanges(entity, afterFields);
            }

            var keys = new HashSet<string>(before.Keys, StringComparer.OrdinalIgnoreCase);
            foreach (var key in afterFields.Keys)
            {
                keys.Add(key);
            }

            var changes = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in keys)
            {
                before.TryGetValue(key, out var beforeValue);
                afterFields.TryGetValue(key, out var afterValue);
                if (!Equals(beforeValue, afterValue))
                {
                    changes[key] = new Dictionary<string, object?>
                    {
                        ["Before"] = beforeValue,
                        ["After"] = afterValue
                    };
                }
            }

            return changes.Count == 0
                ? new Dictionary<string, object?> { ["NoChanges"] = true }
                : changes;
        }

        private static Dictionary<string, object?> BuildCompactFieldChanges(string entity, Dictionary<string, object?> allFields)
        {
            var selected = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            string[] keys = entity switch
            {
                "ProcessedEmail" => new[] { "MessageId", "VendorName", "EmailType", "BookingCode", "CustomerName", "CustomerEmail", "CustomerPhone", "TourName", "TourDate", "TourTime", "NumberOfAttendees", "ProcessingStatus" },
                "Booking" => new[] { "BookingCode", "VendorName", "BookingStatus", "IsActive", "CustomerName", "CustomerEmail", "CustomerPhone", "TourName", "TourDate", "TourTime", "NumberOfAttendees", "NumberOfAdults", "NumberOfChildren" },
                "Customer" => new[] { "Id", "CustomerIdentifier", "FullName", "Email", "PhoneNumber", "BookingIds", "TotalBookings" },
                "InboxEmail" => new[] { "MessageId", "Subject", "FromEmail", "ReceivedDate", "ProcessingStatus" },
                _ => allFields.Keys.ToArray()
            };

            foreach (var key in keys)
            {
                if (allFields.TryGetValue(key, out var value))
                {
                    selected[key] = value;
                }
            }

            return selected;
        }

        private void LogDryRunWrite(string operation, string entity, string key, Dictionary<string, object?> fieldChanges)
        {
            _logger.LogInformation(
                "DryRunWrite Operation={Operation} Entity={Entity} Key={Key} FieldChanges={@FieldChanges}",
                operation,
                entity,
                key,
                fieldChanges);
        }

        private async Task UpdateInboxProcessingStatusAsyncWithDryRun(int inboxEmailId, TourEmailInboxProcessingStatus status, bool dryRunWrites, CancellationToken ct)
        {
            if (!dryRunWrites)
            {
                await _repository.UpdateInboxProcessingStatusAsync(inboxEmailId, status, ct);
                return;
            }

            LogDryRunWrite(
                "Update",
                "InboxEmail",
                $"InboxEmailId:{inboxEmailId}",
                new Dictionary<string, object?>
                {
                    ["InboxEmailId"] = inboxEmailId,
                    ["ProcessingStatus"] = status.ToString()
                });
        }

        private async Task<int> CreateInboxEmailAsyncWithDryRun(InboxEmailRecord inbox, bool dryRunWrites, CancellationToken ct)
        {
            if (!dryRunWrites)
            {
                return await _repository.CreateInboxEmailAsync(inbox, ct);
            }

            var syntheticId = NextDryRunSyntheticId();
            inbox.Id = syntheticId;
            LogDryRunWrite("Insert", "InboxEmail", inbox.MessageId, BuildFieldChanges("InboxEmail", null, inbox));
            return syntheticId;
        }

        private async Task<int> UpsertProcessedEmailAsyncWithDryRun(ProcessedEmailRecord rec, bool dryRunWrites, CancellationToken ct)
        {
            if (!dryRunWrites)
            {
                return await _processedEmailRepository.UpsertProcessedEmailAsync(rec, ct);
            }

            var syntheticId = rec.Id > 0 ? rec.Id : NextDryRunSyntheticId();
            rec.Id = syntheticId;
            LogDryRunWrite("Upsert", "ProcessedEmail", rec.MessageId, BuildFieldChanges("ProcessedEmail", null, rec));
            return syntheticId;
        }

        private async Task SetLatestActionForThreadAsyncWithDryRun(string vendorName, string? bookingCode, int processedEmailId, bool dryRunWrites, CancellationToken ct)
        {
            if (!dryRunWrites)
            {
                await _processedEmailRepository.SetLatestActionForThreadAsync(vendorName, bookingCode, processedEmailId, ct);
                return;
            }

            LogDryRunWrite(
                "Update",
                "ProcessedEmail",
                $"{vendorName}:{bookingCode ?? "(null)"}",
                new Dictionary<string, object?>
                {
                    ["VendorName"] = vendorName,
                    ["BookingCode"] = bookingCode,
                    ["ProcessedEmailId"] = processedEmailId,
                    ["SetLatestAction"] = true
                });
        }

        private async Task UpdateInboxOriginalBookingAsyncWithDryRun(
            int inboxEmailId,
            int? originalBookingId,
            string? originalBookingCode,
            string? originalMessageId,
            bool dryRunWrites,
            CancellationToken ct)
        {
            if (!dryRunWrites)
            {
                await _repository.UpdateInboxOriginalBookingAsync(inboxEmailId, originalBookingId, originalBookingCode, originalMessageId, ct);
                return;
            }

            LogDryRunWrite(
                "Update",
                "InboxEmail",
                $"InboxEmailId:{inboxEmailId}",
                new Dictionary<string, object?>
                {
                    ["OriginalBookingId"] = originalBookingId,
                    ["OriginalBookingCode"] = originalBookingCode,
                    ["OriginalMessageId"] = originalMessageId
                });
        }

        private async Task<int> CreateCustomerAsyncWithDryRun(Customer customer, bool dryRunWrites, CancellationToken ct)
        {
            if (!dryRunWrites)
            {
                return await _customerRepository.CreateAsync(customer, ct);
            }

            var syntheticId = customer.Id > 0 ? customer.Id : NextDryRunSyntheticId();
            customer.Id = syntheticId;
            LogDryRunWrite("Insert", "Customer", customer.CustomerIdentifier ?? syntheticId.ToString(), BuildFieldChanges("Customer", null, customer));
            return syntheticId;
        }

        private async Task UpdateCustomerAsyncWithDryRun(Customer customer, Dictionary<string, object?> before, bool dryRunWrites, CancellationToken ct)
        {
            if (!dryRunWrites)
            {
                await _customerRepository.UpdateAsync(customer, ct);
                return;
            }

            LogDryRunWrite("Update", "Customer", customer.CustomerIdentifier ?? customer.Id.ToString(), BuildFieldChanges("Customer", before, customer));
        }

        private async Task<int> CreateBookingAsyncWithDryRun(Booking booking, bool dryRunWrites, CancellationToken ct)
        {
            if (!dryRunWrites)
            {
                return await _bookingRepository.CreateAsync(booking, ct);
            }

            var syntheticId = booking.Id > 0 ? booking.Id : NextDryRunSyntheticId();
            booking.Id = syntheticId;
            LogDryRunWrite("Insert", "Booking", booking.BookingCode ?? syntheticId.ToString(), BuildFieldChanges("Booking", null, booking));
            return syntheticId;
        }

        private async Task UpdateBookingAsyncWithDryRun(Booking booking, Dictionary<string, object?> before, bool dryRunWrites, CancellationToken ct)
        {
            if (!dryRunWrites)
            {
                await _bookingRepository.UpdateAsync(booking, ct);
                return;
            }

            LogDryRunWrite("Update", "Booking", booking.BookingCode ?? booking.Id.ToString(), BuildFieldChanges("Booking", before, booking));
        }

        private async Task<int> CancelBookingAsyncWithDryRun(string bookingCode, string? cancellationReason, bool dryRunWrites, CancellationToken ct)
        {
            if (!dryRunWrites)
            {
                return await _bookingRepository.CancelAsync(bookingCode, cancellationReason, ct);
            }

            LogDryRunWrite(
                "Update",
                "Booking",
                bookingCode,
                new Dictionary<string, object?>
                {
                    ["BookingCode"] = bookingCode,
                    ["CancellationReason"] = cancellationReason,
                    ["BookingStatus"] = "Cancelled",
                    ["IsActive"] = false
                });
            return 1;
        }

        private async Task DeactivateOriginalOnModificationAsyncWithDryRun(string originalBookingCode, string newBookingCode, bool dryRunWrites, CancellationToken ct)
        {
            if (!dryRunWrites)
            {
                await _bookingRepository.DeactivateOriginalOnModificationAsync(originalBookingCode, newBookingCode, ct);
                return;
            }

            LogDryRunWrite(
                "Update",
                "Booking",
                originalBookingCode,
                new Dictionary<string, object?>
                {
                    ["OriginalBookingCode"] = originalBookingCode,
                    ["NewBookingCode"] = newBookingCode,
                    ["IsActive"] = false
                });
        }

        private async Task<string?> InsertCalendarEventAsyncWithDryRun(Email.Calendar.Models.GoogleAppointmentModel eventData, Booking booking, bool dryRunWrites)
        {
            if (!dryRunWrites)
            {
                return await _googleCalendarService.InsertEventAsync(eventData);
            }

            var syntheticCalendarId = $"DRYRUN-CAL-{Guid.NewGuid():N}";
            LogDryRunWrite(
                "Insert",
                "CalendarEvent",
                booking.BookingCode ?? syntheticCalendarId,
                new Dictionary<string, object?>
                {
                    ["Subject"] = eventData.Subject,
                    ["Start"] = eventData.StartTime,
                    ["End"] = eventData.EndTime,
                    ["Location"] = eventData.Location
                });
            return syntheticCalendarId;
        }

        private async Task UpdateCalendarEventAsyncWithDryRun(Email.Calendar.Models.GoogleAppointmentModel eventData, Booking booking, bool dryRunWrites)
        {
            if (!dryRunWrites)
            {
                await _googleCalendarService.UpdateEventAsync(eventData);
                return;
            }

            LogDryRunWrite(
                "Update",
                "CalendarEvent",
                eventData.Id ?? booking.CalendarEventId ?? booking.BookingCode ?? "unknown",
                new Dictionary<string, object?>
                {
                    ["Subject"] = eventData.Subject,
                    ["Start"] = eventData.StartTime,
                    ["End"] = eventData.EndTime,
                    ["Location"] = eventData.Location
                });
        }

        private static string NormalizeText(string? textBody, string? htmlBody)
        {
            if (!string.IsNullOrWhiteSpace(textBody))
            {
                return textBody;
            }
            if (!string.IsNullOrWhiteSpace(htmlBody))
            {
                // crude fallback: strip tags minimally
                var result = htmlBody.Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
                                     .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
                                     .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase);
                return System.Text.RegularExpressions.Regex.Replace(result, "<.*?>", string.Empty);
            }
            return string.Empty;
        }

        private static ClassificationRuleRecord? FindBestRule(InboxEmailRecord inbox, IReadOnlyList<ClassificationRuleRecord> rules)
        {
            if (rules.Count == 0) return null;
            var domain = ExtractDomain(inbox.FromEmail);
            var subject = inbox.Subject ?? string.Empty;
            var normalizedSubject = NormalizeSubjectForRuleMatch(subject);

            foreach (var r in rules.OrderByDescending(r => r.Priority).ThenBy(r => r.Id))
            {
                // Guardrail: Checkfront/Viator vendor rules must carry an explicit sender domain.
                // This prevents broad catch-all rules (empty domain) from overmatching unrelated mail.
                if (RequiresExplicitDomainForVendorRule(r.VendorName) &&
                    string.IsNullOrWhiteSpace(r.Domain))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(r.Domain) &&
                    !string.Equals(r.Domain, domain, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(r.SubjectPhrase) &&
                    subject.IndexOf(r.SubjectPhrase, StringComparison.OrdinalIgnoreCase) < 0 &&
                    normalizedSubject.IndexOf(r.SubjectPhrase, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                return r;
            }

            return null;
        }

        private static string ExtractDomain(string email)
        {
            var at = email.IndexOf('@');
            if (at >= 0 && at + 1 < email.Length)
            {
                return email[(at + 1)..].Trim().ToLowerInvariant();
            }
            return email.Trim().ToLowerInvariant();
        }

        private static string NormalizeSubjectForRuleMatch(string subject)
        {
            if (string.IsNullOrWhiteSpace(subject))
            {
                return string.Empty;
            }

            var normalized = subject.Trim();
            while (true)
            {
                if (normalized.StartsWith("fwd:", StringComparison.OrdinalIgnoreCase))
                {
                    normalized = normalized[4..].TrimStart();
                    continue;
                }

                if (normalized.StartsWith("fw:", StringComparison.OrdinalIgnoreCase))
                {
                    normalized = normalized[3..].TrimStart();
                    continue;
                }

                if (normalized.StartsWith("re:", StringComparison.OrdinalIgnoreCase))
                {
                    normalized = normalized[3..].TrimStart();
                    continue;
                }

                return normalized;
            }
        }

		/// <summary>
		/// Added: 2025-11-09 00:00 UTC
		/// Global bucketing by email type, enforcing strict overall sequence:
		/// 1) Booking/Confirmation, 2) Modification, 3) Cancellation, 4) Other.
		/// Within each bucket we order by ReceivedDate (fallback CollectedAt).
		/// </summary>
		private List<InboxEmailRecord> BucketInboxByGlobalPriority(IReadOnlyList<InboxEmailRecord> inboxEmails, IReadOnlyList<ClassificationRuleRecord> rules)
		{
			var bookings = new List<InboxEmailRecord>();
			var modifications = new List<InboxEmailRecord>();
			var cancellations = new List<InboxEmailRecord>();
			var others = new List<InboxEmailRecord>();

			foreach (var i in inboxEmails)
			{
				var r = FindBestRule(i, rules);
				var type = r?.EmailType ?? string.Empty;
				if (type.Equals("Booking", StringComparison.OrdinalIgnoreCase) ||
					type.Equals("Confirmation", StringComparison.OrdinalIgnoreCase))
				{
					bookings.Add(i);
				}
				else if (type.Equals("Modification", StringComparison.OrdinalIgnoreCase))
				{
					modifications.Add(i);
				}
				else if (type.Equals("Cancellation", StringComparison.OrdinalIgnoreCase))
				{
					cancellations.Add(i);
				}
				else
				{
					others.Add(i);
				}
			}

			static DateTime SortKey(InboxEmailRecord x)
			{
				var rd = x.ReceivedDate;
				if (rd != default) return rd;
				var cd = x.CollectedAt;
				return cd != default ? cd : DateTime.UtcNow;
			}

			// Stable, per-bucket time ordering
			bookings = bookings.OrderBy(SortKey).ToList();
			modifications = modifications.OrderBy(SortKey).ToList();
			cancellations = cancellations.OrderBy(SortKey).ToList();
			others = others.OrderBy(SortKey).ToList();

			// Concatenate in strict global order
			var result = new List<InboxEmailRecord>(bookings.Count + modifications.Count + cancellations.Count + others.Count);
			result.AddRange(bookings);
			result.AddRange(modifications);
			result.AddRange(cancellations);
			result.AddRange(others);
			return result;
		}

		/// <summary>
		/// Added (stub): 2025-11-09 00:00 UTC
		/// Placeholder for post-collection processing ordering; not wired yet.
		/// This will likely reuse BucketInboxByGlobalPriority when collection feeds a fresh batch.
		/// </summary>
		private List<InboxEmailRecord> OrderInboxForPostCollectionProcessing(IReadOnlyList<InboxEmailRecord> inboxEmails, IReadOnlyList<ClassificationRuleRecord> rules)
		{
			return BucketInboxByGlobalPriority(inboxEmails, rules);
		}

		private List<InboxEmailRecord> OrderInboxByPriority(IReadOnlyList<InboxEmailRecord> inboxEmails, IReadOnlyList<ClassificationRuleRecord> rules)
        {
            var list = new List<(InboxEmailRecord inbox, string vendor, string type)>();
            foreach (var i in inboxEmails)
            {
                var r = FindBestRule(i, rules);
                list.Add((i, r?.VendorName ?? "General", r?.EmailType ?? string.Empty));
            }

            static int Priority(string type)
            {
                if (type.Equals("Booking", StringComparison.OrdinalIgnoreCase) ||
                    type.Equals("Confirmation", StringComparison.OrdinalIgnoreCase))
                    return 0;
                if (type.Equals("Modification", StringComparison.OrdinalIgnoreCase))
                    return 1;
                if (type.Equals("Cancellation", StringComparison.OrdinalIgnoreCase))
                    return 2;
                return 3;
            }

            string ThreadKey((InboxEmailRecord inbox, string vendor, string type) x)
            {
                // Use vendor + booking code extracted from subject/text if available; fallback to vendor only
                var maybeCode = ExtractBookingCode(x.inbox.Subject) ?? ExtractBookingCode(x.inbox.TextBody) ?? ExtractBookingCode(x.inbox.HtmlBody);
                return $"{x.vendor}|{maybeCode ?? ""}".ToLowerInvariant();
            }

            return list
                .OrderBy(x => ThreadKey(x)) // group by thread
                .ThenBy(x => Priority(x.type))
                .ThenBy(x =>
                {
                    // InboxEmailRecord timestamps may be non-nullable; use defaults-aware fallback
                    var rd = x.inbox.ReceivedDate;
                    if (rd != default) return rd;
                    var cd = x.inbox.CollectedAt;
                    return cd != default ? cd : DateTime.UtcNow;
                })
                .Select(x => x.inbox)
                .ToList();
        }

        private VendorParseResult ParseVendor(string vendorName, string emailType, string? subject, string? htmlBody, string? normalizedText)
        {
            if (vendorName.Equals("FreeTour", StringComparison.OrdinalIgnoreCase))
            {
                if (emailType.Equals("Confirmation", StringComparison.OrdinalIgnoreCase) ||
                    emailType.Equals("Booking", StringComparison.OrdinalIgnoreCase))
                {
                    return new FreeTourReservationParser().Parse(subject, htmlBody, normalizedText);
                }
                if (emailType.Equals("Modification", StringComparison.OrdinalIgnoreCase))
                {
                    return new FreeTourModificationParser().Parse(subject, htmlBody, normalizedText);
                }
                if (emailType.Equals("Cancellation", StringComparison.OrdinalIgnoreCase))
                {
                    return new FreeTourCancellationParser().Parse(subject, htmlBody, normalizedText);
                }
            }

            if (vendorName.Equals("GuruWalk", StringComparison.OrdinalIgnoreCase))
            {
                if (emailType.Equals("Confirmation", StringComparison.OrdinalIgnoreCase) ||
                    emailType.Equals("Booking", StringComparison.OrdinalIgnoreCase))
                {
                    return new GuruWalkConfirmationParser().Parse(subject, htmlBody, normalizedText);
                }
                if (emailType.Equals("Modification", StringComparison.OrdinalIgnoreCase))
                {
                    return new GuruWalkModificationParser().Parse(subject, htmlBody, normalizedText);
                }
                if (emailType.Equals("Cancellation", StringComparison.OrdinalIgnoreCase))
                {
                    return new GuruWalkCancellationParser().Parse(subject, htmlBody, normalizedText);
                }
            }

            if (vendorName.Equals("Viator", StringComparison.OrdinalIgnoreCase))
            {
                if (emailType.Equals("Confirmation", StringComparison.OrdinalIgnoreCase) ||
                    emailType.Equals("Booking", StringComparison.OrdinalIgnoreCase))
                {
                    return new ViatorBookingParserV2().Parse(subject, htmlBody, normalizedText);
                }
                if (emailType.Equals("Cancellation", StringComparison.OrdinalIgnoreCase))
                {
                    return new ViatorCancellationParser().Parse(subject, htmlBody, normalizedText);
                }
            }

            if (IsCheckfrontVendor(vendorName))
            {
                if (emailType.Equals("Confirmation", StringComparison.OrdinalIgnoreCase) ||
                    emailType.Equals("Booking", StringComparison.OrdinalIgnoreCase))
                {
                    return new ViatorBookingParserV2().Parse(subject, htmlBody, normalizedText);
                }
                if (emailType.Equals("Cancellation", StringComparison.OrdinalIgnoreCase))
                {
                    return new ViatorCancellationParser().Parse(subject, htmlBody, normalizedText);
                }
            }

            // Fallback: attempt minimal extraction from subject/text
            var fallback = new VendorParseResult { VendorName = vendorName, EmailType = emailType, Data = _extractionService.Extract(subject, htmlBody, normalizedText) };
            return fallback;
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        private static bool IsCheckfrontVendor(string? vendorName)
        {
            if (string.IsNullOrWhiteSpace(vendorName))
            {
                return false;
            }

            return vendorName.Equals("Checkfront", StringComparison.OrdinalIgnoreCase) ||
                   vendorName.Equals("GetYourGuide", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsViatorVendor(string? vendorName)
        {
            if (string.IsNullOrWhiteSpace(vendorName))
            {
                return false;
            }

            return vendorName.Equals("Viator", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        private static bool IsCheckfrontSender(string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return false;
            }

            var domain = ExtractDomain(email);
            return domain.EndsWith("checkfront.com", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsViatorSender(string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return false;
            }

            var domain = ExtractDomain(email);
            return domain.Equals("t1.viator.com", StringComparison.OrdinalIgnoreCase) ||
                   domain.EndsWith(".viator.com", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCheckfrontWriteContext(string? vendorName, string? fromEmail)
        {
            if (IsCheckfrontVendor(vendorName))
            {
                return true;
            }

            return IsCheckfrontSender(fromEmail);
        }

        private static bool IsViatorContext(
            string? vendorName,
            string? fromEmail,
            string? subject,
            string? normalizedText,
            CustomerData? data = null)
        {
            if (IsViatorVendor(vendorName))
            {
                return true;
            }

            if (IsViatorSender(fromEmail))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Checkfront/Viator-only context detector for dry-run write safety.
        /// </summary>
        private static bool IsCheckfrontViatorContext(
            string? vendorName,
            string? fromEmail,
            string? subject,
            string? normalizedText,
            CustomerData? data = null)
        {
            return IsCheckfrontWriteContext(vendorName, fromEmail) ||
                   IsViatorContext(vendorName, fromEmail, subject, normalizedText, data);
        }

        private bool ShouldDryRunWrites(
            string? vendorName,
            string? fromEmail,
            string? subject,
            string? normalizedText,
            CustomerData? data = null)
        {
            var isCheckfrontContext = IsCheckfrontWriteContext(vendorName, fromEmail);
            var isViatorContext = IsViatorContext(vendorName, fromEmail, subject, normalizedText, data);

            // Backward compatibility: if new split keys are not configured, keep legacy behavior.
            if (!_hasDryRunWritesViatorSetting && !_hasDryRunWritesCheckfrontOnlySetting)
            {
                return _dryRunWritesCheckfrontViatorOnly && (isCheckfrontContext || isViatorContext);
            }

            if (isViatorContext && _dryRunWritesViator)
            {
                return true;
            }

            if (isCheckfrontContext && _dryRunWritesCheckfrontOnly)
            {
                return true;
            }

            return false;
        }

        private static bool IsViatorBookingNotification(string? subject, string? normalizedText)
        {
            var haystack = string.Join("\n", new[] { subject ?? string.Empty, normalizedText ?? string.Empty });
            return haystack.IndexOf("Viator booking notification", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// Updated: 2026-03-26 00:00 UTC - Resolve numeric/alphanumeric Checkfront customer ids for booking lookup.
        /// </summary>
        private static string ResolveCheckfrontCustomerLookupId(Email.Models.CheckfrontCustomerInfo? customer)
        {
            if (customer == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(customer.CustomerIdRaw))
            {
                return customer.CustomerIdRaw.Trim();
            }

            return customer.CustomerId > 0 ? customer.CustomerId.ToString() : string.Empty;
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// Updated: 2026-03-26 00:00 UTC - Pull-on-parse enrichment from Checkfront booking/customer endpoints.
        /// </summary>
        private async Task<CheckfrontBooking?> TryEnrichFromCheckfrontAsync(CustomerData data, string? subject, string? normalizedText, CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                var bookingCode = FirstNonEmpty(
                    data.BookingCode,
                    data.ExtractedBookingCode,
                    ExtractBookingCode(subject),
                    ExtractBookingCode(normalizedText));

                if (!string.IsNullOrWhiteSpace(bookingCode))
                {
                    var byCode = await _checkfrontService.GetBookingByCodeOrIdAsync(bookingCode!);
                    if (byCode != null)
                    {
                        MergeCheckfrontIntoCustomerData(data, byCode);
                        return byCode;
                    }

                    _logger.LogInformation(
                        "Checkfront enrichment booking lookup miss for code {BookingCode}.",
                        bookingCode);
                }

                var lookupEmail = FirstNonEmpty(
                    data.CustomerEmail,
                    ExtractFirstEmailFromText(normalizedText),
                    ExtractFirstEmailFromText(subject));
                lookupEmail = EmailNormalizationService.NormalizeEmail(lookupEmail);
                if (string.IsNullOrWhiteSpace(lookupEmail))
                {
                    return null;
                }

                var customerMatches = await _checkfrontService.SearchCustomersByEmailAsync(lookupEmail);
                if (customerMatches.Count == 0)
                {
                    _logger.LogInformation(
                        "Checkfront enrichment customer lookup miss for email {Email}.",
                        lookupEmail);
                    return null;
                }

                var bookingsByCustomer = new List<CheckfrontBooking>();
                var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var match in customerMatches
                    .OrderByDescending(m => m.ConfidenceScore)
                    .ThenBy(m => ResolveCheckfrontCustomerLookupId(m.Customer)))
                {
                    ct.ThrowIfCancellationRequested();
                    var customerLookupId = ResolveCheckfrontCustomerLookupId(match.Customer);
                    if (string.IsNullOrWhiteSpace(customerLookupId))
                    {
                        continue;
                    }

                    var customerBookings = await _checkfrontService.GetBookingsByCustomerIdAsync(customerLookupId, limit: 50, page: 1);
                    foreach (var booking in customerBookings)
                    {
                        var key = $"{booking.BookingId}|{booking.Code}";
                        if (seenKeys.Add(key))
                        {
                            bookingsByCustomer.Add(booking);
                        }
                    }
                }

                var selected = SelectBestCheckfrontBooking(bookingsByCustomer, bookingCode);
                if (selected != null)
                {
                    MergeCheckfrontIntoCustomerData(data, selected);
                }

                return selected;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TryEnrichFromCheckfrontAsync failed for Subject={Subject}", subject ?? string.Empty);
                Console.WriteLine($"[DEBUG] GmailProcessingV2Service.TryEnrichFromCheckfrontAsync error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// Updated: 2026-03-26 00:00 UTC - Reconciles one Checkfront booking with existing email-derived records.
        /// </summary>
        private async Task<bool> SyncSingleCheckfrontBookingAsync(
            CheckfrontBooking sourceBooking,
            CancellationToken ct,
            string? vendorOverride = null,
            bool skipDetailLookup = false)
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                var bookingCode = FirstNonEmpty(
                    sourceBooking.Code,
                    sourceBooking.BookingId > 0 ? sourceBooking.BookingId.ToString() : null);
                if (string.IsNullOrWhiteSpace(bookingCode))
                {
                    _logger.LogWarning("Skipping Checkfront sync booking with missing code and id.");
                    return false;
                }

                var detailBooking = skipDetailLookup
                    ? sourceBooking
                    : await _checkfrontService.GetBookingByCodeOrIdAsync(bookingCode!) ?? sourceBooking;

                var data = new CustomerData();
                MergeCheckfrontIntoCustomerData(data, detailBooking);
                data.BookingCode = FirstNonEmpty(data.BookingCode, bookingCode!) ?? bookingCode!;
                data.ExtractedBookingCode = data.BookingCode;
                data.IsCancellation = IsCancelledStatus(detailBooking);
                data.IsBooking = !data.IsCancellation;
                data.IsModification = false;
                if (data.IsCancellation && string.IsNullOrWhiteSpace(data.PreviousBookingCode))
                {
                    data.PreviousBookingCode = data.BookingCode;
                }

                var latestProcessed = await TryGetLatestProcessedRecordByBookingCodeAsync(data.BookingCode, ct);
                MergeProcessedRecordIntoCustomerData(data, latestProcessed);

                if (data.NumberOfAttendees > MaxReasonableAttendeeCount)
                {
                    data.NumberOfAttendees = 0;
                }

                if (data.NumberOfAdults > MaxReasonableAttendeeCount)
                {
                    data.NumberOfAdults = 0;
                }

                if (data.NumberOfChildren > MaxReasonableAttendeeCount)
                {
                    data.NumberOfChildren = 0;
                }

                data.CustomerEmail = EmailNormalizationService.NormalizeEmail(data.CustomerEmail);
                data.CustomerPhone = PhoneNormalizationService.NormalizePhone(data.CustomerPhone);
                if (data.NumberOfAttendees == 0 && (data.NumberOfAdults > 0 || data.NumberOfChildren > 0))
                {
                    data.NumberOfAttendees = data.NumberOfAdults + data.NumberOfChildren;
                }

                ApplyPriceBasedAttendeeFallback(
                    data,
                    vendorName: "Checkfront",
                    isViatorContext: false,
                    normalizedText: null,
                    checkfrontBooking: detailBooking,
                    diagnosticContext: "checkfront_sync");
                ApplyAttendeeSplitFallback(data, "Checkfront", "checkfront_sync");

                string? countryName = null;
                if (!string.IsNullOrWhiteSpace(data.CustomerPhone) &&
                    CountryExtractor.TryGetCountryNameFromPhone(data.CustomerPhone, out var country))
                {
                    countryName = country;
                }

                var existingBooking = await _bookingRepository.FindByCodeAsync(data.BookingCode, ct);
                var lockedVendor = ResolveLockedVendorFromOverrides(existingBooking, latestProcessed);
                var vendorName = !string.IsNullOrWhiteSpace(lockedVendor)
                    ? lockedVendor
                    : (string.IsNullOrWhiteSpace(vendorOverride)
                        ? InferCheckfrontVendor(null, null, data, detailBooking)
                        : vendorOverride.Trim());
                var emailType = data.IsCancellation ? "Cancellation" : "Booking";
                var dryRunWrites = ShouldDryRunWrites(vendorName, "checkfront-sync@system.local", null, data.TourName, data);
                var existingAttendeeBaseline = existingBooking?.NumberOfAttendees ?? 0;

                if (existingBooking != null &&
                    existingAttendeeBaseline > 0 &&
                    existingAttendeeBaseline <= MaxReasonableAttendeeCount &&
                    !HasExplicitAttendeeTotals(detailBooking))
                {
                    data.NumberOfAttendees = existingAttendeeBaseline;
                    data.NumberOfAdults = Math.Max(0, existingBooking.NumberOfAdults ?? 0);
                    data.NumberOfChildren = Math.Max(0, existingBooking.NumberOfChildren ?? 0);
                }

                var attendeeHint = Math.Max(
                    Math.Max(0, data.NumberOfAttendees),
                    Math.Max(0, existingAttendeeBaseline));
                var checkfrontAmount = ResolveCheckfrontAmountForPersistence(
                    detailBooking,
                    attendeeHint,
                    existingBooking?.BookingAmount);
                var customerResolutionType = existingBooking == null && !data.IsCancellation ? "Booking" : "Sync";

                var customer = await ResolveCustomerAsync(data, customerResolutionType, dryRunWrites, ct);
                customer = await EnsureCustomerForBookingAsync(customer, data, vendorName, customerResolutionType, dryRunWrites, ct);
                var messageId = FirstNonEmpty(latestProcessed?.MessageId, existingBooking?.MessageId);
                var processedEmailId = latestProcessed?.Id ?? existingBooking?.ProcessedEmailId ?? 0;

                if (latestProcessed != null &&
                    !string.IsNullOrWhiteSpace(latestProcessed.MessageId))
                {
                    var refreshed = await RefreshExistingProcessedForCheckfrontSyncAsync(
                        data,
                        vendorName,
                        emailType,
                        latestProcessed,
                        dryRunWrites,
                        ct);
                    messageId = refreshed.MessageId;
                    processedEmailId = refreshed.ProcessedEmailId;
                }
                else if (string.IsNullOrWhiteSpace(messageId) || processedEmailId <= 0)
                {
                    var synthetic = await EnsureSyntheticProcessedForCheckfrontSyncAsync(data, vendorName, emailType, dryRunWrites, ct);
                    messageId = synthetic.MessageId;
                    processedEmailId = synthetic.ProcessedEmailId;
                }

                if (existingBooking == null)
                {
                    var booking = BuildBooking(
                        customer,
                        data,
                        processedEmailId,
                        messageId!,
                        vendorName,
                        emailType,
                        isConfirmation: !data.IsCancellation,
                        isModification: false,
                        isCancellation: data.IsCancellation,
                        countryName);
                    booking.CustomerId = customer.Id;
                    booking.IsActive = !data.IsCancellation;
                    booking.BookingStatus = data.IsCancellation ? "Cancelled" : "Confirmed";
                    booking.BookingAmount = checkfrontAmount.Amount;
                    booking.Currency = checkfrontAmount.Currency;
                    booking.ProcessingNotes = AppendProcessingNote(
                        booking.ProcessingNotes,
                        $"Checkfront sync created booking on {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC.");
                    await CreateBookingAsyncWithDryRun(booking, dryRunWrites, ct);
                    return true;
                }

                var beforeMerged = CaptureObjectFields(existingBooking);
                var merged = MergeCheckfrontIntoExistingBookingRow(
                    existingBooking,
                    customer,
                    data,
                    vendorName,
                    emailType,
                    messageId!,
                    processedEmailId,
                    countryName,
                    checkfrontAmount.Amount,
                    checkfrontAmount.Currency);
                await UpdateBookingAsyncWithDryRun(merged, beforeMerged, dryRunWrites, ct);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SyncSingleCheckfrontBookingAsync failed for booking {BookingCode}", sourceBooking.Code);
                Console.WriteLine($"[DEBUG] GmailProcessingV2Service.SyncSingleCheckfrontBookingAsync error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        private async Task<ProcessedEmailRecord?> TryGetLatestProcessedRecordByBookingCodeAsync(string bookingCode, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(bookingCode))
                {
                    return null;
                }

                var processedRows = await _repository.GetProcessedByBookingCodeAsync(bookingCode, ct);
                var latest = processedRows
                    .OrderByDescending(r => r.ProcessingCompletedAt ?? r.ExtractedAt ?? DateTime.MinValue)
                    .ThenByDescending(r => r.Id)
                    .FirstOrDefault();
                if (latest == null || string.IsNullOrWhiteSpace(latest.MessageId))
                {
                    return null;
                }

                return await _repository.GetProcessedFullByMessageIdAsync(latest.MessageId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TryGetLatestProcessedRecordByBookingCodeAsync failed for booking {BookingCode}", bookingCode);
                Console.WriteLine($"[DEBUG] GmailProcessingV2Service.TryGetLatestProcessedRecordByBookingCodeAsync error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        private static void MergeProcessedRecordIntoCustomerData(CustomerData data, ProcessedEmailRecord? processed)
        {
            if (processed == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(data.CustomerName) && !string.IsNullOrWhiteSpace(processed.CustomerName))
            {
                data.CustomerName = processed.CustomerName;
            }

            if (string.IsNullOrWhiteSpace(data.CustomerEmail) && !string.IsNullOrWhiteSpace(processed.CustomerEmail))
            {
                data.CustomerEmail = processed.CustomerEmail;
            }

            if (string.IsNullOrWhiteSpace(data.CustomerPhone) && !string.IsNullOrWhiteSpace(processed.CustomerPhone))
            {
                data.CustomerPhone = processed.CustomerPhone;
            }

            if (string.IsNullOrWhiteSpace(data.TourName) && !string.IsNullOrWhiteSpace(processed.TourName))
            {
                data.TourName = processed.TourName;
            }

            if (string.IsNullOrWhiteSpace(data.TourLocation) && !string.IsNullOrWhiteSpace(processed.TourLocation))
            {
                data.TourLocation = processed.TourLocation;
            }

            if (string.IsNullOrWhiteSpace(data.TourTime) && !string.IsNullOrWhiteSpace(processed.TourTime))
            {
                data.TourTime = processed.TourTime;
            }

            if (!data.TourDate.HasValue &&
                !string.IsNullOrWhiteSpace(processed.TourDate) &&
                DateTime.TryParse(processed.TourDate, out var parsedDate))
            {
                data.TourDate = parsedDate.Date;
            }

            if (data.NumberOfAttendees == 0 && processed.NumberOfAttendees.HasValue && processed.NumberOfAttendees.Value > 0)
            {
                data.NumberOfAttendees = processed.NumberOfAttendees.Value;
            }

            if (data.NumberOfAdults == 0 && processed.NumberOfAdults.HasValue && processed.NumberOfAdults.Value > 0)
            {
                data.NumberOfAdults = processed.NumberOfAdults.Value;
            }

            if (data.NumberOfChildren == 0 && processed.NumberOfChildren.HasValue && processed.NumberOfChildren.Value > 0)
            {
                data.NumberOfChildren = processed.NumberOfChildren.Value;
            }

            if (string.IsNullOrWhiteSpace(data.Language) && !string.IsNullOrWhiteSpace(processed.Language))
            {
                data.Language = processed.Language;
            }
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        private async Task<(string MessageId, int ProcessedEmailId)> EnsureSyntheticProcessedForCheckfrontSyncAsync(
            CustomerData data,
            string vendorName,
            string emailType,
            bool dryRunWrites,
            CancellationToken ct)
        {
            try
            {
                var now = DateTime.UtcNow;
                var messageId = BuildCheckfrontSyncMessageId(data.BookingCode);
                var existingInboxId = await _repository.GetInboxEmailIdByMessageIdAsync(messageId, ct);
                var inboxTextBody = BuildCheckfrontSyncTextBody(data);
                var inboxId = existingInboxId.GetValueOrDefault();

                if (inboxId <= 0)
                {
                    var inbox = new InboxEmailRecord
                    {
                        MessageId = messageId,
                        Uid = now.Ticks,
                        Subject = $"Checkfront Sync - {data.BookingCode}",
                        FromEmail = "checkfront-sync@system.local",
                        FromName = "Checkfront Sync",
                        ToEmail = "system@local",
                        ReceivedDate = now,
                        CollectedAt = now,
                        TextBody = inboxTextBody,
                        HtmlBody = string.Empty,
                        CollectionBatchId = "CHECKFRONT_SYNC",
                        IsRead = true,
                        CreatedAt = now,
                        UpdatedAt = now,
                        ProcessingStatus = TourEmailInboxProcessingStatus.Processed
                    };
                    inboxId = await CreateInboxEmailAsyncWithDryRun(inbox, dryRunWrites, ct);
                }

                var rec = new ProcessedEmailRecord
                {
                    InboxEmailId = inboxId,
                    MessageId = messageId,
                    VendorName = vendorName,
                    EmailType = emailType,
                    IsTourBookingEmail = !data.IsCancellation,
                    ClassificationRuleId = null,
                    ProcessingStatus = "completed",
                    ProcessingStartedAt = now,
                    ProcessingCompletedAt = now,
                    ProcessingError = null,
                    ProcessingAttempts = 1,
                    NextProcessingAttempt = null,
                    RateLimitResetAt = null,
                    CustomerName = data.CustomerName,
                    BookingCode = SelectBookingCodeForProcessed(data, emailType),
                    CustomerPhone = data.CustomerPhone,
                    CustomerEmail = data.CustomerEmail,
                    NumberOfAttendees = data.NumberOfAttendees,
                    Language = data.Language,
                    TourDate = data.TourDate?.ToString("yyyy-MM-dd"),
                    TourTime = data.TourTime,
                    TourName = data.TourName,
                    TourLocation = data.TourLocation,
                    ActionRequired = null,
                    ExtractedAt = now,
                    CustomerIdentifier = null,
                    RelatedEmailIds = null,
                    IsLatestAction = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                    PlainTextContent = inboxTextBody,
                    HtmlContent = string.Empty,
                    ExtractedBookingCode = data.ExtractedBookingCode ?? data.BookingCode ?? string.Empty,
                    NewBookingCode = data.NewBookingCode ?? string.Empty,
                    PreviousBookingCode = data.PreviousBookingCode ?? string.Empty,
                    IsCancellation = data.IsCancellation,
                    IsModification = false,
                    IsBooking = !data.IsCancellation,
                    NumberOfAdults = data.NumberOfAdults,
                    NumberOfChildren = data.NumberOfChildren
                };

                var processedId = await UpsertProcessedEmailAsyncWithDryRun(rec, dryRunWrites, ct);
                await SetLatestActionForThreadAsyncWithDryRun(vendorName, rec.BookingCode, processedId, dryRunWrites, ct);
                return (messageId, processedId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EnsureSyntheticProcessedForCheckfrontSyncAsync failed for booking {BookingCode}", data.BookingCode);
                Console.WriteLine($"[DEBUG] GmailProcessingV2Service.EnsureSyntheticProcessedForCheckfrontSyncAsync error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Created: 2026-04-04 00:00 UTC
        /// Refreshes an existing processed row with latest Checkfront sync fields so tree reads current values.
        /// </summary>
        private async Task<(string MessageId, int ProcessedEmailId)> RefreshExistingProcessedForCheckfrontSyncAsync(
            CustomerData data,
            string vendorName,
            string emailType,
            ProcessedEmailRecord existingProcessed,
            bool dryRunWrites,
            CancellationToken ct)
        {
            var messageId = existingProcessed.MessageId;
            if (string.IsNullOrWhiteSpace(messageId))
            {
                return await EnsureSyntheticProcessedForCheckfrontSyncAsync(data, vendorName, emailType, dryRunWrites, ct);
            }

            var inboxId = existingProcessed.InboxEmailId;
            if (inboxId <= 0)
            {
                var existingInboxId = await _repository.GetInboxEmailIdByMessageIdAsync(messageId, ct);
                inboxId = existingInboxId.GetValueOrDefault();
            }

            if (inboxId <= 0)
            {
                return await EnsureSyntheticProcessedForCheckfrontSyncAsync(data, vendorName, emailType, dryRunWrites, ct);
            }

            var now = DateTime.UtcNow;
            var bookingCode = SelectBookingCodeForProcessed(data, emailType);
            var processingStatus = data.IsCancellation ? "cancelled" : "completed";
            var numberOfAdults = Math.Max(0, data.NumberOfAdults);
            var numberOfChildren = Math.Max(0, data.NumberOfChildren);
            var numberOfAttendees = data.NumberOfAttendees > 0
                ? data.NumberOfAttendees
                : (numberOfAdults > 0 || numberOfChildren > 0
                    ? numberOfAdults + numberOfChildren
                    : 0);
            var existingPlainText = existingProcessed.PlainTextContent;
            var plainText = string.IsNullOrWhiteSpace(existingPlainText)
                ? BuildCheckfrontSyncTextBody(data)
                : existingPlainText;
            var effectiveVendorName = existingProcessed.VendorManuallyOverridden &&
                                      !string.IsNullOrWhiteSpace(existingProcessed.VendorName)
                ? existingProcessed.VendorName.Trim()
                : vendorName;

            var rec = new ProcessedEmailRecord
            {
                Id = existingProcessed.Id,
                InboxEmailId = inboxId,
                MessageId = messageId,
                VendorName = effectiveVendorName,
                VendorManuallyOverridden = existingProcessed.VendorManuallyOverridden,
                VendorOverrideAt = existingProcessed.VendorOverrideAt,
                EmailType = emailType,
                IsTourBookingEmail = !data.IsCancellation,
                ClassificationRuleId = existingProcessed.ClassificationRuleId,
                ProcessingStatus = processingStatus,
                ProcessingStartedAt = existingProcessed.ProcessingStartedAt ?? now,
                ProcessingCompletedAt = now,
                ProcessingError = null,
                ProcessingAttempts = Math.Max(1, existingProcessed.ProcessingAttempts),
                NextProcessingAttempt = null,
                RateLimitResetAt = null,
                CustomerName = FirstNonEmpty(data.CustomerName, existingProcessed.CustomerName),
                BookingCode = bookingCode,
                CustomerPhone = FirstNonEmpty(data.CustomerPhone, existingProcessed.CustomerPhone),
                CustomerEmail = FirstNonEmpty(data.CustomerEmail, existingProcessed.CustomerEmail),
                NumberOfAttendees = numberOfAttendees,
                Language = FirstNonEmpty(data.Language, existingProcessed.Language),
                TourDate = data.TourDate?.ToString("yyyy-MM-dd") ?? existingProcessed.TourDate,
                TourTime = FirstNonEmpty(data.TourTime, existingProcessed.TourTime),
                TourName = FirstNonEmpty(data.TourName, existingProcessed.TourName),
                TourLocation = FirstNonEmpty(data.TourLocation, existingProcessed.TourLocation),
                ActionRequired = existingProcessed.ActionRequired,
                ExtractedAt = existingProcessed.ExtractedAt ?? now,
                CustomerIdentifier = existingProcessed.CustomerIdentifier,
                RelatedEmailIds = existingProcessed.RelatedEmailIds,
                IsLatestAction = true,
                PlainTextContent = plainText,
                HtmlContent = existingProcessed.HtmlContent ?? string.Empty,
                ExtractedBookingCode = data.ExtractedBookingCode ?? data.BookingCode ?? existingProcessed.ExtractedBookingCode ?? string.Empty,
                NewBookingCode = data.NewBookingCode ?? existingProcessed.NewBookingCode ?? string.Empty,
                PreviousBookingCode = data.PreviousBookingCode ?? existingProcessed.PreviousBookingCode ?? string.Empty,
                IsCancellation = data.IsCancellation,
                IsModification = false,
                IsBooking = !data.IsCancellation,
                NumberOfAdults = numberOfAdults,
                NumberOfChildren = numberOfChildren
            };

            var processedId = await UpsertProcessedEmailAsyncWithDryRun(rec, dryRunWrites, ct);
            await SetLatestActionForThreadAsyncWithDryRun(vendorName, rec.BookingCode, processedId, dryRunWrites, ct);
            return (messageId, processedId);
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        private static Booking MergeCheckfrontIntoExistingBookingRow(
            Booking existing,
            Customer customer,
            CustomerData data,
            string vendorName,
            string emailType,
            string messageId,
            int processedEmailId,
            string? countryName,
            decimal? bookingAmount,
            string currency)
        {
            existing.CustomerId = customer.Id;
            existing.CustomerIdentifier = ComputeCustomerIdentifier(customer, data, vendorName, null);
            existing.ProcessedEmailId = processedEmailId > 0 ? processedEmailId : existing.ProcessedEmailId;
            existing.MessageId = !string.IsNullOrWhiteSpace(messageId) ? messageId : existing.MessageId;
            var isVendorLocked = existing.VendorManuallyOverridden && !string.IsNullOrWhiteSpace(existing.VendorName);
            if (!isVendorLocked)
            {
                existing.VendorName = vendorName;
            }
            existing.EmailType = emailType;
            existing.IsCancellation = data.IsCancellation;
            existing.IsModification = false;
            existing.IsConfirmation = !data.IsCancellation;
            existing.IsActive = !data.IsCancellation;

            existing.TourName = FirstNonEmpty(data.TourName, existing.TourName);
            if (data.TourDate.HasValue)
            {
                var tourDate = data.TourDate.Value.Date;
                existing.TourDate = tourDate;
                existing.TourDayOfWeek = tourDate.DayOfWeek.ToString();
                existing.DisplayDate = tourDate.ToString("MM/dd/yyyy");
            }

            existing.TourTime = FirstNonEmpty(data.TourTime, existing.TourTime);
            if (!string.IsNullOrWhiteSpace(existing.TourTime))
            {
                existing.DisplayTime = TimeStandardizationService.ToDisplayFormat(existing.TourTime);
            }

            existing.TourLocation = FirstNonEmpty(data.TourLocation, existing.TourLocation);
            existing.CustomerName = FirstNonEmpty(data.CustomerName, existing.CustomerName) ?? existing.CustomerName;
            existing.CustomerEmail = FirstNonEmpty(data.CustomerEmail, existing.CustomerEmail);
            existing.CustomerPhone = FirstNonEmpty(data.CustomerPhone, existing.CustomerPhone);

            existing.NumberOfAdults = Math.Max(0, data.NumberOfAdults);
            existing.NumberOfChildren = Math.Max(0, data.NumberOfChildren);
            existing.NumberOfAttendees = data.NumberOfAttendees > 0
                ? data.NumberOfAttendees
                : (existing.NumberOfAdults + existing.NumberOfChildren);

            existing.Language = FirstNonEmpty(data.Language, existing.Language);
            existing.CountryOfOrigin = FirstNonEmpty(countryName, existing.CountryOfOrigin);
            existing.BookingStatus = data.IsCancellation ? "Cancelled" : "Confirmed";
            existing.BookingAmount = bookingAmount;
            existing.Currency = string.IsNullOrWhiteSpace(currency) ? "USD" : currency;
            existing.ProcessingNotes = AppendProcessingNote(
                existing.ProcessingNotes,
                $"Checkfront sync verified on {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC.");
            return existing;
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        private static string BuildCheckfrontSyncMessageId(string bookingCode)
        {
            var safeCode = string.IsNullOrWhiteSpace(bookingCode) ? "unknown" : bookingCode.Trim().Replace(" ", string.Empty);
            return $"CHECKFRONT-SYNC-{safeCode}";
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        private static string BuildCheckfrontSyncTextBody(CustomerData data)
        {
            return string.Join(
                Environment.NewLine,
                new[]
                {
                    "Checkfront Sync Snapshot",
                    $"BookingCode: {data.BookingCode}",
                    $"CustomerName: {data.CustomerName}",
                    $"CustomerEmail: {data.CustomerEmail}",
                    $"CustomerPhone: {data.CustomerPhone}",
                    $"TourName: {data.TourName}",
                    $"TourDate: {data.TourDate:yyyy-MM-dd}",
                    $"TourTime: {data.TourTime}",
                    $"Attendees: {data.NumberOfAttendees}",
                    $"Adults: {data.NumberOfAdults}",
                    $"Children: {data.NumberOfChildren}"
                });
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        private static string AppendProcessingNote(string? existingNotes, string newNote)
        {
            if (string.IsNullOrWhiteSpace(existingNotes))
            {
                return newNote;
            }

            return $"{existingNotes}{Environment.NewLine}{newNote}";
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        private static void MergeCheckfrontIntoCustomerData(CustomerData data, CheckfrontBooking booking)
        {
            var bookingCode = FirstNonEmpty(booking.Code, data.BookingCode);
            if (!string.IsNullOrWhiteSpace(bookingCode))
            {
                data.BookingCode = bookingCode!;
                data.ExtractedBookingCode = bookingCode!;
                if (data.IsCancellation)
                {
                    data.PreviousBookingCode = bookingCode!;
                }
            }

            var customerName = FirstNonEmpty(booking.CustomerName, booking.Customer?.CustomerName);
            if (!string.IsNullOrWhiteSpace(customerName))
            {
                data.CustomerName = customerName!;
            }

            var customerEmail = FirstNonEmpty(booking.CustomerEmail, booking.Customer?.CustomerEmail);
            if (!string.IsNullOrWhiteSpace(customerEmail))
            {
                data.CustomerEmail = customerEmail!;
            }

            var customerPhone = FirstNonEmpty(booking.CustomerPhone, booking.Customer?.CustomerPhone);
            if (!string.IsNullOrWhiteSpace(customerPhone))
            {
                data.CustomerPhone = customerPhone!;
            }

            var tourName = FirstNonEmpty(booking.ItemName, booking.ItemTitle, booking.Summary, data.TourName);
            if (!string.IsNullOrWhiteSpace(tourName))
            {
                data.TourName = tourName!;
            }

            if (!data.TourDate.HasValue && TryParseCheckfrontDate(booking, out var parsedDate))
            {
                data.TourDate = parsedDate.Date;
            }

            if (string.IsNullOrWhiteSpace(data.TourTime))
            {
                var parsedTime = FirstNonEmpty(booking.StartTimeRaw, TryExtractTimeFromText(booking.DateDescription), TryExtractTimeFromText(booking.DateTimeDescription));
                if (!string.IsNullOrWhiteSpace(parsedTime))
                {
                    var standardized = TimeStandardizationService.StandardizeTime(parsedTime);
                    if (!string.IsNullOrWhiteSpace(standardized))
                    {
                        data.TourTime = standardized!;
                    }
                }
            }

            if (booking.NumberOfAdults > 0)
            {
                data.NumberOfAdults = booking.NumberOfAdults;
            }

            if (booking.NumberOfChildren > 0)
            {
                data.NumberOfChildren = booking.NumberOfChildren;
            }

            if (booking.NumberOfAttendees > 0)
            {
                data.NumberOfAttendees = booking.NumberOfAttendees;
            }
            else if (booking.Quantity > 0 && data.NumberOfAttendees == 0)
            {
                data.NumberOfAttendees = booking.Quantity;
            }

            if (data.NumberOfAttendees == 0)
            {
                // Do not parse attendee counts from date fields (e.g., "2026-04-01 14:00"),
                // otherwise year tokens can be misinterpreted as pax counts.
                var parsedTotal =
                    TryParseAttendeeCount(booking.Summary) ??
                    TryParseAttendeeCount(booking.ItemName) ??
                    TryParseAttendeeCount(booking.ItemTitle);
                if (parsedTotal.HasValue && parsedTotal.Value > 0 && parsedTotal.Value <= 100)
                {
                    data.NumberOfAttendees = parsedTotal.Value;
                }
            }

            if (data.NumberOfAttendees == 0 && (data.NumberOfAdults > 0 || data.NumberOfChildren > 0))
            {
                data.NumberOfAttendees = data.NumberOfAdults + data.NumberOfChildren;
            }
        }

        private static bool HasExplicitAttendeeTotals(CheckfrontBooking booking)
        {
            if (booking == null)
            {
                return false;
            }

            return booking.NumberOfAttendees > 0 ||
                   booking.Quantity > 0 ||
                   booking.NumberOfAdults > 0 ||
                   booking.NumberOfChildren > 0;
        }

        private void ApplyPriceBasedAttendeeFallback(
            CustomerData data,
            string vendorName,
            bool isViatorContext,
            string? normalizedText,
            CheckfrontBooking? checkfrontBooking,
            string diagnosticContext)
        {
            var explicitAttendees = Math.Max(0, data.NumberOfAttendees);
            var rawResolution = ResolveRawTotalForAttendeeEstimation(
                data,
                vendorName,
                isViatorContext,
                normalizedText,
                checkfrontBooking);
            var rawTotalAmount = rawResolution.RawTotalAmount;
            var rawTotalText = rawResolution.RawTotalText;
            var totalSource = rawResolution.TotalSource;
            var normalizedTotalAmount = rawTotalAmount;
            var scaledFromMinorUnits = false;

            if (rawTotalAmount.HasValue && rawTotalAmount.Value > 0m && rawResolution.ShouldNormalizeMinorUnits)
            {
                normalizedTotalAmount = NormalizeCheckfrontTotalForEstimation(
                    rawTotalAmount.Value,
                    rawTotalText,
                    explicitAttendees,
                    dbAmountHint: null,
                    out scaledFromMinorUnits);
            }

            int? estimatedAttendees = null;
            if (normalizedTotalAmount.HasValue && normalizedTotalAmount.Value > 0m)
            {
                estimatedAttendees = EstimateAttendeesFromPrice(
                    normalizedTotalAmount.Value,
                    PriceFallbackPerPersonDivisor,
                    MaxReasonableAttendeeCount);
            }

            var source = "unavailable";
            var finalAttendees = explicitAttendees;
            if (explicitAttendees > 0)
            {
                source = "explicit";
            }
            else if (estimatedAttendees.HasValue && estimatedAttendees.Value > 0)
            {
                finalAttendees = estimatedAttendees.Value;
                data.NumberOfAttendees = finalAttendees;
                source = "estimated_by_price";
            }

            var isCheckfrontContext = checkfrontBooking != null || IsCheckfrontVendor(vendorName);
            if (!isViatorContext && !isCheckfrontContext)
            {
                return;
            }

            var rawDisplay = !string.IsNullOrWhiteSpace(rawTotalText)
                ? rawTotalText
                : FormatNullableDecimal(rawTotalAmount);
            var normalizedDisplay = FormatNullableDecimal(normalizedTotalAmount);
            var estimatedDisplay = estimatedAttendees.HasValue
                ? estimatedAttendees.Value.ToString(CultureInfo.InvariantCulture)
                : "n/a";
            var message =
                $"AttendeeCount source={source} context={diagnosticContext} vendor={vendorName} " +
                $"total_source={totalSource} raw_total={rawDisplay} normalized_total={normalizedDisplay} " +
                $"per_person_divisor={PriceFallbackPerPersonDivisor.ToString("0.00", CultureInfo.InvariantCulture)} " +
                $"parsed_attendees={explicitAttendees} estimated_attendees={estimatedDisplay} " +
                $"final_attendees={finalAttendees} minor_unit_scaled={scaledFromMinorUnits}";

            _logger.LogInformation(message);
            Console.WriteLine($"[AttendeeCount] {message}");
        }

        private void ApplyAttendeeSplitFallback(CustomerData data, string vendorName, string diagnosticContext)
        {
            if (data == null)
            {
                return;
            }

            data.NumberOfAdults = Math.Max(0, data.NumberOfAdults);
            data.NumberOfChildren = Math.Max(0, data.NumberOfChildren);
            data.NumberOfAttendees = Math.Max(0, data.NumberOfAttendees);

            if (data.NumberOfAttendees == 0 && (data.NumberOfAdults > 0 || data.NumberOfChildren > 0))
            {
                data.NumberOfAttendees = data.NumberOfAdults + data.NumberOfChildren;
                return;
            }

            if (data.NumberOfAttendees > 0 && data.NumberOfAdults == 0 && data.NumberOfChildren == 0)
            {
                data.NumberOfAdults = data.NumberOfAttendees;
                var message =
                    $"AttendeeSplitFallback context={diagnosticContext} vendor={vendorName} " +
                    $"attendees={data.NumberOfAttendees} adults={data.NumberOfAdults} children={data.NumberOfChildren}";
                _logger.LogInformation(message);
                Console.WriteLine($"[AttendeeSplit] {message}");
            }
        }

        private static (decimal? RawTotalAmount, string RawTotalText, string TotalSource, bool ShouldNormalizeMinorUnits)
            ResolveRawTotalForAttendeeEstimation(
                CustomerData data,
                string vendorName,
                bool isViatorContext,
                string? normalizedText,
                CheckfrontBooking? checkfrontBooking)
        {
            if (isViatorContext || IsViatorVendor(vendorName))
            {
                var viator = ResolveViatorRawTotal(data, normalizedText);
                if (viator.RawTotalAmount.HasValue && viator.RawTotalAmount.Value > 0m)
                {
                    return (viator.RawTotalAmount, viator.RawTotalText, viator.TotalSource, false);
                }
            }

            if (checkfrontBooking != null || IsCheckfrontVendor(vendorName))
            {
                var checkfront = ResolveCheckfrontRawTotal(checkfrontBooking);
                if (checkfront.RawTotalAmount.HasValue && checkfront.RawTotalAmount.Value > 0m)
                {
                    return (checkfront.RawTotalAmount, checkfront.RawTotalText, checkfront.TotalSource, true);
                }
            }

            return (null, string.Empty, "unavailable", false);
        }

        private static (decimal? RawTotalAmount, string RawTotalText, string TotalSource) ResolveViatorRawTotal(
            CustomerData data,
            string? normalizedText)
        {
            var details = data.ViatorDetailsV2;
            if (details?.NetRateAmount.HasValue == true && details.NetRateAmount.Value > 0m)
            {
                return (details.NetRateAmount.Value, details.NetRateRaw, "viator_details_net_rate_amount");
            }

            var parsedNetRateRaw = TryParseMoneyAmount(details?.NetRateRaw);
            if (parsedNetRateRaw.HasValue && parsedNetRateRaw.Value > 0m)
            {
                return (parsedNetRateRaw.Value, details?.NetRateRaw ?? string.Empty, "viator_details_net_rate_raw");
            }

            var labeledCandidates = new (string[] Labels, string Source)[]
            {
                (new[] { "Net Rate" }, "viator_text_net_rate"),
                (new[] { "Total Amount", "Total" }, "viator_text_total"),
                (new[] { "Amount Paid", "Total Paid" }, "viator_text_paid")
            };

            foreach (var candidate in labeledCandidates)
            {
                var raw = TryExtractLabeledValue(normalizedText, candidate.Labels);
                var parsed = TryParseMoneyAmount(raw);
                if (parsed.HasValue && parsed.Value > 0m)
                {
                    return (parsed.Value, raw ?? string.Empty, candidate.Source);
                }
            }

            return (null, string.Empty, "viator_total_unavailable");
        }

        private static (decimal? RawTotalAmount, string RawTotalText, string TotalSource) ResolveCheckfrontRawTotal(CheckfrontBooking? booking)
        {
            if (booking == null)
            {
                return (null, string.Empty, "checkfront_total_unavailable");
            }

            var candidates = new (string Raw, string Source)[]
            {
                (booking.Total, "checkfront_booking_total"),
                (booking.PaidTotal, "checkfront_booking_paid_total"),
                (booking.TaxTotal, "checkfront_booking_tax_total")
            };

            foreach (var candidate in candidates)
            {
                var parsed = TryParseMoneyAmount(candidate.Raw);
                if (parsed.HasValue && parsed.Value > 0m)
                {
                    return (parsed.Value, candidate.Raw ?? string.Empty, candidate.Source);
                }
            }

            return (null, string.Empty, "checkfront_total_unavailable");
        }

        private static (decimal? Amount, string Currency, bool MinorUnitScaled, string TotalSource, string RawTotalText)
            ResolveCheckfrontAmountForPersistence(
                CheckfrontBooking? booking,
                int attendeeHint,
                decimal? dbAmountHint)
        {
            var raw = ResolveCheckfrontRawTotal(booking);
            if (!raw.RawTotalAmount.HasValue || raw.RawTotalAmount.Value <= 0m)
            {
                return (null, "USD", false, raw.TotalSource, raw.RawTotalText);
            }

            var normalized = NormalizeCheckfrontTotalForEstimation(
                raw.RawTotalAmount.Value,
                raw.RawTotalText,
                attendeeHint,
                dbAmountHint,
                out var minorUnitScaled);
            if (normalized <= 0m)
            {
                return (null, "USD", minorUnitScaled, raw.TotalSource, raw.RawTotalText);
            }

            return (Math.Round(normalized, 2, MidpointRounding.AwayFromZero), "USD", minorUnitScaled, raw.TotalSource, raw.RawTotalText);
        }

        private static decimal NormalizeCheckfrontTotalForEstimation(
            decimal rawTotalAmount,
            string rawTotalText,
            int attendeeHint,
            decimal? dbAmountHint,
            out bool scaledFromMinorUnits)
        {
            scaledFromMinorUnits = false;
            var normalized = Math.Abs(rawTotalAmount);
            if (normalized <= 0m)
            {
                return 0m;
            }

            if (LooksLikeMinorUnitMoneyToken(rawTotalText) && normalized >= 100m)
            {
                var scaled = normalized / 100m;
                if (ShouldPreferUnscaledAmount(scaled, normalized, attendeeHint, dbAmountHint))
                {
                    return normalized;
                }

                scaledFromMinorUnits = true;
                return scaled;
            }

            return normalized;
        }

        private static bool ShouldPreferUnscaledAmount(
            decimal scaledAmount,
            decimal unscaledAmount,
            int attendeeHint,
            decimal? dbAmountHint)
        {
            if (Math.Abs(unscaledAmount) <= CheckfrontPlausibleUnscaledMaxUsd)
            {
                return true;
            }

            if (dbAmountHint.HasValue)
            {
                var scaledDiff = Math.Abs(dbAmountHint.Value - scaledAmount);
                var unscaledDiff = Math.Abs(dbAmountHint.Value - unscaledAmount);
                if (unscaledDiff + 0.01m < scaledDiff)
                {
                    return true;
                }
            }

            if (attendeeHint >= 10)
            {
                var perPersonScaled = scaledAmount / attendeeHint;
                var perPersonUnscaled = unscaledAmount / attendeeHint;
                if (perPersonScaled < 1m && perPersonUnscaled >= 1m)
                {
                    return true;
                }
            }

            return false;
        }

        private static int? EstimateAttendeesFromPrice(decimal normalizedTotalAmount, decimal perPersonDivisor, int maxReasonableAttendeeCount)
        {
            if (normalizedTotalAmount <= 0m || perPersonDivisor <= 0m)
            {
                return null;
            }

            var estimated = (int)Math.Round(normalizedTotalAmount / perPersonDivisor, MidpointRounding.AwayFromZero);
            if (estimated <= 0)
            {
                estimated = 1;
            }

            if (estimated > maxReasonableAttendeeCount)
            {
                estimated = maxReasonableAttendeeCount;
            }

            return estimated;
        }

        private static decimal? TryParseMoneyAmount(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            var trimmed = raw.Trim();
            if (decimal.TryParse(
                trimmed,
                NumberStyles.Number | NumberStyles.AllowCurrencySymbol,
                CultureInfo.InvariantCulture,
                out var parsedInvariant) &&
                parsedInvariant > 0m)
            {
                return parsedInvariant;
            }

            if (decimal.TryParse(
                trimmed,
                NumberStyles.Number | NumberStyles.AllowCurrencySymbol,
                CultureInfo.GetCultureInfo("en-US"),
                out var parsedEnUs) &&
                parsedEnUs > 0m)
            {
                return parsedEnUs;
            }

            var match = Regex.Match(
                trimmed,
                @"(?<amount>\d{1,3}(?:,\d{3})*(?:\.\d{1,2})?|\d+(?:\.\d{1,2})?)",
                RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return null;
            }

            var normalized = match.Groups["amount"].Value.Replace(",", string.Empty);
            return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedMatch) && parsedMatch > 0m
                ? parsedMatch
                : null;
        }

        private static string? TryExtractLabeledValue(string? text, params string[] labels)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            foreach (var label in labels)
            {
                var pattern = $@"\b{Regex.Escape(label)}\b\s*[:\-]\s*(?<value>[^\r\n]+)";
                var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
                if (!match.Success)
                {
                    continue;
                }

                var value = match.Groups["value"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        private static bool LooksLikeMinorUnitMoneyToken(string rawTotalText)
        {
            if (string.IsNullOrWhiteSpace(rawTotalText))
            {
                return false;
            }

            var normalized = Regex.Replace(rawTotalText.Trim(), @"[^\d,\.]", string.Empty);
            if (normalized.IndexOf('.', StringComparison.Ordinal) >= 0)
            {
                return false;
            }

            normalized = normalized.Replace(",", string.Empty);
            return Regex.IsMatch(normalized, @"^\d+$");
        }

        private static string FormatNullableDecimal(decimal? value)
        {
            if (!value.HasValue)
            {
                return "n/a";
            }

            return value.Value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        private static string InferCheckfrontVendor(string? subject, string? normalizedText, CustomerData data, CheckfrontBooking? booking)
        {
            var haystack = string.Join(
                "\n",
                new[]
                {
                    subject ?? string.Empty,
                    normalizedText ?? string.Empty,
                    data.TourName ?? string.Empty,
                    booking?.Summary ?? string.Empty,
                    booking?.ItemName ?? string.Empty,
                    booking?.ItemTitle ?? string.Empty
                })
                .ToLowerInvariant();

            if (haystack.Contains("viator", StringComparison.Ordinal))
            {
                return "Viator";
            }

            if (haystack.Contains("getyourguide", StringComparison.Ordinal) || Regex.IsMatch(haystack, @"\bgyg\b", RegexOptions.IgnoreCase))
            {
                return "GetYourGuide";
            }

            return "Checkfront";
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        private static CheckfrontBooking? SelectBestCheckfrontBooking(IEnumerable<CheckfrontBooking> bookings, string? preferredCode)
        {
            var list = bookings?.ToList() ?? new List<CheckfrontBooking>();
            if (list.Count == 0)
            {
                return null;
            }

            return list
                .OrderByDescending(b => !string.IsNullOrWhiteSpace(preferredCode) &&
                                        string.Equals(b.Code, preferredCode, StringComparison.OrdinalIgnoreCase))
                .ThenBy(b => IsCancelledStatus(b))
                .ThenByDescending(b => b.CreatedDateTimestamp)
                .ThenByDescending(b => b.BookingId)
                .FirstOrDefault();
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        private static bool IsCancelledStatus(CheckfrontBooking booking)
        {
            var status = $"{booking.StatusId} {booking.StatusName} {booking.Status}".ToLowerInvariant();
            return status.Contains("cancel", StringComparison.Ordinal) ||
                   status.Contains("void", StringComparison.Ordinal) ||
                   status.Contains("refund", StringComparison.Ordinal);
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        private static bool TryParseCheckfrontDate(CheckfrontBooking booking, out DateTime parsedDate)
        {
            var candidates = new[]
            {
                booking.StartDateRaw,
                booking.DateDescription,
                booking.DateTimeDescription
            };

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                var trimmed = candidate.Trim();
                if (long.TryParse(trimmed, out var epoch) && epoch > 0)
                {
                    parsedDate = DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
                    return true;
                }

                if (DateTime.TryParse(trimmed, out parsedDate))
                {
                    return true;
                }

                if (DateTime.TryParseExact(trimmed, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out parsedDate))
                {
                    return true;
                }
            }

            parsedDate = default;
            return false;
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        private static int? TryParseAttendeeCount(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var match = Regex.Match(text, @"(?<value>\d+)\s*(?:adults?|attendees?|guests?|participants?|travelers?|travellers?)", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups["value"].Value, out var labeledCount))
            {
                return labeledCount;
            }

            match = Regex.Match(text, @"(?<value>\d+)");
            if (match.Success && int.TryParse(match.Groups["value"].Value, out var firstCount))
            {
                return firstCount;
            }

            return null;
        }

        private async Task<string?> TryResolveLockedVendorForAutomationAsync(
            string? messageId,
            CustomerData data,
            string emailType,
            CancellationToken ct)
        {
            var bookingCodes = GetVendorLockLookupCodes(data, emailType);
            foreach (var bookingCode in bookingCodes)
            {
                var booking = await _bookingRepository.FindByCodeAsync(bookingCode, ct);
                if (booking?.VendorManuallyOverridden == true &&
                    !string.IsNullOrWhiteSpace(booking.VendorName))
                {
                    return booking.VendorName.Trim();
                }
            }

            if (!string.IsNullOrWhiteSpace(messageId))
            {
                var processed = await _repository.GetProcessedFullByMessageIdAsync(messageId, ct);
                if (processed?.VendorManuallyOverridden == true &&
                    !string.IsNullOrWhiteSpace(processed.VendorName))
                {
                    return processed.VendorName.Trim();
                }
            }

            return null;
        }

        private static IReadOnlyList<string> GetVendorLockLookupCodes(CustomerData data, string emailType)
        {
            var codes = new[]
            {
                SelectBookingCodeForProcessed(data, emailType),
                data.BookingCode,
                data.ExtractedBookingCode,
                data.NewBookingCode,
                data.PreviousBookingCode
            };

            return codes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string? ResolveLockedVendorFromOverrides(Booking? booking, ProcessedEmailRecord? processed)
        {
            if (booking?.VendorManuallyOverridden == true &&
                !string.IsNullOrWhiteSpace(booking.VendorName))
            {
                return booking.VendorName.Trim();
            }

            if (processed?.VendorManuallyOverridden == true &&
                !string.IsNullOrWhiteSpace(processed.VendorName))
            {
                return processed.VendorName.Trim();
            }

            return null;
        }

        private static bool RequiresExplicitDomainForVendorRule(string? vendorName)
        {
            if (string.IsNullOrWhiteSpace(vendorName))
            {
                return false;
            }

            return vendorName.Equals("Viator", StringComparison.OrdinalIgnoreCase) ||
                   vendorName.Equals("Checkfront", StringComparison.OrdinalIgnoreCase) ||
                   vendorName.Equals("GetYourGuide", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        private static string? TryExtractTimeFromText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var extracted = TimeStandardizationService.ExtractTimeComponent(text);
            return string.IsNullOrWhiteSpace(extracted) ? null : extracted;
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        private static string? FirstNonEmpty(params string?[] values)
        {
            return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        private static string? ExtractFirstEmailFromText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var match = Regex.Match(text, @"[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase);
            return match.Success ? match.Value.Trim() : null;
        }

        private static string? ExtractBookingCode(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            // naive: 6-10 uppercase letters/digits contiguous
            var m = System.Text.RegularExpressions.Regex.Match(text, @"\b[A-Z0-9]{6,10}\b");
            return m.Success ? m.Value : null;
        }

        private static ProcessingResultDto Finish(ProcessingResultDto result, DateTime started)
        {
            result.CompletedAt = DateTime.UtcNow;
            return result;
        }

        private async Task<Customer?> ResolveCustomerAsync(CustomerData data, string emailType, bool dryRunWrites, CancellationToken ct)
        {
            // Fix A: Try all relevant booking codes
            var codes = new List<string?> { data.PreviousBookingCode, data.NewBookingCode, data.BookingCode }
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var code in codes)
            {
                var found = await _customerRepository.GetByBookingCodeAsync(code!, ct);
                if (found != null) return await UpdateExistingCustomerAsync(found, data, emailType, dryRunWrites, ct);
            }

            // Exact email / phone 
            //todo, this method is not exactly the same as the touremail project normalize has more features in previous versions
            if (!string.IsNullOrWhiteSpace(data.CustomerEmail))
            {
                var found = await _customerRepository.GetByEmailAsync(EmailNormalizationService.NormalizeEmail(data.CustomerEmail), ct);
                if (found != null) return await UpdateExistingCustomerAsync(found, data, emailType, dryRunWrites, ct);
            }
            if (!string.IsNullOrWhiteSpace(data.CustomerPhone))
            {
                var found = await _customerRepository.GetByPhoneAsync(PhoneNormalizationService.NormalizePhone(data.CustomerPhone), ct);
                if (found != null) return await UpdateExistingCustomerAsync(found, data, emailType, dryRunWrites, ct);
            }

            // Fuzzy (Fix D): by name + phone last7 + email similarity
            var phoneDigits = new string((data.CustomerPhone ?? string.Empty).Where(char.IsDigit).ToArray());
            var last7 = phoneDigits.Length > 7 ? phoneDigits[^7..] : phoneDigits;
            var email = data.CustomerEmail ?? string.Empty;
            var parts = email.Split('@');
            var emailUser = parts.Length == 2 ? parts[0] : string.Empty;
            var emailDomain = parts.Length == 2 ? parts[1] : string.Empty;

            var candidatesByName = await _customerRepository.FindCandidatesByNameAndPhoneAsync(data.CustomerName ?? string.Empty, string.IsNullOrWhiteSpace(last7) ? null : last7, ct);
            var candidatesByEmail = await _customerRepository.FindCandidatesByEmailSimilarityAsync(emailUser, emailDomain, ct);
            var union = candidatesByName.Concat(candidatesByEmail).GroupBy(c => c.Id).Select(g => g.First()).ToList();
            var ranked = Normalization.CustomerFuzzyMatcher.RankCandidates(data.CustomerName ?? string.Empty, string.IsNullOrWhiteSpace(last7) ? null : last7, emailUser, emailDomain, union);

            if (ranked.Count > 0 && ranked[0].Score >= 0.75)
            {
                EmailProcessingLogger.LogFuzzyMatchUsed(_logger, "CustomerResolve", ranked[0].Score, ranked[0].Reason);
                return await UpdateExistingCustomerAsync(ranked[0].Customer, data, emailType, dryRunWrites, ct);
            }

            // Updated 2025-11-26: Only create customers for booking/confirmation emails
            // Modifications and cancellations MUST find an existing customer (via original booking)
            // This prevents orphaned customers from being created for mod/cancel without original booking
            var shouldCreateCustomer = string.Equals(emailType, "Booking", StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(emailType, "Confirmation", StringComparison.OrdinalIgnoreCase);
            if (!shouldCreateCustomer)
            {
                // For modifications/cancellations: return null if no existing customer found
                // This will be handled by HandleBookingFlowsAsync which will fail the processing
                return null;
            }

            if (string.IsNullOrWhiteSpace(data.CustomerEmail) &&
                string.IsNullOrWhiteSpace(data.CustomerPhone) &&
                string.IsNullOrWhiteSpace(data.CustomerName) &&
                string.IsNullOrWhiteSpace(data.BookingCode) &&
                string.IsNullOrWhiteSpace(data.NewBookingCode) &&
                string.IsNullOrWhiteSpace(data.PreviousBookingCode))
            {
                return null;
            }

            // Create new customer (only for booking/confirmation emails)
            var (firstName, lastName) = SplitCustomerName(data.CustomerName);
            var newCustomer = new Customer
            {
                FullName = !string.IsNullOrWhiteSpace(data.CustomerName)
                    ? data.CustomerName
                    : (!string.IsNullOrWhiteSpace(data.CustomerEmail)
                        ? data.CustomerEmail
                        : (!string.IsNullOrWhiteSpace(data.CustomerPhone)
                            ? data.CustomerPhone
                            : (data.BookingCode ?? data.NewBookingCode ?? data.PreviousBookingCode ?? "Unknown"))),
                FirstName = firstName,
                LastName = lastName,
                PhoneNumber = data.CustomerPhone,
                Email = data.CustomerEmail,
                CustomerIdentifier = GenerateCustomerIdentifier(data),
                BookingIds = BuildBookingIdList(data),
                TotalBookings = (emailType.Equals("Booking", StringComparison.OrdinalIgnoreCase) ||
                                 emailType.Equals("Confirmation", StringComparison.OrdinalIgnoreCase)) ? 1 : 0,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            EmailProcessingLogger.LogCustomerCreatedWithoutCode(_logger, EmailNormalizationService.NormalizeEmail(newCustomer.Email), newCustomer.PhoneNumber);
            var newId = await CreateCustomerAsyncWithDryRun(newCustomer, dryRunWrites, ct);
            newCustomer.Id = newId;
            return newCustomer;
        }

        private async Task<Customer> UpdateExistingCustomerAsync(Customer customer, CustomerData data, string emailType, bool dryRunWrites, CancellationToken ct)
        {
            var before = CaptureObjectFields(customer);
            var updated = false;

            if (string.IsNullOrWhiteSpace(customer.Email) && !string.IsNullOrWhiteSpace(data.CustomerEmail))
            {
                customer.Email = data.CustomerEmail;
                updated = true;
            }

            if (string.IsNullOrWhiteSpace(customer.PhoneNumber) && !string.IsNullOrWhiteSpace(data.CustomerPhone))
            {
                customer.PhoneNumber = data.CustomerPhone;
                updated = true;
            }

            var bookingCode = data.BookingCode ?? data.NewBookingCode ?? data.PreviousBookingCode;
            if (!string.IsNullOrWhiteSpace(bookingCode))
            {
                var existingCodes = new HashSet<string>((customer.BookingIds ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(c => c.Trim()), StringComparer.OrdinalIgnoreCase);
                if (existingCodes.Add(bookingCode))
                {
                    customer.BookingIds = string.Join(",", existingCodes);
                    updated = true;
                }
            }

            if (emailType.Equals("Booking", StringComparison.OrdinalIgnoreCase) ||
                emailType.Equals("Confirmation", StringComparison.OrdinalIgnoreCase))
            {
                customer.TotalBookings = Math.Max(customer.TotalBookings, 0) + 1;
                updated = true;
            }

            if (updated)
            {
                customer.UpdatedAt = DateTime.UtcNow;
                await UpdateCustomerAsyncWithDryRun(customer, before, dryRunWrites, ct);
            }

            return customer;
        }

        private static string BuildBookingIdList(CustomerData data)
        {
            var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(data.BookingCode)) codes.Add(data.BookingCode);
            if (!string.IsNullOrWhiteSpace(data.NewBookingCode)) codes.Add(data.NewBookingCode);
            if (!string.IsNullOrWhiteSpace(data.PreviousBookingCode)) codes.Add(data.PreviousBookingCode);
            return codes.Count == 0 ? null : string.Join(",", codes);
        }

        private static (string? first, string? last) SplitCustomerName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return (null, null);
            var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return (null, null);
            if (parts.Length == 1) return (parts[0], null);
            return (parts[0], string.Join(" ", parts.Skip(1)));
        }

        private static string GenerateCustomerIdentifier(CustomerData data)
        {
            var normalizedName = NormalizeCustomerName(data.CustomerName);
            var normalizedPhone = NormalizePhoneNumber(data.CustomerPhone);
            if (!string.IsNullOrEmpty(normalizedPhone))
            {
                return $"{normalizedName}_{normalizedPhone}";
            }

            var normalizedEmail = NormalizeEmailAddress(data.CustomerEmail);
            if (!string.IsNullOrEmpty(normalizedEmail))
            {
                return $"{normalizedName}_{normalizedEmail.Replace("@", "_at_")}";
            }

            if (!string.IsNullOrWhiteSpace(data.BookingCode))
            {
                return $"{normalizedName}_{data.BookingCode}";
            }
            if (!string.IsNullOrWhiteSpace(data.NewBookingCode))
            {
                return $"{normalizedName}_{data.NewBookingCode}";
            }
            if (!string.IsNullOrWhiteSpace(data.PreviousBookingCode))
            {
                return $"{normalizedName}_{data.PreviousBookingCode}";
            }

            return $"{normalizedName}_{DateTime.UtcNow:yyyyMMddHHmmss}";
        }

        private static string NormalizeCustomerName(string? customerName)
        {
            if (string.IsNullOrWhiteSpace(customerName))
                return "UnknownCustomer";

            var cleaned = Regex.Replace(customerName.Trim(), @"[^a-zA-Z\s]", "");
            cleaned = Regex.Replace(cleaned, @"\s+", " ");
            var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var result = string.Join("", words);
            if (string.IsNullOrEmpty(result))
                return "UnknownCustomer";
            return result.Length > 20 ? result[..20] : result;
        }

        private static string NormalizePhoneNumber(string? phoneNumber)
        {
            if (string.IsNullOrWhiteSpace(phoneNumber))
                return string.Empty;
            var digits = Regex.Replace(phoneNumber, @"[^\d]", "");
            if (digits.Length == 10)
            {
                digits = "1" + digits;
            }
            return digits;
        }

        private static string NormalizeEmailAddress(string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return string.Empty;
            return email.Trim().ToLowerInvariant();
        }

        /// <summary>
        /// Result of booking flow processing
        /// Added: 2025-11-26 00:00 UTC
        /// </summary>
        private sealed class BookingFlowResult
        {
            public bool Success { get; set; } = true;
            public string? ErrorMessage { get; set; }
            
            public static BookingFlowResult Ok() => new BookingFlowResult { Success = true };
            public static BookingFlowResult Failed(string error) => new BookingFlowResult { Success = false, ErrorMessage = error };
        }

        /// <summary>
        /// Updated: 2025-11-26 00:00 UTC - Added booking existence validation for modifications and cancellations
        /// Handles booking flows: confirmation/modification/cancellation with proper validation
        /// </summary>
        private async Task<BookingFlowResult> HandleBookingFlowsAsync(string vendorName, string emailType, CustomerData data, Customer? customer, ProcessedEmailRecord rec, string messageId, string? countryName, bool dryRunWrites, CancellationToken ct)
        {
            var code = data.BookingCode;

            if (string.Equals(emailType, "Confirmation", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(emailType, "Booking", StringComparison.OrdinalIgnoreCase))
            {
                // Validate we have required data for a booking
                if (string.IsNullOrWhiteSpace(code))
                {
                    return BookingFlowResult.Failed("Booking email missing booking code");
                }
                if (string.IsNullOrWhiteSpace(data.CustomerName))
                {
                    return BookingFlowResult.Failed("Booking email missing customer name");
                }
                if (string.IsNullOrWhiteSpace(data.TourName))
                {
                    return BookingFlowResult.Failed("Booking email missing tour name");
                }

                // Ensure we have a valid Customer for FK integrity
                customer = await EnsureCustomerForBookingAsync(customer, data, vendorName, emailType, dryRunWrites, ct);
                // Create if not duplicate
                var existing = await _bookingRepository.FindByCodeAsync(code!, ct);
                if (existing == null)
                {
                    var booking = BuildBooking(customer, data, rec.Id, messageId, vendorName, emailType, isConfirmation: true, isModification: false, isCancellation: false, countryName);
                    
                    // GOOGLE CALENDAR INTEGRATION
                    try 
                    {
                        Console.WriteLine($"[Pipeline] Creating Google Calendar Event for new booking: {booking.BookingCode}");
                        var evt = MapToGoogleEvent(booking);
                        var gId = await InsertCalendarEventAsyncWithDryRun(evt, booking, dryRunWrites);
                        if (!string.IsNullOrEmpty(gId))
                        {
                            booking.CalendarEventId = gId;
                            booking.IsCalendarExportSuccess = true;
                            Console.WriteLine($"[Pipeline] Google Event created: {gId}");
                        }
                    }
                    catch (Exception gEx)
                    {
                        Console.WriteLine($"[Pipeline] Google Calendar Sync Failed: {gEx.Message}");
                        _logger.LogError(gEx, "Failed to sync new booking to Google Calendar");
                    }

                    await CreateBookingAsyncWithDryRun(booking, dryRunWrites, ct);
                }
                return BookingFlowResult.Ok();
            }

            if (string.Equals(emailType, "Modification", StringComparison.OrdinalIgnoreCase))
            {
                // Determine the original booking code to look up
                var originalCode = data.PreviousBookingCode ?? code;
                
                // VALIDATION: Original booking MUST exist for modifications
                if (string.IsNullOrWhiteSpace(originalCode))
                {
                    return BookingFlowResult.Failed("Modification email missing booking code - cannot determine original booking");
                }
                
                var originalBooking = await _bookingRepository.FindByCodeAsync(originalCode!, ct);
                if (originalBooking == null)
                {
                    // Try alternate codes if available
                    if (!string.IsNullOrWhiteSpace(data.NewBookingCode) && !string.Equals(data.NewBookingCode, originalCode, StringComparison.OrdinalIgnoreCase))
                    {
                        originalBooking = await _bookingRepository.FindByCodeAsync(data.NewBookingCode!, ct);
                    }
                    if (originalBooking == null && !string.IsNullOrWhiteSpace(code) && !string.Equals(code, originalCode, StringComparison.OrdinalIgnoreCase))
                    {
                        originalBooking = await _bookingRepository.FindByCodeAsync(code!, ct);
                    }
                }
                
                if (originalBooking == null)
                {
                    return BookingFlowResult.Failed($"Original booking not found for modification. Searched codes: {originalCode}, {data.NewBookingCode ?? "N/A"}, {code ?? "N/A"}");
                }

                // Use the customer from the original booking
                customer = await _customerRepository.GetByIdAsync(originalBooking.CustomerId, ct);
                if (customer == null)
                {
                    return BookingFlowResult.Failed($"Customer not found for original booking {originalCode}");
                }

                var effectiveVendorName = originalBooking.VendorManuallyOverridden &&
                                          !string.IsNullOrWhiteSpace(originalBooking.VendorName)
                    ? originalBooking.VendorName.Trim()
                    : vendorName;
                if (!string.Equals(effectiveVendorName, vendorName, StringComparison.OrdinalIgnoreCase))
                {
                    rec.VendorName = effectiveVendorName;
                }

                // 2025-12-02 00:00 UTC - Backfill unchanged fields when vendor omits them in modification
                // Preserve prior values for TourName/Date/Time/Location/Language if not provided by the modification.
                if (string.IsNullOrWhiteSpace(data.TourName)) data.TourName = originalBooking.TourName;
                if (!data.TourDate.HasValue && originalBooking.TourDate.HasValue) data.TourDate = originalBooking.TourDate;
                if (string.IsNullOrWhiteSpace(data.TourTime)) data.TourTime = originalBooking.TourTime;
                if (string.IsNullOrWhiteSpace(data.TourLocation)) data.TourLocation = originalBooking.TourLocation;
                if (string.IsNullOrWhiteSpace(data.Language)) data.Language = originalBooking.Language;

                // Also mirror into processed record for display if it lacks these fields
                if (string.IsNullOrWhiteSpace(rec.TourName)) rec.TourName = originalBooking.TourName;
                if (string.IsNullOrWhiteSpace(rec.TourTime)) rec.TourTime = originalBooking.TourTime;
                if (string.IsNullOrWhiteSpace(rec.TourDate)) rec.TourDate = originalBooking.TourDate?.ToString("yyyy-MM-dd");
                if (string.IsNullOrWhiteSpace(rec.TourLocation)) rec.TourLocation = originalBooking.TourLocation;

                var updatedCode = data.NewBookingCode ?? code;

                // Deactivate original if codes differ
                if (!string.IsNullOrWhiteSpace(originalCode) && !string.Equals(originalCode, updatedCode, StringComparison.OrdinalIgnoreCase))
                {
                    await DeactivateOriginalOnModificationAsyncWithDryRun(originalCode!, updatedCode!, dryRunWrites, ct);
                }

                var updated = BuildBooking(customer, data, rec.Id, messageId, effectiveVendorName, emailType, isConfirmation: false, isModification: true, isCancellation: false, countryName);
                updated.CustomerId = customer.Id;
                
                // Preserve the Google Event ID from the Original Booking if we found it
                if (!string.IsNullOrEmpty(originalBooking.CalendarEventId))
                {
                    updated.CalendarEventId = originalBooking.CalendarEventId;
                    updated.IsCalendarExportSuccess = originalBooking.IsCalendarExportSuccess;
                }

                // GOOGLE CALENDAR MODIFICATION
                if (!string.IsNullOrEmpty(updated.CalendarEventId))
                {
                    try
                    {
                        Console.WriteLine($"[Pipeline] Updating Google Calendar Event {updated.CalendarEventId} for modification: {updated.BookingCode}");
                        var evt = MapToGoogleEvent(updated);
                        // Ensure we have the ID set on the model
                        evt.Id = updated.CalendarEventId;
                        
                        // Append modification note
                        if (data.SpecialRequests != null)
                             evt.Description = "MODIFICATION: " + data.SpecialRequests + "\n\n" + evt.Description;
                        else
                             evt.Description = "MODIFICATION RECEIVED\n\n" + evt.Description;     

                        await UpdateCalendarEventAsyncWithDryRun(evt, updated, dryRunWrites);
                        Console.WriteLine($"[Pipeline] Google Event updated.");
                    }
                    catch (Exception gEx)
                    {
                         Console.WriteLine($"[Pipeline] Google Calendar Update Failed: {gEx.Message}");
                         _logger.LogError(gEx, "Failed to update booking on Google Calendar");
                    }
                }
                else
                {
                     // If no event exists, treat as new insert? Or skipped?
                     // For now, log and skip as we need an ID to update.
                     // Optionally could Insert here if missing.
                     Console.WriteLine($"[Pipeline] Warning: No Google Event ID found for booking {updated.BookingCode}, skipping Calendar update.");
                }

                var existing = string.IsNullOrWhiteSpace(updated.BookingCode) ? null : await _bookingRepository.FindByCodeAsync(updated.BookingCode, ct);
                if (existing == null)
                {
                    await CreateBookingAsyncWithDryRun(updated, dryRunWrites, ct);
                }
                else
                {
                    var beforeExisting = CaptureObjectFields(existing);
                    updated.Id = existing.Id;
                    await UpdateBookingAsyncWithDryRun(updated, beforeExisting, dryRunWrites, ct);
                }
                return BookingFlowResult.Ok();
            }

            if (string.Equals(emailType, "Cancellation", StringComparison.OrdinalIgnoreCase))
            {
                var cancelCode = data.PreviousBookingCode ?? code;
                
                // VALIDATION: Original booking MUST exist for cancellations
                if (string.IsNullOrWhiteSpace(cancelCode))
                {
                    return BookingFlowResult.Failed("Cancellation email missing booking code - cannot determine booking to cancel");
                }

                var bookingToCancel = await _bookingRepository.FindByCodeAsync(cancelCode!, ct);
                if (bookingToCancel == null)
                {
                    return BookingFlowResult.Failed($"Original booking not found for cancellation. Booking code: {cancelCode}");
                }

                // GOOGLE CALENDAR CANCELLATION
                if (!string.IsNullOrEmpty(bookingToCancel.CalendarEventId))
                {
                    try
                    {
                        Console.WriteLine($"[Pipeline] Marking Google Calendar Event {bookingToCancel.CalendarEventId} as CANCELED.");
                        // We need to fetch it first to get current properties or reconstruction?
                        // But Wait! I can't construct the 'Booking' object fully here easily because 'bookingToCancel' is a Booking entity, and I have 'data' (CustomerData).
                        // I should reuse 'bookingToCancel' to map.
                        var evt = MapToGoogleEvent(bookingToCancel);
                        evt.Id = bookingToCancel.CalendarEventId;
                        evt.Subject = "CANCELED: " + evt.Subject;
                        evt.Description = "CANCELED\n\n" + evt.Description;
                        // Keep time/location same
                        
                        await UpdateCalendarEventAsyncWithDryRun(evt, bookingToCancel, dryRunWrites);
                        Console.WriteLine($"[Pipeline] Google Event marked as CANCELED.");
                    }
                     catch (Exception gEx)
                    {
                         Console.WriteLine($"[Pipeline] Google Calendar Cancel-Mark Failed: {gEx.Message}");
                         _logger.LogError(gEx, "Failed to mark booking as canceled on Google Calendar");
                    }
                }

                var rowsAffected = await CancelBookingAsyncWithDryRun(cancelCode!, "Cancellation email processed", dryRunWrites, ct);
                if (rowsAffected == 0)
                {
                    return BookingFlowResult.Failed($"Cancellation had no effect - booking {cancelCode} may already be cancelled");
                }
                return BookingFlowResult.Ok();
            }

            // Unknown email type - not a booking flow
            return BookingFlowResult.Ok();
        }

        private static Booking BuildBooking(Customer? customer, CustomerData data, int processedEmailId, string messageId, string vendorName, string emailType, bool isConfirmation, bool isModification, bool isCancellation, string? countryName)
        {
            // Updated: 2025-11-30 00:00 UTC - Choose correct booking code per flow
            // - Booking/Confirmation: use BookingCode (fallback ExtractedBookingCode)
            // - Modification: prefer NewBookingCode, fallback BookingCode, then PreviousBookingCode
            // - Cancellation: prefer PreviousBookingCode, fallback BookingCode
            string bookingCodeForRow;
            if (isModification)
            {
                bookingCodeForRow = data.NewBookingCode
                                    ?? (string.IsNullOrWhiteSpace(data.BookingCode) ? null : data.BookingCode)
                                    ?? data.PreviousBookingCode
                                    ?? data.ExtractedBookingCode
                                    ?? string.Empty;
            }
            else if (isCancellation)
            {
                bookingCodeForRow = data.PreviousBookingCode
                                    ?? (string.IsNullOrWhiteSpace(data.BookingCode) ? null : data.BookingCode)
                                    ?? data.ExtractedBookingCode
                                    ?? string.Empty;
            }
            else
            {
                bookingCodeForRow = (string.IsNullOrWhiteSpace(data.BookingCode) ? null : data.BookingCode)
                                    ?? data.ExtractedBookingCode
                                    ?? string.Empty;
            }

            var identifier = ComputeCustomerIdentifier(customer, data, vendorName, null);
            var standardizedTime = data.TourTime;
            var displayTime = TimeStandardizationService.ToDisplayFormat(standardizedTime);
            var displayDate = data.TourDate?.ToString("MM/dd/yyyy");
            return new Booking
            {
                CustomerId = customer?.Id ?? 0,
                CustomerIdentifier = identifier,
                ProcessedEmailId = processedEmailId,
                MessageId = messageId,
                BookingCode = bookingCodeForRow,
                VendorName = vendorName,
                EmailType = emailType,
                IsCancellation = isCancellation,
                IsModification = isModification,
                IsConfirmation = isConfirmation,
                IsActive = !isCancellation,
                TourName = data.TourName,
                TourDate = data.TourDate,
                TourDayOfWeek = data.TourDate?.DayOfWeek.ToString(),
                TourTime = data.TourTime,
                TourLocation = data.TourLocation,
                TourTimeZone = "UTC",
                DisplayDate = displayDate,
                DisplayTime = displayTime,
                CustomerName = data.CustomerName ?? string.Empty,
                CustomerEmail = data.CustomerEmail,
                CustomerPhone = data.CustomerPhone,
                NumberOfAttendees = data.NumberOfAttendees,
                NumberOfAdults = data.NumberOfAdults,
                NumberOfChildren = data.NumberOfChildren,
                NumberOfInfants = 0,
                Language = data.Language,
                CountryOfOrigin = countryName,
                BookingStatus = isCancellation ? "Cancelled" : (isModification ? "Modified" : "Confirmed"),
                BookingAmount = null,
                Currency = "USD",
                SpecialRequests = data.SpecialRequests,
                GuideAssigned = null,
                CalendarEventId = null,
                IsCalendarExportSuccess = false,
                IsSheetExportSuccess = false,
                ProcessingNotes = null,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
        }

        // Added: 2025-11-30 00:00 UTC
        // Mirrors BuildBooking's code selection but for ProcessedEmailRecord.BookingCode to keep
        // the processed-thread association correct, especially for cancellations.
        private static string SelectBookingCodeForProcessed(CustomerData data, string emailType)
        {
            if (string.Equals(emailType, "Modification", StringComparison.OrdinalIgnoreCase))
            {
                return data.NewBookingCode
                       ?? (string.IsNullOrWhiteSpace(data.BookingCode) ? null : data.BookingCode)
                       ?? data.PreviousBookingCode
                       ?? data.ExtractedBookingCode
                       ?? string.Empty;
            }
            if (string.Equals(emailType, "Cancellation", StringComparison.OrdinalIgnoreCase))
            {
                return data.PreviousBookingCode
                       ?? (string.IsNullOrWhiteSpace(data.BookingCode) ? null : data.BookingCode)
                       ?? data.ExtractedBookingCode
                       ?? string.Empty;
            }
            // Booking/Confirmation/Other
            return (string.IsNullOrWhiteSpace(data.BookingCode) ? null : data.BookingCode)
                   ?? data.ExtractedBookingCode
                   ?? string.Empty;
        }

        private static string ComputeCustomerIdentifier(Customer? customer, CustomerData data, string vendorName, string? fallbackSenderEmail)
        {
            if (customer != null && !string.IsNullOrWhiteSpace(customer.CustomerIdentifier))
            {
                return customer.CustomerIdentifier;
            }

            // Prefer a non-vendor customer email if available
            if (!string.IsNullOrWhiteSpace(data.CustomerEmail) && !IsVendorNoReply(data.CustomerEmail, vendorName))
            {
                return data.CustomerEmail;
            }

            // Then phone
            if (!string.IsNullOrWhiteSpace(data.CustomerPhone))
            {
                return data.CustomerPhone;
            }

            // Then booking code
            if (!string.IsNullOrWhiteSpace(data.BookingCode))
            {
                return $"booking:{data.BookingCode}";
            }

            // As a very last resort, use sender email if provided and not vendor no-reply
            if (!string.IsNullOrWhiteSpace(fallbackSenderEmail) && !IsVendorNoReply(fallbackSenderEmail, vendorName))
            {
                return fallbackSenderEmail;
            }

            return "unknown";
        }

        private static bool IsVendorNoReply(string email, string vendorName)
        {
            var lower = email.Trim().ToLowerInvariant();
            if (lower.Contains("no-reply") || lower.Contains("noreply"))
            {
                return true;
            }
            // Basic vendor domain checks
            if (lower.EndsWith("@guruwalk.com") || lower.EndsWith("@freetour.com"))
            {
                return true;
            }
            if (!string.IsNullOrWhiteSpace(vendorName))
            {
                var v = vendorName.Trim().ToLowerInvariant();
                if (lower.EndsWith("@" + v + ".com"))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Added: 2025-11-09 00:00 UTC
        /// Guarantees a persisted Customer for booking/modification flows to satisfy FK_Bookings_Customers.
        /// Creates a minimal placeholder record when resolver yields no match and data is sparse.
        /// </summary>
        private async Task<Customer> EnsureCustomerForBookingAsync(Customer? existing, CustomerData data, string vendorName, string emailType, bool dryRunWrites, CancellationToken ct)
        {
            if (existing != null && existing.Id > 0) return existing;

            // Build a minimal but consistent placeholder
            var (first, last) = SplitCustomerName(data.CustomerName);
            var placeholder = new Customer
            {
                FullName = string.IsNullOrWhiteSpace(data.CustomerName) ? "Unknown Customer" : data.CustomerName!,
                FirstName = first ?? "Unknown",
                LastName = last,
                PhoneNumber = data.CustomerPhone,
                Email = data.CustomerEmail,
                CustomerIdentifier = GenerateCustomerIdentifier(data),
                BookingIds = BuildBookingIdList(data),
                TotalBookings = (emailType.Equals("Booking", StringComparison.OrdinalIgnoreCase) ||
                                 emailType.Equals("Confirmation", StringComparison.OrdinalIgnoreCase)) ? 1 : 0,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            var newId = await CreateCustomerAsyncWithDryRun(placeholder, dryRunWrites, ct);
            placeholder.Id = newId;
            return placeholder;
        }

        private Email.Calendar.Models.GoogleAppointmentModel MapToGoogleEvent(Booking booking)
        {
            var evt = new Email.Calendar.Models.GoogleAppointmentModel
            {
                Subject = booking.TourName ?? "Tour Booking",
                Description = $"Booking Code: {booking.BookingCode}\n" +
                              $"Customer: {booking.CustomerName}\n" +
                              $"Pax: {booking.NumberOfAttendees}\n" +
                              $"Phone: {booking.CustomerPhone}\n" +
                              $"Source: {booking.VendorName}\n" +
                              $"\nNotes: {booking.SpecialRequests}",
                Location = booking.TourLocation ?? "TBD"
            };

            // Parse Date/Time
            // Default to tomorrow 9am if missing
            var date = booking.TourDate ?? DateTime.UtcNow.Date.AddDays(1);
            
            TimeSpan time;
            if (!TimeSpan.TryParse(booking.TourTime, out time))
            {
                // Try simple parsing like "10:00" or "10:00 AM" if TryParse fails for some reason or format
                // For now default to 09:00
                time = new TimeSpan(9, 0, 0);
            }
            
            var startDateTime = date.Date.Add(time);
            evt.StartTime = startDateTime;
            
            // Assume 2 hours duration roughly
            evt.EndTime = startDateTime.AddHours(2);

            return evt;
        }
    }
}



