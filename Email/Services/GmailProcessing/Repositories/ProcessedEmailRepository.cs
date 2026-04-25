using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Email.Services.GmailProcessing.Dtos;
using Email.Services.GmailProcessing.Models;

namespace Email.Services.GmailProcessing.Repositories
{
    /// <summary>
    /// Created: 2025-11-07 00:00 UTC
    /// Repository contract for processed email persistence and retrieval.
    /// </summary>
    public interface IProcessedEmailRepository
    {
        Task<int> UpsertProcessedEmailAsync(ProcessedEmailRecord record, CancellationToken cancellationToken);
        Task SetLatestActionForThreadAsync(string vendorName, string? bookingCode, int processedEmailId, CancellationToken cancellationToken);
        Task<ProcessedEmailDisplayDto?> GetProcessedEmailByIdAsync(int processedId, CancellationToken cancellationToken);
        Task DeleteAsync(IReadOnlyList<int> processedIds, CancellationToken cancellationToken);
    }
}


