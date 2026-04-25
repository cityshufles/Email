using System.Net;
using System.Net.Http;
using System.Text;
using Email.Models;
using Email.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Email.Test;

[TestFixture]
public class CheckfrontReadOnlyV4ApiClientTests
{
    [Test]
    public async Task TestConnectionAsync_UsesV3CompatibleEndpointWhenConfigured()
    {
        var requestUris = new List<string>();
        var client = CreateClient(req =>
        {
            var url = req.RequestUri?.ToString() ?? string.Empty;
            requestUris.Add(url);

            if (url.EndsWith("/ping", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(@"{ ""request"": { ""status"": ""OK"" } }");
            }

            if (url.EndsWith("/company", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(@"{
  ""request"": { ""status"": ""OK"" },
  ""company"": { ""name"": ""CityShuffles"" }
}");
            }

            return JsonResponse(@"{ ""request"": { ""status"": ""OK"" } }");
        });

        var result = await client.TestConnectionAsync();

        Assert.That(result.IsConnected, Is.True);
        Assert.That(client.ActiveEndpoint, Is.EqualTo("https://example.checkfront.test/api/3.0/"));
        Assert.That(requestUris.Count, Is.GreaterThanOrEqualTo(2));
        Assert.That(requestUris.All(u => u.StartsWith("https://example.checkfront.test/api/3.0/", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task TestConnectionAsync_DerivesV3CompatibilityEndpointFromV4WhenNotConfigured()
    {
        var requestUris = new List<string>();
        var client = CreateClient(
            req =>
            {
                var url = req.RequestUri?.ToString() ?? string.Empty;
                requestUris.Add(url);

                if (url.EndsWith("/ping", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonResponse(@"{ ""request"": { ""status"": ""OK"" } }");
                }

                if (url.EndsWith("/company", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonResponse(@"{ ""request"": { ""status"": ""OK"" }, ""company"": { ""name"": ""CityShuffles"" } }");
                }

                return JsonResponse(@"{ ""request"": { ""status"": ""OK"" } }");
            },
            includeV3Endpoint: false);

        var result = await client.TestConnectionAsync();

        Assert.That(result.IsConnected, Is.True);
        Assert.That(client.ActiveEndpoint, Is.EqualTo("https://example.checkfront.test/api/3.0/"));
        Assert.That(requestUris.All(u => u.StartsWith("https://example.checkfront.test/api/3.0/", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task GetBookingByCodeOrIdAsync_UsesV3CompatibleBookingRoute()
    {
        var requestUris = new List<string>();
        var client = CreateClient(req =>
        {
            var url = req.RequestUri?.ToString() ?? string.Empty;
            requestUris.Add(url);

            if (url.Contains("/booking/", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(@"{ ""request"": { ""status"": ""OK"" }, ""booking"": { ""booking_id"": 1, ""code"": ""BR-1"", ""status_id"": ""PAID"" } }");
            }

            return JsonResponse(@"{ ""request"": { ""status"": ""OK"" }, ""bookings"": {} }");
        }, includeV3Endpoint: false);

        var booking = await client.GetBookingByCodeOrIdAsync("BR-1");

        Assert.That(booking, Is.Not.Null);
        Assert.That(booking!.Code, Is.EqualTo("BR-1"));
        Assert.That(requestUris.Any(u => u.Contains("/api/3.0/booking/BR-1", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(requestUris.Any(u => u.Contains("/api/4.0/booking/BR-1", StringComparison.OrdinalIgnoreCase)), Is.False);
    }

    [Test]
    public async Task GetBookingByCodeOrIdAsync_WhenV3MissingTime_FallsBackToV4DetailForStartTime()
    {
        var requestUris = new List<string>();
        var client = CreateClient(req =>
        {
            var url = req.RequestUri?.ToString() ?? string.Empty;
            requestUris.Add(url);

            if (url.Contains("/api/3.0/booking/BR-2", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(@"{
  ""request"": { ""status"": ""OK"" },
  ""booking"": {
    ""booking_id"": 2,
    ""code"": ""BR-2"",
    ""date_desc"": ""2026-04-10"",
    ""item_name"": ""Brooklyn Bridge Tour""
  }
}");
            }

            if (url.Contains("/api/3.0/booking?code=BR-2", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(@"{ ""request"": { ""status"": ""OK"" }, ""booking/index"": {} }");
            }

            if (url.Contains("/api/4.0/bookings/BR-2", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(@"{
  ""data"": {
    ""id"": ""2"",
    ""code"": ""BR-2"",
    ""start"": ""2026-04-10T09:30:00-04:00"",
    ""itemSummary"": ""Brooklyn Bridge Tour"",
    ""statusId"": ""PAID"",
    ""firstName"": ""Taylor"",
    ""lastName"": ""Walker"",
    ""email"": ""taylor.walker@example.com"",
    ""total"": 2900,
    ""taxTotal"": 0,
    ""paidTotal"": 2900,
    ""customerId"": 456
  }
}");
            }

            return JsonResponse(@"{ ""request"": { ""status"": ""OK"" } }");
        }, includeV3Endpoint: false);

        var booking = await client.GetBookingByCodeOrIdAsync("BR-2");

        Assert.That(booking, Is.Not.Null);
        Assert.That(booking!.StartDateRaw, Is.EqualTo("2026-04-10"));
        Assert.That(booking.StartTimeRaw, Is.EqualTo("09:30"));
        Assert.That(requestUris.Any(u => u.Contains("/api/3.0/booking/BR-2", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(requestUris.Any(u => u.Contains("/api/4.0/bookings/BR-2", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task TestConnectionAsync_UsesV4WhenEndpointCannotDeriveV3()
    {
        var requestUris = new List<string>();
        var client = CreateClient(
            req =>
            {
                var url = req.RequestUri?.ToString() ?? string.Empty;
                requestUris.Add(url);

                if (url.EndsWith("/ping", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonResponse(@"{ ""request"": { ""status"": ""OK"" } }");
                }

                if (url.EndsWith("/company", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonResponse(@"{ ""request"": { ""status"": ""OK"" }, ""company"": { ""name"": ""CityShuffles"" } }");
                }

                return JsonResponse(@"{ ""request"": { ""status"": ""OK"" } }");
            },
            includeV3Endpoint: false,
            v4Endpoint: "https://example.checkfront.test/v4/");

        var result = await client.TestConnectionAsync();

        Assert.That(result.IsConnected, Is.True);
        Assert.That(client.ActiveEndpoint, Is.EqualTo("https://example.checkfront.test/v4/"));
        Assert.That(requestUris.All(u => u.StartsWith("https://example.checkfront.test/v4/", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public void Ctor_WithoutV4Endpoint_Throws()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Checkfront:V4:ApiKey"] = "key",
                ["Checkfront:V4:ApiSecret"] = "secret"
            })
            .Build();

        var httpClient = new HttpClient(new StubHttpMessageHandler(_ => JsonResponse(@"{ ""request"": { ""status"": ""OK"" } }")));

        Assert.Throws<InvalidOperationException>(() =>
            new CheckfrontReadOnlyV4ApiClient(
                httpClient,
                new TestHttpClientFactory(httpClient),
                config,
                NullLoggerFactory.Instance,
                NullLogger<CheckfrontReadOnlyV4ApiClient>.Instance));
    }

    [Test]
    public async Task ListV4BookingsAsync_UsesV4BookingsRoute_AndParsesFirstObject()
    {
        var requestUris = new List<string>();
        var client = CreateClient(req =>
        {
            var url = req.RequestUri?.ToString() ?? string.Empty;
            requestUris.Add(url);
            if (url.Contains("/api/4.0/bookings?", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(@"{
  ""data"": [
    {
      ""id"": 372,
      ""code"": ""AXRR-160326"",
      ""created"": ""2026-03-16T21:29:33-04:00"",
      ""start"": ""2026-03-19T09:30:00-04:00"",
      ""end"": ""2026-03-19T12:30:00-04:00"",
      ""checkIn"": null,
      ""checkOut"": null,
      ""customerId"": 295399,
      ""firstName"": ""Wendy"",
      ""lastName"": ""Oddie"",
      ""email"": ""wendy@example.com"",
      ""language"": ""en_US"",
      ""subTotal"": 2900,
      ""inclusiveTaxTotal"": 0,
      ""taxTotal"": 0,
      ""total"": 2900,
      ""paidTotal"": 2900,
      ""statusId"": ""PAID"",
      ""accountId"": 499,
      ""partnerId"": null,
      ""itemSummary"": ""Brooklyn Bridge, Brooklyn Heights, Dumbo Tour"",
      ""fields"": {
        ""customer_name"": ""Wendy Oddie"",
        ""customer_phone"": ""+14035190766""
      },
      ""discountCode"": null
    }
  ]
}");
            }

            return JsonResponse(@"{ ""request"": { ""status"": ""OK"" } }");
        });

        var page = await client.ListV4BookingsAsync(limit: 25, offset: 0);

        Assert.That(page, Is.Not.Null);
        Assert.That(page.Data.Count, Is.EqualTo(1));
        Assert.That(page.Data[0].Id, Is.EqualTo("372"));
        Assert.That(page.Data[0].Code, Is.EqualTo("AXRR-160326"));
        Assert.That(page.Data[0].CustomerId, Is.EqualTo(295399));
        Assert.That(page.Data[0].Fields.TryGetValue("customer_phone", out var phoneField), Is.True);
        Assert.That(phoneField.ToString(), Is.EqualTo("+14035190766"));
        Assert.That(requestUris.Any(u => u.Contains("/api/4.0/bookings?", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(requestUris.Any(u => u.Contains("/api/3.0/bookings?", StringComparison.OrdinalIgnoreCase)), Is.False);
    }

    private static CheckfrontReadOnlyV4ApiClient CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        bool includeV3Endpoint = true,
        string v4Endpoint = "https://example.checkfront.test/api/4.0/")
    {
        var values = new Dictionary<string, string?>
            {
                ["Checkfront:V4:ApiEndpoint"] = v4Endpoint,
                ["Checkfront:V4:ApiKey"] = "v4-key",
                ["Checkfront:V4:ApiSecret"] = "v4-secret",
                ["Checkfront:ApiKey"] = "v3-key",
                ["Checkfront:ApiSecret"] = "v3-secret"
            };

        if (includeV3Endpoint)
        {
            values["Checkfront:ApiEndpoint"] = "https://example.checkfront.test/api/3.0/";
            values["Checkfront:ProductionApiEndpoint"] = "https://example.checkfront.test/api/3.0/";
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var httpClient = new HttpClient(new StubHttpMessageHandler(responder));
        return new CheckfrontReadOnlyV4ApiClient(
            httpClient,
            new TestHttpClientFactory(httpClient),
            config,
            NullLoggerFactory.Instance,
            NullLogger<CheckfrontReadOnlyV4ApiClient>.Instance);
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

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public TestHttpClientFactory(HttpClient client)
        {
            _client = client;
        }

        public HttpClient CreateClient(string name)
            => _client;
    }
}
