using System;
using System.ComponentModel.DataAnnotations;

namespace Email.Models
{
    /// <summary>
    /// Created: 2026-01-31 - Defines a schedule rule for a tour (e.g., "Winter Schedule").
    /// Decouples logistics from the global DbTour to allow seasonal variations.
    /// </summary>
    public class DbTourSchedule : ITourLogistics
    {
        public int Id { get; set; }
        
        // Link to the parent global tour
        public int TourId { get; set; }
        
        // 2026-02-03: Add TourName for display in consolidated lists
        public string? TourName { get; set; }
        
        public string ScheduleName { get; set; } = string.Empty; // e.g. "Winter 2026"

        // Date Range
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        
        // Logistics Overrides (If null, fallback to DbTour?) 
        // For now, we assume these are required for the schedule to be complete.
        public string? MeetingPlace { get; set; }
        
        public string? MeetingTime { get; set; } // "HH:mm" (24h format from NormalizeTime)
        
        public string? MeetingInstructions { get; set; }
        
        public string? VendorLink { get; set; }
        
        // Configuration state for the UI (JSON string or similar to store "Mon: GuideA, Tue: GuideB")
        // This allows us to reload the "Day Buttons" and "Guide Dropdowns" in the editing UI.
        public string? DayAssignmentsJson { get; set; }
        
        public string? TimeSlotsJson { get; set; } // "09:30,14:00"
        
        public bool IsActive { get; set; } = true;
        
        // 2026-02-05: Override flag for temporary schedule replacements
        public bool IsOverride { get; set; } = false;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
