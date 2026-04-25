using System;
using System.Collections.Generic;

namespace Email.Models
{
    /// <summary>
    /// Created: 2025-11-16 00:00 UTC - Copied from TourEmails.Models.ProcessedEmail
    /// Model for processed email data.
    /// </summary>
    public class ProcessedEmail
    {
        public int Id { get; set; }
        public int InboxEmailId { get; set; }
        public string MessageId { get; set; } = string.Empty;
        public string VendorName { get; set; } = string.Empty;
        public bool VendorManuallyOverridden { get; set; }
        public DateTime? VendorOverrideAt { get; set; }
        public string EmailType { get; set; } = string.Empty;
        public bool IsTourBookingEmail { get; set; }
        public bool IsCancellation { get; set; }
        public bool IsModification { get; set; }
        public bool IsBooking { get; set; }
        public int? ClassificationRuleId { get; set; }
        public string ProcessingStatus { get; set; } = "pending";
        public DateTime? ProcessingStartedAt { get; set; }
        public DateTime? ProcessingCompletedAt { get; set; }
        public string? ProcessingError { get; set; }
        public int ProcessingAttempts { get; set; }
        public DateTime? NextProcessingAttempt { get; set; }
        public DateTime? RateLimitResetAt { get; set; }
        public string? CustomerName { get; set; }
        public string? BookingCode { get; set; }
        public string? CustomerPhone { get; set; }
        public string? CustomerEmail { get; set; }
        public int? NumberOfAttendees { get; set; }
        public int? NumberOfAdults { get; set; }
        public int? NumberOfChildren { get; set; }
        public string? TourName { get; set; }
        public DateTime? TourDate { get; set; }
        public string? TourTime { get; set; }
        // 2025-12-10 00:00 UTC - Surface day-of-week/display date/time for messaging tokens
        public string? TourDayOfWeek { get; set; }
        public string? DisplayDate { get; set; }
        public string? DisplayTime { get; set; }
        public string? TourLocation { get; set; }
        public string? Language { get; set; }
        public string? CustomerIdentifier { get; set; }
        public bool IsLatestAction { get; set; } = true;
        public string? PlainTextContent { get; set; }
        public string? HtmlContent { get; set; }
        public List<string> AssociatedBookingIds { get; set; } = new();
        public string? BookingAlterationNotes { get; set; }
        public string? ExtractedBookingCode { get; set; }
        public string? NewBookingCode { get; set; }
        public string? PreviousBookingCode { get; set; }
        public DateTime? ExtractedAt { get; set; }
        public bool ManualParsingCompleted { get; set; }
        public string? ManualParsingNotes { get; set; }
        public DateTime? ManualParsingTimestamp { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public string? CalendarEventId { get; set; } // Added 2025-12-21 for Calendar Sync
    }
}

