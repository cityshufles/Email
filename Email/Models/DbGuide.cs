using System;

namespace Email.Models
{
    /// <summary>
    /// Created: 2025-10-17 - API-aligned Guide model for Guides CRUD
    /// </summary>
    public class DbGuide
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string? LastName { get; set; }
        public string Phone { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? GuideImage { get; set; }
        public string? Description { get; set; }
        public string? DefaultTourName { get; set; }
        public int? DefaultTourId { get; set; }
        public bool IsTouring { get; set; } = true;
        public string? AvailabilityNotes { get; set; }
        public DateTime? AvailabilityUpdatedAt { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime? HireDate { get; set; }
        public DateTime? TerminationDate { get; set; }
        public string? Languages { get; set; }
        public string? Specialties { get; set; }
        public string? Notes { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}


