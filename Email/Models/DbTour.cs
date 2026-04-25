using System;

namespace Email.Models
{
    /// <summary>
    /// Created: 2025-10-17 - API-aligned Tour model for Tours CRUD
    /// Modified: 2026-02-11 - Added master tour name fields
    /// </summary>
    public class DbTour
    {
        public int Id { get; set; }
        public string TourName { get; set; } = string.Empty;
        public string? TourNameAlias { get; set; }
        public string? VendorNames { get; set; }
        public string? MeetingPlace { get; set; }
        public string? TourStartTime { get; set; }
        public string? MeetingTime { get; set; }
        public string? MeetingInstructions { get; set; }
        public int? DefaultGuideId { get; set; }
        public string? Duration { get; set; }
        public int? MaxCapacity { get; set; }
        public bool IsActive { get; set; } = true;
        public string? VendorLink { get; set; }
        public string? VendorTourId { get; set; }
        public string? VendorScheduleLink { get; set; }
        public string? ReviewLink { get; set; }
        public string? Notes { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        
        // Master tour name fields
        public string MasterTourName { get; set; } = string.Empty;
        public string? MasterTourNameDesktop { get; set; }
        public string? MasterTourNameMobile { get; set; }
    }
}


