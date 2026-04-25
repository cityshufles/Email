/*
DEV seed data for guide/report flow in cityshufflesguidesproject.

What this seeds:
- Customers (3)
- Inbox + Processed emails (5)
- Bookings (5)
- Tour guide assignments (4)
- Tour reports (3)
- Booking message events (3)
- Customer contact labels (1)

Safety:
- Uses DEVSEED keys so it can be rerun.
- Removes prior DEVSEED rows before reseeding.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @UtcNow DATETIME2(7) = SYSUTCDATETIME();
    DECLARE @Today DATE = CAST(GETDATE() AS DATE);
    DECLARE @Tomorrow DATE = DATEADD(DAY, 1, @Today);
    DECLARE @TwoDaysAgo DATE = DATEADD(DAY, -2, @Today);

    /* ---------- Seed definitions ---------- */

    DECLARE @SeedCustomers TABLE
    (
        CustomerIdentifier NVARCHAR(400) NOT NULL PRIMARY KEY,
        FullName NVARCHAR(200) NOT NULL,
        FirstName NVARCHAR(100) NULL,
        LastName NVARCHAR(100) NULL,
        Email NVARCHAR(320) NULL,
        PhoneNumber NVARCHAR(50) NULL
    );

    INSERT INTO @SeedCustomers (CustomerIdentifier, FullName, FirstName, LastName, Email, PhoneNumber)
    VALUES
        (N'DEVSEED-CUST-ALICE', N'Alice Johnson', N'Alice', N'Johnson', N'alice.devseed@example.test', N'+1-917-555-0101'),
        (N'DEVSEED-CUST-BOB',   N'Bob Martinez', N'Bob',   N'Martinez', N'bob.devseed@example.test',   N'+1-917-555-0102'),
        (N'DEVSEED-CUST-CARA',  N'Cara Nguyen',  N'Cara',  N'Nguyen',   N'cara.devseed@example.test',  N'+1-917-555-0103');

    DECLARE @SeedBookings TABLE
    (
        BookingCode NVARCHAR(100) NOT NULL PRIMARY KEY,
        MessageId NVARCHAR(255) NOT NULL,
        CustomerIdentifier NVARCHAR(400) NOT NULL,
        VendorName NVARCHAR(100) NOT NULL,
        EmailType NVARCHAR(50) NOT NULL,
        TourName NVARCHAR(200) NOT NULL,
        TourDate DATETIME2(7) NOT NULL,
        TourTime NVARCHAR(20) NOT NULL,
        TourLocation NVARCHAR(200) NULL,
        CustomerName NVARCHAR(200) NOT NULL,
        CustomerEmail NVARCHAR(320) NULL,
        CustomerPhone NVARCHAR(50) NULL,
        NumberOfAttendees INT NULL,
        NumberOfAdults INT NULL,
        NumberOfChildren INT NULL,
        Language NVARCHAR(50) NULL,
        CountryOfOrigin NVARCHAR(100) NULL,
        BookingStatus NVARCHAR(50) NULL,
        BookingAmount DECIMAL(10,2) NULL,
        SpecialRequests NVARCHAR(MAX) NULL,
        GuideAssigned NVARCHAR(200) NULL
    );

    INSERT INTO @SeedBookings
    (
        BookingCode, MessageId, CustomerIdentifier, VendorName, EmailType,
        TourName, TourDate, TourTime, TourLocation,
        CustomerName, CustomerEmail, CustomerPhone,
        NumberOfAttendees, NumberOfAdults, NumberOfChildren,
        Language, CountryOfOrigin, BookingStatus, BookingAmount, SpecialRequests, GuideAssigned
    )
    VALUES
        (N'DEVSEED-BOOK-001', N'DEVSEED-MSG-001', N'DEVSEED-CUST-ALICE', N'DEVSEED', N'booking',
         N'DEVSEED - Brooklyn Bridge Walk', CAST(@Today AS DATETIME2(7)), N'10:07 AM', N'Brooklyn Bridge',
         N'Alice Johnson', N'alice.devseed@example.test', N'+1-917-555-0101',
         4, 2, 2, N'English', N'US', N'Confirmed', 189.00, N'Needs stroller-friendly route.', N'Dev Guide 1'),

        (N'DEVSEED-BOOK-002', N'DEVSEED-MSG-002', N'DEVSEED-CUST-BOB', N'DEVSEED', N'booking',
         N'DEVSEED - Central Park Highlights', CAST(@Today AS DATETIME2(7)), N'1:37 PM', N'Central Park South',
         N'Bob Martinez', N'bob.devseed@example.test', N'+1-917-555-0102',
         2, 2, 0, N'English', N'US', N'Confirmed', 98.00, N'No special requests.', N'Dev Guide 2'),

        (N'DEVSEED-BOOK-003', N'DEVSEED-MSG-003', N'DEVSEED-CUST-CARA', N'DEVSEED', N'booking',
         N'DEVSEED - Soho Night Walk', CAST(@Tomorrow AS DATETIME2(7)), N'6:33 PM', N'Soho Arch',
         N'Cara Nguyen', N'cara.devseed@example.test', N'+1-917-555-0103',
         3, 2, 1, N'English', N'CA', N'Confirmed', 145.00, N'Family with one child.', N'Dev Guide 3'),

        (N'DEVSEED-BOOK-004', N'DEVSEED-MSG-004', N'DEVSEED-CUST-ALICE', N'DEVSEED', N'booking',
         N'DEVSEED - Statue of Liberty Express', CAST(@Tomorrow AS DATETIME2(7)), N'9:13 AM', N'Battery Park',
         N'Alice Johnson', N'alice.devseed@example.test', N'+1-917-555-0101',
         2, 2, 0, N'English', N'US', N'Confirmed', 120.00, N'Prefers early ferry.', N'Dev Guide 1'),

        (N'DEVSEED-BOOK-005', N'DEVSEED-MSG-005', N'DEVSEED-CUST-BOB', N'DEVSEED', N'booking',
         N'DEVSEED - Midtown Landmarks', CAST(@TwoDaysAgo AS DATETIME2(7)), N'11:05 AM', N'Bryant Park',
         N'Bob Martinez', N'bob.devseed@example.test', N'+1-917-555-0102',
         5, 3, 2, N'English', N'US', N'Confirmed', 225.00, N'Birthday group.', N'Dev Guide 2');

    DECLARE @SeedAssignments TABLE
    (
        TourDate DATE NOT NULL,
        TourName NVARCHAR(255) NOT NULL,
        TourTime NVARCHAR(50) NOT NULL,
        GuideId INT NULL
    );

    INSERT INTO @SeedAssignments (TourDate, TourName, TourTime, GuideId)
    VALUES
        (@Today,    N'DEVSEED - Brooklyn Bridge Walk',        N'10:07 AM', NULL),
        (@Today,    N'DEVSEED - Central Park Highlights',     N'1:37 PM',  NULL),
        (@Tomorrow, N'DEVSEED - Soho Night Walk',             N'6:33 PM',  NULL),
        (@Tomorrow, N'DEVSEED - Statue of Liberty Express',   N'9:13 AM',  NULL);

    DECLARE @SeedReports TABLE
    (
        TourDate DATE NOT NULL,
        TourName NVARCHAR(255) NOT NULL,
        TourTime NVARCHAR(50) NOT NULL,
        GuideId INT NULL,
        GeneralNotes NVARCHAR(MAX) NULL,
        ImagePaths NVARCHAR(MAX) NULL,
        IsSubmitted BIT NOT NULL,
        SubmittedAt DATETIME2(7) NULL,
        PublicId NVARCHAR(50) NOT NULL
    );

    INSERT INTO @SeedReports
    (
        TourDate, TourName, TourTime, GuideId, GeneralNotes, ImagePaths,
        IsSubmitted, SubmittedAt, PublicId
    )
    VALUES
        (@Today,    N'DEVSEED - Brooklyn Bridge Walk',      N'10:07 AM', NULL, N'DEVSEED report: smooth tour, no incidents.', N'[]', 1, DATEADD(MINUTE, -95, @UtcNow), N'DEVSEED-RPT-001'),
        (@Today,    N'DEVSEED - Central Park Highlights',   N'1:37 PM',  NULL, N'DEVSEED report: moderate crowd, tour completed.', N'[]', 0, NULL,                          N'DEVSEED-RPT-002'),
        (@Tomorrow, N'DEVSEED - Soho Night Walk',           N'6:33 PM',  NULL, N'DEVSEED report: draft pre-filled for testing.', N'[]', 0, NULL,                          N'DEVSEED-RPT-003');

    DECLARE @SeedMessageEvents TABLE
    (
        BookingCode NVARCHAR(100) NOT NULL,
        MessageId NVARCHAR(255) NOT NULL,
        Stage NVARCHAR(50) NOT NULL,
        Channel NVARCHAR(20) NULL,
        TemplateType NVARCHAR(100) NULL,
        TemplateName NVARCHAR(200) NULL,
        TriggerType NVARCHAR(50) NOT NULL,
        TriggeredBy NVARCHAR(100) NULL,
        MinutesAgo INT NOT NULL
    );

    INSERT INTO @SeedMessageEvents
    (
        BookingCode, MessageId, Stage, Channel, TemplateType, TemplateName, TriggerType, TriggeredBy, MinutesAgo
    )
    VALUES
        (N'DEVSEED-BOOK-001', N'DEVSEED-MSG-001', N'pre_tour_reminder',  N'sms',      N'reminder', N'DEVSEED Reminder T-24h', N'system', N'seed-script', 120),
        (N'DEVSEED-BOOK-002', N'DEVSEED-MSG-002', N'pre_tour_reminder',  N'wa',       N'reminder', N'DEVSEED Reminder T-2h',  N'system', N'seed-script',  80),
        (N'DEVSEED-BOOK-003', N'DEVSEED-MSG-003', N'post_tour_followup', N'platform', N'followup', N'DEVSEED Followup',       N'manual', N'seed-script',  45);

    /* ---------- Cleanup prior DEVSEED rows ---------- */

    DELETE FROM dbo.BookingMessageEvents
    WHERE MessageId LIKE N'DEVSEED-MSG-%'
       OR BookingCode LIKE N'DEVSEED-BOOK-%'
       OR CustomerIdentifier LIKE N'DEVSEED-CUST-%';

    DELETE ccl
    FROM dbo.CustomerContactLabels ccl
    INNER JOIN dbo.Customers c ON c.Id = ccl.CustomerId
    WHERE c.CustomerIdentifier LIKE N'DEVSEED-CUST-%';

    DELETE FROM dbo.BookingContactChannelState
    WHERE MessageId LIKE N'DEVSEED-MSG-%'
       OR BookingCode LIKE N'DEVSEED-BOOK-%';

    DELETE FROM dbo.Bookings
    WHERE BookingCode LIKE N'DEVSEED-BOOK-%'
       OR MessageId LIKE N'DEVSEED-MSG-%'
       OR CustomerIdentifier LIKE N'DEVSEED-CUST-%';

    DELETE FROM dbo.AutomaticGmail_ProcessedEmails
    WHERE MessageId LIKE N'DEVSEED-MSG-%';

    DELETE FROM dbo.AutomaticGmail_InboxEmails
    WHERE MessageId LIKE N'DEVSEED-MSG-%';

    DELETE FROM dbo.TourReports
    WHERE PublicId LIKE N'DEVSEED-RPT-%'
       OR TourName LIKE N'DEVSEED - %';

    DELETE FROM dbo.TourGuideAssignment
    WHERE TourName LIKE N'DEVSEED - %';

    DELETE FROM dbo.Customers
    WHERE CustomerIdentifier LIKE N'DEVSEED-CUST-%';

    /* ---------- Insert customers ---------- */

    INSERT INTO dbo.Customers
    (
        FullName, FirstName, LastName, PhoneNumber, Email, CustomerIdentifier,
        BookingIds, TotalBookings, CreatedAt, UpdatedAt
    )
    SELECT
        s.FullName, s.FirstName, s.LastName, s.PhoneNumber, s.Email, s.CustomerIdentifier,
        N'[]', 0, @UtcNow, @UtcNow
    FROM @SeedCustomers s;

    DECLARE @CustomerMap TABLE
    (
        CustomerIdentifier NVARCHAR(400) NOT NULL PRIMARY KEY,
        CustomerId INT NOT NULL
    );

    INSERT INTO @CustomerMap (CustomerIdentifier, CustomerId)
    SELECT c.CustomerIdentifier, c.Id
    FROM dbo.Customers c
    WHERE c.CustomerIdentifier LIKE N'DEVSEED-CUST-%';

    /* ---------- Insert inbox ---------- */

    INSERT INTO dbo.AutomaticGmail_InboxEmails
    (
        Uid, MessageId, Subject, FromEmail, FromName, ToEmail, ReceivedDate,
        TextBody, HtmlBody, TextBodyPreview, AttachmentCount, AttachmentNames,
        CollectedAt, CollectionBatchId, IsRead, CreatedAt, UpdatedAt, ProcessingStatus,
        OriginalBookingId, OriginalBookingCode, OriginalBookingMessageId
    )
    SELECT
        700000000 + ROW_NUMBER() OVER (ORDER BY sb.MessageId),
        sb.MessageId,
        CONCAT(N'DEVSEED Booking Email ', sb.BookingCode),
        N'bookings@devseed.example.test',
        N'DEVSEED Mailer',
        N'ops@cityshuffles.dev',
        CAST(sb.TourDate AS DATETIME2(0)),
        CONCAT(N'DEVSEED booking mail body for ', sb.BookingCode),
        CONCAT(N'<p>DEVSEED booking mail body for ', sb.BookingCode, N'</p>'),
        CONCAT(N'DEVSEED preview ', sb.BookingCode),
        0,
        N'[]',
        @UtcNow,
        N'DEVSEED-BATCH-2026-04-25',
        0,
        @UtcNow,
        @UtcNow,
        3,
        NULL,
        sb.BookingCode,
        sb.MessageId
    FROM @SeedBookings sb;

    DECLARE @InboxMap TABLE
    (
        MessageId NVARCHAR(255) NOT NULL PRIMARY KEY,
        InboxEmailId INT NOT NULL
    );

    INSERT INTO @InboxMap (MessageId, InboxEmailId)
    SELECT ie.MessageId, ie.Id
    FROM dbo.AutomaticGmail_InboxEmails ie
    WHERE ie.MessageId LIKE N'DEVSEED-MSG-%';

    /* ---------- Insert processed emails ---------- */

    INSERT INTO dbo.AutomaticGmail_ProcessedEmails
    (
        InboxEmailId, MessageId, VendorName, EmailType, IsTourBookingEmail,
        ProcessingStatus, ProcessingStartedAt, ProcessingCompletedAt, ProcessingAttempts,
        CustomerName, BookingCode, CustomerPhone, CustomerEmail, NumberOfAttendees,
        Language, TourDate, TourTime, TourName, TourLocation, ExtractedAt,
        CustomerIdentifier, RelatedEmailIds, IsLatestAction, HasBeenClassified,
        HasAIBeenRun, IsAISuccess, HasCalendarExport, IsCalendarExportSuccess,
        CreatedAt, UpdatedAt, ManualParsingCompleted, ManualParsingConfidence, PlainTextContent,
        IsCancellation, IsModification, IsBooking, AssociatedBookingIds,
        BookingAlterationNotes, HtmlContent, ExtractedBookingCode, NewBookingCode,
        PreviousBookingCode, NumberOfAdults, NumberOfChildren, VendorManuallyOverridden
    )
    SELECT
        im.InboxEmailId,
        sb.MessageId,
        sb.VendorName,
        sb.EmailType,
        1,
        N'completed',
        DATEADD(SECOND, -20, @UtcNow),
        DATEADD(SECOND, -5, @UtcNow),
        1,
        sb.CustomerName,
        sb.BookingCode,
        sb.CustomerPhone,
        sb.CustomerEmail,
        sb.NumberOfAttendees,
        sb.Language,
        CONVERT(NVARCHAR(50), CAST(sb.TourDate AS DATE), 23),
        sb.TourTime,
        sb.TourName,
        sb.TourLocation,
        @UtcNow,
        sb.CustomerIdentifier,
        N'[]',
        1,
        1,
        1,
        1,
        0,
        0,
        @UtcNow,
        @UtcNow,
        1,
        100,
        CONCAT(N'DEVSEED parsed text for ', sb.BookingCode),
        0,
        0,
        1,
        N'[]',
        N'',
        CONCAT(N'<p>DEVSEED parsed html for ', sb.BookingCode, N'</p>'),
        sb.BookingCode,
        N'',
        N'',
        sb.NumberOfAdults,
        sb.NumberOfChildren,
        0
    FROM @SeedBookings sb
    INNER JOIN @InboxMap im ON im.MessageId = sb.MessageId;

    DECLARE @ProcessedMap TABLE
    (
        MessageId NVARCHAR(255) NOT NULL PRIMARY KEY,
        ProcessedEmailId INT NOT NULL
    );

    INSERT INTO @ProcessedMap (MessageId, ProcessedEmailId)
    SELECT pe.MessageId, pe.Id
    FROM dbo.AutomaticGmail_ProcessedEmails pe
    WHERE pe.MessageId LIKE N'DEVSEED-MSG-%';

    /* ---------- Insert bookings ---------- */

    INSERT INTO dbo.Bookings
    (
        CustomerId, CustomerIdentifier, ProcessedEmailId, MessageId, BookingCode,
        VendorName, EmailType, IsCancellation, IsModification, IsConfirmation, IsActive,
        TourName, TourDate, TourDayOfWeek, TourTime, TourLocation, TourTimeZone,
        DisplayDate, DisplayTime, CustomerName, CustomerEmail, CustomerPhone,
        NumberOfAttendees, NumberOfAdults, NumberOfChildren, NumberOfInfants, Language,
        CountryOfOrigin, BookingStatus, BookingAmount, Currency, SpecialRequests, GuideAssigned,
        CalendarEventId, IsCalendarExportSuccess, IsSheetExportSuccess, ProcessingNotes,
        CreatedAt, UpdatedAt, MessageSent, GuideReviewStatus, GuideReviewNotes, ActualAttendees,
        IsCheckedIn, DoNotContact, ActualAdults, ActualChildren, VendorManuallyOverridden
    )
    SELECT
        cm.CustomerId,
        sb.CustomerIdentifier,
        pm.ProcessedEmailId,
        sb.MessageId,
        sb.BookingCode,
        sb.VendorName,
        sb.EmailType,
        0, 0, 1, 1,
        sb.TourName,
        sb.TourDate,
        DATENAME(WEEKDAY, sb.TourDate),
        sb.TourTime,
        sb.TourLocation,
        N'America/New_York',
        CONVERT(NVARCHAR(20), CAST(sb.TourDate AS DATE), 101),
        sb.TourTime,
        sb.CustomerName,
        sb.CustomerEmail,
        sb.CustomerPhone,
        sb.NumberOfAttendees,
        sb.NumberOfAdults,
        sb.NumberOfChildren,
        0,
        sb.Language,
        sb.CountryOfOrigin,
        sb.BookingStatus,
        sb.BookingAmount,
        N'USD',
        sb.SpecialRequests,
        sb.GuideAssigned,
        NULL,
        0,
        0,
        N'DEVSEED: inserted for guide-report/dev testing',
        @UtcNow,
        @UtcNow,
        1,
        NULL,
        NULL,
        NULL,
        0,
        0,
        NULL,
        NULL,
        0
    FROM @SeedBookings sb
    INNER JOIN @CustomerMap cm ON cm.CustomerIdentifier = sb.CustomerIdentifier
    INNER JOIN @ProcessedMap pm ON pm.MessageId = sb.MessageId;

    /* Link inbox rows back to inserted booking ids */
    UPDATE ie
    SET
        ie.OriginalBookingId = b.Id,
        ie.OriginalBookingCode = b.BookingCode,
        ie.OriginalBookingMessageId = b.MessageId,
        ie.UpdatedAt = @UtcNow
    FROM dbo.AutomaticGmail_InboxEmails ie
    INNER JOIN dbo.Bookings b ON b.MessageId = ie.MessageId
    WHERE ie.MessageId LIKE N'DEVSEED-MSG-%';

    /* Link processed rows to concrete booking ids */
    UPDATE pe
    SET
        pe.AssociatedBookingIds = CONCAT(N'[', CONVERT(NVARCHAR(20), b.Id), N']'),
        pe.UpdatedAt = @UtcNow
    FROM dbo.AutomaticGmail_ProcessedEmails pe
    INNER JOIN dbo.Bookings b ON b.MessageId = pe.MessageId
    WHERE pe.MessageId LIKE N'DEVSEED-MSG-%';

    /* ---------- Insert assignments ---------- */

    INSERT INTO dbo.TourGuideAssignment (TourDate, TourName, TourTime, GuideId, CreatedAt, UpdatedAt)
    SELECT sa.TourDate, sa.TourName, sa.TourTime, sa.GuideId, @UtcNow, @UtcNow
    FROM @SeedAssignments sa;

    /* ---------- Insert reports ---------- */

    INSERT INTO dbo.TourReports
    (
        TourDate, TourName, TourTime, GuideId, GeneralNotes, ImagePaths,
        IsSubmitted, SubmittedAt, CreatedAt, UpdatedAt, PublicId
    )
    SELECT
        sr.TourDate, sr.TourName, sr.TourTime, sr.GuideId, sr.GeneralNotes, sr.ImagePaths,
        sr.IsSubmitted, sr.SubmittedAt, @UtcNow, @UtcNow, sr.PublicId
    FROM @SeedReports sr;

    /* ---------- Insert message events ---------- */

    INSERT INTO dbo.BookingMessageEvents
    (
        CustomerId, CustomerIdentifier, BookingId, BookingCode, MessageId,
        TourName, TourDate, TourTime, VendorName, Stage, Channel,
        TemplateId, TemplateType, TemplateName, TriggerType, TriggeredBy,
        SentAtUtc, CreatedAtUtc
    )
    SELECT
        b.CustomerId,
        b.CustomerIdentifier,
        b.Id,
        b.BookingCode,
        b.MessageId,
        b.TourName,
        b.TourDate,
        b.TourTime,
        b.VendorName,
        se.Stage,
        se.Channel,
        NULL,
        se.TemplateType,
        se.TemplateName,
        se.TriggerType,
        se.TriggeredBy,
        DATEADD(MINUTE, -se.MinutesAgo, @UtcNow),
        @UtcNow
    FROM @SeedMessageEvents se
    INNER JOIN dbo.Bookings b ON b.BookingCode = se.BookingCode;

    /* ---------- Insert labels ---------- */

    INSERT INTO dbo.CustomerContactLabels
    (
        CustomerId, LabelKey, IsActive, UpdatedBy, Notes, CreatedAtUtc, UpdatedAtUtc
    )
    SELECT
        c.Id,
        N'needs_number',
        1,
        N'dev-seed-script',
        N'DEVSEED: contact label for report/message testing',
        @UtcNow,
        @UtcNow
    FROM dbo.Customers c
    WHERE c.CustomerIdentifier = N'DEVSEED-CUST-CARA';

    /* ---------- Backfill customer booking summary ---------- */

    ;WITH BookingAgg AS
    (
        SELECT
            b.CustomerId,
            COUNT(*) AS BookingCount,
            STRING_AGG(CONVERT(NVARCHAR(20), b.Id), N',') AS BookingIdCsv
        FROM dbo.Bookings b
        WHERE b.CustomerIdentifier LIKE N'DEVSEED-CUST-%'
        GROUP BY b.CustomerId
    )
    UPDATE c
    SET
        c.TotalBookings = ba.BookingCount,
        c.BookingIds = CONCAT(N'[', ba.BookingIdCsv, N']'),
        c.UpdatedAt = @UtcNow
    FROM dbo.Customers c
    INNER JOIN BookingAgg ba ON ba.CustomerId = c.Id;

    COMMIT TRANSACTION;

    SELECT
        DEVSEED_Customers = (SELECT COUNT(*) FROM dbo.Customers WHERE CustomerIdentifier LIKE N'DEVSEED-CUST-%'),
        DEVSEED_Bookings = (SELECT COUNT(*) FROM dbo.Bookings WHERE BookingCode LIKE N'DEVSEED-BOOK-%'),
        DEVSEED_InboxEmails = (SELECT COUNT(*) FROM dbo.AutomaticGmail_InboxEmails WHERE MessageId LIKE N'DEVSEED-MSG-%'),
        DEVSEED_ProcessedEmails = (SELECT COUNT(*) FROM dbo.AutomaticGmail_ProcessedEmails WHERE MessageId LIKE N'DEVSEED-MSG-%'),
        DEVSEED_Assignments = (SELECT COUNT(*) FROM dbo.TourGuideAssignment WHERE TourName LIKE N'DEVSEED - %'),
        DEVSEED_Reports = (SELECT COUNT(*) FROM dbo.TourReports WHERE PublicId LIKE N'DEVSEED-RPT-%'),
        DEVSEED_MessageEvents = (SELECT COUNT(*) FROM dbo.BookingMessageEvents WHERE BookingCode LIKE N'DEVSEED-BOOK-%'),
        DEVSEED_Labels = (SELECT COUNT(*) FROM dbo.CustomerContactLabels ccl INNER JOIN dbo.Customers c ON c.Id = ccl.CustomerId WHERE c.CustomerIdentifier LIKE N'DEVSEED-CUST-%');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    DECLARE @Err NVARCHAR(4000) = ERROR_MESSAGE();
    DECLARE @ErrState INT = ERROR_STATE();
    DECLARE @ErrSeverity INT = ERROR_SEVERITY();

    RAISERROR(@Err, @ErrSeverity, @ErrState);
END CATCH;
