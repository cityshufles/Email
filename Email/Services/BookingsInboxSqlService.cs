using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Email.Models.Reports;

namespace Email.Services
{
    public sealed class BookingsInboxSqlService : IBookingsInboxService
    {
        private readonly SqlConnectionFactory _connectionFactory;
        private readonly IGuideReportService _guideReportService;
        private readonly IGalleryLinkResolver _galleryResolver;

        public BookingsInboxSqlService(
            SqlConnectionFactory connectionFactory,
            IGuideReportService guideReportService,
            IGalleryLinkResolver galleryResolver)
        {
            _connectionFactory = connectionFactory;
            _guideReportService = guideReportService;
            _galleryResolver = galleryResolver;
        }

        public async Task<IReadOnlyList<BookingsLiveListItem>> GetRecentBookingsAsync(
            int limit = 100,
            IEnumerable<string>? vendors = null,
            string orderBy = "booking_date",
            CancellationToken ct = default)
        {
            var safeLimit = Math.Clamp(limit <= 0 ? 100 : limit, 1, 5000);
            var normalizedOrder = NormalizeListOrder(orderBy);
            var vendorList = vendors?
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();

            using var conn = _connectionFactory.CreateOpenConnection();

            var sql = @"
SELECT TOP (@Limit)
    b.Id AS BookingId,
    b.CustomerId,
    ISNULL(b.CustomerIdentifier, '') AS CustomerIdentifier,
    ISNULL(b.CustomerName, '') AS CustomerName,
    b.CustomerPhone,
    b.CustomerEmail,
    ISNULL(b.BookingCode, '') AS BookingCode,
    ISNULL(b.MessageId, '') AS MessageId,
    ISNULL(b.VendorName, '') AS VendorName,
    ISNULL(b.TourName, '') AS TourName,
    b.TourDate,
    b.TourTime,
    b.IsCancellation,
    b.IsModification,
    b.IsConfirmation,
    b.IsActive,
    b.CreatedAt,
    b.UpdatedAt,
    COALESCE(b.UpdatedAt, b.CreatedAt) AS ActivityAtUtc,
    CAST(CASE WHEN EXISTS (
        SELECT 1
        FROM dbo.CustomerContactLabels ccl
        WHERE ccl.CustomerId = b.CustomerId
          AND ccl.LabelKey = 'needs_number'
          AND ccl.IsActive = 1
    ) THEN 1 ELSE 0 END AS bit) AS NeedsNumber
FROM dbo.Bookings b";

            if (vendorList.Count > 0)
            {
                sql += "\nWHERE b.VendorName IN @Vendors";
            }

            sql += normalizedOrder switch
            {
                "tour_date" => "\nORDER BY b.TourDate DESC, COALESCE(b.UpdatedAt, b.CreatedAt) DESC, b.Id DESC;",
                "booking_date" => "\nORDER BY b.CreatedAt DESC, b.Id DESC;",
                _ => "\nORDER BY COALESCE(b.UpdatedAt, b.CreatedAt) DESC, b.Id DESC;"
            };

            var rows = await conn.QueryAsync<BookingsLiveListItem>(
                new CommandDefinition(
                    sql,
                    new { Limit = safeLimit, Vendors = vendorList },
                    cancellationToken: ct));

            return rows.ToList();
        }

        public async Task<CustomerCommunicationProfile?> GetWalkerProfileAsync(
            int bookingId,
            CancellationToken ct = default)
        {
            using var conn = _connectionFactory.CreateOpenConnection();

            const string anchorSql = @"
SELECT TOP 1
    b.Id AS BookingId,
    b.CustomerId,
    ISNULL(b.CustomerIdentifier, '') AS CustomerIdentifier,
    ISNULL(b.CustomerName, '') AS CustomerName,
    b.CustomerPhone,
    b.CustomerEmail,
    ISNULL(b.BookingCode, '') AS BookingCode,
    ISNULL(b.MessageId, '') AS MessageId,
    ISNULL(b.VendorName, '') AS VendorName,
    ISNULL(b.TourName, '') AS TourName,
    b.TourDate,
    b.TourTime,
    b.IsCancellation,
    b.IsModification,
    b.IsConfirmation,
    b.IsActive,
    b.CreatedAt,
    b.UpdatedAt,
    COALESCE(b.UpdatedAt, b.CreatedAt) AS ActivityAtUtc,
    CAST(0 AS bit) AS NeedsNumber
FROM dbo.Bookings b
WHERE b.Id = @BookingId;";

            var selected = await conn.QueryFirstOrDefaultAsync<BookingsLiveListItem>(
                new CommandDefinition(anchorSql, new { BookingId = bookingId }, cancellationToken: ct));
            if (selected == null)
            {
                return null;
            }

            var related = await LoadRelatedBookingsAsync(conn, selected, ct);
            if (related.Count == 0)
            {
                related.Add(selected);
            }

            var labelsByCustomer = await GetCustomerLabelsAsync(new[] { selected.CustomerId }, ct);
            var labels = labelsByCustomer.TryGetValue(selected.CustomerId, out var profileLabels)
                ? profileLabels
                : new GuestContactLabels();
            selected.NeedsNumber = labels.NeedsNumber;

            var timeline = related
                .OrderByDescending(r => r.ActivityAtUtc)
                .ThenByDescending(r => r.BookingId)
                .Select(r => new BookingTimelineEvent
                {
                    BookingId = r.BookingId,
                    CustomerId = r.CustomerId,
                    CustomerIdentifier = r.CustomerIdentifier,
                    EventType = DetermineBookingEventType(r),
                    BookingCode = r.BookingCode,
                    MessageId = r.MessageId,
                    TourName = r.TourName,
                    TourDate = r.TourDate,
                    TourTime = r.TourTime,
                    VendorName = r.VendorName,
                    CustomerPhone = r.CustomerPhone,
                    CustomerEmail = r.CustomerEmail,
                    IsActive = r.IsActive,
                    OccurredAtUtc = r.ActivityAtUtc
                })
                .ToList();

            var commEvents = await LoadCommunicationEventsAsync(conn, selected, related, ct);
            var sentStageSnapshots = await LoadSentStageSnapshotsAsync(conn, related.Select(r => r.MessageId), ct);
            var sentStages = sentStageSnapshots
                .Where(s => !string.IsNullOrWhiteSpace(s.Stage))
                .Select(s => s.Stage)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var guideReport = await LoadGuideReportSummaryAsync(selected, ct);
            var gallery = await LoadGallerySummaryAsync(selected, ct);

            return new CustomerCommunicationProfile
            {
                SelectedBooking = selected,
                BookingTimeline = timeline,
                CommunicationEvents = commEvents,
                Labels = labels,
                GuideReport = guideReport,
                Gallery = gallery,
                SentStages = sentStages,
                SentStageSnapshots = sentStageSnapshots
            };
        }

        public async Task SetCustomerLabelAsync(
            int customerId,
            string labelKey,
            bool isActive,
            string? updatedBy = null,
            string? notes = null,
            CancellationToken ct = default)
        {
            if (customerId <= 0)
            {
                return;
            }

            var normalized = GuestContactLabels.NormalizeKey(labelKey);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            const string sql = @"
MERGE dbo.CustomerContactLabels AS target
USING (SELECT @CustomerId AS CustomerId, @LabelKey AS LabelKey) AS src
ON (target.CustomerId = src.CustomerId AND target.LabelKey = src.LabelKey)
WHEN MATCHED THEN
    UPDATE SET
        IsActive = @IsActive,
        UpdatedBy = @UpdatedBy,
        Notes = @Notes,
        UpdatedAtUtc = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT (CustomerId, LabelKey, IsActive, UpdatedBy, Notes, CreatedAtUtc, UpdatedAtUtc)
    VALUES (@CustomerId, @LabelKey, @IsActive, @UpdatedBy, @Notes, SYSUTCDATETIME(), SYSUTCDATETIME());";

            using var conn = _connectionFactory.CreateOpenConnection();
            await conn.ExecuteAsync(new CommandDefinition(
                sql,
                new
                {
                    CustomerId = customerId,
                    LabelKey = normalized,
                    IsActive = isActive,
                    UpdatedBy = string.IsNullOrWhiteSpace(updatedBy) ? null : updatedBy.Trim(),
                    Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
                },
                cancellationToken: ct));
        }

        public async Task LogMessageEventAsync(
            BookingMessageEventWriteModel evt,
            CancellationToken ct = default)
        {
            if (evt == null)
            {
                return;
            }

            var hydrated = await HydrateMessageEventAsync(evt, ct);

            const string sql = @"
INSERT INTO dbo.BookingMessageEvents
(
    CustomerId,
    CustomerIdentifier,
    BookingId,
    BookingCode,
    MessageId,
    TourName,
    TourDate,
    TourTime,
    VendorName,
    Stage,
    Channel,
    TemplateId,
    TemplateType,
    TemplateName,
    TriggerType,
    TriggeredBy,
    SentAtUtc,
    CreatedAtUtc
)
VALUES
(
    @CustomerId,
    @CustomerIdentifier,
    @BookingId,
    @BookingCode,
    @MessageId,
    @TourName,
    @TourDate,
    @TourTime,
    @VendorName,
    @Stage,
    @Channel,
    @TemplateId,
    @TemplateType,
    @TemplateName,
    @TriggerType,
    @TriggeredBy,
    @SentAtUtc,
    SYSUTCDATETIME()
);";

            using var conn = _connectionFactory.CreateOpenConnection();
            await conn.ExecuteAsync(new CommandDefinition(
                sql,
                new
                {
                    hydrated.CustomerId,
                    hydrated.CustomerIdentifier,
                    hydrated.BookingId,
                    hydrated.BookingCode,
                    hydrated.MessageId,
                    hydrated.TourName,
                    hydrated.TourDate,
                    hydrated.TourTime,
                    hydrated.VendorName,
                    Stage = NormalizeStage(hydrated.Stage),
                    Channel = NormalizeChannel(hydrated.Channel),
                    hydrated.TemplateId,
                    hydrated.TemplateType,
                    hydrated.TemplateName,
                    TriggerType = NormalizeTriggerType(hydrated.TriggerType),
                    hydrated.TriggeredBy,
                    SentAtUtc = hydrated.SentAtUtc ?? DateTime.UtcNow
                },
                cancellationToken: ct));
        }

        public async Task<Dictionary<int, GuestContactLabels>> GetCustomerLabelsAsync(
            IEnumerable<int> customerIds,
            CancellationToken ct = default)
        {
            var ids = customerIds?
                .Where(id => id > 0)
                .Distinct()
                .ToList() ?? new List<int>();
            var result = ids.ToDictionary(id => id, _ => new GuestContactLabels());
            if (ids.Count == 0)
            {
                return result;
            }

            const string sql = @"
SELECT CustomerId, LabelKey, IsActive
FROM dbo.CustomerContactLabels
WHERE CustomerId IN @Ids;";

            using var conn = _connectionFactory.CreateOpenConnection();
            var rows = await conn.QueryAsync<CustomerLabelRow>(new CommandDefinition(
                sql,
                new { Ids = ids },
                cancellationToken: ct));

            foreach (var row in rows)
            {
                if (!result.TryGetValue(row.CustomerId, out var labels))
                {
                    labels = new GuestContactLabels();
                    result[row.CustomerId] = labels;
                }

                labels.SetByKey(row.LabelKey, row.IsActive);
            }

            return result;
        }

        private async Task<List<BookingsLiveListItem>> LoadRelatedBookingsAsync(
            IDbConnection conn,
            BookingsLiveListItem selected,
            CancellationToken ct)
        {
            if (selected.CustomerId > 0)
            {
                const string sqlByCustomerId = @"
SELECT
    b.Id AS BookingId,
    b.CustomerId,
    ISNULL(b.CustomerIdentifier, '') AS CustomerIdentifier,
    ISNULL(b.CustomerName, '') AS CustomerName,
    b.CustomerPhone,
    b.CustomerEmail,
    ISNULL(b.BookingCode, '') AS BookingCode,
    ISNULL(b.MessageId, '') AS MessageId,
    ISNULL(b.VendorName, '') AS VendorName,
    ISNULL(b.TourName, '') AS TourName,
    b.TourDate,
    b.TourTime,
    b.IsCancellation,
    b.IsModification,
    b.IsConfirmation,
    b.IsActive,
    b.CreatedAt,
    b.UpdatedAt,
    COALESCE(b.UpdatedAt, b.CreatedAt) AS ActivityAtUtc,
    CAST(0 AS bit) AS NeedsNumber
FROM dbo.Bookings b
WHERE b.CustomerId = @CustomerId;";

                var rowsByCustomerId = await conn.QueryAsync<BookingsLiveListItem>(
                    new CommandDefinition(sqlByCustomerId, new { selected.CustomerId }, cancellationToken: ct));
                return rowsByCustomerId.ToList();
            }

            if (!string.IsNullOrWhiteSpace(selected.CustomerIdentifier))
            {
                const string sqlByIdentifier = @"
SELECT
    b.Id AS BookingId,
    b.CustomerId,
    ISNULL(b.CustomerIdentifier, '') AS CustomerIdentifier,
    ISNULL(b.CustomerName, '') AS CustomerName,
    b.CustomerPhone,
    b.CustomerEmail,
    ISNULL(b.BookingCode, '') AS BookingCode,
    ISNULL(b.MessageId, '') AS MessageId,
    ISNULL(b.VendorName, '') AS VendorName,
    ISNULL(b.TourName, '') AS TourName,
    b.TourDate,
    b.TourTime,
    b.IsCancellation,
    b.IsModification,
    b.IsConfirmation,
    b.IsActive,
    b.CreatedAt,
    b.UpdatedAt,
    COALESCE(b.UpdatedAt, b.CreatedAt) AS ActivityAtUtc,
    CAST(0 AS bit) AS NeedsNumber
FROM dbo.Bookings b
WHERE b.CustomerIdentifier = @CustomerIdentifier;";

                var rowsByIdentifier = await conn.QueryAsync<BookingsLiveListItem>(
                    new CommandDefinition(sqlByIdentifier, new { selected.CustomerIdentifier }, cancellationToken: ct));
                return rowsByIdentifier.ToList();
            }

            var phoneDigits = NormalizePhone(selected.CustomerPhone);
            var email = NormalizeEmail(selected.CustomerEmail);
            if (string.IsNullOrWhiteSpace(phoneDigits) && string.IsNullOrWhiteSpace(email))
            {
                return new List<BookingsLiveListItem> { selected };
            }

            const string sqlByFallback = @"
SELECT
    b.Id AS BookingId,
    b.CustomerId,
    ISNULL(b.CustomerIdentifier, '') AS CustomerIdentifier,
    ISNULL(b.CustomerName, '') AS CustomerName,
    b.CustomerPhone,
    b.CustomerEmail,
    ISNULL(b.BookingCode, '') AS BookingCode,
    ISNULL(b.MessageId, '') AS MessageId,
    ISNULL(b.VendorName, '') AS VendorName,
    ISNULL(b.TourName, '') AS TourName,
    b.TourDate,
    b.TourTime,
    b.IsCancellation,
    b.IsModification,
    b.IsConfirmation,
    b.IsActive,
    b.CreatedAt,
    b.UpdatedAt,
    COALESCE(b.UpdatedAt, b.CreatedAt) AS ActivityAtUtc,
    CAST(0 AS bit) AS NeedsNumber
FROM dbo.Bookings b
WHERE
    (@PhoneDigits <> '' AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(b.CustomerPhone, ''), ' ', ''), '-', ''), '(', ''), ')', ''), '+', '') = @PhoneDigits)
    OR (@Email <> '' AND LOWER(LTRIM(RTRIM(ISNULL(b.CustomerEmail, '')))) = @Email);";

            var rowsByFallback = await conn.QueryAsync<BookingsLiveListItem>(
                new CommandDefinition(sqlByFallback, new { PhoneDigits = phoneDigits, Email = email }, cancellationToken: ct));
            var rows = rowsByFallback.ToList();
            if (rows.Count == 0)
            {
                rows.Add(selected);
            }

            return rows;
        }

        private async Task<List<CommunicationEvent>> LoadCommunicationEventsAsync(
            IDbConnection conn,
            BookingsLiveListItem selected,
            List<BookingsLiveListItem> relatedBookings,
            CancellationToken ct)
        {
            var bookingIds = relatedBookings.Select(b => b.BookingId).Distinct().ToList();
            var messageIds = relatedBookings
                .Select(b => b.MessageId)
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            string sql;
            object parameters;

            if (selected.CustomerId > 0)
            {
                sql = @"
SELECT
    e.Id,
    e.CustomerId,
    e.CustomerIdentifier,
    e.BookingId,
    e.BookingCode,
    e.MessageId,
    e.TourName,
    e.TourDate,
    e.TourTime,
    e.VendorName,
    e.Stage,
    e.Channel,
    e.TemplateId,
    e.TemplateType,
    COALESCE(e.TemplateName, tm.MessageName) AS TemplateName,
    e.TriggerType,
    e.TriggeredBy,
    e.SentAtUtc,
    e.CreatedAtUtc
FROM dbo.BookingMessageEvents e
LEFT JOIN dbo.TourMessages tm ON tm.Id = e.TemplateId
WHERE e.CustomerId = @CustomerId
ORDER BY e.SentAtUtc DESC, e.CreatedAtUtc DESC, e.Id DESC;";
                parameters = new { selected.CustomerId };
            }
            else if (!string.IsNullOrWhiteSpace(selected.CustomerIdentifier))
            {
                sql = @"
SELECT
    e.Id,
    e.CustomerId,
    e.CustomerIdentifier,
    e.BookingId,
    e.BookingCode,
    e.MessageId,
    e.TourName,
    e.TourDate,
    e.TourTime,
    e.VendorName,
    e.Stage,
    e.Channel,
    e.TemplateId,
    e.TemplateType,
    COALESCE(e.TemplateName, tm.MessageName) AS TemplateName,
    e.TriggerType,
    e.TriggeredBy,
    e.SentAtUtc,
    e.CreatedAtUtc
FROM dbo.BookingMessageEvents e
LEFT JOIN dbo.TourMessages tm ON tm.Id = e.TemplateId
WHERE e.CustomerIdentifier = @CustomerIdentifier
   OR e.BookingId IN @BookingIds
   OR e.MessageId IN @MessageIds
ORDER BY e.SentAtUtc DESC, e.CreatedAtUtc DESC, e.Id DESC;";
                parameters = new
                {
                    selected.CustomerIdentifier,
                    BookingIds = bookingIds.Count > 0 ? bookingIds : new List<int> { selected.BookingId },
                    MessageIds = messageIds.Count > 0 ? messageIds : new List<string> { selected.MessageId }
                };
            }
            else
            {
                sql = @"
SELECT
    e.Id,
    e.CustomerId,
    e.CustomerIdentifier,
    e.BookingId,
    e.BookingCode,
    e.MessageId,
    e.TourName,
    e.TourDate,
    e.TourTime,
    e.VendorName,
    e.Stage,
    e.Channel,
    e.TemplateId,
    e.TemplateType,
    COALESCE(e.TemplateName, tm.MessageName) AS TemplateName,
    e.TriggerType,
    e.TriggeredBy,
    e.SentAtUtc,
    e.CreatedAtUtc
FROM dbo.BookingMessageEvents e
LEFT JOIN dbo.TourMessages tm ON tm.Id = e.TemplateId
WHERE e.BookingId IN @BookingIds
   OR e.MessageId IN @MessageIds
ORDER BY e.SentAtUtc DESC, e.CreatedAtUtc DESC, e.Id DESC;";
                parameters = new
                {
                    BookingIds = bookingIds.Count > 0 ? bookingIds : new List<int> { selected.BookingId },
                    MessageIds = messageIds.Count > 0 ? messageIds : new List<string> { selected.MessageId }
                };
            }

            var rows = await conn.QueryAsync<CommunicationEvent>(new CommandDefinition(sql, parameters, cancellationToken: ct));
            return rows.ToList();
        }

        private async Task<List<MessageStageSnapshot>> LoadSentStageSnapshotsAsync(
            IDbConnection conn,
            IEnumerable<string> messageIds,
            CancellationToken ct)
        {
            var ids = messageIds
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (ids.Count == 0)
            {
                return new List<MessageStageSnapshot>();
            }

            const string sql = @"
SELECT Stage, MAX(SentAtUtc) AS SentAtUtc
FROM dbo.BookingMessageStatus
WHERE MessageId IN @Ids
  AND SentFlag = 1
GROUP BY Stage;";

            var snapshots = await conn.QueryAsync<MessageStageSnapshot>(
                new CommandDefinition(sql, new { Ids = ids }, cancellationToken: ct));

            return snapshots
                .Where(s => !string.IsNullOrWhiteSpace(s.Stage))
                .GroupBy(s => s.Stage, StringComparer.OrdinalIgnoreCase)
                .Select(g => g
                    .OrderByDescending(x => x.SentAtUtc ?? DateTime.MinValue)
                    .First())
                .ToList();
        }

        private async Task<WalkerGuideReportSummary> LoadGuideReportSummaryAsync(
            BookingsLiveListItem selected,
            CancellationToken ct)
        {
            var summary = new WalkerGuideReportSummary();
            if (!selected.TourDate.HasValue || string.IsNullOrWhiteSpace(selected.TourName) || string.IsNullOrWhiteSpace(selected.TourTime))
            {
                return summary;
            }

            var report = await _guideReportService.GetReportAsync(
                selected.TourDate.Value.Date,
                selected.TourName,
                selected.TourTime);

            if (report == null)
            {
                return summary;
            }

            summary.ReportFound = true;
            summary.IsSubmitted = report.IsSubmitted;
            summary.SubmittedAt = report.SubmittedAt;
            summary.PublicId = report.PublicId;

            var walker = report.Walkers?.FirstOrDefault(w =>
                w.BookingId == selected.BookingId ||
                (!string.IsNullOrWhiteSpace(selected.CustomerPhone) &&
                 !string.IsNullOrWhiteSpace(w.Phone) &&
                 NormalizePhone(w.Phone) == NormalizePhone(selected.CustomerPhone)) ||
                (!string.IsNullOrWhiteSpace(selected.CustomerEmail) &&
                 !string.IsNullOrWhiteSpace(w.Email) &&
                 NormalizeEmail(w.Email) == NormalizeEmail(selected.CustomerEmail)));

            if (walker != null)
            {
                summary.IsCheckedIn = walker.IsCheckedIn;
                summary.ReviewStatus = walker.ReviewStatus;
                summary.ReviewNotes = walker.ReviewNotes;
                summary.DoNotContact = walker.DoNotContact;
                summary.ActualAdults = walker.ActualAdults;
                summary.ActualChildren = walker.ActualChildren;
                summary.ActualAttendees = walker.ActualAttendees;
            }

            return summary;
        }

        private async Task<GalleryResolutionInfo> LoadGallerySummaryAsync(
            BookingsLiveListItem selected,
            CancellationToken ct)
        {
            if (!selected.TourDate.HasValue || string.IsNullOrWhiteSpace(selected.TourName) || string.IsNullOrWhiteSpace(selected.TourTime))
            {
                return new GalleryResolutionInfo();
            }

            return await _galleryResolver.ResolveAsync(
                selected.TourDate.Value.Date,
                selected.TourName,
                selected.TourTime,
                selected.VendorName,
                ct);
        }

        private async Task<BookingMessageEventWriteModel> HydrateMessageEventAsync(
            BookingMessageEventWriteModel evt,
            CancellationToken ct)
        {
            if (evt.BookingId.GetValueOrDefault() <= 0)
            {
                return evt;
            }

            const string sql = @"
SELECT TOP 1
    CustomerId,
    CustomerIdentifier,
    BookingCode,
    MessageId,
    TourName,
    TourDate,
    TourTime,
    VendorName
FROM dbo.Bookings
WHERE Id = @BookingId;";

            using var conn = _connectionFactory.CreateOpenConnection();
            var booking = await conn.QueryFirstOrDefaultAsync<BookingIdentityRow>(
                new CommandDefinition(sql, new { evt.BookingId }, cancellationToken: ct));

            if (booking == null)
            {
                return evt;
            }

            evt.CustomerId ??= booking.CustomerId > 0 ? booking.CustomerId : null;
            evt.CustomerIdentifier ??= booking.CustomerIdentifier;
            evt.BookingCode ??= booking.BookingCode;
            evt.MessageId ??= booking.MessageId;
            evt.TourName ??= booking.TourName;
            evt.TourDate ??= booking.TourDate;
            evt.TourTime ??= booking.TourTime;
            evt.VendorName ??= booking.VendorName;
            return evt;
        }

        private static string DetermineBookingEventType(BookingsLiveListItem booking)
        {
            if (booking.IsCancellation) return "cancelled";
            if (booking.IsModification) return "rescheduled";
            return "booked";
        }

        private static string NormalizeListOrder(string? value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "tour" => "tour_date",
                "tour_date" => "tour_date",
                "tour date" => "tour_date",
                "booking" => "booking_date",
                "booking_date" => "booking_date",
                "booking date" => "booking_date",
                _ => "booking_date"
            };
        }

        private static string NormalizePhone(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return new string(value.Where(char.IsDigit).ToArray());
        }

        private static string NormalizeEmail(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToLowerInvariant();
        }

        private static string NormalizeStage(string? stage)
        {
            var s = (stage ?? string.Empty).Trim().ToLowerInvariant();
            return s switch
            {
                "welcome" => "welcome",
                "tomorrow" => "tomorrow",
                "dayof" => "dayOf",
                "day-of" => "dayOf",
                "day_of" => "dayOf",
                "day of" => "dayOf",
                "thankyou" => "thankyou",
                "thank you" => "thankyou",
                "promo" => "promo",
                _ => "misc"
            };
        }

        private static string NormalizeChannel(string? channel)
        {
            var c = (channel ?? string.Empty).Trim().ToLowerInvariant();
            return c switch
            {
                "wa" => "wa",
                "whatsapp" => "wa",
                "sms" => "sms",
                _ => c
            };
        }

        private static string NormalizeTriggerType(string? triggerType)
        {
            var t = (triggerType ?? string.Empty).Trim().ToLowerInvariant();
            return t switch
            {
                "manual" => "manual_toggle",
                "manual_toggle" => "manual_toggle",
                "open" => "open_channel",
                "open_channel" => "open_channel",
                _ => "open_channel"
            };
        }

        private sealed class CustomerLabelRow
        {
            public int CustomerId { get; set; }
            public string LabelKey { get; set; } = string.Empty;
            public bool IsActive { get; set; }
        }

        private sealed class BookingIdentityRow
        {
            public int CustomerId { get; set; }
            public string CustomerIdentifier { get; set; } = string.Empty;
            public string BookingCode { get; set; } = string.Empty;
            public string MessageId { get; set; } = string.Empty;
            public string TourName { get; set; } = string.Empty;
            public DateTime? TourDate { get; set; }
            public string? TourTime { get; set; }
            public string VendorName { get; set; } = string.Empty;
        }
    }
}
