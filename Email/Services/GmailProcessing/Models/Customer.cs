using System;

namespace Email.Services.GmailProcessing.Models
{
    /// <summary>
    /// Created: 2025-11-07 00:00 UTC
    /// Customer entity (mirrors TourEmails.Models.Customer).
    /// </summary>
    public sealed class Customer
    {
        public int Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Email { get; set; }
        public string CustomerIdentifier { get; set; } = string.Empty;
        public string? BookingIds { get; set; }
        public int TotalBookings { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}


