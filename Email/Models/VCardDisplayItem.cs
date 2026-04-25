using System;

namespace Email.Models
{
    /// <summary>
    /// Created: 12/19/2025 5:12 PM (local) | 2025-12-19T17:12:00
    /// Purpose: Simple vCard data model for UI display/copy flows (manual export page).
    /// </summary>
    public sealed class VCardDisplayItem
    {
        public string DisplayName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string TourName { get; set; } = string.Empty;
        public string DateLabel { get; set; } = string.Empty;
        public string VCardContent { get; set; } = string.Empty;
        public string MessageId { get; set; } = string.Empty;
    }
}


