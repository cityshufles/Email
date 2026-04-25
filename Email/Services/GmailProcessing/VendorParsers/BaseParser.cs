using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Email.Services.GmailProcessing.Extraction;
using Email.Services.GmailProcessing.Models;
using Email.Services.GmailProcessing.Normalization;
using HtmlAgilityPack;

namespace Email.Services.GmailProcessing.VendorParsers
{
    /// <summary>
    /// Created: 2025-11-07 00:00 UTC
    /// Base contract and helpers for vendor parsers.
    /// </summary>
    public abstract class BaseParser
    {
        public abstract VendorParseResult Parse(string? subject, string? htmlBody, string? textBody);

        protected static void ParseAndSetCustomerName(string? fullName, CustomerData data)
        {
            if (string.IsNullOrWhiteSpace(fullName)) return;
            data.CustomerName = fullName.Trim();
        }

        protected static void ParseAndSetTourDateTime(string? dateTimeStr, CustomerData data)
        {
            if (string.IsNullOrWhiteSpace(dateTimeStr)) return;

            DateTime? parsedDateTime = null;

            if (DateTime.TryParse(dateTimeStr, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dt))
            {
                parsedDateTime = dt;
            }
            else
            {
                // Sometimes strings include leading weekday or other text; try removing leading word + comma
                var compact = Regex.Replace(dateTimeStr!, @"^\w+,\s*", string.Empty);
                if (DateTime.TryParse(compact, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dt2))
                {
                    parsedDateTime = dt2;
                }
            }

            if (parsedDateTime.HasValue)
            {
                data.TourDate = parsedDateTime.Value.Date;
                if (string.IsNullOrWhiteSpace(data.TourTime))
                {
                    var standardizedFromDate = TimeStandardizationService.StandardizeTime(parsedDateTime.Value.ToString("h:mm tt", CultureInfo.InvariantCulture));
                    if (!string.IsNullOrWhiteSpace(standardizedFromDate))
                    {
                        data.TourTime = standardizedFromDate!;
                    }
                }
            }

            // Standardize any existing time (from dedicated "Time:" fields) after parsing
            if (!string.IsNullOrWhiteSpace(data.TourTime))
            {
                var standardized = TimeStandardizationService.StandardizeTime(data.TourTime);
                if (!string.IsNullOrWhiteSpace(standardized))
                {
                    data.TourTime = standardized!;
                }
            }
            else
            {
                // Final fallback: attempt to extract time directly from the raw string
                var extracted = TimeStandardizationService.ExtractTimeComponent(dateTimeStr);
                if (!string.IsNullOrWhiteSpace(extracted))
                {
                    var standardized = TimeStandardizationService.StandardizeTime(extracted);
                    if (!string.IsNullOrWhiteSpace(standardized))
                    {
                        data.TourTime = standardized!;
                    }
                }
            }
        }

        protected static void SetStandardizedTourTime(string? timeString, CustomerData data)
        {
            var norm = TimeStandardizationService.StandardizeTime(timeString);
            if (!string.IsNullOrWhiteSpace(norm))
            {
                data.TourTime = norm!;
            }
        }

        protected static void ParseAttendees(string attendeesStr, CustomerData data)
        {
            if (string.IsNullOrWhiteSpace(attendeesStr)) return;
            var s = Regex.Replace(attendeesStr, "\\s+", " ").Trim();

            // Original pipeline behavior:
            // 1) Try FreeTour-specific
            if (TryParseFreeTourAttendees(s, data)) { data.NumberOfAttendees = data.NumberOfAdults + data.NumberOfChildren; return; }
            // 2) Try GuruWalk-specific
            if (TryParseGuruWalkAttendees(s, data)) { data.NumberOfAttendees = data.NumberOfAdults + data.NumberOfChildren; return; }
            // 3) Generic fallback
            ParseGenericAttendees(s, data);
        }

        private static bool TryParseFreeTourAttendees(string attendeesStr, CustomerData data)
        {
            try
            {
                var guestsMatch = Regex.Match(attendeesStr, @"guests:\s*(\d+)\s*people", RegexOptions.IgnoreCase);
                var childrenMatch = Regex.Match(attendeesStr, @"children\s*\(≤?15\s*y\.?o\.?\):\s*(\d+)", RegexOptions.IgnoreCase);

                var parsed = false;
                if (guestsMatch.Success && int.TryParse(guestsMatch.Groups[1].Value, out var adults))
                {
                    data.NumberOfAdults = adults;
                    parsed = true;
                }
                if (childrenMatch.Success && int.TryParse(childrenMatch.Groups[1].Value, out var children))
                {
                    data.NumberOfChildren = children;
                    parsed = true;
                }
                return parsed;
            }
            catch { return false; }
        }

        private static bool TryParseGuruWalkAttendees(string attendeesStr, CustomerData data)
        {
            try
            {
                var match = Regex.Match(attendeesStr, @"attendees:\s*(\d+)\s*adults?(?:\s*,\s*(\d+)\s*children)?", RegexOptions.IgnoreCase);
                if (!match.Success) return false;

                if (int.TryParse(match.Groups[1].Value, out var adults)) data.NumberOfAdults = adults;
                if (match.Groups[2].Success && int.TryParse(match.Groups[2].Value, out var children)) data.NumberOfChildren = children;
                return true;
            }
            catch { return false; }
        }

        private static void ParseGenericAttendees(string attendeesStr, CustomerData data)
        {
            int adults = 0;
            int children = 0;

            // Generic label-based matches
            var adultMatch = Regex.Match(attendeesStr, @"(\d+)\s*adults?", RegexOptions.IgnoreCase);
            if (adultMatch.Success) int.TryParse(adultMatch.Groups[1].Value, out adults);

            // Support both "children" and "Children (Age 0-15): N" forms
            var childMatch = Regex.Match(attendeesStr, @"(\d+)\s*child(ren)?", RegexOptions.IgnoreCase);
            if (childMatch.Success)
            {
                int.TryParse(childMatch.Groups[1].Value, out children);
            }
            else
            {
                var childAgeMatch = Regex.Match(attendeesStr, @"Children\s*\(Age\s*0-15\):\s*(\d+)", RegexOptions.IgnoreCase);
                if (childAgeMatch.Success) int.TryParse(childAgeMatch.Groups[1].Value, out children);
            }

            if (adults == 0 && children == 0)
            {
                // Ultimate fallback: find a single number in the text
                var totalMatch = Regex.Match(attendeesStr, @"(\d+)");
                if (totalMatch.Success) int.TryParse(totalMatch.Groups[1].Value, out adults);
            }

            data.NumberOfAdults = adults > 0 ? adults : 0;
            data.NumberOfChildren = children > 0 ? children : 0;
            data.NumberOfAttendees = data.NumberOfAdults + data.NumberOfChildren;
        }
        protected static string ParseValueFromDiv(HtmlNode divNode, string label)
        {
            // Support <b> and <strong>
            var labelNode = divNode.SelectSingleNode($".//b[contains(text(), '{label}')]")
                         ?? divNode.SelectSingleNode($".//strong[contains(text(), '{label}')]");
            if (labelNode != null)
            {
                var parent = labelNode.ParentNode;
                if (parent != null)
                {
                    var fullText = parent.InnerText.Trim();
                    var labelText = labelNode.InnerText.Trim();
                    var value = fullText.Replace(labelText, string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
                    if (label.Equals("GuruWalk:", StringComparison.OrdinalIgnoreCase))
                    {
                        var link = parent.SelectSingleNode(".//a");
                        if (link != null) value = link.InnerText.Trim();
                    }
                    return value;
                }
            }
            return string.Empty;
        }

        protected static string ParseValueFromTable(HtmlNode tableNode, string label)
        {
            var valueNode = tableNode.SelectSingleNode($".//strong[contains(text(), '{label}')]/ancestor::td[1]/following-sibling::td[1]");
            if (valueNode != null) return valueNode.InnerText.Trim();

            var labelNode = tableNode.SelectSingleNode($".//strong[contains(text(), '{label}')]");
            if (labelNode != null)
            {
                var row = labelNode.SelectSingleNode("ancestor::tr[1]");
                var tds = row?.SelectNodes(".//td");
                if (tds != null && tds.Count >= 2)
                {
                    for (var i = 0; i < tds.Count - 1; i++)
                    {
                        if (tds[i].InnerText.Contains(label, StringComparison.OrdinalIgnoreCase))
                        {
                            return tds[i + 1].InnerText.Trim();
                        }
                    }
                }
            }
            return string.Empty;
        }

        protected static string ExtractCountryFromPhone(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone)) return string.Empty;
            var normalized = PhoneNormalizationService.NormalizePhone(phone);
            return CountryExtractor.TryGetCountryNameFromPhone(normalized, out var name) ? (name ?? string.Empty) : string.Empty;
        }
    }

    public sealed class VendorParseResult
    {
        public string VendorName { get; set; } = string.Empty;
        public string EmailType { get; set; } = string.Empty;
        public CustomerData Data { get; set; } = new CustomerData();
    }
}


