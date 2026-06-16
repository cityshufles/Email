using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Email.Models.Community;

namespace Email.Services.Community
{
    public interface ICommunityService
    {
        Task<List<CommunityPost>> GetPostsAsync(int limit = 100, CancellationToken ct = default);
        Task<int> CreatePostAsync(CommunityPostWriteModel post, bool isAdmin = false, CancellationToken ct = default);
        Task TogglePinAsync(int postId, bool isPinned, CancellationToken ct = default);
        Task ToggleHighlightAsync(int postId, bool isHighlighted, CancellationToken ct = default);
        Task DeletePostAsync(int postId, CancellationToken ct = default);
        Task SendAnnouncementEmailsAsync(int postId, bool isAdmin = false, CancellationToken ct = default);
    }
}
