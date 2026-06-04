using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Email.Models.Reports;

namespace Email.Services
{
    public interface IBookingsInboxService
    {
        Task<IReadOnlyList<BookingsLiveListItem>> GetRecentBookingsAsync(
            int limit = 100,
            IEnumerable<string>? vendors = null,
            string orderBy = "booking_date",
            string? languageFilter = null,
            CancellationToken ct = default);

        Task<CustomerCommunicationProfile?> GetWalkerProfileAsync(
            int bookingId,
            CancellationToken ct = default);

        Task SetCustomerLabelAsync(
            int customerId,
            string labelKey,
            bool isActive,
            string? updatedBy = null,
            string? notes = null,
            CancellationToken ct = default);

        Task LogMessageEventAsync(
            BookingMessageEventWriteModel evt,
            CancellationToken ct = default);

        Task<Dictionary<int, GuestContactLabels>> GetCustomerLabelsAsync(
            IEnumerable<int> customerIds,
            CancellationToken ct = default);

        // 2026-06-03 - Guest page: aggregate profile by customer + notes
        Task<CustomerCommunicationProfile?> GetCustomerProfileAsync(
            int customerId,
            CancellationToken ct = default);

        Task<List<CustomerNote>> GetCustomerNotesAsync(
            int customerId,
            CancellationToken ct = default);

        Task<int> SaveCustomerNoteAsync(
            int customerId,
            string content,
            string noteType = "general",
            string? createdBy = null,
            CancellationToken ct = default);
    }
}
