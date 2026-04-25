using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Email.Services.GmailProcessing.Dtos;
using Email.Services.GmailProcessing.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Email.Services.GmailProcessing.Repositories
{
    /// <summary>
    /// Created: 2025-11-07 00:00 UTC
    /// Updated: 2025-11-26 00:00 UTC - Added new fields for booking code tracking and content storage
    /// SQL Server implementation for processed emails repository.
    /// </summary>
    public sealed class SqlServerProcessedEmailRepository : IProcessedEmailRepository
    {
        private readonly string _connectionString;

        public SqlServerProcessedEmailRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("AutomaticGmailSqlServer") ?? throw new InvalidOperationException("Missing ConnectionStrings:AutomaticGmailSqlServer");
        }

        private IDbConnection CreateConnection() => new SqlConnection(_connectionString);

        public async Task<int> UpsertProcessedEmailAsync(ProcessedEmailRecord rec, CancellationToken cancellationToken)
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
    PlainTextContent = @PlainTextContent,
    HtmlContent = @HtmlContent,
    ExtractedBookingCode = @ExtractedBookingCode,
    NewBookingCode = @NewBookingCode,
    PreviousBookingCode = @PreviousBookingCode,
    IsCancellation = @IsCancellation,
    IsModification = @IsModification,
    IsBooking = @IsBooking,
    NumberOfAdults = @NumberOfAdults,
    NumberOfChildren = @NumberOfChildren,
    UpdatedAt = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
  INSERT (InboxEmailId, MessageId, VendorName, EmailType, IsTourBookingEmail, ClassificationRuleId,
          ProcessingStatus, ProcessingStartedAt, ProcessingCompletedAt, ProcessingError, ProcessingAttempts,
          NextProcessingAttempt, RateLimitResetAt, CustomerName, BookingCode, CustomerPhone, CustomerEmail,
          NumberOfAttendees, Language, TourDate, TourTime, TourName, TourLocation, ActionRequired, ExtractedAt,
          CustomerIdentifier, RelatedEmailIds, IsLatestAction,
          PlainTextContent, HtmlContent, ExtractedBookingCode, NewBookingCode, PreviousBookingCode,
          IsCancellation, IsModification, IsBooking, NumberOfAdults, NumberOfChildren,
          CreatedAt, UpdatedAt)
  VALUES (@InboxEmailId, @MessageId, @VendorName, @EmailType, @IsTourBookingEmail, @ClassificationRuleId,
          @ProcessingStatus, @ProcessingStartedAt, @ProcessingCompletedAt, @ProcessingError, @ProcessingAttempts,
          @NextProcessingAttempt, @RateLimitResetAt, @CustomerName, @BookingCode, @CustomerPhone, @CustomerEmail,
          @NumberOfAttendees, @Language, @TourDate, @TourTime, @TourName, @TourLocation, @ActionRequired, @ExtractedAt,
          @CustomerIdentifier, @RelatedEmailIds, @IsLatestAction,
          @PlainTextContent, @HtmlContent, @ExtractedBookingCode, @NewBookingCode, @PreviousBookingCode,
          @IsCancellation, @IsModification, @IsBooking, @NumberOfAdults, @NumberOfChildren,
          SYSUTCDATETIME(), SYSUTCDATETIME())
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
                rec.IsLatestAction,
                rec.PlainTextContent,
                rec.HtmlContent,
                rec.ExtractedBookingCode,
                rec.NewBookingCode,
                rec.PreviousBookingCode,
                rec.IsCancellation,
                rec.IsModification,
                rec.IsBooking,
                rec.NumberOfAdults,
                rec.NumberOfChildren
            }, cancellationToken: cancellationToken));
            return newId;
        }

        public async Task SetLatestActionForThreadAsync(string vendorName, string? bookingCode, int processedEmailId, CancellationToken cancellationToken)
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
            }, cancellationToken: cancellationToken));
        }

        public async Task<ProcessedEmailDisplayDto?> GetProcessedEmailByIdAsync(int processedId, CancellationToken cancellationToken)
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
                new CommandDefinition(sql, new { Id = processedId }, cancellationToken: cancellationToken));
        }

        public async Task DeleteAsync(IReadOnlyList<int> processedIds, CancellationToken cancellationToken)
        {
            if (processedIds == null || processedIds.Count == 0) return;
            const string sql = @"DELETE FROM dbo.AutomaticGmail_ProcessedEmails WHERE Id IN @Ids";
            using var conn = CreateConnection();
            await conn.ExecuteAsync(new CommandDefinition(sql, new { Ids = processedIds }, cancellationToken: cancellationToken));
        }
    }
}


