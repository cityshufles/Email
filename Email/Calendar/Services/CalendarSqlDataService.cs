using System.Data;
using Dapper;
using Email.Calendar.Models;
using Email.Services;

namespace Email.Calendar.Services
{
    /// <summary>
    /// 2025-12-21 - Enhanced to resolve "Effective Guide" using the same hierarchy as TourTreeDisplay:
    /// 1. Bookings.GuideAssigned (explicit assignment)
    /// 2. TourGuideDefaults (matching TourId + DayOfWeek + StartTime)
    /// 3. Tours.DefaultGuideId (general default)
    /// </summary>
    public class CalendarSqlDataService : ICalendarDataService
    {
        private readonly SqlConnectionFactory _connectionFactory;
        private readonly ITourCatalogService _catalogService;

        public CalendarSqlDataService(SqlConnectionFactory connectionFactory, ITourCatalogService catalogService)
        {
            _connectionFactory = connectionFactory;
            _catalogService = catalogService;
        }

        public async Task<List<CalendarBookingModel>> GetBookingsAsync(DateTime startDate, DateTime endDate, string? guideName = null)
        {
            using var connection = _connectionFactory.CreateOpenConnection();
            
            // Always fetch all bookings for the date range - filtering happens after guide resolution
            var sql = @"
                SELECT 
                    Id, 
                    CustomerId,
                    CustomerIdentifier,
                    MessageId,
                    BookingCode,
                    TourName, 
                    TourDate, 
                    TourTime, 
                    TourLocation,
                    TourTimeZone,
                    CustomerName,
                    CustomerEmail,
                    CustomerPhone,
                    NumberOfAttendees,
                    NumberOfAdults,
                    NumberOfChildren,
                    VendorName,
                    GuideAssigned
                FROM Bookings
                WHERE TourDate >= @StartDate AND TourDate <= @EndDate
                AND IsActive = 1
                AND IsCancellation = 0
                ORDER BY TourDate";

            var bookings = await connection.QueryAsync<CalendarBookingModel>(sql, new { StartDate = startDate, EndDate = endDate });
            return bookings.ToList();
        }

        public async Task<List<CalendarGuideModel>> GetGuidesAsync()
        {
            using var connection = _connectionFactory.CreateOpenConnection();
            var sql = "SELECT Id, FirstName, LastName, IsActive FROM Guides WHERE IsActive = 1 ORDER BY FirstName";
            var guides = await connection.QueryAsync<CalendarGuideModel>(sql);
            return guides.ToList();
        }

        public async Task<List<CalendarAppointmentModel>> GetAppointmentsAsync(DateTime startDate, DateTime endDate, string? guideName = null)
        {
            // Fetch all data needed for resolution
            var bookings = await GetBookingsAsync(startDate, endDate, null); // No SQL filter - we filter after resolution
            var guides = await GetGuidesAsync();
            var catalog = await _catalogService.BuildCatalogAsync();

            // Build guide Id-to-FullName lookup
            var guideNameById = guides.ToDictionary(
                g => g.Id,
                g => string.IsNullOrWhiteSpace(g.LastName) ? g.FirstName : $"{g.FirstName} {g.LastName}"
            );

            var appointments = new List<CalendarAppointmentModel>();

            foreach (var b in bookings)
            {
                if (!b.TourDate.HasValue) continue;

                // Resolve effective guide using the hierarchy
                var effectiveGuide = ResolveEffectiveGuide(b, catalog, guideNameById);

                // Filter by guide name if specified
                if (!string.IsNullOrEmpty(guideName))
                {
                    if (string.Equals(guideName, "Unassigned", StringComparison.OrdinalIgnoreCase))
                    {
                        // Show only bookings with no effective guide
                        if (!string.Equals(effectiveGuide, "Unassigned", StringComparison.OrdinalIgnoreCase))
                            continue;
                    }
                    else
                    {
                        // Show only bookings matching the selected guide
                        if (!string.Equals(effectiveGuide, guideName, StringComparison.OrdinalIgnoreCase))
                            continue;
                    }
                }

                var startTime = b.TourDate.Value;
                
                // Try to parse the time from TourTime string (expected format HH:mm or similar)
                if (!string.IsNullOrEmpty(b.TourTime) && TimeSpan.TryParse(b.TourTime, out var timeSpan))
                {
                     // Reset time component of date to midnight then add parsed time
                     startTime = startTime.Date.Add(timeSpan);
                }

                appointments.Add(new CalendarAppointmentModel
                {
                    Id = b.Id,
                    Subject = b.TourName ?? "Unnamed Tour",
                    StartTime = startTime,
                    EndTime = startTime.AddHours(2), // Default duration
                    Location = b.TourLocation,
                    Description = $"Guide: {effectiveGuide}\nCustomer: {b.CustomerName}\n{bookingDetails(b)}",
                    IsAllDay = false,
                    BookingId = b.Id,
                    MessageId = b.MessageId,
                    BookingCode = b.BookingCode,
                    CustomerId = b.CustomerId,
                    CustomerIdentifier = b.CustomerIdentifier,
                    GuideName = effectiveGuide, // Use effective guide, not just GuideAssigned
                    CustomerName = b.CustomerName,
                    NumberOfAttendees = b.NumberOfAttendees,
                    NumberOfAdults = b.NumberOfAdults,
                    NumberOfChildren = b.NumberOfChildren,
                    TourName = b.TourName,
                    CustomerPhone = b.CustomerPhone,
                    VendorName = b.VendorName
                });
            }

            return appointments;
        }

        /// <summary>
        /// Resolves the effective guide for a booking using the hierarchy:
        /// 1. Bookings.GuideAssigned (explicit)
        /// 2. TourGuideDefaults (TourId + DayOfWeek + StartTime match)
        /// 3. Tours.DefaultGuideId (general default)
        /// </summary>
        private string ResolveEffectiveGuide(
            CalendarBookingModel booking,
            TourCatalogSnapshot catalog,
            Dictionary<int, string> guideNameById)
        {
            // Priority 1: Explicit assignment
            if (!string.IsNullOrWhiteSpace(booking.GuideAssigned))
            {
                return booking.GuideAssigned;
            }

            // Priority 2 & 3: Lookup via catalog
            if (!string.IsNullOrWhiteSpace(booking.TourName) && booking.TourDate.HasValue)
            {
                var canonicalName = _catalogService.NormalizeName(booking.TourName);
                var entry = catalog.Entries.FirstOrDefault(e =>
                    string.Equals(e.CanonicalName, canonicalName, StringComparison.OrdinalIgnoreCase) ||
                    e.Aliases.Any(a => string.Equals(_catalogService.NormalizeName(a), canonicalName, StringComparison.OrdinalIgnoreCase)));

                if (entry?.TourId != null)
                {
                    // Priority 2: TourGuideDefaults - match by TourId + DayOfWeek + StartTime
                    if (catalog.GuideDefaultsByTourId.TryGetValue(entry.TourId.Value, out var defaults) && defaults.Count > 0)
                    {
                        var dayOfWeek = (int)booking.TourDate.Value.DayOfWeek;
                        var normalizedTime = _catalogService.NormalizeTime(booking.TourTime ?? string.Empty);

                        // Try exact match (DayOfWeek + StartTime)
                        var match = defaults.FirstOrDefault(d =>
                            d.DayOfWeek == dayOfWeek &&
                            string.Equals(_catalogService.NormalizeTime(d.StartTime), normalizedTime, StringComparison.OrdinalIgnoreCase));

                        // Fallback: match by DayOfWeek only
                        match ??= defaults.FirstOrDefault(d => d.DayOfWeek == dayOfWeek);

                        if (match != null && guideNameById.TryGetValue(match.GuideId, out var guideName))
                        {
                            return guideName;
                        }
                    }

                    // Priority 3: Tours.DefaultGuideId
                    if (entry.DefaultGuideId.HasValue && guideNameById.TryGetValue(entry.DefaultGuideId.Value, out var defaultGuideName))
                    {
                        return defaultGuideName;
                    }
                }
            }

            return "Unassigned";
        }

        private string bookingDetails(CalendarBookingModel b) => 
            $"{b.TourName} ({b.VendorName}) - {b.NumberOfAttendees} walkers";
    }
}
