using System;
using System.Collections.Generic;
using System.Linq;

namespace Email.Models.Mobile
{
    // Created: 10/7/2025 7:30 PM (local) | 2025-10-07T19:30:00
    // Purpose: Mobile-optimized data models for tour cards and walker lists

    public class MobileVendorGroupModel
    {
        public string VendorName { get; set; } = string.Empty;
        public List<MobileWalkerInfoModel> Walkers { get; set; } = new List<MobileWalkerInfoModel>();

        public int TotalWalkers => Walkers?.Count ?? 0;
        // 2026-01-08 - TotalGuests sums attendees (each walker may have multiple guests)
        public int TotalGuests => Walkers?.Sum(w => Math.Max(1, w.Attendees)) ?? 0;
    }
}


