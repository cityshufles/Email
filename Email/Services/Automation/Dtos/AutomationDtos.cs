using System;
using System.Collections.Generic;

namespace Email.Services.Automation.Dtos
{
    /// <summary>
    /// Created: 2025-11-25 16:12 UTC
    /// Request DTO for automation collect endpoint.
    /// Limits are optional - when null, no limit is applied (processes all).
    /// </summary>
    public sealed class AutomationCollectRequest
    {
        /// <summary>
        /// Window: day|week|month|year|all|daysPrior. Default: all
        /// </summary>
        public string Window { get; set; } = "all";

        /// <summary>
        /// Days prior (used when Window == "daysPrior" or "year" maps to 365)
        /// </summary>
        public int? DaysPrior { get; set; }

        /// <summary>
        /// Per-pass limit. When null, no limit applied.
        /// </summary>
        public int? Limit { get; set; }

        /// <summary>
        /// Batch size for IMAP fetch. Default: 150
        /// </summary>
        public int BatchSize { get; set; } = 150;

        /// <summary>
        /// When true, ignores high-watermark and re-collects all matching emails.
        /// </summary>
        public bool IgnoreWatermark { get; set; } = false;
    }

    /// <summary>
    /// Created: 2025-11-25 16:12 UTC
    /// Combined result for auto (collect + process) endpoint.
    /// </summary>
    public sealed class AutomationCombinedResult
    {
        public Email.Services.GmailCollection.Dtos.CollectionResultDto? Collect { get; set; }
        public Email.Services.GmailProcessing.Dtos.ProcessingResultDto? Process { get; set; }
    }

    /// <summary>
    /// Created: 2026-03-26 00:00 UTC
    /// Request DTO for on-demand Checkfront pull/reconciliation.
    /// </summary>
    public sealed class CheckfrontSyncRequest
    {
        /// <summary>
        /// Rolling lookback window for Checkfront booking pulls.
        /// </summary>
        public int DaysBack { get; set; } = 30;

        /// <summary>
        /// Page size per Checkfront booking list request.
        /// </summary>
        public int LimitPerPage { get; set; } = 50;

        /// <summary>
        /// Maximum number of Checkfront pages to scan in one run.
        /// </summary>
        public int MaxPages { get; set; } = 5;
    }

    /// <summary>
    /// Created: 2025-11-25 16:12 UTC
    /// Request DTO for auto-SMS endpoint.
    /// </summary>
    public sealed class AutoSmsRequest
    {
        /// <summary>
        /// Template ID from dbo.TourMessages
        /// </summary>
        public int TemplateId { get; set; }

        /// <summary>
        /// Optional: Only process bookings created/updated since this UTC datetime (ISO8601)
        /// </summary>
        public string? SinceUtc { get; set; }

        /// <summary>
        /// Optional: Limit number of bookings to process. When null, processes all.
        /// </summary>
        public int? Limit { get; set; }
    }

    /// <summary>
    /// Created: 2025-11-25 16:12 UTC
    /// Result summary for auto-SMS endpoint.
    /// </summary>
    public sealed class AutoSmsResult
    {
        public int Attempted { get; set; }
        public int SimulatedSent { get; set; }
        public int SkippedNoPhone { get; set; }
        public int SkippedNotConfirmation { get; set; }
        public List<AutoSmsItemResult> Results { get; set; } = new();
    }

    /// <summary>
    /// Created: 2025-11-25 16:12 UTC
    /// Individual SMS result item.
    /// </summary>
    public sealed class AutoSmsItemResult
    {
        public int BookingId { get; set; }
        public string? To { get; set; }
        public string? Body { get; set; }
        public string Status { get; set; } = "simulated";
        public string? SkipReason { get; set; }
    }

    /// <summary>
    /// Created: 2025-11-25 16:12 UTC
    /// Walker DTO for /automation/gmail/walkers endpoint.
    /// </summary>
    public sealed class WalkerDto
    {
        public int Id { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerPhone { get; set; }
        public string? TourName { get; set; }
        public DateTime? TourDate { get; set; }
        public string? DisplayDate { get; set; }
        public string? DisplayTime { get; set; }
        public string? VendorName { get; set; }
        public string? MessageId { get; set; }
        public bool IsConfirmation { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}

