using System;

namespace Email.Models
{
    /// <summary>
    /// Created: 2025-11-16 00:00 UTC - Copied from TourEmails.Models.LiveUnifiedEmailListItem
    /// Unified list item representing a live Gmail message, with optional DB matches.
    /// </summary>
    public class LiveUnifiedEmailListItem
    {
        public long Uid { get; set; }
        public string MessageId { get; set; } = string.Empty;
        public string FromName { get; set; } = string.Empty;
        public string FromEmail { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public DateTime ReceivedDate { get; set; }
        public int? InboxEmailId { get; set; }
        public int? ProcessedEmailId { get; set; }
        public string? ProcessedEmailType { get; set; }
        public string? ProcessedStatus { get; set; }
        public string? BookingCode { get; set; }
        public bool HasProcessedEmail { get; set; }
        public string? ProcessedVendorName { get; set; }
        public string? ProcessedCustomerName { get; set; }
        public string? ProcessedCustomerEmail { get; set; }
        public string? ProcessedCustomerPhone { get; set; }
        public string? ProcessedTourName { get; set; }
        public DateTime? ProcessedTourDate { get; set; }
        public string? ProcessedTourTime { get; set; }
        public string? ProcessedTourLocation { get; set; }
        public int? ProcessedNumberOfAdults { get; set; }
        public int? ProcessedNumberOfChildren { get; set; }
        public string? ProcessedLanguage { get; set; }
        public bool ProcessedIsBooking { get; set; }
        public bool ProcessedIsCancellation { get; set; }
        public bool ProcessedIsModification { get; set; }
        public DateTime? ProcessedExtractedAt { get; set; }
        public string? ProcessedBookingAlterationNotes { get; set; }
        public string ProcessedGroupStatus => HasProcessedEmail ? "Processed" : "Not Processed";
        public string IconCss => HasProcessedEmail
            ? (ProcessedIsBooking ? "bi bi-check-circle-fill text-success"
               : ProcessedIsCancellation ? "bi bi-x-circle-fill text-danger"
               : ProcessedIsModification ? "bi bi-pencil-square-fill text-warning"
               : "bi bi-check-circle-fill text-success")
            : "bi bi-exclamation-triangle-fill text-warning";
    }

    /// <summary>
    /// Created: 2025-11-16 00:00 UTC - Copied from TourEmails.Models.LiveEmailDetail
    /// Detailed live email content fetched from IMAP.
    /// </summary>
    public class LiveEmailDetail
    {
        public long Uid { get; set; }
        public string MessageId { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string FromName { get; set; } = string.Empty;
        public string FromEmail { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public DateTime ReceivedDate { get; set; }
        public string TextBody { get; set; } = string.Empty;
        public string HtmlBody { get; set; } = string.Empty;
        public int AttachmentCount { get; set; }
        public string AttachmentNames { get; set; } = string.Empty;
    }
}

