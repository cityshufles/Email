using System;
using System.Collections.Generic;

namespace Email.Models.Reports
{
    /// <summary>
    /// Created: 1/28/2026 5:05 PM
    /// Model for the overall tour report submitted by a guide.
    /// Maps to dbo.TourReports table.
    /// </summary>
    public class TourReport
    {
        public int Id { get; set; }
        public string PublicId { get; set; } = string.Empty;
        public DateTime TourDate { get; set; }
        public string TourName { get; set; } = string.Empty;
        public string TourTime { get; set; } = string.Empty;
        public int? GuideId { get; set; }
        public string GuideName { get; set; } = string.Empty; // Helper for UI display

        public string? GeneralNotes { get; set; }
        
        /// <summary>
        /// JSON or comma-separated list of image paths uploaded by the guide.
        /// </summary>
        public string? ImagePaths { get; set; }

        public bool IsSubmitted { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// List of walkers for this report. Not persisted in TourReports table directly,
        /// but populated from Bookings for the UI.
        /// </summary>
        public List<GuideReportWalker> Walkers { get; set; } = new();
    }
}
