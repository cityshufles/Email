using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Email.Services.GmailProcessing.Models;

namespace Email.Services.GmailProcessing.Repositories
{
    /// <summary>
    /// Created: 2025-11-07 00:00 UTC
    /// Updated: 2025-11-26 00:00 UTC - Changed CancelAsync to return affected rows count
    /// Repository contract for bookings (create/update/deactivate/cancel and lookups).
    /// </summary>
    public interface IBookingRepository
    {
        Task<Booking?> FindByCodeAsync(string bookingCode, CancellationToken cancellationToken);
        Task<IReadOnlyList<Booking>> GetByCodesAsync(IReadOnlyList<string> bookingCodes, CancellationToken cancellationToken);
        Task<int> CreateAsync(Booking booking, CancellationToken cancellationToken);
        Task UpdateAsync(Booking booking, CancellationToken cancellationToken);
        Task DeactivateOriginalOnModificationAsync(string originalBookingCode, string newBookingCode, CancellationToken cancellationToken);
        /// <summary>
        /// Cancels a booking by booking code. Returns the number of rows affected.
        /// If 0 rows affected, the booking was not found.
        /// </summary>
        Task<int> CancelAsync(string bookingCode, string? cancellationReason, CancellationToken cancellationToken);
        Task<IReadOnlyList<Booking>> GetRecentAsync(int limit, CancellationToken cancellationToken);
        Task DeleteAsync(IReadOnlyList<int> bookingIds, CancellationToken cancellationToken);
    }
}


