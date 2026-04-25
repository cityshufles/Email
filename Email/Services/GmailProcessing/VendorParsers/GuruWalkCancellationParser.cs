using Email.Services.GmailProcessing.Models;
using HtmlAgilityPack;

namespace Email.Services.GmailProcessing.VendorParsers
{
    /// <summary>
    /// Created: 2025-11-07 00:00 UTC
    /// Updated: 2025-11-26 00:00 UTC - Added booking code field population for cancellations
    /// GuruWalk cancellation parser.
    /// </summary>
    public sealed class GuruWalkCancellationParser : BaseParser
    {
        public override VendorParseResult Parse(string? subject, string? htmlBody, string? textBody)
        {
            var data = new CustomerData
            {
                IsCancellation = true
            };

            var doc = new HtmlAgilityPack.HtmlDocument();
            if (!string.IsNullOrWhiteSpace(htmlBody))
            {
                doc.LoadHtml(htmlBody);
                var detailsAnchor = doc.DocumentNode.SelectSingleNode("//strong[contains(text(), 'Booking code:')]/ancestor::td[1]");
                if (detailsAnchor != null)
                {
                    var detailsNode = detailsAnchor;
                    var fullName = ParseValueFromDiv(detailsNode, "Walker:");
                    ParseAndSetCustomerName(fullName, data);
                    
                    var extractedCode = ParseValueFromDiv(detailsNode, "Booking code:");
                    // For cancellations: the booking code is the one being cancelled
                    data.BookingCode = extractedCode;
                    data.ExtractedBookingCode = extractedCode;
                    data.PreviousBookingCode = extractedCode;  // The booking being cancelled
                    
                    data.CustomerPhone = ParseValueFromDiv(detailsNode, "Phone");
                    data.Language = ParseValueFromDiv(detailsNode, "Language:");
                    SetStandardizedTourTime(ParseValueFromDiv(detailsNode, "Time:"), data);
                    data.TourName = ParseValueFromDiv(detailsNode, "GuruWalk:");
                    ParseAndSetTourDateTime(ParseValueFromDiv(detailsNode, "Date:"), data);
                    ParseAttendees(ParseValueFromDiv(detailsNode, "Attendees:"), data);
                    data.CountryName = ExtractCountryFromPhone(data.CustomerPhone);
                }
            }

            return new VendorParseResult { VendorName = "GuruWalk", EmailType = "Cancellation", Data = data };
        }
    }
}


