using Email.Services.GmailProcessing.Models;
using HtmlAgilityPack;
using System.Text.RegularExpressions;

namespace Email.Services.GmailProcessing.VendorParsers
{
    /// <summary>
    /// Created: 2025-11-07 00:00 UTC
    /// Updated: 2025-11-26 00:00 UTC - Added ExtractedBookingCode population
    /// FreeTour reservation/confirmation parser.
    /// </summary>
    public sealed class FreeTourReservationParser : BaseParser
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
                var bookingDetailsNode = doc.DocumentNode.SelectSingleNode("//div[@class='email-message']");
                var tourNameNode = doc.DocumentNode.SelectSingleNode("//div[@class='email-title']");

                if (tourNameNode != null)
                {
                    data.TourName = tourNameNode.InnerText.Trim();
                }

                if (bookingDetailsNode != null)
                {
                    var fullName = ParseValueFromDiv(bookingDetailsNode, "Booking Name:");
                    ParseAndSetCustomerName(fullName, data);
                    data.CustomerPhone = ParseValueFromDiv(bookingDetailsNode, "Booking phone:");
                    data.CustomerEmail = ParseValueFromDiv(bookingDetailsNode, "Booking E-mail:");
                    
                    var extractedCode = ParseValueFromDiv(bookingDetailsNode, "Booking Reference Number:");
                    // For bookings: set all booking code fields to the extracted code
                    data.BookingCode = extractedCode;
                    data.ExtractedBookingCode = extractedCode;
                    
                    data.Language = ParseValueFromDiv(bookingDetailsNode, "Language:");

                    var dateTimeStr = ParseValueFromDiv(bookingDetailsNode, "Date of the Tour:");
                    ParseAndSetTourDateTime(dateTimeStr, data);

                    var adultsStr = ParseValueFromDiv(bookingDetailsNode, "Adults:");
                    var childrenStr = ParseValueFromDiv(bookingDetailsNode, "Children (Age 0-15):");
                    // Parse adults
                    if (!string.IsNullOrWhiteSpace(adultsStr))
                    {
                        var s = Regex.Replace(adultsStr, @"\s+", " ").Trim();
                        var m = Regex.Match(s, @"(\d+)");
                        if (m.Success && int.TryParse(m.Groups[1].Value, out var a)) data.NumberOfAdults = a;
                    }
                    // Parse children
                    if (!string.IsNullOrWhiteSpace(childrenStr))
                    {
                        var s = Regex.Replace(childrenStr, @"\s+", " ").Trim();
                        var m = Regex.Match(s, @"(\d+)");
                        if (m.Success && int.TryParse(m.Groups[1].Value, out var kids)) data.NumberOfChildren = kids;
                    }
                    data.NumberOfAttendees = data.NumberOfAdults + data.NumberOfChildren;

                    data.CountryName = ExtractCountryFromPhone(data.CustomerPhone);
                }
            }

            return new VendorParseResult { VendorName = "FreeTour", EmailType = "Confirmation", Data = data };
        }
    }
}


