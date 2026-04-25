using System;
using System.Collections.Generic;

namespace Email.Services.GmailProcessing.Dtos
{
    /// <summary>
    /// Created: 2026-04-04 00:00 UTC
    /// Per-booking canonical write outcome for Checkfront materialization.
    /// </summary>
    public enum CheckfrontCanonicalWriteAction
    {
        Skipped = 0,
        Created = 1,
        Updated = 2
    }

    /// <summary>
    /// Created: 2026-04-04 00:00 UTC
    /// Result for one canonical write attempt from a Checkfront booking source row.
    /// </summary>
    public sealed class CheckfrontSyncWriteResult
    {
        public bool Success { get; set; }
        public string BookingCode { get; set; } = string.Empty;
        public CheckfrontCanonicalWriteAction Action { get; set; } = CheckfrontCanonicalWriteAction.Skipped;
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Created: 2026-04-04 00:00 UTC
    /// Materialize-to-tree run summary for CheckfrontV4 snapshots.
    /// </summary>
    public sealed class CheckfrontV4MaterializationResultDto
    {
        public string RunId { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime CompletedAtUtc { get; set; }
        public bool Success { get; set; }
        public int SnapshotsRead { get; set; }
        public int MaterializedCount { get; set; }
        public int CreatedCount { get; set; }
        public int UpdatedCount { get; set; }
        public int SkippedCount { get; set; }
        public int ErrorCount { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<CheckfrontV4MaterializationIssueDto> Issues { get; set; } = new();
    }

    /// <summary>
    /// Created: 2026-04-04 00:00 UTC
    /// Individual mapping/materialization issue row shown in UI diagnostics.
    /// </summary>
    public sealed class CheckfrontV4MaterializationIssueDto
    {
        public string Stage { get; set; } = string.Empty;
        public string BookingCode { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }
}
