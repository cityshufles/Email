using System.Collections.Generic;
using System.Threading.Tasks;
using Email.Models;

namespace Email.Services
{
    // Created: 2025-11-16 00:00 UTC - Local replacement for the prior API client
    public interface ITourEmailsService
    {
        Task<TourEmailsApiResponse<TourEmailsProcessingResult>> ProcessCustomSpanAsync(string collectionSpan, bool createReport = false, string? reportPath = null, bool processChronologically = false);
        Task<TourEmailsApiResponse<TourEmailsProcessingResult>> ProcessCollectedEmailsAsync();
        Task<TourEmailsApiResponse<TourEmailsProcessingResult>> ProcessSpecificEmailsAsync(List<int> emailIds, bool createReport = false, string? reportPath = null);
        Task<TourEmailsApiResponse<CollectionResult>> CollectEmailsOnlyAsync(string collectionSpan);

        Task<CollectionStatusDto> GetCollectionStatusAsync();
        Task<List<CollectionSummaryRowDto>> GetCollectionSummaryAsync(int limit = 10, string bookingFilter = "all");
        Task<List<UnprocessedEmailDto>> GetUnprocessedEmailsAsync(int limit = 50, int offset = 0, string? vendorFilter = null, string? searchTerm = null, bool onlyUnclassified = false, bool onlyTourRelated = true, bool onlyClassified = false);
        Task<LatestInboxEmailDto?> GetLatestInboxEmailAsync();
        Task<InboxEmailDetailDto?> GetInboxEmailByIdAsync(int id);
        Task<InboxEmailDetailDto?> GetInboxEmailByMessageIdAsync(string messageId);
        Task<ProcessedEmail?> GetProcessedEmailByMessageIdAsync(string messageId);
        // 2025-12-07 00:00 UTC - Mark booking as messaged (tree SMS/WA click)
        Task<bool> MarkBookingMessageSentAsync(string messageId, string bookingCode);
        // 2025-12-07 00:00 UTC - Explicit set/unset message sent flag (used by toggle UI)
        Task<bool> SetBookingMessageSentAsync(string messageId, string bookingCode, bool isSent);
        // 2026-03-13 - Per-booking per-channel 4-state contact outcome toggle (sms/wa)
        Task<ContactChannelStateResult?> CycleBookingContactChannelStateAsync(string messageId, string bookingCode, string channel, string? source = null, string? updatedBy = null);
        // 2026-03-14 - Explicit contact-state setter (used for binary platform checkbox behavior)
        Task<ContactChannelStateResult?> SetBookingContactChannelStateAsync(string messageId, string bookingCode, string channel, byte state, string? source = null, string? updatedBy = null);
        // 2026-03-13 - Quick-send path setter: force channel state to green and stamp legacy mirror fields in new table
        Task<ContactChannelStateResult?> MarkBookingContactChannelQuickSendAsync(string messageId, string bookingCode, string channel, string? source = null, string? updatedBy = null);
        // 2026-03-13 - Bulk read states keyed by (MessageId + BookingCode)
        Task<IReadOnlyList<ContactChannelStateResult>> GetBookingContactChannelStatesAsync(IEnumerable<ContactChannelStateKey> keys);
        // 2025-12-09 00:00 UTC - Per-stage message tracking for badges (welcome/tomorrow/dayOf/thankyou/promo/misc)
        Task<bool> SetBookingMessageStageAsync(string messageId, string bookingCode, string stage, bool isSent, string? channel, int? templateId, string? vendorName = null);
        Task<Dictionary<string, List<MessageStageStatusModel>>> GetBookingMessageStagesAsync(IEnumerable<string> messageIds);
        // 2025-12-09 00:00 UTC - Message history list (grouped by booking)
        Task<List<MessageHistoryItem>> GetMessageHistoryAsync(int skip = 0, int take = 300);

        Task<List<LiveUnifiedEmailListItem>> GetLiveInboxAsync(int limit = 50, long? olderThanUid = null);
        Task<LiveEmailDetail?> GetLiveEmailAsync(long uid);

        // Created: 2025-11-25 00:00 UTC - Processed grid with tour fields
        Task<List<ProcessedEmailDisplayTourEmailLibModel>> GetProcessedEmailsForGridAsync(int limit = 50, int offset = 0, string? searchTerm = null);
    }
}

