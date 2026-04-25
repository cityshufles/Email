using System;

namespace Email.Models
{
    /// <summary>
    /// Created: 2025-11-16 00:00 UTC - Copied from TourEmails.Models.Customer
    /// Model for customer data in the database.
    /// </summary>
    public class Customer
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

