using System;

namespace Email.Calendar.Models
{
    /// <summary>
    /// Model for mapping booking data to Syncfusion Scheduler appointments.
    /// </summary>
    public class CalendarAppointmentModel
    {
        public int Id { get; set; }
        public string Subject { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        
        public string? Description { get; set; }
        public string? Location { get; set; }
        public bool IsAllDay { get; set; }
        public string? RecurrenceRule { get; set; }
        public string? RecurrenceException { get; set; }
        public int? RecurrenceID { get; set; }
        public string? CssClass { get; set; }
        
        // Additional properties for application logic
        public int BookingId { get; set; }
        public string? MessageId { get; set; }
        public string? BookingCode { get; set; }
        public int CustomerId { get; set; }
        public string? CustomerIdentifier { get; set; }
        public string? GuideName { get; set; }
        public string? CustomerName { get; set; }
        public int? NumberOfAttendees { get; set; }
        // 2026-01-09 - Added for Guide Calculator adult/child breakdown
        public int? NumberOfAdults { get; set; }
        public int? NumberOfChildren { get; set; }
        public string? TourName { get; set; }
        // 2026-01-08 - Added for Guide Calendar phone display
        public string? CustomerPhone { get; set; }
        // 2026-01-08 - Added for GuideView vendor grouping
        public string? VendorName { get; set; }
    }
}
