using System;
using System.Collections.Generic;

namespace Email.TourTreeViewShapedData.Models
{
    /// <summary>
    /// Created: 2025-11-16 00:00 UTC - Copied from TourEmails.TourTreeViewShapedData.Models.ShapedTreeData
    /// Root data structure for the shaped tree.
    /// </summary>
    public class ShapedTreeData
    {
        public List<ShapedTreeNode> TreeNodes { get; set; } = new List<ShapedTreeNode>();
        public int TotalBookings { get; set; }
        public int TotalWalkers { get; set; }
        public Dictionary<string, int> VendorCounts { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> TourCounts { get; set; } = new Dictionary<string, int>();
        public DateTime GeneratedAt { get; set; }
    }
}

