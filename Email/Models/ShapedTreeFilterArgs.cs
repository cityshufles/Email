using System;
using Email.TourTreeViewShapedData.Enums;

namespace Email.Models
{
    /// <summary>
    /// Created: 2025-11-16 00:00 UTC - Copied from CityShufflesWorkStation.Models.ShapedTreeFilterArgs
    /// Arguments for requesting shaped tour tree data.
    /// </summary>
    public class ShapedTreeFilterArgs
    {
        public TreeDataFilterType FilterType { get; set; } = TreeDataFilterType.TodayOnly;
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? Vendor { get; set; }
    }
}

