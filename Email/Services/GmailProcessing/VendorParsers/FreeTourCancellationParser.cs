using Email.Services.GmailProcessing.Models;
using HtmlAgilityPack;
using System.Text.RegularExpressions;

namespace Email.Services.GmailProcessing.VendorParsers
{
    /// <summary>
    /// Created: 2025-11-07 00:00 UTC
    /// Updated: 2025-11-26 00:00 UTC - Added booking code field population for cancellations
    /// FreeTour cancellation parser.
    /// </summary>
    public sealed class FreeTourCancellationParser : BaseParser
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
                var cancellationNode = doc.DocumentNode.SelectSingleNode("//div[@class='email-text']");
                if (cancellationNode != null)
                {
                    var txt = cancellationNode.InnerText;

                    var customerMatch = Regex.Match(txt, @"Your customer:\s*(.*?)\s+has cancelled", RegexOptions.IgnoreCase);
                    if (customerMatch.Success)
                    {
                        ParseAndSetCustomerName(customerMatch.Groups[1].Value.Trim(), data);
                    }

                    // Booking code patterns like (#ABC123) or similar
                    var codeMatch = Regex.Match(txt, @"\(#([^)]+)\)");
                    if (codeMatch.Success)
                    {
                        var extractedCode = codeMatch.Groups[1].Value.Trim();
                        // For cancellations: the booking code is the one being cancelled
                        data.BookingCode = extractedCode;
                        data.ExtractedBookingCode = extractedCode;
                        data.PreviousBookingCode = extractedCode;  // The booking being cancelled
                    }

                    var tourMatch = Regex.Match(txt, @"for\s+(.*?)\s+at", RegexOptions.IgnoreCase);
                    if (tourMatch.Success)
                    {
                        data.TourName = tourMatch.Groups[1].Value.Trim();
                    }

                    var timeMatch = Regex.Match(txt, @"at\s+([^\s]+)\s+on", RegexOptions.IgnoreCase);
                    if (timeMatch.Success)
                    {
                        SetStandardizedTourTime(timeMatch.Groups[1].Value.Trim(), data);
                    }

                    var dateMatch = Regex.Match(txt, @"on\s+(\d{4}-\d{2}-\d{2})", RegexOptions.IgnoreCase);
                    if (dateMatch.Success)
                    {
                        ParseAndSetTourDateTime(dateMatch.Groups[1].Value.Trim(), data);
                    }
                }
            }

            return new VendorParseResult { VendorName = "FreeTour", EmailType = "Cancellation", Data = data };
        }
    }
}


