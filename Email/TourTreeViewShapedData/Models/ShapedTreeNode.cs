using System.Collections.Generic;

namespace Email.TourTreeViewShapedData.Models
{
    /// <summary>
    /// Created: 2025-11-16 00:00 UTC - Copied from TourEmails.TourTreeViewShapedData.Models.ShapedTreeNode
    /// Node for the shaped tree structure (Date → Tour → Vendor → Walker).
    /// </summary>
    public class ShapedTreeNode
    {
        public string Label { get; set; } = string.Empty;
        public string? Icon { get; set; }
        public string NodeType { get; set; } = string.Empty;
        public bool IsExpanded { get; set; } = true;
        public int? DailyGuestCount { get; set; }
        // 2026-02-12 - Optional mobile-friendly label (e.g., abbreviated master tour name + time)
        public string? MobileLabel { get; set; }
        // Edited: 2026-01-31 - Added support for guide assignments from DB
        public int? AssignedGuideId { get; set; }
        public string? AssignedGuideName { get; set; }
        public ShapedWalkerData? WalkerData { get; set; }
        public List<ShapedTreeNode> Children { get; set; } = new List<ShapedTreeNode>();
    }
}

