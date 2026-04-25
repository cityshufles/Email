using System;

namespace Email.Calendar.Models
{
    /// <summary>
    /// Model for booking data in the Calendar module.
    /// Matches the Bookings table schema exactly.
    /// </summary>
    public class CalendarBookingModel
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public string CustomerIdentifier { get; set; } = string.Empty;
        public int ProcessedEmailId { get; set; }
        public string MessageId { get; set; } = string.Empty;
        public string BookingCode { get; set; } = string.Empty;
        public string VendorName { get; set; } = string.Empty;
        public string EmailType { get; set; } = string.Empty;
        public bool IsCancellation { get; set; }
        public bool IsModification { get; set; }
        public bool IsConfirmation { get; set; }
        public bool MessageSent { get; set; }
        public DateTime? MessageSentAtUtc { get; set; }
        public bool IsActive { get; set; } = true;
        public string? TourName { get; set; }
        public DateTime? TourDate { get; set; }
        public string? TourDayOfWeek { get; set; }
        public string? TourTime { get; set; }
        public string? TourLocation { get; set; }
        public string? TourTimeZone { get; set; } = "UTC";
        public string? DisplayDate { get; set; }
        public string? DisplayTime { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string? CustomerEmail { get; set; }
        public string? CustomerPhone { get; set; }
        public int? NumberOfAttendees { get; set; }
        public int? NumberOfAdults { get; set; }
        public int? NumberOfChildren { get; set; }
        public int NumberOfInfants { get; set; }
        public string? Language { get; set; }
        public string? CountryOfOrigin { get; set; }
        public string? BookingStatus { get; set; }
        public decimal? BookingAmount { get; set; }
        public string Currency { get; set; } = "USD";
        public string? SpecialRequests { get; set; }
        public string? GuideAssigned { get; set; }
        public string? CalendarEventId { get; set; }
        public bool IsCalendarExportSuccess { get; set; }
        public bool IsSheetExportSuccess { get; set; }
        public string? ProcessingNotes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
