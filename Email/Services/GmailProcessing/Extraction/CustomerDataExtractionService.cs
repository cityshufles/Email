using Email.Services.GmailProcessing.Models;
using Email.Services.GmailProcessing.Normalization;

namespace Email.Services.GmailProcessing.Extraction
{
    /// <summary>
    /// Created: 2025-11-07 00:00 UTC
    /// Extracts customer-centric data from email content (HTML/plaintext).
    /// </summary>
    public sealed class CustomerDataExtractionService
    {
        public CustomerData Extract(string? subject, string? htmlBody, string? textBody)
        {
            var (plain, html) = ForwardedEmailUnwrapper.Unwrap(htmlBody, textBody);
            var normalizedPlain = string.IsNullOrWhiteSpace(plain) ? PlainTextBuilder.FromHtml(html) : plain;

            // Initial scaffold: return minimal fields
            return new CustomerData
            {
                CustomerEmail = EmailNormalizationService.NormalizeEmail(null),
                CustomerPhone = PhoneNormalizationService.NormalizePhone(null),
                BookingCode = null,
                CustomerName = null,
                NumberOfAttendees = 0,
                Language = null,
                TourDate = null,
                TourTime = null,
                TourName = null,
                TourLocation = null,
                CountryName = null
            };
        }
    }
}


