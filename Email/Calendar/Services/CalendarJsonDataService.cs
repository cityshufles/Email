using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Email.Calendar.Models;
using Microsoft.AspNetCore.Hosting;

namespace Email.Calendar.Services
{
    public class CalendarJsonDataService : ICalendarDataService
    {
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly string _jsonFilePath;

        public CalendarJsonDataService(IWebHostEnvironment webHostEnvironment)
        {
            _webHostEnvironment = webHostEnvironment;
            _jsonFilePath = Path.Combine(_webHostEnvironment.WebRootPath, "data", "calendar-demo-data.json");
        }

        private class JsonDataWrapper
        {
            public List<CalendarBookingModel> bookings { get; set; } = new();
            public List<CalendarGuideModel> guides { get; set; } = new();
        }

        private async Task<JsonDataWrapper> LoadDataAsync()
        {
            if (!File.Exists(_jsonFilePath))
            {
                // Return empty data if file doesn't exist
                return new JsonDataWrapper();
            }

            var json = await File.ReadAllTextAsync(_jsonFilePath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            return JsonSerializer.Deserialize<JsonDataWrapper>(json, options) ?? new JsonDataWrapper();
        }

        public async Task<List<CalendarBookingModel>> GetBookingsAsync(DateTime startDate, DateTime endDate, string? guideName = null)
        {
            var data = await LoadDataAsync();
            var bookings = data.bookings.Where(b => 
                b.TourDate.HasValue &&
                b.TourDate.Value >= startDate &&
                b.TourDate.Value <= endDate
            );

            if (!string.IsNullOrEmpty(guideName))
            {
                bookings = bookings.Where(b => 
                    !string.IsNullOrEmpty(b.GuideAssigned) && 
                    b.GuideAssigned.Equals(guideName, StringComparison.OrdinalIgnoreCase)
                );
            }

            return bookings.ToList();
        }

        public async Task<List<CalendarGuideModel>> GetGuidesAsync()
        {
            var data = await LoadDataAsync();
            return data.guides.AsReadOnly().ToList();
        }

        public async Task<List<CalendarAppointmentModel>> GetAppointmentsAsync(DateTime startDate, DateTime endDate, string? guideName = null)
        {
            var bookings = await GetBookingsAsync(startDate, endDate, guideName);
            var appointments = new List<CalendarAppointmentModel>();

            foreach (var booking in bookings)
            {
                if (booking.TourDate.HasValue)
                {
                    var startTime = booking.TourDate.Value;
                    var endTime = startTime.AddHours(2); // Default duration if not specified, can be improved

                    // Parse duration if available? 
                    // For now, assuming fixed duration or just using startTime + 2h as placeholder logic
                    // Schema has 'Duration' on Tours table but not explicitly on Booking table except maybe inferred.
                    // Guide says "Tours table Fields... Duration". Booking has 'TourName'.
                    // For this demo, 2 hours is a safe default.

                    appointments.Add(new CalendarAppointmentModel
                    {
                        Id = booking.Id,
                        Subject = booking.TourName ?? "Unnamed Tour",
                        StartTime = startTime,
                        EndTime = endTime,
                        Location = booking.TourLocation,
                        Description = $"Guide: {(booking.GuideAssigned ?? "Unassigned")}\nCustomer: {booking.CustomerName}\n{booking.TourName} ({booking.VendorName}) - {booking.NumberOfAttendees} pax",
                        IsAllDay = false,
                        BookingId = booking.Id,
                        GuideName = booking.GuideAssigned,
                        CustomerName = booking.CustomerName,
                        NumberOfAttendees = booking.NumberOfAttendees,
                        TourName = booking.TourName
                    });
                }
            }

            return appointments;
        }
    }
}
