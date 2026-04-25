using System;

namespace Email.Services.GmailProcessing.Models
{
    /// <summary>
    /// Created: 2025-11-07 00:00 UTC
    /// Updated: 2025-11-26 00:00 UTC - Added missing DB fields for booking code tracking and content storage
    /// SQL Server representation of dbo.AutomaticGmail_ProcessedEmails (full schema).
    /// </summary>
    public sealed class ProcessedEmailRecord
    {
        public int Id { get; set; }
        public int InboxEmailId { get; set; }
        public string MessageId { get; set; } = string.Empty;
        public string VendorName { get; set; } = string.Empty;
        public bool VendorManuallyOverridden { get; set; }
        public DateTime? VendorOverrideAt { get; set; }
        public string EmailType { get; set; } = string.Empty;
        public bool IsTourBookingEmail { get; set; }
        public int? ClassificationRuleId { get; set; }
        public string ProcessingStatus { get; set; } = "completed";
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
        public string? Language { get; set; }
        public string? TourDate { get; set; }
        public string? TourTime { get; set; }
        public string? TourName { get; set; }
        public string? TourLocation { get; set; }
        public string? ActionRequired { get; set; }
        public DateTime? ExtractedAt { get; set; }
        public string? CustomerIdentifier { get; set; }
        public string? RelatedEmailIds { get; set; }
        public bool IsLatestAction { get; set; } = true;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Added 2025-11-26: Content storage fields
        /// <summary>Plain text content extracted from email HTML body</summary>
        public string? PlainTextContent { get; set; }
        /// <summary>Original HTML content from email body</summary>
        public string HtmlContent { get; set; } = string.Empty;

        // Added 2025-11-26: Booking code tracking fields
        /// <summary>Raw booking code as extracted from HTML before any processing logic</summary>
        public string ExtractedBookingCode { get; set; } = string.Empty;
        /// <summary>For modifications: the new booking code (if different from original)</summary>
        public string NewBookingCode { get; set; } = string.Empty;
        /// <summary>For modifications/cancellations: the original booking code being modified or cancelled</summary>
        public string PreviousBookingCode { get; set; } = string.Empty;

        // Added 2025-11-26: Email type flags
        /// <summary>True if this email is a cancellation</summary>
        public bool IsCancellation { get; set; }
        /// <summary>True if this email is a modification</summary>
        public bool IsModification { get; set; }
        /// <summary>True if this email is an original booking/confirmation</summary>
        public bool IsBooking { get; set; }

        // Added 2025-11-26: Attendee breakdown
        /// <summary>Number of adult attendees</summary>
        public int? NumberOfAdults { get; set; }
        /// <summary>Number of child attendees</summary>
        public int? NumberOfChildren { get; set; }
    }
}


