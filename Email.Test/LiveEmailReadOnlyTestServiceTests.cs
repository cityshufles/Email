using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Email.Models;
using Email.Services;
using Email.Services.GmailProcessing;
using Email.Services.GmailProcessing.Dtos;
using Email.Services.GmailProcessing.Models;
using Email.Services.GmailProcessing.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Email.Test;

[TestFixture]
public class LiveEmailReadOnlyTestServiceTests
{
    private static string FixturePath(string fileName)
        => Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", fileName);

    [Test]
    public async Task RunAsync_NotificationScenario_ExtractsWalkerAndLogsWouldWritePreview()
    {
        var repo = new TestClassificationRepository(new[]
        {
            new ClassificationRuleRecord
            {
                Id = 1,
                VendorName = "Checkfront",
                Domain = "bookings.checkfront.com",
                SubjectPhrase = "Viator booking notification",
                EmailType = "Booking",
                IsActive = true,
                Priority = 500
            }
        });

        var service = CreateCheckfrontApi(req =>
        {
            var url = req.RequestUri?.ToString() ?? string.Empty;
            if (url.Contains("customer?limit=100", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(@"{
  ""request"": { ""status"": ""OK"" },
  ""customers"": {
    ""CF-123"": {
      ""customer_id"": ""CF-123"",
      ""customer_name"": ""KATIA YAZMIN CAVAZOS LEAL"",
      ""customer_email"": ""katia@example.com"",
      ""customer_phone"": ""+15134486530""
    }
  }
}");
            }

            if (url.Contains("booking?customer_id=CF-123", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(@"{
  ""request"": { ""status"": ""OK"" },
  ""booking/index"": {
    ""BR-1001"": {
      ""booking_id"": ""1001"",
      ""code"": ""BR-1001"",
      ""customer_name"": ""KATIA YAZMIN CAVAZOS LEAL"",
      ""status_id"": ""PAID"",
      ""status_name"": ""Confirmed"",
      ""status"": ""PAID""
    }
  }
}");
            }

            return JsonResponse(@"{ ""request"": { ""status"": ""OK"" } }");
        });

        var logger = new InMemoryLiveEmailLogger();
        var sut = new LiveEmailReadOnlyTestService(
            repo,
            service,
            logger,
            NullLogger<LiveEmailReadOnlyTestService>.Instance);

        var result = await sut.RunAsync(new LiveEmailReadOnlyTestRequest
        {
            Sender = "city-shuffles@bookings.checkfront.com",
            Subject = "Viator booking notification",
            HtmlBody = File.ReadAllText(FixturePath("checkfront_viator_notification_minimal.txt")),
            SelectedFile = "checkfront_viator_notification_minimal.txt",
            EmailTypeOverride = "Auto"
        }, CancellationToken.None);

        Assert.That(result.Vendor, Is.EqualTo("Viator"));
        Assert.That(result.EmailType, Is.EqualTo("Booking"));
        Assert.That(result.ExtractedFields.CustomerName, Is.EqualTo("KATIA YAZMIN CAVAZOS LEAL"));
        Assert.That(result.CheckfrontLookupSummary, Does.Contain("NameLookup"));
        Assert.That(result.WouldWrite.Count, Is.EqualTo(4));
        Assert.That(result.LogPreviewLines.Count, Is.GreaterThanOrEqualTo(6));
    }

    [Test]
    public async Task RunAsync_BookingScenario_ExtractsCoreFieldsAndBuildsWouldWrite()
    {
        var repo = new TestClassificationRepository(new[]
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

        var service = CreateCheckfrontApi(_ => JsonResponse(@"{ ""request"": { ""status"": ""FAIL"" } }"));
        var sut = new LiveEmailReadOnlyTestService(
            repo,
            service,
            new InMemoryLiveEmailLogger(),
            NullLogger<LiveEmailReadOnlyTestService>.Instance);

        var result = await sut.RunAsync(new LiveEmailReadOnlyTestRequest
        {
            Sender = "booking@t1.viator.com",
            Subject = "New Booking for Sun, Feb 22, 2026 (#BR-1361381631)",
            HtmlBody = File.ReadAllText(FixturePath("viator_booking_full_html.html")),
            SelectedFile = "viator_booking_full_html.html",
            EmailTypeOverride = "Auto"
        }, CancellationToken.None);

        Assert.That(result.EmailType, Is.EqualTo("Booking"));
        Assert.That(result.ExtractedFields.BookingCode, Is.EqualTo("BR-1361381631"));
        Assert.That(result.ExtractedFields.TourDate?.ToString("yyyy-MM-dd"), Is.EqualTo("2026-02-22"));
        Assert.That(result.ExtractedFields.CustomerPhone, Is.EqualTo("+15134486530"));
        Assert.That(result.ExtractedFields.NumberOfAttendees, Is.GreaterThan(0));
        Assert.That(result.CheckfrontComparison.HasApiMatch, Is.False);
        Assert.That(result.WouldWrite.Any(x => x.Entity == "Booking"), Is.True);
        Assert.That(result.WouldWrite.First(x => x.Entity == "Booking").Payload["BookingCode"]?.ToString(), Is.EqualTo("BR-1361381631"));
    }

    [Test]
    public async Task RunAsync_CancellationScenario_SetsCancellationAndPreviousBookingCode()
    {
        var repo = new TestClassificationRepository(new[]
        {
            new ClassificationRuleRecord
            {
                Id = 3,
                VendorName = "Viator",
                Domain = "t1.viator.com",
                SubjectPhrase = "Cancellation",
                EmailType = "Cancellation",
                IsActive = true,
                Priority = 500
            }
        });

        var service = CreateCheckfrontApi(_ => JsonResponse(@"{ ""request"": { ""status"": ""FAIL"" } }"));
        var sut = new LiveEmailReadOnlyTestService(
            repo,
            service,
            new InMemoryLiveEmailLogger(),
            NullLogger<LiveEmailReadOnlyTestService>.Instance);

        var result = await sut.RunAsync(new LiveEmailReadOnlyTestRequest
        {
            Sender = "booking@t1.viator.com",
            Subject = "Cancellation (BR-1361381631)",
            HtmlBody = "<div>Cancellation (BR-1361381631)</div><div>Lead Traveler Name: Christopher Knisely</div>",
            SelectedFile = "viator_cancellation_inline.txt",
            EmailTypeOverride = "Auto"
        }, CancellationToken.None);

        Assert.That(result.EmailType, Is.EqualTo("Cancellation"));
        Assert.That(result.ExtractedFields.IsCancellation, Is.True);
        Assert.That(result.ExtractedFields.PreviousBookingCode, Is.EqualTo("BR-1361381631"));
        Assert.That(result.WouldWrite.First(x => x.Entity == "Booking").Payload["BookingStatus"]?.ToString(), Is.EqualTo("Cancelled"));
    }

    [Test]
    public async Task RunAsync_ModificationScenario_WithSparseData_ReturnsWarnings()
    {
        var repo = new TestClassificationRepository(new[]
        {
            new ClassificationRuleRecord
            {
                Id = 4,
                VendorName = "Viator",
                Domain = "t1.viator.com",
                SubjectPhrase = "Booking Updated",
                EmailType = "Modification",
                IsActive = true,
                Priority = 500
            }
        });

        var service = CreateCheckfrontApi(_ => JsonResponse(@"{ ""request"": { ""status"": ""FAIL"" } }"));
        var sut = new LiveEmailReadOnlyTestService(
            repo,
            service,
            new InMemoryLiveEmailLogger(),
            NullLogger<LiveEmailReadOnlyTestService>.Instance);

        var result = await sut.RunAsync(new LiveEmailReadOnlyTestRequest
        {
            Sender = "booking@t1.viator.com",
            Subject = "Booking Updated",
            HtmlBody = "<div>Booking Updated</div><div>No structured fields present</div>",
            SelectedFile = "viator_modification_inline.txt",
            EmailTypeOverride = "Modification"
        }, CancellationToken.None);

        Assert.That(result.EmailType, Is.EqualTo("Modification"));
        Assert.That(result.Warnings.Count, Is.GreaterThan(0));
        Assert.That(result.FinalStatus, Is.EqualTo("completed-with-warnings"));
    }

    [Test]
    public async Task RunAsync_ConfirmationScenario_ProducesBookingEquivalentExtraction()
    {
        var repo = new TestClassificationRepository(new[]
        {
            new ClassificationRuleRecord
            {
                Id = 5,
                VendorName = "Viator",
                Domain = "t1.viator.com",
                SubjectPhrase = "New Booking for",
                EmailType = "Confirmation",
                IsActive = true,
                Priority = 500
            }
        });

        var service = CreateCheckfrontApi(_ => JsonResponse(@"{ ""request"": { ""status"": ""FAIL"" } }"));
        var sut = new LiveEmailReadOnlyTestService(
            repo,
            service,
            new InMemoryLiveEmailLogger(),
            NullLogger<LiveEmailReadOnlyTestService>.Instance);

        var result = await sut.RunAsync(new LiveEmailReadOnlyTestRequest
        {
            Sender = "booking@t1.viator.com",
            Subject = "New Booking for Sun, Feb 22, 2026 (#BR-1361381631)",
            HtmlBody = File.ReadAllText(FixturePath("viator_booking_full_html.html")),
            SelectedFile = "viator_booking_full_html.html",
            EmailTypeOverride = "Confirmation"
        }, CancellationToken.None);

        Assert.That(new[] { "Confirmation", "Booking" }, Does.Contain(result.EmailType));
        Assert.That(result.ExtractedFields.BookingCode, Is.EqualTo("BR-1361381631"));
        Assert.That(result.WouldWrite.First(x => x.Entity == "ProcessedEmail").Payload["EmailType"]?.ToString(), Is.Not.Empty);
    }

    [Test]
    public async Task JsonLineLogger_WritesRequiredFields()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "LiveEmailReadOnlyTestServiceTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var logPath = Path.Combine(tempDir, "checkfront-live-email.log");

        var env = new TestWebHostEnvironment
        {
            ContentRootPath = tempDir,
            WebRootPath = tempDir
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LiveEmailReadOnlyTest:LogPath"] = logPath
            })
            .Build();

        var repo = new TestClassificationRepository(new[]
        {
            new ClassificationRuleRecord
            {
                Id = 6,
                VendorName = "Viator",
                Domain = "t1.viator.com",
                SubjectPhrase = "New Booking for",
                EmailType = "Booking",
                IsActive = true,
                Priority = 500
            }
        });

        var service = CreateCheckfrontApi(_ => JsonResponse(@"{ ""request"": { ""status"": ""FAIL"" } }"));
        var jsonLogger = new LiveEmailReadOnlyJsonLineLogger(env, config);
        var sut = new LiveEmailReadOnlyTestService(
            repo,
            service,
            jsonLogger,
            NullLogger<LiveEmailReadOnlyTestService>.Instance);

        var result = await sut.RunAsync(new LiveEmailReadOnlyTestRequest
        {
            Sender = "booking@t1.viator.com",
            Subject = "New Booking for Sun, Feb 22, 2026 (#BR-1361381631)",
            HtmlBody = File.ReadAllText(FixturePath("viator_booking_full_html.html")),
            SelectedFile = "viator_booking_full_html.html",
            EmailTypeOverride = "Auto"
        }, CancellationToken.None);

        Assert.That(File.Exists(logPath), Is.True);
        var lines = File.ReadAllLines(logPath);
        Assert.That(lines.Length, Is.GreaterThanOrEqualTo(6));

        using var doc = JsonDocument.Parse(lines[0]);
        var root = doc.RootElement;
        Assert.That(root.GetProperty("runId").GetString(), Is.EqualTo(result.RunId));
        Assert.That(root.GetProperty("sender").GetString(), Is.EqualTo("booking@t1.viator.com"));
        Assert.That(root.GetProperty("operation").GetString(), Is.EqualTo("ParseResult"));
        Assert.That(root.TryGetProperty("payload", out _), Is.True);
    }

    private static ICheckfrontReadOnlyApi CreateCheckfrontApi(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Checkfront:ApiEndpoint"] = "https://example.checkfront.test/api/3.0/",
                ["Checkfront:ApiKey"] = "key",
                ["Checkfront:ApiSecret"] = "secret"
            })
            .Build();

        var httpClient = new HttpClient(new StubHttpMessageHandler(responder));
        var inner = new CheckfrontService(httpClient, config, NullLogger<CheckfrontService>.Instance);
        return new TestCheckfrontReadOnlyApi(inner);
    }

    private static HttpResponseMessage JsonResponse(string json)
        => new HttpResponseMessage(HttpStatusCode.OK)
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

    private sealed class TestCheckfrontReadOnlyApi : ICheckfrontReadOnlyApi
    {
        private readonly CheckfrontService _inner;

        public TestCheckfrontReadOnlyApi(CheckfrontService inner)
        {
            _inner = inner;
        }

        public string ActiveEndpoint => "https://example.checkfront.test/api/3.0/";

        public Task<CheckfrontConnectionTest> TestConnectionAsync()
            => _inner.TestConnectionAsync();

        public Task<CheckfrontV4BookingsListResponse> ListV4BookingsAsync(int limit = 25, int offset = 0)
            => Task.FromResult(new CheckfrontV4BookingsListResponse());

        public Task<CheckfrontBooking?> GetBookingByCodeOrIdAsync(string codeOrId)
            => _inner.GetBookingByCodeOrIdAsync(codeOrId);

        public Task<List<CheckfrontV4BookingNote>> GetBookingNotesByCodeOrIdAsync(string codeOrId)
            => Task.FromResult(new List<CheckfrontV4BookingNote>());

        public Task<List<CheckfrontBooking>> GetBookingsAsync(DateTime? startDate = null, DateTime? endDate = null, string? status = null, int? limit = null, int? page = null)
            => _inner.GetBookingsAsync(startDate, endDate, status, limit, page);

        public Task<List<CheckfrontCustomerMatch>> SearchCustomersByEmailAsync(string emailAddress)
            => _inner.SearchCustomersByEmailAsync(emailAddress);

        public Task<List<CheckfrontCustomerMatch>> SearchCustomersByNameAsync(string customerName)
            => _inner.SearchCustomersByNameAsync(customerName);

        public Task<List<CheckfrontBooking>> GetBookingsByCustomerIdAsync(string customerId, int limit = 25, int page = 1)
            => _inner.GetBookingsByCustomerIdAsync(customerId, limit, page);

        public Task<List<CheckfrontBooking>> GetBookingsByCustomerIdAsync(int customerId, int limit = 25, int page = 1)
            => _inner.GetBookingsByCustomerIdAsync(customerId, limit, page);
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

    private sealed class InMemoryLiveEmailLogger : ILiveEmailReadOnlyLogger
    {
        public string LogFilePath => "in-memory";
        public List<string> Lines { get; } = new();

        public Task<IReadOnlyList<string>> WriteEntriesAsync(IReadOnlyList<LiveEmailReadOnlyLogEntry> entries, CancellationToken cancellationToken)
        {
            var lines = entries.Select(entry => JsonSerializer.Serialize(entry)).ToList();
            Lines.AddRange(lines);
            return Task.FromResult<IReadOnlyList<string>>(lines);
        }
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Email.Test";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
