using Email.Calendar.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Email.Calendar.Services
{
    public interface IGoogleCalendarService
    {
        Task<List<GoogleAppointmentModel>> GetEventsAsync(DateTime start, DateTime end);
        Task<GoogleAppointmentModel?> GetEventAsync(string eventId);
        Task<string?> InsertEventAsync(GoogleAppointmentModel eventData);
        Task UpdateEventAsync(GoogleAppointmentModel eventData);
        Task RemoveEventAsync(string id);
    }
}
