using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Email.Models;
using Email.TourTreeViewShapedData;
using Email.TourTreeViewShapedData.Enums;
using Email.TourTreeViewShapedData.Models;
using Email.Calendar.Services; // Added for Calendar Sync
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Email.Services
{
    // Created: 2025-11-16 00:00 UTC - SQL Server shaped tree service
    // Updated: 2025-11-18 - Return ShapedTreeData directly (no lossy conversion)
    // Updated: 2025-11-19 00:00 UTC - Added commented interface stubs and future API method skeletons (do not enable yet)
    public interface ITourTreeService
    {
        Task<ShapedTreeData> GetTourTreeDataShapedAsync(TreeDataFilterType filterType = TreeDataFilterType.ConfirmationsOnly, DateTime? startDate = null, DateTime? endDate = null, string? vendor = null);

        // 2025-11-21 00:00 UTC - SQL methods for Edit Booking Details
        Task<ProcessedEmail?> GetProcessedEmailByMessageIdAsync(string messageId);
        Task<List<string>> GetDistinctTourNamesAsync();
        Task<bool> UpdateProcessedEmailAndBookingAsync(ProcessedEmail processedEmail);

        /*
        // 2025-11-19 00:00 UTC - FUTURE API INTERFACE (commented; enable when API is ready)
        Task<TreeData> GetTourTreeDataAsync(DateTime? selectedDate = null, string? vendorName = null, bool guruWalkOnly = true, bool loadAllData = false, bool bypassCache = false);
        Task<List<TreeBookingExtractionResult>> ExtractBookingsFromEmailsAsync(List<object> emails);
        TreeBookingExtractionResult ExtractBookingFromTextBody(string textBody, string vendorName);
        Task<List<object>> GetEmailsByVendorAsync(string vendorName, bool guruWalkOnly = true, bool loadAllData = false, int pageSize = 0);
        Task<ProcessedEmail?> GetProcessedEmailByMessageIdAsync(string messageId);
        Task<List<string>> GetDistinctTourNamesAsync();
        Task<bool> UpdateProcessedEmailAndBookingAsync(ProcessedEmail processedEmail);
        */
    }

    public class TourTreeService : ITourTreeService
    {
        private readonly TourTreeDataProviderSqlServer _provider;
        private readonly string _connectionString;
        private readonly IGoogleCalendarService _googleCalendarService; // Added 2025-12-21

        /*
        // 2025-11-19 00:00 UTC - FUTURE API FIELDS (commented; enable when API is ready)
        private readonly HttpClient _httpClient;
        private readonly ILogger<TourTreeService> _logger;
        private readonly string _apiBaseUrl = string.Empty;
        */

        public TourTreeService(IConfiguration configuration, IGoogleCalendarService googleCalendarService, ITourNameNormalizer tourNameNormalizer)
        {
            var connectionString = configuration.GetConnectionString("AutomaticGmailSqlServer")
                ?? throw new InvalidOperationException("ConnectionStrings:AutomaticGmailSqlServer is not configured.");
            _provider = new TourTreeDataProviderSqlServer(connectionString, tourNameNormalizer);
            _connectionString = connectionString;
            _googleCalendarService = googleCalendarService;
        }

        /*
        // 2025-11-19 00:00 UTC - FUTURE API CONSTRUCTOR (commented; enable when API is ready)
        public TourTreeService(HttpClient httpClient, ILogger<TourTreeService> logger, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _logger = logger;
            // Example: configure base URL from appsettings.json: { "Api": { "BaseUrl": "https://localhost:5001" } }
            _apiBaseUrl = (configuration["Api:BaseUrl"] ?? string.Empty).TrimEnd('/');
        }
        */

        // 2025-11-18: Return ShapedTreeData directly to preserve status colors (WalkerData.StatusClass)
        // Modified: 2026-02-11 - Updated to use async GetShapedTreeDataAsync for DB-driven master tour name resolution
        public async Task<ShapedTreeData> GetTourTreeDataShapedAsync(TreeDataFilterType filterType = TreeDataFilterType.ConfirmationsOnly, DateTime? startDate = null, DateTime? endDate = null, string? vendor = null)
        {
            // 2025-11-17: Console trace for shaped tree request
            try { Console.WriteLine($"[TourTreeService] GetTourTreeDataShapedAsync start: FilterType={filterType}, Start={startDate:yyyy-MM-dd}, End={endDate:yyyy-MM-dd}, Vendor={vendor ?? "(null)"}"); } catch { }
            var shaped = await _provider.GetShapedTreeDataAsync(filterType, startDate, endDate, vendor);
            try { Console.WriteLine($"[TourTreeService] Shaped data received. DateNodes={shaped?.TreeNodes?.Count ?? 0}"); } catch { }
            return shaped;
        }

        private IDbConnection CreateConnection() => new SqlConnection(_connectionString);

        // 2025-11-21 00:00 UTC - Get processed email by MessageId (TRY_CONVERT TourDate)
        public async Task<ProcessedEmail?> GetProcessedEmailByMessageIdAsync(string messageId)
        {
            const string sql = @"
SELECT TOP 1 
    p.Id, p.InboxEmailId, p.MessageId, p.VendorName, p.VendorManuallyOverridden, p.VendorOverrideAt, p.EmailType, p.IsTourBookingEmail,
    p.ClassificationRuleId, p.ProcessingStatus, p.ProcessingStartedAt, p.ProcessingCompletedAt,
    p.ProcessingError, p.ProcessingAttempts, p.NextProcessingAttempt, p.RateLimitResetAt,
    p.CustomerName, p.BookingCode, p.CustomerPhone, p.CustomerEmail, p.NumberOfAttendees,
    p.NumberOfAdults, p.NumberOfChildren, p.Language, p.TourName,
    TRY_CONVERT(datetime2, p.TourDate) AS TourDate,
    p.TourTime, p.TourLocation, p.CustomerIdentifier, p.IsLatestAction, p.PlainTextContent,
    p.HtmlContent, p.BookingAlterationNotes, p.ExtractedBookingCode, p.NewBookingCode, p.PreviousBookingCode,
    p.ExtractedAt, p.ManualParsingCompleted, p.ManualParsingNotes, p.ManualParsingTimestamp,
    p.CreatedAt, p.UpdatedAt, p.IsCancellation, p.IsModification, p.IsBooking,
    b.CalendarEventId
FROM dbo.AutomaticGmail_ProcessedEmails p
LEFT JOIN dbo.Bookings b ON p.Id = b.ProcessedEmailId
WHERE p.MessageId = @MessageId";
            using var conn = CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<ProcessedEmail>(sql, new { MessageId = messageId });
        }

        // 2025-11-21 00:00 UTC - Distinct active tour names
        public async Task<List<string>> GetDistinctTourNamesAsync()
        {
            const string sql = @"
SELECT DISTINCT TourName 
FROM dbo.Tours
WHERE IsActive = 1 AND TourName IS NOT NULL
ORDER BY TourName";
            using var conn = CreateConnection();
            var rows = await conn.QueryAsync<string>(sql);
            return rows.ToList();
        }

        // 2025-11-21 00:00 UTC - Update ProcessedEmail (nvarchar TourDate) + Booking (datetime2 TourDate)
        public async Task<bool> UpdateProcessedEmailAndBookingAsync(ProcessedEmail processedEmail)
        {
            using var conn = CreateConnection();
            if (conn is SqlConnection sqlConn)
            {
                await sqlConn.OpenAsync();
                using var tx = sqlConn.BeginTransaction();

                try
                {
                    const string scopeSql = @"
SELECT TOP 1 Id, MessageId, BookingCode, VendorName
FROM dbo.AutomaticGmail_ProcessedEmails
WHERE Id = @Id;";

                    var scopeRow = await conn.QueryFirstOrDefaultAsync<EditScopeRow>(scopeSql, new
                    {
                        processedEmail.Id
                    }, tx);

                    if (scopeRow == null)
                    {
                        return false;
                    }

                    var scopeMessageId = FirstNonEmpty(scopeRow.MessageId, processedEmail.MessageId);
                    var scopeBookingCode = FirstNonEmpty(scopeRow.BookingCode, processedEmail.BookingCode);
                    var originalVendor = NormalizeVendor(scopeRow.VendorName);
                    var requestedVendor = NormalizeVendor(processedEmail.VendorName);
                    var isWalkUpSource = string.Equals(originalVendor, "Walk-Up", StringComparison.OrdinalIgnoreCase);
                    var vendorChanged = !string.IsNullOrWhiteSpace(requestedVendor) &&
                                        !string.Equals(originalVendor, requestedVendor, StringComparison.OrdinalIgnoreCase);

                    if (isWalkUpSource)
                    {
                        processedEmail.VendorName = originalVendor;
                    }
                    else if (!string.IsNullOrWhiteSpace(requestedVendor))
                    {
                        processedEmail.VendorName = requestedVendor;
                    }
                    else
                    {
                        processedEmail.VendorName = originalVendor;
                    }

                    var tourDateString = processedEmail.TourDate.HasValue
                        ? processedEmail.TourDate.Value.ToString("yyyy-MM-dd")
                        : null;

                    const string updateProcessedSql = @"
UPDATE dbo.AutomaticGmail_ProcessedEmails
SET CustomerName = @CustomerName,
    CustomerEmail = @CustomerEmail,
    CustomerPhone = @CustomerPhone,
    NumberOfAttendees = @NumberOfAttendees,
    NumberOfAdults = @NumberOfAdults,
    NumberOfChildren = @NumberOfChildren,
    Language = @Language,
    TourName = @TourName,
    TourDate = @TourDateString,
    TourTime = @TourTime,
    TourLocation = @TourLocation,
    EmailType = @EmailType,
    BookingAlterationNotes = @BookingAlterationNotes,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id";

                    var processedRows = await conn.ExecuteAsync(updateProcessedSql, new
                    {
                        processedEmail.CustomerName,
                        processedEmail.CustomerEmail,
                        processedEmail.CustomerPhone,
                        processedEmail.NumberOfAttendees,
                        processedEmail.NumberOfAdults,
                        processedEmail.NumberOfChildren,
                        processedEmail.Language,
                        processedEmail.TourName,
                        TourDateString = tourDateString,
                        processedEmail.TourTime,
                        processedEmail.TourLocation,
                        processedEmail.EmailType,
                        BookingAlterationNotes = processedEmail.BookingAlterationNotes,
                        processedEmail.Id
                    }, tx);

                    const string updateBookingSql = @"
UPDATE dbo.Bookings
SET CustomerName = @CustomerName,
    CustomerEmail = @CustomerEmail,
    CustomerPhone = @CustomerPhone,
    NumberOfAttendees = @NumberOfAttendees,
    NumberOfAdults = @NumberOfAdults,
    NumberOfChildren = @NumberOfChildren,
    Language = @Language,
    TourName = @TourName,
    TourDate = @TourDate,
    TourTime = @TourTime,
    TourLocation = @TourLocation,
    IsCancellation = CASE WHEN LOWER(ISNULL(@EmailType, '')) = 'cancellation' THEN 1 ELSE 0 END,
    IsActive = CASE WHEN LOWER(ISNULL(@EmailType, '')) = 'cancellation' THEN 0 ELSE 1 END,
    UpdatedAt = SYSUTCDATETIME()
WHERE ProcessedEmailId = @ProcessedEmailId
   OR (MessageId = @ScopeMessageId AND @ScopeMessageId IS NOT NULL)
   OR (BookingCode IS NOT NULL AND BookingCode = @ScopeBookingCode AND @ScopeBookingCode IS NOT NULL)";

                    var bookingRows = await conn.ExecuteAsync(updateBookingSql, new
                    {
                        processedEmail.CustomerName,
                        processedEmail.CustomerEmail,
                        processedEmail.CustomerPhone,
                        processedEmail.NumberOfAttendees,
                        processedEmail.NumberOfAdults,
                        processedEmail.NumberOfChildren,
                        processedEmail.Language,
                        processedEmail.TourName,
                        TourDate = processedEmail.TourDate,
                        processedEmail.TourTime,
                        processedEmail.TourLocation,
                        processedEmail.EmailType,
                        ProcessedEmailId = processedEmail.Id,
                        ScopeMessageId = scopeMessageId,
                        ScopeBookingCode = scopeBookingCode
                    }, tx);

                    if (bookingRows == 0)
                    {
                        Console.WriteLine($"[TourTreeService] UpdateProcessedEmailAndBookingAsync: No Bookings row matched for ProcessedEmailId={processedEmail.Id}, MessageId={scopeMessageId}, BookingCode={scopeBookingCode}");
                    }

                    if (vendorChanged && !isWalkUpSource)
                    {
                        const string updateProcessedVendorSql = @"
UPDATE dbo.AutomaticGmail_ProcessedEmails
SET VendorName = @VendorName,
    VendorManuallyOverridden = 1,
    VendorOverrideAt = SYSUTCDATETIME(),
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @ProcessedEmailId
   OR (MessageId = @ScopeMessageId AND @ScopeMessageId IS NOT NULL)
   OR (BookingCode IS NOT NULL AND BookingCode = @ScopeBookingCode AND @ScopeBookingCode IS NOT NULL);";

                        await conn.ExecuteAsync(updateProcessedVendorSql, new
                        {
                            VendorName = processedEmail.VendorName,
                            ProcessedEmailId = processedEmail.Id,
                            ScopeMessageId = scopeMessageId,
                            ScopeBookingCode = scopeBookingCode
                        }, tx);

                        const string updateBookingVendorSql = @"
UPDATE dbo.Bookings
SET VendorName = @VendorName,
    VendorManuallyOverridden = 1,
    VendorOverrideAt = SYSUTCDATETIME(),
    UpdatedAt = SYSUTCDATETIME()
WHERE ProcessedEmailId = @ProcessedEmailId
   OR (MessageId = @ScopeMessageId AND @ScopeMessageId IS NOT NULL)
   OR (BookingCode IS NOT NULL AND BookingCode = @ScopeBookingCode AND @ScopeBookingCode IS NOT NULL);";

                        await conn.ExecuteAsync(updateBookingVendorSql, new
                        {
                            VendorName = processedEmail.VendorName,
                            ProcessedEmailId = processedEmail.Id,
                            ScopeMessageId = scopeMessageId,
                            ScopeBookingCode = scopeBookingCode
                        }, tx);

                        const string updateBookingMessageStatusVendorSql = @"
UPDATE dbo.BookingMessageStatus
SET VendorName = @VendorName,
    UpdatedAt = SYSUTCDATETIME()
WHERE (MessageId = @ScopeMessageId AND @ScopeMessageId IS NOT NULL)
   OR (BookingCode = @ScopeBookingCode AND @ScopeBookingCode IS NOT NULL);";

                        await conn.ExecuteAsync(updateBookingMessageStatusVendorSql, new
                        {
                            VendorName = processedEmail.VendorName,
                            ScopeMessageId = scopeMessageId,
                            ScopeBookingCode = scopeBookingCode
                        }, tx);
                    }

                    if (tx is SqlTransaction sqlTx)
                    {
                        await sqlTx.CommitAsync();
                    }

                    // 2025-12-21 00:30 UTC - Automatic Google Calendar Sync on Edit
                    if (processedRows > 0 && bookingRows > 0 && processedEmail != null && !string.IsNullOrEmpty(processedEmail.CalendarEventId))
                    {
                        try
                        {
                            var evt = MapProcessedEmailToGoogleEvent(processedEmail);
                            evt.Id = processedEmail.CalendarEventId;
                            await _googleCalendarService.UpdateEventAsync(evt);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[TourTreeService] Failed to update Google Calendar: {ex.Message}");
                        }
                    }

                    // ProcessedEmails update is authoritative; Bookings update is best-effort.
                    // A missing Booking row (bookingRows == 0) should not block saving the edit.
                    return processedRows > 0;
                }
                catch (Exception ex)
                {
                    System.IO.File.AppendAllText(@"c:\Users\daves\source\repos\WhatsAppBusiness\Email\tmp_error.log", $"{DateTime.Now}: {ex}\n");
                    if (tx is SqlTransaction sqlTx)
                    {
                        try { await sqlTx.RollbackAsync(); } catch { }
                    }
                    return false;
                }
            }
            return false;
        }

        /*
        // 2025-11-19 00:00 UTC - FUTURE API METHODS (commented; enable when API is ready)
        public async Task<TreeData> GetTourTreeDataAsync(DateTime? selectedDate = null, string? vendorName = null, bool guruWalkOnly = true, bool loadAllData = false, bool bypassCache = false)
        {
            // placeholder for future API call
            await Task.CompletedTask;
            return new TreeData();
        }

        public async Task<List<TreeBookingExtractionResult>> ExtractBookingsFromEmailsAsync(List<object> emails)
        {
            await Task.CompletedTask;
            return new List<TreeBookingExtractionResult>();
        }

        public TreeBookingExtractionResult ExtractBookingFromTextBody(string textBody, string vendorName)
        {
            return new TreeBookingExtractionResult { VendorName = vendorName, IsValid = false };
        }

        public async Task<List<object>> GetEmailsByVendorAsync(string vendorName, bool guruWalkOnly = true, bool loadAllData = false, int pageSize = 0)
        {
            await Task.CompletedTask;
            return new List<object>();
        }

        public async Task<ProcessedEmail?> GetProcessedEmailByMessageIdAsync(string messageId)
        {
            await Task.CompletedTask;
            return null;
        }

        public async Task<List<string>> GetDistinctTourNamesAsync()
        {
            await Task.CompletedTask;
            return new List<string>();
        }

        public async Task<bool> UpdateProcessedEmailAndBookingAsync(ProcessedEmail processedEmail)
        {
            await Task.CompletedTask;
            return false;
        }
        */

        // DEPRECATED 2025-11-18: Legacy conversion loses status colors - use ShapedTreeData directly
        private static TreeData ConvertShapedToLegacy(ShapedTreeData shaped)
        {
            var result = new TreeData();
            if (shaped?.TreeNodes == null)
            {
                return result;
            }

            foreach (var dateNode in shaped.TreeNodes)
            {
                var legacyDate = new TreeNode
                {
                    Label = dateNode.Label,
                    Icon = dateNode.Icon,
                    IsExpanded = dateNode.IsExpanded,
                    DailyGuestCount = dateNode.DailyGuestCount
                };

                foreach (var tourNode in dateNode.Children ?? Enumerable.Empty<ShapedTreeNode>())
                {
                    var legacyTour = new TreeNode
                    {
                        Label = tourNode.Label,
                        Icon = tourNode.Icon,
                        IsExpanded = tourNode.IsExpanded
                    };

                    foreach (var vendorNode in tourNode.Children ?? Enumerable.Empty<ShapedTreeNode>())
                    {
                        var legacyVendor = new TreeNode
                        {
                            Label = vendorNode.Label,
                            Icon = vendorNode.Icon,
                            IsExpanded = vendorNode.IsExpanded
                        };

                        foreach (var walkerNode in vendorNode.Children ?? Enumerable.Empty<ShapedTreeNode>())
                        {
                            legacyVendor.Children.Add(new TreeNode
                            {
                                Label = walkerNode.Label,
                                Icon = walkerNode.Icon,
                                IsExpanded = walkerNode.IsExpanded,
                            Data = walkerNode.WalkerData != null ? JsonSerializer.Serialize(walkerNode.WalkerData) : null
                            });
                        }

                        legacyTour.Children.Add(legacyVendor);
                    }

                    legacyDate.Children.Add(legacyTour);
                }

                result.TreeNodes.Add(legacyDate);
            }

            result.TotalBookings = shaped.TotalBookings;
            result.TotalWalkers = shaped.TotalWalkers;
            result.VendorCounts = shaped.VendorCounts ?? result.VendorCounts;
            return result;
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return null;
        }

        private static string NormalizeVendor(string? vendorName)
        {
            return (vendorName ?? string.Empty).Trim();
        }

        private sealed class EditScopeRow
        {
            public int Id { get; set; }
            public string? MessageId { get; set; }
            public string? BookingCode { get; set; }
            public string? VendorName { get; set; }
        }

        // 2025-12-21 - Helper for Calendar Sync
        private Email.Calendar.Models.GoogleAppointmentModel MapProcessedEmailToGoogleEvent(ProcessedEmail p)
        {
            var evt = new Email.Calendar.Models.GoogleAppointmentModel
            {
                Subject = p.TourName ?? "Tour Booking",
                Description = $"Booking Code: {p.BookingCode}\n" +
                              $"Customer: {p.CustomerName}\n" +
                              $"Pax: {p.NumberOfAttendees}\n" +
                              $"Phone: {p.CustomerPhone}\n" +
                              $"Source: {p.VendorName}\n" +
                              $"\nNotes: {p.BookingAlterationNotes}",
                Location = p.TourLocation ?? "TBD"
            };

            // Parse Date/Time
            var date = p.TourDate ?? DateTime.UtcNow.Date.AddDays(1);
            TimeSpan time;
            if (!TimeSpan.TryParse(p.TourTime, out time))
            {
                time = new TimeSpan(9, 0, 0);
            }
            var startDateTime = date.Date.Add(time);
            evt.StartTime = startDateTime;
            evt.EndTime = startDateTime.AddHours(2);

            return evt;
        }
    }
}

