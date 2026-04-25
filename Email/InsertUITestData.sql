USE [AutoGmail_Local]
GO

SET NOCOUNT ON
GO

PRINT 'Inserting UI Test Data...'

DECLARE @Today DATE = CAST(SYSUTCDATETIME() AS DATE)
DECLARE @Tomorrow DATE = DATEADD(DAY, 1, @Today)
DECLARE @Yesterday DATE = DATEADD(DAY, -1, @Today)

-- Variables for IDs
DECLARE @MarketingTourId INT, @FoodTourId INT, @HistoryTourId INT
DECLARE @GuideBobId INT, @GuideAliceId INT
DECLARE @CustId1 INT, @CustId2 INT, @CustId3 INT, @CustId4 INT
DECLARE @InboxId INT
DECLARE @ProcessedId INT

-- 1. Create specific Tours with real-ish names
IF NOT EXISTS (SELECT 1 FROM [dbo].[Tours] WHERE TourName = 'Rome Historic Center Free Tour')
BEGIN
    INSERT INTO [dbo].[Tours] (TourName, MeetingPlace, Duration, MaxCapacity, IsActive, VendorNames)
    VALUES ('Rome Historic Center Free Tour', 'Spanish Steps', '2 hours', 30, 1, 'Freetour.com,GuruWalk')
    SET @HistoryTourId = SCOPE_IDENTITY()
END
ELSE
    SELECT @HistoryTourId = Id FROM [dbo].[Tours] WHERE TourName = 'Rome Historic Center Free Tour'

IF NOT EXISTS (SELECT 1 FROM [dbo].[Tours] WHERE TourName = 'Trastevere Street Food Tour')
BEGIN
    INSERT INTO [dbo].[Tours] (TourName, MeetingPlace, Duration, MaxCapacity, IsActive, VendorNames)
    VALUES ('Trastevere Street Food Tour', 'Piazza di Santa Maria', '3 hours', 15, 1, 'Viator,GetYourGuide')
    SET @FoodTourId = SCOPE_IDENTITY()
END
ELSE
    SELECT @FoodTourId = Id FROM [dbo].[Tours] WHERE TourName = 'Trastevere Street Food Tour'

-- 2. Create specific Guides
IF NOT EXISTS (SELECT 1 FROM [dbo].[Guides] WHERE Email = 'bob.guide@example.com')
BEGIN
    INSERT INTO [dbo].[Guides] (FirstName, LastName, Phone, Email, IsTouring, IsActive)
    VALUES ('Bob', 'Walker', '+15550101', 'bob.guide@example.com', 1, 1)
    SET @GuideBobId = SCOPE_IDENTITY()
END
ELSE
    SELECT @GuideBobId = Id FROM [dbo].[Guides] WHERE Email = 'bob.guide@example.com'

IF NOT EXISTS (SELECT 1 FROM [dbo].[Guides] WHERE Email = 'alice.guide@example.com')
BEGIN
    INSERT INTO [dbo].[Guides] (FirstName, LastName, Phone, Email, IsTouring, IsActive)
    VALUES ('Alice', 'Wonder', '+15550102', 'alice.guide@example.com', 1, 1)
    SET @GuideAliceId = SCOPE_IDENTITY()
END
ELSE
    SELECT @GuideAliceId = Id FROM [dbo].[Guides] WHERE Email = 'alice.guide@example.com'

-- 3. Create fresh Customers
INSERT INTO [dbo].[Customers] (FullName, FirstName, LastName, PhoneNumber, Email, CustomerIdentifier, TotalBookings)
VALUES 
('John Smith', 'John', 'Smith', '+15559991111', 'john.smith.ui@test.com', 'CUST-UI-1', 1),
('Maria Garcia', 'Maria', 'Garcia', '+15559992222', 'maria.garcia.ui@test.com', 'CUST-UI-2', 1),
('Sven Svensson', 'Sven', 'Svensson', '+15559993333', 'sven.ui@test.com', 'CUST-UI-3', 1),
('Cancel Me', 'Cancel', 'Me', '+15559994444', 'cancel.ui@test.com', 'CUST-UI-4', 1)

SET @CustId4 = SCOPE_IDENTITY()
SET @CustId3 = @CustId4 - 1
SET @CustId2 = @CustId4 - 2
SET @CustId1 = @CustId4 - 3

-- 4. Create complete chain: Inbox -> Processed -> Booking

-- SCENARIO A: Freetour.com | Confirmed | Today | 10:00 AM | History Tour | Guide Bob
-- Insert Inbox
INSERT INTO [dbo].[AutomaticGmail_InboxEmails] (Uid, MessageId, Subject, FromEmail, ReceivedDate, CollectionBatchId)
VALUES (20001, 'MSG-UI-1', 'Booking Confirmation Freetour 12345', 'bookings@freetour.com', DATEADD(HOUR, -2, GETUTCDATE()), 'BATCH-UI-1')
SET @InboxId = SCOPE_IDENTITY()

-- Insert Processed
INSERT INTO [dbo].[AutomaticGmail_ProcessedEmails] (InboxEmailId, MessageId, VendorName, EmailType, IsTourBookingEmail, ProcessingStatus, IsLatestAction, CustomerName, CustomerEmail, TourName, TourDate, TourTime, BookingCode)
VALUES (@InboxId, 'MSG-UI-1', 'Freetour.com', 'Booking', 1, 'processed', 1, 'John Smith', 'john.smith.ui@test.com', 'Rome Historic Center Free Tour', @Today, '10:00 AM', 'BK-UI-1')
SET @ProcessedId = SCOPE_IDENTITY()

-- Insert Booking
INSERT INTO [dbo].[Bookings] 
(CustomerId, CustomerIdentifier, ProcessedEmailId, MessageId, BookingCode, VendorName, EmailType, IsActive, IsConfirmation, TourName, TourDate, TourTime, CustomerName, CustomerEmail, NumberOfAdults, BookingAmount, Currency)
VALUES 
(@CustId1, 'CUST-UI-1', @ProcessedId, 'MSG-UI-1', 'BK-UI-1', 'Freetour.com', 'Booking', 1, 1, 'Rome Historic Center Free Tour', @Today, '10:00 AM', 'John Smith', 'john.smith.ui@test.com', 2, 0.00, 'EUR')


-- SCENARIO B: GuruWalk | Modified | Today | 10:00 AM | History Tour | Guide Bob
-- Insert Inbox
INSERT INTO [dbo].[AutomaticGmail_InboxEmails] (Uid, MessageId, Subject, FromEmail, ReceivedDate, CollectionBatchId)
VALUES (20002, 'MSG-UI-2', 'Booking Modification GuruWalk 67890', 'bookings@guruwalk.com', DATEADD(HOUR, -1, GETUTCDATE()), 'BATCH-UI-1')
SET @InboxId = SCOPE_IDENTITY()

-- Insert Processed
INSERT INTO [dbo].[AutomaticGmail_ProcessedEmails] (InboxEmailId, MessageId, VendorName, EmailType, IsTourBookingEmail, ProcessingStatus, IsLatestAction, CustomerName, CustomerEmail, TourName, TourDate, TourTime, BookingCode)
VALUES (@InboxId, 'MSG-UI-2', 'GuruWalk', 'Modification', 1, 'processed', 1, 'Maria Garcia', 'maria.garcia.ui@test.com', 'Rome Historic Center Free Tour', @Today, '10:00 AM', 'BK-UI-2')
SET @ProcessedId = SCOPE_IDENTITY()

-- Insert Booking
INSERT INTO [dbo].[Bookings] 
(CustomerId, CustomerIdentifier, ProcessedEmailId, MessageId, BookingCode, VendorName, EmailType, IsActive, IsModification, TourName, TourDate, TourTime, CustomerName, CustomerEmail, NumberOfAdults, BookingAmount, Currency)
VALUES 
(@CustId2, 'CUST-UI-2', @ProcessedId, 'MSG-UI-2', 'BK-UI-2', 'GuruWalk', 'Modification', 1, 1, 'Rome Historic Center Free Tour', @Today, '10:00 AM', 'Maria Garcia', 'maria.garcia.ui@test.com', 4, 0.00, 'EUR')


-- SCENARIO C: Viator | Confirmed | Tomorrow | 06:00 PM | Food Tour | Guide Alice
-- Insert Inbox
INSERT INTO [dbo].[AutomaticGmail_InboxEmails] (Uid, MessageId, Subject, FromEmail, ReceivedDate, CollectionBatchId)
VALUES (20003, 'MSG-UI-3', 'New Booking from Viator', 'supplier@viator.com', GETUTCDATE(), 'BATCH-UI-1')
SET @InboxId = SCOPE_IDENTITY()

-- Insert Processed
INSERT INTO [dbo].[AutomaticGmail_ProcessedEmails] (InboxEmailId, MessageId, VendorName, EmailType, IsTourBookingEmail, ProcessingStatus, IsLatestAction, CustomerName, CustomerEmail, TourName, TourDate, TourTime, BookingCode)
VALUES (@InboxId, 'MSG-UI-3', 'Viator', 'Booking', 1, 'processed', 1, 'Sven Svensson', 'sven.ui@test.com', 'Trastevere Street Food Tour', @Tomorrow, '06:00 PM', 'BK-UI-3')
SET @ProcessedId = SCOPE_IDENTITY()

-- Insert Booking
INSERT INTO [dbo].[Bookings] 
(CustomerId, CustomerIdentifier, ProcessedEmailId, MessageId, BookingCode, VendorName, EmailType, IsActive, IsConfirmation, TourName, TourDate, TourTime, CustomerName, CustomerEmail, NumberOfAdults, BookingAmount, Currency)
VALUES 
(@CustId3, 'CUST-UI-3', @ProcessedId, 'MSG-UI-3', 'BK-UI-3', 'Viator', 'Booking', 1, 1, 'Trastevere Street Food Tour', @Tomorrow, '06:00 PM', 'Sven Svensson', 'sven.ui@test.com', 1, 85.50, 'USD')


-- SCENARIO D: Freetour.com | Cancelled | Tomorrow | 06:00 PM | Food Tour | Guide Alice
-- Insert Inbox
INSERT INTO [dbo].[AutomaticGmail_InboxEmails] (Uid, MessageId, Subject, FromEmail, ReceivedDate, CollectionBatchId)
VALUES (20004, 'MSG-UI-4', 'CANCELLATION Freetour', 'bookings@freetour.com', GETUTCDATE(), 'BATCH-UI-1')
SET @InboxId = SCOPE_IDENTITY()

-- Insert Processed
INSERT INTO [dbo].[AutomaticGmail_ProcessedEmails] (InboxEmailId, MessageId, VendorName, EmailType, IsTourBookingEmail, ProcessingStatus, IsLatestAction, CustomerName, CustomerEmail, TourName, TourDate, TourTime, BookingCode)
VALUES (@InboxId, 'MSG-UI-4', 'Freetour.com', 'Cancellation', 1, 'processed', 1, 'Cancel Me', 'cancel.ui@test.com', 'Trastevere Street Food Tour', @Tomorrow, '06:00 PM', 'BK-UI-4')
SET @ProcessedId = SCOPE_IDENTITY()

-- Insert Booking
INSERT INTO [dbo].[Bookings] 
(CustomerId, CustomerIdentifier, ProcessedEmailId, MessageId, BookingCode, VendorName, EmailType, IsActive, IsCancellation, TourName, TourDate, TourTime, CustomerName, CustomerEmail, NumberOfAdults, BookingAmount, Currency)
VALUES 
(@CustId4, 'CUST-UI-4', @ProcessedId, 'MSG-UI-4', 'BK-UI-4', 'Freetour.com', 'Cancellation', 0, 1, 'Trastevere Street Food Tour', @Tomorrow, '06:00 PM', 'Cancel Me', 'cancel.ui@test.com', 2, 0.00, 'EUR')


PRINT 'Done inserting UI Test Data.'
GO
