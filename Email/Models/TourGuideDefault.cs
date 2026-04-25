using System;

namespace Email.Models
{
    /// <summary>
    /// 2025-10-22 - Default guide schedule row keyed by Tour + DayOfWeek + StartTime
    /// </summary>
    public class TourGuideDefault
    {
        public int Id { get; set; }
        public int TourId { get; set; }
        public int DayOfWeek { get; set; } // 0=Sunday..6=Saturday
        public string StartTime { get; set; } = string.Empty; // e.g., "1:50 PM"
        public int GuideId { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}


