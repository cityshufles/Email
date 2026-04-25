using System;
using System.Linq;
using System.Text.RegularExpressions;
using PhoneNumbers;

namespace Email.Services.GmailProcessing.Normalization
{
    /// <summary>
    /// Created: 2025-11-07 00:00 UTC
    /// Normalizes phone numbers to a consistent format (E.164 when possible).
    /// </summary>
    public static class PhoneNormalizationService
    {
        public static string NormalizePhone(string? rawPhone, string defaultRegion = "US")
        {
            if (string.IsNullOrWhiteSpace(rawPhone)) return string.Empty;
            var input = Regex.Replace(rawPhone.Trim(), @"\s+", " ");

            var normalizedDirect = TryParseToE164(input, defaultRegion);
            if (!string.IsNullOrWhiteSpace(normalizedDirect))
            {
                return normalizedDirect;
            }

            foreach (Match match in Regex.Matches(input, @"(?<!\w)(?:\+?\d[\d\(\)\s\.\-]{6,}\d)"))
            {
                var candidate = match.Value.Trim();
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                var normalizedCandidate = TryParseToE164(candidate, defaultRegion);
                if (!string.IsNullOrWhiteSpace(normalizedCandidate))
                {
                    return normalizedCandidate;
                }
            }

            // Permissive cleanup for values like "(Alternate Phone)US+1 5134486530 Send..."
            // where label/link text is concatenated with the number.
            var permissive = Regex.Replace(input, @"[^\d\+\(\)\s\.\-]", " ");
            permissive = Regex.Replace(permissive, @"\s+", " ").Trim();
            permissive = Regex.Replace(permissive, @"^[^\d\+]+|[^\d]+$", string.Empty);

            var normalizedPermissive = TryParseToE164(permissive, defaultRegion);
            if (!string.IsNullOrWhiteSpace(normalizedPermissive))
            {
                return normalizedPermissive;
            }

            var digits = new string(permissive.Where(char.IsDigit).ToArray());
            if (string.IsNullOrWhiteSpace(digits))
            {
                return string.Empty;
            }

            if (digits.Length == 11 && digits.StartsWith('1'))
            {
                return $"+{digits}";
            }

            if (digits.Length == 10 && string.Equals(defaultRegion, "US", StringComparison.OrdinalIgnoreCase))
            {
                return $"+1{digits}";
            }

            return permissive.Contains('+') ? $"+{digits}" : digits;
        }

        private static string TryParseToE164(string? value, string defaultRegion)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            try
            {
                var util = PhoneNumberUtil.GetInstance();
                var number = util.Parse(value, defaultRegion);
                if (util.IsValidNumber(number))
                {
                    return util.Format(number, PhoneNumberFormat.E164);
                }
            }
            catch
            {
                // Caller handles fallback paths.
            }

            return string.Empty;
        }
    }
}


