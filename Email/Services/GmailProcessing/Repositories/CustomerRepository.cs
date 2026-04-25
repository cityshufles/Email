using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Email.Services.GmailProcessing.Models;

namespace Email.Services.GmailProcessing.Repositories
{
    /// <summary>
    /// Created: 2025-11-07 00:00 UTC
    /// Updated: 2025-11-26 00:00 UTC - Added GetByIdAsync for modification/cancellation flows
    /// Repository contract for customers (lookups, creation, fuzzy support).
    /// </summary>
    public interface ICustomerRepository
    {
        /// <summary>
        /// Get customer by primary key ID
        /// Added: 2025-11-26 00:00 UTC
        /// </summary>
        Task<Customer?> GetByIdAsync(int customerId, CancellationToken cancellationToken);
        Task<Customer?> GetByBookingCodeAsync(string bookingCode, CancellationToken cancellationToken);
        Task<Customer?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);
        Task<Customer?> GetByPhoneAsync(string normalizedPhone, CancellationToken cancellationToken);
        Task<int> CreateAsync(Customer customer, CancellationToken cancellationToken);
        Task UpdateAsync(Customer customer, CancellationToken cancellationToken);

        // Fuzzy matching helpers (Fix D)
        Task<IReadOnlyList<Customer>> FindCandidatesByNameAndPhoneAsync(string customerName, string? phoneLast7, CancellationToken cancellationToken);
        Task<IReadOnlyList<Customer>> FindCandidatesByEmailSimilarityAsync(string emailUser, string emailDomain, CancellationToken cancellationToken);
        Task<IReadOnlyList<Customer>> GetRecentAsync(int limit, CancellationToken cancellationToken);
        Task DeleteAsync(IReadOnlyList<int> customerIds, CancellationToken cancellationToken);
    }
}


