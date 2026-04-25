using System;
using System.Text.RegularExpressions;

namespace Email.Services.GmailProcessing.VendorParsers
{
    /// <summary>
    /// Created: 2025-11-07 00:00 UTC
    /// Time parsing/standardization logic copied to match original pipeline behavior.
    /// </summary>
    public static class TimeStandardizationService
    {
        /// <summary>
        /// Standardizes a time string to HH:MM format (24-hour)
        /// </summary>
        /// <param name="timeString">The time string to standardize</param>
        /// <returns>Standardized time in HH:MM format, or null if parsing fails</returns>
        public static string? StandardizeTime(string? timeString)
        {
            if (string.IsNullOrWhiteSpace(timeString))
                return null;

            // Clean up the time string
            timeString = timeString.Trim();

            // Handle common patterns
            if (timeString.Equals("N/A", StringComparison.OrdinalIgnoreCase) ||
                timeString.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                return null;

            // Try to parse various time formats
            var timeFormats = new[]
            {
                // 24-hour formats
                "HH:mm",
                "H:mm",
                "HH:mm:ss",
                "H:mm:ss",

                // 12-hour formats
                "h:mm tt",
                "h:mmt",
                "h tt",
                "ht",
                "h:mm",
                "h",

                // With timezone
                "HH:mm:ss UTC",
                "H:mm:ss UTC",
                "h:mm tt UTC",
                "h:mmt UTC"
            };

            foreach (var format in timeFormats)
            {
                try
                {
                    if (DateTime.TryParseExact(timeString, format, null,
                        System.Globalization.DateTimeStyles.None, out var parsedTime))
                    {
                        return parsedTime.ToString("HH:mm");
                    }
                }
                catch (FormatException)
                {
                    // Keep iterating other formats when one format token is invalid.
                }
            }

            // Try standard DateTime parsing as fallback
            if (DateTime.TryParse(timeString, out var fallbackTime))
            {
                return fallbackTime.ToString("HH:mm");
            }

            // Try to extract time using regex patterns
            var extractedTime = ExtractTimeWithRegex(timeString);
            if (!string.IsNullOrEmpty(extractedTime))
            {
                return StandardizeTime(extractedTime); // Recursive call to standardize the extracted time
            }

            return null;
        }

        /// <summary>
        /// Extracts time using regex patterns for common time formats
        /// </summary>
        /// <param name="text">Text that may contain time information</param>
        /// <returns>Extracted time string or null</returns>
        private static string? ExtractTimeWithRegex(string text)
        {
            var patterns = new[]
            {
                // Time patterns
                @"(\d{1,2}:\d{2}(?::\d{2})?\s*(?:AM|PM)?)",
                @"(\d{1,2}:\d{2}(?::\d{2})?\s*UTC)",
                @"at\s+(\d{1,2}:\d{2}(?::\d{2})?\s*(?:AM|PM)?)",
                @"Time:\s*(\d{1,2}:\d{2}(?::\d{2})?\s*(?:AM|PM)?)",
                @"(\d{1,2}\s*(?:AM|PM))",
                @"(\d{1,2}:\d{2})",

                // FreeTour specific patterns
                @"at\s+([^o]+)\s+on",
                @"Time:\s*([^&\n<]+?)(?=\s+FreeTour:|$)"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
                if (match.Success && match.Groups.Count > 1)
                {
                    var extracted = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(extracted))
                        return extracted;
                }
            }

            return null;
        }

        /// <summary>
        /// Public helper to pull out a time component without standardizing.
        /// </summary>
        public static string? ExtractTimeComponent(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            return ExtractTimeWithRegex(text);
        }

        /// <summary>
        /// Converts standardized time to display format (12-hour with AM/PM)
        /// </summary>
        /// <param name="standardizedTime">Time in HH:MM format</param>
        /// <returns>Display format time (e.g., "2:30 PM")</returns>
        public static string? ToDisplayFormat(string? standardizedTime)
        {
            if (string.IsNullOrWhiteSpace(standardizedTime))
                return null;

            if (DateTime.TryParseExact(standardizedTime, "HH:mm", null,
                System.Globalization.DateTimeStyles.None, out var time))
            {
                return time.ToString("h:mm tt");
            }

            return standardizedTime;
        }

        /// <summary>
        /// Creates a display date string with standardized time
        /// </summary>
        /// <param name="date">Date string in MM/dd/yyyy format</param>
        /// <param name="time">Time string (will be standardized)</param>
        /// <returns>Display date string (e.g., "08/20/2025 at 2:30 PM")</returns>
        public static string CreateDisplayDate(string date, string? time)
        {
            var standardizedTime = StandardizeTime(time);
            var displayTime = ToDisplayFormat(standardizedTime);

            if (!string.IsNullOrEmpty(displayTime))
            {
                return $"{date} at {displayTime}";
            }

            return date;
        }

        /// <summary>
        /// Validates if a time string is in a valid format
        /// </summary>
        /// <param name="timeString">Time string to validate</param>
        /// <returns>True if valid, false otherwise</returns>
        public static bool IsValidTime(string? timeString)
        {
            if (string.IsNullOrWhiteSpace(timeString))
                return false;

            var standardized = StandardizeTime(timeString);
            return !string.IsNullOrEmpty(standardized);
        }
    }
}


