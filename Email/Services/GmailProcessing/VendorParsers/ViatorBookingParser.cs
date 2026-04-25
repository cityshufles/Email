using System;
using System.Globalization;
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
    /// Updated: 2026-03-26 00:00 UTC - Added deterministic Checkfront + Viator booking parsing for forwarded/direct notifications.
    /// </summary>
    public sealed class ViatorBookingParser : BaseParser
    {
        private readonly ILogger<ViatorBookingParser> _logger;

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        public ViatorBookingParser(ILogger<ViatorBookingParser>? logger = null)
        {
            _logger = logger ?? NullLogger<ViatorBookingParser>.Instance;
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// Updated: 2026-03-26 00:00 UTC - Supports Checkfront receipt formats and Viator "New Booking for ... (#BR-...)" payloads.
        /// </summary>
        public override VendorParseResult Parse(string? subject, string? htmlBody, string? textBody)
        {
            var data = new CustomerData
            {
                IsBooking = true
            };

            try
            {
                var normalizedInput = NormalizeRawEmailInput(subject, htmlBody, textBody);
                var effectiveSubject = normalizedInput.Subject;
                var effectiveHtmlBody = normalizedInput.HtmlBody;
                var effectiveTextBody = normalizedInput.TextBody;

                var mergedText = BuildMergedText(effectiveSubject, effectiveHtmlBody, effectiveTextBody);
                var detailsV2 = ExtractViatorBookingDetailsV2(effectiveSubject, mergedText);
                if (HasViatorDetails(detailsV2))
                {
                    data.ViatorDetailsV2 = detailsV2;
                }

                var code = FirstNonEmpty(
                    detailsV2.BookingReference,
                    ExtractBookingCode(effectiveSubject, mergedText));
                if (!string.IsNullOrWhiteSpace(code))
                {
                    data.BookingCode = code!;
                    data.ExtractedBookingCode = code!;
                }

                data.CustomerEmail = ExtractLabeledValue(mergedText,
                    "Customer Email", "Email", "Traveler Email", "Traveller Email");
                if (string.IsNullOrWhiteSpace(data.CustomerEmail))
                {
                    data.CustomerEmail = ExtractFirstEmail(mergedText);
                }

                var customerName = FirstNonEmpty(
                    detailsV2.LeadTravelerName,
                    ExtractLabeledValue(mergedText,
                    "Lead Traveler Name", "Lead Traveller Name",
                    "Traveler", "Traveller", "Customer Name", "Traveler Name", "Traveller Name", "Name"));
                ParseAndSetCustomerName(customerName, data);
                if (string.IsNullOrWhiteSpace(data.CustomerName))
                {
                    ParseAndSetCustomerName(detailsV2.TravelerNames, data);
                }

                if (string.IsNullOrWhiteSpace(data.CustomerName))
                {
                    var inferredWalkerName = ExtractMinimalNotificationWalkerName(mergedText);
                    ParseAndSetCustomerName(inferredWalkerName, data);
                }

                var labeledPhone = ExtractLabeledValue(mergedText,
                    "Phone", "Customer Phone", "Traveler Phone", "Traveller Phone", "Mobile");
                data.CustomerPhone = NormalizePhoneLine(FirstNonEmpty(detailsV2.PhoneNormalized, labeledPhone));

                var subjectTourName = ExtractSubjectTourName(effectiveSubject);
                var labeledTourName = ExtractLabeledValue(mergedText,
                    "Tour Name", "Tour Grade Description", "Tour Grade",
                    "Tour", "Item", "Activity", "Product");
                data.TourName = FirstNonEmpty(
                    detailsV2.TourName,
                    detailsV2.TourGradeDescription,
                    labeledTourName,
                    subjectTourName) ?? string.Empty;

                var dateLine = FirstNonEmpty(
                    detailsV2.TravelDateRaw,
                    ExtractLabeledValue(mergedText, "Travel Date", "Date", "Tour Date", "Start Date", "When"));
                if (!string.IsNullOrWhiteSpace(dateLine))
                {
                    ParseAndSetTourDateTime(dateLine, data);
                }

                var timeLine = FirstNonEmpty(
                    detailsV2.TourGradeCode,
                    detailsV2.TourGrade,
                    ExtractLabeledValue(mergedText, "Time", "Start Time", "Tour Grade", "Tour Grade Code"));
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

                data.TourLocation = FirstNonEmpty(
                    detailsV2.Location,
                    detailsV2.MeetingPoint,
                    ExtractLabeledValue(mergedText, "Location", "Meeting Point")) ?? string.Empty;
                data.Language = FirstNonEmpty(
                    detailsV2.TourLanguage,
                    ExtractLabeledValue(mergedText, "Tour Language", "Language")) ?? string.Empty;
                data.SpecialRequests = FirstNonEmpty(
                    detailsV2.SpecialRequirements,
                    ExtractLabeledValue(mergedText, "Special Requirements", "Special Requests"));

                var attendeesLine = FirstNonEmpty(
                    detailsV2.TravelersRaw,
                    ExtractLabeledValue(mergedText, "Attendees", "Participants", "Guests", "Travelers", "Travellers"));
                if (!string.IsNullOrWhiteSpace(attendeesLine))
                {
                    ParseAttendees(attendeesLine, data);
                }
                else
                {
                    ParseAttendeeBreakdown(mergedText, data);
                }

                if (data.NumberOfAttendees == 0 && (data.NumberOfAdults > 0 || data.NumberOfChildren > 0))
                {
                    data.NumberOfAttendees = data.NumberOfAdults + data.NumberOfChildren;
                }

                if (detailsV2.TravelersAdults.HasValue && data.NumberOfAdults == 0)
                {
                    data.NumberOfAdults = detailsV2.TravelersAdults.Value;
                }

                if (detailsV2.TravelersChildren.HasValue && data.NumberOfChildren == 0)
                {
                    data.NumberOfChildren = detailsV2.TravelersChildren.Value;
                }

                if (detailsV2.TravelersTotal.HasValue && data.NumberOfAttendees == 0)
                {
                    data.NumberOfAttendees = detailsV2.TravelersTotal.Value;
                }

                if (data.NumberOfAttendees == 0 && (data.NumberOfAdults > 0 || data.NumberOfChildren > 0))
                {
                    data.NumberOfAttendees = data.NumberOfAdults + data.NumberOfChildren;
                }

                if (data.TourDate == null && detailsV2.TravelDate.HasValue)
                {
                    data.TourDate = detailsV2.TravelDate.Value.Date;
                }

                data.CountryName = ExtractCountryFromPhone(data.CustomerPhone);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ViatorBookingParser failed for subject {Subject}", subject ?? string.Empty);
                Console.WriteLine($"[DEBUG] ViatorBookingParser.Parse error: {ex.Message}");
            }

            var vendorName = InferVendorName(subject, htmlBody, textBody);
            return new VendorParseResult
            {
                VendorName = vendorName,
                EmailType = "Booking",
                Data = data
            };
        }

        /// <summary>
        /// Supports raw source fixtures that include headers + delimiter + html payload in one text blob.
        /// </summary>
        private static (string? Subject, string? HtmlBody, string? TextBody) NormalizeRawEmailInput(
            string? subject,
            string? htmlBody,
            string? textBody)
        {
            var effectiveSubject = subject;
            var effectiveHtmlBody = htmlBody;
            var effectiveTextBody = textBody;

            if (string.IsNullOrWhiteSpace(textBody))
            {
                return (effectiveSubject, effectiveHtmlBody, effectiveTextBody);
            }

            var raw = textBody!;
            if (!LooksLikeRawEmailBlob(raw))
            {
                return (effectiveSubject, effectiveHtmlBody, effectiveTextBody);
            }

            if (string.IsNullOrWhiteSpace(effectiveSubject))
            {
                var extractedSubject = ExtractHeaderValue(raw, "Subject");
                if (!string.IsNullOrWhiteSpace(extractedSubject))
                {
                    effectiveSubject = extractedSubject;
                }
            }

            if (TryExtractBodySection(raw, out var bodySection))
            {
                if (ContainsHtmlMarkup(bodySection))
                {
                    if (string.IsNullOrWhiteSpace(effectiveHtmlBody))
                    {
                        effectiveHtmlBody = bodySection;
                    }

                    effectiveTextBody = ExtractHtmlText(bodySection);
                }
                else
                {
                    effectiveTextBody = bodySection;
                }
            }

            return (effectiveSubject, effectiveHtmlBody, effectiveTextBody);
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

        private static ViatorBookingDetailsV2 ExtractViatorBookingDetailsV2(string? subject, string mergedText)
        {
            var details = new ViatorBookingDetailsV2
            {
                BookingReference = NormalizeBookingCode(
                    FirstNonEmpty(
                        ExtractLabeledValue(mergedText, "Booking Reference", "Booking Reference Number"),
                        ExtractBookingCode(subject, mergedText))) ?? string.Empty,
                TourName = CleanLabelValue(ExtractLabeledValue(mergedText, "Tour Name")),
                TravelDateRaw = CleanLabelValue(ExtractLabeledValue(mergedText, "Travel Date")),
                LeadTravelerName = CleanLabelValue(ExtractLabeledValue(mergedText, "Lead Traveler Name", "Lead Traveller Name")),
                TravelerNames = CleanLabelValue(ExtractLabeledValue(mergedText, "Traveler Names", "Traveller Names")),
                TravelersRaw = CleanLabelValue(ExtractLabeledValue(mergedText, "Travelers", "Travellers")),
                ProductCode = CleanLabelValue(ExtractLabeledValue(mergedText, "Product Code")),
                TourGrade = CleanLabelValue(ExtractLabeledValue(mergedText, "Tour Grade")),
                TourGradeCode = CleanLabelValue(ExtractLabeledValue(mergedText, "Tour Grade Code")),
                TourGradeDescription = CleanLabelValue(ExtractLabeledValue(mergedText, "Tour Grade Description")),
                TourLanguage = CleanLabelValue(ExtractLabeledValue(mergedText, "Tour Language")),
                Location = CleanLabelValue(ExtractLabeledValue(mergedText, "Location")),
                NetRateRaw = CleanLabelValue(ExtractLabeledValue(mergedText, "Net Rate")),
                MeetingPoint = CleanLabelValue(ExtractLabeledValue(mergedText, "Meeting Point")),
                SpecialRequirements = CleanLabelValue(ExtractLabeledValue(mergedText, "Special Requirements")),
                PhoneRaw = CleanLabelValue(ExtractLabeledValue(mergedText, "Phone")),
                OptionalText = CleanLabelValue(ExtractLabeledValue(mergedText, "Optional"))
            };

            if (!string.IsNullOrWhiteSpace(details.PhoneRaw))
            {
                details.PhoneNormalized = NormalizePhoneLine(details.PhoneRaw);
            }

            if (!string.IsNullOrWhiteSpace(details.TravelDateRaw))
            {
                details.TravelDate = ParseViatorDate(details.TravelDateRaw);
            }

            ParseTravelers(details.TravelersRaw, details);
            ParseNetRate(details.NetRateRaw, details);
            return details;
        }

        private static bool HasViatorDetails(ViatorBookingDetailsV2 details)
        {
            return !string.IsNullOrWhiteSpace(details.BookingReference) ||
                   !string.IsNullOrWhiteSpace(details.TourName) ||
                   !string.IsNullOrWhiteSpace(details.TravelDateRaw) ||
                   !string.IsNullOrWhiteSpace(details.LeadTravelerName) ||
                   !string.IsNullOrWhiteSpace(details.ProductCode) ||
                   !string.IsNullOrWhiteSpace(details.TourGradeCode) ||
                   !string.IsNullOrWhiteSpace(details.TourGradeDescription) ||
                   !string.IsNullOrWhiteSpace(details.TourLanguage) ||
                   !string.IsNullOrWhiteSpace(details.Location) ||
                   !string.IsNullOrWhiteSpace(details.NetRateRaw) ||
                   !string.IsNullOrWhiteSpace(details.MeetingPoint) ||
                   !string.IsNullOrWhiteSpace(details.SpecialRequirements) ||
                   !string.IsNullOrWhiteSpace(details.PhoneNormalized);
        }

        private static string CleanLabelValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return Regex.Replace(value, @"\s+", " ").Trim();
        }

        private static string NormalizePhoneLine(string? rawPhone)
        {
            if (string.IsNullOrWhiteSpace(rawPhone))
            {
                return string.Empty;
            }

            var cleaned = Regex.Replace(rawPhone, @"\s*Send\s+the\s+customer\s+a\s+message\.?\s*$", string.Empty, RegexOptions.IgnoreCase).Trim();
            var match = Regex.Match(cleaned, @"(?<phone>\+?\d[\d\-\s\(\)]{6,}\d)");
            if (match.Success)
            {
                return Regex.Replace(match.Groups["phone"].Value, @"\s+", " ").Trim();
            }

            return cleaned;
        }

        private static DateTime? ParseViatorDate(string rawDate)
        {
            if (string.IsNullOrWhiteSpace(rawDate))
            {
                return null;
            }

            if (DateTime.TryParse(rawDate, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dt))
            {
                return dt.Date;
            }

            var noWeekday = Regex.Replace(rawDate, @"^\w+,\s*", string.Empty);
            if (DateTime.TryParse(noWeekday, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dtNoWeekday))
            {
                return dtNoWeekday.Date;
            }

            return null;
        }

        private static void ParseTravelers(string travelersRaw, ViatorBookingDetailsV2 details)
        {
            if (string.IsNullOrWhiteSpace(travelersRaw))
            {
                return;
            }

            details.TravelersAdults = TryParseIntCapture(travelersRaw, @"(?<value>\d+)\s*Adults?");
            details.TravelersChildren = TryParseIntCapture(travelersRaw, @"(?<value>\d+)\s*Child(?:ren)?");
            details.TravelersTotal = TryParseIntCapture(travelersRaw, @"(?<value>\d+)\s*(?:Travelers?|Travellers?|Guests?|Participants?)");

            if (!details.TravelersTotal.HasValue &&
                (details.TravelersAdults.HasValue || details.TravelersChildren.HasValue))
            {
                details.TravelersTotal = (details.TravelersAdults ?? 0) + (details.TravelersChildren ?? 0);
            }
        }

        private static void ParseNetRate(string netRateRaw, ViatorBookingDetailsV2 details)
        {
            if (string.IsNullOrWhiteSpace(netRateRaw))
            {
                return;
            }

            var match = Regex.Match(
                netRateRaw,
                @"(?:(?<currency>[A-Z]{3})\s*)?\$?\s*(?<amount>\d+(?:\.\d{1,2})?)",
                RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return;
            }

            if (match.Groups["currency"].Success)
            {
                details.NetRateCurrency = match.Groups["currency"].Value.Trim().ToUpperInvariant();
            }

            if (decimal.TryParse(match.Groups["amount"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            {
                details.NetRateAmount = amount;
            }
        }

        private static int? TryParseIntCapture(string text, string pattern)
        {
            var match = Regex.Match(text ?? string.Empty, pattern, RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return null;
            }

            return int.TryParse(match.Groups["value"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return null;
        }

        private static string? NormalizeBookingCode(string? bookingCode)
        {
            if (string.IsNullOrWhiteSpace(bookingCode))
            {
                return null;
            }

            var normalized = bookingCode.Trim();
            normalized = normalized.TrimStart('#');
            return Regex.Replace(normalized, @"\s+", string.Empty);
        }

        private static bool LooksLikeRawEmailBlob(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            if (!Regex.IsMatch(raw, @"(?im)^\s*subject\s*:\s*.+$"))
            {
                return false;
            }

            return Regex.IsMatch(raw, @"(?m)^\s*-{6,}\s*$") ||
                   Regex.IsMatch(raw, @"<\s*(html|body|div|table)\b", RegexOptions.IgnoreCase);
        }

        private static string? ExtractHeaderValue(string raw, string headerName)
        {
            var match = Regex.Match(
                raw,
                $@"(?im)^\s*{Regex.Escape(headerName)}\s*:\s*(?<value>.+)$");
            if (!match.Success)
            {
                return null;
            }

            var value = match.Groups["value"].Value.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static bool TryExtractBodySection(string raw, out string bodySection)
        {
            bodySection = string.Empty;

            var delimiterMatch = Regex.Match(raw, @"(?m)^\s*-{6,}\s*$");
            if (delimiterMatch.Success)
            {
                var start = delimiterMatch.Index + delimiterMatch.Length;
                bodySection = raw[start..].Trim();
                return !string.IsNullOrWhiteSpace(bodySection);
            }

            var htmlStart = Regex.Match(raw, @"<\s*(html|body|div|table)\b", RegexOptions.IgnoreCase);
            if (htmlStart.Success)
            {
                bodySection = raw[htmlStart.Index..].Trim();
                return !string.IsNullOrWhiteSpace(bodySection);
            }

            return false;
        }

        private static bool ContainsHtmlMarkup(string content)
            => Regex.IsMatch(content, @"<\s*(html|body|div|table|tr|td|span|a|p|br)\b", RegexOptions.IgnoreCase);

        private static string ExtractHtmlText(string htmlBody)
        {
            if (string.IsNullOrWhiteSpace(htmlBody))
            {
                return string.Empty;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(htmlBody);
            return HtmlEntity.DeEntitize(doc.DocumentNode.InnerText ?? string.Empty);
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        private static string? ExtractBookingCode(string? subject, string mergedText)
        {
            var subjectPatterns = new[]
            {
                @"Booking\s+Receipt\s+For.+?\((?:#)?(?<code>[A-Z0-9\-]{4,})\)",
                @"New\s+Booking\s+for.+?\((?:#)?(?<code>[A-Z0-9\-]{4,})\)",
                @"Booking\s+Invoice:\s*(?<code>[A-Z0-9\-]{4,})",
                @"\((?:#)?(?<code>[A-Z0-9\-]{4,})\)"
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
                @"Booking\s+Invoice:\s*(?<code>[A-Z0-9\-]{4,})",
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
        /// Handles minimal Checkfront notifications like:
        /// "KATIA YAZMIN CAVAZOS LEAL viator/gyg booking"
        /// </summary>
        private static string ExtractMinimalNotificationWalkerName(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var lines = Regex.Split(text, @"\r?\n");
            foreach (var rawLine in lines)
            {
                var line = Regex.Replace(rawLine ?? string.Empty, @"\s+", " ").Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var match = Regex.Match(
                    line,
                    @"^(?<name>[\p{L}\p{M}'\.\-\s]{3,}?)\s+(?:viator(?:\/gyg)?\s+booking)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!match.Success)
                {
                    continue;
                }

                var candidate = Regex.Replace(match.Groups["name"].Value, @"\s+", " ").Trim();
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    return candidate;
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
        private static string? ExtractSubjectTourName(string? subject)
        {
            if (string.IsNullOrWhiteSpace(subject))
            {
                return null;
            }

            var match = Regex.Match(subject, @"Booking\s+Receipt\s+For\s+(?<tour>.+?)\s+\((?:#)?[A-Z0-9\-]{4,}\)", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                match = Regex.Match(subject, @"New\s+Booking\s+for\s+(?<tour>.+?)\s+\((?:#)?[A-Z0-9\-]{4,}\)", RegexOptions.IgnoreCase);
            }
            if (!match.Success)
            {
                return null;
            }

            var value = match.Groups["tour"].Value.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        private static void ParseAttendeeBreakdown(string mergedText, CustomerData data)
        {
            var adults = TryReadInteger(mergedText, @"Adults?\s*[:\-]\s*(?<value>\d+)");
            var children = TryReadInteger(mergedText, @"Children?\s*[:\-]\s*(?<value>\d+)");
            var total = TryReadInteger(mergedText, @"(?:Attendees|Guests|Participants|Travelers|Travellers)\s*[:\-]\s*(?<value>\d+)");

            if (adults.HasValue)
            {
                data.NumberOfAdults = adults.Value;
            }

            if (children.HasValue)
            {
                data.NumberOfChildren = children.Value;
            }

            if (total.HasValue)
            {
                data.NumberOfAttendees = total.Value;
            }

            if (data.NumberOfAttendees == 0 && (data.NumberOfAdults > 0 || data.NumberOfChildren > 0))
            {
                data.NumberOfAttendees = data.NumberOfAdults + data.NumberOfChildren;
            }
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        private static int? TryReadInteger(string text, string pattern)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return null;
            }

            return int.TryParse(match.Groups["value"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
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
