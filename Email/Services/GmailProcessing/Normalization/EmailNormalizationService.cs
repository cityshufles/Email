using System;

namespace Email.Services.GmailProcessing.Normalization
{
    /// <summary>
    /// Created: 2025-11-07 00:00 UTC
    /// Normalizes email address and related fields.
    /// </summary>
    public static class EmailNormalizationService
    {
        public static string NormalizeEmail(string? email)
        {
            if (string.IsNullOrWhiteSpace(email)) return string.Empty;
            return email.Trim().ToLowerInvariant();
        }

        public static string NormalizeSubject(string? subject)
        {
            return (subject ?? string.Empty).Trim();
        }
    }
}


