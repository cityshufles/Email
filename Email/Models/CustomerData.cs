using System;
using System.Collections.Generic;

namespace Email.Models
{
    /// <summary>
    /// Created: 2025-11-16 00:00 UTC - Copied from TourEmails.Models.CustomerData
    /// Model for customer data extracted from emails.
    /// </summary>
    public class CustomerData
    {
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerPhone { get; set; } = string.Empty;
        public string CustomerEmail { get; set; } = string.Empty;
        public string CustomerIdentifier { get; set; } = string.Empty;
        public string TourName { get; set; } = string.Empty;
        public DateTime? TourDate { get; set; }
        public string TourTime { get; set; } = string.Empty;
        public string TourLocation { get; set; } = string.Empty;
        public int NumberOfAttendees { get; set; }
        public int NumberOfAdults { get; set; }
        public int NumberOfChildren { get; set; }
        public string Language { get; set; } = string.Empty;
        public string BookingCode { get; set; } = string.Empty;
        public string? NewBookingCode { get; set; }
        public string? PreviousBookingCode { get; set; }
        public bool IsCancellation { get; set; }
        public bool IsModification { get; set; }
        public bool IsBooking { get; set; }
        public string? BookingAlterationNotes { get; set; }
        public List<string> AssociatedBookingIds { get; set; } = new();
        public string? ExtractedBookingCode { get; internal set; }
    }
}

