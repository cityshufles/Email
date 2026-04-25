using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Email.Models;

namespace Email.Services
{
    /// <summary>
    /// Phase 1 scaffold contract for the direct-production tour workbench.
    /// </summary>
    public interface ITourProductionWorkbenchService
    {
        Task<TourProductionSourceCoverageResult> GetSourceCoverageAsync(TourProductionSourceCoverageQuery? query = null, CancellationToken ct = default);
        Task<List<TourProductionGroupCard>> GetGroupCardsAsync(TourProductionGroupQuery? query = null, CancellationToken ct = default);
        Task<TourProductionGroupDetail?> GetGroupDetailAsync(string groupKey, CancellationToken ct = default);
        Task<List<TourProductionMeetingPlaceItem>> GetMeetingPlaceLibraryAsync(string? search = null, CancellationToken ct = default);
        Task<List<TourProductionScheduleTimeItem>> GetScheduleTimesAsync(int tourId, CancellationToken ct = default);
        Task<List<DbTour>> GetToursAsync(bool includeInactive = false, string? search = null, CancellationToken ct = default);
        Task<DbTour?> CreateTourAsync(TourProductionTourUpsertInput input, CancellationToken ct = default);
        Task<bool> UpdateTourAsync(int tourId, TourProductionTourUpsertInput input, CancellationToken ct = default);
        Task<bool> DeactivateTourAsync(int tourId, CancellationToken ct = default);
        Task<List<TourNameMapping>> GetAliasesAsync(int tourId, string? vendorName = null, bool includeInactive = false, CancellationToken ct = default);
        Task<int> CreateAliasAsync(TourProductionAliasCreateInput input, CancellationToken ct = default);
        Task<bool> UpdateAliasAsync(int aliasId, TourProductionAliasUpdateInput input, CancellationToken ct = default);
        Task<bool> ReassignAliasAsync(int aliasId, int targetTourId, CancellationToken ct = default);
        Task<bool> SetAliasActiveAsync(int aliasId, bool isActive, CancellationToken ct = default);
        Task<int> ApplyGroupOverrideToGroupAsync(string groupKey, string overrideGroupKey, CancellationToken ct = default);
        Task<bool> ApplyGroupOverrideToVariantAsync(TourProductionVariantOverrideInput input, string overrideGroupKey, CancellationToken ct = default);
        Task<int> ClearGroupOverridesForGroupAsync(string groupKey, CancellationToken ct = default);
        Task<bool> ClearGroupOverrideForVariantAsync(TourProductionVariantOverrideInput input, CancellationToken ct = default);
    }

    public sealed class TourProductionSourceCoverageQuery
    {
        public string? Search { get; set; }
        public string? VendorName { get; set; }
        public string? MasterTourName { get; set; }
    }

    public sealed class TourProductionGroupQuery
    {
        public string? Search { get; set; }
        public string? StatusFilter { get; set; }
        public string? VendorName { get; set; }
    }

    public sealed class TourProductionSourceCoverageResult
    {
        public int TotalRows { get; set; }
        public int MappedRows { get; set; }
        public int NeedsTourRows { get; set; }
        public int NeedsMeetingPlaceRows { get; set; }
        public int NeedsAliasRows { get; set; }
        public List<TourProductionSourceCoverageItem> Items { get; set; } = new();
    }

    public sealed class TourProductionSourceCoverageItem
    {
        public string SourceType { get; set; } = string.Empty;
        public string? VendorName { get; set; }
        public string RawTourName { get; set; } = string.Empty;
        public int RecordCount { get; set; }
        public int? TourId { get; set; }
        public string? MasterTourName { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? SuggestedMeetingPlace { get; set; }
        public string? SuggestedDuration { get; set; }
        public string? SuggestedTimes { get; set; }
    }

    public sealed class TourProductionGroupCard
    {
        public string GroupKey { get; set; } = string.Empty;
        public string GroupLabel { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int VariantCount { get; set; }
        public int TotalSourceRows { get; set; }
        public int? TourId { get; set; }
        public string? MasterTourName { get; set; }
        public string? MeetingPlace { get; set; }
        public string? ManualGroupKey { get; set; }
    }

    public sealed class TourProductionGroupDetail
    {
        public string GroupKey { get; set; } = string.Empty;
        public string GroupLabel { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int? TourId { get; set; }
        public string? MasterTourName { get; set; }
        public string? ManualGroupKey { get; set; }
        public List<TourProductionGroupVariant> Variants { get; set; } = new();
        public List<TourProductionCandidateTour> Candidates { get; set; } = new();
        public List<TourProductionScheduleTimeItem> ScheduleTimes { get; set; } = new();
    }

    public sealed class TourProductionGroupVariant
    {
        public string SourceType { get; set; } = string.Empty;
        public string? VendorName { get; set; }
        public string RawTourName { get; set; } = string.Empty;
        public int RecordCount { get; set; }
        public bool IsMapped { get; set; }
        public int? TourId { get; set; }
        public string VariantKey { get; set; } = string.Empty;
        public string? ManualGroupKey { get; set; }
    }

    public sealed class TourProductionCandidateTour
    {
        public int TourId { get; set; }
        public string TourName { get; set; } = string.Empty;
        public string MasterTourName { get; set; } = string.Empty;
        public string? MeetingPlace { get; set; }
        public string? Duration { get; set; }
        public decimal Score { get; set; }
    }

    public sealed class TourProductionMeetingPlaceItem
    {
        public string MeetingPlace { get; set; } = string.Empty;
        public string SourceType { get; set; } = string.Empty;
        public int UsageCount { get; set; }
    }

    public sealed class TourProductionScheduleTimeItem
    {
        public int? TourScheduleId { get; set; }
        public string TourTime { get; set; } = string.Empty;
        public string? MeetingTime { get; set; }
        public string? MeetingPlace { get; set; }
    }

    public sealed class TourProductionTourUpsertInput
    {
        public string TourName { get; set; } = string.Empty;
        public string MasterTourName { get; set; } = string.Empty;
        public string? MasterTourNameDesktop { get; set; }
        public string? MasterTourNameMobile { get; set; }
        public string? Duration { get; set; }

        // dbo.Tours.MeetingPlace is NOT NULL in production schema.
        public string MeetingPlace { get; set; } = string.Empty;

        public string? VendorLink { get; set; }
        public string? VendorScheduleLink { get; set; }
        public string? ReviewLink { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public sealed class TourProductionAliasCreateInput
    {
        public int TourId { get; set; }
        public string IncomingTourName { get; set; } = string.Empty;
        public string? VendorName { get; set; }
    }

    public sealed class TourProductionAliasUpdateInput
    {
        public string IncomingTourName { get; set; } = string.Empty;
        public string? VendorName { get; set; }
    }

    public sealed class TourProductionVariantOverrideInput
    {
        public string SourceType { get; set; } = string.Empty;
        public string? VendorName { get; set; }
        public string RawTourName { get; set; } = string.Empty;
    }
}
