using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Email.Services.GmailProcessing.Dtos;
using Email.Services.GmailProcessing.Models;
using Email.Services.GmailCollection.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Email.Services.GmailProcessing
{
    /// <summary>
    /// Created: 2025-11-07 00:00 UTC
    /// SQL Server repository for Gmail v2 processing (classification + processed emails).
    /// </summary>
    public interface IGmailProcessingRepository
    {
        Task<IReadOnlyList<ClassificationRuleRecord>> LoadActiveClassificationRulesAsync(CancellationToken ct);
        Task<IReadOnlyList<UnprocessedEmailRowDto>> GetUnprocessedInboxEmailsAsync(int limit, CancellationToken ct);
        Task<IReadOnlyList<InboxEmailRecord>> GetInboxEmailsByIdsAsync(IReadOnlyList<int> inboxIds, CancellationToken ct);
        Task<IReadOnlyList<InboxEmailRecord>> GetUnprocessedInboxPageAsync(DateTime? afterReceivedDate, int? afterId, int pageSize, CancellationToken ct);
        Task<int?> GetInboxEmailIdByMessageIdAsync(string messageId, CancellationToken ct);
        Task<int> UpsertProcessedEmailAsync(ProcessedEmailRecord rec, CancellationToken ct);
        Task SetLatestActionForThreadAsync(string vendorName, string? bookingCode, int processedEmailId, CancellationToken ct);
        Task<GmailProcessingStatusDto> GetStatusAsync(CancellationToken ct);
        Task<ProcessedEmailDisplayDto?> GetProcessedEmailByIdAsync(int processedId, CancellationToken ct);
        Task<IReadOnlyList<ProcessedEmailDisplayDto>> GetRecentProcessedAsync(int limit, CancellationToken ct);
        Task<IReadOnlyList<ProcessedEmailRecord>> GetProcessedByIdsAsync(IReadOnlyList<int> processedIds, CancellationToken ct);
        Task DeleteAsync(IReadOnlyList<int> processedIds, CancellationToken ct);
        Task<IReadOnlyList<ProcessedEmailDisplayDto>> GetRecentSkippedAsync(int limit, CancellationToken ct);
		Task UpdateInboxProcessingStatusAsync(int inboxEmailId, TourEmailInboxProcessingStatus status, CancellationToken ct);
		// Added: 2025-11-30 00:00 UTC - Persist original booking pointer on inbox row (idempotent)
		Task UpdateInboxOriginalBookingAsync(int inboxEmailId, int? originalBookingId, string? originalBookingCode, string? originalMessageId, CancellationToken ct);
		Task<IReadOnlyList<InboxProcessingStatusCountDto>> GetInboxProcessingStatusCountsAsync(CancellationToken ct);
		Task<IReadOnlyList<ProcessedEmailDisplayDto>> GetProcessedByBookingCodeAsync(string bookingCode, CancellationToken ct);
		// 2025-12-02 00:00 UTC - Batch helper to find modification rows missing tour fields
		Task<IReadOnlyList<int>> GetProcessedIdsForModificationsMissingTourFieldsAsync(int limit, CancellationToken ct);
		/// <summary>
		/// Created: 2025-11-29 00:00 UTC
		/// Returns a diagnostic hydration by MessageId: Inbox, latest Processed, Booking (by MessageId then BookingCode), and Customer.
		/// </summary>
		Task<HydratedEmailDiagnosticContext?> GetHydratedDiagnosticByMessageIdAsync(string messageId, CancellationToken ct);
		/// <summary>
		/// Created: 2025-11-29 00:00 UTC
		/// SELECT * full row from AutomaticGmail_ProcessedEmails by MessageId (latest by ProcessingCompletedAt, Id).
		/// </summary>
		Task<ProcessedEmailRecord?> GetProcessedFullByMessageIdAsync(string messageId, CancellationToken ct);
		/// <summary>
		/// Added: 2025-11-30 00:00 UTC
		/// Repairs historical ProcessedEmail.BookingCode values for Modification/Cancellation rows using
		/// the same selection rules used by the processor. Returns number of rows updated.
		/// </summary>
		Task<int> RepairProcessedBookingCodesAsync(int limit, CancellationToken ct);

		// Added: 2025-12-01 00:00 UTC - Root/original booking computation helpers
		Task<ProcessedEmailRecord?> GetEarliestEventByBookingCodeAsync(string bookingCode, CancellationToken ct);
		Task<ProcessedEmailRecord?> GetEarliestBookingEventByBookingCodeAsync(string bookingCode, CancellationToken ct);
		Task<ProcessedEmailRecord?> GetEarliestModificationProducingNewCodeAsync(string newCode, CancellationToken ct);
		Task<IReadOnlyList<ProcessedEmailRecord>> GetProcessedByBookingCodesAsync(IReadOnlyList<string> codes, CancellationToken ct);
		// 2025-12-01 00:00 UTC - Forward traversal helper: find modifications that originate from a given previous code
		Task<IReadOnlyList<ProcessedEmailRecord>> GetModificationsByPreviousCodeAsync(string previousCode, CancellationToken ct);
		// Added: 2025-12-01 00:00 UTC - Override (no COALESCE) pointer for repair
		Task<int> OverrideInboxOriginalBookingPointerAsync(int inboxEmailId, int? rootBookingId, string? rootBookingCode, string? rootMessageId, CancellationToken ct);
		// Added: 2025-12-01 00:00 UTC - Repair batch helpers
		Task<IReadOnlyList<InboxEmailRecord>> GetInboxBatchNeedingOriginRebuildAsync(int lastIdExclusive, int batchSize, CancellationToken ct);
		Task<string?> GetEffectiveBookingCodeForMessageAsync(string messageId, CancellationToken ct);
        // Added: 2025-12-16 - Manual booking support
        Task<int> CreateInboxEmailAsync(InboxEmailRecord email, CancellationToken ct);
    }

    public sealed class SqlServerGmailProcessingRepository : IGmailProcessingRepository
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<SqlServerGmailProcessingRepository> _logger;
        private readonly string _connectionString;

        public SqlServerGmailProcessingRepository(IConfiguration configuration, ILogger<SqlServerGmailProcessingRepository> logger)
        {
            _configuration = configuration;
            _logger = logger;
            _connectionString = _configuration.GetConnectionString("AutomaticGmailSqlServer") ?? throw new InvalidOperationException("Missing ConnectionStrings:AutomaticGmailSqlServer");
        }

        private IDbConnection CreateConnection()
        {
            if (string.IsNullOrWhiteSpace(_connectionString))
            {
                throw new InvalidOperationException("ConnectionStrings:AutomaticGmailSqlServer is not configured.");
            }
            return new SqlConnection(_connectionString);
        }

        public async Task<IReadOnlyList<ClassificationRuleRecord>> LoadActiveClassificationRulesAsync(CancellationToken ct)
        {
            const string sql = @"
SELECT Id, VendorName, Domain, SubjectPhrase, EmailType, IsActive, Priority, CreatedAt, UpdatedAt
FROM dbo.AutomaticGmail_EmailClassificationRules
WHERE IsActive = 1
ORDER BY Priority DESC, Id ASC;";
            using var conn = CreateConnection();
            var rows = await conn.QueryAsync<ClassificationRuleRecord>(new CommandDefinition(sql, cancellationToken: ct));
            return rows.ToList();
        }

        public async Task<IReadOnlyList<UnprocessedEmailRowDto>> GetUnprocessedInboxEmailsAsync(int limit, CancellationToken ct)
        {
            const string sql = @"
SELECT TOP (@Limit)
    i.Id,
    i.MessageId,
    ISNULL(i.Subject, '') AS Subject,
    ISNULL(i.FromEmail, '') AS FromEmail,
    COALESCE(i.ReceivedDate, i.CollectedAt) AS ReceivedDate
FROM dbo.AutomaticGmail_InboxEmails i
LEFT JOIN dbo.AutomaticGmail_ProcessedEmails p
  ON p.MessageId = i.MessageId
WHERE p.Id IS NULL
ORDER BY i.ReceivedDate DESC;";
            using var conn = CreateConnection();
            var rows = await conn.QueryAsync<UnprocessedEmailRowDto>(new CommandDefinition(sql, new { Limit = limit }, cancellationToken: ct));
            return rows.ToList();
        }

        public async Task<IReadOnlyList<InboxEmailRecord>> GetInboxEmailsByIdsAsync(IReadOnlyList<int> inboxIds, CancellationToken ct)
        {
            if (inboxIds == null || inboxIds.Count == 0) return Array.Empty<InboxEmailRecord>();
            const string sql = @"
SELECT Id, Uid, MessageId, Subject, FromEmail, FromName, ToEmail, ReceivedDate, TextBody, HtmlBody, TextBodyPreview,
       AttachmentCount, AttachmentNames, CollectedAt, CollectionBatchId, IsRead, CreatedAt, UpdatedAt,
       OriginalBookingId, OriginalBookingCode, OriginalBookingMessageId
FROM dbo.AutomaticGmail_InboxEmails
WHERE Id IN @Ids;";
            using var conn = CreateConnection();
            var rows = await conn.QueryAsync<InboxEmailRecord>(new CommandDefinition(sql, new { Ids = inboxIds }, cancellationToken: ct));
            return rows.ToList();
        }

        /// <summary>
        /// Created: 2025-11-10 00:00 UTC
        /// Streams unprocessed inbox emails in ascending order of (ReceivedDate, Id) starting after the bookmark.
        /// Returns full inbox rows including HTML/text bodies to avoid follow-up IN (@...).
        /// </summary>
        public async Task<IReadOnlyList<InboxEmailRecord>> GetUnprocessedInboxPageAsync(DateTime? afterReceivedDate, int? afterId, int pageSize, CancellationToken ct)
        {
            const string sql = @"
SELECT TOP (@PageSize)
       i.Id, i.Uid, i.MessageId, i.Subject, i.FromEmail, i.FromName, i.ToEmail,
       i.ReceivedDate, i.TextBody, i.HtmlBody, i.TextBodyPreview,
       i.AttachmentCount, i.AttachmentNames, i.CollectedAt, i.CollectionBatchId,
       i.IsRead, i.CreatedAt, i.UpdatedAt, i.ProcessingStatus
FROM dbo.AutomaticGmail_InboxEmails i
LEFT JOIN dbo.AutomaticGmail_ProcessedEmails p
  ON p.MessageId = i.MessageId
WHERE p.Id IS NULL
  AND i.ProcessingStatus = 3
  AND (
        @AfterDate IS NULL
        OR i.ReceivedDate > @AfterDate
        OR (i.ReceivedDate = @AfterDate AND i.Id > @AfterId)
      )
ORDER BY i.ReceivedDate ASC, i.Id ASC;";
            using var conn = CreateConnection();
            var rows = await conn.QueryAsync<InboxEmailRecord>(new CommandDefinition(sql, new
            {
                AfterDate = afterReceivedDate,
                AfterId = afterId,
                PageSize = pageSize
            }, cancellationToken: ct));
            return rows.ToList();
        }

        public async Task<int?> GetInboxEmailIdByMessageIdAsync(string messageId, CancellationToken ct)
        {
            const string sql = @"SELECT TOP 1 Id FROM dbo.AutomaticGmail_InboxEmails WHERE MessageId = @MessageId";
            using var conn = CreateConnection();
            return await conn.ExecuteScalarAsync<int?>(new CommandDefinition(sql, new { MessageId = messageId }, cancellationToken: ct));
        }

        public async Task<int> UpsertProcessedEmailAsync(ProcessedEmailRecord rec, CancellationToken ct)
        {
            const string merge = @"
MERGE dbo.AutomaticGmail_ProcessedEmails AS t
USING (SELECT @MessageId AS MessageId) AS s
ON (t.MessageId = s.MessageId)
WHEN MATCHED THEN
  UPDATE SET
    InboxEmailId = @InboxEmailId,
    VendorName = @VendorName,
    EmailType = @EmailType,
    IsTourBookingEmail = @IsTourBookingEmail,
    ClassificationRuleId = @ClassificationRuleId,
    ProcessingStatus = @ProcessingStatus,
    ProcessingStartedAt = @ProcessingStartedAt,
    ProcessingCompletedAt = @ProcessingCompletedAt,
    ProcessingError = @ProcessingError,
    ProcessingAttempts = @ProcessingAttempts,
    NextProcessingAttempt = @NextProcessingAttempt,
    RateLimitResetAt = @RateLimitResetAt,
    CustomerName = @CustomerName,
    BookingCode = @BookingCode,
    CustomerPhone = @CustomerPhone,
    CustomerEmail = @CustomerEmail,
    NumberOfAttendees = @NumberOfAttendees,
    Language = @Language,
    TourDate = @TourDate,
    TourTime = @TourTime,
    TourName = @TourName,
    TourLocation = @TourLocation,
    ActionRequired = @ActionRequired,
    ExtractedAt = @ExtractedAt,
    CustomerIdentifier = @CustomerIdentifier,
    RelatedEmailIds = @RelatedEmailIds,
    IsLatestAction = @IsLatestAction,
    UpdatedAt = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
  INSERT (InboxEmailId, MessageId, VendorName, EmailType, IsTourBookingEmail, ClassificationRuleId,
          ProcessingStatus, ProcessingStartedAt, ProcessingCompletedAt, ProcessingError, ProcessingAttempts,
          NextProcessingAttempt, RateLimitResetAt, CustomerName, BookingCode, CustomerPhone, CustomerEmail,
          NumberOfAttendees, Language, TourDate, TourTime, TourName, TourLocation, ActionRequired, ExtractedAt,
          CustomerIdentifier, RelatedEmailIds, IsLatestAction, CreatedAt, UpdatedAt)
  VALUES (@InboxEmailId, @MessageId, @VendorName, @EmailType, @IsTourBookingEmail, @ClassificationRuleId,
          @ProcessingStatus, @ProcessingStartedAt, @ProcessingCompletedAt, @ProcessingError, @ProcessingAttempts,
          @NextProcessingAttempt, @RateLimitResetAt, @CustomerName, @BookingCode, @CustomerPhone, @CustomerEmail,
          @NumberOfAttendees, @Language, @TourDate, @TourTime, @TourName, @TourLocation, @ActionRequired, @ExtractedAt,
          @CustomerIdentifier, @RelatedEmailIds, @IsLatestAction, SYSUTCDATETIME(), SYSUTCDATETIME())
OUTPUT inserted.Id;";

            using var conn = CreateConnection();
            var newId = await conn.QuerySingleAsync<int>(new CommandDefinition(merge, new
            {
                rec.InboxEmailId,
                rec.MessageId,
                rec.VendorName,
                rec.EmailType,
                rec.IsTourBookingEmail,
                rec.ClassificationRuleId,
                rec.ProcessingStatus,
                rec.ProcessingStartedAt,
                rec.ProcessingCompletedAt,
                rec.ProcessingError,
                rec.ProcessingAttempts,
                rec.NextProcessingAttempt,
                rec.RateLimitResetAt,
                rec.CustomerName,
                rec.BookingCode,
                rec.CustomerPhone,
                rec.CustomerEmail,
                rec.NumberOfAttendees,
                rec.Language,
                rec.TourDate,
                rec.TourTime,
                rec.TourName,
                rec.TourLocation,
                rec.ActionRequired,
                rec.ExtractedAt,
                rec.CustomerIdentifier,
                rec.RelatedEmailIds,
                rec.IsLatestAction
            }, cancellationToken: ct));

            return newId;
        }

        public async Task SetLatestActionForThreadAsync(string vendorName, string? bookingCode, int processedEmailId, CancellationToken ct)
        {
            const string sql = @"
UPDATE dbo.AutomaticGmail_ProcessedEmails
SET IsLatestAction = 0, UpdatedAt = SYSUTCDATETIME()
WHERE VendorName = @VendorName
  AND ((@BookingCode IS NULL AND BookingCode IS NULL) OR BookingCode = @BookingCode)
  AND Id <> @ProcessedId;

UPDATE dbo.AutomaticGmail_ProcessedEmails
SET IsLatestAction = 1, UpdatedAt = SYSUTCDATETIME()
WHERE Id = @ProcessedId;";

            using var conn = CreateConnection();
            await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                VendorName = vendorName,
                BookingCode = bookingCode,
                ProcessedId = processedEmailId
            }, cancellationToken: ct));
        }

        public async Task<GmailProcessingStatusDto> GetStatusAsync(CancellationToken ct)
        {
            const string sql = @"
SELECT COUNT_BIG(*) FROM dbo.AutomaticGmail_InboxEmails;
SELECT COUNT_BIG(*) FROM dbo.AutomaticGmail_ProcessedEmails;
SELECT COUNT_BIG(*) FROM dbo.AutomaticGmail_InboxEmails i
LEFT JOIN dbo.AutomaticGmail_ProcessedEmails p ON p.MessageId = i.MessageId
WHERE p.Id IS NULL;
SELECT COUNT_BIG(*) FROM dbo.AutomaticGmail_ProcessedEmails WHERE ProcessingStatus = 'Error';
SELECT MAX(ProcessingCompletedAt) FROM dbo.AutomaticGmail_ProcessedEmails;";

            using var conn = CreateConnection();
            using var grid = await conn.QueryMultipleAsync(new CommandDefinition(sql, cancellationToken: ct));
            var inbox = await grid.ReadFirstAsync<long>();
            var processed = await grid.ReadFirstAsync<long>();
            var unprocessed = await grid.ReadFirstAsync<long>();
            var errors = await grid.ReadFirstAsync<long>();
            var lastCompleted = await grid.ReadFirstOrDefaultAsync<DateTime?>();

            return new GmailProcessingStatusDto
            {
                Success = true,
                TotalInboxRows = inbox,
                TotalProcessedRows = processed,
                TotalUnprocessedRows = unprocessed,
                ErrorCount = errors,
                LastProcessingCompletedAt = lastCompleted,
                Message = "OK"
            };
        }

        public async Task<ProcessedEmailDisplayDto?> GetProcessedEmailByIdAsync(int processedId, CancellationToken ct)
        {
            const string sql = @"
SELECT 
    p.Id,
    p.InboxEmailId,
    p.MessageId,
    p.VendorName,
    p.EmailType,
    p.CustomerName,
    p.CustomerEmail,
    p.CustomerPhone,
    p.BookingCode,
    p.ProcessingStatus,
    p.ExtractedAt,
    p.IsLatestAction,
    p.ProcessingCompletedAt,
    p.ProcessingError
FROM dbo.AutomaticGmail_ProcessedEmails p
WHERE p.Id = @Id;";

            using var conn = CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<ProcessedEmailDisplayDto>(
                new CommandDefinition(sql, new { Id = processedId }, cancellationToken: ct));
        }

        public async Task<IReadOnlyList<ProcessedEmailDisplayDto>> GetRecentProcessedAsync(int limit, CancellationToken ct)
        {
            const string sql = @"
SELECT TOP (@Limit)
    p.Id,
    p.InboxEmailId,
    p.MessageId,
    p.VendorName,
    p.EmailType,
    p.CustomerName,
    p.CustomerEmail,
    p.CustomerPhone,
    p.BookingCode,
    p.ProcessingStatus,
    p.ExtractedAt,
    p.IsLatestAction,
    p.ProcessingCompletedAt,
    p.ProcessingError
FROM dbo.AutomaticGmail_ProcessedEmails p
WHERE (p.IsTourBookingEmail = 1)
   OR (p.EmailType IN ('Booking','Confirmation','Modification','Cancellation'))
ORDER BY p.ProcessingCompletedAt DESC, p.Id DESC;";
            using var conn = CreateConnection();
            var rows = await conn.QueryAsync<ProcessedEmailDisplayDto>(
                new CommandDefinition(sql, new { Limit = limit }, cancellationToken: ct));
            return rows.ToList();
        }

        public async Task<IReadOnlyList<ProcessedEmailRecord>> GetProcessedByIdsAsync(IReadOnlyList<int> processedIds, CancellationToken ct)
        {
            if (processedIds == null || processedIds.Count == 0) return Array.Empty<ProcessedEmailRecord>();
            const string sql = @"
SELECT
    Id,
    InboxEmailId,
    MessageId,
    VendorName,
    VendorManuallyOverridden,
    VendorOverrideAt,
    EmailType,
    IsTourBookingEmail,
    ClassificationRuleId,
    ProcessingStatus,
    ProcessingStartedAt,
    ProcessingCompletedAt,
    ProcessingError,
    ProcessingAttempts,
    NextProcessingAttempt,
    RateLimitResetAt,
    CustomerName,
    BookingCode,
    CustomerPhone,
    CustomerEmail,
    NumberOfAttendees,
    Language,
    TourDate,
    TourTime,
    TourName,
    TourLocation,
    ActionRequired,
    ExtractedAt,
    CustomerIdentifier,
    RelatedEmailIds,
    IsLatestAction,
    CreatedAt,
    UpdatedAt
FROM dbo.AutomaticGmail_ProcessedEmails
WHERE Id IN @Ids;";
            using var conn = CreateConnection();
            var rows = await conn.QueryAsync<ProcessedEmailRecord>(
                new CommandDefinition(sql, new { Ids = processedIds }, cancellationToken: ct));
            return rows.ToList();
        }

        public async Task DeleteAsync(IReadOnlyList<int> processedIds, CancellationToken ct)
        {
            const string sql = @"DELETE FROM dbo.AutomaticGmail_ProcessedEmails WHERE Id IN @Ids";
            using var conn = CreateConnection();
            await conn.ExecuteAsync(new CommandDefinition(sql, new { Ids = processedIds }, cancellationToken: ct));
        }

        public async Task<IReadOnlyList<ProcessedEmailDisplayDto>> GetRecentSkippedAsync(int limit, CancellationToken ct)
        {
            const string sql = @"
SELECT TOP (@Limit)
    p.Id,
    p.InboxEmailId,
    p.MessageId,
    p.VendorName,
    p.EmailType,
    p.CustomerName,
    p.CustomerEmail,
    p.CustomerPhone,
    p.BookingCode,
    p.ProcessingStatus,
    p.ExtractedAt,
    p.IsLatestAction,
    p.ProcessingCompletedAt,
    p.ProcessingError
FROM dbo.AutomaticGmail_ProcessedEmails p
WHERE (p.IsTourBookingEmail = 0)
   OR (p.EmailType NOT IN ('Booking','Confirmation','Modification','Cancellation'))
   OR (p.ProcessingStatus IS NULL OR p.ProcessingStatus NOT IN ('completed'))
   OR (p.BookingCode IS NULL AND p.EmailType IN ('Booking','Confirmation','Modification','Cancellation'))
ORDER BY p.ProcessingCompletedAt DESC, p.Id DESC;";
            using var conn = CreateConnection();
            var rows = await conn.QueryAsync<ProcessedEmailDisplayDto>(
                new CommandDefinition(sql, new { Limit = limit }, cancellationToken: ct));
            return rows.ToList();
        }

		public async Task UpdateInboxProcessingStatusAsync(int inboxEmailId, TourEmailInboxProcessingStatus status, CancellationToken ct)
		{
			const string sql = @"UPDATE dbo.AutomaticGmail_InboxEmails SET ProcessingStatus = @Status, UpdatedAt = SYSUTCDATETIME() WHERE Id = @Id";
			using var conn = CreateConnection();
			await conn.ExecuteAsync(new CommandDefinition(sql, new { Id = inboxEmailId, Status = (short)status }, cancellationToken: ct));
		}

		// Added: 2025-11-30 00:00 UTC - Idempotent original booking pointer update
		public async Task UpdateInboxOriginalBookingAsync(int inboxEmailId, int? originalBookingId, string? originalBookingCode, string? originalMessageId, CancellationToken ct)
		{
			const string sql = @"
UPDATE dbo.AutomaticGmail_InboxEmails
SET OriginalBookingId = COALESCE(OriginalBookingId, @OriginalBookingId),
    OriginalBookingCode = COALESCE(OriginalBookingCode, @OriginalBookingCode),
    OriginalBookingMessageId = COALESCE(OriginalBookingMessageId, @OriginalBookingMessageId),
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @InboxEmailId;";
			using var conn = CreateConnection();
			await conn.ExecuteAsync(new CommandDefinition(sql, new
			{
				InboxEmailId = inboxEmailId,
				OriginalBookingId = originalBookingId,
				OriginalBookingCode = originalBookingCode,
				OriginalBookingMessageId = originalMessageId
			}, cancellationToken: ct));
		}

		public async Task<IReadOnlyList<InboxProcessingStatusCountDto>> GetInboxProcessingStatusCountsAsync(CancellationToken ct)
		{
			const string sql = @"
SELECT CAST(ProcessingStatus AS smallint) AS Status,
       COUNT_BIG(*) AS [Count]
FROM dbo.AutomaticGmail_InboxEmails
GROUP BY ProcessingStatus
ORDER BY Status;";
			using var conn = CreateConnection();
			var rows = await conn.QueryAsync<InboxProcessingStatusCountDto>(new CommandDefinition(sql, cancellationToken: ct));
			return rows.ToList();
		}

		public async Task<IReadOnlyList<ProcessedEmailDisplayDto>> GetProcessedByBookingCodeAsync(string bookingCode, CancellationToken ct)
		{
			const string sql = @"
SELECT
    p.Id,
    p.InboxEmailId,
    p.MessageId,
    p.VendorName,
    p.EmailType,
    p.CustomerName,
    p.CustomerEmail,
    p.CustomerPhone,
    p.BookingCode,
    p.ProcessingStatus,
    p.ExtractedAt,
    p.IsLatestAction,
    p.ProcessingCompletedAt,
    p.ProcessingError
FROM dbo.AutomaticGmail_ProcessedEmails p
WHERE p.BookingCode = @BookingCode
ORDER BY p.ProcessingCompletedAt ASC, p.Id ASC;";
			using var conn = CreateConnection();
			var rows = await conn.QueryAsync<ProcessedEmailDisplayDto>(new CommandDefinition(sql, new { BookingCode = bookingCode }, cancellationToken: ct));
			return rows.ToList();
		}

		/// <summary>
		/// Created: 2025-11-29 00:00 UTC
		/// Hydrates a diagnostic view by MessageId across Inbox, ProcessedEmails, Bookings, and Customers.
		/// </summary>
		public async Task<HydratedEmailDiagnosticContext?> GetHydratedDiagnosticByMessageIdAsync(string messageId, CancellationToken ct)
		{
			using var conn = CreateConnection();

			// Inbox
			const string inboxSql = @"
SELECT TOP 1 Id, Uid, MessageId, Subject, FromEmail, FromName, ToEmail, ReceivedDate, TextBody, HtmlBody, TextBodyPreview,
       AttachmentCount, AttachmentNames, CollectedAt, CollectionBatchId, IsRead, CreatedAt, UpdatedAt, ProcessingStatus,
       OriginalBookingId, OriginalBookingCode, OriginalBookingMessageId
FROM dbo.AutomaticGmail_InboxEmails
WHERE MessageId = @MessageId
ORDER BY Id DESC;";

			// Latest processed by completion time
			const string processedSql = @"
SELECT TOP 1
    Id,
    InboxEmailId,
    MessageId,
    VendorName,
    VendorManuallyOverridden,
    VendorOverrideAt,
    EmailType,
    IsTourBookingEmail,
    ClassificationRuleId,
    ProcessingStatus,
    ProcessingStartedAt,
    ProcessingCompletedAt,
    ProcessingError,
    ProcessingAttempts,
    NextProcessingAttempt,
    RateLimitResetAt,
    CustomerName,
    BookingCode,
    CustomerPhone,
    CustomerEmail,
    NumberOfAttendees,
    Language,
    TourDate,
    TourTime,
    TourName,
    TourLocation,
    ActionRequired,
    ExtractedAt,
    CustomerIdentifier,
    RelatedEmailIds,
    IsLatestAction,
    CreatedAt,
    UpdatedAt,
    PlainTextContent,
    HtmlContent,
    ExtractedBookingCode,
    NewBookingCode,
    PreviousBookingCode,
    IsCancellation,
    IsModification,
    IsBooking,
    NumberOfAdults,
    NumberOfChildren
FROM dbo.AutomaticGmail_ProcessedEmails
WHERE MessageId = @MessageId
ORDER BY ProcessingCompletedAt DESC, Id DESC;";

			// Booking by message id
			const string bookingByMessageSql = @"
SELECT TOP 1 *
FROM dbo.Bookings
WHERE MessageId = @MessageId
ORDER BY UpdatedAt DESC, Id DESC;";

			var inbox = await conn.QueryFirstOrDefaultAsync<InboxEmailRecord>(new CommandDefinition(inboxSql, new { MessageId = messageId }, cancellationToken: ct));
			var processed = await conn.QueryFirstOrDefaultAsync<ProcessedEmailRecord>(new CommandDefinition(processedSql, new { MessageId = messageId }, cancellationToken: ct));

			// Try booking by message id first
			var booking = await conn.QueryFirstOrDefaultAsync<Booking>(new CommandDefinition(bookingByMessageSql, new { MessageId = messageId }, cancellationToken: ct));

			// Fallback by BookingCode (from processed)
			if (booking == null && !string.IsNullOrWhiteSpace(processed?.BookingCode))
			{
				const string bookingByCodeSql = @"
SELECT TOP 1 *
FROM dbo.Bookings
WHERE BookingCode = @BookingCode
ORDER BY UpdatedAt DESC, Id DESC;";
				booking = await conn.QueryFirstOrDefaultAsync<Booking>(new CommandDefinition(bookingByCodeSql, new { BookingCode = processed!.BookingCode }, cancellationToken: ct));
			}

			Customer? customer = null;
			if (booking?.CustomerId > 0)
			{
				const string customerSql = @"SELECT TOP 1 * FROM dbo.Customers WHERE Id = @Id;";
				customer = await conn.QueryFirstOrDefaultAsync<Customer>(new CommandDefinition(customerSql, new { Id = booking.CustomerId }, cancellationToken: ct));
			}

			if (inbox == null && processed == null && booking == null && customer == null)
			{
				return null;
			}

			return new HydratedEmailDiagnosticContext
			{
				InboxDiagnostic = inbox,
				ProcessedDiagnostic = processed,
				BookingDiagnostic = booking,
				CustomerDiagnostic = customer
			};
		}

		/// <summary>
		/// Created: 2025-11-29 00:00 UTC
		/// Full ProcessedEmailRecord row by MessageId (SELECT *), latest by completion time.
		/// </summary>
		public async Task<ProcessedEmailRecord?> GetProcessedFullByMessageIdAsync(string messageId, CancellationToken ct)
		{
			const string sqlByMessage = @"
SELECT TOP 1 *
FROM dbo.AutomaticGmail_ProcessedEmails
WHERE MessageId = @MessageId
ORDER BY ProcessingCompletedAt DESC, Id DESC;";
			using var conn = CreateConnection();
			var rec = await conn.QueryFirstOrDefaultAsync<ProcessedEmailRecord>(new CommandDefinition(sqlByMessage, new { MessageId = messageId }, cancellationToken: ct));
			if (rec != null) return rec;

			// Fallback: resolve InboxEmailId from Inbox table, then look up processed by InboxEmailId
			const string inboxIdSql = @"SELECT TOP 1 Id FROM dbo.AutomaticGmail_InboxEmails WHERE MessageId = @MessageId";
			var inboxId = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(inboxIdSql, new { MessageId = messageId }, cancellationToken: ct));
			if (inboxId.HasValue)
			{
				const string sqlByInbox = @"
SELECT TOP 1 *
FROM dbo.AutomaticGmail_ProcessedEmails
WHERE InboxEmailId = @InboxId
ORDER BY ProcessingCompletedAt DESC, Id DESC;";
				rec = await conn.QueryFirstOrDefaultAsync<ProcessedEmailRecord>(new CommandDefinition(sqlByInbox, new { InboxId = inboxId.Value }, cancellationToken: ct));
			}

			return rec;
		}

		// Added: 2025-11-30 00:00 UTC - Historical repair for processed BookingCode
		public async Task<int> RepairProcessedBookingCodesAsync(int limit, CancellationToken ct)
		{
			using var conn = CreateConnection();

			// Cancellation: expected code = COALESCE(PreviousBookingCode, NULLIF(BookingCode,''), ExtractedBookingCode)
			const string fixCancels = @"
WITH cte AS (
	SELECT TOP (@Limit) *
	FROM dbo.AutomaticGmail_ProcessedEmails WITH (READPAST)
	WHERE EmailType = 'Cancellation'
	  AND ISNULL(BookingCode,'') <> ISNULL(COALESCE(PreviousBookingCode, NULLIF(BookingCode,''), ExtractedBookingCode),'')
	ORDER BY Id
)
UPDATE cte
SET BookingCode = COALESCE(PreviousBookingCode, NULLIF(BookingCode,''), ExtractedBookingCode);";

			// Modification: expected code = COALESCE(NewBookingCode, NULLIF(BookingCode,''), PreviousBookingCode, ExtractedBookingCode)
			const string fixMods = @"
WITH cte AS (
	SELECT TOP (@Limit) *
	FROM dbo.AutomaticGmail_ProcessedEmails WITH (READPAST)
	WHERE EmailType = 'Modification'
	  AND ISNULL(BookingCode,'') <> ISNULL(COALESCE(NewBookingCode, NULLIF(BookingCode,''), PreviousBookingCode, ExtractedBookingCode),'')
	ORDER BY Id
)
UPDATE cte
SET BookingCode = COALESCE(NewBookingCode, NULLIF(BookingCode,''), PreviousBookingCode, ExtractedBookingCode);";

			var updCancels = await conn.ExecuteAsync(new CommandDefinition(fixCancels, new { Limit = limit }, cancellationToken: ct));
			var updMods = await conn.ExecuteAsync(new CommandDefinition(fixMods, new { Limit = limit }, cancellationToken: ct));
			return updCancels + updMods;
		}

		// Added: 2025-12-01 00:00 UTC - Root/original booking computation helpers
		public async Task<ProcessedEmailRecord?> GetEarliestEventByBookingCodeAsync(string bookingCode, CancellationToken ct)
		{
			const string sql = @"
SELECT TOP (1) *
FROM dbo.AutomaticGmail_ProcessedEmails
WHERE BookingCode = @Code
ORDER BY COALESCE(ProcessingCompletedAt, ExtractedAt), Id;";
			using var conn = CreateConnection();
			return await conn.QueryFirstOrDefaultAsync<ProcessedEmailRecord>(new CommandDefinition(sql, new { Code = bookingCode }, cancellationToken: ct));
		}

		public async Task<ProcessedEmailRecord?> GetEarliestBookingEventByBookingCodeAsync(string bookingCode, CancellationToken ct)
		{
			const string sql = @"
SELECT TOP (1) *
FROM dbo.AutomaticGmail_ProcessedEmails
WHERE BookingCode = @Code
  AND EmailType IN ('Booking','Confirmation')
ORDER BY COALESCE(ProcessingCompletedAt, ExtractedAt), Id;";
			using var conn = CreateConnection();
			return await conn.QueryFirstOrDefaultAsync<ProcessedEmailRecord>(new CommandDefinition(sql, new { Code = bookingCode }, cancellationToken: ct));
		}

		public async Task<ProcessedEmailRecord?> GetEarliestModificationProducingNewCodeAsync(string newCode, CancellationToken ct)
		{
			const string sql = @"
SELECT TOP (1) *
FROM dbo.AutomaticGmail_ProcessedEmails
WHERE NewBookingCode = @Code AND EmailType = 'Modification'
ORDER BY COALESCE(ProcessingCompletedAt, ExtractedAt), Id;";
			using var conn = CreateConnection();
			return await conn.QueryFirstOrDefaultAsync<ProcessedEmailRecord>(new CommandDefinition(sql, new { Code = newCode }, cancellationToken: ct));
		}

		public async Task<IReadOnlyList<ProcessedEmailRecord>> GetProcessedByBookingCodesAsync(IReadOnlyList<string> codes, CancellationToken ct)
		{
			if (codes == null || codes.Count == 0) return Array.Empty<ProcessedEmailRecord>();
			const string sql = @"
SELECT *
FROM dbo.AutomaticGmail_ProcessedEmails
WHERE BookingCode IN @Codes;";
			using var conn = CreateConnection();
			var rows = await conn.QueryAsync<ProcessedEmailRecord>(new CommandDefinition(sql, new { Codes = codes }, cancellationToken: ct));
			return rows.ToList();
		}

		// 2025-12-01 00:00 UTC - Forward traversal helper
		public async Task<IReadOnlyList<ProcessedEmailRecord>> GetModificationsByPreviousCodeAsync(string previousCode, CancellationToken ct)
		{
			const string sql = @"
SELECT *
FROM dbo.AutomaticGmail_ProcessedEmails
WHERE EmailType = 'Modification'
  AND PreviousBookingCode = @PrevCode;";
			using var conn = CreateConnection();
			var rows = await conn.QueryAsync<ProcessedEmailRecord>(new CommandDefinition(sql, new { PrevCode = previousCode }, cancellationToken: ct));
			return rows.ToList();
		}

		// Added: 2025-12-01 00:00 UTC - Override (no COALESCE) pointer for repair
		public async Task<int> OverrideInboxOriginalBookingPointerAsync(int inboxEmailId, int? rootBookingId, string? rootBookingCode, string? rootMessageId, CancellationToken ct)
		{
			const string sql = @"
UPDATE dbo.AutomaticGmail_InboxEmails
SET OriginalBookingId = @OriginalBookingId,
    OriginalBookingCode = @OriginalBookingCode,
    OriginalBookingMessageId = @OriginalBookingMessageId,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @InboxEmailId;";
			using var conn = CreateConnection();
			return await conn.ExecuteAsync(new CommandDefinition(sql, new
			{
				InboxEmailId = inboxEmailId,
				OriginalBookingId = rootBookingId,
				OriginalBookingCode = rootBookingCode,
				OriginalBookingMessageId = rootMessageId
			}, cancellationToken: ct));
		}

		// Added: 2025-12-01 00:00 UTC - Repair batch helpers
		public async Task<IReadOnlyList<InboxEmailRecord>> GetInboxBatchNeedingOriginRebuildAsync(int lastIdExclusive, int batchSize, CancellationToken ct)
		{
			const string sql = @"
SELECT TOP (@BatchSize)
       Id, Uid, MessageId, Subject, FromEmail, FromName, ToEmail, ReceivedDate, TextBody, HtmlBody, TextBodyPreview,
       AttachmentCount, AttachmentNames, CollectedAt, CollectionBatchId, IsRead, CreatedAt, UpdatedAt, ProcessingStatus,
       OriginalBookingId, OriginalBookingCode, OriginalBookingMessageId
FROM dbo.AutomaticGmail_InboxEmails WITH (READPAST)
WHERE Id > @LastId
  AND (OriginalBookingId IS NULL OR OriginalBookingCode IS NULL OR OriginalBookingMessageId IS NULL)
ORDER BY Id ASC;";
			using var conn = CreateConnection();
			var rows = await conn.QueryAsync<InboxEmailRecord>(new CommandDefinition(sql, new { LastId = lastIdExclusive, BatchSize = batchSize }, cancellationToken: ct));
			return rows.ToList();
		}

		public async Task<string?> GetEffectiveBookingCodeForMessageAsync(string messageId, CancellationToken ct)
		{
			const string sql = @"
SELECT TOP (1) BookingCode
FROM dbo.AutomaticGmail_ProcessedEmails
WHERE MessageId = @MessageId
ORDER BY COALESCE(ProcessingCompletedAt, ExtractedAt) DESC, Id DESC;";
			using var conn = CreateConnection();
			return await conn.ExecuteScalarAsync<string?>(new CommandDefinition(sql, new { MessageId = messageId }, cancellationToken: ct));
		}

        public async Task<int> CreateInboxEmailAsync(InboxEmailRecord email, CancellationToken ct)
        {
            const string sql = @"
INSERT INTO dbo.AutomaticGmail_InboxEmails
(Uid, MessageId, Subject, FromEmail, FromName, ToEmail, ReceivedDate, TextBody, HtmlBody, TextBodyPreview, AttachmentCount, AttachmentNames, CollectedAt, CollectionBatchId, IsRead, CreatedAt, UpdatedAt, ProcessingStatus)
VALUES
(@Uid, @MessageId, @Subject, @FromEmail, @FromName, @ToEmail, @ReceivedDate, @TextBody, @HtmlBody, @TextBodyPreview, @AttachmentCount, @AttachmentNames, @CollectedAt, @CollectionBatchId, @IsRead, SYSUTCDATETIME(), SYSUTCDATETIME(), @ProcessingStatus);
SELECT CAST(SCOPE_IDENTITY() as int);";

            using var conn = CreateConnection();
            return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new
            {
                email.Uid,
                email.MessageId,
                email.Subject,
                email.FromEmail,
                email.FromName,
                email.ToEmail,
                email.ReceivedDate,
                email.TextBody,
                email.HtmlBody,
                email.TextBodyPreview,
                email.AttachmentCount,
                email.AttachmentNames,
                email.CollectedAt,
                email.CollectionBatchId,
                email.IsRead,
                ProcessingStatus = (short)email.ProcessingStatus
            }, cancellationToken: ct));
        }

		// 2025-12-02 00:00 UTC - Identify modifications with missing tour fields for reprocessing
		public async Task<IReadOnlyList<int>> GetProcessedIdsForModificationsMissingTourFieldsAsync(int limit, CancellationToken ct)
		{
			const string sql = @"
SELECT TOP (@Limit) Id
FROM dbo.AutomaticGmail_ProcessedEmails WITH (READPAST)
WHERE EmailType = 'Modification'
  AND (
        TourName IS NULL OR LTRIM(RTRIM(TourName)) = '' OR
        TourDate IS NULL OR LTRIM(RTRIM(TourDate)) = '' OR
        TourTime IS NULL OR LTRIM(RTRIM(TourTime)) = '' OR
        TourLocation IS NULL OR LTRIM(RTRIM(TourLocation)) = '' OR
        Language IS NULL OR LTRIM(RTRIM(Language)) = ''
      )
ORDER BY ProcessingCompletedAt DESC, Id DESC;";
			using var conn = CreateConnection();
			var rows = await conn.QueryAsync<int>(new CommandDefinition(sql, new { Limit = limit }, cancellationToken: ct));
			return rows.ToList();
		}
    }
}


