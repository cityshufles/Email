using System;

namespace Email.Services.GmailCollection.Models
{
    /// <summary>
    /// Created: 2025-11-04 00:00 UTC
    /// Migrated: 2025-11-21 00:00 UTC - from CityShufflesGuides to Email (verbatim)
		/// Updated: 2025-11-28 00:00 UTC - Added ProcessingStatus (SMALLINT)
		/// Updated: 2025-11-30 00:00 UTC - Original booking pointer fields
    /// SQL Server representation of AutomaticGmail_InboxEmails.
    /// </summary>
    public sealed class InboxEmailRecord
    {
        public int Id { get; set; }
        public long Uid { get; set; }
        public string MessageId { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string FromEmail { get; set; } = string.Empty;
        public string? FromName { get; set; }
        public string? ToEmail { get; set; }
        public DateTime ReceivedDate { get; set; }
        public string? TextBody { get; set; }
        public string? HtmlBody { get; set; }
        public string? TextBodyPreview { get; set; }
        public int AttachmentCount { get; set; }
        public string AttachmentNames { get; set; } = "[]";
        public DateTime CollectedAt { get; set; }
        public string CollectionBatchId { get; set; } = string.Empty;
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
		public TourEmailInboxProcessingStatus ProcessingStatus { get; set; }

		// Added: 2025-11-30 00:00 UTC - Original booking pointer fields
		public int? OriginalBookingId { get; set; }
		public string? OriginalBookingCode { get; set; }
		public string? OriginalBookingMessageId { get; set; }
    }
}


