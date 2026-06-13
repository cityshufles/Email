using System;
using System.Collections.Generic;
using System.Data;
using Dapper;
using System.Threading.Tasks;
using System.Linq;
using Email.Models.Reports;
using Email.Models;
using Microsoft.Data.SqlClient;

namespace Email.Services
{
    /// <summary>
    /// Created: 1/28/2026 5:10 PM
    /// Edited: 1/28/2026 10:20 PM - Added IsCheckedIn, DoNotContact persistence
    /// Service for managing Guide Reports (TourReports table + Bookings reviews).
    /// </summary>
    public interface IGuideReportService
    {
        Task<TourReport> GetReportAsync(DateTime date, string tourName, string tourTime);
        Task<TourReport> GetReportByPublicIdAsync(string publicId);
        Task SaveReportAsync(TourReport report);
        Task<int> UpsertReportImagePathsAsync(DateTime tourDate, string tourName, string tourTime, string? imagePaths);
        Task<int> AddWalkUpBookingAsync(GuideReportWalker walker, DateTime date, string tourName, string tourTime);
        Task UpdateWalkUpBookingAsync(GuideReportWalker walker);
        Task DeleteWalkUpBookingAsync(int bookingId);
        Task<List<TourReportSummary>> GetAllReportsAsync(DateTime? startDate = null, DateTime? endDate = null);
        Task UpdateBookingAttendeesAsync(int bookingId, int actualAdults, int actualChildren);
        Task UpdateBookingGuideStateAsync(int bookingId, string? reviewStatus, string? reviewNotes, bool isCheckedIn, bool doNotContact);
        Task UpdateBookingPhoneAsync(int bookingId, string phone);
        Task UpdateBookingContactAsync(int bookingId, string name, string phone, string email);
    }

    public class GuideReportService : IGuideReportService
    {
        private readonly SqlConnectionFactory _sqlConnectionFactory;
        private readonly ITourNameNormalizer _tourNameNormalizer;

        public GuideReportService(SqlConnectionFactory sqlConnectionFactory, ITourNameNormalizer tourNameNormalizer)
        {
            _sqlConnectionFactory = sqlConnectionFactory;
            _tourNameNormalizer = tourNameNormalizer;
        }

        private IDbConnection CreateConnection() => _sqlConnectionFactory.CreateOpenConnection();

        public async Task<int> AddWalkUpBookingAsync(GuideReportWalker walker, DateTime date, string tourName, string tourTime)
        {
            Console.WriteLine($"[{DateTime.UtcNow:O}] AddWalkUpBookingAsync called for {walker.CustomerName} on {date:d}");
            using var conn = CreateConnection();
            using var tx = conn.BeginTransaction();
            
            try
            {
                var now = DateTime.UtcNow;
                var safeTourName = (tourName ?? string.Empty).Trim();
                var safeTourTime = (tourTime ?? string.Empty).Trim();
                var messageId = $"WALKUP-{Guid.NewGuid():N}";
                var bookingCode = "WALK-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
                var customerIdentifier = !string.IsNullOrWhiteSpace(walker.Phone)
                    ? walker.Phone
                    : (!string.IsNullOrWhiteSpace(walker.Email) ? walker.Email : Guid.NewGuid().ToString());

                // 1. Get or Create CustomerId
                int customerId = 0;
                
                // Try find by phone first
                if (!string.IsNullOrWhiteSpace(walker.Phone))
                {
                    var digits = new string(walker.Phone.Where(char.IsDigit).ToArray());
                    var last7 = digits.Length > 7 ? digits[^7..] : digits;
                    
                    const string findCustomerSql = @"
                        SELECT TOP 1 Id 
                        FROM dbo.Customers 
                        WHERE REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(PhoneNumber,''),'-',''),'(',''),')',''),' ','') LIKE '%' + @Digits
                        ORDER BY UpdatedAt DESC";
                        
                    customerId = await conn.QueryFirstOrDefaultAsync<int>(findCustomerSql, new { Digits = last7 }, tx);
                }
                
                // If not found, create new Customer
                if (customerId == 0)
                {
                    Console.WriteLine($"[{DateTime.UtcNow:O}] Customer not found, creating new for {walker.CustomerName}");
                    
                    // Split name
                    var parts = walker.CustomerName.Trim().Split(' ');
                    var first = parts[0];
                    var last = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : "";

                    const string createCustomerSql = @"
                        INSERT INTO dbo.Customers (
                            FullName, FirstName, LastName, PhoneNumber, Email, CustomerIdentifier, 
                            BookingIds, TotalBookings, CreatedAt, UpdatedAt
                        )
                        OUTPUT inserted.Id
                        VALUES (
                            @FullName, @FirstName, @LastName, @PhoneNumber, @Email, @CustomerIdentifier, 
                            NULL, 0, SYSUTCDATETIME(), SYSUTCDATETIME()
                        );";

                    customerId = await conn.QuerySingleAsync<int>(createCustomerSql, new {
                        FullName = walker.CustomerName,
                        FirstName = first,
                        LastName = last,
                        PhoneNumber = walker.Phone,
                        Email = walker.Email,
                        CustomerIdentifier = customerIdentifier
                    }, tx);
                }

                // 2. Insert synthetic inbox row (ProcessedEmails has FK to InboxEmails)
                const string insertInboxSql = @"
                    INSERT INTO dbo.AutomaticGmail_InboxEmails
                    (
                        Uid, MessageId, Subject, FromEmail, FromName, ToEmail,
                        ReceivedDate, TextBody, HtmlBody, TextBodyPreview,
                        AttachmentCount, AttachmentNames, CollectedAt, CollectionBatchId,
                        IsRead, CreatedAt, UpdatedAt, ProcessingStatus
                    )
                    OUTPUT INSERTED.Id
                    VALUES
                    (
                        0, @MessageId, @Subject, @FromEmail, @FromName, @ToEmail,
                        @ReceivedDate, @TextBody, @HtmlBody, @TextBodyPreview,
                        0, N'[]', @CollectedAt, @CollectionBatchId,
                        1, @CreatedAt, @UpdatedAt, @ProcessingStatus
                    );";

                var inboxId = await conn.QuerySingleAsync<int>(insertInboxSql, new
                {
                    MessageId = messageId,
                    Subject = $"Guide Walk-Up: {safeTourName} ({date:yyyy-MM-dd})",
                    FromEmail = "walkup@guide-report.local",
                    FromName = "Guide Report",
                    ToEmail = "system@local",
                    ReceivedDate = now,
                    TextBody = $"Guide walk-up entry for {safeTourName} at {safeTourTime}",
                    HtmlBody = string.Empty,
                    TextBodyPreview = "Guide walk-up entry",
                    CollectedAt = now,
                    CollectionBatchId = "GUIDE_REPORT",
                    CreatedAt = now,
                    UpdatedAt = now,
                    ProcessingStatus = 3 // Processed
                }, tx);

                // 3. Insert new booking for Walk-Up
                // Added missing required fields with defaults to prevent SqlException
                const string sql = @"
                    INSERT INTO dbo.Bookings (
                        CustomerId,
                        CustomerName, 
                        CustomerPhone, 
                        CustomerEmail,
                        VendorName, 
                        TourDate, 
                        TourName, 
                        TourTime, 
                        NumberOfAttendees, 
                        NumberOfAdults, 
                        NumberOfChildren, 
                        ActualAttendees,
                        ActualAdults,
                        ActualChildren,
                        IsCheckedIn, 
                        IsActive, 
                        IsCancellation,
                        CreatedAt,
                        UpdatedAt,
                        GuideReviewNotes,
                        BookingCode,
                        CustomerIdentifier,
                        BookingStatus,
                        ProcessedEmailId,
                        MessageId,
                        EmailType
                    )
                    OUTPUT INSERTED.Id
                    VALUES (
                        @CustomerId,
                        @CustomerName, 
                        @Phone, 
                        @Email,
                        'Walk-Up', 
                        @date, 
                        @tourName, 
                        @tourTime, 
                        @ActualAttendees, 
                        @ActualAdults, 
                        @ActualChildren, 
                        @ActualAttendees,
                        @ActualAdults,
                        @ActualChildren,
                        1, 
                        1, 
                        0, 
                        SYSUTCDATETIME(),
                        SYSUTCDATETIME(),
                        @ReviewNotes,
                        @BookingCode,
                        @CustomerIdentifier,
                        'Confirmed',
                        0,
                        @MessageId,
                        'WalkUp'
                    )";

                // Prepare params
                var p = new
                {
                    CustomerId = customerId,
                    walker.CustomerName,
                    walker.Phone,
                    walker.Email,
                    date,
                    tourName = safeTourName,
                    tourTime = safeTourTime,
                    walker.ActualAttendees,
                    walker.ActualAdults,
                    walker.ActualChildren,
                    ReviewNotes = walker.ReviewNotes,
                    BookingCode = bookingCode,
                    CustomerIdentifier = customerIdentifier,
                    MessageId = messageId
                };
                
                var id = await conn.QuerySingleAsync<int>(sql, p, tx);

                // 4. Insert processed row so walk-up guests render in TourTree (tree source).
                const string insertProcessedSql = @"
                    INSERT INTO dbo.AutomaticGmail_ProcessedEmails
                    (
                        InboxEmailId, MessageId, VendorName, EmailType, IsTourBookingEmail,
                        ProcessingStatus, ProcessingStartedAt, ProcessingCompletedAt, ProcessingAttempts,
                        CustomerName, BookingCode, CustomerPhone, CustomerEmail,
                        NumberOfAttendees, NumberOfAdults, NumberOfChildren,
                        TourDate, TourTime, TourName,
                        ExtractedAt, CustomerIdentifier, IsLatestAction,
                        PlainTextContent, HtmlContent,
                        IsCancellation, IsModification, IsBooking,
                        ExtractedBookingCode, NewBookingCode, PreviousBookingCode,
                        CreatedAt, UpdatedAt
                    )
                    OUTPUT INSERTED.Id
                    VALUES
                    (
                        @InboxEmailId, @MessageId, @VendorName, @EmailType, 1,
                        N'completed', @NowUtc, @NowUtc, 1,
                        @CustomerName, @BookingCode, @CustomerPhone, @CustomerEmail,
                        @NumberOfAttendees, @NumberOfAdults, @NumberOfChildren,
                        @TourDate, @TourTime, @TourName,
                        @NowUtc, @CustomerIdentifier, 1,
                        @PlainTextContent, N'',
                        0, 0, 1,
                        @BookingCode, N'', N'',
                        @NowUtc, @NowUtc
                    );";

                var processedId = await conn.QuerySingleAsync<int>(insertProcessedSql, new
                {
                    InboxEmailId = inboxId,
                    MessageId = messageId,
                    VendorName = "Walk-Up",
                    EmailType = "booking",
                    CustomerName = walker.CustomerName,
                    BookingCode = bookingCode,
                    CustomerPhone = walker.Phone,
                    CustomerEmail = walker.Email,
                    NumberOfAttendees = walker.ActualAttendees,
                    NumberOfAdults = walker.ActualAdults,
                    NumberOfChildren = walker.ActualChildren,
                    TourDate = date.ToString("yyyy-MM-dd"),
                    TourTime = safeTourTime,
                    TourName = safeTourName,
                    CustomerIdentifier = customerIdentifier,
                    PlainTextContent = $"Guide walk-up entry for {safeTourName} at {safeTourTime}",
                    NowUtc = now
                }, tx);

                // 5. Link booking -> processed row for consistency.
                const string linkProcessedSql = @"
                    UPDATE dbo.Bookings
                    SET ProcessedEmailId = @ProcessedEmailId,
                        UpdatedAt = SYSUTCDATETIME()
                    WHERE Id = @BookingId;";

                await conn.ExecuteAsync(linkProcessedSql, new { ProcessedEmailId = processedId, BookingId = id }, tx);
                tx.Commit();
                Console.WriteLine($"[{DateTime.UtcNow:O}] AddWalkUpBookingAsync success. New BookingId: {id} for CustomerId: {customerId}");
                return id;
            }
            catch (Exception ex)
            {
                try { tx.Rollback(); } catch { }
                Console.WriteLine($"[{DateTime.UtcNow:O}] AddWalkUpBookingAsync ERROR: {ex.Message}");
                if (ex.InnerException != null) 
                    Console.WriteLine($"[{DateTime.UtcNow:O}] Inner Ex: {ex.InnerException.Message}");
                throw;
            }
        }

        public async Task UpdateWalkUpBookingAsync(GuideReportWalker walker)
        {
            Console.WriteLine($"[{DateTime.UtcNow:O}] UpdateWalkUpBookingAsync called for BookingId {walker.BookingId}");
            using var conn = CreateConnection();
            
            try
            {
                const string sql = @"
                    UPDATE dbo.Bookings
                    SET CustomerName = @CustomerName,
                        CustomerPhone = @Phone,
                        CustomerEmail = @Email,
                        NumberOfAttendees = @ActualAttendees,
                        NumberOfAdults = @ActualAdults,
                        NumberOfChildren = @ActualChildren,
                        ActualAttendees = @ActualAttendees,
                        ActualAdults = @ActualAdults,
                        ActualChildren = @ActualChildren,
                        GuideReviewNotes = @ReviewNotes,
                        UpdatedAt = SYSUTCDATETIME()
                    WHERE Id = @BookingId AND VendorName = 'Walk-Up'";

                const string syncProcessedSql = @"
                    UPDATE p
                    SET p.CustomerName = @CustomerName,
                        p.CustomerPhone = @Phone,
                        p.CustomerEmail = @Email,
                        p.NumberOfAttendees = @ActualAttendees,
                        p.NumberOfAdults = @ActualAdults,
                        p.NumberOfChildren = @ActualChildren,
                        p.UpdatedAt = SYSUTCDATETIME()
                    FROM dbo.AutomaticGmail_ProcessedEmails p
                    INNER JOIN dbo.Bookings b ON b.MessageId = p.MessageId
                    WHERE b.Id = @BookingId AND b.VendorName = 'Walk-Up';";

                var p = new
                {
                    walker.BookingId,
                    walker.CustomerName,
                    walker.Phone,
                    walker.Email,
                    walker.ActualAttendees,
                    walker.ActualAdults,
                    walker.ActualChildren,
                    ReviewNotes = walker.ReviewNotes
                };

                await conn.ExecuteAsync(sql, p);
                await conn.ExecuteAsync(syncProcessedSql, p);
                Console.WriteLine($"[{DateTime.UtcNow:O}] UpdateWalkUpBookingAsync success");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.UtcNow:O}] UpdateWalkUpBookingAsync ERROR: {ex.Message}");
                throw;
            }
        }

        public async Task DeleteWalkUpBookingAsync(int bookingId)
        {
            Console.WriteLine($"[{DateTime.UtcNow:O}] DeleteWalkUpBookingAsync called for BookingId {bookingId}");
            using var conn = CreateConnection();
            
            try
            {
                const string sql = @"
                    UPDATE dbo.Bookings
                    SET IsActive = 0,
                        IsCancellation = 1,
                        BookingStatus = 'Cancelled',
                        UpdatedAt = SYSUTCDATETIME()
                    WHERE Id = @BookingId AND VendorName = 'Walk-Up'";

                const string cancelProcessedSql = @"
                    UPDATE p
                    SET p.IsCancellation = 1,
                        p.IsModification = 0,
                        p.IsBooking = 0,
                        p.EmailType = N'cancellation',
                        p.ProcessingStatus = N'cancelled',
                        p.UpdatedAt = SYSUTCDATETIME()
                    FROM dbo.AutomaticGmail_ProcessedEmails p
                    INNER JOIN dbo.Bookings b ON b.MessageId = p.MessageId
                    WHERE b.Id = @BookingId AND b.VendorName = 'Walk-Up';";

                await conn.ExecuteAsync(sql, new { BookingId = bookingId });
                await conn.ExecuteAsync(cancelProcessedSql, new { BookingId = bookingId });
                Console.WriteLine($"[{DateTime.UtcNow:O}] DeleteWalkUpBookingAsync success");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.UtcNow:O}] DeleteWalkUpBookingAsync ERROR: {ex.Message}");
                throw;
            }
        }

        public async Task<TourReport> GetReportAsync(DateTime date, string tourName, string tourTime)
        {
            using var conn = CreateConnection();
            var familyCache = new Dictionary<string, TourFamilySnapshot>(StringComparer.OrdinalIgnoreCase);
            var requestedFamily = await ResolveTourFamilyAsync(tourName, vendorName: null, familyCache);

            // 1. Try to get existing report
            const string sqlReport = @"
                SELECT * 
                FROM dbo.TourReports 
                WHERE TourDate = @date AND TourName = @tourName AND TourTime = @tourTime";
            
            var report = await conn.QuerySingleOrDefaultAsync<TourReport>(sqlReport, new { date, tourName, tourTime });

            if (report == null)
            {
                var candidatesByDateTime = (await conn.QueryAsync<TourReport>(
                    "SELECT * FROM dbo.TourReports WHERE TourDate = @date AND TourTime = @tourTime", 
                    new { date, tourTime })).ToList();

                Console.WriteLine($"[GalleryDebug] Fallback: Found {candidatesByDateTime.Count} candidate(s) by date/time. InputDate={date:yyyy-MM-dd}, InputName='{tourName}', InputTime='{tourTime}'");
                foreach (var cand in candidatesByDateTime)
                {
                    Console.WriteLine($"[GalleryDebug] Cand: Id={cand.Id}, Name='{cand.TourName}', PublicId='{cand.PublicId}', GuideId={cand.GuideId}, IsSubmitted={cand.IsSubmitted}");
                }

                var candidatePool = candidatesByDateTime;
                if (!candidatePool.Any())
                {
                    var dateOnly = (await conn.QueryAsync<TourReport>(
                        "SELECT * FROM dbo.TourReports WHERE TourDate = @date ORDER BY Id DESC",
                        new { date })).ToList();

                    Console.WriteLine($"[GalleryDebug] Fallback: Found {dateOnly.Count} report(s) on date {date:yyyy-MM-dd} (any time).");
                    if (dateOnly.Count > 0)
                    {
                        var distinctTimes = dateOnly.Select(r => r.TourTime ?? "").Distinct().Take(10).ToList();
                        Console.WriteLine($"[GalleryDebug] Distinct TourTime on date (top 10): {string.Join(", ", distinctTimes.Select(t => $"'{t}'"))}");
                        foreach (var cand in dateOnly.Take(20))
                        {
                            Console.WriteLine($"[GalleryDebug] DateOnly Cand: Id={cand.Id}, Name='{cand.TourName}', Time='{cand.TourTime}', PublicId='{cand.PublicId}', GuideId={cand.GuideId}, IsSubmitted={cand.IsSubmitted}");
                        }
                    }

                    candidatePool = dateOnly;
                }

                if (!candidatePool.Any() && !string.IsNullOrWhiteSpace(tourName))
                {
                    var nameLike = $"%{tourName}%";
                    var nameMatches = (await conn.QueryAsync<TourReport>(
                        "SELECT TOP 20 * FROM dbo.TourReports WHERE TourDate = @date AND TourName LIKE @nameLike ORDER BY Id DESC",
                        new { date, nameLike })).ToList();

                    Console.WriteLine($"[GalleryDebug] Fallback: Name LIKE matches on date {date:yyyy-MM-dd} for '{tourName}': {nameMatches.Count}");
                    foreach (var cand in nameMatches)
                    {
                        Console.WriteLine($"[GalleryDebug] NameLike Cand: Id={cand.Id}, Name='{cand.TourName}', Time='{cand.TourTime}', PublicId='{cand.PublicId}', GuideId={cand.GuideId}, IsSubmitted={cand.IsSubmitted}");
                    }

                    candidatePool = nameMatches;
                }

                if (candidatePool.Any())
                {
                    var requestedTimeKey = NormalizeTourTimeKeyForLookup(tourTime);
                    if (!string.IsNullOrWhiteSpace(requestedTimeKey))
                    {
                        var normalizedTimeMatches = candidatePool
                            .Where(c => string.Equals(NormalizeTourTimeKeyForLookup(c.TourTime), requestedTimeKey, StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        if (normalizedTimeMatches.Count > 0)
                        {
                            if (normalizedTimeMatches.Count != candidatePool.Count)
                            {
                                Console.WriteLine($"[GalleryDebug] Time-normalized filter retained {normalizedTimeMatches.Count}/{candidatePool.Count} candidate(s).");
                            }
                            candidatePool = normalizedTimeMatches;
                        }
                        else
                        {
                            // No report candidate matches requested time. Avoid selecting unrelated reports from the same date.
                            Console.WriteLine($"[GalleryDebug] Time-normalized filter found 0/{candidatePool.Count} candidate(s); skipping report reuse.");
                            candidatePool = new List<TourReport>();
                        }
                    }

                    var familyScopedCandidates = new List<TourReport>();
                    foreach (var candidate in candidatePool)
                    {
                        var candidateFamily = await ResolveTourFamilyAsync(candidate.TourName, vendorName: null, familyCache);
                        if (string.Equals(candidateFamily.FamilyKey, requestedFamily.FamilyKey, StringComparison.OrdinalIgnoreCase))
                        {
                            familyScopedCandidates.Add(candidate);
                        }
                    }

                    var scopedPool = familyScopedCandidates.Count > 0
                        ? familyScopedCandidates
                        : (string.IsNullOrWhiteSpace(requestedFamily.FamilyKey) || string.Equals(requestedFamily.FamilyKey, "__unknown__", StringComparison.OrdinalIgnoreCase)
                            ? candidatePool
                            : new List<TourReport>());
                    if (familyScopedCandidates.Count > 0 && familyScopedCandidates.Count != candidatePool.Count)
                    {
                        Console.WriteLine($"[GalleryDebug] Family filter retained {familyScopedCandidates.Count}/{candidatePool.Count} candidate(s).");
                    }
                    else if (familyScopedCandidates.Count == 0 && candidatePool.Count > 0 && !ReferenceEquals(scopedPool, candidatePool))
                    {
                        Console.WriteLine($"[GalleryDebug] Family filter found 0/{candidatePool.Count} candidate(s); skipping report reuse.");
                    }

                    // Prioritize reports that are Submitted and have PublicId
                    var bestCandidates = scopedPool.Where(c => c.IsSubmitted && !string.IsNullOrEmpty(c.PublicId)).ToList();
                    
                    // If no submitted ones, fallback to any valid PublicId
                    if (!bestCandidates.Any())
                        bestCandidates = scopedPool.Where(c => !string.IsNullOrEmpty(c.PublicId)).ToList();
                        
                    // If still none, use all (e.g. maybe just created but has PublicId?) - typically PublicId is set on create?
                    if (!bestCandidates.Any())
                        bestCandidates = scopedPool;
                        
                    // If multiple best candidates, use name matching on THEM
                    if (bestCandidates.Count == 1)
                    {
                        report = bestCandidates.First();
                        Console.WriteLine($"[GalleryDebug] Selected single best candidate: Id={report.Id}");
                    }
                    else
                    {
                        // Fuzzy match logic on bestCandidates
                        var simplify = (string s) => new string(s.Where(c => char.IsLetterOrDigit(c)).ToArray()).ToLowerInvariant();
                        var safeInput = simplify(tourName ?? string.Empty);

                        foreach (var c in bestCandidates)
                        {
                            var safeDb = simplify(c.TourName);
                            // 1. Strict containment (simplified)
                            if (!string.IsNullOrWhiteSpace(safeInput) &&
                                (safeDb.Contains(safeInput) || safeInput.Contains(safeDb)))
                            {
                                report = c;
                                Console.WriteLine($"[GalleryDebug] Selected best by simplified containment: Id={report.Id}");
                                break;
                            }
                            
                            // 2. Loose fallback (e.g. "Soho" check)
                            if (safeDb.Contains("soho") && safeInput.Contains("soho")) 
                            { 
                                // Matches "Soho" - good enough given we already matched Date/Time
                                report = c;
                                Console.WriteLine($"[GalleryDebug] Selected best by 'soho' keyword match: Id={report.Id}");
                                break;
                            }
                        }
                        
                        if (report == null)
                        {
                            var cleanInput = (tourName ?? string.Empty).Trim();
                            if (cleanInput.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
                            {
                                cleanInput = cleanInput.Substring(4).Trim();
                            }

                            if (!string.IsNullOrWhiteSpace(cleanInput))
                            {
                                report = bestCandidates.FirstOrDefault(c =>
                                    c.TourName.Contains(cleanInput, StringComparison.OrdinalIgnoreCase) ||
                                    cleanInput.Contains(c.TourName, StringComparison.OrdinalIgnoreCase));
                                if (report != null) Console.WriteLine($"[GalleryDebug] Selected best by trim 'The': Id={report.Id}");
                            }
                        }
                        
                        if (report == null)
                        {
                            report = bestCandidates.FirstOrDefault(c =>
                            {
                                var decoded = System.Net.WebUtility.HtmlDecode(c.TourName);
                                return string.Equals(decoded, tourName, StringComparison.OrdinalIgnoreCase) ||
                                       (!string.IsNullOrWhiteSpace(tourName) &&
                                        (decoded.Contains(tourName, StringComparison.OrdinalIgnoreCase) ||
                                         tourName.Contains(decoded, StringComparison.OrdinalIgnoreCase)));
                            });
                            if (report != null) Console.WriteLine($"[GalleryDebug] Selected best by HTML decode: Id={report.Id}");
                        }
                    }

                    if (report == null && bestCandidates.Count > 0)
                    {
                        report = bestCandidates
                            .OrderByDescending(c => c.SubmittedAt ?? c.UpdatedAt)
                            .ThenByDescending(c => c.Id)
                            .FirstOrDefault();
                        if (report != null)
                        {
                            Console.WriteLine($"[GalleryDebug] Selected fallback latest candidate: Id={report.Id}");
                        }
                    }
                }
            }

            if (report == null)
            {
                // Create a transient model (not saved yet)
                report = new TourReport
                {
                    TourDate = date,
                    TourName = tourName,
                    TourTime = tourTime,
                    // GuideId/Name would be fetched from assignment or context if needed, 
                    // but for now we let the UI pass it or we fetch it here.
                };
            }
            
            // 2. Fetch Walkers (Bookings) - 2026-02-07: Use matching logic to capture normalized name variations
            const string sqlBookings = @"
                IF OBJECT_ID(N'dbo.BookingContactChannelState', N'U') IS NULL
                BEGIN
                    SELECT 
                        b.Id as BookingId,
                        b.CustomerName,
                        b.CustomerPhone as Phone,
                        b.VendorName,
                        ISNULL(b.MessageId, '') as MessageId,
                        ISNULL(b.BookingCode, '') as BookingCode,
                        b.NumberOfAttendees as BookedAttendees,
                        CASE
                            WHEN ISNULL(b.NumberOfAdults, 0) = 0
                             AND ISNULL(b.NumberOfChildren, 0) = 0
                             AND ISNULL(b.NumberOfAttendees, 0) > 0
                                THEN b.NumberOfAttendees
                            ELSE ISNULL(b.NumberOfAdults, b.NumberOfAttendees)
                        END as BookedAdults,
                        ISNULL(b.NumberOfChildren, 0) as BookedChildren,
                        b.ActualAttendees,
                        b.ActualAdults,
                        b.ActualChildren,
                        ISNULL(b.IsCheckedIn, 0) as IsCheckedIn,
                        ISNULL(b.DoNotContact, 0) as DoNotContact,
                        b.GuideReviewStatus as ReviewStatus,
                        b.GuideReviewNotes as ReviewNotes,
                        b.TourName,
                        b.TourDate,
                        b.TourTime,
                        CAST(0 AS TINYINT) as SmsContactState,
                        CAST(0 AS TINYINT) as WaContactState
                    FROM dbo.Bookings b
                    WHERE b.IsActive = 1 
                      AND b.TourDate = @date 
                      AND b.IsCancellation = 0
                    ORDER BY b.CustomerName;
                END
                ELSE
                BEGIN
                    SELECT 
                        b.Id as BookingId,
                        b.CustomerName,
                        b.CustomerPhone as Phone,
                        b.VendorName,
                        ISNULL(b.MessageId, '') as MessageId,
                        ISNULL(b.BookingCode, '') as BookingCode,
                        b.NumberOfAttendees as BookedAttendees,
                        CASE
                            WHEN ISNULL(b.NumberOfAdults, 0) = 0
                             AND ISNULL(b.NumberOfChildren, 0) = 0
                             AND ISNULL(b.NumberOfAttendees, 0) > 0
                                THEN b.NumberOfAttendees
                            ELSE ISNULL(b.NumberOfAdults, b.NumberOfAttendees)
                        END as BookedAdults,
                        ISNULL(b.NumberOfChildren, 0) as BookedChildren,
                        b.ActualAttendees,
                        b.ActualAdults,
                        b.ActualChildren,
                        ISNULL(b.IsCheckedIn, 0) as IsCheckedIn,
                        ISNULL(b.DoNotContact, 0) as DoNotContact,
                        b.GuideReviewStatus as ReviewStatus,
                        b.GuideReviewNotes as ReviewNotes,
                        b.TourName,
                        b.TourDate,
                        b.TourTime,
                        CAST(ISNULL(sms.ContactState, 0) AS TINYINT) as SmsContactState,
                        CAST(ISNULL(wa.ContactState, 0) AS TINYINT) as WaContactState
                    FROM dbo.Bookings b
                    OUTER APPLY
                    (
                        SELECT TOP 1 cs.ContactState
                        FROM dbo.BookingContactChannelState cs
                        WHERE cs.MessageId = b.MessageId
                          AND cs.BookingCode = b.BookingCode
                          AND cs.Channel = N'sms'
                        ORDER BY cs.UpdatedAtUtc DESC, cs.Id DESC
                    ) sms
                    OUTER APPLY
                    (
                        SELECT TOP 1 cs.ContactState
                        FROM dbo.BookingContactChannelState cs
                        WHERE cs.MessageId = b.MessageId
                          AND cs.BookingCode = b.BookingCode
                          AND cs.Channel = N'wa'
                        ORDER BY cs.UpdatedAtUtc DESC, cs.Id DESC
                    ) wa
                    WHERE b.IsActive = 1 
                      AND b.TourDate = @date 
                      AND b.IsCancellation = 0
                    ORDER BY b.CustomerName;
                END";

            var allWalkers = (await conn.QueryAsync<GuideReportWalker>(sqlBookings, new { date })).ToList();
            var requestedWalkerTimeKey = NormalizeTourTimeKeyForLookup(tourTime);
            var timeScopedWalkers = string.IsNullOrWhiteSpace(requestedWalkerTimeKey)
                ? allWalkers
                : allWalkers
                    .Where(w => string.Equals(
                        NormalizeTourTimeKeyForLookup(w.TourTime),
                        requestedWalkerTimeKey,
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();

            var reportFamily = await ResolveTourFamilyAsync(report.TourName, vendorName: null, familyCache);

            var walkers = new List<GuideReportWalker>();
            foreach (var walker in timeScopedWalkers)
            {
                var walkerFamily = await ResolveTourFamilyAsync(walker.TourName, walker.VendorName, familyCache);
                if (string.Equals(walkerFamily.FamilyKey, reportFamily.FamilyKey, StringComparison.OrdinalIgnoreCase))
                {
                    walkers.Add(walker);
                }
            }

            if (!walkers.Any())
            {
                // Safety fallback for unmapped tour names.
                var targetNormalized = TourNameNormalization.NormalizeTourNameForGrouping(string.Empty, report.TourName);
                walkers = timeScopedWalkers.Where(w =>
                    TourNameNormalization.NormalizeTourNameForGrouping(w.VendorName ?? string.Empty, w.TourName)
                        .Equals(targetNormalized, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            Console.WriteLine(
                $"[GuideReportService] WalkerMatch Date={date:yyyy-MM-dd} RequestedTime='{tourTime}' RequestedTimeKey='{requestedWalkerTimeKey}' " +
                $"AllWalkers={allWalkers.Count} TimeScoped={timeScopedWalkers.Count} ReportTour='{report.TourName}' FamilyKey='{reportFamily.FamilyKey}' FinalWalkers={walkers.Count}");
            
            // Apply defaults if ActualAdults/ActualChildren are NULL (first load)
            report.Walkers = walkers.Select(w => {
                 if (w.ActualAdults == 0 && w.BookedAdults > 0 && w.ReviewStatus == null) 
                 {
                     w.ActualAdults = w.BookedAdults;
                     w.ActualChildren = w.BookedChildren;
                     w.ActualAttendees = w.BookedAttendees;
                 }
                 return w;
            }).ToList();

            return report;
        }

        public async Task<TourReport> GetReportByPublicIdAsync(string publicId)
        {
            using var conn = CreateConnection();
            const string sql = "SELECT * FROM dbo.TourReports WHERE PublicId = @publicId";
            return await conn.QuerySingleOrDefaultAsync<TourReport>(sql, new { publicId });
        }

        public async Task SaveReportAsync(TourReport report)
        {
            // Generate Short ID if missing
            if (string.IsNullOrWhiteSpace(report.PublicId))
            {
                // Simple 16-char alphanumeric ID
                report.PublicId = Guid.NewGuid().ToString("N").Substring(0, 16);
            }

            using var conn = CreateConnection();
            // conn.Open(); // Factory returns open connection
            using var tx = conn.BeginTransaction();

            try
            {
                // 1. Upsert TourReports
                const string upsertReport = @"
                    MERGE dbo.TourReports AS target
                    USING (SELECT @TourDate AS D, @TourName AS N, @TourTime AS T) AS source
                    ON (target.TourDate = source.D AND target.TourName = source.N AND target.TourTime = source.T)
                    WHEN MATCHED THEN
                        UPDATE SET 
                            GeneralNotes = @GeneralNotes,
                            ImagePaths = @ImagePaths,
                            IsSubmitted = @IsSubmitted,
                            SubmittedAt = @SubmittedAt,
                            UpdatedAt = SYSUTCDATETIME()
                    WHEN NOT MATCHED THEN
                        INSERT (PublicId, TourDate, TourName, TourTime, GuideId, GeneralNotes, ImagePaths, IsSubmitted, SubmittedAt, CreatedAt, UpdatedAt)
                        VALUES (@PublicId, @TourDate, @TourName, @TourTime, @GuideId, @GeneralNotes, @ImagePaths, @IsSubmitted, @SubmittedAt, SYSUTCDATETIME(), SYSUTCDATETIME());";

                await conn.ExecuteAsync(upsertReport, report, transaction: tx);

                // 2. Update Bookings (Walkers)
                const string updateBooking = @"
                    UPDATE dbo.Bookings
                    SET 
                        GuideReviewStatus = @ReviewStatus,
                        GuideReviewNotes = @ReviewNotes,
                        ActualAttendees = @ActualAdults + @ActualChildren,
                        ActualAdults = @ActualAdults,
                        ActualChildren = @ActualChildren,
                        IsCheckedIn = @IsCheckedIn,
                        DoNotContact = @DoNotContact
                    WHERE Id = @BookingId";

                foreach (var walker in report.Walkers)
                {
                    await conn.ExecuteAsync(updateBooking, walker, transaction: tx);
                }

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        public async Task<int> UpsertReportImagePathsAsync(DateTime tourDate, string tourName, string tourTime, string? imagePaths)
        {
            using var conn = CreateConnection();

            const string sql = @"
DECLARE @ExistingId INT;
SELECT TOP 1 @ExistingId = Id
FROM dbo.TourReports
WHERE TourDate = @TourDate
  AND TourName = @TourName
  AND TourTime = @TourTime;

IF @ExistingId IS NOT NULL
BEGIN
    UPDATE dbo.TourReports
    SET ImagePaths = @ImagePaths,
        UpdatedAt = SYSUTCDATETIME()
    WHERE Id = @ExistingId;

    SELECT @ExistingId;
END
ELSE
BEGIN
    INSERT INTO dbo.TourReports
    (
        PublicId,
        TourDate,
        TourName,
        TourTime,
        GuideId,
        GeneralNotes,
        ImagePaths,
        IsSubmitted,
        SubmittedAt,
        CreatedAt,
        UpdatedAt
    )
    VALUES
    (
        @PublicId,
        @TourDate,
        @TourName,
        @TourTime,
        NULL,
        NULL,
        @ImagePaths,
        0,
        NULL,
        SYSUTCDATETIME(),
        SYSUTCDATETIME()
    );

    SELECT CAST(SCOPE_IDENTITY() AS INT);
END";

            var reportId = await conn.QuerySingleAsync<int>(sql, new
            {
                TourDate = tourDate.Date,
                TourName = tourName,
                TourTime = tourTime,
                ImagePaths = imagePaths,
                PublicId = Guid.NewGuid().ToString("N")[..16]
            });

            return reportId;
        }

        public async Task<List<TourReportSummary>> GetAllReportsAsync(DateTime? startDate = null, DateTime? endDate = null)
        {
            using var conn = CreateConnection();

            // 1. Fetch Reports
            var sqlReports = @"
                SELECT 
                    r.Id,
                    r.TourDate,
                    r.TourName,
                    r.TourTime,
                    r.GuideId,
                    g.FirstName + ' ' + ISNULL(g.LastName, '') AS GuideName,
                    r.GeneralNotes,
                    r.ImagePaths,
                    r.IsSubmitted,
                    r.SubmittedAt,
                    r.CreatedAt,
                    COALESCE(matchMap.MasterTourName, r.TourName) AS MasterTourName,
                    COALESCE(matchMap.MasterTourNameMobile, matchMap.MasterTourName, r.TourName) AS MasterTourNameMobile
                FROM dbo.TourReports r
                LEFT JOIN dbo.Guides g ON r.GuideId = g.Id
                OUTER APPLY (
                    SELECT TOP 1
                        x.MasterTourName,
                        x.MasterTourNameMobile
                    FROM (
                        SELECT 
                            t.MasterTourName,
                            t.MasterTourNameMobile,
                            0 AS Priority,
                            CASE WHEN m.VendorName IS NULL THEN 0 ELSE 1 END AS SubPriority,
                            m.Id AS SortId
                        FROM dbo.TourNameMappings m
                        INNER JOIN dbo.Tours t ON t.Id = m.TourId AND t.IsActive = 1
                        WHERE m.IsActive = 1
                          AND LOWER(m.IncomingTourName) = LOWER(r.TourName)
                        UNION ALL
                        SELECT 
                            t.MasterTourName,
                            t.MasterTourNameMobile,
                            1 AS Priority,
                            0 AS SubPriority,
                            t.Id AS SortId
                        FROM dbo.Tours t
                        WHERE t.IsActive = 1
                          AND (LOWER(t.MasterTourName) = LOWER(r.TourName) OR LOWER(t.TourName) = LOWER(r.TourName))
                    ) x
                    ORDER BY x.Priority, x.SubPriority, x.SortId
                ) matchMap
                WHERE (@startDate IS NULL OR r.TourDate >= @startDate)
                  AND (@endDate IS NULL OR r.TourDate <= @endDate)
                ORDER BY r.TourDate DESC, r.TourTime DESC";

            var reports = (await conn.QueryAsync<TourReportSummary>(sqlReports, new { startDate, endDate })).ToList();

            // 2. Fetch ALL relevant bookings for this range (lightweight) to compute counts In-Memory
            //    This is required because SQL grouping cannot use the C# Normalization logic
            var sqlBookings = @"
                SELECT 
                    TourDate, 
                    TourTime, 
                    TourName, 
                    VendorName,
                    ISNULL(ActualAttendees, NumberOfAttendees) as Attendees
                FROM dbo.Bookings
                WHERE IsActive = 1 
                  AND IsCancellation = 0
                  AND (@startDate IS NULL OR TourDate >= @startDate)
                  AND (@endDate IS NULL OR TourDate <= @endDate)";

            var allBookings = (await conn.QueryAsync<dynamic>(sqlBookings, new { startDate, endDate })).ToList();

            // 3. Map bookigns to reports using Normalization
            foreach (var r in reports)
            {
                var targetNormalized = TourNameNormalization.NormalizeTourNameForGrouping(string.Empty, r.TourName);
                
                var matchingBookings = allBookings.Where(b => 
                    ((DateTime)b.TourDate).Date == r.TourDate.Date && 
                    ((string)b.TourTime) == r.TourTime &&
                    TourNameNormalization.NormalizeTourNameForGrouping((string)b.VendorName, (string)b.TourName)
                        .Equals(targetNormalized, StringComparison.OrdinalIgnoreCase)
                ).ToList();

                r.WalkerCount = matchingBookings.Count;
                r.TotalAttendees = matchingBookings.Sum(b => (int)b.Attendees);
            }

            return reports;
        }
        public async Task UpdateBookingPhoneAsync(int bookingId, string phone)
        {
             Console.WriteLine($"[{DateTime.UtcNow:O}] UpdateBookingPhoneAsync called for BookingId {bookingId}");
             using var conn = CreateConnection();
             
             try
             {
                 // 1. Update Booking
                 const string updateBookingSql = @"
                     UPDATE dbo.Bookings
                     SET CustomerPhone = @Phone,
                         CustomerIdentifier = @Phone, -- Keep ID in sync for phone-based users
                         UpdatedAt = SYSUTCDATETIME()
                     OUTPUT INSERTED.CustomerId
                     WHERE Id = @BookingId";

                 var customerId = await conn.QuerySingleOrDefaultAsync<int?>(updateBookingSql, new { BookingId = bookingId, Phone = phone });
                 
                 // 2. Update Customer if exists
                 if (customerId.HasValue && customerId.Value > 0)
                 {
                     const string updateCustomerSql = @"
                         UPDATE dbo.Customers
                         SET PhoneNumber = @Phone,
                             UpdatedAt = SYSUTCDATETIME()
                         WHERE Id = @CustomerId";
                         
                     await conn.ExecuteAsync(updateCustomerSql, new { CustomerId = customerId.Value, Phone = phone });
                 }
                 
                 Console.WriteLine($"[{DateTime.UtcNow:O}] UpdateBookingPhoneAsync success");
             }
             catch (Exception ex)
             {
                 Console.WriteLine($"[{DateTime.UtcNow:O}] UpdateBookingPhoneAsync ERROR: {ex.Message}");
                 throw;
             }
        }

        // 2026-06-04 - Update guest name + phone + email together (bookings page pencil edit)
        public async Task UpdateBookingContactAsync(int bookingId, string name, string phone, string email)
        {
            using var conn = CreateConnection();
            try
            {
                const string updateBookingSql = @"
                    UPDATE dbo.Bookings
                    SET CustomerName = @Name,
                        CustomerPhone = @Phone,
                        CustomerEmail = @Email,
                        CustomerIdentifier = CASE WHEN @Phone <> '' THEN @Phone ELSE CustomerIdentifier END,
                        UpdatedAt = SYSUTCDATETIME()
                    OUTPUT INSERTED.CustomerId
                    WHERE Id = @BookingId";

                var customerId = await conn.QuerySingleOrDefaultAsync<int?>(updateBookingSql,
                    new { BookingId = bookingId, Name = name ?? string.Empty, Phone = phone ?? string.Empty, Email = email ?? string.Empty });

                if (customerId.HasValue && customerId.Value > 0)
                {
                    const string updateCustomerSql = @"
                        UPDATE dbo.Customers
                        SET FullName = @Name,
                            PhoneNumber = @Phone,
                            Email = @Email,
                            UpdatedAt = SYSUTCDATETIME()
                        WHERE Id = @CustomerId";
                    await conn.ExecuteAsync(updateCustomerSql,
                        new { CustomerId = customerId.Value, Name = name ?? string.Empty, Phone = phone ?? string.Empty, Email = email ?? string.Empty });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.UtcNow:O}] UpdateBookingContactAsync ERROR: {ex.Message}");
                throw;
            }
        }

        public async Task UpdateBookingAttendeesAsync(int bookingId, int actualAdults, int actualChildren)
        {
            using var conn = CreateConnection();

            const string sql = @"
                UPDATE dbo.Bookings
                SET
                    ActualAttendees = @ActualAdults + @ActualChildren,
                    ActualAdults = @ActualAdults,
                    ActualChildren = @ActualChildren,
                    UpdatedAt = SYSUTCDATETIME()
                WHERE Id = @BookingId";

            var rows = await conn.ExecuteAsync(sql, new
            {
                BookingId = bookingId,
                ActualAdults = actualAdults,
                ActualChildren = actualChildren
            });

            if (rows == 0)
            {
                throw new InvalidOperationException($"Booking {bookingId} was not found.");
            }
        }

        public async Task UpdateBookingGuideStateAsync(int bookingId, string? reviewStatus, string? reviewNotes, bool isCheckedIn, bool doNotContact)
        {
            using var conn = CreateConnection();

            const string sql = @"
                UPDATE dbo.Bookings
                SET
                    GuideReviewStatus = @ReviewStatus,
                    GuideReviewNotes = @ReviewNotes,
                    IsCheckedIn = @IsCheckedIn,
                    DoNotContact = @DoNotContact,
                    UpdatedAt = SYSUTCDATETIME()
                WHERE Id = @BookingId";

            var rows = await conn.ExecuteAsync(sql, new
            {
                BookingId = bookingId,
                ReviewStatus = reviewStatus,
                ReviewNotes = reviewNotes,
                IsCheckedIn = isCheckedIn,
                DoNotContact = doNotContact
            });

            if (rows == 0)
            {
                throw new InvalidOperationException($"Booking {bookingId} was not found.");
            }
        }

        private static string NormalizeTourTimeKeyForLookup(string? rawTime)
        {
            if (string.IsNullOrWhiteSpace(rawTime))
            {
                return string.Empty;
            }

            var trimmed = rawTime.Trim();
            if (DateTime.TryParse(trimmed, out var parsedDateTime))
            {
                return parsedDateTime.ToString("HH:mm");
            }

            if (TimeSpan.TryParse(trimmed, out var parsedTimeSpan))
            {
                return DateTime.Today.Add(parsedTimeSpan).ToString("HH:mm");
            }

            return trimmed.ToUpperInvariant();
        }

        private async Task<TourFamilySnapshot> ResolveTourFamilyAsync(
            string? tourName,
            string? vendorName,
            Dictionary<string, TourFamilySnapshot> cache)
        {
            var safeTourName = (tourName ?? string.Empty).Trim();
            var normalizedRaw = TourNameNormalization.NormalizeTourNameForGrouping(vendorName ?? string.Empty, safeTourName);
            var cacheKey = $"{NormalizeVendorLookupKey(vendorName)}|{normalizedRaw}";
            if (cache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            TourNameResolution? resolution = null;
            if (!string.IsNullOrWhiteSpace(safeTourName))
            {
                try
                {
                    resolution = await _tourNameNormalizer.ResolveToMasterTourAsync(safeTourName, vendorName);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GuideReportService] ResolveToMasterTourAsync failed for '{safeTourName}': {ex.Message}");
                }
            }

            var desktopName = !string.IsNullOrWhiteSpace(resolution?.MasterTourNameDesktop)
                ? resolution!.MasterTourNameDesktop!.Trim()
                : !string.IsNullOrWhiteSpace(resolution?.MasterTourName)
                    ? resolution!.MasterTourName.Trim()
                    : TourNameNormalization.NormalizeTourNameForDisplay(safeTourName);

            if (string.IsNullOrWhiteSpace(desktopName))
            {
                desktopName = string.IsNullOrWhiteSpace(safeTourName) ? "Unknown Tour" : safeTourName;
            }

            var familyKey = TourNameNormalization.NormalizeTourNameForGrouping(string.Empty, desktopName);
            if (string.IsNullOrWhiteSpace(familyKey))
            {
                familyKey = normalizedRaw;
            }
            if (string.IsNullOrWhiteSpace(familyKey))
            {
                familyKey = "__unknown__";
            }

            var snapshot = new TourFamilySnapshot
            {
                FamilyKey = familyKey
            };
            cache[cacheKey] = snapshot;
            return snapshot;
        }

        private static string NormalizeVendorLookupKey(string? vendorName)
        {
            if (string.IsNullOrWhiteSpace(vendorName))
            {
                return string.Empty;
            }

            return new string(vendorName.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        }

        private sealed class TourFamilySnapshot
        {
            public string FamilyKey { get; set; } = string.Empty;
        }
    }

    /// <summary>
    /// Created: 1/28/2026 10:20 PM
    /// Summary model for displaying reports in a list view.
    /// </summary>
    public class TourReportSummary
    {
        public int Id { get; set; }
        public DateTime TourDate { get; set; }
        public string TourName { get; set; } = string.Empty;
        public string TourTime { get; set; } = string.Empty;
        public int? GuideId { get; set; }
        public string GuideName { get; set; } = string.Empty;
        public string? GeneralNotes { get; set; }
        public string? ImagePaths { get; set; }
        public bool IsSubmitted { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public int WalkerCount { get; set; }
        public int TotalAttendees { get; set; }
        public string? MasterTourName { get; set; }
        public string? MasterTourNameMobile { get; set; }
    }
}

