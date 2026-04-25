using System;

namespace Email.Services.GmailProcessing.Dtos
{
    /// <summary>
    /// Created: 2025-11-07 00:00 UTC
    /// Status summary for v2 processing dashboard.
    /// </summary>
    public sealed class GmailProcessingStatusDto
    {
        public bool Success { get; set; }
        public long TotalInboxRows { get; set; }
        public long TotalProcessedRows { get; set; }
        public long TotalUnprocessedRows { get; set; }
        public long ErrorCount { get; set; }
        public DateTime? LastProcessingCompletedAt { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}


