using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Email.Services
{
    /// <summary>
    /// Read-only diagnostics contract for tour tree coverage QA.
    /// </summary>
    public interface ITourTreeCoverageQaService
    {
        Task<TourTreeCoverageQaSnapshot> GetCoverageSnapshotAsync(TourTreeCoverageQaQuery? query = null, CancellationToken ct = default);
        Task<List<TourTreeCoverageQaRow>> GetCoverageRowsAsync(TourTreeCoverageQaQuery? query = null, CancellationToken ct = default);
        Task<TourTreeCoverageQaSummary> GetCoverageSummaryAsync(TourTreeCoverageQaQuery? query = null, CancellationToken ct = default);
        Task<List<TourTreeCoverageQaSourceBreakdownItem>> GetCoverageSourceBreakdownAsync(TourTreeCoverageQaQuery? query = null, CancellationToken ct = default);
    }

    public sealed class TourTreeCoverageQaQuery
    {
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public string? Vendor { get; set; }
        public string? SearchText { get; set; }

        public bool IncludeEmailsCurrentWindow { get; set; } = true;
        public bool IncludeEmailsHistory { get; set; } = true;
        public bool IncludeActiveMappings { get; set; } = true;
        public bool IncludeActiveTours { get; set; } = true;
        public bool IncludeQaInputTables { get; set; } = true;
        public bool IncludeManagerNotes { get; set; } = true;
    }

    public sealed class TourTreeCoverageQaSummary
    {
        public int TotalUniqueVariants { get; set; }
        public int MappedCount { get; set; }
        public int FallbackCount { get; set; }
        public int MissingActiveTourCount { get; set; }
        public int MappingBrokenCount { get; set; }
        public int NotCurrentInputCount { get; set; }
        public int NeedsAliasCount { get; set; }
        public int ReferenceOnlyCount { get; set; }
    }

    public sealed class TourTreeCoverageQaRow
    {
        public string RowKey { get; set; } = string.Empty;
        public string RawTourName { get; set; } = string.Empty;
        public string NormalizedKey { get; set; } = string.Empty;
        public List<string> Vendors { get; set; } = new();
        public List<string> SourcesPresent { get; set; } = new();

        public bool SourceEmailsCurrentWindow { get; set; }
        public bool SourceEmailsHistory { get; set; }
        public bool SourceActiveMappings { get; set; }
        public bool SourceActiveTours { get; set; }
        public bool SourceQaInputTables { get; set; }
        public bool SourceManagerNotes { get; set; }

        public int CurrentWindowCount { get; set; }
        public int EmailHistoryCount { get; set; }
        public int ActiveMappingCount { get; set; }
        public int ActiveToursCount { get; set; }
        public int QaInputCount { get; set; }
        public int ManagerNotesCount { get; set; }

        public bool IsInCurrentTreeInputWindow { get; set; }
        public bool HasActiveAliasMapping { get; set; }
        public bool HasAnyAliasMapping { get; set; }
        public bool ResolvedTourActive { get; set; }
        public bool HasBrokenActiveMapping { get; set; }
        public bool HasActiveTourNameMatch { get; set; }

        public int? ResolvedTourId { get; set; }
        public string? MasterTourName { get; set; }
        public int? SuggestedActiveTourId { get; set; }
        public string? SuggestedActiveTourName { get; set; }

        public string RenderMode { get; set; } = "NotCurrentInput";
        public string CoverageStatus { get; set; } = "Reference Only";
        public string Notes { get; set; } = string.Empty;
    }

    public sealed class TourTreeCoverageQaGroup
    {
        public string GroupType { get; set; } = string.Empty;
        public string GroupKey { get; set; } = string.Empty;
        public string GroupLabel { get; set; } = string.Empty;
        public int RowCount { get; set; }
        public int MappedCount { get; set; }
        public int FallbackCount { get; set; }
        public int NotCurrentInputCount { get; set; }
        public int MappingBrokenCount { get; set; }
        public int NeedsAliasCount { get; set; }
        public int NeedsActiveTourCount { get; set; }
        public int ReferenceOnlyCount { get; set; }
        public List<TourTreeCoverageQaGroupVariant> Variants { get; set; } = new();
    }

    public sealed class TourTreeCoverageQaGroupVariant
    {
        public string RowKey { get; set; } = string.Empty;
        public string RawTourName { get; set; } = string.Empty;
        public string NormalizedKey { get; set; } = string.Empty;
        public List<string> Vendors { get; set; } = new();
        public string RenderMode { get; set; } = string.Empty;
        public string CoverageStatus { get; set; } = string.Empty;
        public int? ResolvedTourId { get; set; }
        public string? MasterTourName { get; set; }
        public string Notes { get; set; } = string.Empty;
    }

    public sealed class TourTreeCoverageQaSourceBreakdownItem
    {
        public string SourceKey { get; set; } = string.Empty;
        public string SourceLabel { get; set; } = string.Empty;
        public bool Included { get; set; }
        public int VariantCount { get; set; }
        public int MappedCount { get; set; }
        public int FallbackCount { get; set; }
        public int NotCurrentInputCount { get; set; }
        public int MappingBrokenCount { get; set; }
        public int NeedsAliasCount { get; set; }
        public int NeedsActiveTourCount { get; set; }
    }

    public sealed class TourTreeCoverageQaSnapshot
    {
        public DateTime GeneratedAtUtc { get; set; }
        public TourTreeCoverageQaQuery EffectiveQuery { get; set; } = new();
        public TourTreeCoverageQaSummary Summary { get; set; } = new();
        public List<TourTreeCoverageQaRow> Rows { get; set; } = new();
        public List<TourTreeCoverageQaSourceBreakdownItem> SourceBreakdown { get; set; } = new();
        public List<TourTreeCoverageQaGroup> CanonicalGroups { get; set; } = new();
        public List<TourTreeCoverageQaGroup> NormalizedGroups { get; set; } = new();
    }
}
