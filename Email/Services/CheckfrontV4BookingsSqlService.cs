using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Text.Json;
using Email.Models;
using Microsoft.Data.SqlClient;

namespace Email.Services
{
    /// <summary>
    /// Created: 2026-04-03 00:00 UTC
    /// Inserts Checkfront v4 booking snapshots into dbo.CheckfrontV4Bookings.
    /// </summary>
    public sealed class CheckfrontV4BookingsSqlService
    {
        private readonly SqlConnectionFactory _connectionFactory;
        private readonly ILogger<CheckfrontV4BookingsSqlService> _logger;

        public CheckfrontV4BookingsSqlService(
            SqlConnectionFactory connectionFactory,
            ILogger<CheckfrontV4BookingsSqlService> logger)
        {
            _connectionFactory = connectionFactory;
            _logger = logger;
        }

        public async Task<int> InsertSingleAsync(
            CheckfrontV4Booking booking,
            string? sourceEndpoint,
            CancellationToken ct = default)
        {
            if (booking == null)
            {
                throw new ArgumentNullException(nameof(booking));
            }

            var operationId = Guid.NewGuid().ToString("N");
            var nowUtc = DateTime.UtcNow;
            var customerPhone = ResolveCustomerPhone(booking);
            var snapshotJson = string.IsNullOrWhiteSpace(booking.RawJson)
                ? JsonSerializer.Serialize(booking)
                : booking.RawJson;

            const string sql = @"
INSERT INTO dbo.CheckfrontV4Bookings
(
    CheckfrontBookingId,
    BookingCode,
    CreatedAtLocal,
    StartAtLocal,
    EndAtLocal,
    CheckInAtLocal,
    CheckOutAtLocal,
    CustomerId,
    CustomerCode,
    CustomerFirstName,
    CustomerLastName,
    CustomerEmail,
    CustomerPhone,
    Language,
    SubTotal,
    InclusiveTaxTotal,
    TaxTotal,
    Total,
    PaidTotal,
    StatusId,
    StatusName,
    ItemSummary,
    DiscountCode,
    AccountId,
    PartnerId,
    Cfx,
    Gcfx,
    FieldsJson,
    StatusJson,
    CustomerJson,
    NotesJson,
    SnapshotJson,
    SourceEndpoint,
    PulledAtUtc,
    UpdatedAtUtc
)
OUTPUT inserted.Id
VALUES
(
    @CheckfrontBookingId,
    @BookingCode,
    @CreatedAtLocal,
    @StartAtLocal,
    @EndAtLocal,
    @CheckInAtLocal,
    @CheckOutAtLocal,
    @CustomerId,
    @CustomerCode,
    @CustomerFirstName,
    @CustomerLastName,
    @CustomerEmail,
    @CustomerPhone,
    @Language,
    @SubTotal,
    @InclusiveTaxTotal,
    @TaxTotal,
    @Total,
    @PaidTotal,
    @StatusId,
    @StatusName,
    @ItemSummary,
    @DiscountCode,
    @AccountId,
    @PartnerId,
    @Cfx,
    @Gcfx,
    @FieldsJson,
    @StatusJson,
    @CustomerJson,
    @NotesJson,
    @SnapshotJson,
    @SourceEndpoint,
    @PulledAtUtc,
    @UpdatedAtUtc
);";

            using var conn = _connectionFactory.CreateOpenConnection();
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@CheckfrontBookingId", SqlDbType.NVarChar, 50) { Value = booking.Id?.Trim() ?? string.Empty });
            cmd.Parameters.Add(new SqlParameter("@BookingCode", SqlDbType.NVarChar, 100) { Value = booking.Code?.Trim() ?? string.Empty });
            cmd.Parameters.Add(new SqlParameter("@CreatedAtLocal", SqlDbType.DateTimeOffset) { Value = (object?)ParseDateTimeOffset(booking.Created) ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@StartAtLocal", SqlDbType.DateTimeOffset) { Value = (object?)ParseDateTimeOffset(booking.Start) ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@EndAtLocal", SqlDbType.DateTimeOffset) { Value = (object?)ParseDateTimeOffset(booking.End) ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@CheckInAtLocal", SqlDbType.DateTimeOffset) { Value = (object?)ParseDateTimeOffset(booking.CheckIn) ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@CheckOutAtLocal", SqlDbType.DateTimeOffset) { Value = (object?)ParseDateTimeOffset(booking.CheckOut) ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@CustomerId", SqlDbType.Int) { Value = booking.CustomerId });
            cmd.Parameters.Add(new SqlParameter("@CustomerCode", SqlDbType.NVarChar, 100) { Value = (object?)booking.Customer?.Code ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@CustomerFirstName", SqlDbType.NVarChar, 150) { Value = (object?)NullIfWhitespace(booking.FirstName) ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@CustomerLastName", SqlDbType.NVarChar, 150) { Value = (object?)NullIfWhitespace(booking.LastName) ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@CustomerEmail", SqlDbType.NVarChar, 320) { Value = (object?)NullIfWhitespace(booking.Email) ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@CustomerPhone", SqlDbType.NVarChar, 50) { Value = (object?)NullIfWhitespace(customerPhone) ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@Language", SqlDbType.NVarChar, 20) { Value = (object?)NullIfWhitespace(booking.Language) ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@SubTotal", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = booking.SubTotal });
            cmd.Parameters.Add(new SqlParameter("@InclusiveTaxTotal", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = booking.InclusiveTaxTotal });
            cmd.Parameters.Add(new SqlParameter("@TaxTotal", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = booking.TaxTotal });
            cmd.Parameters.Add(new SqlParameter("@Total", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = booking.Total });
            cmd.Parameters.Add(new SqlParameter("@PaidTotal", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = booking.PaidTotal });
            cmd.Parameters.Add(new SqlParameter("@StatusId", SqlDbType.NVarChar, 50) { Value = (object?)NullIfWhitespace(booking.StatusId) ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@StatusName", SqlDbType.NVarChar, 150) { Value = (object?)NullIfWhitespace(booking.Status?.Name) ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@ItemSummary", SqlDbType.NVarChar, 1000) { Value = (object?)NullIfWhitespace(booking.ItemSummary) ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@DiscountCode", SqlDbType.NVarChar, 100) { Value = (object?)NullIfWhitespace(booking.DiscountCode) ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@AccountId", SqlDbType.Int) { Value = booking.AccountId });
            cmd.Parameters.Add(new SqlParameter("@PartnerId", SqlDbType.NVarChar, 100) { Value = (object?)NullIfWhitespace(booking.PartnerId) ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@Cfx", SqlDbType.NVarChar, 256) { Value = (object?)NullIfWhitespace(booking.Cfx) ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@Gcfx", SqlDbType.NVarChar, 256) { Value = (object?)NullIfWhitespace(booking.Gcfx) ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@FieldsJson", SqlDbType.NVarChar, -1) { Value = (object?)SerializeJsonOrNull(booking.Fields) ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@StatusJson", SqlDbType.NVarChar, -1) { Value = (object?)SerializeJsonOrNull(booking.Status) ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@CustomerJson", SqlDbType.NVarChar, -1) { Value = (object?)SerializeJsonOrNull(booking.Customer) ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@NotesJson", SqlDbType.NVarChar, -1) { Value = (object?)SerializeJsonOrNull(booking.Notes) ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@SnapshotJson", SqlDbType.NVarChar, -1) { Value = snapshotJson });
            cmd.Parameters.Add(new SqlParameter("@SourceEndpoint", SqlDbType.NVarChar, 500) { Value = (object?)NullIfWhitespace(sourceEndpoint) ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@PulledAtUtc", SqlDbType.DateTime2) { Value = nowUtc });
            cmd.Parameters.Add(new SqlParameter("@UpdatedAtUtc", SqlDbType.DateTime2) { Value = nowUtc });

            var insertedId = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
            _logger.LogInformation(
                "Inserted CheckfrontV4Bookings row. OperationId={OperationId} RowId={RowId} BookingId={BookingId} BookingCode={BookingCode}",
                operationId,
                insertedId,
                booking.Id,
                booking.Code);
            Console.WriteLine(
                $"[CheckfrontTestPage] {DateTime.UtcNow:O} Operation=PersistV4Booking OperationId={operationId} End InsertedRowId={insertedId} BookingId={booking.Id} BookingCode={booking.Code}");
            return insertedId;
        }

        /// <summary>
        /// Created: 2026-04-04 00:00 UTC
        /// Returns the latest snapshot row per booking code from dbo.CheckfrontV4Bookings.
        /// </summary>
        public async Task<IReadOnlyList<DbCheckfrontV4Booking>> GetLatestSnapshotsByBookingCodeAsync(
            int daysBack,
            int limit,
            CancellationToken ct = default)
        {
            var safeDaysBack = Math.Clamp(daysBack <= 0 ? 90 : daysBack, 1, 3650);
            var safeLimit = Math.Clamp(limit <= 0 ? 500 : limit, 1, 5000);
            var pulledAtUtcMin = DateTime.UtcNow.AddDays(-safeDaysBack);

            const string sql = @"
;WITH ranked AS
(
    SELECT
        Id,
        CheckfrontBookingId,
        BookingCode,
        CreatedAtLocal,
        StartAtLocal,
        EndAtLocal,
        CheckInAtLocal,
        CheckOutAtLocal,
        CustomerId,
        CustomerCode,
        CustomerFirstName,
        CustomerLastName,
        CustomerEmail,
        CustomerPhone,
        Language,
        SubTotal,
        InclusiveTaxTotal,
        TaxTotal,
        Total,
        PaidTotal,
        StatusId,
        StatusName,
        ItemSummary,
        DiscountCode,
        AccountId,
        PartnerId,
        Cfx,
        Gcfx,
        FieldsJson,
        StatusJson,
        CustomerJson,
        NotesJson,
        SnapshotJson,
        SourceEndpoint,
        PulledAtUtc,
        UpdatedAtUtc,
        ROW_NUMBER() OVER (PARTITION BY BookingCode ORDER BY PulledAtUtc DESC, Id DESC) AS rn
    FROM dbo.CheckfrontV4Bookings
    WHERE
        NULLIF(LTRIM(RTRIM(ISNULL(BookingCode, ''))), '') IS NOT NULL
        AND PulledAtUtc >= @PulledAtUtcMin
)
SELECT TOP (@Limit)
    Id,
    CheckfrontBookingId,
    BookingCode,
    CreatedAtLocal,
    StartAtLocal,
    EndAtLocal,
    CheckInAtLocal,
    CheckOutAtLocal,
    CustomerId,
    CustomerCode,
    CustomerFirstName,
    CustomerLastName,
    CustomerEmail,
    CustomerPhone,
    Language,
    SubTotal,
    InclusiveTaxTotal,
    TaxTotal,
    Total,
    PaidTotal,
    StatusId,
    StatusName,
    ItemSummary,
    DiscountCode,
    AccountId,
    PartnerId,
    Cfx,
    Gcfx,
    FieldsJson,
    StatusJson,
    CustomerJson,
    NotesJson,
    SnapshotJson,
    SourceEndpoint,
    PulledAtUtc,
    UpdatedAtUtc
FROM ranked
WHERE rn = 1
ORDER BY PulledAtUtc DESC, Id DESC;";

            var rows = new List<DbCheckfrontV4Booking>();
            using var conn = _connectionFactory.CreateOpenConnection();
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@PulledAtUtcMin", SqlDbType.DateTime2) { Value = pulledAtUtcMin });
            cmd.Parameters.Add(new SqlParameter("@Limit", SqlDbType.Int) { Value = safeLimit });

            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(ReadSnapshotRow(reader));
            }

            _logger.LogInformation(
                "Loaded latest CheckfrontV4 snapshot rows. DaysBack={DaysBack} Limit={Limit} ResultRows={Rows}",
                safeDaysBack,
                safeLimit,
                rows.Count);

            return rows;
        }

        /// <summary>
        /// Created: 2026-04-04 00:00 UTC
        /// Returns the most recent non-empty item summary for a booking code.
        /// Used as a fallback when the latest snapshot has blank itemSummary.
        /// </summary>
        public async Task<string?> GetLatestNonEmptyItemSummaryByBookingCodeAsync(
            string bookingCode,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(bookingCode))
            {
                return null;
            }

            const string sql = @"
SELECT TOP (1) ItemSummary
FROM dbo.CheckfrontV4Bookings
WHERE BookingCode = @BookingCode
  AND NULLIF(LTRIM(RTRIM(ISNULL(ItemSummary, ''))), '') IS NOT NULL
ORDER BY PulledAtUtc DESC, Id DESC;";

            using var conn = _connectionFactory.CreateOpenConnection();
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@BookingCode", SqlDbType.NVarChar, 100) { Value = bookingCode.Trim() });

            var scalar = await cmd.ExecuteScalarAsync(ct);
            if (scalar == null || scalar == DBNull.Value)
            {
                return null;
            }

            return NullIfWhitespace(Convert.ToString(scalar, CultureInfo.InvariantCulture));
        }

        private static string? SerializeJsonOrNull<T>(T value)
        {
            if (value == null)
            {
                return null;
            }

            return JsonSerializer.Serialize(value);
        }

        private static string? NullIfWhitespace(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static DateTimeOffset? ParseDateTimeOffset(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return DateTimeOffset.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : null;
        }

        private static string ResolveCustomerPhone(CheckfrontV4Booking booking)
        {
            if (booking.Fields != null &&
                booking.Fields.TryGetValue("customer_phone", out var phoneElement))
            {
                return phoneElement.ValueKind == JsonValueKind.String
                    ? phoneElement.GetString() ?? string.Empty
                    : phoneElement.ToString();
            }

            if (booking.Customer?.Fields != null &&
                booking.Customer.Fields.TryGetValue("customer_phone", out var nestedPhoneElement))
            {
                return nestedPhoneElement.ValueKind == JsonValueKind.String
                    ? nestedPhoneElement.GetString() ?? string.Empty
                    : nestedPhoneElement.ToString();
            }

            return string.Empty;
        }

        private static DbCheckfrontV4Booking ReadSnapshotRow(SqlDataReader reader)
        {
            var id = reader.GetOrdinal("Id");
            var checkfrontBookingId = reader.GetOrdinal("CheckfrontBookingId");
            var bookingCode = reader.GetOrdinal("BookingCode");
            var createdAtLocal = reader.GetOrdinal("CreatedAtLocal");
            var startAtLocal = reader.GetOrdinal("StartAtLocal");
            var endAtLocal = reader.GetOrdinal("EndAtLocal");
            var checkInAtLocal = reader.GetOrdinal("CheckInAtLocal");
            var checkOutAtLocal = reader.GetOrdinal("CheckOutAtLocal");
            var customerId = reader.GetOrdinal("CustomerId");
            var customerCode = reader.GetOrdinal("CustomerCode");
            var customerFirstName = reader.GetOrdinal("CustomerFirstName");
            var customerLastName = reader.GetOrdinal("CustomerLastName");
            var customerEmail = reader.GetOrdinal("CustomerEmail");
            var customerPhone = reader.GetOrdinal("CustomerPhone");
            var language = reader.GetOrdinal("Language");
            var subTotal = reader.GetOrdinal("SubTotal");
            var inclusiveTaxTotal = reader.GetOrdinal("InclusiveTaxTotal");
            var taxTotal = reader.GetOrdinal("TaxTotal");
            var total = reader.GetOrdinal("Total");
            var paidTotal = reader.GetOrdinal("PaidTotal");
            var statusId = reader.GetOrdinal("StatusId");
            var statusName = reader.GetOrdinal("StatusName");
            var itemSummary = reader.GetOrdinal("ItemSummary");
            var discountCode = reader.GetOrdinal("DiscountCode");
            var accountId = reader.GetOrdinal("AccountId");
            var partnerId = reader.GetOrdinal("PartnerId");
            var cfx = reader.GetOrdinal("Cfx");
            var gcfx = reader.GetOrdinal("Gcfx");
            var fieldsJson = reader.GetOrdinal("FieldsJson");
            var statusJson = reader.GetOrdinal("StatusJson");
            var customerJson = reader.GetOrdinal("CustomerJson");
            var notesJson = reader.GetOrdinal("NotesJson");
            var snapshotJson = reader.GetOrdinal("SnapshotJson");
            var sourceEndpoint = reader.GetOrdinal("SourceEndpoint");
            var pulledAtUtc = reader.GetOrdinal("PulledAtUtc");
            var updatedAtUtc = reader.GetOrdinal("UpdatedAtUtc");

            return new DbCheckfrontV4Booking
            {
                Id = reader.GetInt32(id),
                CheckfrontBookingId = reader.IsDBNull(checkfrontBookingId) ? string.Empty : reader.GetString(checkfrontBookingId),
                BookingCode = reader.IsDBNull(bookingCode) ? string.Empty : reader.GetString(bookingCode),
                CreatedAtLocal = reader.IsDBNull(createdAtLocal) ? null : reader.GetFieldValue<DateTimeOffset>(createdAtLocal),
                StartAtLocal = reader.IsDBNull(startAtLocal) ? null : reader.GetFieldValue<DateTimeOffset>(startAtLocal),
                EndAtLocal = reader.IsDBNull(endAtLocal) ? null : reader.GetFieldValue<DateTimeOffset>(endAtLocal),
                CheckInAtLocal = reader.IsDBNull(checkInAtLocal) ? null : reader.GetFieldValue<DateTimeOffset>(checkInAtLocal),
                CheckOutAtLocal = reader.IsDBNull(checkOutAtLocal) ? null : reader.GetFieldValue<DateTimeOffset>(checkOutAtLocal),
                CustomerId = reader.IsDBNull(customerId) ? null : reader.GetInt32(customerId),
                CustomerCode = reader.IsDBNull(customerCode) ? null : reader.GetString(customerCode),
                CustomerFirstName = reader.IsDBNull(customerFirstName) ? null : reader.GetString(customerFirstName),
                CustomerLastName = reader.IsDBNull(customerLastName) ? null : reader.GetString(customerLastName),
                CustomerEmail = reader.IsDBNull(customerEmail) ? null : reader.GetString(customerEmail),
                CustomerPhone = reader.IsDBNull(customerPhone) ? null : reader.GetString(customerPhone),
                Language = reader.IsDBNull(language) ? null : reader.GetString(language),
                SubTotal = reader.IsDBNull(subTotal) ? null : reader.GetDecimal(subTotal),
                InclusiveTaxTotal = reader.IsDBNull(inclusiveTaxTotal) ? null : reader.GetDecimal(inclusiveTaxTotal),
                TaxTotal = reader.IsDBNull(taxTotal) ? null : reader.GetDecimal(taxTotal),
                Total = reader.IsDBNull(total) ? null : reader.GetDecimal(total),
                PaidTotal = reader.IsDBNull(paidTotal) ? null : reader.GetDecimal(paidTotal),
                StatusId = reader.IsDBNull(statusId) ? null : reader.GetString(statusId),
                StatusName = reader.IsDBNull(statusName) ? null : reader.GetString(statusName),
                ItemSummary = reader.IsDBNull(itemSummary) ? null : reader.GetString(itemSummary),
                DiscountCode = reader.IsDBNull(discountCode) ? null : reader.GetString(discountCode),
                AccountId = reader.IsDBNull(accountId) ? null : reader.GetInt32(accountId),
                PartnerId = reader.IsDBNull(partnerId) ? null : reader.GetString(partnerId),
                Cfx = reader.IsDBNull(cfx) ? null : reader.GetString(cfx),
                Gcfx = reader.IsDBNull(gcfx) ? null : reader.GetString(gcfx),
                FieldsJson = reader.IsDBNull(fieldsJson) ? null : reader.GetString(fieldsJson),
                StatusJson = reader.IsDBNull(statusJson) ? null : reader.GetString(statusJson),
                CustomerJson = reader.IsDBNull(customerJson) ? null : reader.GetString(customerJson),
                NotesJson = reader.IsDBNull(notesJson) ? null : reader.GetString(notesJson),
                SnapshotJson = reader.IsDBNull(snapshotJson) ? string.Empty : reader.GetString(snapshotJson),
                SourceEndpoint = reader.IsDBNull(sourceEndpoint) ? null : reader.GetString(sourceEndpoint),
                PulledAtUtc = reader.GetDateTime(pulledAtUtc),
                UpdatedAtUtc = reader.GetDateTime(updatedAtUtc)
            };
        }
    }
}
