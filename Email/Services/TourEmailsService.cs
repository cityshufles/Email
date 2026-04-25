using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Email.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Email.Services
{
    // Created: 2025-11-16 00:00 UTC - SQL Server-backed implementation of the former API client
    public class TourEmailsService : ITourEmailsService
    {
        private readonly string _connectionString;
        private readonly GmailSettings _gmailSettings;
        private readonly ILogger<TourEmailsService> _logger;
        private static readonly HashSet<string> AllowedStages = new(StringComparer.OrdinalIgnoreCase) { "welcome", "tomorrow", "dayOf", "thankyou", "promo", "misc" };
        private static readonly HashSet<string> AllowedChannels = new(StringComparer.OrdinalIgnoreCase) { "", "sms", "wa" };
        private static readonly HashSet<string> AllowedContactOutcomeChannels = new(StringComparer.OrdinalIgnoreCase) { "sms", "wa", "platform" };

        public TourEmailsService(
            IConfiguration configuration,
            IOptions<GmailSettings> gmailOptions,
            ILogger<TourEmailsService> logger)
        {
            _connectionString = configuration.GetConnectionString("AutomaticGmailSqlServer")
                ?? throw new InvalidOperationException("ConnectionStrings:AutomaticGmailSqlServer is not configured.");
            _gmailSettings = gmailOptions.Value;
            _logger = logger;
        }

        private IDbConnection CreateConnection() => new SqlConnection(_connectionString);

        public Task<TourEmailsApiResponse<TourEmailsProcessingResult>> ProcessCustomSpanAsync(string collectionSpan, bool createReport = false, string? reportPath = null, bool processChronologically = false)
        {
            throw new NotImplementedException("Email collection pipeline migration pending.");
        }

        public Task<TourEmailsApiResponse<TourEmailsProcessingResult>> ProcessCollectedEmailsAsync()
        {
            throw new NotImplementedException("Email collection pipeline migration pending.");
        }

        public Task<TourEmailsApiResponse<TourEmailsProcessingResult>> ProcessSpecificEmailsAsync(List<int> emailIds, bool createReport = false, string? reportPath = null)
        {
            throw new NotImplementedException("Email collection pipeline migration pending.");
        }

        public Task<TourEmailsApiResponse<CollectionResult>> CollectEmailsOnlyAsync(string collectionSpan)
        {
            throw new NotImplementedException("Email collection pipeline migration pending.");
        }

        // 2025-12-07 00:00 UTC - Mark booking as messaged from tree SMS/WhatsApp click
        public async Task<bool> MarkBookingMessageSentAsync(string messageId, string bookingCode)
        {
            if (string.IsNullOrWhiteSpace(messageId) || string.IsNullOrWhiteSpace(bookingCode)) return false;

            const string sql = @"
UPDATE b
SET MessageSent = 1,
    MessageSentAtUtc = SYSUTCDATETIME(),
    UpdatedAt = SYSUTCDATETIME()
FROM dbo.Bookings b
WHERE b.MessageId = @MessageId AND b.BookingCode = @BookingCode;";

            using var conn = CreateConnection();
            var rows = await conn.ExecuteAsync(sql, new { MessageId = messageId, BookingCode = bookingCode });
            return rows > 0;
        }

        // 2025-12-07 00:00 UTC - Explicit set/unset message sent flag (used by toggle UI)
        public async Task<bool> SetBookingMessageSentAsync(string messageId, string bookingCode, bool isSent)
        {
            if (string.IsNullOrWhiteSpace(messageId) || string.IsNullOrWhiteSpace(bookingCode)) return false;

            const string sql = @"
UPDATE b
SET MessageSent = @IsSent,
    MessageSentAtUtc = CASE WHEN @IsSent = 1 THEN SYSUTCDATETIME() ELSE NULL END,
    UpdatedAt = SYSUTCDATETIME()
FROM dbo.Bookings b
WHERE b.MessageId = @MessageId AND b.BookingCode = @BookingCode;";

            using var conn = CreateConnection();
            var rows = await conn.ExecuteAsync(sql, new { MessageId = messageId, BookingCode = bookingCode, IsSent = isSent });
            return rows > 0;
        }

        // 2026-03-13 - Manual 4-state cycle for booking/channel contact outcome.
        public async Task<ContactChannelStateResult?> CycleBookingContactChannelStateAsync(string messageId, string bookingCode, string channel, string? source = null, string? updatedBy = null)
        {
            if (string.IsNullOrWhiteSpace(messageId) || string.IsNullOrWhiteSpace(bookingCode))
            {
                return null;
            }

            var normalizedChannel = NormalizeContactOutcomeChannel(channel);
            if (normalizedChannel == null)
            {
                return null;
            }

            const string sql = @"
DECLARE @BookingId INT =
(
    SELECT TOP 1 b.Id
    FROM dbo.Bookings b
    WHERE b.MessageId = @MessageId AND b.BookingCode = @BookingCode
    ORDER BY b.UpdatedAt DESC, b.Id DESC
);

DECLARE @Result TABLE
(
    MessageId NVARCHAR(255),
    BookingCode NVARCHAR(100),
    Channel NVARCHAR(20),
    ContactState TINYINT,
    LegacyMessageSent BIT,
    LegacyMessageSentAtUtc DATETIME2(7),
    UpdatedAtUtc DATETIME2(7),
    UpdatedBy NVARCHAR(100),
    UpdatedSource NVARCHAR(30)
);

UPDATE target
SET ContactState = CASE WHEN target.ContactState >= 3 THEN 0 ELSE target.ContactState + 1 END,
    BookingId = COALESCE(@BookingId, target.BookingId),
    UpdatedBy = @UpdatedBy,
    UpdatedSource = @UpdatedSource,
    UpdatedAtUtc = SYSUTCDATETIME()
OUTPUT inserted.MessageId,
       inserted.BookingCode,
       inserted.Channel,
       inserted.ContactState,
       inserted.LegacyMessageSent,
       inserted.LegacyMessageSentAtUtc,
       inserted.UpdatedAtUtc,
       inserted.UpdatedBy,
       inserted.UpdatedSource
INTO @Result
FROM dbo.BookingContactChannelState AS target WITH (UPDLOCK, HOLDLOCK)
WHERE target.MessageId = @MessageId
  AND target.BookingCode = @BookingCode
  AND target.Channel = @Channel;

IF @@ROWCOUNT = 0
BEGIN
    INSERT INTO dbo.BookingContactChannelState
    (
        MessageId,
        BookingCode,
        BookingId,
        Channel,
        ContactState,
        LegacyMessageSent,
        LegacyMessageSentAtUtc,
        UpdatedBy,
        UpdatedSource,
        CreatedAtUtc,
        UpdatedAtUtc
    )
    OUTPUT inserted.MessageId,
           inserted.BookingCode,
           inserted.Channel,
           inserted.ContactState,
           inserted.LegacyMessageSent,
           inserted.LegacyMessageSentAtUtc,
           inserted.UpdatedAtUtc,
           inserted.UpdatedBy,
           inserted.UpdatedSource
    INTO @Result
    VALUES
    (
        @MessageId,
        @BookingCode,
        @BookingId,
        @Channel,
        1,
        0,
        NULL,
        @UpdatedBy,
        @UpdatedSource,
        SYSUTCDATETIME(),
        SYSUTCDATETIME()
    );
END

SELECT TOP 1
    MessageId,
    BookingCode,
    Channel,
    ContactState,
    LegacyMessageSent,
    LegacyMessageSentAtUtc,
    UpdatedAtUtc,
    UpdatedBy,
    UpdatedSource
FROM @Result;";

            using var conn = CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<ContactChannelStateResult>(sql, new
            {
                MessageId = messageId.Trim(),
                BookingCode = bookingCode.Trim(),
                Channel = normalizedChannel,
                UpdatedBy = string.IsNullOrWhiteSpace(updatedBy) ? null : updatedBy.Trim(),
                UpdatedSource = string.IsNullOrWhiteSpace(source) ? null : source.Trim()
            });
        }

        // 2026-03-14 - Explicit contact state setter (supports binary platform checkbox behavior).
        public async Task<ContactChannelStateResult?> SetBookingContactChannelStateAsync(string messageId, string bookingCode, string channel, byte state, string? source = null, string? updatedBy = null)
        {
            if (string.IsNullOrWhiteSpace(messageId) || string.IsNullOrWhiteSpace(bookingCode))
            {
                return null;
            }

            var normalizedChannel = NormalizeContactOutcomeChannel(channel);
            if (normalizedChannel == null)
            {
                return null;
            }

            if (state > 3)
            {
                return null;
            }

            // Platform is intentionally binary: 0 = unchecked, 1 = checked.
            if (string.Equals(normalizedChannel, "platform", StringComparison.OrdinalIgnoreCase) && state > 1)
            {
                return null;
            }

            const string sql = @"
DECLARE @BookingId INT =
(
    SELECT TOP 1 b.Id
    FROM dbo.Bookings b
    WHERE b.MessageId = @MessageId AND b.BookingCode = @BookingCode
    ORDER BY b.UpdatedAt DESC, b.Id DESC
);

DECLARE @NowUtc DATETIME2(7) = SYSUTCDATETIME();

DECLARE @Result TABLE
(
    MessageId NVARCHAR(255),
    BookingCode NVARCHAR(100),
    Channel NVARCHAR(20),
    ContactState TINYINT,
    LegacyMessageSent BIT,
    LegacyMessageSentAtUtc DATETIME2(7),
    UpdatedAtUtc DATETIME2(7),
    UpdatedBy NVARCHAR(100),
    UpdatedSource NVARCHAR(30)
);

UPDATE target
SET ContactState = @ContactState,
    BookingId = COALESCE(@BookingId, target.BookingId),
    UpdatedBy = @UpdatedBy,
    UpdatedSource = @UpdatedSource,
    UpdatedAtUtc = @NowUtc
OUTPUT inserted.MessageId,
       inserted.BookingCode,
       inserted.Channel,
       inserted.ContactState,
       inserted.LegacyMessageSent,
       inserted.LegacyMessageSentAtUtc,
       inserted.UpdatedAtUtc,
       inserted.UpdatedBy,
       inserted.UpdatedSource
INTO @Result
FROM dbo.BookingContactChannelState AS target WITH (UPDLOCK, HOLDLOCK)
WHERE target.MessageId = @MessageId
  AND target.BookingCode = @BookingCode
  AND target.Channel = @Channel;

IF @@ROWCOUNT = 0
BEGIN
    INSERT INTO dbo.BookingContactChannelState
    (
        MessageId,
        BookingCode,
        BookingId,
        Channel,
        ContactState,
        LegacyMessageSent,
        LegacyMessageSentAtUtc,
        UpdatedBy,
        UpdatedSource,
        CreatedAtUtc,
        UpdatedAtUtc
    )
    OUTPUT inserted.MessageId,
           inserted.BookingCode,
           inserted.Channel,
           inserted.ContactState,
           inserted.LegacyMessageSent,
           inserted.LegacyMessageSentAtUtc,
           inserted.UpdatedAtUtc,
           inserted.UpdatedBy,
           inserted.UpdatedSource
    INTO @Result
    VALUES
    (
        @MessageId,
        @BookingCode,
        @BookingId,
        @Channel,
        @ContactState,
        0,
        NULL,
        @UpdatedBy,
        @UpdatedSource,
        @NowUtc,
        @NowUtc
    );
END

SELECT TOP 1
    MessageId,
    BookingCode,
    Channel,
    ContactState,
    LegacyMessageSent,
    LegacyMessageSentAtUtc,
    UpdatedAtUtc,
    UpdatedBy,
    UpdatedSource
FROM @Result;";

            using var conn = CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<ContactChannelStateResult>(sql, new
            {
                MessageId = messageId.Trim(),
                BookingCode = bookingCode.Trim(),
                Channel = normalizedChannel,
                ContactState = state,
                UpdatedBy = string.IsNullOrWhiteSpace(updatedBy) ? null : updatedBy.Trim(),
                UpdatedSource = string.IsNullOrWhiteSpace(source) ? null : source.Trim()
            });
        }

        // 2026-03-13 - Quick-send dual-write helper for new channel-state table.
        public async Task<ContactChannelStateResult?> MarkBookingContactChannelQuickSendAsync(string messageId, string bookingCode, string channel, string? source = null, string? updatedBy = null)
        {
            if (string.IsNullOrWhiteSpace(messageId) || string.IsNullOrWhiteSpace(bookingCode))
            {
                return null;
            }

            var normalizedChannel = NormalizeContactOutcomeChannel(channel);
            if (normalizedChannel == null)
            {
                return null;
            }

            // Quick-send state promotion only applies to channels with an outbound quick-send action.
            if (!string.Equals(normalizedChannel, "sms", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(normalizedChannel, "wa", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            const string sql = @"
DECLARE @BookingId INT =
(
    SELECT TOP 1 b.Id
    FROM dbo.Bookings b
    WHERE b.MessageId = @MessageId AND b.BookingCode = @BookingCode
    ORDER BY b.UpdatedAt DESC, b.Id DESC
);

DECLARE @NowUtc DATETIME2(7) = SYSUTCDATETIME();

DECLARE @Result TABLE
(
    MessageId NVARCHAR(255),
    BookingCode NVARCHAR(100),
    Channel NVARCHAR(20),
    ContactState TINYINT,
    LegacyMessageSent BIT,
    LegacyMessageSentAtUtc DATETIME2(7),
    UpdatedAtUtc DATETIME2(7),
    UpdatedBy NVARCHAR(100),
    UpdatedSource NVARCHAR(30)
);

UPDATE target
SET ContactState = 1,
    LegacyMessageSent = 1,
    LegacyMessageSentAtUtc = @NowUtc,
    BookingId = COALESCE(@BookingId, target.BookingId),
    UpdatedBy = @UpdatedBy,
    UpdatedSource = @UpdatedSource,
    UpdatedAtUtc = @NowUtc
OUTPUT inserted.MessageId,
       inserted.BookingCode,
       inserted.Channel,
       inserted.ContactState,
       inserted.LegacyMessageSent,
       inserted.LegacyMessageSentAtUtc,
       inserted.UpdatedAtUtc,
       inserted.UpdatedBy,
       inserted.UpdatedSource
INTO @Result
FROM dbo.BookingContactChannelState AS target WITH (UPDLOCK, HOLDLOCK)
WHERE target.MessageId = @MessageId
  AND target.BookingCode = @BookingCode
  AND target.Channel = @Channel;

IF @@ROWCOUNT = 0
BEGIN
    INSERT INTO dbo.BookingContactChannelState
    (
        MessageId,
        BookingCode,
        BookingId,
        Channel,
        ContactState,
        LegacyMessageSent,
        LegacyMessageSentAtUtc,
        UpdatedBy,
        UpdatedSource,
        CreatedAtUtc,
        UpdatedAtUtc
    )
    OUTPUT inserted.MessageId,
           inserted.BookingCode,
           inserted.Channel,
           inserted.ContactState,
           inserted.LegacyMessageSent,
           inserted.LegacyMessageSentAtUtc,
           inserted.UpdatedAtUtc,
           inserted.UpdatedBy,
           inserted.UpdatedSource
    INTO @Result
    VALUES
    (
        @MessageId,
        @BookingCode,
        @BookingId,
        @Channel,
        1,
        1,
        @NowUtc,
        @UpdatedBy,
        @UpdatedSource,
        @NowUtc,
        @NowUtc
    );
END

SELECT TOP 1
    MessageId,
    BookingCode,
    Channel,
    ContactState,
    LegacyMessageSent,
    LegacyMessageSentAtUtc,
    UpdatedAtUtc,
    UpdatedBy,
    UpdatedSource
FROM @Result;";

            using var conn = CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<ContactChannelStateResult>(sql, new
            {
                MessageId = messageId.Trim(),
                BookingCode = bookingCode.Trim(),
                Channel = normalizedChannel,
                UpdatedBy = string.IsNullOrWhiteSpace(updatedBy) ? null : updatedBy.Trim(),
                UpdatedSource = string.IsNullOrWhiteSpace(source) ? null : source.Trim()
            });
        }

        // 2026-03-13 - Bulk read booking/channel states by composite key.
        public async Task<IReadOnlyList<ContactChannelStateResult>> GetBookingContactChannelStatesAsync(IEnumerable<ContactChannelStateKey> keys)
        {
            var normalizedKeys = keys?
                .Where(k => k != null && !string.IsNullOrWhiteSpace(k.MessageId) && !string.IsNullOrWhiteSpace(k.BookingCode))
                .Select(k => new ContactChannelStateKey
                {
                    MessageId = k.MessageId.Trim(),
                    BookingCode = k.BookingCode.Trim()
                })
                .GroupBy(k => $"{k.MessageId}||{k.BookingCode}", StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList() ?? new List<ContactChannelStateKey>();

            if (normalizedKeys.Count == 0)
            {
                return Array.Empty<ContactChannelStateResult>();
            }

            var valueRows = new List<string>(normalizedKeys.Count);
            var parameters = new DynamicParameters();
            for (int i = 0; i < normalizedKeys.Count; i++)
            {
                var messageParam = $"@MessageId{i}";
                var bookingParam = $"@BookingCode{i}";
                valueRows.Add($"({messageParam}, {bookingParam})");
                parameters.Add(messageParam, normalizedKeys[i].MessageId);
                parameters.Add(bookingParam, normalizedKeys[i].BookingCode);
            }

            var sql = $@"
SELECT
    s.MessageId,
    s.BookingCode,
    s.Channel,
    s.ContactState,
    s.LegacyMessageSent,
    s.LegacyMessageSentAtUtc,
    s.UpdatedAtUtc,
    s.UpdatedBy,
    s.UpdatedSource
FROM dbo.BookingContactChannelState s
INNER JOIN
(
    VALUES {string.Join(", ", valueRows)}
) AS k(MessageId, BookingCode)
    ON s.MessageId = k.MessageId
   AND s.BookingCode = k.BookingCode;";

            using var conn = CreateConnection();
            var rows = await conn.QueryAsync<ContactChannelStateResult>(sql, parameters);
            return rows.ToList();
        }

        // 2025-12-09 00:00 UTC - Per-stage message tracking for badges
        public async Task<bool> SetBookingMessageStageAsync(string messageId, string bookingCode, string stage, bool isSent, string? channel, int? templateId, string? vendorName = null)
        {
            if (string.IsNullOrWhiteSpace(messageId)) return false;
            if (string.IsNullOrWhiteSpace(stage) || !AllowedStages.Contains(stage)) return false;

            var normalizedChannel = string.IsNullOrWhiteSpace(channel) ? string.Empty : channel.Trim();
            if (!AllowedChannels.Contains(normalizedChannel)) return false;

            const string sql = @"
MERGE dbo.BookingMessageStatus AS target
USING (
    SELECT @MessageId AS MessageId, @Stage AS Stage, @Channel AS Channel,
    (SELECT TOP 1 Id FROM dbo.Bookings WHERE MessageId = @MessageId AND BookingCode = @BookingCode) AS BookingId
) AS src
ON (target.MessageId = src.MessageId AND target.Stage = src.Stage AND target.Channel = src.Channel)
WHEN MATCHED THEN
    UPDATE SET
        SentFlag   = @IsSent,
        SentAtUtc  = CASE WHEN @IsSent = 1 THEN SYSUTCDATETIME() ELSE NULL END,
        TemplateId = @TemplateId,
        BookingCode = COALESCE(@BookingCode, target.BookingCode),
        VendorName  = COALESCE(@VendorName, target.VendorName),
        BookingId   = COALESCE(src.BookingId, target.BookingId),
        UpdatedAt  = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT (MessageId, BookingCode, BookingId, VendorName, Stage, Channel, TemplateId, SentFlag, SentAtUtc, CreatedAt, UpdatedAt)
    VALUES (@MessageId, @BookingCode, src.BookingId, @VendorName, @Stage, @Channel, @TemplateId, @IsSent, CASE WHEN @IsSent = 1 THEN SYSUTCDATETIME() ELSE NULL END, SYSUTCDATETIME(), SYSUTCDATETIME());
";

            using var conn = CreateConnection();
            var rows = await conn.ExecuteAsync(sql, new
            {
                MessageId = messageId,
                BookingCode = bookingCode,
                VendorName = vendorName,
                Stage = stage,
                Channel = normalizedChannel,
                TemplateId = templateId,
                IsSent = isSent
            });

            if (!isSent)
            {
                // Also clear any other channel rows for this stage to avoid stale "sent" badges after refresh
                const string clearSql = @"
UPDATE dbo.BookingMessageStatus
SET SentFlag = 0,
    SentAtUtc = NULL,
    UpdatedAt = SYSUTCDATETIME()
WHERE MessageId = @MessageId AND Stage = @Stage;";
                await conn.ExecuteAsync(clearSql, new { MessageId = messageId, Stage = stage });
            }

            return rows > 0;
        }

        // 2025-12-09 00:00 UTC - Fetch per-stage message status for a set of MessageIds
        public async Task<Dictionary<string, List<MessageStageStatusModel>>> GetBookingMessageStagesAsync(IEnumerable<string> messageIds)
        {
            var ids = messageIds?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new();
            var result = new Dictionary<string, List<MessageStageStatusModel>>(StringComparer.OrdinalIgnoreCase);
            if (ids.Count == 0) return result;

            const string sql = @"
SELECT MessageId, Stage, SentFlag, SentAtUtc, Channel, TemplateId, BookingCode, VendorName
FROM dbo.BookingMessageStatus
WHERE MessageId IN @Ids;";

            using var conn = CreateConnection();
            var rows = await conn.QueryAsync<MessageStageStatusModel>(sql, new { Ids = ids });

            foreach (var row in rows)
            {
                if (!result.TryGetValue(row.MessageId, out var list))
                {
                    list = new List<MessageStageStatusModel>();
                    result[row.MessageId] = list;
                }
                list.Add(row);
            }

            return result;
        }

        // 2025-12-09 00:00 UTC - Message history (grouped by booking)
        public async Task<List<MessageHistoryItem>> GetMessageHistoryAsync(int skip = 0, int take = 100)
        {
            skip = Math.Max(skip, 0);
            take = Math.Clamp(take, 1, 500);

            const string sql = @"
SELECT ms.MessageId,
       ms.BookingCode,
       b.CustomerName,
       b.CustomerPhone,
       b.TourName,
       b.TourDate,
       b.DisplayDate,
       b.DisplayTime,
       b.VendorName,
       ms.Stage,
       ms.Channel,
       ms.SentFlag,
       ms.SentAtUtc,
       ms.TemplateId,
       tm.MessageName AS TemplateName
FROM dbo.BookingMessageStatus ms
LEFT JOIN dbo.Bookings b ON b.MessageId = ms.MessageId
LEFT JOIN dbo.TourMessages tm ON tm.Id = ms.TemplateId
ORDER BY b.TourDate DESC, b.BookingCode, ms.Stage, ms.Channel
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;";

            using var conn = CreateConnection();
            var rows = await conn.QueryAsync<MessageHistoryItem>(sql, new { Skip = skip, Take = take });
            return rows.ToList();
        }

        public async Task<CollectionStatusDto> GetCollectionStatusAsync()
        {
            //const string inboxSql = "SELECT COUNT_BIG(*) FROM dbo.AutomaticGmail_InboxEmails";
            //const string processedSql = "SELECT COUNT_BIG(*) FROM dbo.AutomaticGmail_ProcessedEmails";
            //const string lastCollectSql = "SELECT MAX(CollectedAt) FROM dbo.AutomaticGmail_InboxEmails";
            //const string lastProcessSql = "SELECT MAX(ProcessingCompletedAt) FROM dbo.AutomaticGmail_ProcessedEmails";

            //using var conn = CreateConnection();
            //await conn.Open();

            //var totalCollected = await conn.ExecuteScalarAsync<long>(inboxSql);
            //var totalProcessed = await conn.ExecuteScalarAsync<long>(processedSql);
            //var totalUnprocessed = Math.Max(0, totalCollected - totalProcessed);
            //var lastCollect = await conn.ExecuteScalarAsync<DateTime?>(lastCollectSql);
            //var lastProcess = await conn.ExecuteScalarAsync<DateTime?>(lastProcessSql);

            return new CollectionStatusDto
            {
                TotalCollected = 0,// (int)Math.Min(totalCollected, int.MaxValue),
                TotalProcessed = 0, //(int)Math.Min(totalProcessed, int.MaxValue),
                TotalUnprocessed = 0,//(int)Math.Min(totalUnprocessed, int.MaxValue),
                LastCollectionDate = null,// lastCollect,
                LastProcessingDate = null//lastProcess
            };
        }

        public async Task<List<CollectionSummaryRowDto>> GetCollectionSummaryAsync(int limit = 10, string bookingFilter = "all")
        {
            limit = Math.Clamp(limit, 1, 200);
            using var conn = CreateConnection();

            var inboxRows = await conn.QueryAsync<CollectionSummaryRowDto>(@"
SELECT TOP (@Limit)
    'Inbox' AS RowType,
    MessageId,
    ReceivedDate AS InboxReceivedDate,
    UpdatedAt AS InboxUpdatedAt,
    Subject AS InboxSubject,
    NULL AS InboxManualParsedEmailType
FROM dbo.AutomaticGmail_InboxEmails
ORDER BY ReceivedDate DESC", new { Limit = limit });

            var processedRows = await conn.QueryAsync<CollectionSummaryRowDto>(@"
SELECT TOP (@Limit)
    'Processed' AS RowType,
    MessageId,
    NULL AS InboxReceivedDate,
    NULL AS InboxUpdatedAt,
    NULL AS InboxSubject,
    NULL AS InboxManualParsedEmailType,
    EmailType AS ProcessedEmailType,
    ProcessingStatus AS ProcessedStatus,
    BookingCode AS ProcessedBookingCode,
    CustomerName AS ProcessedCustomerName,
    TourName AS ProcessedTourName,
    TRY_CONVERT(datetime2, TourDate) AS ProcessedTourDate,
    TourTime AS ProcessedTourTime,
    PreviousBookingCode
FROM dbo.AutomaticGmail_ProcessedEmails
ORDER BY CreatedAt DESC", new { Limit = limit });

            var bookingRows = await conn.QueryAsync<CollectionSummaryRowDto>(@"
SELECT TOP (@Limit)
    'Booking' AS RowType,
    MessageId,
    NULL AS InboxReceivedDate,
    NULL AS InboxUpdatedAt,
    NULL AS InboxSubject,
    NULL AS InboxManualParsedEmailType,
    NULL AS ProcessedEmailType,
    BookingStatus AS ProcessedStatus,
    BookingCode AS ProcessedBookingCode,
    CustomerName AS ProcessedCustomerName,
    TourName AS ProcessedTourName,
    TourDate AS ProcessedTourDate,
    TourTime AS ProcessedTourTime,
    NULL AS PreviousBookingCode,
    EmailType AS BookingEmailType,
    IsCancellation AS BookingIsCancellation,
    IsModification AS BookingIsModification,
    IsConfirmation AS BookingIsConfirmation,
    IsActive AS BookingIsActive,
    TourName AS BookingTourName,
    TourDate AS BookingTourDate,
    TourTime AS BookingTourTime,
    NumberOfAttendees AS BookingNumberOfAttendees,
    BookingStatus,
    CreatedAt AS BookingCreatedAt,
    UpdatedAt AS BookingUpdatedAt
FROM dbo.Bookings
ORDER BY CreatedAt DESC", new { Limit = limit });

            var list = new List<CollectionSummaryRowDto>();
            list.AddRange(inboxRows);
            list.AddRange(processedRows);
            if (!string.Equals(bookingFilter, "without", StringComparison.OrdinalIgnoreCase))
            {
                list.AddRange(bookingRows);
            }

            if (string.Equals(bookingFilter, "with", StringComparison.OrdinalIgnoreCase))
            {
                list = list.Where(r => r.RowType == "Booking").ToList();
            }

            return list.OrderByDescending(r => r.InboxReceivedDate ?? r.ProcessedTourDate ?? r.BookingTourDate ?? DateTime.MinValue).Take(limit * 3).ToList();
        }

        public async Task<List<UnprocessedEmailDto>> GetUnprocessedEmailsAsync(int limit = 50, int offset = 0, string? vendorFilter = null, string? searchTerm = null, bool onlyUnclassified = false, bool onlyTourRelated = true, bool onlyClassified = false)
        {
            limit = Math.Clamp(limit, 1, 200);
            if (onlyClassified && onlyUnclassified)
            {
                onlyUnclassified = false;
            }

            var sql = @"
SELECT i.Id, i.MessageId, ISNULL(i.Subject,'') AS Subject,
       ISNULL(i.FromEmail,'') AS FromEmail, ISNULL(i.FromName,'') AS FromName,
       i.ReceivedDate, i.CollectedAt,
       LEFT(ISNULL(i.TextBody, ''), 200) AS TextBodyPreview
FROM dbo.AutomaticGmail_InboxEmails i
WHERE NOT EXISTS (SELECT 1 FROM dbo.AutomaticGmail_ProcessedEmails p WHERE p.MessageId = i.MessageId)";

            var parameters = new DynamicParameters();
            if (!string.IsNullOrWhiteSpace(vendorFilter))
            {
                sql += " AND i.Subject LIKE @VendorPattern";
                parameters.Add("VendorPattern", $"%{vendorFilter}%");
            }
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                sql += " AND (i.Subject LIKE @Search OR i.FromEmail LIKE @Search)";
                parameters.Add("Search", $"%{searchTerm}%");
            }

            sql += " ORDER BY ReceivedDate DESC OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY";
            parameters.Add("Offset", offset);
            parameters.Add("Limit", limit);

            using var conn = CreateConnection();
            var rows = await conn.QueryAsync(sql, parameters);

            return rows.Select(r => new UnprocessedEmailDto
            {
                Id = r.Id,
                MessageId = r.MessageId,
                Subject = r.Subject,
                FromEmail = r.FromEmail,
                FromName = r.FromName,
                ReceivedDate = r.ReceivedDate ?? DateTime.MinValue,
                VendorName = null,
                EmailType = null,
                CollectedAt = r.CollectedAt ?? DateTime.MinValue,
                TextBodyPreview = r.TextBodyPreview ?? string.Empty
            }).ToList();
        }

        public async Task<LatestInboxEmailDto?> GetLatestInboxEmailAsync()
        {
            const string sql = @"
SELECT TOP 1 Id, Subject, FromEmail, ReceivedDate, CollectedAt, 0 AS ManualParsingCompleted
FROM dbo.AutomaticGmail_InboxEmails
ORDER BY ReceivedDate DESC";

            using var conn = CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<LatestInboxEmailDto>(sql);
        }

        public async Task<InboxEmailDetailDto?> GetInboxEmailByIdAsync(int id)
        {
            const string sql = @"
SELECT Id, Uid, MessageId, Subject, FromEmail, FromName, ToEmail,
       ReceivedDate, TextBody, HtmlBody, LEFT(ISNULL(TextBody,''),200) AS TextBodyPreview,
       AttachmentCount, AttachmentNames, CollectedAt, CollectionBatchId
FROM dbo.AutomaticGmail_InboxEmails
WHERE Id = @Id";
            using var conn = CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<InboxEmailDetailDto>(sql, new { Id = id });
        }

        public async Task<InboxEmailDetailDto?> GetInboxEmailByMessageIdAsync(string messageId)
        {
            const string sql = @"
SELECT Id, Uid, MessageId, Subject, FromEmail, FromName, ToEmail,
       ReceivedDate, TextBody, HtmlBody, LEFT(ISNULL(TextBody,''),200) AS TextBodyPreview,
       AttachmentCount, AttachmentNames, CollectedAt, CollectionBatchId
FROM dbo.AutomaticGmail_InboxEmails
WHERE MessageId = @MessageId";
            using var conn = CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<InboxEmailDetailDto>(sql, new { MessageId = messageId });
        }

        public async Task<ProcessedEmail?> GetProcessedEmailByMessageIdAsync(string messageId)
        {
            // Null-safe and type-safe projection for ProcessedEmail model
            const string sqlByMessage = @"
SELECT TOP 1
    Id,
    InboxEmailId,
    MessageId,
    ISNULL(VendorName,'')               AS VendorName,
    ISNULL(VendorManuallyOverridden,0)  AS VendorManuallyOverridden,
    VendorOverrideAt,
    ISNULL(EmailType,'')                AS EmailType,
    ISNULL(IsTourBookingEmail, 0)       AS IsTourBookingEmail,
    ISNULL(IsCancellation, 0)           AS IsCancellation,
    ISNULL(IsModification, 0)           AS IsModification,
    ISNULL(IsBooking, 0)                AS IsBooking,
    ClassificationRuleId,
    ISNULL(ProcessingStatus,'')         AS ProcessingStatus,
    ProcessingStartedAt,
    ProcessingCompletedAt,
    ProcessingError,
    ISNULL(ProcessingAttempts,0)        AS ProcessingAttempts,
    NextProcessingAttempt,
    RateLimitResetAt,
    CustomerName,
    BookingCode,
    CustomerPhone,
    CustomerEmail,
    NumberOfAttendees,
    NumberOfAdults,
    NumberOfChildren,
    TourName,
    TRY_CONVERT(datetime2, NULLIF(TourDate,'')) AS TourDate,
    TourTime,
    TourLocation,
    Language,
    CustomerIdentifier,
    ISNULL(IsLatestAction,1)            AS IsLatestAction,
    PlainTextContent,
    HtmlContent,
    ExtractedBookingCode,
    NewBookingCode,
    PreviousBookingCode,
    ExtractedAt,
    CreatedAt,
    UpdatedAt
FROM dbo.AutomaticGmail_ProcessedEmails
WHERE MessageId = @MessageId
ORDER BY ProcessingCompletedAt DESC, Id DESC;";

            using var conn = CreateConnection();
            var row = await conn.QueryFirstOrDefaultAsync<ProcessedEmail>(sqlByMessage, new { MessageId = messageId });
            if (row != null)
            {
                return row;
            }

            // Fallback: resolve by InboxEmailId if MessageId linkage is inconsistent
            const string inboxIdSql = @"SELECT TOP 1 Id FROM dbo.AutomaticGmail_InboxEmails WHERE MessageId = @MessageId";
            var inboxId = await conn.ExecuteScalarAsync<int?>(inboxIdSql, new { MessageId = messageId });
            if (inboxId.HasValue)
            {
                const string sqlByInbox = @"
SELECT TOP 1
    Id,
    InboxEmailId,
    MessageId,
    ISNULL(VendorName,'')               AS VendorName,
    ISNULL(VendorManuallyOverridden,0)  AS VendorManuallyOverridden,
    VendorOverrideAt,
    ISNULL(EmailType,'')                AS EmailType,
    ISNULL(IsTourBookingEmail, 0)       AS IsTourBookingEmail,
    ISNULL(IsCancellation, 0)           AS IsCancellation,
    ISNULL(IsModification, 0)           AS IsModification,
    ISNULL(IsBooking, 0)                AS IsBooking,
    ClassificationRuleId,
    ISNULL(ProcessingStatus,'')         AS ProcessingStatus,
    ProcessingStartedAt,
    ProcessingCompletedAt,
    ProcessingError,
    ISNULL(ProcessingAttempts,0)        AS ProcessingAttempts,
    NextProcessingAttempt,
    RateLimitResetAt,
    CustomerName,
    BookingCode,
    CustomerPhone,
    CustomerEmail,
    NumberOfAttendees,
    NumberOfAdults,
    NumberOfChildren,
    TourName,
    TRY_CONVERT(datetime2, NULLIF(TourDate,'')) AS TourDate,
    TourTime,
    TourLocation,
    Language,
    CustomerIdentifier,
    ISNULL(IsLatestAction,1)            AS IsLatestAction,
    PlainTextContent,
    HtmlContent,
    ExtractedBookingCode,
    NewBookingCode,
    PreviousBookingCode,
    ExtractedAt,
    CreatedAt,
    UpdatedAt
FROM dbo.AutomaticGmail_ProcessedEmails
WHERE InboxEmailId = @InboxId
ORDER BY ProcessingCompletedAt DESC, Id DESC;";

                row = await conn.QueryFirstOrDefaultAsync<ProcessedEmail>(sqlByInbox, new { InboxId = inboxId.Value });
            }

            return row;
        }

        public async Task<List<LiveUnifiedEmailListItem>> GetLiveInboxAsync(int limit = 50, long? olderThanUid = null)
        {
            limit = Math.Clamp(limit, 1, 200);
            using var conn = CreateConnection();

            var sql = @"SELECT TOP (@Limit) Uid, MessageId, FromName, FromEmail, Subject, ReceivedDate
                        FROM dbo.AutomaticGmail_InboxEmails";
            if (olderThanUid.HasValue)
            {
                sql += " WHERE Uid < @OlderThan";
            }
            sql += " ORDER BY Uid DESC";

            var items = (await conn.QueryAsync<LiveUnifiedEmailListItem>(sql, new { Limit = limit, OlderThan = olderThanUid })).ToList();

            if (items.Count == 0)
            {
                return items;
            }

            var messageIds = items.Select(i => i.MessageId).ToArray();
            var processedLookup = await conn.QueryAsync(@"
SELECT MessageId, Id AS ProcessedEmailId, EmailType AS ProcessedEmailType, ProcessingStatus AS ProcessedStatus,
       BookingCode, VendorName AS ProcessedVendorName, CustomerName AS ProcessedCustomerName,
       CustomerEmail AS ProcessedCustomerEmail, CustomerPhone AS ProcessedCustomerPhone, TourName AS ProcessedTourName,
       TRY_CONVERT(datetime2, TourDate) AS ProcessedTourDate, TourTime AS ProcessedTourTime, TourLocation AS ProcessedTourLocation,
       NumberOfAdults AS ProcessedNumberOfAdults, NumberOfChildren AS ProcessedNumberOfChildren, Language AS ProcessedLanguage,
       IsBooking AS ProcessedIsBooking, IsCancellation AS ProcessedIsCancellation, IsModification AS ProcessedIsModification,
       ExtractedAt AS ProcessedExtractedAt, BookingAlterationNotes AS ProcessedBookingAlterationNotes,
       InboxEmailId
FROM dbo.AutomaticGmail_ProcessedEmails
WHERE MessageId IN @MessageIds", new { MessageIds = messageIds });

            var inboxLookup = await conn.QueryAsync<(int InboxEmailId, string MessageId, int? Id)>(@"
SELECT Id AS InboxEmailId, MessageId
FROM dbo.AutomaticGmail_InboxEmails
WHERE MessageId IN @MessageIds", new { MessageIds = messageIds });

            var processedByMessage = processedLookup.ToLookup(r => (string)r.MessageId);
            var inboxByMessage = inboxLookup.ToDictionary(r => r.MessageId, r => r.InboxEmailId);

            foreach (var item in items)
            {
                if (inboxByMessage.TryGetValue(item.MessageId, out var inboxId))
                {
                    item.InboxEmailId = inboxId;
                }

                var processed = processedByMessage[item.MessageId].FirstOrDefault();
                if (processed != null)
                {
                    item.HasProcessedEmail = true;
                    item.ProcessedEmailId = processed.ProcessedEmailId;
                    item.ProcessedEmailType = processed.ProcessedEmailType;
                    item.ProcessedStatus = processed.ProcessedStatus;
                    item.BookingCode = processed.BookingCode;
                    item.ProcessedVendorName = processed.ProcessedVendorName;
                    item.ProcessedCustomerName = processed.ProcessedCustomerName;
                    item.ProcessedCustomerEmail = processed.ProcessedCustomerEmail;
                    item.ProcessedCustomerPhone = processed.ProcessedCustomerPhone;
                    item.ProcessedTourName = processed.ProcessedTourName;
                    item.ProcessedTourDate = processed.ProcessedTourDate;
                    item.ProcessedTourTime = processed.ProcessedTourTime;
                    item.ProcessedTourLocation = processed.ProcessedTourLocation;
                    item.ProcessedNumberOfAdults = processed.ProcessedNumberOfAdults;
                    item.ProcessedNumberOfChildren = processed.ProcessedNumberOfChildren;
                    item.ProcessedLanguage = processed.ProcessedLanguage;
                    item.ProcessedIsBooking = processed.ProcessedIsBooking;
                    item.ProcessedIsCancellation = processed.ProcessedIsCancellation;
                    item.ProcessedIsModification = processed.ProcessedIsModification;
                    item.ProcessedExtractedAt = processed.ProcessedExtractedAt;
                    item.ProcessedBookingAlterationNotes = processed.ProcessedBookingAlterationNotes;
                }
            }

            return items;
        }

        public async Task<LiveEmailDetail?> GetLiveEmailAsync(long uid)
        {
            const string sql = @"
SELECT TOP 1 Uid, MessageId, Subject, FromName, FromEmail,
       ToEmail AS [To], ReceivedDate, TextBody, HtmlBody,
       AttachmentCount, AttachmentNames
FROM dbo.AutomaticGmail_InboxEmails
WHERE Uid = @Uid";
            using var conn = CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<LiveEmailDetail>(sql, new { Uid = uid });
        }

        // Created: 2025-11-25 00:00 UTC - Processed grid for Syncfusion (Tour fields included)
        public async Task<List<ProcessedEmailDisplayTourEmailLibModel>> GetProcessedEmailsForGridAsync(int limit = 50, int offset = 0, string? searchTerm = null)
        {
            limit = Math.Clamp(limit, 1, 200);

            var sql = @"
SELECT p.Id,
       p.MessageId,
       ISNULL(i.Subject,'') AS Subject,
       ISNULL(i.FromEmail,'') AS FromEmail,
       i.ReceivedDate,
       ISNULL(p.VendorName,'') AS VendorName,
       ISNULL(p.EmailType,'') AS EmailType,
       ISNULL(p.IsBooking, 0) AS IsBooking,
       ISNULL(p.IsCancellation, 0) AS IsCancellation,
       ISNULL(p.IsModification, 0) AS IsModification,
       ISNULL(p.CustomerName,'') AS CustomerName,
       ISNULL(p.BookingCode,'') AS BookingCode,
       ISNULL(p.TourName,'') AS TourName,
       TRY_CONVERT(datetime2, NULLIF(p.TourDate,'')) AS TourDate,
       ISNULL(p.TourTime,'') AS TourTime,
       ISNULL(p.NumberOfAdults, 0) AS NumberOfAdults,
       ISNULL(p.NumberOfChildren, 0) AS NumberOfChildren,
       ISNULL(p.CustomerEmail,'') AS CustomerEmail,
       ISNULL(p.CustomerPhone,'') AS CustomerPhone,
       ISNULL(p.Language,'') AS Language,
       ISNULL(p.TourLocation,'') AS TourLocation,
       p.CreatedAt
FROM dbo.AutomaticGmail_ProcessedEmails p
LEFT JOIN dbo.AutomaticGmail_InboxEmails i ON i.MessageId = p.MessageId
WHERE 1=1";

            var parameters = new DynamicParameters();
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                sql += @" AND (
                        p.MessageId LIKE @q OR
                        ISNULL(i.Subject,'') LIKE @q OR
                        ISNULL(i.FromEmail,'') LIKE @q OR
                        ISNULL(p.CustomerName,'') LIKE @q OR
                        ISNULL(p.BookingCode,'') LIKE @q OR
                        ISNULL(p.VendorName,'') LIKE @q OR
                        ISNULL(p.EmailType,'') LIKE @q OR
                        ISNULL(p.TourName,'') LIKE @q
                    )";
                parameters.Add("q", $"%{searchTerm}%");
            }

            sql += " ORDER BY p.CreatedAt DESC OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY";
            parameters.Add("Offset", offset);
            parameters.Add("Limit", limit);

            using var conn = CreateConnection();
            var rows = await conn.QueryAsync(sql, parameters);

            var list = rows.Select(r => new ProcessedEmailDisplayTourEmailLibModel
            {
                Id = r.Id,
                MessageId = r.MessageId ?? string.Empty,
                Subject = r.Subject ?? string.Empty,
                FromEmail = r.FromEmail ?? string.Empty,
                ReceivedDate = r.ReceivedDate ?? DateTime.MinValue,
                VendorName = r.VendorName ?? string.Empty,
                EmailType = r.EmailType ?? string.Empty,
                IsBooking = r.IsBooking ?? false,
                IsCancellation = r.IsCancellation ?? false,
                IsModification = r.IsModification ?? false,
                CustomerName = r.CustomerName ?? string.Empty,
                BookingCode = r.BookingCode ?? string.Empty,
                TourName = r.TourName ?? string.Empty,
                TourDate = r.TourDate,
                TourTime = r.TourTime ?? string.Empty,
                NumberOfAdults = r.NumberOfAdults ?? 0,
                NumberOfChildren = r.NumberOfChildren ?? 0,
                CustomerEmail = r.CustomerEmail ?? string.Empty,
                CustomerPhone = r.CustomerPhone ?? string.Empty,
                Language = r.Language ?? string.Empty,
                TourLocation = r.TourLocation ?? string.Empty,
                CreatedAt = r.CreatedAt ?? DateTime.MinValue
            }).ToList();

            return list;
        }

        private static string? NormalizeContactOutcomeChannel(string? channel)
        {
            if (string.IsNullOrWhiteSpace(channel))
            {
                return null;
            }

            var normalized = channel.Trim().ToLowerInvariant();
            return AllowedContactOutcomeChannels.Contains(normalized) ? normalized : null;
        }
    }
}

