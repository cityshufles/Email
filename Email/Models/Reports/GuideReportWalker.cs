using System;

namespace Email.Models.Reports
{
    /// <summary>
    /// Created: 1/28/2026 5:05 PM
    /// Edited: 1/28/2026 9:30 PM - Added IsCheckedIn, DoNotContact for report UI
    /// Edited: 1/29/2026 10:30 AM - Added ActualAdults, ActualChildren for A.C format
    /// Model for an individual walker/party within a guide report.
    /// Maps primarily to columns in dbo.Bookings.
    /// </summary>
    public class GuideReportWalker
    {
        public int BookingId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? VendorName { get; set; }
        public string MessageId { get; set; } = string.Empty;
        public string BookingCode { get; set; } = string.Empty;
        public byte SmsContactState { get; set; }
        public byte WaContactState { get; set; }
        
        // Original Booking Data
        public int BookedAttendees { get; set; }
        public int BookedAdults { get; set; }
        public int BookedChildren { get; set; }
        
        // Report Data (Mutable) - Legacy single value
        public int ActualAttendees { get; set; }
        
        // Report Data (Mutable) - Adults/Children split
        public int ActualAdults { get; set; }
        public int ActualChildren { get; set; }
        
        /// <summary>
        /// Display format: "A.C" (e.g., "3.2" = 3 adults, 2 children)
        /// </summary>
        public string AttendeeDisplay => ActualChildren > 0 
            ? $"{ActualAdults}.{ActualChildren}" 
            : ActualAdults.ToString();
        
        /// <summary>
        /// Whether the walker checked in (showed up)
        /// </summary>
        public bool IsCheckedIn { get; set; }
        
        /// <summary>
        /// Do not contact this walker
        /// </summary>
        public bool DoNotContact { get; set; }
        
        /// <summary>
        /// 'Positive', 'Negative', 'NoContact'
        /// </summary>
        public string? ReviewStatus { get; set; }
        
        public string? ReviewNotes { get; set; }

        // Helpers for UI logic
        public string TourName { get; set; } = string.Empty;
        public DateTime? TourDate { get; set; }
        public string? TourTime { get; set; }
        
        // UI State
        public bool IsExpanded { get; set; } = false;
    }
}

