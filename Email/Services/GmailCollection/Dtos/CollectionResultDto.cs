using System;

namespace Email.Services.GmailCollection.Dtos
{
    /// <summary>
    /// Created: 2025-11-04 00:00 UTC
    /// Migrated: 2025-11-21 00:00 UTC - from CityShufflesGuides to Email (verbatim)
    /// Result summary for v2 Gmail collection.
    /// </summary>
    public sealed class CollectionResultDto
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int TotalCandidates { get; set; }
        public int InsertedCount { get; set; }
        public int UpdatedCount { get; set; }
        public int SkippedCount { get; set; }
        public int TotalProcessed { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime CompletedAt { get; set; }
        public TimeSpan Elapsed { get; set; }
        public long NewWatermarkUid { get; set; }
        public long UidValidity { get; set; }
    }
}


