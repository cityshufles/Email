using System.Net;
using System.Net.Http;
using System.Text;
using Email.Models;
using Email.Services;
using Email.Services.GmailProcessing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Email.Test;

[TestFixture]
public class CheckfrontNotificationActivityResolverTests
{
    [Test]
    public void ExtractWalkerName_MinimalNotification_ReturnsFullName()
    {
        var input = "KATIA YAZMIN CAVAZOS LEAL viator/gyg booking";

        var result = CheckfrontNotificationActivityResolver.ExtractWalkerName(input);

        Assert.That(result, Is.EqualTo("KATIA YAZMIN CAVAZOS LEAL"));
    }

    [Test]
    public async Task ResolveByNameOrRecentActivity_UsesNameSearchAndCustomerBookings()
    {
        var requestLog = new List<string>();
        var service = CreateCheckfrontApi(req =>
        {
            var url = req.RequestUri?.ToString() ?? string.Empty;
            requestLog.Add(url);

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
    },
    ""BR-1002"": {
      ""booking_id"": ""1002"",
      ""code"": ""BR-1002"",
      ""customer_name"": ""KATIA YAZMIN CAVAZOS LEAL"",
      ""status_id"": ""CANCELLED"",
      ""status_name"": ""Cancelled"",
      ""status"": ""CANCELLED""
    },
    ""BR-1003"": {
      ""booking_id"": ""1003"",
      ""code"": ""BR-1003"",
      ""customer_name"": ""KATIA YAZMIN CAVAZOS LEAL"",
      ""status_id"": ""MODIFIED"",
      ""status_name"": ""Modified"",
      ""status"": ""MODIFIED""
    }
  }
}");
            }

            return JsonResponse(@"{ ""request"": { ""status"": ""OK"" } }");
        });

        var resolver = new CheckfrontNotificationActivityResolver(service, NullLogger.Instance);
        var activity = await resolver.ResolveByNameOrRecentActivityAsync("KATIA YAZMIN CAVAZOS LEAL");

        Assert.That(activity, Is.Not.Null);
        Assert.That(activity!.WalkerName, Is.EqualTo("KATIA YAZMIN CAVAZOS LEAL"));
        Assert.That(activity.CustomerMatchCount, Is.EqualTo(1));
        Assert.That(activity.MatchedBookingCount, Is.EqualTo(3));
        Assert.That(activity.BookingOrConfirmationCount, Is.EqualTo(1));
        Assert.That(activity.ModificationCount, Is.EqualTo(1));
        Assert.That(activity.CancellationCount, Is.EqualTo(1));
        Assert.That(requestLog.Any(u => u.Contains("customer?limit=100", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(requestLog.Any(u => u.Contains("booking?customer_id=CF-123", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task ResolveByNameOrRecentActivity_FallsBackToRecentBookingsWhenNoCustomerMatch()
    {
        var requestLog = new List<string>();
        var service = CreateCheckfrontApi(req =>
        {
            var url = req.RequestUri?.ToString() ?? string.Empty;
            requestLog.Add(url);

            if (url.Contains("customer?limit=100", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(@"{ ""request"": { ""status"": ""OK"" }, ""customers"": {} }");
            }

            if (url.Contains("booking?start_date=", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(@"{
  ""request"": { ""status"": ""OK"" },
  ""booking/index"": {
    ""BR-2001"": {
      ""booking_id"": ""2001"",
      ""code"": ""BR-2001"",
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

        var resolver = new CheckfrontNotificationActivityResolver(service, NullLogger.Instance);
        var activity = await resolver.ResolveByNameOrRecentActivityAsync("KATIA YAZMIN CAVAZOS LEAL");

        Assert.That(activity, Is.Not.Null);
        Assert.That(activity!.CustomerMatchCount, Is.EqualTo(0));
        Assert.That(activity.MatchedBookingCount, Is.EqualTo(1));
        Assert.That(activity.BookingOrConfirmationCount, Is.EqualTo(1));
        Assert.That(requestLog.Any(u => u.Contains("booking?start_date=", StringComparison.OrdinalIgnoreCase)), Is.True);
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
}
