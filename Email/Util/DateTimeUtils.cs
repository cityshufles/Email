using System;
using Microsoft.AspNetCore.Components;

namespace Email.Util
{
    /// <summary>
    /// Created: 2025-11-13 00:00 UTC - DateTime utility methods for timezone conversions and formatting
    /// Migrated: 2025-11-21 00:00 UTC - from CityShufflesGuides to Email (verbatim)
    /// </summary>
    public static class DateTimeUtils
    {
        /// <summary>
        /// Converts UTC DateTime to Eastern Time and formats for display with (EST) indicator
        /// </summary>
        /// <param name="utcDate">The UTC DateTime to convert</param>
        /// <returns>MarkupString with formatted date and (EST) in small muted text</returns>
        public static MarkupString ConvertToEstDisplay(DateTime utcDate)
        {
            // Created: 2025-11-13 - Convert UTC server time to Eastern Time for display
            try
            {
                var estTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                var estDate = TimeZoneInfo.ConvertTimeFromUtc(utcDate, estTimeZone);
                var formatted = estDate.ToString("MM/dd h:mm:ss tt");
                return new MarkupString($"{formatted} <small class=\"text-muted\">(EST)</small>");
            }
            catch
            {
                // Fallback to original format if timezone conversion fails
                return new MarkupString(utcDate.ToString("MM/dd/yyyy h:mm:ss tt"));
            }
        }
    }
}


