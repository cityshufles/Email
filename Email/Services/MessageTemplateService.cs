using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Globalization;
using Email.Models;
using Email.Models.Mobile;

namespace Email.Services
{
    /// <summary>
    /// Service to handle substitution of placeholders in message templates.
    /// Centralizes logic for both Mobile and Desktop (eventually) to ensure consistency.
    /// Created: 2025-12-18
    /// </summary>
    public class MessageTemplateService
    {
        public string PopulateMessageTemplate(
            InMemoryMessageTemplate template,
            MobileWalkerInfoModel walker,
            DbGuide? guide = null,
            string? allToursLink = null,
            VendorLinkResolution? vendorLinks = null,
            string? galleryLink = null)
        {
            var message = template?.Content ?? string.Empty;
            if (walker == null) return message;

            // Walker-specific tokens
            var (walkerFirst, walkerLast) = SplitPersonName(walker.DisplayName);
            message = ReplaceToken(message, "{name}", walker.DisplayName);
            message = ReplaceToken(message, "{walker}", walker.DisplayName);
            message = ReplaceToken(message, "{walkerFirst}", walkerFirst);
            message = ReplaceToken(message, "{walkerLast}", walkerLast);
            message = ReplaceToken(message, "{guest}", string.Empty);
            // Desktop parity: {phone} in templates refers to the walker/customer phone (company phone is in the default/fallback message text)
            message = ReplaceToken(message, "{phone}", walker.Phone);

            // Tour-specific tokens
            message = ReplaceToken(message, "{tour}", walker.TourName);
            message = ReplaceToken(message, "{tourname}", walker.TourName);
            message = ReplaceToken(message, "{vendorName}", walker.VendorName);

            // Date tokens - use pre-computed fields from walker (populated from ShapedWalkerData)
            var dateLabel = walker.DisplayDate ?? walker.DateLabel;
            message = ReplaceToken(message, "{date}", dateLabel);
            message = ReplaceToken(message, "{tourDayOfWeek}", walker.TourDayOfWeek);
            message = ReplaceToken(message, "{tourMonthAndDayOrdinal}", walker.TourMonthAndDayOrdinal);
            message = ReplaceToken(message, "{monthOfTour}", walker.MonthOfTour);
            message = ReplaceToken(message, "{displayDate}", dateLabel);
            message = ReplaceToken(message, "{displayDayOrdinal}", walker.DisplayDayOrdinal);

            // Time tokens resolution
            var labelTime = ExtractTimeFromTourLabel(walker.TourName);
            var effectiveTourStartTime =
                !string.IsNullOrWhiteSpace(template?.TourStartTime) ? template!.TourStartTime! :
                !string.IsNullOrWhiteSpace(walker.TourStartTime) ? walker.TourStartTime! :
                !string.IsNullOrWhiteSpace(walker.DisplayTime) ? walker.DisplayTime! :
                !string.IsNullOrWhiteSpace(labelTime) ? labelTime :
                string.Empty;

            var effectiveMeetingTime =
                !string.IsNullOrWhiteSpace(template?.MeetingTime) ? template!.MeetingTime! :
                !string.IsNullOrWhiteSpace(walker.MeetingTime) ? walker.MeetingTime! :
                !string.IsNullOrWhiteSpace(walker.DisplayTime) ? walker.DisplayTime! :
                effectiveTourStartTime;

            // 2025-12-18 - Match desktop logic: if meeting time is missing OR matches tour start, set to 10 minutes before tour start
            if (!string.IsNullOrWhiteSpace(effectiveTourStartTime) &&
                (string.IsNullOrWhiteSpace(effectiveMeetingTime) ||
                 string.Equals(effectiveMeetingTime, effectiveTourStartTime, StringComparison.OrdinalIgnoreCase)))
            {
                var shifted = ShiftTimeByMinutes(effectiveTourStartTime, -10);
                if (!string.IsNullOrWhiteSpace(shifted))
                {
                    effectiveMeetingTime = shifted;
                }
            }

            message = ReplaceToken(message, "{tourStartTime}", FormatAsAmPm(effectiveTourStartTime));
            message = ReplaceToken(message, "{displayTime}", FormatAsAmPm(walker.DisplayTime ?? effectiveTourStartTime));
            message = ReplaceToken(message, "{meetingTime}", FormatAsAmPm(effectiveMeetingTime));

            // {tempMeetingTime} - computed from tour label time (or template tour start), shifted -10 minutes
            // {startMinus10} - alias for {tempMeetingTime}
            // Updated 2025-12-18 to include walker tour start time fallback
            var tempMeetingTime = ComputeTempMeetingTime(walker.TourName, effectiveTourStartTime, template?.TourStartTime);
            message = ReplaceToken(message, "{tempMeetingTime}", tempMeetingTime);
            message = ReplaceToken(message, "{startMinus10}", tempMeetingTime);

            // Meeting/location tokens
            // Desktop parity: template values take priority over booking/walker values
            var location =
                !string.IsNullOrWhiteSpace(template?.MeetingPlace) ? template!.MeetingPlace! :
                !string.IsNullOrWhiteSpace(template?.MeetingLocation) ? template!.MeetingLocation! :
                !string.IsNullOrWhiteSpace(walker.MeetingPlace) ? walker.MeetingPlace! :
                !string.IsNullOrWhiteSpace(walker.TourLocation) ? walker.TourLocation! :
                string.Empty;

            var instructions =
                !string.IsNullOrWhiteSpace(template?.MeetingInstructions) ? template!.MeetingInstructions! :
                !string.IsNullOrWhiteSpace(walker.MeetingInstructions) ? walker.MeetingInstructions! :
                string.Empty;

            message = ReplaceToken(message, "{meetingLocation}", location);
            message = ReplaceToken(message, "{meetingPlace}", location); // Alias if needed
            message = ReplaceToken(message, "{meetingInstructions}", instructions);

            // Guide tokens
            string guideFirst = "", guideLast = "", guideFull = "", guidePhone = "", guideEmail = "";
            if (guide != null)
            {
                guideFirst = guide.FirstName ?? "";
                guideLast = guide.LastName ?? "";
                guideFull = string.IsNullOrWhiteSpace(guideLast) ? guideFirst : $"{guideFirst} {guideLast}";
                guidePhone = guide.Phone ?? "";
                guideEmail = guide.Email ?? "";
            }

            message = ReplaceToken(message, "{guide}", guideFull);
            message = ReplaceToken(message, "{guideFirstName}", guideFirst);
            message = ReplaceToken(message, "{guideLastName}", guideLast);
            message = ReplaceToken(message, "{guidePhone}", guidePhone);
            message = ReplaceToken(message, "{guideEmail}", guideEmail);

            // Signature and vendor links
            var resolvedVendorLink = !string.IsNullOrWhiteSpace(template?.VendorLink)
                ? template!.VendorLink!
                : (vendorLinks?.VendorLink ?? string.Empty);
            var resolvedVendorTourLink = vendorLinks?.VendorTourLink ?? string.Empty;
            var resolvedVendorReviewLink = vendorLinks?.VendorReviewLink ?? string.Empty;
            var resolvedAllToursLink = vendorLinks?.AllToursLink ?? allToursLink ?? string.Empty;

            message = ReplaceToken(message, "{signature}", template?.Signature ?? "");
            message = ReplaceToken(message, "{vendorLink}", ResolveLinkTokenDisplayValue(resolvedVendorLink, "vendorLink"));
            message = ReplaceToken(message, "{vendorTourLink}", ResolveLinkTokenDisplayValue(resolvedVendorTourLink, "vendorTourLink"));
            message = ReplaceToken(message, "{vendorReviewLink}", ResolveLinkTokenDisplayValue(resolvedVendorReviewLink, "vendorReviewLink"));
            message = ReplaceToken(message, "{allToursLink}", ResolveLinkTokenDisplayValue(resolvedAllToursLink, "allToursLink"));
            message = ReplaceToken(message, "{galleryLink}", ResolveLinkTokenDisplayValue(galleryLink, "galleryLink"));

            // {tempSignature} - stage-aware signature/link behavior shared with desktop/messages flows
            var stageHint = $"{template?.Type} {template?.Name}".Trim();
            var tempSignature = VendorLinkResolver.ResolveTempSignature(stageHint, walker.VendorName, resolvedAllToursLink);
            message = ReplaceToken(message, "{tempSignature}", tempSignature);

            message = message.Replace("&amp;", "&");

            return message;
        }

        private string ReplaceToken(string text, string token, string? value)
        {
            return text.Replace(token, value ?? string.Empty);
        }

        private static string ResolveLinkTokenDisplayValue(string? value, string tokenName)
        {
            return string.IsNullOrWhiteSpace(value)
                ? $"[missing {tokenName}]"
                : value.Trim();
        }

        private static string FormatAsAmPm(string? time)
        {
            if (string.IsNullOrWhiteSpace(time)) return "";
            // If already formatted with AM/PM, return as-is
            if (time.Contains("AM", StringComparison.OrdinalIgnoreCase) || 
                time.Contains("PM", StringComparison.OrdinalIgnoreCase))
                return time;
            // Try parsing and format
            if (TimeSpan.TryParse(time, out var ts))
            {
                var dt = DateTime.Today.Add(ts);
                return dt.ToString("h:mm tt");
            }
            return time;
        }

        private static string? ShiftTimeByMinutes(string? time, int minutes)
        {
            if (string.IsNullOrWhiteSpace(time)) return null;
            // Clean up AM/PM for TimeSpan parsing if needed (though some parsers handle it, let's differ to DateTime if AM/PM present)
            var clean = time.Trim();
            
            // Try explicit DateTime parse first (handles AM/PM)
            if (DateTime.TryParse(clean, out var dt))
            {
                return dt.AddMinutes(minutes).ToString("h:mm tt");
            }

            // Try TimeSpan (e.g. "14:00")
            if (TimeSpan.TryParse(clean.Replace(" AM", "").Replace(" PM", "").Replace("AM", "").Replace("PM", ""), out var ts))
            {
                 // Handle PM logic manually if raw timespan
                var isPm = clean.Contains("PM", StringComparison.OrdinalIgnoreCase);
                if (isPm && ts.Hours < 12) ts = ts.Add(TimeSpan.FromHours(12));
                
                ts = ts.Add(TimeSpan.FromMinutes(minutes));
                if (ts < TimeSpan.Zero) ts = ts.Add(TimeSpan.FromHours(24));
                var res = DateTime.Today.Add(ts);
                return res.ToString("h:mm tt");
            }
            return null;
        }

        private static string ExtractTimeFromTourLabel(string? label)
        {
            if (string.IsNullOrWhiteSpace(label)) return string.Empty;
            var parts = label.Split('—'); // Em dash
            if (parts.Length >= 2)
            {
                return parts.Last().Trim();
            }
            var dashParts = label.Split('-');
            if (dashParts.Length >= 2)
            {
                var lastPart = dashParts.Last().Trim();
                // Only return if it looks like a time
                if ((lastPart.Contains("AM", StringComparison.OrdinalIgnoreCase) || 
                     lastPart.Contains("PM", StringComparison.OrdinalIgnoreCase)) &&
                     lastPart.Any(char.IsDigit))
                {
                    return lastPart;
                }
            }
            return string.Empty;
        }

        private static string ComputeTempMeetingTime(string? tourName, string? walkerTourStartTime, string? templateTourStartTime)
        {
            var labelTime = ExtractTimeFromTourLabel(tourName);
            var baseTime = !string.IsNullOrWhiteSpace(labelTime)
                ? labelTime
                : (walkerTourStartTime ?? templateTourStartTime ?? string.Empty);

            var shifted = ShiftTimeByMinutes(baseTime, -10);
            var display = FormatAsAmPm(string.IsNullOrWhiteSpace(shifted) ? baseTime : shifted);
            return display ?? string.Empty;
        }

        private static (string First, string Last) SplitPersonName(string? fullName)
        {
            var raw = (fullName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return (string.Empty, string.Empty);
            }

            var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1)
            {
                return (parts[0], string.Empty);
            }

            return (parts[0], string.Join(" ", parts.Skip(1)));
        }
        
        // Helper to get ordinal (1st, 2nd, 3rd)
        private static string GetDayOrdinal(int day)
        {
            return (day % 100) switch
            {
                11 or 12 or 13 => $"{day}th",
                _ => (day % 10) switch
                {
                    1 => $"{day}st",
                    2 => $"{day}nd",
                    3 => $"{day}rd",
                    _ => $"{day}th"
                }
            };
        }
    }
}
