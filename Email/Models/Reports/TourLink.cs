using System;

namespace Email.Models.Reports
{
    /// <summary>
    /// Created: 2/10/2026
    /// Model for vendor-specific tour links.
    /// Maps to dbo.TourLinks table.
    /// </summary>
    public class TourLink
    {
        public int Id { get; set; }
        public string Vendor { get; set; } = string.Empty;
        public string TourName { get; set; } = string.Empty;
        public int? TourId { get; set; }
        public string? DaysTimesAvailable { get; set; }
        public int? TourDay { get; set; }
        public string? TourTime { get; set; }
        public string? ProductId { get; set; }
        public string? ReviewLink { get; set; }
        public string? TourLinkUrl { get; set; } // Renamed to avoid confusion with class name
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
