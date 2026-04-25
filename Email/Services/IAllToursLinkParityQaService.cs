using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Email.Services
{
    /// <summary>
    /// Read-only diagnostics contract for verifying AllToursLink parity
    /// across vendors, tours, and message template rendering paths.
    /// </summary>
    public interface IAllToursLinkParityQaService
    {
        Task<AllToursLinkParityQaSnapshot> GetSnapshotAsync(AllToursLinkParityQaQuery? query = null, CancellationToken ct = default);
    }

    public sealed class AllToursLinkParityQaQuery
    {
        public DateTime? DateFrom { get; set; } = DateTime.Today;
        public DateTime? DateTo { get; set; } = DateTime.Today.AddDays(2);
        public string? Vendor { get; set; }
        public int MaxVendors { get; set; } = 12;
        public int MaxWalkersPerVendor { get; set; } = 2;
        public int MaxTemplates { get; set; } = 4;
    }

    public sealed class AllToursLinkParityQaSummary
    {
        public int TotalRows { get; set; }
        public int RowsWithDbValue { get; set; }
        public int PassCount { get; set; }
        public int MismatchCount { get; set; }
        public int GapCount { get; set; }
        public int UniqueVendorsTested { get; set; }
        public int UniqueTemplatesTested { get; set; }
        public int UniqueWalkersSampled { get; set; }
        public int MissingAllToursLinkVendorCount { get; set; }
    }

    public sealed class AllToursLinkParityQaRow
    {
        public string VendorName { get; set; } = string.Empty;
        public string VendorKey { get; set; } = string.Empty;
        public int? VendorId { get; set; }

        public string WalkerName { get; set; } = string.Empty;
        public string BookingCode { get; set; } = string.Empty;
        public string MessageId { get; set; } = string.Empty;
        public string TourName { get; set; } = string.Empty;

        public int TemplateId { get; set; }
        public string TemplateName { get; set; } = string.Empty;
        public string TemplateType { get; set; } = string.Empty;
        public bool TemplateContainsAllToursLinkToken { get; set; }
        public bool TemplateContainsTempSignatureToken { get; set; }

        public string DbAllToursLink { get; set; } = string.Empty;
        public string MessagesAllToursOutput { get; set; } = string.Empty;
        public string ToursAllToursOutput { get; set; } = string.Empty;
        public string MobileAllToursOutput { get; set; } = string.Empty;

        public bool HasDbValue { get; set; }
        public bool IsCrossSurfaceMatch { get; set; }
        public bool MatchesDbValue { get; set; }
        public string Status { get; set; } = string.Empty; // Pass | Mismatch | Gap
        public string Notes { get; set; } = string.Empty;
    }

    public sealed class AllToursLinkParityQaGapRow
    {
        public string VendorName { get; set; } = string.Empty;
        public string VendorKey { get; set; } = string.Empty;
        public int? VendorId { get; set; }
        public string CurrentAllToursLink { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty; // ActiveVendorTours | SampledWalkers
        public int ActiveVendorTourCount { get; set; }
        public int SampledWalkerCount { get; set; }
        public string MasterTours { get; set; } = string.Empty;
    }

    public sealed class AllToursLinkParityQaSnapshot
    {
        public DateTime GeneratedAtUtc { get; set; }
        public AllToursLinkParityQaQuery EffectiveQuery { get; set; } = new();
        public AllToursLinkParityQaSummary Summary { get; set; } = new();
        public List<AllToursLinkParityQaRow> Rows { get; set; } = new();
        public List<AllToursLinkParityQaGapRow> Gaps { get; set; } = new();
    }
}
