using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MimeKit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Email.Services.GmailCollection.Dtos;
using Email.Services.GmailCollection.Models;

namespace Email.Services.GmailCollection
{
    /// <summary>
    /// Created: 2025-11-04 00:00 UTC
    /// Migrated: 2025-11-21 00:00 UTC - from CityShufflesGuides to Email (verbatim)
    /// MailKit-based Gmail collection with UID high-watermark and date windows.
    /// </summary>
    public sealed class GmailCollectionV2Service
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<GmailCollectionV2Service> _logger;
        private readonly IGmailCollectionRepository _repository;

        public GmailCollectionV2Service(IConfiguration configuration, ILogger<GmailCollectionV2Service> logger, IGmailCollectionRepository repository)
        {
            _configuration = configuration;
            _logger = logger;
            _repository = repository;
        }

        public async Task<CollectionResultDto> CollectAsync(CollectionRequestDto request, CancellationToken ct)
        {
            var startedAt = DateTime.UtcNow;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var result = new CollectionResultDto
            {
                Success = false,
                StartedAt = startedAt
            };

            var mailbox = "INBOX";
            var batchId = $"v2-{DateTime.UtcNow:yyyyMMdd-HHmmss}";

            try
            {
                // Resolve Gmail config
                var imapServer = _configuration["Gmail:ImapServer"] ?? "imap.gmail.com";
                var imapPort = int.TryParse(_configuration["Gmail:ImapPort"], out var p) ? p : 993;
                var email = _configuration["Gmail:Email"] ?? string.Empty;
                var password = _configuration["Gmail:Password"] ?? string.Empty;
                var enableSsl = bool.TryParse(_configuration["Gmail:EnableSsl"], out var ssl) ? ssl : true;

                if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                {
                    result.Message = "Gmail credentials not configured (Gmail:Email / Gmail:Password).";
                    return result;
                }

                // Load watermark
                var wm = await _repository.GetWatermarkAsync(mailbox, ct) ?? new GmailWatermarkRecord
                {
                    Mailbox = mailbox,
                    UidValidity = 0,
                    LastSeenUid = 0
                };

                using var client = new ImapClient();
                client.Timeout = (int)TimeSpan.FromMinutes(30).TotalMilliseconds;

                await client.ConnectAsync(imapServer, imapPort, enableSsl, ct);
                await client.AuthenticateAsync(email, password, ct);

                var inbox = client.Inbox;
                await inbox.OpenAsync(FolderAccess.ReadOnly, ct);

                var currentUidValidity = (long)inbox.UidValidity;
                if (wm.UidValidity != 0 && wm.UidValidity != currentUidValidity)
                {
                    // UIDVALIDITY changed: reset high-watermark
                    wm.LastSeenUid = 0;
                }
                wm.UidValidity = currentUidValidity;
                wm.LastScanStartedAt = DateTime.UtcNow;
                await _repository.UpsertWatermarkAsync(wm, ct);

                // Compute date window
                var startDate = ComputeStartDate(request);
                var query = SearchQuery.All;
                if (startDate.HasValue)
                {
                    // Inclusive-day semantics: DeliveredAfter(startDate - 1 day)
                    var inclusive = DateTime.SpecifyKind(startDate.Value.Date, DateTimeKind.Local).AddDays(-1);
                    query = query.And(SearchQuery.DeliveredAfter(inclusive));
                }

                var uidResults = await inbox.SearchAsync(query, ct);
                var uids = uidResults.OrderBy(u => u.Id).ToList();

                // Apply high-watermark unless ignored
                var ignoreWatermark = request.IgnoreWatermark;
                if (!ignoreWatermark && wm.LastSeenUid > 0)
                {
                    uids = uids.Where(u => (long)u.Id > wm.LastSeenUid).ToList();
                }

                // Apply total limit only for windows other than day/week/month
                var windowLower = request.Window?.ToLowerInvariant();
                if (!(windowLower == "day" || windowLower == "week" || windowLower == "month"))
                {
                    var totalLimit = request.Limit.GetValueOrDefault(int.MaxValue);
                    if (totalLimit > 0 && uids.Count > totalLimit)
                    {
                        uids = uids.Take(totalLimit).ToList();
                    }
                }

                var totalCandidates = uids.Count;
                var inserted = 0;
                var updated = 0;
                var skipped = 0;
                long maxSeenUid = wm.LastSeenUid;

                var batchSize = Math.Max(1, request.BatchSize);
                for (int i = 0; i < uids.Count; i += batchSize)
                {
                    ct.ThrowIfCancellationRequested();
                    var batch = uids.Skip(i).Take(batchSize).ToList();

                    foreach (var uid in batch)
                    {
                        ct.ThrowIfCancellationRequested();

                        MimeMessage? message = null;
                        try
                        {
                            message = await inbox.GetMessageAsync(uid, ct);
                        }
                        catch (Exception exMsg)
                        {
                            _logger.LogWarning(exMsg, "Error fetching message UID {Uid}", uid.Id);
                            continue;
                        }
                        if (message == null)
                        {
                            continue;
                        }

                        var msgId = message.MessageId ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(msgId))
                        {
                            skipped++;
                            continue; // require MessageId for idempotency
                        }

                        var fromMailbox = message.From?.Mailboxes?.FirstOrDefault();
                        var fromEmail = fromMailbox?.Address ?? string.Empty;
                        var fromName = fromMailbox?.Name;
                        var toString = message.To?.ToString();
                        var textBody = message.TextBody ?? string.Empty;
                        var htmlBody = message.HtmlBody ?? string.Empty;
                        var preview = BuildPreview(textBody, 200);
                        var attachments = message.Attachments?.Select(a => a.ContentDisposition?.FileName ?? "attachment").ToArray() ?? Array.Empty<string>();
                        var attachmentJson = System.Text.Json.JsonSerializer.Serialize(attachments);
                        //todo fix recieved date, we cannot set it as now, we need to make it 00:00 so it
                        //shows up as an error and doesnt just merge in without notice
                        var rec = new InboxEmailRecord
                        {
                            Uid = (long)uid.Id,
                            MessageId = msgId,
                            Subject = message.Subject ?? string.Empty,
                            FromEmail = fromEmail,
                            FromName = fromName,
                            ToEmail = toString,
                            ReceivedDate = message.Date != null ? message.Date.DateTime : DateTime.UtcNow,
                            TextBody = textBody,
                            HtmlBody = htmlBody,
                            TextBodyPreview = preview,
                            AttachmentCount = attachments.Length,
                            AttachmentNames = attachmentJson,
                            CollectionBatchId = batchId,
						IsRead = false,
						// Created: 2025-11-28 00:00 UTC - Set ProcessingStatus at collection time
						// Strategy: mark as Pending by default; mark as NonBooking only when clearly determined.
						ProcessingStatus = IsBookingRelatedEmail(message.Subject, fromEmail)
							? TourEmailInboxProcessingStatus.Pending
							: (IsClearlyNonBookingEmail(message.Subject, fromEmail)
								? TourEmailInboxProcessingStatus.NonBooking
								: TourEmailInboxProcessingStatus.Pending)
                        };

                        var affected = await _repository.UpsertInboxEmailAsync(rec, ct);
                        if (affected > 0)
                        {
                            // MERGE returns 1 on either insert or update; we cannot distinguish reliably.
                            // Use heuristic: track updated vs inserted by checking if LastSeenUid boundary indicates new.
                            if (rec.Uid > wm.LastSeenUid)
                                inserted++;
                            else
                                updated++;
                        }
                        else
                        {
                            skipped++;
                        }

                        if (rec.Uid > maxSeenUid)
                        {
                            maxSeenUid = rec.Uid;
                        }
                    }
                }

                // Update watermark completion
                wm.LastSeenUid = Math.Max(wm.LastSeenUid, maxSeenUid);
                wm.LastScanCompletedAt = DateTime.UtcNow;
                await _repository.UpsertWatermarkAsync(wm, ct);

                await client.DisconnectAsync(true, ct);

                sw.Stop();
                result.Success = true;
                result.TotalCandidates = totalCandidates;
                result.InsertedCount = inserted;
                result.UpdatedCount = updated;
                result.SkippedCount = skipped;
                result.TotalProcessed = inserted + updated + skipped;
                result.CompletedAt = DateTime.UtcNow;
                result.Elapsed = sw.Elapsed;
                result.NewWatermarkUid = wm.LastSeenUid;
                result.UidValidity = wm.UidValidity;
                result.Message = $"Processed={result.TotalProcessed}, Inserted~={inserted}, Updated~={updated}, Skipped={skipped}";
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "v2 Gmail collection failed");
                sw.Stop();
                result.Success = false;
                result.Message = ex.Message;
                result.CompletedAt = DateTime.UtcNow;
                result.Elapsed = sw.Elapsed;
                return result;
            }
        }

        /// <summary>
        /// we may not be uing this method
        /// Estimate the number of emails that would be processed for a given request (applies date and watermark rules).
        /// </summary>
        public async Task<int> EstimateCandidateCountAsync(CollectionRequestDto request, CancellationToken ct)
        {
            var mailbox = "INBOX";
            try
            {
                var imapServer = _configuration["Gmail:ImapServer"] ?? "imap.gmail.com";
                var imapPort = int.TryParse(_configuration["Gmail:ImapPort"], out var p) ? p : 993;
                var email = _configuration["Gmail:Email"] ?? string.Empty;
                var password = _configuration["Gmail:Password"] ?? string.Empty;
                var enableSsl = bool.TryParse(_configuration["Gmail:EnableSsl"], out var ssl) ? ssl : true;
                if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return 0;

                var wm = await _repository.GetWatermarkAsync(mailbox, ct) ?? new GmailWatermarkRecord { Mailbox = mailbox, UidValidity = 0, LastSeenUid = 0 };

                using var client = new ImapClient();
                client.Timeout = (int)TimeSpan.FromMinutes(15).TotalMilliseconds;
                await client.ConnectAsync(imapServer, imapPort, enableSsl, ct);
                await client.AuthenticateAsync(email, password, ct);
                var inbox = client.Inbox;
                await inbox.OpenAsync(FolderAccess.ReadOnly, ct);

                var currentUidValidity = (long)inbox.UidValidity;
                if (wm.UidValidity != 0 && wm.UidValidity != currentUidValidity)
                {
                    wm.LastSeenUid = 0; // reset on UIDVALIDITY change for estimate as well
                }

                var startDate = ComputeStartDate(request);
                var query = SearchQuery.All;
                if (startDate.HasValue)
                {
                    var inclusive = DateTime.SpecifyKind(startDate.Value.Date, DateTimeKind.Local).AddDays(-1);
                    query = query.And(SearchQuery.DeliveredAfter(inclusive));
                }

                var uidResults = await inbox.SearchAsync(query, ct);
                var uids = uidResults.OrderBy(u => u.Id).ToList();

                // watermark
                if (!request.IgnoreWatermark && wm.LastSeenUid > 0)
                {
                    uids = uids.Where(u => (long)u.Id > wm.LastSeenUid).ToList();
                }

                // day/week/month unlimited; others honor optional limit
                var windowLower = request.Window?.ToLowerInvariant();
                if (!(windowLower == "day" || windowLower == "week" || windowLower == "month"))
                {
                    var totalLimit = request.Limit.GetValueOrDefault(int.MaxValue);
                    if (totalLimit > 0 && uids.Count > totalLimit)
                    {
                        uids = uids.Take(totalLimit).ToList();
                    }
                }

                await client.DisconnectAsync(true, ct);
                return uids.Count;
            }
            catch
            {
                return 0;
            }
        }

        public async Task<StatusDto> GetStatusAsync(CancellationToken ct)
        {
            try
            {
                var total = await _repository.GetInboxTotalCountAsync(ct);
                var newest = await _repository.GetNewestReceivedDateAsync(ct);
                var wm = await _repository.GetWatermarkAsync("INBOX", ct) ?? new GmailWatermarkRecord
                {
                    Mailbox = "INBOX",
                    UidValidity = 0,
                    LastSeenUid = 0
                };

                return new StatusDto
                {
                    Success = true,
                    Message = "OK",
                    TotalRows = total,
                    NewestReceivedDate = newest,
                    UidValidity = wm.UidValidity,
                    LastSeenUid = wm.LastSeenUid,
                    LastScanStartedAt = wm.LastScanStartedAt,
                    LastScanCompletedAt = wm.LastScanCompletedAt
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting v2 gmailcollection status");
                return new StatusDto { Success = false, Message = ex.Message };
            }
        }

        public Task<GmailWatermarkRecord?> GetWatermarkAsync(CancellationToken ct)
            => _repository.GetWatermarkAsync("INBOX", ct);

        public Task ResetWatermarkAsync(CancellationToken ct)
            => _repository.ResetWatermarkAsync("INBOX", ct);

        /// <summary>
        /// Created: 2025-11-27 00:00 UTC
        /// Deletes all tour-related data (Bookings, Customers, Processed, Inbox) and resets the Gmail watermark.
        /// </summary>
        public Task ClearAllTourDataAndResetWatermarkAsync(CancellationToken ct)
            => _repository.ClearAllTourDataAndResetWatermarkAsync(ct);

        /// <summary>
        /// Created: 2025-11-11 00:00 UTC
        /// Returns the total number of messages currently in the Gmail INBOX on the server.
        /// </summary>
        public async Task<long> GetServerInboxCountAsync(CancellationToken ct)
        {
            var imapServer = _configuration["Gmail:ImapServer"] ?? "imap.gmail.com";
            var imapPort = int.TryParse(_configuration["Gmail:ImapPort"], out var p) ? p : 993;
            var email = _configuration["Gmail:Email"] ?? string.Empty;
            var password = _configuration["Gmail:Password"] ?? string.Empty;
            var enableSsl = bool.TryParse(_configuration["Gmail:EnableSsl"], out var ssl) ? ssl : true;

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                return 0;
            }

            using var client = new ImapClient();
            client.Timeout = (int)TimeSpan.FromMinutes(15).TotalMilliseconds;

            await client.ConnectAsync(imapServer, imapPort, enableSsl, ct);
            await client.AuthenticateAsync(email, password, ct);

            var inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadOnly, ct);

            var count = inbox.Count;

            await client.DisconnectAsync(true, ct);
            return count;
        }

        /// <summary>
        /// Created: 2025-11-11 00:00 UTC
        /// Deletes all collected inbox emails from the SQL Server storage.
        /// </summary>
        public Task DeleteAllInboxEmailsAsync(CancellationToken ct)
            => _repository.DeleteAllInboxEmailsAsync(ct);

        private static DateTime? ComputeStartDate(CollectionRequestDto request)
        {
            var now = DateTime.UtcNow;
            return request.Window?.ToLowerInvariant() switch
            {
                "day" => now.Date,
                "week" => now.Date.AddDays(-7),
                "month" => now.Date.AddDays(-30),
                "daysprior" => request.DaysPrior.HasValue ? now.Date.AddDays(-Math.Max(0, request.DaysPrior.Value)) : null,
                _ => null
            };
        }

        private static string BuildPreview(string textBody, int max)
        {
            if (string.IsNullOrEmpty(textBody)) return string.Empty;
            return textBody.Length <= max ? textBody : textBody.Substring(0, max) + "...";
        }

		/// <summary>
		/// Created: 2025-11-28 00:00 UTC
		/// Simple heuristic to determine if an email is likely booking-related.
		/// </summary>
		private static bool IsBookingRelatedEmail(string? subject, string fromEmail)
		{
			if (string.IsNullOrWhiteSpace(subject)) return false;

			var bookingKeywords = new[]
			{
				"booking", "reservation", "confirmation", "cancel", "modification",
				"tour", "check-in", "checkin", "voucher", "receipt"
			};

			return bookingKeywords.Any(k => subject.Contains(k, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>
		/// Created: 2025-11-28 00:00 UTC
		/// Conservative heuristic to mark clearly non-booking emails.
		/// Only mark as NonBooking when confident.
		/// </summary>
		private static bool IsClearlyNonBookingEmail(string? subject, string fromEmail)
		{
			var s = subject ?? string.Empty;
			// Conservative: avoid false positives; do not include 'receipt' here.
			var nonBookingHints = new[]
			{
				"newsletter", "digest", "promotion", "marketing", "password reset",
				"security alert", "two-factor", "2fa", "verification code", "verify your email"
			};
			return nonBookingHints.Any(k => s.Contains(k, StringComparison.OrdinalIgnoreCase));
		}
    }
}


