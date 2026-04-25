using System;

namespace Email.Models
{
    /// <summary>
    /// Created: 2025-11-16 00:00 UTC - Copied from TourEmails.Models.EmailCollectionSpan
    /// Email collection span options for Gmail collection.
    /// </summary>
    public enum EmailCollectionSpan
    {
        New = 0,
        Today = 1,
        Week = 2,
        Month = 3,
        All = 4,
        Last10 = 10,
        Last20 = 20,
        Last50 = 50,
        Last100 = 100
    }

    /// <summary>
    /// Created: 2025-11-16 00:00 UTC - Copied from TourEmails.Models.EmailFetchCount
    /// Predefined fetch counts for Gmail collection.
    /// </summary>
    public enum EmailFetchCount
    {
        ALL = 0,
        Five = 5,
        Ten = 10,
        Twenty = 20,
        Fifty = 50,
        SeventyFive = 75,
        OneHundred = 100,
        TwoHundred = 200,
        ThreeHundred = 300,
        FourHundred = 400,
        FiveHundred = 500,
        OneThousand = 1000,
        FifteenHundred = 1500,
        TwoThousand = 2000,
        FiveThousand = 5000
    }

    /// <summary>
    /// Created: 2025-11-16 00:00 UTC - Copied from TourEmails.Models.GmailSettings
    /// Gmail IMAP connection settings for email collection.
    /// </summary>
    public class GmailSettings
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string ImapServer { get; set; } = "imap.gmail.com";
        public int ImapPort { get; set; } = 993;
        public bool EnableSsl { get; set; } = true;
        public string SearchFilter { get; set; } = string.Empty;
        public int MaxEmailsToFetch { get; set; }
        public int InterBatchDelayMs { get; set; } = 150;
        public string? LocalTimeZoneId { get; set; }
        public EmailFetchCount FetchCount { get; set; } = EmailFetchCount.ALL;
    }

    /// <summary>
    /// Created: 2025-11-16 00:00 UTC - Copied from TourEmails.Models.GmailInboxEmail
    /// Represents a Gmail inbox email for collection and database storage.
    /// </summary>
    public class GmailInboxEmail
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
        public string? AttachmentNames { get; set; }
        public DateTime CollectedAt { get; set; }
        public string CollectionBatchId { get; set; } = string.Empty;
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool ManualParsingCompleted { get; set; }
        public string? ManualParsedVendorName { get; set; }
        public string? ManualParsedEmailType { get; set; }
        public string? ManualParsedContent { get; set; }
        public DateTime? ManualParsingTimestamp { get; set; }
        public bool VendorDetectionCompleted { get; set; }
        public bool IsTourBookingEmail { get; set; }
        public string VendorName { get; set; } = string.Empty;
        public string EmailType { get; set; } = string.Empty;
    }
}

