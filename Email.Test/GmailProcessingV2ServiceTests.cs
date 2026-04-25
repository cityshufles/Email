using System.Net;
using System.Net.Http;
using System.Text;
using Email.Calendar.Models;
using Email.Calendar.Services;
using Email.Services;
using Email.Services.GmailCollection.Models;
using Email.Services.GmailProcessing;
using Email.Services.GmailProcessing.Dtos;
using Email.Services.GmailProcessing.Models;
using Email.Services.GmailProcessing.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Email.Test;

[TestFixture]
public class GmailProcessingV2ServiceTests
{
    private static string FixturePath(string fileName)
        => Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", fileName);

    [Test]
    public async Task ProcessSpecific_ViatorBooking_InboxOnlyMode_SkipsCheckfrontAndPersists()
    {
        var repo = new TestGmailProcessingRepository();
        var processedRepo = new TestProcessedEmailRepository();
        var bookingRepo = new TestBookingRepository();
        var customerRepo = new TestCustomerRepository();
        var classificationRepo = new TestClassificationRepository(new[]
        {
            new ClassificationRuleRecord
            {
                Id = 1,
                VendorName = "Viator",
                Domain = "t1.viator.com",
                SubjectPhrase = "New Booking for",
                EmailType = "Booking",
                IsActive = true,
                Priority = 500
            }
        });

        repo.InboxById[1] = new InboxEmailRecord
        {
            Id = 1,
            MessageId = "msg-viator-booking-1",
            Subject = "New Booking for Sun, Feb 22, 2026 (#BR-1361381631)",
            FromEmail = "booking@t1.viator.com",
            ReceivedDate = DateTime.UtcNow,
            CollectedAt = DateTime.UtcNow,
            TextBody = File.ReadAllText(FixturePath("viator_booking_full_text.txt")),
            HtmlBody = File.ReadAllText(FixturePath("viator_booking_full_html.html")),
            ProcessingStatus = TourEmailInboxProcessingStatus.Pending
        };

        var requestLog = new List<string>();
        var checkfrontService = CreateCheckfrontService(
            req =>
            {
                requestLog.Add(req.RequestUri?.ToString() ?? string.Empty);
                return JsonResponse(@"{""request"":{""status"":""OK""},""data"":{}}");
            });

        var sut = CreateSut(
            repo,
            processedRepo,
            classificationRepo,
            bookingRepo,
            customerRepo,
            new TestGoogleCalendarService(),
            checkfrontService,
            dryRunWritesCheckfrontViatorOnly: false,
            dryRunWritesViator: false,
            dryRunWritesCheckfrontOnly: false,
            viatorInboxOnlyMode: true,
            enableCheckfrontEnrichmentForViator: false);

        var result = await sut.ProcessSpecificAsync(new List<int> { 1 }, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ProcessedCount, Is.EqualTo(1));
        Assert.That(result.ErrorCount, Is.EqualTo(0));
        Assert.That(processedRepo.UpsertCount, Is.EqualTo(1));
        Assert.That(customerRepo.CreateCount, Is.EqualTo(1));
        Assert.That(bookingRepo.CreateCount, Is.EqualTo(1));
        Assert.That(processedRepo.LastRecord?.ProcessingStatus, Is.EqualTo("completed"));
        Assert.That(processedRepo.LastRecord?.VendorName, Is.EqualTo("Viator"));
        Assert.That(processedRepo.LastRecord?.BookingCode, Is.EqualTo("BR-1361381631"));
        Assert.That(requestLog.Count, Is.EqualTo(0), "Active Viator path must not call Checkfront.");
    }

    [Test]
    public async Task ProcessSpecific_ViatorPriceFallback_EstimatesAttendeesFromNetRate()
    {
        var repo = new TestGmailProcessingRepository();
        var processedRepo = new TestProcessedEmailRepository();
        var bookingRepo = new TestBookingRepository();
        var customerRepo = new TestCustomerRepository();
        var classificationRepo = new TestClassificationRepository(new[]
        {
            new ClassificationRuleRecord
            {
                Id = 101,
                VendorName = "Viator",
                Domain = "t1.viator.com",
                SubjectPhrase = "New Booking for",
                EmailType = "Booking",
                IsActive = true,
                Priority = 500
            }
        });

        repo.InboxById[101] = new InboxEmailRecord
        {
            Id = 101,
            MessageId = "msg-viator-price-fallback",
            Subject = "New Booking for Sun, Feb 22, 2026 (#BR-PRICE-1001)",
            FromEmail = "booking@t1.viator.com",
            ReceivedDate = DateTime.UtcNow,
            CollectedAt = DateTime.UtcNow,
            TextBody = @"Booking Confirmation
Booking Reference: BR-PRICE-1001
Tour Name: Grand Central Terminal History and Mysteries
Travel Date: Sun, Feb 22, 2026
Lead Traveler Name: Price Fallback Walker
Travelers: Adults
Tour Grade: Grand Central Terminal History and Mysteries 14:00
Phone: +1 555 111 2222
Customer Email: price.fallback@example.com
Net Rate: USD $25.00",
            HtmlBody = string.Empty,
            ProcessingStatus = TourEmailInboxProcessingStatus.Pending
        };

        var logger = new TestLogger<GmailProcessingV2Service>();
        var checkfrontService = CreateCheckfrontService(_ => JsonResponse(@"{""request"":{""status"":""OK""},""data"":{}}"));
        var sut = CreateSut(
            repo,
            processedRepo,
            classificationRepo,
            bookingRepo,
            customerRepo,
            new TestGoogleCalendarService(),
            checkfrontService,
            dryRunWritesCheckfrontViatorOnly: false,
            dryRunWritesViator: false,
            dryRunWritesCheckfrontOnly: false,
            viatorInboxOnlyMode: true,
            enableCheckfrontEnrichmentForViator: false,
            logger: logger);

        var result = await sut.ProcessSpecificAsync(new List<int> { 101 }, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ErrorCount, Is.EqualTo(0));
        Assert.That(bookingRepo.CreateCount, Is.EqualTo(1));
        Assert.That(bookingRepo.CreatedBookings[0].NumberOfAttendees, Is.EqualTo(5));
        Assert.That(processedRepo.LastRecord?.NumberOfAttendees, Is.EqualTo(5));
        Assert.That(logger.Messages.Any(m => m.Contains("source=estimated_by_price", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(logger.Messages.Any(m => m.Contains("normalized_total=25", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task ProcessSpecific_ViatorExplicitAttendees_WinOverPriceFallback()
    {
        var repo = new TestGmailProcessingRepository();
        var processedRepo = new TestProcessedEmailRepository();
        var bookingRepo = new TestBookingRepository();
        var customerRepo = new TestCustomerRepository();
        var classificationRepo = new TestClassificationRepository(new[]
        {
            new ClassificationRuleRecord
            {
                Id = 102,
                VendorName = "Viator",
                Domain = "t1.viator.com",
                SubjectPhrase = "New Booking for",
                EmailType = "Booking",
                IsActive = true,
                Priority = 500
            }
        });

        repo.InboxById[102] = new InboxEmailRecord
        {
            Id = 102,
            MessageId = "msg-viator-explicit-overrides-estimate",
            Subject = "New Booking for Sun, Feb 22, 2026 (#BR-PRICE-1002)",
            FromEmail = "booking@t1.viator.com",
            ReceivedDate = DateTime.UtcNow,
            CollectedAt = DateTime.UtcNow,
            TextBody = @"Booking Confirmation
Booking Reference: BR-PRICE-1002
Tour Name: Grand Central Terminal History and Mysteries
Travel Date: Sun, Feb 22, 2026
Lead Traveler Name: Explicit Count Walker
Travelers: 2 Adults
Tour Grade: Grand Central Terminal History and Mysteries 14:00
Phone: +1 555 111 3333
Customer Email: explicit.count@example.com
Net Rate: USD $25.00",
            HtmlBody = string.Empty,
            ProcessingStatus = TourEmailInboxProcessingStatus.Pending
        };

        var logger = new TestLogger<GmailProcessingV2Service>();
        var checkfrontService = CreateCheckfrontService(_ => JsonResponse(@"{""request"":{""status"":""OK""},""data"":{}}"));
        var sut = CreateSut(
            repo,
            processedRepo,
            classificationRepo,
            bookingRepo,
            customerRepo,
            new TestGoogleCalendarService(),
            checkfrontService,
            dryRunWritesCheckfrontViatorOnly: false,
            dryRunWritesViator: false,
            dryRunWritesCheckfrontOnly: false,
            viatorInboxOnlyMode: true,
            enableCheckfrontEnrichmentForViator: false,
            logger: logger);

        var result = await sut.ProcessSpecificAsync(new List<int> { 102 }, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ErrorCount, Is.EqualTo(0));
        Assert.That(bookingRepo.CreateCount, Is.EqualTo(1));
        Assert.That(bookingRepo.CreatedBookings[0].NumberOfAttendees, Is.EqualTo(2), "Explicit attendees should take precedence over price estimate.");
        Assert.That(processedRepo.LastRecord?.NumberOfAttendees, Is.EqualTo(2));
        Assert.That(logger.Messages.Any(m => m.Contains("source=explicit", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(logger.Messages.Any(m => m.Contains("estimated_attendees=5", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task ProcessSpecific_DryRunEnabledForViator_BlocksMutatingWrites()
    {
        var repo = new TestGmailProcessingRepository();
        var processedRepo = new TestProcessedEmailRepository();
        var bookingRepo = new TestBookingRepository();
        var customerRepo = new TestCustomerRepository();
        var classificationRepo = new TestClassificationRepository(new[]
        {
            new ClassificationRuleRecord
            {
                Id = 2,
                VendorName = "Viator",
                Domain = "t1.viator.com",
                SubjectPhrase = "New Booking for",
                EmailType = "Booking",
                IsActive = true,
                Priority = 500
            }
        });

        repo.InboxById[2] = new InboxEmailRecord
        {
            Id = 2,
            MessageId = "msg-viator-dryrun",
            Subject = "New Booking for Sun, Feb 22, 2026 (#BR-1361381631)",
            FromEmail = "booking@t1.viator.com",
            ReceivedDate = DateTime.UtcNow,
            CollectedAt = DateTime.UtcNow,
            TextBody = File.ReadAllText(FixturePath("viator_booking_full_text.txt")),
            HtmlBody = File.ReadAllText(FixturePath("viator_booking_full_html.html")),
            ProcessingStatus = TourEmailInboxProcessingStatus.Pending
        };

        var checkfrontService = CreateCheckfrontService(_ => JsonResponse(@"{""request"":{""status"":""ERROR""}}"));
        var sut = CreateSut(
            repo,
            processedRepo,
            classificationRepo,
            bookingRepo,
            customerRepo,
            new TestGoogleCalendarService(),
            checkfrontService,
            dryRunWritesCheckfrontViatorOnly: false,
            dryRunWritesViator: true,
            dryRunWritesCheckfrontOnly: false,
            viatorInboxOnlyMode: true,
            enableCheckfrontEnrichmentForViator: false);

        var result = await sut.ProcessSpecificAsync(new List<int> { 2 }, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ErrorCount, Is.EqualTo(0));
        Assert.That(result.ProcessedCount, Is.EqualTo(1));
        Assert.That(processedRepo.UpsertCount, Is.EqualTo(0), "ProcessedEmail upsert must be blocked in dry-run for Viator context.");
        Assert.That(bookingRepo.CreateCount, Is.EqualTo(0), "Booking create must be blocked in dry-run for Viator context.");
        Assert.That(customerRepo.CreateCount, Is.EqualTo(0), "Customer create must be blocked in dry-run for Viator context.");
    }

    [Test]
    public async Task ProcessSpecific_DryRunEnabledForViator_EmitsDryRunWriteLogs()
    {
        var repo = new TestGmailProcessingRepository();
        var processedRepo = new TestProcessedEmailRepository();
        var bookingRepo = new TestBookingRepository();
        var customerRepo = new TestCustomerRepository();
        var classificationRepo = new TestClassificationRepository(new[]
        {
            new ClassificationRuleRecord
            {
                Id = 22,
                VendorName = "Viator",
                Domain = "t1.viator.com",
                SubjectPhrase = "New Booking for",
                EmailType = "Booking",
                IsActive = true,
                Priority = 500
            }
        });

        repo.InboxById[22] = new InboxEmailRecord
        {
            Id = 22,
            MessageId = "msg-viator-dryrun-log",
            Subject = "New Booking for Sun, Feb 22, 2026 (#BR-1361381631)",
            FromEmail = "booking@t1.viator.com",
            ReceivedDate = DateTime.UtcNow,
            CollectedAt = DateTime.UtcNow,
            TextBody = File.ReadAllText(FixturePath("viator_booking_full_text.txt")),
            HtmlBody = File.ReadAllText(FixturePath("viator_booking_full_html.html")),
            ProcessingStatus = TourEmailInboxProcessingStatus.Pending
        };

        var logger = new TestLogger<GmailProcessingV2Service>();
        var checkfrontService = CreateCheckfrontService(_ => JsonResponse(@"{""request"":{""status"":""ERROR""}}"));
        var sut = CreateSut(
            repo,
            processedRepo,
            classificationRepo,
            bookingRepo,
            customerRepo,
            new TestGoogleCalendarService(),
            checkfrontService,
            dryRunWritesCheckfrontViatorOnly: false,
            dryRunWritesViator: true,
            dryRunWritesCheckfrontOnly: false,
            viatorInboxOnlyMode: true,
            enableCheckfrontEnrichmentForViator: false,
            logger: logger);

        var result = await sut.ProcessSpecificAsync(new List<int> { 22 }, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        var dryRunLogs = logger.Messages
            .Where(m => m.Contains("DryRunWrite Operation=", StringComparison.OrdinalIgnoreCase))
            .ToList();

        TestContext.Progress.WriteLine(string.Join(Environment.NewLine, dryRunLogs.Take(5)));

        Assert.That(dryRunLogs.Count, Is.GreaterThan(0));
        Assert.That(dryRunLogs.Any(m => m.Contains("Entity=Booking", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(dryRunLogs.Any(m => m.Contains("Entity=ProcessedEmail", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task ProcessSpecific_DryRunEnabledForCheckfrontOnly_NonCheckfrontVendor_StillWrites()
    {
        var repo = new TestGmailProcessingRepository();
        var processedRepo = new TestProcessedEmailRepository();
        var bookingRepo = new TestBookingRepository();
        var customerRepo = new TestCustomerRepository();
        var classificationRepo = new TestClassificationRepository(new[]
        {
            new ClassificationRuleRecord
            {
                Id = 3,
                VendorName = "General",
                Domain = "example.com",
                SubjectPhrase = "Random",
                EmailType = "Other",
                IsActive = true,
                Priority = 500
            }
        });

        repo.InboxById[3] = new InboxEmailRecord
        {
            Id = 3,
            MessageId = "msg-general-1",
            Subject = "Random update",
            FromEmail = "hello@example.com",
            ReceivedDate = DateTime.UtcNow,
            CollectedAt = DateTime.UtcNow,
            TextBody = "Simple non-booking email body.",
            HtmlBody = string.Empty,
            ProcessingStatus = TourEmailInboxProcessingStatus.Pending
        };

        var checkfrontService = CreateCheckfrontService(_ => JsonResponse(@"{""request"":{""status"":""OK""},""data"":{}}"));
        var sut = CreateSut(
            repo,
            processedRepo,
            classificationRepo,
            bookingRepo,
            customerRepo,
            new TestGoogleCalendarService(),
            checkfrontService,
            dryRunWritesCheckfrontViatorOnly: false,
            dryRunWritesViator: false,
            dryRunWritesCheckfrontOnly: true,
            viatorInboxOnlyMode: true,
            enableCheckfrontEnrichmentForViator: false);

        var result = await sut.ProcessSpecificAsync(new List<int> { 3 }, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ErrorCount, Is.EqualTo(0));
        Assert.That(processedRepo.UpsertCount, Is.EqualTo(1), "Dry-run should not apply to non-Checkfront/Viator vendors.");
    }

    [Test]
    public async Task ProcessSpecific_DomainlessViatorRule_DoesNotMatchNonViatorSender()
    {
        var repo = new TestGmailProcessingRepository();
        var processedRepo = new TestProcessedEmailRepository();
        var bookingRepo = new TestBookingRepository();
        var customerRepo = new TestCustomerRepository();
        var classificationRepo = new TestClassificationRepository(new[]
        {
            new ClassificationRuleRecord
            {
                Id = 33,
                VendorName = "Viator",
                Domain = string.Empty,
                SubjectPhrase = "New Booking for",
                EmailType = "Booking",
                IsActive = true,
                Priority = 900
            }
        });

        repo.InboxById[33] = new InboxEmailRecord
        {
            Id = 33,
            MessageId = "msg-domainless-viator-guardrail",
            Subject = "New Booking for Sun, Feb 22, 2026 (#BR-1361381631)",
            FromEmail = "alerts@example.net",
            ReceivedDate = DateTime.UtcNow,
            CollectedAt = DateTime.UtcNow,
            TextBody = File.ReadAllText(FixturePath("viator_booking_full_text.txt")),
            HtmlBody = File.ReadAllText(FixturePath("viator_booking_full_html.html")),
            ProcessingStatus = TourEmailInboxProcessingStatus.Pending
        };

        var checkfrontService = CreateCheckfrontService(_ => JsonResponse(@"{""request"":{""status"":""OK""},""data"":{}}"));
        var sut = CreateSut(
            repo,
            processedRepo,
            classificationRepo,
            bookingRepo,
            customerRepo,
            new TestGoogleCalendarService(),
            checkfrontService,
            dryRunWritesCheckfrontViatorOnly: false,
            dryRunWritesViator: false,
            dryRunWritesCheckfrontOnly: false,
            viatorInboxOnlyMode: true,
            enableCheckfrontEnrichmentForViator: false);

        var result = await sut.ProcessSpecificAsync(new List<int> { 33 }, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ErrorCount, Is.EqualTo(0));
        Assert.That(processedRepo.UpsertCount, Is.EqualTo(1));
        Assert.That(processedRepo.LastRecord?.VendorName, Is.EqualTo("General"));
        Assert.That(processedRepo.LastRecord?.EmailType, Is.Not.EqualTo("Booking"));
    }

    [Test]
    public async Task ProcessSpecific_ViatorForwardedSubject_MatchesRuleAndSkipsCheckfront()
    {
        var repo = new TestGmailProcessingRepository();
        var processedRepo = new TestProcessedEmailRepository();
        var bookingRepo = new TestBookingRepository();
        var customerRepo = new TestCustomerRepository();
        var classificationRepo = new TestClassificationRepository(new[]
        {
            new ClassificationRuleRecord
            {
                Id = 4,
                VendorName = "Viator",
                Domain = "t1.viator.com",
                SubjectPhrase = "New Booking for",
                EmailType = "Booking",
                IsActive = true,
                Priority = 500
            }
        });

        repo.InboxById[4] = new InboxEmailRecord
        {
            Id = 4,
            MessageId = "msg-viator-forwarded-1",
            Subject = "Fwd: New Booking for Sun, Feb 22, 2026 (#BR-1361381631)",
            FromEmail = "booking@t1.viator.com",
            ReceivedDate = DateTime.UtcNow,
            CollectedAt = DateTime.UtcNow,
            TextBody = File.ReadAllText(FixturePath("viator_booking_full_text.txt")),
            HtmlBody = File.ReadAllText(FixturePath("viator_booking_full_html.html")),
            ProcessingStatus = TourEmailInboxProcessingStatus.Pending
        };

        var requestLog = new List<string>();
        var checkfrontService = CreateCheckfrontService(req =>
        {
            requestLog.Add(req.RequestUri?.ToString() ?? string.Empty);
            return JsonResponse(@"{""request"":{""status"":""OK""},""data"":{}}");
        });

        var sut = CreateSut(
            repo,
            processedRepo,
            classificationRepo,
            bookingRepo,
            customerRepo,
            new TestGoogleCalendarService(),
            checkfrontService,
            dryRunWritesCheckfrontViatorOnly: false,
            dryRunWritesViator: false,
            dryRunWritesCheckfrontOnly: false,
            viatorInboxOnlyMode: true,
            enableCheckfrontEnrichmentForViator: false);

        var result = await sut.ProcessSpecificAsync(new List<int> { 4 }, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(bookingRepo.CreateCount, Is.EqualTo(1));
        Assert.That(processedRepo.LastRecord?.EmailType, Is.EqualTo("Booking"));
        Assert.That(processedRepo.LastRecord?.BookingCode, Is.EqualTo("BR-1361381631"));
        Assert.That(requestLog.Count, Is.EqualTo(0), "Forwarded Viator booking should still avoid Checkfront calls.");
    }

    [Test]
    public async Task CheckfrontService_GetBookingByCodeOrId_NonOkRequestStatus_ReturnsNull()
    {
        var service = CreateCheckfrontService(_ => JsonResponse(@"{""request"":{""status"":""FAIL""}}"));

        var booking = await service.GetBookingByCodeOrIdAsync("BR-999");

        Assert.That(booking, Is.Null);
    }

    [Test]
    public async Task CheckfrontService_GetBookingsByCustomerId_AllowsAlphanumericCustomerId()
    {
        var observedUris = new List<string>();
        var service = CreateCheckfrontService(req =>
        {
            observedUris.Add(req.RequestUri?.ToString() ?? string.Empty);
            return JsonResponse(@"{
  ""request"": { ""status"": ""OK"" },
  ""booking/index"": {
    ""BR-0001"": {
      ""booking_id"": ""1"",
      ""code"": ""BR-0001"",
      ""customer_name"": ""Alpha Traveler"",
      ""item_name"": ""Sample Tour"",
      ""qty"": ""1"",
      ""total_pax"": ""1""
    }
  }
}");
        });

        var rows = await service.GetBookingsByCustomerIdAsync("TH1-878-188", limit: 10, page: 1);

        Assert.That(rows.Count, Is.EqualTo(1));
        Assert.That(observedUris.Any(u => u.Contains("customer_id=TH1-878-188", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task CheckfrontService_GetBookingsByCustomerId_BookingIndexArrayShape_ParsesRows()
    {
        var service = CreateCheckfrontService(_ => JsonResponse(@"{
  ""request"": { ""status"": ""OK"" },
  ""booking/index"": [
    {
      ""booking_id"": ""2001"",
      ""code"": ""BR-2001"",
      ""customer_name"": ""Array Traveler One"",
      ""item_name"": ""Array Tour 1"",
      ""qty"": ""2"",
      ""total_pax"": ""2""
    },
    {
      ""booking_id"": ""2002"",
      ""id"": ""BR-2002"",
      ""customer_name"": ""Array Traveler Two"",
      ""item_name"": ""Array Tour 2"",
      ""qty"": ""1"",
      ""total_pax"": ""1""
    }
  ]
}"));

        var rows = await service.GetBookingsByCustomerIdAsync("CF-ARRAY-1", limit: 10, page: 1);

        Assert.That(rows.Count, Is.EqualTo(2));
        Assert.That(rows.Any(r => r.Code == "BR-2001"), Is.True);
        Assert.That(rows.Any(r => r.Code == "BR-2002"), Is.True);
    }

    [Test]
    public async Task CheckfrontService_GetBookingByCodeOrId_StartFieldAndItemsArray_HydratesTourTime()
    {
        var service = CreateCheckfrontService(req =>
        {
            var uri = req.RequestUri?.ToString() ?? string.Empty;
            if (uri.Contains("/booking/BR-START-ARRAY", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(@"{
  ""request"": { ""status"": ""OK"" },
  ""booking"": {
    ""id"": ""BR-START-ARRAY"",
    ""code"": ""BR-START-ARRAY"",
    ""start"": ""2026-04-10T09:30:00-04:00"",
    ""itemSummary"": ""Brooklyn Bridge, Brooklyn Heights, Dumbo Tour"",
    ""items"": [
      {
        ""name"": ""Brooklyn Bridge Tour"",
        ""summary"": ""Brooklyn Bridge, Brooklyn Heights, Dumbo Tour"",
        ""start"": ""2026-04-10T09:30:00-04:00"",
        ""qty"": ""3"",
        ""adults"": ""2"",
        ""children"": ""1"",
        ""totalPax"": ""3""
      }
    ]
  }
}");
            }

            if (uri.Contains("booking?code=BR-START-ARRAY", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(@"{ ""request"": { ""status"": ""OK"" }, ""booking/index"": {} }");
            }

            return JsonResponse(@"{ ""request"": { ""status"": ""OK"" } }");
        });

        var booking = await service.GetBookingByCodeOrIdAsync("BR-START-ARRAY");

        Assert.That(booking, Is.Not.Null);
        Assert.That(booking!.StartDateRaw, Is.EqualTo("2026-04-10"));
        Assert.That(booking.StartTimeRaw, Is.EqualTo("09:30"));
        Assert.That(booking.DateTimeDescription, Does.StartWith("2026-04-10T09:30:00"));
        Assert.That(booking.Summary, Is.EqualTo("Brooklyn Bridge, Brooklyn Heights, Dumbo Tour"));
        Assert.That(booking.NumberOfAdults, Is.EqualTo(2));
        Assert.That(booking.NumberOfChildren, Is.EqualTo(1));
        Assert.That(booking.NumberOfAttendees, Is.EqualTo(3));
    }

    [Test]
    public async Task SyncCheckfrontSnapshotBookingAsync_ReRun_UpdatesInsteadOfCreatingDuplicate()
    {
        var repo = new TestGmailProcessingRepository();
        var processedRepo = new TestProcessedEmailRepository();
        var bookingRepo = new TestBookingRepository();
        var customerRepo = new TestCustomerRepository();
        var classificationRepo = new TestClassificationRepository(Array.Empty<ClassificationRuleRecord>());
        var checkfrontService = CreateCheckfrontService(_ => JsonResponse(@"{""request"":{""status"":""OK""},""data"":{}}"));

        var sut = CreateSut(
            repo,
            processedRepo,
            classificationRepo,
            bookingRepo,
            customerRepo,
            new TestGoogleCalendarService(),
            checkfrontService,
            dryRunWritesCheckfrontViatorOnly: false,
            dryRunWritesViator: false,
            dryRunWritesCheckfrontOnly: false,
            viatorInboxOnlyMode: true,
            enableCheckfrontEnrichmentForViator: false);

        var firstSnapshot = new Email.Models.CheckfrontBooking
        {
            BookingId = 1001,
            Code = "BR-SNAPSHOT-1001",
            BookingReference = "1001",
            Status = "Confirmed",
            StatusId = "CONFIRMED",
            StatusName = "Confirmed",
            CustomerName = "Snapshot Traveler",
            CustomerEmail = "snapshot.traveler@example.com",
            CustomerPhone = "+1 (555) 123-4567",
            ItemName = "Brooklyn Bridge Tour",
            ItemTitle = "Brooklyn Bridge Tour",
            Summary = "Brooklyn Bridge Tour",
            StartDateRaw = "2026-04-15",
            StartTimeRaw = "09:30",
            DateDescription = "2026-04-15 09:30",
            DateTimeDescription = "2026-04-15T09:30:00-04:00",
            Total = "5000",
            NumberOfAdults = 2,
            NumberOfChildren = 1,
            NumberOfAttendees = 3
        };

        var first = await sut.SyncCheckfrontSnapshotBookingAsync(firstSnapshot, CancellationToken.None);

        var secondSnapshot = new Email.Models.CheckfrontBooking
        {
            BookingId = 1001,
            Code = "BR-SNAPSHOT-1001",
            BookingReference = "1001",
            Status = "Confirmed",
            StatusId = "CONFIRMED",
            StatusName = "Confirmed",
            CustomerName = "Snapshot Traveler",
            CustomerEmail = "snapshot.traveler@example.com",
            CustomerPhone = "+1 (555) 123-4567",
            ItemName = "Brooklyn Bridge Tour",
            ItemTitle = "Brooklyn Bridge Tour",
            Summary = "Brooklyn Bridge Tour",
            StartDateRaw = "2026-04-15",
            StartTimeRaw = "09:30",
            DateDescription = "2026-04-15 09:30",
            DateTimeDescription = "2026-04-15T09:30:00-04:00",
            Total = "5800",
            NumberOfAdults = 3,
            NumberOfChildren = 1,
            NumberOfAttendees = 4
        };

        var second = await sut.SyncCheckfrontSnapshotBookingAsync(secondSnapshot, CancellationToken.None);

        Assert.That(first.Success, Is.True);
        Assert.That(first.Action, Is.EqualTo(CheckfrontCanonicalWriteAction.Created));
        Assert.That(second.Success, Is.True);
        Assert.That(second.Action, Is.EqualTo(CheckfrontCanonicalWriteAction.Updated));
        Assert.That(bookingRepo.CreateCount, Is.EqualTo(1), "Second run should not create a duplicate booking row.");
        Assert.That(bookingRepo.UpdateCount, Is.EqualTo(1), "Second run should update canonical row.");
        Assert.That(processedRepo.UpsertCount, Is.EqualTo(1), "Synthetic processed row should be stable and reused.");
        var mergedRow = await bookingRepo.FindByCodeAsync("BR-SNAPSHOT-1001", CancellationToken.None);
        Assert.That(mergedRow, Is.Not.Null);
        Assert.That(mergedRow!.BookingAmount, Is.EqualTo(58m));
        Assert.That(mergedRow.Currency, Is.EqualTo("USD"));
    }

    [Test]
    public async Task SyncCheckfrontSnapshotBookingAsync_ExistingProcessedRow_IsRefreshedWithSnapshotFields()
    {
        var repo = new TestGmailProcessingRepository();
        var processedRepo = new TestProcessedEmailRepository();
        var bookingRepo = new TestBookingRepository();
        var customerRepo = new TestCustomerRepository();
        var classificationRepo = new TestClassificationRepository(Array.Empty<ClassificationRuleRecord>());
        var checkfrontService = CreateCheckfrontService(_ => JsonResponse(@"{""request"":{""status"":""OK""},""data"":{}}"));

        var sut = CreateSut(
            repo,
            processedRepo,
            classificationRepo,
            bookingRepo,
            customerRepo,
            new TestGoogleCalendarService(),
            checkfrontService,
            dryRunWritesCheckfrontViatorOnly: false,
            dryRunWritesViator: false,
            dryRunWritesCheckfrontOnly: false,
            viatorInboxOnlyMode: true,
            enableCheckfrontEnrichmentForViator: false);

        const string bookingCode = "GPSR-300326";
        const string messageId = "CHECKFRONT-SYNC-GPSR-300326";
        const int processedId = 77;
        const int inboxId = 501;

        repo.InboxById[inboxId] = new InboxEmailRecord
        {
            Id = inboxId,
            MessageId = messageId,
            Subject = "Checkfront Sync",
            FromEmail = "checkfront-sync@system.local",
            ReceivedDate = DateTime.UtcNow,
            CollectedAt = DateTime.UtcNow,
            TextBody = "seed",
            HtmlBody = string.Empty,
            ProcessingStatus = TourEmailInboxProcessingStatus.Processed
        };

        repo.ProcessedByBookingCode.Add(new ProcessedEmailDisplayDto
        {
            Id = processedId,
            InboxEmailId = inboxId,
            MessageId = messageId,
            BookingCode = bookingCode,
            VendorName = "Checkfront",
            EmailType = "Booking",
            ProcessingStatus = "completed",
            ProcessingCompletedAt = DateTime.UtcNow
        });

        repo.ProcessedFullByMessageId[messageId] = new ProcessedEmailRecord
        {
            Id = processedId,
            InboxEmailId = inboxId,
            MessageId = messageId,
            VendorName = "Checkfront",
            EmailType = "Booking",
            ProcessingStatus = "completed",
            ProcessingAttempts = 1,
            BookingCode = bookingCode,
            CustomerName = string.Empty,
            CustomerEmail = "doris1977@gmail.com",
            CustomerPhone = "+14846824182",
            NumberOfAttendees = 0,
            NumberOfAdults = 0,
            NumberOfChildren = 0,
            TourDate = "2026-04-01",
            TourTime = null,
            TourName = "Times Square, Grand Central, Rockefeller tour!",
            TourLocation = string.Empty,
            PlainTextContent = "seed",
            HtmlContent = string.Empty,
            ExtractedBookingCode = bookingCode,
            NewBookingCode = string.Empty,
            PreviousBookingCode = string.Empty,
            IsCancellation = false,
            IsModification = false,
            IsBooking = true
        };

        await bookingRepo.CreateAsync(new Booking
        {
            BookingCode = bookingCode,
            MessageId = messageId,
            ProcessedEmailId = processedId,
            IsActive = true,
            IsConfirmation = true
        }, CancellationToken.None);

        var snapshot = new Email.Models.CheckfrontBooking
        {
            BookingId = 999,
            Code = bookingCode,
            BookingReference = "999",
            Status = "Confirmed",
            StatusId = "PAID",
            StatusName = "Paid",
            CustomerName = "Doris Harrer-Dulnig",
            CustomerEmail = "doris1977@gmail.com",
            CustomerPhone = "+14846824182",
            ItemName = "Times Square, Grand Central, Rockefeller tour!",
            ItemTitle = "Times Square, Grand Central, Rockefeller tour!",
            Summary = "Times Square, Grand Central, Rockefeller tour!",
            StartDateRaw = "2026-04-01",
            StartTimeRaw = "09:30",
            DateDescription = "2026-04-01 09:30",
            DateTimeDescription = "2026-04-01T09:30:00-05:00",
            NumberOfAdults = 2,
            NumberOfChildren = 0,
            NumberOfAttendees = 2
        };

        var result = await sut.SyncCheckfrontSnapshotBookingAsync(snapshot, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Action, Is.EqualTo(CheckfrontCanonicalWriteAction.Updated));
        Assert.That(processedRepo.UpsertCount, Is.EqualTo(1), "Existing processed row should be refreshed.");
        Assert.That(processedRepo.LastRecord, Is.Not.Null);
        Assert.That(processedRepo.LastRecord!.MessageId, Is.EqualTo(messageId));
        Assert.That(processedRepo.LastRecord.CustomerName, Is.EqualTo("Doris Harrer-Dulnig"));
        Assert.That(processedRepo.LastRecord.NumberOfAttendees, Is.EqualTo(2));
        Assert.That(processedRepo.LastRecord.NumberOfAdults, Is.EqualTo(2));
        Assert.That(processedRepo.LastRecord.TourTime, Is.EqualTo("09:30"));
        Assert.That(bookingRepo.UpdateCount, Is.EqualTo(1));
    }

    [Test]
    public async Task SyncCheckfrontSnapshotBookingAsync_DoesNotParseYearAsAttendees_AndClearsStaleCount()
    {
        var repo = new TestGmailProcessingRepository();
        var processedRepo = new TestProcessedEmailRepository();
        var bookingRepo = new TestBookingRepository();
        var customerRepo = new TestCustomerRepository();
        var classificationRepo = new TestClassificationRepository(Array.Empty<ClassificationRuleRecord>());
        var checkfrontService = CreateCheckfrontService(_ => JsonResponse(@"{""request"":{""status"":""OK""},""data"":{}}"));

        var sut = CreateSut(
            repo,
            processedRepo,
            classificationRepo,
            bookingRepo,
            customerRepo,
            new TestGoogleCalendarService(),
            checkfrontService,
            dryRunWritesCheckfrontViatorOnly: false,
            dryRunWritesViator: false,
            dryRunWritesCheckfrontOnly: false,
            viatorInboxOnlyMode: true,
            enableCheckfrontEnrichmentForViator: false);

        const string bookingCode = "GPSR-300326";
        const string messageId = "CHECKFRONT-SYNC-GPSR-300326";
        const int processedId = 88;
        const int inboxId = 601;

        repo.InboxById[inboxId] = new InboxEmailRecord
        {
            Id = inboxId,
            MessageId = messageId,
            Subject = "Checkfront Sync",
            FromEmail = "checkfront-sync@system.local",
            ReceivedDate = DateTime.UtcNow,
            CollectedAt = DateTime.UtcNow,
            TextBody = "seed",
            HtmlBody = string.Empty,
            ProcessingStatus = TourEmailInboxProcessingStatus.Processed
        };

        repo.ProcessedByBookingCode.Add(new ProcessedEmailDisplayDto
        {
            Id = processedId,
            InboxEmailId = inboxId,
            MessageId = messageId,
            BookingCode = bookingCode,
            VendorName = "Checkfront",
            EmailType = "Booking",
            ProcessingStatus = "completed",
            ProcessingCompletedAt = DateTime.UtcNow
        });

        repo.ProcessedFullByMessageId[messageId] = new ProcessedEmailRecord
        {
            Id = processedId,
            InboxEmailId = inboxId,
            MessageId = messageId,
            VendorName = "Checkfront",
            EmailType = "Booking",
            ProcessingStatus = "completed",
            ProcessingAttempts = 1,
            BookingCode = bookingCode,
            NumberOfAttendees = 2026,
            NumberOfAdults = 0,
            NumberOfChildren = 0,
            TourDate = "2026-04-01",
            TourTime = "14:00",
            TourName = "Times Square, Grand Central, Rockefeller tour!",
            ExtractedBookingCode = bookingCode,
            NewBookingCode = string.Empty,
            PreviousBookingCode = string.Empty,
            IsCancellation = false,
            IsModification = false,
            IsBooking = true
        };

        await bookingRepo.CreateAsync(new Booking
        {
            BookingCode = bookingCode,
            MessageId = messageId,
            ProcessedEmailId = processedId,
            NumberOfAttendees = 2026,
            NumberOfAdults = 0,
            NumberOfChildren = 0,
            IsActive = true,
            IsConfirmation = true
        }, CancellationToken.None);

        var snapshot = new Email.Models.CheckfrontBooking
        {
            BookingId = 1000,
            Code = bookingCode,
            BookingReference = "1000",
            Status = "Confirmed",
            StatusId = "PAID",
            StatusName = "Paid",
            CustomerName = "Doris Harrer-Dullnig",
            CustomerEmail = "doris1977@gmail.com",
            CustomerPhone = "+14846824182",
            ItemName = "Times Square, Grand Central, Rockefeller tour!",
            ItemTitle = "Times Square, Grand Central, Rockefeller tour!",
            Summary = "Times Square, Grand Central, Rockefeller tour!",
            StartDateRaw = "2026-04-01",
            StartTimeRaw = "14:00",
            DateDescription = "2026-04-01 14:00",
            DateTimeDescription = "2026-04-01T14:00:00-04:00",
            NumberOfAdults = 0,
            NumberOfChildren = 0,
            NumberOfAttendees = 0
        };

        var result = await sut.SyncCheckfrontSnapshotBookingAsync(snapshot, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Action, Is.EqualTo(CheckfrontCanonicalWriteAction.Updated));
        Assert.That(processedRepo.LastRecord, Is.Not.Null);
        Assert.That(processedRepo.LastRecord!.NumberOfAttendees, Is.EqualTo(0));
        Assert.That(processedRepo.LastRecord.NumberOfAdults, Is.EqualTo(0));
        Assert.That(processedRepo.LastRecord.NumberOfChildren, Is.EqualTo(0));
    }

    [Test]
    public async Task SyncCheckfrontSnapshotBookingAsync_MinorUnitTotal_EstimatesAttendeesFromPrice()
    {
        var repo = new TestGmailProcessingRepository();
        var processedRepo = new TestProcessedEmailRepository();
        var bookingRepo = new TestBookingRepository();
        var customerRepo = new TestCustomerRepository();
        var classificationRepo = new TestClassificationRepository(Array.Empty<ClassificationRuleRecord>());
        var checkfrontService = CreateCheckfrontService(_ => JsonResponse(@"{""request"":{""status"":""OK""},""data"":{}}"));
        var logger = new TestLogger<GmailProcessingV2Service>();

        var sut = CreateSut(
            repo,
            processedRepo,
            classificationRepo,
            bookingRepo,
            customerRepo,
            new TestGoogleCalendarService(),
            checkfrontService,
            dryRunWritesCheckfrontViatorOnly: false,
            dryRunWritesViator: false,
            dryRunWritesCheckfrontOnly: false,
            viatorInboxOnlyMode: true,
            enableCheckfrontEnrichmentForViator: false,
            logger: logger);

        var snapshot = new Email.Models.CheckfrontBooking
        {
            BookingId = 5000,
            Code = "BR-PRICE-5000",
            BookingReference = "5000",
            Status = "Confirmed",
            StatusId = "PAID",
            StatusName = "Paid",
            CustomerName = "Minor Unit Walker",
            CustomerEmail = "minor.units@example.com",
            CustomerPhone = "+1 555 777 9999",
            ItemName = "Brooklyn Bridge Tour",
            ItemTitle = "Brooklyn Bridge Tour",
            Summary = "Brooklyn Bridge Tour",
            StartDateRaw = "2026-04-18",
            StartTimeRaw = "09:30",
            DateDescription = "2026-04-18 09:30",
            DateTimeDescription = "2026-04-18T09:30:00-04:00",
            Total = "5000",
            NumberOfAdults = 0,
            NumberOfChildren = 0,
            NumberOfAttendees = 0
        };

        var result = await sut.SyncCheckfrontSnapshotBookingAsync(snapshot, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Action, Is.EqualTo(CheckfrontCanonicalWriteAction.Created));
        Assert.That(bookingRepo.CreateCount, Is.EqualTo(1));
        Assert.That(bookingRepo.CreatedBookings[0].NumberOfAttendees, Is.EqualTo(10));
        Assert.That(bookingRepo.CreatedBookings[0].BookingAmount, Is.EqualTo(50m));
        Assert.That(bookingRepo.CreatedBookings[0].Currency, Is.EqualTo("USD"));
        Assert.That(processedRepo.LastRecord?.NumberOfAttendees, Is.EqualTo(10));
        Assert.That(logger.Messages.Any(m => m.Contains("source=estimated_by_price", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(logger.Messages.Any(m => m.Contains("minor_unit_scaled=True", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(logger.Messages.Any(m => m.Contains("normalized_total=50", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task SyncCheckfrontSnapshotBookingAsync_SparseApiTotals_DoNotUnderscaleOrOverwriteExistingAttendees()
    {
        var repo = new TestGmailProcessingRepository();
        var processedRepo = new TestProcessedEmailRepository();
        var bookingRepo = new TestBookingRepository();
        var customerRepo = new TestCustomerRepository();
        var classificationRepo = new TestClassificationRepository(Array.Empty<ClassificationRuleRecord>());
        var checkfrontService = CreateCheckfrontService(_ => JsonResponse(@"{""request"":{""status"":""OK""},""data"":{}}"));

        var sut = CreateSut(
            repo,
            processedRepo,
            classificationRepo,
            bookingRepo,
            customerRepo,
            new TestGoogleCalendarService(),
            checkfrontService,
            dryRunWritesCheckfrontViatorOnly: false,
            dryRunWritesViator: false,
            dryRunWritesCheckfrontOnly: false,
            viatorInboxOnlyMode: true,
            enableCheckfrontEnrichmentForViator: false);

        const string bookingCode = "YHCH-300325";
        await bookingRepo.CreateAsync(new Booking
        {
            BookingCode = bookingCode,
            NumberOfAttendees = 47,
            NumberOfAdults = 47,
            NumberOfChildren = 0,
            BookingAmount = null,
            Currency = "USD",
            IsActive = true,
            IsConfirmation = true
        }, CancellationToken.None);

        var sparseSnapshot = new Email.Models.CheckfrontBooking
        {
            BookingId = 300325,
            Code = bookingCode,
            BookingReference = "300325",
            Status = "Confirmed",
            StatusId = "PAID",
            StatusName = "Paid",
            CustomerName = "Sparse Snapshot",
            CustomerEmail = "sparse.snapshot@example.com",
            CustomerPhone = "+1 555 123 4567",
            ItemName = "Brooklyn Bridge Tour",
            ItemTitle = "Brooklyn Bridge Tour",
            Summary = "Brooklyn Bridge Tour",
            StartDateRaw = "2026-03-25",
            StartTimeRaw = "09:30",
            DateDescription = "2026-03-25 09:30",
            DateTimeDescription = "2026-03-25T09:30:00-04:00",
            Total = "234",
            NumberOfAdults = 0,
            NumberOfChildren = 0,
            NumberOfAttendees = 0
        };

        var result = await sut.SyncCheckfrontSnapshotBookingAsync(sparseSnapshot, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Action, Is.EqualTo(CheckfrontCanonicalWriteAction.Updated));
        var updated = await bookingRepo.FindByCodeAsync(bookingCode, CancellationToken.None);
        Assert.That(updated, Is.Not.Null);
        Assert.That(updated!.NumberOfAttendees, Is.EqualTo(47), "Sparse API attendee payload should not overwrite known attendee totals.");
        Assert.That(updated.NumberOfAdults, Is.EqualTo(47));
        Assert.That(updated.NumberOfChildren, Is.EqualTo(0));
        Assert.That(updated.BookingAmount, Is.EqualTo(234m), "Raw 234 should stay 234 when minor-unit scaling fails attendee sanity.");
    }

    private static GmailProcessingV2Service CreateSut(
        TestGmailProcessingRepository repo,
        TestProcessedEmailRepository processedRepo,
        TestClassificationRepository classificationRepo,
        TestBookingRepository bookingRepo,
        TestCustomerRepository customerRepo,
        TestGoogleCalendarService calendarService,
        CheckfrontService checkfrontService,
        bool dryRunWritesCheckfrontViatorOnly,
        bool dryRunWritesViator = false,
        bool dryRunWritesCheckfrontOnly = false,
        bool viatorInboxOnlyMode = true,
        bool enableCheckfrontEnrichmentForViator = false,
        ILogger<GmailProcessingV2Service>? logger = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GmailProcessing:ViatorInboxOnlyMode"] = viatorInboxOnlyMode ? "true" : "false",
                ["GmailProcessing:EnableCheckfrontEnrichmentForViator"] = enableCheckfrontEnrichmentForViator ? "true" : "false",
                ["GmailProcessing:DryRunWritesViator"] = dryRunWritesViator ? "true" : "false",
                ["GmailProcessing:DryRunWritesCheckfrontOnly"] = dryRunWritesCheckfrontOnly ? "true" : "false",
                ["GmailProcessing:DryRunWritesCheckfrontViatorOnly"] = dryRunWritesCheckfrontViatorOnly ? "true" : "false",
                ["GmailProcessing:DryRunVerboseFieldDiff"] = "true",
                ["CheckfrontSync:MinIntervalMinutes"] = "1",
                ["CheckfrontSync:DaysBack"] = "30",
                ["CheckfrontSync:LimitPerPage"] = "50",
                ["CheckfrontSync:MaxPages"] = "5"
            })
            .Build();

        return new GmailProcessingV2Service(
            logger ?? NullLogger<GmailProcessingV2Service>.Instance,
            repo,
            config,
            processedRepo,
            classificationRepo,
            bookingRepo,
            customerRepo,
            calendarService,
            checkfrontService);
    }

    private static CheckfrontService CreateCheckfrontService(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new StubHttpMessageHandler(responder);
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.test/api/3.0/")
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Checkfront:ApiEndpoint"] = "https://example.test/api/3.0/",
                ["Checkfront:ApiKey"] = "key",
                ["Checkfront:ApiSecret"] = "secret",
                ["Checkfront:CompanyName"] = "CityShuffles"
            })
            .Build();

        return new CheckfrontService(client, config, NullLogger<CheckfrontService>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
            => NoopScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }

        private sealed class NoopScope : IDisposable
        {
            public static readonly NoopScope Instance = new();
            public void Dispose() { }
        }
    }

    private sealed class TestClassificationRepository : IClassificationRepository
    {
        private readonly IReadOnlyList<ClassificationRuleRecord> _rules;

        public TestClassificationRepository(IReadOnlyList<ClassificationRuleRecord> rules)
        {
            _rules = rules;
        }

        public Task<IReadOnlyList<ClassificationRuleRecord>> LoadActiveClassificationRulesAsync(CancellationToken cancellationToken)
            => Task.FromResult(_rules);
    }

    private sealed class TestProcessedEmailRepository : IProcessedEmailRepository
    {
        private int _nextId = 1;

        public int UpsertCount { get; private set; }
        public int SetLatestCount { get; private set; }
        public ProcessedEmailRecord? LastRecord { get; private set; }

        public Task<int> UpsertProcessedEmailAsync(ProcessedEmailRecord record, CancellationToken cancellationToken)
        {
            UpsertCount++;
            LastRecord = record;
            if (record.Id <= 0)
            {
                record.Id = _nextId++;
            }

            return Task.FromResult(record.Id);
        }

        public Task SetLatestActionForThreadAsync(string vendorName, string? bookingCode, int processedEmailId, CancellationToken cancellationToken)
        {
            SetLatestCount++;
            return Task.CompletedTask;
        }

        public Task<ProcessedEmailDisplayDto?> GetProcessedEmailByIdAsync(int processedId, CancellationToken cancellationToken)
            => Task.FromResult<ProcessedEmailDisplayDto?>(null);

        public Task DeleteAsync(IReadOnlyList<int> processedIds, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class TestBookingRepository : IBookingRepository
    {
        private int _nextId = 1;
        private readonly Dictionary<string, Booking> _byCode = new(StringComparer.OrdinalIgnoreCase);

        public int CreateCount { get; private set; }
        public int UpdateCount { get; private set; }
        public int CancelCount { get; private set; }
        public int DeactivateCount { get; private set; }
        public List<Booking> CreatedBookings { get; } = new();

        public Task<Booking?> FindByCodeAsync(string bookingCode, CancellationToken cancellationToken)
        {
            _byCode.TryGetValue(bookingCode, out var row);
            return Task.FromResult<Booking?>(row);
        }

        public Task<IReadOnlyList<Booking>> GetByCodesAsync(IReadOnlyList<string> bookingCodes, CancellationToken cancellationToken)
        {
            if (bookingCodes == null || bookingCodes.Count == 0)
            {
                return Task.FromResult<IReadOnlyList<Booking>>(Array.Empty<Booking>());
            }

            var normalized = new HashSet<string>(bookingCodes, StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<Booking> rows = _byCode
                .Where(kvp => normalized.Contains(kvp.Key))
                .Select(kvp => CloneBooking(kvp.Value))
                .ToList();
            return Task.FromResult(rows);
        }

        public Task<int> CreateAsync(Booking booking, CancellationToken cancellationToken)
        {
            CreateCount++;
            if (booking.Id <= 0)
            {
                booking.Id = _nextId++;
            }

            CreatedBookings.Add(CloneBooking(booking));
            if (!string.IsNullOrWhiteSpace(booking.BookingCode))
            {
                _byCode[booking.BookingCode] = CloneBooking(booking);
            }

            return Task.FromResult(booking.Id);
        }

        public Task UpdateAsync(Booking booking, CancellationToken cancellationToken)
        {
            UpdateCount++;
            if (!string.IsNullOrWhiteSpace(booking.BookingCode))
            {
                _byCode[booking.BookingCode] = CloneBooking(booking);
            }

            return Task.CompletedTask;
        }

        public Task DeactivateOriginalOnModificationAsync(string originalBookingCode, string newBookingCode, CancellationToken cancellationToken)
        {
            DeactivateCount++;
            return Task.CompletedTask;
        }

        public Task<int> CancelAsync(string bookingCode, string? cancellationReason, CancellationToken cancellationToken)
        {
            CancelCount++;
            return Task.FromResult(1);
        }

        public Task<IReadOnlyList<Booking>> GetRecentAsync(int limit, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Booking>>(CreatedBookings.Take(limit).ToList());

        public Task DeleteAsync(IReadOnlyList<int> bookingIds, CancellationToken cancellationToken)
            => Task.CompletedTask;

        private static Booking CloneBooking(Booking b)
            => new()
            {
                Id = b.Id,
                CustomerId = b.CustomerId,
                CustomerIdentifier = b.CustomerIdentifier,
                ProcessedEmailId = b.ProcessedEmailId,
                MessageId = b.MessageId,
                BookingCode = b.BookingCode,
                VendorName = b.VendorName,
                EmailType = b.EmailType,
                IsCancellation = b.IsCancellation,
                IsModification = b.IsModification,
                IsConfirmation = b.IsConfirmation,
                IsActive = b.IsActive,
                TourName = b.TourName,
                TourDate = b.TourDate,
                TourDayOfWeek = b.TourDayOfWeek,
                TourTime = b.TourTime,
                TourLocation = b.TourLocation,
                TourTimeZone = b.TourTimeZone,
                DisplayDate = b.DisplayDate,
                DisplayTime = b.DisplayTime,
                CustomerName = b.CustomerName,
                CustomerEmail = b.CustomerEmail,
                CustomerPhone = b.CustomerPhone,
                NumberOfAttendees = b.NumberOfAttendees,
                NumberOfAdults = b.NumberOfAdults,
                NumberOfChildren = b.NumberOfChildren,
                NumberOfInfants = b.NumberOfInfants,
                Language = b.Language,
                CountryOfOrigin = b.CountryOfOrigin,
                BookingStatus = b.BookingStatus,
                BookingAmount = b.BookingAmount,
                Currency = b.Currency,
                SpecialRequests = b.SpecialRequests,
                GuideAssigned = b.GuideAssigned,
                CalendarEventId = b.CalendarEventId,
                IsCalendarExportSuccess = b.IsCalendarExportSuccess,
                IsSheetExportSuccess = b.IsSheetExportSuccess,
                ProcessingNotes = b.ProcessingNotes,
                CreatedAt = b.CreatedAt,
                UpdatedAt = b.UpdatedAt
            };
    }

    private sealed class TestCustomerRepository : ICustomerRepository
    {
        private int _nextId = 1;
        private readonly Dictionary<int, Customer> _byId = new();

        public int CreateCount { get; private set; }
        public int UpdateCount { get; private set; }

        public Task<Customer?> GetByIdAsync(int customerId, CancellationToken cancellationToken)
        {
            _byId.TryGetValue(customerId, out var row);
            return Task.FromResult(CloneCustomer(row));
        }

        public Task<Customer?> GetByBookingCodeAsync(string bookingCode, CancellationToken cancellationToken)
            => Task.FromResult<Customer?>(null);

        public Task<Customer?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(normalizedEmail))
            {
                return Task.FromResult<Customer?>(null);
            }

            var match = _byId.Values.FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(x.Email) &&
                string.Equals(x.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(CloneCustomer(match));
        }

        public Task<Customer?> GetByPhoneAsync(string normalizedPhone, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(normalizedPhone))
            {
                return Task.FromResult<Customer?>(null);
            }

            var digits = new string(normalizedPhone.Where(char.IsDigit).ToArray());
            var match = _byId.Values.FirstOrDefault(x =>
            {
                var existingDigits = new string((x.PhoneNumber ?? string.Empty).Where(char.IsDigit).ToArray());
                return !string.IsNullOrWhiteSpace(existingDigits) &&
                       (existingDigits.EndsWith(digits, StringComparison.Ordinal) ||
                        digits.EndsWith(existingDigits, StringComparison.Ordinal));
            });

            return Task.FromResult(CloneCustomer(match));
        }

        public Task<int> CreateAsync(Customer customer, CancellationToken cancellationToken)
        {
            CreateCount++;
            if (customer.Id <= 0)
            {
                customer.Id = _nextId++;
            }

            _byId[customer.Id] = CloneCustomer(customer)!;
            return Task.FromResult(customer.Id);
        }

        public Task UpdateAsync(Customer customer, CancellationToken cancellationToken)
        {
            UpdateCount++;
            if (customer.Id > 0)
            {
                _byId[customer.Id] = CloneCustomer(customer)!;
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Customer>> FindCandidatesByNameAndPhoneAsync(string customerName, string? phoneLast7, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Customer>>(Array.Empty<Customer>());

        public Task<IReadOnlyList<Customer>> FindCandidatesByEmailSimilarityAsync(string emailUser, string emailDomain, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Customer>>(Array.Empty<Customer>());

        public Task<IReadOnlyList<Customer>> GetRecentAsync(int limit, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Customer>>(Array.Empty<Customer>());

        public Task DeleteAsync(IReadOnlyList<int> customerIds, CancellationToken cancellationToken)
            => Task.CompletedTask;

        private static Customer? CloneCustomer(Customer? c)
        {
            if (c == null)
            {
                return null;
            }

            return new Customer
            {
                Id = c.Id,
                FullName = c.FullName,
                FirstName = c.FirstName,
                LastName = c.LastName,
                PhoneNumber = c.PhoneNumber,
                Email = c.Email,
                CustomerIdentifier = c.CustomerIdentifier,
                BookingIds = c.BookingIds,
                TotalBookings = c.TotalBookings,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt
            };
        }
    }

    private sealed class TestGoogleCalendarService : IGoogleCalendarService
    {
        public int InsertCount { get; private set; }
        public int UpdateCount { get; private set; }

        public Task<List<GoogleAppointmentModel>> GetEventsAsync(DateTime start, DateTime end)
            => Task.FromResult(new List<GoogleAppointmentModel>());

        public Task<GoogleAppointmentModel?> GetEventAsync(string eventId)
            => Task.FromResult<GoogleAppointmentModel?>(null);

        public Task<string?> InsertEventAsync(GoogleAppointmentModel eventData)
        {
            InsertCount++;
            return Task.FromResult<string?>($"event-{InsertCount}");
        }

        public Task UpdateEventAsync(GoogleAppointmentModel eventData)
        {
            UpdateCount++;
            return Task.CompletedTask;
        }

        public Task RemoveEventAsync(string id)
            => Task.CompletedTask;
    }

    private sealed class TestGmailProcessingRepository : IGmailProcessingRepository
    {
        private int _nextInboxId = 1000;

        public Dictionary<int, InboxEmailRecord> InboxById { get; } = new();
        public List<ProcessedEmailDisplayDto> ProcessedByBookingCode { get; } = new();
        public Dictionary<string, ProcessedEmailRecord> ProcessedFullByMessageId { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlyList<ClassificationRuleRecord>> LoadActiveClassificationRulesAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ClassificationRuleRecord>>(Array.Empty<ClassificationRuleRecord>());

        public Task<IReadOnlyList<UnprocessedEmailRowDto>> GetUnprocessedInboxEmailsAsync(int limit, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<UnprocessedEmailRowDto>>(Array.Empty<UnprocessedEmailRowDto>());

        public Task<IReadOnlyList<InboxEmailRecord>> GetInboxEmailsByIdsAsync(IReadOnlyList<int> inboxIds, CancellationToken ct)
        {
            var rows = inboxIds.Where(InboxById.ContainsKey).Select(id => InboxById[id]).ToList();
            return Task.FromResult<IReadOnlyList<InboxEmailRecord>>(rows);
        }

        public Task<IReadOnlyList<InboxEmailRecord>> GetUnprocessedInboxPageAsync(DateTime? afterReceivedDate, int? afterId, int pageSize, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<InboxEmailRecord>>(Array.Empty<InboxEmailRecord>());

        public Task<int?> GetInboxEmailIdByMessageIdAsync(string messageId, CancellationToken ct)
            => Task.FromResult<int?>(InboxById.Values.FirstOrDefault(x => x.MessageId == messageId)?.Id);

        public Task<int> UpsertProcessedEmailAsync(ProcessedEmailRecord rec, CancellationToken ct)
            => Task.FromResult(rec.Id > 0 ? rec.Id : 1);

        public Task SetLatestActionForThreadAsync(string vendorName, string? bookingCode, int processedEmailId, CancellationToken ct)
            => Task.CompletedTask;

        public Task<GmailProcessingStatusDto> GetStatusAsync(CancellationToken ct)
            => Task.FromResult(new GmailProcessingStatusDto());

        public Task<ProcessedEmailDisplayDto?> GetProcessedEmailByIdAsync(int processedId, CancellationToken ct)
            => Task.FromResult<ProcessedEmailDisplayDto?>(null);

        public Task<IReadOnlyList<ProcessedEmailDisplayDto>> GetRecentProcessedAsync(int limit, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ProcessedEmailDisplayDto>>(Array.Empty<ProcessedEmailDisplayDto>());

        public Task<IReadOnlyList<ProcessedEmailRecord>> GetProcessedByIdsAsync(IReadOnlyList<int> processedIds, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ProcessedEmailRecord>>(Array.Empty<ProcessedEmailRecord>());

        public Task DeleteAsync(IReadOnlyList<int> processedIds, CancellationToken ct)
            => Task.CompletedTask;

        public Task<IReadOnlyList<ProcessedEmailDisplayDto>> GetRecentSkippedAsync(int limit, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ProcessedEmailDisplayDto>>(Array.Empty<ProcessedEmailDisplayDto>());

        public Task UpdateInboxProcessingStatusAsync(int inboxEmailId, TourEmailInboxProcessingStatus status, CancellationToken ct)
            => Task.CompletedTask;

        public Task UpdateInboxOriginalBookingAsync(int inboxEmailId, int? originalBookingId, string? originalBookingCode, string? originalMessageId, CancellationToken ct)
            => Task.CompletedTask;

        public Task<IReadOnlyList<InboxProcessingStatusCountDto>> GetInboxProcessingStatusCountsAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<InboxProcessingStatusCountDto>>(Array.Empty<InboxProcessingStatusCountDto>());

        public Task<IReadOnlyList<ProcessedEmailDisplayDto>> GetProcessedByBookingCodeAsync(string bookingCode, CancellationToken ct)
        {
            var rows = ProcessedByBookingCode
                .Where(x => string.Equals(x.BookingCode, bookingCode, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return Task.FromResult<IReadOnlyList<ProcessedEmailDisplayDto>>(rows);
        }

        public Task<IReadOnlyList<int>> GetProcessedIdsForModificationsMissingTourFieldsAsync(int limit, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<int>>(Array.Empty<int>());

        public Task<HydratedEmailDiagnosticContext?> GetHydratedDiagnosticByMessageIdAsync(string messageId, CancellationToken ct)
            => Task.FromResult<HydratedEmailDiagnosticContext?>(null);

        public Task<ProcessedEmailRecord?> GetProcessedFullByMessageIdAsync(string messageId, CancellationToken ct)
        {
            ProcessedFullByMessageId.TryGetValue(messageId, out var row);
            return Task.FromResult<ProcessedEmailRecord?>(row);
        }

        public Task<int> RepairProcessedBookingCodesAsync(int limit, CancellationToken ct)
            => Task.FromResult(0);

        public Task<ProcessedEmailRecord?> GetEarliestEventByBookingCodeAsync(string bookingCode, CancellationToken ct)
            => Task.FromResult<ProcessedEmailRecord?>(null);

        public Task<ProcessedEmailRecord?> GetEarliestBookingEventByBookingCodeAsync(string bookingCode, CancellationToken ct)
            => Task.FromResult<ProcessedEmailRecord?>(null);

        public Task<ProcessedEmailRecord?> GetEarliestModificationProducingNewCodeAsync(string newCode, CancellationToken ct)
            => Task.FromResult<ProcessedEmailRecord?>(null);

        public Task<IReadOnlyList<ProcessedEmailRecord>> GetProcessedByBookingCodesAsync(IReadOnlyList<string> codes, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ProcessedEmailRecord>>(Array.Empty<ProcessedEmailRecord>());

        public Task<IReadOnlyList<ProcessedEmailRecord>> GetModificationsByPreviousCodeAsync(string previousCode, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ProcessedEmailRecord>>(Array.Empty<ProcessedEmailRecord>());

        public Task<int> OverrideInboxOriginalBookingPointerAsync(int inboxEmailId, int? rootBookingId, string? rootBookingCode, string? rootMessageId, CancellationToken ct)
            => Task.FromResult(0);

        public Task<IReadOnlyList<InboxEmailRecord>> GetInboxBatchNeedingOriginRebuildAsync(int lastIdExclusive, int batchSize, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<InboxEmailRecord>>(Array.Empty<InboxEmailRecord>());

        public Task<string?> GetEffectiveBookingCodeForMessageAsync(string messageId, CancellationToken ct)
            => Task.FromResult<string?>(null);

        public Task<int> CreateInboxEmailAsync(InboxEmailRecord email, CancellationToken ct)
        {
            if (email.Id <= 0)
            {
                email.Id = ++_nextInboxId;
            }

            InboxById[email.Id] = email;
            return Task.FromResult(email.Id);
        }
    }
}
