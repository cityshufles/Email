using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Email.Models.Mobile;
using Email.TourTreeViewShapedData.Models;

namespace Email.Services
{
    /// <summary>
    /// Created: 2025-12-19 00:00 UTC
    /// Purpose: Centralized vCard generation for iOS-friendly multi-contact .vcf exports.
    /// Notes:
    /// - Always uses CRLF (\r\n) line endings (iOS parser sensitivity).
    /// - Includes VERSION:3.0 and UID to improve iPhone/iPad multi-contact import behavior.
    /// - Provides UTF-8 bytes (with BOM by default) to improve iOS handling of non-ASCII names.
    /// </summary>
    public sealed class VCardExportService
    {
        private const string CRLF = "\r\n";
        // Updated: 12/19/2025 5:12 PM (local) | 2025-12-19T17:12:00
        // Decode HTML entities (e.g., "&amp;") BEFORE vCard escaping, otherwise the entity's ';' becomes '\;'.
        // Also strip CR/LF (and literal "\r"/"\n") from source strings so we don't emit visible "\r" artifacts in NOTE values.
        private static string CleanTextForVCard(string? s)
        {
            var decoded = WebUtility.HtmlDecode(s ?? string.Empty);

            // Handle literal backslash sequences that occasionally leak from upstream text sources.
            decoded = decoded.Replace("\\r", " ").Replace("\\n", " ");

            // Replace actual line breaks with spaces (vCard values should not contain raw CR/LF).
            decoded = decoded.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");

            return decoded.Trim();
        }

        /// <summary>
        /// Created: 2025-12-19 00:00 UTC
        /// Generate one vCard (VERSION 3.0) from shaped walker data.
        /// </summary>
        public string GenerateVCard(ShapedWalkerData w, string? dateLabelOverride = null)
        {
            if (w == null) throw new ArgumentNullException(nameof(w));

            var displayName = CleanTextForVCard(w.DisplayName);
            var phone = CleanTextForVCard(w.DisplayPhone);
            var bookingCode = w.BookingCode ?? string.Empty;
            var messageId = w.MessageId ?? string.Empty;

            var vendorName = CleanTextForVCard(w.VendorName);
            var tourName = CleanTextForVCard(w.TourName);
            var dateLabel = !string.IsNullOrWhiteSpace(dateLabelOverride)
                ? CleanTextForVCard(dateLabelOverride)
                : CleanTextForVCard(w.DisplayDate ?? w.TourDate ?? string.Empty);

            var attendees = w.Attendees;
            var note = $"Vendor: {vendorName} | Tour: {tourName} | Date: {dateLabel}";
            if (attendees > 0) note += $" | Attendees: {attendees}";

            return GenerateVCardCore(
                displayName: displayName,
                phone: phone,
                bookingCode: bookingCode,
                messageId: messageId,
                note: note);
        }

        /// <summary>
        /// Created: 2025-12-19 00:00 UTC
        /// Generate one vCard (VERSION 3.0) from mobile walker model.
        /// </summary>
        public string GenerateVCard(MobileWalkerInfoModel w)
        {
            if (w == null) throw new ArgumentNullException(nameof(w));

            var displayName = CleanTextForVCard(w.DisplayName);
            var phone = CleanTextForVCard(w.Phone);
            var bookingCode = w.BookingCode ?? string.Empty;
            var messageId = w.MessageId ?? string.Empty;

            var vendorName = CleanTextForVCard(w.VendorName);
            var tourName = CleanTextForVCard(w.TourName);
            var dateLabel = CleanTextForVCard(w.DateLabel);

            var attendees = w.Attendees;
            var note = $"Vendor: {vendorName} | Tour: {tourName} | Date: {dateLabel}";
            if (attendees > 0) note += $" | Attendees: {attendees}";

            return GenerateVCardCore(
                displayName: displayName,
                phone: phone,
                bookingCode: bookingCode,
                messageId: messageId,
                note: note);
        }

        /// <summary>
        /// Created: 2025-12-19 00:00 UTC
        /// Generate a .vcf file containing multiple vCards, separated by a blank line (CRLF) between cards.
        /// </summary>
        public string GenerateVcf(IEnumerable<string> vcards)
        {
            if (vcards == null) throw new ArgumentNullException(nameof(vcards));

            var sb = new StringBuilder();
            foreach (var v in vcards.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                // Each vCard returned by GenerateVCardCore ends with CRLF.
                sb.Append(v);
                // vCard spec requires a blank line between multiple vCards for iPhone/desktop compatibility.
                sb.Append(CRLF);
            }

            // Avoid extra blank lines at end of file (but preserve internal separators).
            return sb.ToString().TrimEnd('\r', '\n');
        }

        /// <summary>
        /// Created: 2025-12-19 00:00 UTC
        /// Get UTF-8 bytes for the .vcf file content. BOM is emitted by default for iOS compatibility.
        /// </summary>
        public byte[] GetUtf8Bytes(string vcfContent, bool includeBom = true)
        {
            var content = vcfContent ?? string.Empty;
            var enc = new UTF8Encoding(encoderShouldEmitUTF8Identifier: includeBom);
            return enc.GetBytes(content);
        }

        private static string GenerateVCardCore(string displayName, string phone, string bookingCode, string messageId, string note)
        {
            var sb = new StringBuilder();
            sb.Append("BEGIN:VCARD").Append(CRLF);
            sb.Append("VERSION:3.0").Append(CRLF);

            // Deterministic UID based on contact data so same contact gets same UID on re-export.
            var uid = ComputeDeterministicUid(phone, displayName, bookingCode, messageId);
            sb.Append("UID:").Append(uid).Append(CRLF);

            sb.Append("FN:").Append(EscapeV(displayName)).Append(CRLF);

            var parts = (displayName ?? string.Empty).Split(' ', 2);
            var first = parts.Length > 0 ? parts[0] : string.Empty;
            var last = parts.Length > 1 ? parts[1] : string.Empty;
            sb.Append("N:")
                .Append(EscapeV(last)).Append(';')
                .Append(EscapeV(first)).Append(";;;")
                .Append(CRLF);

            if (!string.IsNullOrWhiteSpace(phone))
            {
                // Keep mobile-compatible TEL style (Apple Contacts accepts item1.* lines).
                sb.Append("item1.TEL;TYPE=CELL,VOICE:").Append(EscapeV(phone)).Append(CRLF);
                sb.Append("item1.X-ABLabel:").Append(CRLF);
            }

            sb.Append("NOTE:").Append(EscapeV(note)).Append(CRLF);
            sb.Append("END:VCARD").Append(CRLF);
            return sb.ToString();
        }

        private static string ComputeDeterministicUid(string phone, string displayName, string bookingCode, string messageId)
        {
            var uidSource = $"{phone ?? string.Empty}|{displayName ?? string.Empty}|{bookingCode ?? string.Empty}|{messageId ?? string.Empty}";
            if (string.IsNullOrWhiteSpace(uidSource))
            {
                return Guid.NewGuid().ToString("D").ToUpperInvariant();
            }

            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(uidSource));
            return new Guid(hash).ToString("D").ToUpperInvariant();
        }

        private static string EscapeV(string v)
            => (v ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace(";", "\\;")
                .Replace(",", "\\,")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r");
    }
}


