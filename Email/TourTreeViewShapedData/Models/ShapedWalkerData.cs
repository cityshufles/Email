using System;
using System.Collections.Generic;

namespace Email.TourTreeViewShapedData.Models
{
    /// <summary>
    /// Created: 2025-11-16 00:00 UTC - Copied from TourEmails.TourTreeViewShapedData.Models.ShapedWalkerData
    /// Represents a single walker/customer entry used in the shaped tree.
    /// </summary>
    public class ShapedWalkerData
    {
        public string MessageId { get; set; } = string.Empty;
        public string BookingCode { get; set; } = string.Empty;
        public int CustomerId { get; set; }
        public string CustomerIdentifier { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string DisplayAttendees { get; set; } = string.Empty;
        public string DisplayPhone { get; set; } = string.Empty;
        public string DisplayLabel { get; set; } = string.Empty;
        public string TourName { get; set; } = string.Empty;
        public string TourDate { get; set; } = string.Empty;
        public string TourTime { get; set; } = string.Empty;
        // 2025-12-10 00:00 UTC - Add day-of-week/display date/time for messaging tokens
        public string? TourDayOfWeek { get; set; }
        public string? DisplayDate { get; set; }
        public string? DisplayTime { get; set; }
        // 2025-12-10 00:00 UTC - Ordinal / month tokens for messaging
        public string? TourMonthAndDayOrdinal { get; set; }
        public string? MonthOfTour { get; set; }
        public string? DisplayDayOrdinal { get; set; }
        // Tour meeting details (used for template tokens)
        public string TourLocation { get; set; } = string.Empty;
        public string? MeetingPlace { get; set; }
        public string? MeetingTime { get; set; }
        public string? MeetingInstructions { get; set; }
        public string? TourStartTime { get; set; }
        public string VendorName { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending";
        public string StatusIcon { get; set; } = string.Empty;
        public string StatusClass { get; set; } = string.Empty;
        public string StatusTitle { get; set; } = string.Empty;
        public int Attendees { get; set; }
        public int NumberOfAdults { get; set; }
        public int NumberOfChildren { get; set; }
        public string Language { get; set; } = string.Empty;
        public string EmailType { get; set; } = string.Empty;
        public bool IsCancellation { get; set; }
        public bool IsModification { get; set; }
        public bool IsConfirmation { get; set; }
        // 2025-12-07 00:00 UTC - message sent tracking for UI badge
        public bool MessageSent { get; set; }
        public DateTime? MessageSentAtUtc { get; set; }
        // 2026-03-13 - Contact outcomes: SMS/WA use 4-state (0 yellow, 1 green, 2 red, 3 gray); platform uses binary (0/1)
        public byte SmsContactState { get; set; }
        public byte WaContactState { get; set; }
        public byte PlatformContactState { get; set; }
        // 2026-03-10 - Guide report status flag used for walker-level submitted indicator in trees
        public bool HasSubmittedGuideReport { get; set; }
        // 2026-03-10 - Guide report status flag used for walker-level unsubmitted indicator in trees
        public bool HasUnsubmittedGuideReport { get; set; }
        // 2026-03-14 - TourReports.PublicId exists for this tour group (used by gallery button color)
        public bool HasGuideGalleryInDb { get; set; }
        // 2026-03-14 - Default gallery route key (TourReports.PublicId) for this tour group
        public string GuideReportPublicId { get; set; } = string.Empty;
        // 2026-03-12 - Explicit check-in flag from Bookings.IsCheckedIn for tree UI
        public bool IsCheckedIn { get; set; }
        // 2026-01-08 - Booking date for chronological ordering (when the booking email was processed)
        public DateTime? BookingDate { get; set; }

        // 2025-12-09 00:00 UTC - per-stage message tracking (welcome/tomorrow/dayOf/thankyou/promo/misc)
        public Dictionary<string, MessageStageStatus> MessageStages { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class MessageStageStatus
    {
        public string Stage { get; set; } = string.Empty;
        public bool SentFlag { get; set; }
        public DateTime? SentAtUtc { get; set; }
        public string? Channel { get; set; }
        public int? TemplateId { get; set; }
    }
}

