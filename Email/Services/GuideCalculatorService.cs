// Created: 2026-01-09 - Guide Calculator service implementation
using Email.Calendar.Models;
using Email.Calendar.Services;
using Email.Models;
using Email.Models.GuideCalculator;
using Email.Models.Reports;

namespace Email.Services
{
    /// <summary>
    /// Calculates guide owed/earned amounts based on booking sources and tiered charging formula.
    /// 
    /// Charging Logic:
    /// - Guide earns $10/guest from Viator/GYG
    /// - Guide is charged for FreeTour/GuruWalk adults only
    /// - Children from any vendor: no charge
    /// - Website/Viator/GYG bookings: no charge
    /// 
    /// Tiered Charging (if total guests >= 5):
    /// - First 5 chargeable guests: $3/ea
    /// - Each guest after: $5/ea
    /// - If total guests < 5: no charge
    /// 
    /// Guest ordering for threshold:
    /// 1. Children (any vendor) - no charge
    /// 2. Website bookings - no charge
    /// 3. Viator bookings - no charge (but earn $10)
    /// 4. GYG bookings - no charge (but earn $10)
    /// 5. FreeTour adults - charged
    /// 6. GuruWalk adults - charged
    /// </summary>
    public sealed class GuideCalculatorService : IGuideCalculatorService
    {
        private readonly ITourGuideAssignmentService _assignmentService;
        private readonly ICalendarDataService _calendarService;
        private readonly IGuidesApiService _guidesService;

        // Vendor name patterns (case-insensitive matching)
        private static readonly string[] EarnVendors = { "viator", "getyourguide", "gyg" };
        private static readonly string[] ChargeVendors = { "freetour", "guruwalk", "guru" };
        private static readonly string[] FreeVendors = { "website", "direct" };

        public GuideCalculatorService(
            ITourGuideAssignmentService assignmentService,
            ICalendarDataService calendarService,
            IGuidesApiService guidesService)
        {
            _assignmentService = assignmentService;
            _calendarService = calendarService;
            _guidesService = guidesService;
        }

        public async Task<GuideCalculationResult> CalculateForGuideAsync(
            int guideId,
            DateTime tourDate,
            CancellationToken ct = default)
        {
            var result = new GuideCalculationResult
            {
                GuideId = guideId,
                GuideName = await ResolveGuideNameAsync(guideId),
                TourDate = tourDate
            };

            // Get assignments for this guide on this date
            var allAssignments = await _assignmentService.GetAssignmentsForDateAsync(tourDate, ct);
            var guideAssignments = allAssignments.Where(a => a.GuideId == guideId).ToList();

            if (guideAssignments.Count == 0)
                return result;

            // Get all appointments for this date
            var appointments = await _calendarService.GetAppointmentsAsync(tourDate, tourDate);

            foreach (var assignment in guideAssignments)
            {
                var tourDetail = CalculateTour(assignment, appointments, tourDate);
                result.Tours.Add(tourDetail);
                result.TotalOwed += tourDetail.TourOwed;
                result.TotalEarned += tourDetail.TourEarned;
            }

            return result;
        }

        // Modified: 2026-03-25 - Only checked-in walkers are included in calculation
        public async Task<GuideCalculationResult> CalculateFromReportAsync(TourReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));

            // Only count walkers who are actually checked in — excludes no-shows and
            // un-checked-in duplicate bookings so they don't inflate guest counts or fees.
            var checkedInWalkers = report.Walkers.Where(w => w.IsCheckedIn).ToList();

            Console.WriteLine($"[GuideCalculator] CalculateFromReport start. Tour='{report.TourName}', Time='{report.TourTime}', Date={report.TourDate:yyyy-MM-dd}, GuideId={report.GuideId}, GuideName='{report.GuideName}', TotalWalkers={report.Walkers.Count}, CheckedInWalkers={checkedInWalkers.Count}");
            foreach (var walker in report.Walkers)
            {
                Console.WriteLine($"[GuideCalculator] Walker: BookingId={walker.BookingId}, Name='{walker.CustomerName}', Vendor='{walker.VendorName}', Adults={walker.ActualAdults}, Children={walker.ActualChildren}, Attendees={walker.ActualAttendees}, IsCheckedIn={walker.IsCheckedIn}");
            }

            var result = new GuideCalculationResult
            {
                GuideId = report.GuideId ?? 0,
                GuideName = await ResolveGuideNameAsync(report.GuideId, report.GuideName),
                TourDate = report.TourDate
            };

            var normalizedGuests = new List<NormalizedGuest>();
            foreach (var walker in checkedInWalkers)
            {
                var adults = walker.ActualAdults;
                var children = walker.ActualChildren;

                if (adults == 0 && children == 0 && walker.ActualAttendees > 0)
                {
                    adults = walker.ActualAttendees;
                }

                for (int i = 0; i < children; i++)
                {
                    normalizedGuests.Add(new NormalizedGuest(walker.VendorName, walker.CustomerName, true));
                }

                for (int i = 0; i < adults; i++)
                {
                    normalizedGuests.Add(new NormalizedGuest(walker.VendorName, walker.CustomerName, false));
                }
            }

            Console.WriteLine($"[GuideCalculator] Normalized guests count (checked-in only): {normalizedGuests.Count}");

            var tourDetail = new TourCalculationDetail
            {
                TourName = report.TourName ?? "Unknown Tour",
                TourTime = report.TourTime ?? ""
            };

            var roster = CalculateGuestRoster(normalizedGuests);
            tourDetail.GuestRoster = roster;
            tourDetail.TotalGuests = roster.Count;
            tourDetail.TourOwed = roster.Sum(g => g.ChargeAmount);
            tourDetail.TourEarned = roster.Sum(g => g.EarnAmount);

            result.Tours.Add(tourDetail);
            result.TotalOwed = tourDetail.TourOwed;
            result.TotalEarned = tourDetail.TourEarned;

            Console.WriteLine($"[GuideCalculator] CalculateFromReport complete. TotalGuests={tourDetail.TotalGuests}, Earned={tourDetail.TourEarned:N2}, Owed={tourDetail.TourOwed:N2}");

            return result;
        }

        private TourCalculationDetail CalculateTour(
            TourGuideAssignment assignment,
            List<CalendarAppointmentModel> allAppointments,
            DateTime tourDate)
        {
            // Find appointments matching this tour
            var tourAppointments = allAppointments
                .Where(a => a.StartTime.Date == tourDate.Date &&
                            (a.TourName ?? "").Equals(assignment.TourName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Filter by time if specified
            if (!string.IsNullOrWhiteSpace(assignment.TourTime))
            {
                var timeFiltered = tourAppointments
                    .Where(a => a.StartTime.ToString("h:mm tt").Equals(assignment.TourTime, StringComparison.OrdinalIgnoreCase) ||
                                a.StartTime.ToString("HH:mm").Equals(assignment.TourTime, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (timeFiltered.Count > 0)
                    tourAppointments = timeFiltered;
            }

            var detail = new TourCalculationDetail
            {
                TourName = assignment.TourName ?? "Unknown Tour",
                TourTime = assignment.TourTime ?? ""
            };

            // Build normalized guest list from appointments
            var normalizedGuests = new List<NormalizedGuest>();
            foreach (var appt in tourAppointments)
            {
                var vendor = appt.VendorName;
                var customerName = appt.CustomerName;
                
                var attendees = Math.Max(0, appt.NumberOfAttendees ?? 0);
                var adults = Math.Max(0, appt.NumberOfAdults ?? 0);
                var children = Math.Max(0, appt.NumberOfChildren ?? 0);

                // If split is unknown but walkers are known, treat all walkers as adults for payout math.
                if (adults == 0 && children == 0 && attendees > 0)
                {
                    adults = attendees;
                    children = 0;
                }
                else if (appt.NumberOfAdults == null && appt.NumberOfChildren == null && attendees == 0)
                {
                    adults = 1;
                    children = 0;
                }

                for (int i = 0; i < children; i++)
                {
                    normalizedGuests.Add(new NormalizedGuest(vendor, customerName, true));
                }
                
                // Add adults
                for (int i = 0; i < adults; i++)
                {
                    normalizedGuests.Add(new NormalizedGuest(vendor, customerName, false));
                }
            }

            var roster = CalculateGuestRoster(normalizedGuests);

            detail.GuestRoster = roster;
            detail.TotalGuests = roster.Count;
            detail.TourOwed = roster.Sum(g => g.ChargeAmount);
            detail.TourEarned = roster.Sum(g => g.EarnAmount);

            return detail;
        }

        private List<GuestLineItem> CalculateGuestRoster(IEnumerable<NormalizedGuest> normalizedGuests)
        {
            var guests = new List<GuestLineItem>();

            foreach (var guest in normalizedGuests)
            {
                var vendor = string.IsNullOrWhiteSpace(guest.VendorName) ? "Direct" : guest.VendorName;
                var customerName = string.IsNullOrWhiteSpace(guest.CustomerName) ? "Unknown" : guest.CustomerName;
                var displayVendor = NormalizeVendorForDisplay(vendor);
                var vendorKey = NormalizeVendorForClassification(displayVendor);

                // Determine vendor classification
                var isEarnVendor = EarnVendors.Any(v => vendorKey.Contains(v, StringComparison.OrdinalIgnoreCase));
                var isChargeVendor = IsChargeableVendor(vendorKey);

                guests.Add(new GuestLineItem
                {
                    VendorName = displayVendor,
                    CustomerName = customerName,
                    IsChild = guest.IsChild,
                    IsChargeable = !guest.IsChild && isChargeVendor,
                    EarnAmount = isEarnVendor ? 10m : 0m
                });
            }

            // Sort guests: free categories first (children, website, viator, gyg), then chargeable (freetour, guruwalk)
            var sortedGuests = guests
                .OrderBy(g => GetVendorSortOrder(NormalizeVendorForClassification(g.VendorName), g.IsChild))
                .ToList();

            // Apply tiered charging
            ApplyTieredCharging(sortedGuests);

            // Assign positions
            for (int i = 0; i < sortedGuests.Count; i++)
            {
                sortedGuests[i].Position = i + 1;
            }

            return sortedGuests;
        }

        private async Task<string> ResolveGuideNameAsync(int? guideId, string? fallbackName = null)
        {
            if (!string.IsNullOrWhiteSpace(fallbackName))
            {
                return fallbackName;
            }

            if (!guideId.HasValue)
            {
                return "Unknown Guide";
            }

            var guides = await _guidesService.GetGuidesAsync();
            var guide = guides.FirstOrDefault(g => g.Id == guideId.Value);
            return guide != null ? $"{guide.FirstName} {guide.LastName}" : "Unknown Guide";
        }

        private static bool IsChargeableVendor(string vendor)
        {
            if (string.IsNullOrWhiteSpace(vendor)) return false;
            return ChargeVendors.Any(v => vendor.Contains(v, StringComparison.OrdinalIgnoreCase));
        }

        private static int GetVendorSortOrder(string vendor, bool isChild)
        {
            // Children first (0), then website (1), then viator/gyg (2), then freetour (3), then guruwalk (4)
            if (isChild) return 0;
            
            var vendorLower = NormalizeVendorForClassification(vendor);
            
            if (vendorLower.Contains("website") || vendorLower.Contains("direct")) return 1;
            if (vendorLower.Contains("viator")) return 2;
            if (vendorLower.Contains("getyourguide") || vendorLower.Contains("gyg")) return 3;
            if (vendorLower.Contains("freetour")) return 4;
            if (vendorLower.Contains("guruwalk") || vendorLower.Contains("guru")) return 5;
            
            return 6; // Unknown vendors go last
        }

        private static string NormalizeVendorForClassification(string vendor)
        {
            if (string.IsNullOrWhiteSpace(vendor)) return "direct";

            var vendorLower = vendor.Trim().ToLowerInvariant();
            if (vendorLower.Contains("walk-up") || vendorLower.Contains("walk up") || vendorLower.Contains("walkup"))
            {
                return "direct";
            }

            return vendorLower;
        }

        private static string NormalizeVendorForDisplay(string vendor)
        {
            if (string.IsNullOrWhiteSpace(vendor)) return "Direct";

            var vendorLower = vendor.Trim().ToLowerInvariant();
            if (vendorLower.Contains("walk-up") || vendorLower.Contains("walk up") || vendorLower.Contains("walkup"))
            {
                return "Direct";
            }

            return vendor.Trim();
        }

        private static void ApplyTieredCharging(List<GuestLineItem> sortedGuests)
        {
            // If total guests < 5, no charge
            if (sortedGuests.Count < 5)
            {
                foreach (var g in sortedGuests)
                    g.ChargeAmount = 0m;
                return;
            }

            // Apply tiered charging
            int chargeablePosition = 0;
            for (int i = 0; i < sortedGuests.Count; i++)
            {
                var guest = sortedGuests[i];
                
                if (!guest.IsChargeable)
                {
                    guest.ChargeAmount = 0m;
                    continue;
                }

                chargeablePosition++;
                
                // First 5 chargeable guests: $3 each
                // After that: $5 each
                // But remember: position i+1 determines threshold, not chargeablePosition
                // The 5-guest threshold is based on TOTAL guests, not chargeable guests
                
                // If we're at position 5 or earlier in the overall roster
                if (i < 5)
                {
                    guest.ChargeAmount = 3m;
                }
                else
                {
                    guest.ChargeAmount = 5m;
                }
            }
        }

        private sealed class NormalizedGuest
        {
            public NormalizedGuest(string? vendorName, string? customerName, bool isChild)
            {
                VendorName = vendorName;
                CustomerName = customerName;
                IsChild = isChild;
            }

            public string? VendorName { get; }
            public string? CustomerName { get; }
            public bool IsChild { get; }
        }
    }
}
