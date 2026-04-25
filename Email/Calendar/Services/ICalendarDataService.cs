using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Email.Calendar.Models;

namespace Email.Calendar.Services
{
    public interface ICalendarDataService
    {
        Task<List<CalendarBookingModel>> GetBookingsAsync(DateTime startDate, DateTime endDate, string? guideName = null);
        Task<List<CalendarGuideModel>> GetGuidesAsync();
        Task<List<CalendarAppointmentModel>> GetAppointmentsAsync(DateTime startDate, DateTime endDate, string? guideName = null);
    }
}
