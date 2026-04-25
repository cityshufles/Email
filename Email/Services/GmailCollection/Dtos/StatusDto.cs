using System;

namespace Email.Services.GmailCollection.Dtos
{
    /// <summary>
    /// Created: 2025-11-04 00:00 UTC
    /// Migrated: 2025-11-21 00:00 UTC - from CityShufflesGuides to Email (verbatim)
    /// Status snapshot of inbox counts and watermark.
    /// </summary>
    public sealed class StatusDto
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public long TotalRows { get; set; }
        public DateTime? NewestReceivedDate { get; set; }
        public long UidValidity { get; set; }
        public long LastSeenUid { get; set; }
        public DateTime? LastScanStartedAt { get; set; }
        public DateTime? LastScanCompletedAt { get; set; }
    }
}


