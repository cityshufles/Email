using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Email.Services.GmailProcessing.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Email.Services.GmailProcessing.Repositories
{
    /// <summary>
    /// Created: 2025-11-07 00:00 UTC
    /// Updated: 2025-11-26 00:00 UTC - Changed CancelAsync to return affected rows count
    /// SQL Server implementation for bookings repository.
    /// </summary>
    public sealed class SqlServerBookingRepository : IBookingRepository
    {
        private readonly string _connectionString;

        public SqlServerBookingRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("AutomaticGmailSqlServer") ?? throw new InvalidOperationException("Missing ConnectionStrings:AutomaticGmailSqlServer");
        }

        private IDbConnection CreateConnection() => new SqlConnection(_connectionString);

        public async Task<Booking?> FindByCodeAsync(string bookingCode, CancellationToken cancellationToken)
        {
            const string sql = @"
SELECT TOP 1 *
FROM dbo.Bookings
WHERE BookingCode = @BookingCode
ORDER BY UpdatedAt DESC;";
            using var conn = CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<Booking>(new CommandDefinition(sql, new { BookingCode = bookingCode }, cancellationToken: cancellationToken));
        }

        public async Task<IReadOnlyList<Booking>> GetByCodesAsync(IReadOnlyList<string> bookingCodes, CancellationToken cancellationToken)
        {
            if (bookingCodes == null || bookingCodes.Count == 0)
            {
                return Array.Empty<Booking>();
            }

            var normalizedCodes = bookingCodes
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (normalizedCodes.Count == 0)
            {
                return Array.Empty<Booking>();
            }

            const string sql = @"
SELECT *
FROM dbo.Bookings
WHERE BookingCode IN @Codes
ORDER BY UpdatedAt DESC, Id DESC;";

            using var conn = CreateConnection();
            var rows = await conn.QueryAsync<Booking>(
                new CommandDefinition(sql, new { Codes = normalizedCodes }, cancellationToken: cancellationToken));
            return rows.ToList();
        }

        public async Task<int> CreateAsync(Booking booking, CancellationToken cancellationToken)
        {
            const string sql = @"
INSERT INTO dbo.Bookings (
    CustomerId, CustomerIdentifier, ProcessedEmailId, MessageId, BookingCode, VendorName, EmailType,
    IsCancellation, IsModification, IsConfirmation, IsActive, TourName, TourDate, TourDayOfWeek, TourTime,
    TourLocation, TourTimeZone, DisplayDate, DisplayTime, CustomerName, CustomerEmail, CustomerPhone,
    NumberOfAttendees, NumberOfAdults, NumberOfChildren, NumberOfInfants, Language, CountryOfOrigin,
    BookingStatus, BookingAmount, Currency, SpecialRequests, GuideAssigned, CalendarEventId,
    IsCalendarExportSuccess, IsSheetExportSuccess, ProcessingNotes, CreatedAt, UpdatedAt
)
OUTPUT inserted.Id
VALUES (
    @CustomerId, @CustomerIdentifier, @ProcessedEmailId, @MessageId, @BookingCode, @VendorName, @EmailType,
    @IsCancellation, @IsModification, @IsConfirmation, @IsActive, @TourName, @TourDate, @TourDayOfWeek, @TourTime,
    @TourLocation, @TourTimeZone, @DisplayDate, @DisplayTime, @CustomerName, @CustomerEmail, @CustomerPhone,
    @NumberOfAttendees, @NumberOfAdults, @NumberOfChildren, @NumberOfInfants, @Language, @CountryOfOrigin,
    @BookingStatus, @BookingAmount, @Currency, @SpecialRequests, @GuideAssigned, @CalendarEventId,
    @IsCalendarExportSuccess, @IsSheetExportSuccess, @ProcessingNotes, SYSUTCDATETIME(), SYSUTCDATETIME()
);";
            using var conn = CreateConnection();
            return await conn.QuerySingleAsync<int>(new CommandDefinition(sql, booking, cancellationToken: cancellationToken));
        }

        public async Task UpdateAsync(Booking booking, CancellationToken cancellationToken)
        {
            const string sql = @"
UPDATE dbo.Bookings
SET
    CustomerId = @CustomerId,
    CustomerIdentifier = @CustomerIdentifier,
    ProcessedEmailId = @ProcessedEmailId,
    MessageId = @MessageId,
    VendorName = @VendorName,
    EmailType = @EmailType,
    IsCancellation = @IsCancellation,
    IsModification = @IsModification,
    IsConfirmation = @IsConfirmation,
    IsActive = @IsActive,
    TourName = @TourName,
    TourDate = @TourDate,
    TourDayOfWeek = @TourDayOfWeek,
    TourTime = @TourTime,
    TourLocation = @TourLocation,
    TourTimeZone = @TourTimeZone,
    DisplayDate = @DisplayDate,
    DisplayTime = @DisplayTime,
    CustomerName = @CustomerName,
    CustomerEmail = @CustomerEmail,
    CustomerPhone = @CustomerPhone,
    NumberOfAttendees = @NumberOfAttendees,
    NumberOfAdults = @NumberOfAdults,
    NumberOfChildren = @NumberOfChildren,
    NumberOfInfants = @NumberOfInfants,
    Language = @Language,
    CountryOfOrigin = @CountryOfOrigin,
    BookingStatus = @BookingStatus,
    BookingAmount = @BookingAmount,
    Currency = @Currency,
    SpecialRequests = @SpecialRequests,
    GuideAssigned = @GuideAssigned,
    CalendarEventId = @CalendarEventId,
    IsCalendarExportSuccess = @IsCalendarExportSuccess,
    IsSheetExportSuccess = @IsSheetExportSuccess,
    ProcessingNotes = @ProcessingNotes,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id;";
            using var conn = CreateConnection();
            await conn.ExecuteAsync(new CommandDefinition(sql, booking, cancellationToken: cancellationToken));
        }

        public async Task DeactivateOriginalOnModificationAsync(string originalBookingCode, string newBookingCode, CancellationToken cancellationToken)
        {
            const string sql = @"
UPDATE dbo.Bookings
SET IsActive = 0, UpdatedAt = SYSUTCDATETIME()
WHERE BookingCode = @OriginalBookingCode;";
            using var conn = CreateConnection();
            await conn.ExecuteAsync(new CommandDefinition(sql, new { OriginalBookingCode = originalBookingCode }, cancellationToken: cancellationToken));
        }

        /// <summary>
        /// Cancels a booking by booking code. Returns the number of rows affected.
        /// If 0 rows affected, the booking was not found.
        /// </summary>
        public async Task<int> CancelAsync(string bookingCode, string? cancellationReason, CancellationToken cancellationToken)
        {
            const string sql = @"
UPDATE dbo.Bookings
SET IsActive = 0,
    IsCancellation = 1,
    BookingStatus = 'Cancelled',
    ProcessingNotes = COALESCE(ProcessingNotes,'') + CASE WHEN @Reason IS NULL THEN '' ELSE CHAR(13)+CHAR(10)+'Cancel: '+@Reason END,
    UpdatedAt = SYSUTCDATETIME()
WHERE BookingCode = @BookingCode;";
            using var conn = CreateConnection();
            var rowsAffected = await conn.ExecuteAsync(new CommandDefinition(sql, new { BookingCode = bookingCode, Reason = cancellationReason }, cancellationToken: cancellationToken));
            return rowsAffected;
        }

        public async Task<IReadOnlyList<Booking>> GetRecentAsync(int limit, CancellationToken cancellationToken)
        {
            const string sql = @"
SELECT TOP (@Limit) *
FROM dbo.Bookings
ORDER BY UpdatedAt DESC, Id DESC;";
            using var conn = CreateConnection();
            var rows = await conn.QueryAsync<Booking>(new CommandDefinition(sql, new { Limit = limit }, cancellationToken: cancellationToken));
            return rows.ToList();
        }

        public async Task DeleteAsync(IReadOnlyList<int> bookingIds, CancellationToken cancellationToken)
        {
            if (bookingIds == null || bookingIds.Count == 0) return;
            const string sql = @"DELETE FROM dbo.Bookings WHERE Id IN @Ids";
            using var conn = CreateConnection();
            await conn.ExecuteAsync(new CommandDefinition(sql, new { Ids = bookingIds }, cancellationToken: cancellationToken));
        }
    }
}


