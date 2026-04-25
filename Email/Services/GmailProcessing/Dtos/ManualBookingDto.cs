using System;

namespace Email.Services.GmailProcessing.Dtos
{
    /// <summary>
    /// DTO for manual booking entry from the UI.
    /// </summary>
    public class ManualBookingDto
    {
        public string? CustomerName { get; set; }
        public string? CustomerEmail { get; set; }
        public string? CustomerPhone { get; set; }
        public string? TourName { get; set; }
        public DateTime? TourDate { get; set; }
        public string? TourTime { get; set; }
        public int NumberOfAdults { get; set; }
        public int NumberOfChildren { get; set; }
        public string? Language { get; set; }
        public string? CountryName { get; set; }
        public string? VendorName { get; set; } // "GuruWalk", "FreeTour", "Manual"
        public string? BookingCode { get; set; }
    }
}
