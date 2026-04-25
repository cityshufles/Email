using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Email.Services
{
    /// <summary>
    /// Created: 12/19/2025 5:12 PM (local) | 2025-12-19T17:12:00
    /// Purpose: Send vCards via SMTP using configured Gmail credentials (Gmail:Email / Gmail:Password).
    /// Notes:
    /// - Uses Gmail SMTP: smtp.gmail.com:587 (TLS).
    /// - No API endpoints required; called directly from Blazor page.
    /// </summary>
    public sealed class SmtpVCardEmailService
    {
        private readonly IConfiguration _configuration;
        private readonly VCardExportService _vcardExportService;
        private string? _lastErrorMessage;

        public SmtpVCardEmailService(IConfiguration configuration, VCardExportService vcardExportService)
        {
            _configuration = configuration;
            _vcardExportService = vcardExportService;
        }

        /// <summary>
        /// Created: 12/19/2025 5:12 PM (local) | 2025-12-19T17:12:00
        /// Returns the last send failure message (if any). Intended for UI display/debug.
        /// </summary>
        public string? GetLastErrorMessage() => _lastErrorMessage;

        /// <summary>
        /// Created: 12/19/2025 5:12 PM (local) | 2025-12-19T17:12:00
        /// Validate Gmail SMTP configuration presence (Email/Password).
        /// </summary>
        public bool ValidateSmtpConfig()
        {
            var email = (_configuration["Gmail:Email"] ?? string.Empty).Trim();
            var password = (_configuration["Gmail:Password"] ?? string.Empty).Trim();
            return !string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(password);
        }

        /// <summary>
        /// Created: 12/19/2025 5:12 PM (local) | 2025-12-19T17:12:00
        /// Send vCards via SMTP as ONE combined .vcf file (same content formatting as tree download: VCardExportService.GenerateVcf + UTF-8 BOM).
        /// </summary>
        public async Task<bool> SendVCardAsync(string toAddress, List<string> vCardTexts, string? subject = null, string? attachmentFileName = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            _lastErrorMessage = null;

            if (string.IsNullOrWhiteSpace(toAddress))
            {
                _lastErrorMessage = "Recipient email address is required.";
                return false;
            }

            var fromEmail = (_configuration["Gmail:Email"] ?? string.Empty).Trim();
            var password = (_configuration["Gmail:Password"] ?? string.Empty).Trim();
            var enableSsl = bool.TryParse(_configuration["Gmail:EnableSsl"], out var ssl) ? ssl : true;

            if (string.IsNullOrWhiteSpace(fromEmail) || string.IsNullOrWhiteSpace(password))
            {
                _lastErrorMessage = "Gmail SMTP credentials are missing (Gmail:Email / Gmail:Password).";
                return false;
            }

            var vcards = (vCardTexts ?? new List<string>())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => NormalizeToVCardText(v))
                .ToList();

            if (vcards.Count == 0)
            {
                _lastErrorMessage = "No vCard content provided.";
                return false;
            }

            // Match tree download: build a multi-contact VCF file and emit UTF-8 bytes with BOM (iOS friendliness).
            var vcf = _vcardExportService.GenerateVcf(vcards);
            var vcfBytes = _vcardExportService.GetUtf8Bytes(vcf, includeBom: true);

            try
            {
                using var message = new MailMessage();
                message.From = new MailAddress(fromEmail);
                message.To.Add(new MailAddress(toAddress.Trim()));
                message.Subject = string.IsNullOrWhiteSpace(subject) ? "vCard Export" : subject.Trim();
                message.IsBodyHtml = false;

                message.Body = "vCards are attached as a single .vcf file (multiple contacts).";

                var fileName = string.IsNullOrWhiteSpace(attachmentFileName) ? "walkers.vcf" : attachmentFileName.Trim();
                var ms = new MemoryStream(vcfBytes);
                var attachment = new Attachment(ms, fileName, "text/vcard");
                message.Attachments.Add(attachment);

                using var client = new SmtpClient("smtp.gmail.com", 587)
                {
                    // Gmail SMTP requires TLS on 587.
                    EnableSsl = true,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(fromEmail, password),
                    Timeout = 1000 * 60 * 2 // 2 minutes
                };

                // SmtpClient does not accept CancellationToken; we honor ct before/while building message.
                await client.SendMailAsync(message);
                return true;
            }
            catch (FormatException ex)
            {
                _lastErrorMessage = ex.Message;
                try { Console.WriteLine($"[SmtpVCardEmailService] Invalid email address: {ex.Message}"); } catch { }
                return false;
            }
            catch (SmtpException ex)
            {
                _lastErrorMessage = ex.Message;
                try { Console.WriteLine($"[SmtpVCardEmailService] SMTP error: {ex.Message}"); } catch { }
                return false;
            }
            catch (Exception ex)
            {
                _lastErrorMessage = ex.Message;
                try { Console.WriteLine($"[SmtpVCardEmailService] Send failed: {ex.Message}"); } catch { }
                return false;
            }
        }

        private static string NormalizeToVCardText(string raw)
        {
            var s = raw ?? string.Empty;
            if (s.Length == 0) return string.Empty;

            // Normalize to CRLF to match iOS expectations (do NOT trim; preserve exact vCard endings).
            s = s.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");

            // Ensure the content looks like a vCard block.
            if (!s.Contains("BEGIN:VCARD", StringComparison.OrdinalIgnoreCase))
            {
                return s;
            }

            return s;
        }
    }
}


