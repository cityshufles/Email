using Email.Models;

namespace Email.Services
{
    public interface IMessageStatusApiService
    {
        Task<List<MessageStatusDetail>> GetMessageStatusesAsync(CancellationToken ct = default);
        Task<List<GuideTourTreeNode>> GetGuideTourTreeAsync(DateTime? startDate = null, DateTime? endDate = null, CancellationToken ct = default);
    }
}

