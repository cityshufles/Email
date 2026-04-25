using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Email.Models;
using Email.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Email.Services.GmailProcessing
{
    /// <summary>
    /// Resolves notification-only Checkfront activity by walker/customer name.
    /// Read-only helper intended for minimal "viator/gyg booking" notifications.
    /// </summary>
    public sealed class CheckfrontNotificationActivityResolver
    {
        private readonly ICheckfrontReadOnlyApi _checkfrontReadOnlyApi;
        private readonly ILogger _logger;

        public CheckfrontNotificationActivityResolver(ICheckfrontReadOnlyApi checkfrontReadOnlyApi, ILogger? logger = null)
        {
            _checkfrontReadOnlyApi = checkfrontReadOnlyApi;
            _logger = logger ?? NullLogger.Instance;
        }

        public CheckfrontNotificationActivityResolver(CheckfrontService checkfrontService, ILogger? logger = null)
            : this(new CheckfrontServiceReadOnlyAdapter(checkfrontService), logger)
        {
        }

        public async Task<CheckfrontNotificationActivityResult?> ResolveByNameOrRecentActivityAsync(
            string? walkerName,
            int recentDaysBack = 14,
            CancellationToken ct = default)
        {
            var extractedName = ExtractWalkerName(walkerName);
            if (string.IsNullOrWhiteSpace(extractedName))
            {
                return null;
            }

            var result = new CheckfrontNotificationActivityResult
            {
                WalkerName = extractedName
            };

            var customerMatches = await _checkfrontReadOnlyApi.SearchCustomersByNameAsync(extractedName);
            result.CustomerMatchCount = customerMatches.Count;

            var dedupedBookings = new Dictionary<string, CheckfrontBooking>(StringComparer.OrdinalIgnoreCase);
            foreach (var match in customerMatches
                .OrderByDescending(m => m.ConfidenceScore)
                .ThenByDescending(m => ResolveCustomerLookupId(m.Customer))
                .Take(5))
            {
                ct.ThrowIfCancellationRequested();
                var customerLookupId = ResolveCustomerLookupId(match.Customer);
                if (string.IsNullOrWhiteSpace(customerLookupId))
                {
                    continue;
                }

                var customerBookings = await _checkfrontReadOnlyApi.GetBookingsByCustomerIdAsync(customerLookupId, limit: 50, page: 1);
                foreach (var booking in customerBookings)
                {
                    if (!NameLooksLikeMatch(extractedName, booking.CustomerName))
                    {
                        continue;
                    }

                    var key = BuildBookingKey(booking);
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    dedupedBookings[key!] = booking;
                }
            }

            if (dedupedBookings.Count == 0)
            {
                var safeDays = Math.Clamp(recentDaysBack <= 0 ? 14 : recentDaysBack, 1, 90);
                var recent = await _checkfrontReadOnlyApi.GetBookingsAsync(
                    startDate: DateTime.UtcNow.Date.AddDays(-safeDays),
                    endDate: null,
                    status: null,
                    limit: 100,
                    page: 1);

                foreach (var booking in recent)
                {
                    if (!NameLooksLikeMatch(extractedName, booking.CustomerName))
                    {
                        continue;
                    }

                    var key = BuildBookingKey(booking);
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    dedupedBookings[key!] = booking;
                }
            }

            var bookings = dedupedBookings.Values.ToList();
            result.MatchedBookingCount = bookings.Count;
            result.CancellationCount = bookings.Count(IsCancellationStatus);
            result.ModificationCount = bookings.Count(IsModificationStatus);
            result.BookingOrConfirmationCount = Math.Max(0, bookings.Count - result.CancellationCount - result.ModificationCount);

            _logger.LogInformation(
                "Checkfront notification name lookup WalkerName={WalkerName} CustomerMatches={CustomerMatches} MatchedBookings={MatchedBookings} BookingOrConfirmation={BookingOrConfirmation} Modifications={Modifications} Cancellations={Cancellations}",
                result.WalkerName,
                result.CustomerMatchCount,
                result.MatchedBookingCount,
                result.BookingOrConfirmationCount,
                result.ModificationCount,
                result.CancellationCount);

            return result;
        }

        public static string ExtractWalkerName(string? notificationText)
        {
            if (string.IsNullOrWhiteSpace(notificationText))
            {
                return string.Empty;
            }

            var value = Regex.Replace(notificationText, @"\s+", " ").Trim();

            var match = Regex.Match(
                value,
                @"^(?<name>[\p{L}\p{M}'\.\-\s]{3,}?)\s+(?:viator(?:\/gyg)?\s+booking)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success)
            {
                return Regex.Replace(match.Groups["name"].Value, @"\s+", " ").Trim();
            }

            return value;
        }

        private static string? BuildBookingKey(CheckfrontBooking booking)
        {
            if (!string.IsNullOrWhiteSpace(booking.Code))
            {
                return booking.Code.Trim();
            }

            if (!string.IsNullOrWhiteSpace(booking.BookingReference))
            {
                return booking.BookingReference.Trim();
            }

            return booking.BookingId > 0 ? booking.BookingId.ToString() : null;
        }

        private static string ResolveCustomerLookupId(CheckfrontCustomerInfo customer)
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

        private static bool IsCancellationStatus(CheckfrontBooking booking)
        {
            var status = $"{booking.StatusId} {booking.StatusName} {booking.Status}".ToLowerInvariant();
            return status.Contains("cancel", StringComparison.Ordinal);
        }

        private static bool IsModificationStatus(CheckfrontBooking booking)
        {
            var status = $"{booking.StatusId} {booking.StatusName} {booking.Status}".ToLowerInvariant();
            return status.Contains("modif", StringComparison.Ordinal) ||
                   status.Contains("amend", StringComparison.Ordinal) ||
                   status.Contains("change", StringComparison.Ordinal);
        }

        private static bool NameLooksLikeMatch(string walkerName, string? customerName)
        {
            var left = NormalizeName(walkerName);
            var right = NormalizeName(customerName);
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            if (left.Contains(right, StringComparison.Ordinal) || right.Contains(left, StringComparison.Ordinal))
            {
                return true;
            }

            var leftTokens = new HashSet<string>(left.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
            var rightTokens = new HashSet<string>(right.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
            if (leftTokens.Count == 0 || rightTokens.Count == 0)
            {
                return false;
            }

            var overlap = leftTokens.Count(token => rightTokens.Contains(token));
            return overlap >= Math.Min(2, leftTokens.Count);
        }

        private static string NormalizeName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var cleaned = Regex.Replace(name, @"[^\p{L}\p{M}\s'\.\-]", " ");
            return Regex.Replace(cleaned, @"\s+", " ").Trim().ToUpperInvariant();
        }

        private sealed class CheckfrontServiceReadOnlyAdapter : ICheckfrontReadOnlyApi
        {
            private readonly CheckfrontService _service;

            public CheckfrontServiceReadOnlyAdapter(CheckfrontService service)
            {
                _service = service;
            }

            public string ActiveEndpoint => string.Empty;

            public Task<CheckfrontConnectionTest> TestConnectionAsync()
                => _service.TestConnectionAsync();

            public Task<CheckfrontV4BookingsListResponse> ListV4BookingsAsync(int limit = 25, int offset = 0)
                => Task.FromResult(new CheckfrontV4BookingsListResponse());

            public Task<CheckfrontBooking?> GetBookingByCodeOrIdAsync(string codeOrId)
                => _service.GetBookingByCodeOrIdAsync(codeOrId);

            public Task<List<CheckfrontV4BookingNote>> GetBookingNotesByCodeOrIdAsync(string codeOrId)
                => Task.FromResult(new List<CheckfrontV4BookingNote>());

            public Task<List<CheckfrontBooking>> GetBookingsAsync(DateTime? startDate = null, DateTime? endDate = null, string? status = null, int? limit = null, int? page = null)
                => _service.GetBookingsAsync(startDate, endDate, status, limit, page);

            public Task<List<CheckfrontCustomerMatch>> SearchCustomersByEmailAsync(string emailAddress)
                => _service.SearchCustomersByEmailAsync(emailAddress);

            public Task<List<CheckfrontCustomerMatch>> SearchCustomersByNameAsync(string customerName)
                => _service.SearchCustomersByNameAsync(customerName);

            public Task<List<CheckfrontBooking>> GetBookingsByCustomerIdAsync(string customerId, int limit = 25, int page = 1)
                => _service.GetBookingsByCustomerIdAsync(customerId, limit, page);

            public Task<List<CheckfrontBooking>> GetBookingsByCustomerIdAsync(int customerId, int limit = 25, int page = 1)
                => _service.GetBookingsByCustomerIdAsync(customerId, limit, page);
        }
    }

    public sealed class CheckfrontNotificationActivityResult
    {
        public string WalkerName { get; set; } = string.Empty;
        public int CustomerMatchCount { get; set; }
        public int MatchedBookingCount { get; set; }
        public int BookingOrConfirmationCount { get; set; }
        public int ModificationCount { get; set; }
        public int CancellationCount { get; set; }
    }
}
