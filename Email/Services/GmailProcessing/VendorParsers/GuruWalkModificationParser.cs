using Email.Services.GmailProcessing.Models;
using HtmlAgilityPack;

namespace Email.Services.GmailProcessing.VendorParsers
{
    /// <summary>
    /// Created: 2025-11-07 00:00 UTC
    /// Updated: 2025-11-26 00:00 UTC - Added Previous/New booking code extraction for modifications
    /// GuruWalk modification parser.
    /// </summary>
    public sealed class GuruWalkModificationParser : BaseParser
    {
        public override VendorParseResult Parse(string? subject, string? htmlBody, string? textBody)
        {
            var data = new CustomerData
            {
                IsModification = true
            };

            var doc = new HtmlAgilityPack.HtmlDocument();
            if (!string.IsNullOrWhiteSpace(htmlBody))
            {
                doc.LoadHtml(htmlBody);

                // Extract Previous booking code from "Details of the previous booking:" section
                const string previousHeaderText = "Details of the previous booking:";
                var previousHeaderNode = doc.DocumentNode.SelectSingleNode($"//span[contains(text(), '{previousHeaderText}')]");
                var previousTableNode = previousHeaderNode?.SelectSingleNode("ancestor::table[1]/following-sibling::table[1]");
                
                if (previousTableNode != null)
                {
                    var previousCode = ParseValueFromTable(previousTableNode, "Booking code:");
                    if (!string.IsNullOrWhiteSpace(previousCode))
                    {
                        data.PreviousBookingCode = previousCode;
                    }
                }

                // Extract New booking details from "Details of the new booking:" section
                const string newHeaderText = "Details of the new booking:";
                var newHeaderNode = doc.DocumentNode.SelectSingleNode($"//span[contains(text(), '{newHeaderText}')]");
                var newTableNode = newHeaderNode?.SelectSingleNode("ancestor::table[1]/following-sibling::table[1]");

                if (newTableNode != null)
                {
                    var fullName = ParseValueFromTable(newTableNode, "Walker:");
                    ParseAndSetCustomerName(fullName, data);
                    
                    var newBookingCode = ParseValueFromTable(newTableNode, "Booking code:");
                    data.BookingCode = newBookingCode;
                    data.NewBookingCode = newBookingCode;
                    data.ExtractedBookingCode = newBookingCode;
                    
                    // If no previous code was found, use the new code as the previous (same code scenario)
                    if (string.IsNullOrWhiteSpace(data.PreviousBookingCode))
                    {
                        data.PreviousBookingCode = newBookingCode;
                    }
                    
                    data.CustomerPhone = ParseValueFromTable(newTableNode, "Phone:");
                    data.Language = ParseValueFromTable(newTableNode, "Language:");
                    SetStandardizedTourTime(ParseValueFromTable(newTableNode, "Time:"), data);
                    data.TourName = ParseValueFromTable(newTableNode, "GuruWalk:");
                    ParseAndSetTourDateTime(ParseValueFromTable(newTableNode, "Date:"), data);
                    ParseAttendees(ParseValueFromTable(newTableNode, "Attendees:"), data);
                    data.CountryName = ExtractCountryFromPhone(data.CustomerPhone);
                }
            }

            return new VendorParseResult { VendorName = "GuruWalk", EmailType = "Modification", Data = data };
        }
    }
}


