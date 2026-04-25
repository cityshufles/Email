using Email.Services.GmailProcessing.Models;
using HtmlAgilityPack;

namespace Email.Services.GmailProcessing.VendorParsers
{
    /// <summary>
    /// Created: 2025-11-07 00:00 UTC
    /// Updated: 2025-11-26 00:00 UTC - Added ExtractedBookingCode population
    /// GuruWalk confirmation parser.
    /// </summary>
    public sealed class GuruWalkConfirmationParser : BaseParser
    {
        public override VendorParseResult Parse(string? subject, string? htmlBody, string? textBody)
        {
            var data = new CustomerData
            {
                IsBooking = true
            };

            var doc = new HtmlDocument();
            if (!string.IsNullOrWhiteSpace(htmlBody))
            {
                doc.LoadHtml(htmlBody);

                // First, try dashed-table layout block as in legacy implementation
                var bookingDetailsNode = doc.DocumentNode.SelectSingleNode("//td[contains(@style, 'border:1px dashed #c8c8c8')]");
                if (bookingDetailsNode != null)
                {
                    var fullName = ParseValueFromDiv(bookingDetailsNode, "Walker:");
                    ParseAndSetCustomerName(fullName, data);
                    
                    var extractedCode = ParseValueFromDiv(bookingDetailsNode, "Booking code:");
                    // For bookings: set all booking code fields to the extracted code
                    data.BookingCode = extractedCode;
                    data.ExtractedBookingCode = extractedCode;
                    
                    data.CustomerPhone = ParseValueFromDiv(bookingDetailsNode, "Phone");
                    data.Language = ParseValueFromDiv(bookingDetailsNode, "Language:");
                    SetStandardizedTourTime(ParseValueFromDiv(bookingDetailsNode, "Time:"), data);
                    data.TourName = ParseValueFromDiv(bookingDetailsNode, "GuruWalk:");
                    var rawDateStr = ParseValueFromDiv(bookingDetailsNode, "Date:");
                    ParseAndSetTourDateTime(rawDateStr, data);
                    var attendeesStr = ParseValueFromDiv(bookingDetailsNode, "Attendees:");
                    ParseAttendees(attendeesStr, data);
                    data.CountryName = ExtractCountryFromPhone(data.CustomerPhone);
                }
                else
                {
                    // Fallback: global label search (div/table mix)
                    string ExtractValueByLabelGlobal(string label)
                    {
                        var tableValue = ParseValueFromTable(doc.DocumentNode, label);
                        if (!string.IsNullOrWhiteSpace(tableValue)) return tableValue;
                        return ParseValueFromDiv(doc.DocumentNode, label);
                    }

                    var fullName = ExtractValueByLabelGlobal("Walker:");
                    ParseAndSetCustomerName(fullName, data);
                    
                    var extractedCode = ExtractValueByLabelGlobal("Booking code:");
                    // For bookings: set all booking code fields to the extracted code
                    data.BookingCode = extractedCode;
                    data.ExtractedBookingCode = extractedCode;
                    
                    data.CustomerPhone = ExtractValueByLabelGlobal("Phone");
                    data.Language = ExtractValueByLabelGlobal("Language:");
                    SetStandardizedTourTime(ExtractValueByLabelGlobal("Time:"), data);
                    // Tour name may be a link following "GuruWalk:"
                    var tourNameDiv = ExtractValueByLabelGlobal("GuruWalk:");
                    if (!string.IsNullOrWhiteSpace(tourNameDiv))
                        data.TourName = tourNameDiv;
                    else
                    {
                        var tourNameNode = doc.DocumentNode.SelectSingleNode("//strong[contains(text(), 'GuruWalk:')]/following-sibling::a");
                        data.TourName = tourNameNode?.InnerText.Trim();
                    }
                    var rawDate = ExtractValueByLabelGlobal("Date:");
                    ParseAndSetTourDateTime(rawDate, data);
                    var attendees = ExtractValueByLabelGlobal("Attendees:");
                    ParseAttendees(attendees, data);
                    data.CountryName = ExtractCountryFromPhone(data.CustomerPhone);

                    // Last-resort regex over whole HTML text: "at <time> on <date>"
                    if (string.IsNullOrWhiteSpace(data.TourTime))
                    {
                        var plain = doc.DocumentNode.InnerText ?? string.Empty;
                        var m = System.Text.RegularExpressions.Regex.Match(plain, @"at\s+([^o]+)\s+on", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (m.Success)
                        {
                            SetStandardizedTourTime(m.Groups[1].Value.Trim(), data);
                        }
                    }
                }
            }

            return new VendorParseResult { VendorName = "GuruWalk", EmailType = "Confirmation", Data = data };
        }
    }
}


