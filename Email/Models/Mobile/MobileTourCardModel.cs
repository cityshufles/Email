using System;
using System.Collections.Generic;
using System.Linq;

namespace Email.Models.Mobile
{
    // Created: 10/7/2025 7:30 PM (local) | 2025-10-07T19:30:00
    // Purpose: Mobile-optimized data models for tour cards and walker lists

    public class MobileTourCardModel
    {
        public string DateLabel { get; set; } = string.Empty;
        public string TourName { get; set; } = string.Empty;
        // 2026-02-12 - Display-friendly name for mobile UI (may be abbreviated)
        public string? TourNameDisplay { get; set; }
        // 2026-02-07 - Added to support pre-assigned guides from TourTree (Manual or Schedule)
        public int? AssignedGuideId { get; set; }
        public List<MobileVendorGroupModel> Vendors { get; set; } = new List<MobileVendorGroupModel>();

        public int TotalWalkers => Vendors?.Sum(v => v.TotalWalkers) ?? 0;
        public int TotalGuests => Vendors?.Sum(v => v.Walkers?.Sum(w => Math.Max(0, w.Attendees)) ?? 0) ?? 0; // Sum attendees for GuruWalk totals

        public IEnumerable<MobileWalkerInfoModel> EnumerateAllWalkers()
        {
            if (Vendors == null) yield break;
            foreach (var v in Vendors)
            {
                if (v?.Walkers == null) continue;
                foreach (var w in v.Walkers) yield return w;
            }
        }
    }
}


