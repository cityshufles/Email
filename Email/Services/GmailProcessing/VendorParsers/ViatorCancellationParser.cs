using System;
using System.Linq;
using System.Text.RegularExpressions;
using Email.Services.GmailProcessing.Models;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Email.Services.GmailProcessing.VendorParsers
{
    /// <summary>
    /// Created: 2026-03-26 00:00 UTC
    /// Updated: 2026-03-26 00:00 UTC - Added deterministic Checkfront + Viator cancellation parsing.
    /// </summary>
    public sealed class ViatorCancellationParser : BaseParser
    {
        private readonly ILogger<ViatorCancellationParser> _logger;

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        public ViatorCancellationParser(ILogger<ViatorCancellationParser>? logger = null)
        {
            _logger = logger ?? NullLogger<ViatorCancellationParser>.Instance;
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// Updated: 2026-03-26 00:00 UTC - Supports "Cancellation (CODE)" and fallback booking identifiers.
        /// </summary>
        public override VendorParseResult Parse(string? subject, string? htmlBody, string? textBody)
        {
            var data = new CustomerData
            {
                IsCancellation = true
            };

            try
            {
                var mergedText = BuildMergedText(subject, htmlBody, textBody);
                var code = ExtractCancellationCode(subject, mergedText);
                if (!string.IsNullOrWhiteSpace(code))
                {
                    data.BookingCode = code!;
                    data.ExtractedBookingCode = code!;
                    data.PreviousBookingCode = code!;
                }

                var customerName = ExtractLabeledValue(mergedText,
                    "Lead Traveler Name", "Lead Traveller Name",
                    "Traveler", "Traveller", "Customer Name", "Traveler Name", "Traveller Name", "Name");
                ParseAndSetCustomerName(customerName, data);

                data.CustomerEmail = ExtractLabeledValue(mergedText,
                    "Customer Email", "Email", "Traveler Email", "Traveller Email");
                if (string.IsNullOrWhiteSpace(data.CustomerEmail))
                {
                    data.CustomerEmail = ExtractFirstEmail(mergedText);
                }

                data.CustomerPhone = ExtractLabeledValue(mergedText,
                    "Phone", "Customer Phone", "Traveler Phone", "Traveller Phone", "Mobile");

                data.TourName = ExtractLabeledValue(mergedText,
                    "Tour Name", "Tour Grade Description", "Tour Grade",
                    "Tour", "Item", "Activity", "Product");

                var dateLine = ExtractLabeledValue(mergedText, "Travel Date", "Date", "Tour Date", "Start Date", "When");
                if (!string.IsNullOrWhiteSpace(dateLine))
                {
                    ParseAndSetTourDateTime(dateLine, data);
                }

                var timeLine = ExtractLabeledValue(mergedText, "Time", "Start Time", "Tour Grade", "Tour Grade Code");
                if (!string.IsNullOrWhiteSpace(timeLine))
                {
                    SetStandardizedTourTime(timeLine, data);
                    if (string.IsNullOrWhiteSpace(data.TourTime))
                    {
                        var extractedTime = TimeStandardizationService.ExtractTimeComponent(timeLine);
                        if (!string.IsNullOrWhiteSpace(extractedTime))
                        {
                            SetStandardizedTourTime(extractedTime, data);
                        }
                    }
                }

                data.TourLocation = ExtractLabeledValue(mergedText, "Location", "Meeting Point");
                data.Language = ExtractLabeledValue(mergedText, "Tour Language", "Language");
                data.SpecialRequests = ExtractLabeledValue(mergedText, "Special Requirements", "Special Requests");

                var attendeesLine = ExtractLabeledValue(mergedText, "Attendees", "Participants", "Guests", "Travelers", "Travellers");
                if (!string.IsNullOrWhiteSpace(attendeesLine))
                {
                    ParseAttendees(attendeesLine, data);
                }

                if (data.NumberOfAttendees == 0 && (data.NumberOfAdults > 0 || data.NumberOfChildren > 0))
                {
                    data.NumberOfAttendees = data.NumberOfAdults + data.NumberOfChildren;
                }

                data.CountryName = ExtractCountryFromPhone(data.CustomerPhone);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ViatorCancellationParser failed for subject {Subject}", subject ?? string.Empty);
                Console.WriteLine($"[DEBUG] ViatorCancellationParser.Parse error: {ex.Message}");
            }

            var vendorName = InferVendorName(subject, htmlBody, textBody);
            return new VendorParseResult
            {
                VendorName = vendorName,
                EmailType = "Cancellation",
                Data = data
            };
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        private static string BuildMergedText(string? subject, string? htmlBody, string? textBody)
        {
            var htmlText = string.Empty;
            if (!string.IsNullOrWhiteSpace(htmlBody))
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(htmlBody);
                htmlText = HtmlEntity.DeEntitize(doc.DocumentNode.InnerText ?? string.Empty);
            }

            return string.Join(
                "\n",
                new[]
                {
                    subject ?? string.Empty,
                    textBody ?? string.Empty,
                    htmlText
                }.Where(s => !string.IsNullOrWhiteSpace(s)))
                .Trim();
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        private static string? ExtractCancellationCode(string? subject, string mergedText)
        {
            var subjectPatterns = new[]
            {
                @"Cancellation\s*\((?:#)?(?<code>[A-Z0-9\-]{4,})\)",
                @"New\s+Booking\s+for.+?\((?:#)?(?<code>[A-Z0-9\-]{4,})\)",
                @"Booking\s+Invoice:\s*(?<code>[A-Z0-9\-]{4,})"
            };

            foreach (var pattern in subjectPatterns)
            {
                var match = Regex.Match(subject ?? string.Empty, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return match.Groups["code"].Value.Trim();
                }
            }

            var bodyPatterns = new[]
            {
                @"Cancellation\s*\((?:#)?(?<code>[A-Z0-9\-]{4,})\)",
                @"Booking\s+Reference(?:\s+Number)?\s*[:\-]\s*(?:#)?(?<code>[A-Z0-9\-]{4,})",
                @"Booking\s+Code:\s*(?<code>[A-Z0-9\-]{4,})",
                @"Booking\s+ID:\s*(?<code>[A-Z0-9\-]{4,})"
            };

            foreach (var pattern in bodyPatterns)
            {
                var match = Regex.Match(mergedText, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return match.Groups["code"].Value.Trim();
                }
            }

            return null;
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        private static string ExtractLabeledValue(string text, params string[] labels)
        {
            foreach (var label in labels)
            {
                var pattern = $@"\b{Regex.Escape(label)}\b\s*[:\-]\s*(?<value>[^\r\n]+)";
                var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var value = match.Groups["value"].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        private static string ExtractFirstEmail(string text)
        {
            var matches = Regex.Matches(text, @"[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                var value = match.Value.Trim();
                if (!value.Contains("checkfront", StringComparison.OrdinalIgnoreCase))
                {
                    return value;
                }
            }

            return matches.Count > 0 ? matches[0].Value.Trim() : string.Empty;
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        private static string InferVendorName(string? subject, string? htmlBody, string? textBody)
        {
            var haystack = string.Join(
                "\n",
                new[] { subject ?? string.Empty, textBody ?? string.Empty, htmlBody ?? string.Empty })
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
    }
}
