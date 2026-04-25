using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Email.Models;

namespace Email.Services
{
    /// <summary>
    /// Read-only Checkfront API contract used by the Checkfront test page.
    /// </summary>
    public interface ICheckfrontReadOnlyApi
    {
        string ActiveEndpoint { get; }

        Task<CheckfrontConnectionTest> TestConnectionAsync();
        Task<CheckfrontV4BookingsListResponse> ListV4BookingsAsync(int limit = 25, int offset = 0);
        Task<CheckfrontBooking?> GetBookingByCodeOrIdAsync(string codeOrId);
        Task<List<CheckfrontV4BookingNote>> GetBookingNotesByCodeOrIdAsync(string codeOrId);
        Task<List<CheckfrontBooking>> GetBookingsAsync(
            DateTime? startDate = null,
            DateTime? endDate = null,
            string? status = null,
            int? limit = null,
            int? page = null);
        Task<List<CheckfrontCustomerMatch>> SearchCustomersByEmailAsync(string emailAddress);
        Task<List<CheckfrontCustomerMatch>> SearchCustomersByNameAsync(string customerName);
        Task<List<CheckfrontBooking>> GetBookingsByCustomerIdAsync(string customerId, int limit = 25, int page = 1);
        Task<List<CheckfrontBooking>> GetBookingsByCustomerIdAsync(int customerId, int limit = 25, int page = 1);
    }
}
