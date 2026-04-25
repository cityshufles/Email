using System;

namespace Email.Services.GmailCollection.Models
{
    /// <summary>
    /// Created: 2025-11-04 00:00 UTC
    /// Migrated: 2025-11-21 00:00 UTC - from CityShufflesGuides to Email (verbatim)
    /// High-watermark state per mailbox for IMAP incremental collection.
    /// </summary>
    public sealed class GmailWatermarkRecord
    {
        public int Id { get; set; }
        public string Mailbox { get; set; } = "INBOX";
        public long UidValidity { get; set; }
        public long LastSeenUid { get; set; }
        public DateTime? LastScanStartedAt { get; set; }
        public DateTime? LastScanCompletedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}


