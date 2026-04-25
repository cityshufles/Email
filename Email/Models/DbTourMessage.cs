using System;

namespace Email.Models
{
    /// <summary>
    /// Created: 2025-10-18 09:30 - API-aligned model for TourMessages CRUD
    /// </summary>
    public class DbTourMessage
    {
        public int Id { get; set; }
        public string MessageName { get; set; } = string.Empty;
        public string MessageType { get; set; } = string.Empty; // welcome|reminder|custom|confirmation
        public string TourName { get; set; } = string.Empty;
        public int? TourId { get; set; }
        public string? TourStartTime { get; set; }
        public int? GuideId { get; set; }
        public int? MeetingPlaceId { get; set; }
        public string? MeetingPlace { get; set; }
        public string? MeetingTime { get; set; }
        public string MessageContent { get; set; } = string.Empty;
        public string? Signature { get; set; }
        public string? Description { get; set; }
        public string? VendorLink { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string MeetingInstructions { get; internal set; }
    }
}


