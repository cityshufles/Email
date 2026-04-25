using System;
using System.Collections.Generic;

namespace Email.Services.GmailProcessing.Dtos
{
    /// <summary>
    /// Created: 2025-11-07 00:00 UTC
    /// Result summary for a processing invocation.
    /// </summary>
    public sealed class ProcessingResultDto
    {
        public bool Success { get; set; }
        public int ProcessedCount { get; set; }
        public int ErrorCount { get; set; }
        public List<string> Errors { get; set; } = new();
        public DateTime StartedAt { get; set; }
        public DateTime CompletedAt { get; set; }
        public TimeSpan Elapsed => CompletedAt >= StartedAt ? CompletedAt - StartedAt : TimeSpan.Zero;
        public string Message { get; set; } = string.Empty;
    }
}


