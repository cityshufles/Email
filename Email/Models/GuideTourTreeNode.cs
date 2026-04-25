using System;
using System.Collections.Generic;

namespace Email.Models
{
    /// <summary>
    /// Created: 2025-12-09 - Node for Guide/Tour tree grid
    /// Hierarchy: Date -> Guide -> Tour -> Walker
    /// </summary>
    public class GuideTourTreeNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string? ParentId { get; set; }
        
        // Display properties
        public string Name { get; set; } = string.Empty; // Date / Guide Name / Tour Name / Walker Name
        public string? Detail { get; set; } // Phone / Stats
        public int? Attendees { get; set; }
        public string? Phone { get; set; }
        public string? Time { get; set; }
        public string NodeType { get; set; } = "Walker"; // Date, Guide, Tour, Walker
        
        // Sorting helpers
        public DateTime? DateValue { get; set; }
        public string? SortString { get; set; }

        public List<GuideTourTreeNode> Children { get; set; } = new();
    }
}

