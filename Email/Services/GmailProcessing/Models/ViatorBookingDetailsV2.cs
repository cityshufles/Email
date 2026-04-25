using System;

namespace Email.Services.GmailProcessing.Models
{
    /// <summary>
    /// Created: 2026-04-03 00:00 UTC
    /// Extended Viator booking-details model extracted from confirmation/cancellation email blocks.
    /// </summary>
    public sealed class ViatorBookingDetailsV2
    {
        public string BookingReference { get; set; } = string.Empty;
        public string TourName { get; set; } = string.Empty;
        public string TravelDateRaw { get; set; } = string.Empty;
        public DateTime? TravelDate { get; set; }
        public string LeadTravelerName { get; set; } = string.Empty;
        public string TravelerNames { get; set; } = string.Empty;
        public string TravelersRaw { get; set; } = string.Empty;
        public int? TravelersAdults { get; set; }
        public int? TravelersChildren { get; set; }
        public int? TravelersTotal { get; set; }
        public string ProductCode { get; set; } = string.Empty;
        public string TourGrade { get; set; } = string.Empty;
        public string TourGradeCode { get; set; } = string.Empty;
        public string TourGradeDescription { get; set; } = string.Empty;
        public string TourLanguage { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string NetRateRaw { get; set; } = string.Empty;
        public decimal? NetRateAmount { get; set; }
        public string NetRateCurrency { get; set; } = string.Empty;
        public string MeetingPoint { get; set; } = string.Empty;
        public string SpecialRequirements { get; set; } = string.Empty;
        public string PhoneRaw { get; set; } = string.Empty;
        public string PhoneNormalized { get; set; } = string.Empty;
        public string OptionalText { get; set; } = string.Empty;
    }
}
