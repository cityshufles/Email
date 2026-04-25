using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Email.Models;
using Email.Services.GmailProcessing.Dtos;
using Email.Services.GmailProcessing.Repositories;
using Email.Services.GmailProcessing.VendorParsers;
using Microsoft.Extensions.Logging;

namespace Email.Services.GmailProcessing
{
    /// <summary>
    /// Read-only service that compares recent Checkfront API bookings to existing DB booking rows.
    /// </summary>
    public sealed class CheckfrontRecentDbCompareService
    {
        private const decimal CheckfrontPlausibleUnscaledMaxUsd = 500m;
        private readonly ICheckfrontReadOnlyApi _checkfrontReadOnlyApi;
        private readonly IBookingRepository _bookingRepository;
        private readonly ILogger<CheckfrontRecentDbCompareService> _logger;

        public CheckfrontRecentDbCompareService(
            ICheckfrontReadOnlyApi checkfrontReadOnlyApi,
            IBookingRepository bookingRepository,
            ILogger<CheckfrontRecentDbCompareService> logger)
        {
            _checkfrontReadOnlyApi = checkfrontReadOnlyApi;
            _bookingRepository = bookingRepository;
            _logger = logger;
        }

        public async Task<CheckfrontRecentDbCompareResult> CompareRecentAsync(
            int daysBack,
            int limitPerPage,
            int maxPages,
            CancellationToken ct,
            CheckfrontRecentDbCompareOptions? options = null)
        {
            var operationId = Guid.NewGuid().ToString("N");
            var safeDaysBack = Math.Clamp(daysBack <= 0 ? 30 : daysBack, 1, 365);
            var safeLimitPerPage = Math.Clamp(limitPerPage <= 0 ? 50 : limitPerPage, 1, 200);
            var safeMaxPages = Math.Clamp(maxPages <= 0 ? 5 : maxPages, 1, 50);
            var safeHighAmountThresholdUsd = options?.HighAmountThresholdUsd > 0m
                ? Math.Round(options.HighAmountThresholdUsd, 2, MidpointRounding.AwayFromZero)
                : 500m;
            var safeGlobalScanLimit = Math.Clamp(options?.GlobalScanLimit ?? 5000, 1, 50000);
            var includeGlobalHighAmountReport = options?.IncludeGlobalHighAmountReport ?? true;
            var safeCheckfrontMinorUnitMinRaw = options?.CheckfrontMinorUnitMinRaw > 0m
                ? options.CheckfrontMinorUnitMinRaw
                : 100m;
            var startDate = DateTime.UtcNow.Date.AddDays(-safeDaysBack);

            var result = new CheckfrontRecentDbCompareResult
            {
                OperationId = operationId,
                TimestampUtc = DateTime.UtcNow,
                DaysBack = safeDaysBack,
                LimitPerPage = safeLimitPerPage,
                MaxPages = safeMaxPages
            };

            WriteConsole(
                $"Operation=RecentCompare Start OperationId={operationId} Endpoint={_checkfrontReadOnlyApi.ActiveEndpoint} DaysBack={safeDaysBack} LimitPerPage={safeLimitPerPage} MaxPages={safeMaxPages} HighAmountThresholdUsd={safeHighAmountThresholdUsd.ToString("0.##", CultureInfo.InvariantCulture)} GlobalScanLimit={safeGlobalScanLimit} IncludeGlobal={includeGlobalHighAmountReport} CheckfrontMinorUnitMinRaw={safeCheckfrontMinorUnitMinRaw.ToString("0.##", CultureInfo.InvariantCulture)}");

            var checkfrontBookings = new Dictionary<string, CheckfrontBooking>(StringComparer.OrdinalIgnoreCase);
            for (var page = 1; page <= safeMaxPages; page++)
            {
                ct.ThrowIfCancellationRequested();
                var pageBookings = await _checkfrontReadOnlyApi.GetBookingsAsync(
                    startDate: startDate,
                    endDate: null,
                    status: null,
                    limit: safeLimitPerPage,
                    page: page);

                result.PagesFetched++;

                if (pageBookings.Count == 0)
                {
                    break;
                }

                foreach (var booking in pageBookings)
                {
                    var code = ResolveBookingCode(booking);
                    if (string.IsNullOrWhiteSpace(code))
                    {
                        continue;
                    }

                    checkfrontBookings[code] = booking;
                }

                // Keep paging until an empty page because Checkfront can return fewer rows
                // than requested even when more pages still exist.
            }

            var apiCodes = checkfrontBookings.Keys
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            result.CheckfrontCount = apiCodes.Count;
            if (apiCodes.Count > 0)
            {
                var dbRows = await _bookingRepository.GetByCodesAsync(apiCodes, ct);
                var dbByCode = dbRows
                    .Where(x => !string.IsNullOrWhiteSpace(x.BookingCode))
                    .GroupBy(x => x.BookingCode, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        g => g.Key,
                        g => g.OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.Id).First(),
                        StringComparer.OrdinalIgnoreCase);

                result.DbCount = dbByCode.Count;

                foreach (var code in apiCodes)
                {
                    if (!dbByCode.TryGetValue(code, out var dbBooking))
                    {
                        result.MissingInDb.Add(code);
                        continue;
                    }

                    result.PresentInDb.Add(code);
                    var apiBooking = checkfrontBookings[code];
                    CompareBooking(code, apiBooking, dbBooking, result.FieldMismatches);

                    var amountCandidate = BuildAmountCorrectionCandidate(
                        code,
                        apiBooking,
                        dbBooking,
                        safeCheckfrontMinorUnitMinRaw);
                    if (amountCandidate != null)
                    {
                        result.CheckfrontAmountCandidates.Add(amountCandidate);
                    }
                }
            }

            if (includeGlobalHighAmountReport)
            {
                var recentRows = await _bookingRepository.GetRecentAsync(safeGlobalScanLimit, ct);
                var highAmountRows = recentRows
                    .Where(x =>
                        x != null &&
                        x.BookingAmount.HasValue &&
                        x.BookingAmount.Value >= safeHighAmountThresholdUsd &&
                        x.UpdatedAt >= startDate)
                    .OrderByDescending(x => x.BookingAmount!.Value)
                    .ThenByDescending(x => x.UpdatedAt)
                    .ToList();

                foreach (var row in highAmountRows)
                {
                    result.GlobalHighAmountCandidates.Add(new GlobalHighAmountCandidate
                    {
                        BookingId = row.Id,
                        BookingCode = row.BookingCode ?? string.Empty,
                        VendorName = string.IsNullOrWhiteSpace(row.VendorName) ? string.Empty : row.VendorName.Trim(),
                        BookingAmount = row.BookingAmount!.Value,
                        Currency = NormalizeCurrency(row.Currency),
                        UpdatedAtUtc = row.UpdatedAt
                    });
                }
            }

            if (apiCodes.Count == 0)
            {
                result.Outcome = "NoApiData";
                result.Message =
                    $"No Checkfront bookings returned for the selected window. " +
                    $"GlobalHighAmountCandidates={result.GlobalHighAmountCandidateCount}.";
                WriteConsole(
                    $"Operation=RecentCompare End OperationId={operationId} Outcome={result.Outcome} CheckfrontCount=0 DbCount=0 AmountCandidates={result.CheckfrontAmountCandidateCount} GlobalHighAmountCandidates={result.GlobalHighAmountCandidateCount}");
                return result;
            }

            result.Outcome = result.MissingInDb.Count == 0 &&
                             result.FieldMismatches.Count == 0 &&
                             result.CheckfrontAmountCandidates.Count == 0
                ? "Match"
                : "Mismatch";
            result.Message =
                $"Compared {result.CheckfrontCount} Checkfront booking(s) against {result.DbCount} DB booking row(s): " +
                $"MissingInDb={result.MissingInDb.Count}, PresentInDb={result.PresentInDb.Count}, " +
                $"FieldMismatches={result.FieldMismatches.Count}, AmountCandidates={result.CheckfrontAmountCandidateCount}, " +
                $"GlobalHighAmountCandidates={result.GlobalHighAmountCandidateCount}.";

            WriteConsole(
                $"Operation=RecentCompare End OperationId={operationId} Outcome={result.Outcome} CheckfrontCount={result.CheckfrontCount} DbCount={result.DbCount} MissingInDb={result.MissingInDb.Count} FieldMismatches={result.FieldMismatches.Count} AmountCandidates={result.CheckfrontAmountCandidateCount} GlobalHighAmountCandidates={result.GlobalHighAmountCandidateCount}");

            return result;
        }

        private void CompareBooking(
            string code,
            CheckfrontBooking apiBooking,
            Models.Booking dbBooking,
            List<CheckfrontRecentDbFieldMismatch> mismatches)
        {
            AddMismatchIfDifferent(code, "CustomerName", NormalizeLooseText(apiBooking.CustomerName), NormalizeLooseText(dbBooking.CustomerName), apiBooking.CustomerName, dbBooking.CustomerName, mismatches);
            AddMismatchIfDifferent(code, "TourDate", NormalizeDate(apiBooking), NormalizeDate(dbBooking.TourDate), GetDisplayApiDate(apiBooking), dbBooking.TourDate?.ToString("yyyy-MM-dd"), mismatches);
            AddMismatchIfDifferent(code, "TourTime", NormalizeTime(apiBooking.StartTimeRaw ?? apiBooking.DateDescription), NormalizeTime(dbBooking.TourTime), NormalizeTimeRaw(apiBooking.StartTimeRaw ?? apiBooking.DateDescription), NormalizeTimeRaw(dbBooking.TourTime), mismatches);
            AddMismatchIfDifferent(code, "Attendees", NormalizeAttendees(apiBooking).ToString(CultureInfo.InvariantCulture), NormalizeAttendees(dbBooking).ToString(CultureInfo.InvariantCulture), NormalizeAttendees(apiBooking).ToString(CultureInfo.InvariantCulture), NormalizeAttendees(dbBooking).ToString(CultureInfo.InvariantCulture), mismatches);
            AddMismatchIfDifferent(code, "Status", NormalizeStatus(apiBooking), NormalizeStatus(dbBooking), $"{apiBooking.StatusId}/{apiBooking.StatusName}/{apiBooking.Status}", dbBooking.BookingStatus, mismatches);
        }

        private static void AddMismatchIfDifferent(
            string code,
            string field,
            string normalizedApi,
            string normalizedDb,
            string? rawApi,
            string? rawDb,
            List<CheckfrontRecentDbFieldMismatch> mismatches)
        {
            if (string.Equals(normalizedApi, normalizedDb, StringComparison.Ordinal))
            {
                return;
            }

            mismatches.Add(new CheckfrontRecentDbFieldMismatch
            {
                BookingCode = code,
                Field = field,
                ApiValue = rawApi ?? string.Empty,
                DbValue = rawDb ?? string.Empty
            });
        }

        private static CheckfrontAmountCorrectionCandidate? BuildAmountCorrectionCandidate(
            string bookingCode,
            CheckfrontBooking apiBooking,
            Models.Booking dbBooking,
            decimal minorUnitMinRaw)
        {
            var rawResolved = ResolveRawTotal(apiBooking);
            if (!rawResolved.RawTotal.HasValue || rawResolved.RawTotal.Value <= 0m)
            {
                return null;
            }

            var attendeeHint = Math.Max(
                NormalizeAttendees(apiBooking),
                NormalizeAttendees(dbBooking));
            var normalizedAmount = NormalizeCheckfrontAmount(
                rawResolved.RawTotal.Value,
                minorUnitMinRaw,
                attendeeHint,
                dbBooking.BookingAmount,
                out var scaledFromMinorUnits,
                out var minorUnitScalingSuppressed);
            var dbAmount = dbBooking.BookingAmount;
            var needsCorrection = !dbAmount.HasValue || Math.Abs(dbAmount.Value - normalizedAmount) >= 0.01m;
            if (!needsCorrection)
            {
                return null;
            }

            var reason = BuildAmountCandidateReason(
                dbAmount,
                scaledFromMinorUnits,
                minorUnitScalingSuppressed,
                rawResolved.Source);
            return new CheckfrontAmountCorrectionCandidate
            {
                BookingCode = bookingCode,
                RawTotal = rawResolved.RawTotal.Value,
                NormalizedTotal = normalizedAmount,
                DbBookingAmount = dbAmount,
                Reason = reason,
                SuggestedAmount = normalizedAmount,
                Currency = NormalizeCurrency(dbBooking.Currency)
            };
        }

        private static string BuildAmountCandidateReason(
            decimal? dbAmount,
            bool scaledFromMinorUnits,
            bool minorUnitScalingSuppressed,
            string source)
        {
            if (minorUnitScalingSuppressed)
            {
                return !dbAmount.HasValue
                    ? $"DB booking amount is null; API integer total did not pass attendee/amount sanity for minor-unit scaling. Source={source}"
                    : $"DB booking amount differs; API integer total did not pass attendee/amount sanity for minor-unit scaling. Source={source}";
            }

            if (!dbAmount.HasValue)
            {
                return scaledFromMinorUnits
                    ? $"DB booking amount is null; API total appears to be minor-unit encoded. Source={source}"
                    : $"DB booking amount is null. Source={source}";
            }

            return scaledFromMinorUnits
                ? $"DB booking amount differs from minor-unit normalized API total. Source={source}"
                : $"DB booking amount differs from API total. Source={source}";
        }

        private static (decimal? RawTotal, string Source) ResolveRawTotal(CheckfrontBooking booking)
        {
            var candidates = new (string Raw, string Source)[]
            {
                (booking.Total, "total"),
                (booking.PaidTotal, "paid_total"),
                (booking.TaxTotal, "tax_total")
            };

            foreach (var candidate in candidates)
            {
                var parsed = TryParseMoneyAmount(candidate.Raw);
                if (parsed.HasValue && parsed.Value > 0m)
                {
                    return (parsed.Value, candidate.Source);
                }
            }

            return (null, string.Empty);
        }

        private static decimal NormalizeCheckfrontAmount(
            decimal rawTotal,
            decimal minorUnitMinRaw,
            int attendeeHint,
            decimal? dbAmountHint,
            out bool scaledFromMinorUnits,
            out bool minorUnitScalingSuppressed)
        {
            scaledFromMinorUnits = false;
            minorUnitScalingSuppressed = false;
            var absolute = Math.Abs(rawTotal);
            var integerLike = decimal.Truncate(absolute) == absolute;
            if (integerLike && absolute >= minorUnitMinRaw)
            {
                var scaled = Math.Round(rawTotal / 100m, 2, MidpointRounding.AwayFromZero);
                var unscaled = Math.Round(rawTotal, 2, MidpointRounding.AwayFromZero);
                if (ShouldPreferUnscaledAmount(scaled, unscaled, attendeeHint, dbAmountHint))
                {
                    minorUnitScalingSuppressed = true;
                    return unscaled;
                }

                scaledFromMinorUnits = true;
                return scaled;
            }

            return Math.Round(rawTotal, 2, MidpointRounding.AwayFromZero);
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

        private static string NormalizeCurrency(string? currency)
        {
            var value = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();
            return value.Length > 8 ? value.Substring(0, 8) : value;
        }

        private static string ResolveBookingCode(CheckfrontBooking booking)
        {
            if (!string.IsNullOrWhiteSpace(booking.Code))
            {
                return booking.Code.Trim();
            }

            if (!string.IsNullOrWhiteSpace(booking.BookingReference))
            {
                return booking.BookingReference.Trim();
            }

            return booking.BookingId > 0 ? booking.BookingId.ToString(CultureInfo.InvariantCulture) : string.Empty;
        }

        private static string NormalizeLooseText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var lowered = value.Trim().ToLowerInvariant();
            lowered = System.Text.RegularExpressions.Regex.Replace(lowered, @"\s+", " ");
            lowered = System.Text.RegularExpressions.Regex.Replace(lowered, @"[^a-z0-9 ]", string.Empty);
            return lowered.Trim();
        }

        private static string NormalizeDate(CheckfrontBooking booking)
        {
            return TryParseCheckfrontDate(booking, out var parsed)
                ? parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : string.Empty;
        }

        private static string NormalizeDate(DateTime? value)
            => value.HasValue ? value.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty;

        private static string GetDisplayApiDate(CheckfrontBooking booking)
        {
            if (TryParseCheckfrontDate(booking, out var parsed))
            {
                return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            return string.IsNullOrWhiteSpace(booking.DateDescription) ? string.Empty : booking.DateDescription.Trim();
        }

        private static bool TryParseCheckfrontDate(CheckfrontBooking booking, out DateTime parsedDate)
        {
            parsedDate = default;
            var candidates = new[] { booking.StartDateRaw, booking.DateDescription, booking.DateTimeDescription };
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

                if (DateTime.TryParseExact(trimmed, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeTime(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var standardized = TimeStandardizationService.StandardizeTime(value);
            if (!string.IsNullOrWhiteSpace(standardized))
            {
                return standardized.Trim().ToLowerInvariant();
            }

            return value.Trim().ToLowerInvariant();
        }

        private static string NormalizeTimeRaw(string? value)
            => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

        private static int NormalizeAttendees(CheckfrontBooking booking)
        {
            if (booking.NumberOfAttendees > 0)
            {
                return booking.NumberOfAttendees;
            }

            if (booking.Quantity > 0)
            {
                return booking.Quantity;
            }

            if (booking.NumberOfAdults > 0 || booking.NumberOfChildren > 0)
            {
                return booking.NumberOfAdults + booking.NumberOfChildren;
            }

            return 0;
        }

        private static int NormalizeAttendees(Models.Booking booking)
        {
            if (booking.NumberOfAttendees.HasValue && booking.NumberOfAttendees.Value > 0)
            {
                return booking.NumberOfAttendees.Value;
            }

            var adults = booking.NumberOfAdults ?? 0;
            var children = booking.NumberOfChildren ?? 0;
            return adults + children;
        }

        private static string NormalizeStatus(CheckfrontBooking booking)
        {
            var status = $"{booking.StatusId} {booking.StatusName} {booking.Status}".ToLowerInvariant();
            return status.Contains("cancel", StringComparison.Ordinal) ? "cancelled" : "active";
        }

        private static string NormalizeStatus(Models.Booking booking)
        {
            if (!booking.IsActive)
            {
                return "cancelled";
            }

            var status = booking.BookingStatus ?? string.Empty;
            return status.Contains("cancel", StringComparison.OrdinalIgnoreCase) ? "cancelled" : "active";
        }

        private void WriteConsole(string message)
        {
            _logger.LogInformation("{Message}", message);
            Console.WriteLine($"[CheckfrontTestPage] {DateTime.UtcNow:O} {message}");
        }
    }
}
