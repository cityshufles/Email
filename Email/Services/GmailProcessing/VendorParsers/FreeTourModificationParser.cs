using Email.Services.GmailProcessing.Models;
using HtmlAgilityPack;
using System.Text.RegularExpressions;

namespace Email.Services.GmailProcessing.VendorParsers
{
    /// <summary>
    /// Created: 2025-11-07 00:00 UTC
    /// Updated: 2025-11-26 00:00 UTC - Added booking code field population (FreeTour uses same code for mods)
    /// FreeTour modification parser.
    /// </summary>
    public sealed class FreeTourModificationParser : BaseParser
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
                var modificationDetailsNode = doc.DocumentNode.SelectSingleNode("//div[@class='email-text']");
                var tourNameNode = doc.DocumentNode.SelectSingleNode("//div[@class='email-title']");                

                if (tourNameNode != null)
                {
                    data.TourName = tourNameNode.InnerText.Trim();
                }

                if (modificationDetailsNode != null)
                {
                    var fullText = modificationDetailsNode.InnerText;

                    var customerMatch = Regex.Match(fullText, @"Booking Name:\s*([^\n\r]+)", RegexOptions.IgnoreCase);
                    if (customerMatch.Success)
                    {
                        ParseAndSetCustomerName(customerMatch.Groups[1].Value.Trim(), data);
                    }

                    var emailMatch = Regex.Match(fullText, @"Booking E-mail:\s*([^\n\r]+)", RegexOptions.IgnoreCase);
                    if (emailMatch.Success)
                    {
                        data.CustomerEmail = emailMatch.Groups[1].Value.Trim();
                    }

                    var phoneMatch = Regex.Match(fullText, @"\+?\d{10,15}");
                    if (phoneMatch.Success)
                    {
                        data.CustomerPhone = phoneMatch.Value.Trim();
                    }

                    var bookingCodeMatch = Regex.Match(fullText, @"Booking Reference Number:\s*([^\n\r]+)", RegexOptions.IgnoreCase);
                    if (bookingCodeMatch.Success)
                    {
                        var extractedCode = bookingCodeMatch.Groups[1].Value.Trim();
                        // FreeTour uses the SAME booking code for modifications (no previous/new distinction)
                        // Set all booking code fields to the same value for consistency
                        data.BookingCode = extractedCode;
                        data.ExtractedBookingCode = extractedCode;
                        data.PreviousBookingCode = extractedCode;  // Same as BookingCode for FreeTour
                        data.NewBookingCode = extractedCode;       // Same as BookingCode for FreeTour
                    }

                    var languageMatch = Regex.Match(fullText, @"Language:\s*([^\n\r]+)", RegexOptions.IgnoreCase);
                    if (languageMatch.Success)
                    {
                        data.Language = languageMatch.Groups[1].Value.Trim();
                    }

                    // Prefer "New Date of the Tour:" per modification context
                    var dateTimeMatch = Regex.Match(fullText, @"New Date of the Tour:\s*([^\n\r]+)", RegexOptions.IgnoreCase);
                    if (!dateTimeMatch.Success)
                    {
                        dateTimeMatch = Regex.Match(fullText, @"Previous Date of the Tour:\s*([^\n\r]+)", RegexOptions.IgnoreCase);
                    }
                    if (dateTimeMatch.Success)
                    {
                        ParseAndSetTourDateTime(dateTimeMatch.Groups[1].Value.Trim(), data);
                    }

                    // Attendees (FreeTour variants)
                    var guestsMatch = Regex.Match(fullText, @"Guests:\s*([^\n\r]+)", RegexOptions.IgnoreCase);
                    if (guestsMatch.Success)
                    {
                        var s = Regex.Replace(guestsMatch.Groups[1].Value, @"\s+", " ").Trim();
                        var m = Regex.Match(s, @"(\d+)");
                        if (m.Success && int.TryParse(m.Groups[1].Value, out var a)) data.NumberOfAdults = a;
                    }
                    var childrenMatch = Regex.Match(fullText, @"Children\s*\(≤?15\s*y\.?o\.\):\s*([^\n\r]+)", RegexOptions.IgnoreCase);
                    if (childrenMatch.Success)
                    {
                        var s = Regex.Replace(childrenMatch.Groups[1].Value, @"\s+", " ").Trim();
                        var m = Regex.Match(s, @"(\d+)");
                        if (m.Success && int.TryParse(m.Groups[1].Value, out var kids)) data.NumberOfChildren = kids;
                    }
                    data.NumberOfAttendees = data.NumberOfAdults + data.NumberOfChildren;

                    data.CountryName = ExtractCountryFromPhone(data.CustomerPhone);
                }
            }

            return new VendorParseResult { VendorName = "FreeTour", EmailType = "Modification", Data = data };
        }
    }
}


