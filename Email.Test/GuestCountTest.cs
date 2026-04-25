using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Email.Test;

[TestFixture]
public class GuestCountTest
{
    private static readonly string[] BookingCodes =
    {
        "YTVD-070325",
        "MDFY-110325",
        "LXKA-160325",
        "LAPB-170325",
        "GVXG-170325",
        "AZBZ-170325",
        "GNCR-170325",
        "DZAN-210325",
        "FLNY-210325",
        "FATM-220325",
        "QRSB-220325",
        "ZDSV-220325"
    };


    //[Explicit("Live production probe. Calls Checkfront production endpoints and writes detailed output to .NET console.")]
    [Test]
    public async Task ProbeProductionGuestEndpoints_AndReportAdultChildrenCounts()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile(ResolveEmailAppSettingsPath(), optional: false, reloadOnChange: false)
            .Build();

        var v4Base = NormalizeBaseUrl(config["Checkfront:V4:ApiEndpoint"]);
        //var v3Base = NormalizeBaseUrl(config["Checkfront:ProductionApiEndpoint"]);
        var apiKey = config["Checkfront:V4:ApiKey"] ?? string.Empty;
        var apiSecret = config["Checkfront:V4:ApiSecret"] ?? string.Empty;

        Assert.That(v4Base, Is.Not.Empty, "Missing Checkfront:V4:ApiEndpoint.");
        //Assert.That(v3Base, Is.Not.Empty, "Missing Checkfront:ProductionApiEndpoint.");
        Assert.That(apiKey, Is.Not.Empty, "Missing Checkfront:V4:ApiKey.");
        Assert.That(apiSecret, Is.Not.Empty, "Missing Checkfront:V4:ApiSecret.");

        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:{apiSecret}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        http.DefaultRequestHeaders.Accept.Clear();
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        //http.DefaultRequestHeaders.Add("X-On-Behalf", "1");
        //http.DefaultRequestHeaders.Add("X-Forwarded-For", "44.214.139.11");

        var results = new List<GuestProbeResult>();

        Console.WriteLine($"[GuestCountTest] {DateTime.UtcNow:O} Start BookingCount={BookingCodes.Length}");

        foreach (var bookingCode in BookingCodes)
        {
            var result = new GuestProbeResult { BookingCode = bookingCode };

            var guestsListUrl = $"{v4Base}bookings/{bookingCode}/guests/";
            var guestsList = await SendJsonAsync(http, guestsListUrl);
            result.GuestsListStatus = guestsList.StatusCode;

            Console.WriteLine(
                $"[GuestCountTest] {DateTime.UtcNow:O} Booking={bookingCode} Step=GuestsList HttpStatus={(int)guestsList.StatusCode}");

            if (guestsList.StatusCode == HttpStatusCode.OK)
            {
                var uuids = ExtractGuestUuids(guestsList.Body);
                result.GuestUuidsDiscovered = uuids.Count;

                foreach (var uuid in uuids)
                {
                    var detailUrl = $"{v4Base}bookings/{bookingCode}/guests/{uuid}";
                    var detail = await SendJsonAsync(http, detailUrl);
                    result.GuestDetailCalls++;
                    if (detail.StatusCode == HttpStatusCode.Forbidden)
                    {
                        result.GuestDetailForbiddenCount++;
                    }

                    Console.WriteLine(
                        $"[GuestCountTest] {DateTime.UtcNow:O} Booking={bookingCode} Step=GuestDetail Uuid={uuid} HttpStatus={(int)detail.StatusCode}");
                }
            }
            else
            {
                result.UsedFallback = true;
                //var bookingUrl = $"{v3Base}booking/{bookingCode}";
                //var bookingDetail = await SendJsonAsync(http, bookingUrl);
                //result.FallbackBookingStatus = bookingDetail.StatusCode;

                //Console.WriteLine(
                //    $"[GuestCountTest] {DateTime.UtcNow:O} Booking={bookingCode} Step=FallbackBooking HttpStatus={(int)bookingDetail.StatusCode}");

                //if (bookingDetail.StatusCode == HttpStatusCode.OK)
                //{
                //    var counts = ExtractAdultChildrenFromV3Booking(bookingDetail.Body);
                //    result.Adults = counts.Adults;
                //    result.Children = counts.Children;
                //    result.GuestsRaw = counts.GuestsRaw;

                //    // If booking only has "guest" qty, treat it as adults for reporting parity.
                //    if (result.Adults == 0 && result.Children == 0 && result.GuestsRaw > 0)
                //    {
                //        result.Adults = result.GuestsRaw;
                //    }
                //}
            }

            Console.WriteLine(
                $"[GuestCountTest] {DateTime.UtcNow:O} Booking={bookingCode} Result Adults={result.Adults} Children={result.Children} GuestsRaw={result.GuestsRaw} GuestsListStatus={(int)result.GuestsListStatus} UsedFallback={result.UsedFallback}");

            results.Add(result);
        }

        Console.WriteLine($"[GuestCountTest] {DateTime.UtcNow:O} Summary Start");
        foreach (var row in results)
        {
            Console.WriteLine(
                $"[GuestCountTest] Summary Booking={row.BookingCode} Adults={row.Adults} Children={row.Children} GuestsRaw={row.GuestsRaw} GuestsListStatus={(int)row.GuestsListStatus} FallbackBookingStatus={(int)row.FallbackBookingStatus} UsedFallback={row.UsedFallback}");
        }
        Console.WriteLine($"[GuestCountTest] {DateTime.UtcNow:O} Completed");

        Assert.That(results.Count, Is.EqualTo(BookingCodes.Length));
    }

    [Test]
    [Explicit("Live production v4 smoke test. Verifies at least one booking can be retrieved from v4 booking routes.")]
    public async Task ProbeProductionV4BookingEndpoints_CanFetchAtLeastOneBooking()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile(ResolveEmailAppSettingsPath(), optional: false, reloadOnChange: false)
            .Build();

        var v4Base = NormalizeBaseUrl(config["Checkfront:V4:ApiEndpoint"]);
        var apiKey = config["Checkfront:V4:ApiKey"] ?? string.Empty;
        var apiSecret = config["Checkfront:V4:ApiSecret"] ?? string.Empty;

        Assert.That(v4Base, Is.Not.Empty, "Missing Checkfront:V4:ApiEndpoint.");
        Assert.That(apiKey, Is.Not.Empty, "Missing Checkfront:V4:ApiKey.");
        Assert.That(apiSecret, Is.Not.Empty, "Missing Checkfront:V4:ApiSecret.");

        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:{apiSecret}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        http.DefaultRequestHeaders.Accept.Clear();
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        http.DefaultRequestHeaders.Add("X-On-Behalf", "1");
        //http.DefaultRequestHeaders.Add("X-Forwarded-For", "44.214.139.11");

        var foundCodes = new List<string>();
        Console.WriteLine($"[GuestCountTest] {DateTime.UtcNow:O} Start v4 booking fetch probe");

        foreach (var bookingCode in BookingCodes)
        {
            var bookingUrl = $"{v4Base}bookings/{bookingCode}";
            var detail = await SendJsonAsync(http, bookingUrl);
            Console.WriteLine(
                $"[GuestCountTest] {DateTime.UtcNow:O} Booking={bookingCode} Step=V4BookingDetail HttpStatus={(int)detail.StatusCode}");

            if (detail.StatusCode != HttpStatusCode.OK)
            {
                continue;
            }

            var resolvedCode = ExtractV4BookingDetailCode(detail.Body);
            var finalCode = string.IsNullOrWhiteSpace(resolvedCode) ? bookingCode : resolvedCode;
            foundCodes.Add(finalCode);

            Console.WriteLine(
                $"[GuestCountTest] {DateTime.UtcNow:O} Booking={bookingCode} Step=V4BookingDetail ParsedCode={finalCode}");
        }

        if (foundCodes.Count == 0)
        {
            var listUrl = $"{v4Base}bookings?limit=1&offset=0";
            var listResponse = await SendJsonAsync(http, listUrl);
            Console.WriteLine(
                $"[GuestCountTest] {DateTime.UtcNow:O} Step=V4BookingsList HttpStatus={(int)listResponse.StatusCode}");

            if (listResponse.StatusCode == HttpStatusCode.OK)
            {
                var firstCode = ExtractV4FirstBookingCodeFromList(listResponse.Body);
                if (!string.IsNullOrWhiteSpace(firstCode))
                {
                    foundCodes.Add(firstCode);
                    Console.WriteLine(
                        $"[GuestCountTest] {DateTime.UtcNow:O} Step=V4BookingsList ParsedFirstCode={firstCode}");
                }
            }
        }

        Console.WriteLine($"[GuestCountTest] {DateTime.UtcNow:O} End v4 booking fetch probe Found={foundCodes.Count}");
        Assert.That(foundCodes.Count, Is.GreaterThan(0), "Unable to fetch any booking from v4 detail/list endpoints.");
    }

    private static async Task<(HttpStatusCode StatusCode, string Body)> SendJsonAsync(HttpClient http, string url)
    {
        using var response = await http.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, body);
    }

    private static string ExtractV4BookingDetailCode(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        return FirstString(data, "code", "id");
    }

    private static string ExtractV4FirstBookingCodeFromList(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var code = FirstString(item, "code", "id");
            if (!string.IsNullOrWhiteSpace(code))
            {
                return code;
            }
        }

        return string.Empty;
    }

    private static List<string> ExtractGuestUuids(string json)
    {
        var uuids = new List<string>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return uuids;
        }

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return uuids;
        }

        foreach (var item in data.EnumerateArray())
        {
            var candidate = FirstString(item, "uuid", "id", "guestUuid", "guest_id");
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                uuids.Add(candidate);

            }
        }

        return uuids;
    }

    private static (int Adults, int Children, int GuestsRaw) ExtractAdultChildrenFromV3Booking(string json)
    {
        var adults = 0;
        var children = 0;
        var guestsRaw = 0;

        if (string.IsNullOrWhiteSpace(json))
        {
            return (adults, children, guestsRaw);
        }

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("booking", out var booking) ||
            !booking.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Object)
        {
            return (adults, children, guestsRaw);
        }

        foreach (var item in items.EnumerateObject())
        {
            if (!item.Value.TryGetProperty("param", out var param) || param.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var entry in param.EnumerateObject())
            {
                var key = entry.Name ?? string.Empty;
                var qty = 0;

                if (entry.Value.ValueKind == JsonValueKind.Object &&
                    entry.Value.TryGetProperty("qty", out var qtyElement))
                {
                    qty = ParseIntFromElement(qtyElement);
                }

                if (key.IndexOf("adult", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    adults += qty;
                }
                else if (key.IndexOf("child", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    children += qty;
                    Console.WriteLine(children + "child count");
                }
                else if (key.IndexOf("guest", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    guestsRaw += qty;
                }
            }
        }

        return (adults, children, guestsRaw);
    }

    private static int ParseIntFromElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var n))
        {
            return n;
        }

        if (element.ValueKind == JsonValueKind.String &&
            int.TryParse(element.GetString(), out var parsed))
        {
            return parsed;
        }

        return 0;
    }

    private static string FirstString(JsonElement element, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!element.TryGetProperty(key, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text.Trim();
                }
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            {
                return number.ToString();
            }
        }

        return string.Empty;
    }

    private static string NormalizeBaseUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.EndsWith("/", StringComparison.Ordinal) ? trimmed : trimmed + "/";
    }

    private static string ResolveEmailAppSettingsPath()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Email", "appsettings.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate Email/appsettings.json from test directory traversal.");
    }

    private sealed class GuestProbeResult
    {
        public string BookingCode { get; set; } = string.Empty;
        public HttpStatusCode GuestsListStatus { get; set; }
        public int GuestUuidsDiscovered { get; set; }
        public int GuestDetailCalls { get; set; }
        public int GuestDetailForbiddenCount { get; set; }
        public bool UsedFallback { get; set; }
        public HttpStatusCode FallbackBookingStatus { get; set; }
        public int Adults { get; set; }
        public int Children { get; set; }
        public int GuestsRaw { get; set; }
    }
}
