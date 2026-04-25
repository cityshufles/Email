using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Email.Services
{
    /// <summary>
    /// Service contract for manager QA review over TourSetup staging batches.
    /// </summary>
    public interface ITourSetupQaService
    {
        // Legacy row-grid API (kept for compatibility with older UI)
        Task<List<TourSetupQaBatchSummary>> GetRecentBatchSummariesAsync(int maxBatches = 20, CancellationToken ct = default);

        Task<List<TourSetupQaRow>> GetBatchRowsAsync(Guid batchId, bool onlyNonOk = true, bool onlyPendingReview = false, CancellationToken ct = default);

        Task<bool> SaveReviewAsync(
            long stagingId,
            string? gapTypeFinal,
            string? meetingPlaceExpected,
            string? meetingTimeExpected,
            string? notes,
            string reviewedBy,
            bool markApplied,
            CancellationToken ct = default);

        Task<int> ApproveAllNonOkAsync(Guid batchId, string reviewedBy, CancellationToken ct = default);

        // Initial setup family-card API
        Task<TourSetupQaInitialSetupSummary> GetInitialSetupSummaryAsync(CancellationToken ct = default);

        Task<List<TourSetupQaFamilyCard>> GetFamilyCardsAsync(
            string? statusFilter = null,
            string? search = null,
            CancellationToken ct = default);

        Task<TourSetupQaFamilyDetail?> GetFamilyDetailAsync(string familyKey, CancellationToken ct = default);

        Task<bool> SaveFamilyDecisionAsync(
            TourSetupQaFamilyDecisionInput input,
            string reviewedBy,
            CancellationToken ct = default);

        Task<bool> ApproveFamilyAsync(string familyKey, string reviewedBy, CancellationToken ct = default);

        Task<TourSetupQaApplyPreview> GetApplyPreviewAsync(CancellationToken ct = default);
    }

    public sealed class TourSetupQaBatchSummary
    {
        public Guid BatchId { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public int TotalRows { get; set; }
        public int NonOkRows { get; set; }
        public int PendingReviewRows { get; set; }
        public int AppliedRows { get; set; }
    }

    public sealed class TourSetupQaRow
    {
        public long Id { get; set; }
        public Guid BatchId { get; set; }
        public string SourceType { get; set; } = string.Empty;
        public string? VendorName { get; set; }
        public string? VendorKey { get; set; }
        public string RawTourName { get; set; } = string.Empty;
        public string NormalizedTourKey { get; set; } = string.Empty;
        public int RecordCount { get; set; }
        public DateTime? TourDate { get; set; }
        public int? ProposedTourId { get; set; }
        public string? ProposedMasterTourName { get; set; }
        public string? MeetingPlaceExpected { get; set; }
        public string? MeetingTimeExpected { get; set; }
        public string? MeetingPlaceProposed { get; set; }
        public string? MeetingTimeProposed { get; set; }
        public string GapTypeComputed { get; set; } = string.Empty;
        public string? GapTypeFinal { get; set; }
        public string? Notes { get; set; }
        public string? ReviewedBy { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public DateTime? AppliedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public sealed class TourSetupQaInitialSetupSummary
    {
        public Guid ActiveRunId { get; set; }
        public int FamilyCount { get; set; }
        public int VariantCount { get; set; }
        public int NeedsDecisionCount { get; set; }
        public int ReadyCount { get; set; }
        public int ApprovedCount { get; set; }
        public DateTime? LastUpdatedAtUtc { get; set; }
    }

    public sealed class TourSetupQaFamilyCard
    {
        public string FamilyKey { get; set; } = string.Empty;
        public string FamilyLabel { get; set; } = string.Empty;
        public string ManagerStateLabel { get; set; } = "Needs Tour Decision";
        public string DecisionStatus { get; set; } = "Pending";
        public int VariantCount { get; set; }
        public int TotalSourceRows { get; set; }
        public int? SelectedTourId { get; set; }
        public string? SelectedMasterTourName { get; set; }
        public string? SelectedTourDuration { get; set; }
        public string? SelectedMeetingPlace { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
    }

    public sealed class TourSetupQaFamilyDetail
    {
        public string FamilyKey { get; set; } = string.Empty;
        public string FamilyLabel { get; set; } = string.Empty;
        public string ManagerStateLabel { get; set; } = "Needs Tour Decision";
        public string DecisionStatus { get; set; } = "Pending";
        public string DecisionType { get; set; } = "SameTour";
        public int? SelectedTourId { get; set; }
        public string? SelectedMasterTourName { get; set; }
        public string? SelectedMasterTourNameMobile { get; set; }
        public string? SelectedTourDuration { get; set; }
        public string? SelectedMeetingPlace { get; set; }
        public string? VendorLinkStatus { get; set; }
        public string? VendorScheduleLinkStatus { get; set; }
        public string? ReviewLinkStatus { get; set; }
        public string? SelectedVendorLink { get; set; }
        public string? SelectedVendorScheduleLink { get; set; }
        public string? SelectedReviewLink { get; set; }
        public string? Notes { get; set; }
        public List<TourSetupQaFamilyVariant> Variants { get; set; } = new();
        public List<TourSetupQaTourCandidate> Candidates { get; set; } = new();
        public List<TourSetupQaTourOption> TourOptions { get; set; } = new();
        public List<string> MasterOptions { get; set; } = new();
        public List<string> MobileOptions { get; set; } = new();
        public List<string> TourDurationOptions { get; set; } = new();
        public List<string> MeetingPlaceOptions { get; set; } = new();
        public List<TourSetupQaVendorLinkSnapshot> VendorLinkSnapshots { get; set; } = new();
    }

    public sealed class TourSetupQaFamilyVariant
    {
        public long StagingId { get; set; }
        public string SourceType { get; set; } = string.Empty;
        public string? VendorName { get; set; }
        public string RawTourName { get; set; } = string.Empty;
        public int RecordCount { get; set; }
        public bool IsAliasSelected { get; set; }
        public bool HasExactAliasMatch { get; set; }
        public string? GapTypeComputed { get; set; }
        public int? ProposedTourId { get; set; }
        public string? ProposedMasterTourName { get; set; }
        public string? ProposedMeetingPlace { get; set; }
        public string? ProposedMeetingTime { get; set; }
    }

    public sealed class TourSetupQaTourCandidate
    {
        public int TourId { get; set; }
        public string TourName { get; set; } = string.Empty;
        public string MasterTourName { get; set; } = string.Empty;
        public string? MasterTourNameDesktop { get; set; }
        public string? MasterTourNameMobile { get; set; }
        public string? Duration { get; set; }
        public string? MeetingPlace { get; set; }
        public string? VendorLink { get; set; }
        public string? VendorScheduleLink { get; set; }
        public string? ReviewLink { get; set; }
        public decimal Score { get; set; }
        public string MatchReason { get; set; } = string.Empty;
    }

    public sealed class TourSetupQaTourOption
    {
        public int TourId { get; set; }
        public string Label { get; set; } = string.Empty;
    }

    public sealed class TourSetupQaVendorLinkSnapshot
    {
        public int Id { get; set; }
        public string Vendor { get; set; } = string.Empty;
        public string TourName { get; set; } = string.Empty;
        public string? ProductId { get; set; }
        public string? TourLink { get; set; }
        public string? ReviewLink { get; set; }
        public int? TourDay { get; set; }
        public string? TourTime { get; set; }
    }

    public sealed class TourSetupQaFamilyDecisionInput
    {
        public string FamilyKey { get; set; } = string.Empty;
        public string DecisionType { get; set; } = "SameTour";
        public int? SelectedTourId { get; set; }
        public string? SelectedMasterTourName { get; set; }
        public string? SelectedMasterTourNameMobile { get; set; }
        public string? SelectedTourDuration { get; set; }
        public string? SelectedMeetingPlace { get; set; }
        public string? VendorLinkStatus { get; set; }
        public string? VendorScheduleLinkStatus { get; set; }
        public string? ReviewLinkStatus { get; set; }
        public string? SelectedVendorLink { get; set; }
        public string? SelectedVendorScheduleLink { get; set; }
        public string? SelectedReviewLink { get; set; }
        public string? Notes { get; set; }
        public List<TourSetupQaVariantSelection> VariantSelections { get; set; } = new();
    }

    public sealed class TourSetupQaVariantSelection
    {
        public long StagingId { get; set; }
        public bool IsAliasSelected { get; set; }
    }

    public sealed class TourSetupQaApplyPreview
    {
        public Guid ActiveRunId { get; set; }
        public int FamiliesApproved { get; set; }
        public int ProposedTourInserts { get; set; }
        public int ProposedTourUpdates { get; set; }
        public int ProposedMappingUpserts { get; set; }
        public List<TourSetupQaApplyAction> Actions { get; set; } = new();
    }

    public sealed class TourSetupQaApplyAction
    {
        public string ActionType { get; set; } = string.Empty;
        public string FamilyKey { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string SqlPreview { get; set; } = string.Empty;
    }
}
