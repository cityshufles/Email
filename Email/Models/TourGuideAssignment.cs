using System;

namespace Email.Models
{
    /// <summary>
    /// Created: 2026-01-08 - Per-tour-instance guide assignment
    /// </summary>
    public class TourGuideAssignment
    {
        public int Id { get; set; }
        public DateTime TourDate { get; set; }
        public string TourName { get; set; } = string.Empty;
        public string TourTime { get; set; } = string.Empty;
        public int? GuideId { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
