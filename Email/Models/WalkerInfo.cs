// Created: 2025-11-19 00:00 UTC - Copied verbatim from CityShufflesWorkStation.Models.WalkerInfo (namespace adjusted)
namespace Email.Models
{
    public class WalkerInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public int? Attendees { get; set; }
        public string TourName { get; set; } = string.Empty;
        public string TourDate { get; set; } = string.Empty;
        public string VendorName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string BookingCode { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
    }
}

