using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient; // NOTE: Microsoft.Data.SqlClient package is present in Email.csproj
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Email.Services.GmailCollection.Models;

namespace Email.Services.GmailCollection
{
    /// <summary>
    /// Created: 2025-11-04 00:00 UTC
    /// Migrated: 2025-11-21 00:00 UTC - from CityShufflesGuides to Email (verbatim)
    /// SQL Server repository for Gmail v2 collection (inbox + watermark).
    /// </summary>
    public interface IGmailCollectionRepository
    {
        Task<GmailWatermarkRecord?> GetWatermarkAsync(string mailbox, CancellationToken ct);
        Task UpsertWatermarkAsync(GmailWatermarkRecord rec, CancellationToken ct);
        Task ResetWatermarkAsync(string mailbox, CancellationToken ct);
        Task<int> UpsertInboxEmailAsync(InboxEmailRecord rec, CancellationToken ct);
        Task<long> GetInboxTotalCountAsync(CancellationToken ct);
        Task<DateTime?> GetNewestReceivedDateAsync(CancellationToken ct);
        Task DeleteAllInboxEmailsAsync(CancellationToken ct);
        Task ClearAllTourDataAndResetWatermarkAsync(CancellationToken ct);
    }

    public sealed class SqlServerGmailCollectionRepository : IGmailCollectionRepository
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<SqlServerGmailCollectionRepository> _logger;
        private readonly string _connectionString;

        public SqlServerGmailCollectionRepository(IConfiguration configuration, ILogger<SqlServerGmailCollectionRepository> logger)
        {
            _configuration = configuration;
            _logger = logger;
            // Updated: 2025-11-21 00:00 UTC - Use Email project's existing connection string key
            _connectionString = _configuration.GetConnectionString("AutomaticGmailSqlServer")
                ?? throw new InvalidOperationException("ConnectionStrings:AutomaticGmailSqlServer is not configured.");
        }

        private IDbConnection CreateConnection()
        {
            return new SqlConnection(_connectionString);
        }

        public async Task<GmailWatermarkRecord?> GetWatermarkAsync(string mailbox, CancellationToken ct)
        {
            const string sql = @"SELECT TOP 1 Id, Mailbox, UidValidity, LastSeenUid, LastScanStartedAt, LastScanCompletedAt, UpdatedAt
                                 FROM dbo.AutomaticGmail_InboxWatermark WHERE Mailbox = @Mailbox";
            using var conn = CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<GmailWatermarkRecord>(new CommandDefinition(sql, new { Mailbox = mailbox }, cancellationToken: ct));
        }

        public async Task UpsertWatermarkAsync(GmailWatermarkRecord rec, CancellationToken ct)
        {
            const string merge = @"
MERGE dbo.AutomaticGmail_InboxWatermark AS t
USING (SELECT @Mailbox AS Mailbox) AS s
ON (t.Mailbox = s.Mailbox)
WHEN MATCHED THEN
  UPDATE SET UidValidity=@UidValidity, LastSeenUid=@LastSeenUid, LastScanStartedAt=@LastScanStartedAt,
             LastScanCompletedAt=@LastScanCompletedAt, UpdatedAt=GETUTCDATE()
WHEN NOT MATCHED THEN
  INSERT (Mailbox, UidValidity, LastSeenUid, LastScanStartedAt, LastScanCompletedAt, UpdatedAt)
  VALUES (@Mailbox, @UidValidity, @LastSeenUid, @LastScanStartedAt, @LastScanCompletedAt, GETUTCDATE());";

            using var conn = CreateConnection();
            await conn.ExecuteAsync(new CommandDefinition(merge, new
            {
                rec.Mailbox,
                rec.UidValidity,
                rec.LastSeenUid,
                rec.LastScanStartedAt,
                rec.LastScanCompletedAt
            }, cancellationToken: ct));
        }

        public async Task ResetWatermarkAsync(string mailbox, CancellationToken ct)
        {
            const string sql = @"UPDATE dbo.AutomaticGmail_InboxWatermark SET LastSeenUid = 0, UpdatedAt = SYSUTCDATETIME() WHERE Mailbox = @Mailbox";
            using var conn = CreateConnection();
            await conn.ExecuteAsync(new CommandDefinition(sql, new { Mailbox = mailbox }, cancellationToken: ct));
        }

        //todo important!  review code additions because we are going to have issue with FKs.
        //More DA has been added after this method created, it needs to handle our new additions
        public async Task DeleteAllInboxEmailsAsync(CancellationToken ct)
        {
            // Updated: 2025-01-XX - Delete ProcessedEmails first due to FK constraint, then InboxEmails
            const string sql = @"
                DELETE FROM dbo.AutomaticGmail_ProcessedEmails;
                DELETE FROM dbo.AutomaticGmail_InboxEmails;";
            using var conn = CreateConnection();
            await conn.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct));
        }

        public async Task ClearAllTourDataAndResetWatermarkAsync(CancellationToken ct)
        {
            // Created: 2025-11-27 00:00 UTC
            // Deletes all tour-related data (Bookings, Customers, Processed, Inbox) then resets watermark.
            using var conn = CreateConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();
            try
            {
                // Order matters due to FK relationships: Bookings -> Customers -> Processed -> Inbox -> Watermark
                await conn.ExecuteAsync(new CommandDefinition("DELETE FROM dbo.Bookings;", transaction: tran, cancellationToken: ct));
                await conn.ExecuteAsync(new CommandDefinition("DELETE FROM dbo.Customers;", transaction: tran, cancellationToken: ct));
                await conn.ExecuteAsync(new CommandDefinition("DELETE FROM dbo.AutomaticGmail_ProcessedEmails;", transaction: tran, cancellationToken: ct));
                await conn.ExecuteAsync(new CommandDefinition("DELETE FROM dbo.AutomaticGmail_InboxEmails;", transaction: tran, cancellationToken: ct));
                await conn.ExecuteAsync(new CommandDefinition("UPDATE dbo.AutomaticGmail_InboxWatermark SET LastSeenUid = 0, UpdatedAt = SYSUTCDATETIME() WHERE Mailbox = 'INBOX';", transaction: tran, cancellationToken: ct));

                tran.Commit();
            }
            catch
            {
                try { tran.Rollback(); } catch { }
                throw;
            }
        }


        //todo important!  We need to log this and handle any errors
        public async Task<int> UpsertInboxEmailAsync(InboxEmailRecord rec, CancellationToken ct)
        {
            const string merge = @"
MERGE dbo.AutomaticGmail_InboxEmails AS t
USING (SELECT @MessageId AS MessageId) AS s
ON (t.MessageId = s.MessageId)
WHEN MATCHED THEN
  UPDATE SET Subject=@Subject, FromEmail=@FromEmail, FromName=@FromName, ToEmail=@ToEmail,
             ReceivedDate=@ReceivedDate, TextBody=@TextBody, HtmlBody=@HtmlBody,
             TextBodyPreview=@TextBodyPreview, AttachmentCount=@AttachmentCount, AttachmentNames=@AttachmentNames,
             Uid=@Uid, CollectionBatchId=@CollectionBatchId, IsRead=@IsRead, UpdatedAt=GETUTCDATE()
WHEN NOT MATCHED THEN
  INSERT (Uid, MessageId, Subject, FromEmail, FromName, ToEmail, ReceivedDate, TextBody, HtmlBody,
          TextBodyPreview, AttachmentCount, AttachmentNames, CollectedAt, CollectionBatchId, IsRead, ProcessingStatus,
          CreatedAt, UpdatedAt)
  VALUES (@Uid, @MessageId, @Subject, @FromEmail, @FromName, @ToEmail, @ReceivedDate, @TextBody, @HtmlBody,
          @TextBodyPreview, @AttachmentCount, @AttachmentNames, GETUTCDATE(), @CollectionBatchId, @IsRead, @ProcessingStatus,
          GETUTCDATE(), GETUTCDATE());";

            using var conn = CreateConnection();
            // Dapper returns affected rows; treat >=1 as success
            var affected = await conn.ExecuteAsync(new CommandDefinition(merge, new
            {
                rec.Uid,
                rec.MessageId,
                rec.Subject,
                rec.FromEmail,
                rec.FromName,
                rec.ToEmail,
                rec.ReceivedDate,
                rec.TextBody,
                rec.HtmlBody,
                rec.TextBodyPreview,
                rec.AttachmentCount,
                rec.AttachmentNames,
                rec.CollectionBatchId,
                rec.IsRead,
                ProcessingStatus = (short)rec.ProcessingStatus
            }, cancellationToken: ct));
            return affected;
        }

        public async Task<long> GetInboxTotalCountAsync(CancellationToken ct)
        {
            const string sql = "SELECT COUNT_BIG(*) FROM dbo.AutomaticGmail_InboxEmails";
            using var conn = CreateConnection();
            return await conn.ExecuteScalarAsync<long>(new CommandDefinition(sql, cancellationToken: ct));
        }

        public async Task<DateTime?> GetNewestReceivedDateAsync(CancellationToken ct)
        {
            const string sql = "SELECT TOP 1 ReceivedDate FROM dbo.AutomaticGmail_InboxEmails ORDER BY ReceivedDate DESC";
            using var conn = CreateConnection();
            return await conn.ExecuteScalarAsync<DateTime?>(new CommandDefinition(sql, cancellationToken: ct));
        }
    }
}


