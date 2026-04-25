// Created: 2026-01-09 - Standalone models for Guide Calculator feature
namespace Email.Models.GuideCalculator
{
    /// <summary>
    /// Calculation result for a guide on a single day.
    /// </summary>
    public class GuideCalculationResult
    {
        public int GuideId { get; set; }
        public string GuideName { get; set; } = "";
        public DateTime TourDate { get; set; }
        public List<TourCalculationDetail> Tours { get; set; } = new();
        
        /// <summary>Guide owes company (from FreeTour/GuruWalk adults)</summary>
        public decimal TotalOwed { get; set; }
        
        /// <summary>Company owes guide (from Viator/GYG)</summary>
        public decimal TotalEarned { get; set; }
    }

    /// <summary>
    /// Per-tour breakdown with guest roster.
    /// </summary>
    public class TourCalculationDetail
    {
        public string TourName { get; set; } = "";
        public string TourTime { get; set; } = "";
        public List<GuestLineItem> GuestRoster { get; set; } = new();
        public int TotalGuests { get; set; }
        
        /// <summary>Guide owes for this tour</summary>
        public decimal TourOwed { get; set; }
        
        /// <summary>Guide earns for this tour</summary>
        public decimal TourEarned { get; set; }
    }

    /// <summary>
    /// Individual guest in the roster with charge/earn amounts.
    /// </summary>
    public class GuestLineItem
    {
        /// <summary>1-based position in roster</summary>
        public int Position { get; set; }
        
        public string VendorName { get; set; } = "";
        
        /// <summary>Walker/booking name</summary>
        public string CustomerName { get; set; } = "";
        
        public bool IsChild { get; set; }
        
        /// <summary>True if FreeTour/GuruWalk adult (chargeable)</summary>
        public bool IsChargeable { get; set; }
        
        /// <summary>$0, $3, or $5 depending on position and chargeability</summary>
        public decimal ChargeAmount { get; set; }
        
        /// <summary>$10 for Viator/GYG guests, else $0</summary>
        public decimal EarnAmount { get; set; }
    }
}
