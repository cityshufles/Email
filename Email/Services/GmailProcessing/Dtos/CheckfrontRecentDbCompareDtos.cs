using System;
using System.Collections.Generic;

namespace Email.Services.GmailProcessing.Dtos
{
    /// <summary>
    /// Read-only options for recent Checkfront compare/audit runs.
    /// </summary>
    public sealed class CheckfrontRecentDbCompareOptions
    {
        public decimal HighAmountThresholdUsd { get; set; } = 500m;
        public int GlobalScanLimit { get; set; } = 5000;
        public bool IncludeGlobalHighAmountReport { get; set; } = true;
        public decimal CheckfrontMinorUnitMinRaw { get; set; } = 100m;
    }

    /// <summary>
    /// Read-only result summary for recent Checkfront API data compared with DB booking rows.
    /// </summary>
    public sealed class CheckfrontRecentDbCompareResult
    {
        public string OperationId { get; set; } = string.Empty;
        public DateTime TimestampUtc { get; set; }
        public int DaysBack { get; set; }
        public int LimitPerPage { get; set; }
        public int MaxPages { get; set; }
        public int PagesFetched { get; set; }
        public int CheckfrontCount { get; set; }
        public int DbCount { get; set; }
        public int MissingInDbCount => MissingInDb.Count;
        public int PresentInDbCount => PresentInDb.Count;
        public int FieldMismatchCount => FieldMismatches.Count;
        public string Outcome { get; set; } = "NoData";
        public string Message { get; set; } = string.Empty;
        public List<string> MissingInDb { get; set; } = new();
        public List<string> PresentInDb { get; set; } = new();
        public List<CheckfrontRecentDbFieldMismatch> FieldMismatches { get; set; } = new();
        public List<CheckfrontAmountCorrectionCandidate> CheckfrontAmountCandidates { get; set; } = new();
        public List<GlobalHighAmountCandidate> GlobalHighAmountCandidates { get; set; } = new();
        public int CheckfrontAmountCandidateCount => CheckfrontAmountCandidates.Count;
        public int GlobalHighAmountCandidateCount => GlobalHighAmountCandidates.Count;
    }

    /// <summary>
    /// Field-level mismatch between one Checkfront booking snapshot and one DB booking row.
    /// </summary>
    public sealed class CheckfrontRecentDbFieldMismatch
    {
        public string BookingCode { get; set; } = string.Empty;
        public string Field { get; set; } = string.Empty;
        public string ApiValue { get; set; } = string.Empty;
        public string DbValue { get; set; } = string.Empty;
    }

    /// <summary>
    /// Checkfront booking row that appears to need booking amount normalization/correction in DB.
    /// </summary>
    public sealed class CheckfrontAmountCorrectionCandidate
    {
        public string BookingCode { get; set; } = string.Empty;
        public decimal? RawTotal { get; set; }
        public decimal? NormalizedTotal { get; set; }
        public decimal? DbBookingAmount { get; set; }
        public string Reason { get; set; } = string.Empty;
        public decimal? SuggestedAmount { get; set; }
        public string Currency { get; set; } = "USD";
    }

    /// <summary>
    /// Read-only high-value booking candidate from the DB recent window.
    /// </summary>
    public sealed class GlobalHighAmountCandidate
    {
        public int BookingId { get; set; }
        public string BookingCode { get; set; } = string.Empty;
        public string VendorName { get; set; } = string.Empty;
        public decimal BookingAmount { get; set; }
        public string Currency { get; set; } = "USD";
        public DateTime UpdatedAtUtc { get; set; }
    }
}
