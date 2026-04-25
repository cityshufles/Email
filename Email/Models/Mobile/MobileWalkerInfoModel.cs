using System;
using System.Collections.Generic;
using System.Linq;

namespace Email.Models.Mobile
{
    // Created: 10/7/2025 7:30 PM (local) | 2025-10-07T19:30:00
    // Purpose: Mobile-optimized data models for tour cards and walker lists

    public class MobileWalkerInfoModel
    {
        public string DisplayName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public int Attendees { get; set; }
        public string AttendeeDisplay { get; set; } = string.Empty;
        public string VendorName { get; set; } = string.Empty;
        public string TourName { get; set; } = string.Empty;
        public string DateLabel { get; set; } = string.Empty;
        public int CustomerId { get; set; }
        public string CustomerIdentifier { get; set; } = string.Empty;
        public bool NeedsNumber { get; set; }
        // 2025-12-07 00:00 UTC - Message tracking identifiers for sent flag update
        public string MessageId { get; set; } = string.Empty;
        public string BookingCode { get; set; } = string.Empty;
        public bool MessageSent { get; set; }
        public DateTime? MessageSentAtUtc { get; set; }
        // 2026-03-13 - Contact outcomes: SMS/WA use 4-state (0 yellow, 1 green, 2 red, 3 gray); platform uses binary (0/1)
        public byte SmsContactState { get; set; }
        public byte WaContactState { get; set; }
        public byte PlatformContactState { get; set; }
        // 2026-03-10 - Mirrors ShapedWalkerData.HasSubmittedGuideReport for mobile walker check icon
        public bool HasSubmittedGuideReport { get; set; }
        // 2026-03-10 - Mirrors ShapedWalkerData.HasUnsubmittedGuideReport for mobile walker check icon
        public bool HasUnsubmittedGuideReport { get; set; }
        // 2026-03-14 - Mirrors ShapedWalkerData.HasGuideGalleryInDb for tour-level gallery status
        public bool HasGuideGalleryInDb { get; set; }
        // 2026-03-14 - Mirrors ShapedWalkerData.GuideReportPublicId for opening default gallery route
        public string GuideReportPublicId { get; set; } = string.Empty;
        // 2026-03-12 - Mirrors ShapedWalkerData.IsCheckedIn for walker-level check-in icon
        public bool IsCheckedIn { get; set; }
        // 2026-01-08 - Booking date for chronological ordering
        public DateTime? BookingDate { get; set; }

        // 2025-12-18 00:00 UTC - Pre-computed date/time fields from ShapedWalkerData for template tokens
        public string? TourDayOfWeek { get; set; }
        public string? TourMonthAndDayOrdinal { get; set; }
        public string? MonthOfTour { get; set; }
        public string? DisplayDayOrdinal { get; set; }
        public string? DisplayDate { get; set; }
        public string? DisplayTime { get; set; }
        public string? TourStartTime { get; set; }
        public string? MeetingTime { get; set; }
        public string? MeetingPlace { get; set; }
        public string? MeetingInstructions { get; set; }
        // 2025-12-18 00:00 UTC - Support desktop parity fallback for {meetingLocation} when MeetingPlace is missing
        public string? TourLocation { get; set; }
    }
}


