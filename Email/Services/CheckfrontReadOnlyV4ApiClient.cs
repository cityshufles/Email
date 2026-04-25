using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Email.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Email.Services
{
    /// <summary>
    /// Production read-only API wrapper for Checkfront test flows.
    /// Uses v4 credentials and prefers a route-compatible v3 endpoint when needed.
    /// </summary>
    public sealed class CheckfrontReadOnlyV4ApiClient : ICheckfrontReadOnlyApi
    {
        private static readonly JsonSerializerOptions V4JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        private readonly ILogger<CheckfrontReadOnlyV4ApiClient> _logger;
        private readonly CheckfrontService _routeCompatibleClient;
        private readonly HttpClient _v4HttpClient;
        private readonly string _v4Endpoint;
        private readonly string _v4ApiKey;
        private readonly string _v4ApiSecret;

        public string ActiveEndpoint { get; }

        public CheckfrontReadOnlyV4ApiClient(
            HttpClient httpClient,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILoggerFactory loggerFactory,
            ILogger<CheckfrontReadOnlyV4ApiClient> logger)
        {
            _logger = logger;

            var boundConfig = new CheckfrontConfig();
            configuration.GetSection("Checkfront").Bind(boundConfig);

            var v4Endpoint = NormalizeEndpoint(boundConfig.V4?.ApiEndpoint);
            if (string.IsNullOrWhiteSpace(v4Endpoint))
            {
                throw new InvalidOperationException("Checkfront:V4:ApiEndpoint is required for the read-only test client.");
            }

            var apiKey = FirstNonEmpty(boundConfig.V4?.ApiKey, boundConfig.ApiKey);
            var apiSecret = FirstNonEmpty(boundConfig.V4?.ApiSecret, boundConfig.ApiSecret);
            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
            {
                throw new InvalidOperationException("Checkfront V4 credentials are required for the read-only test client.");
            }

            _v4HttpClient = httpClient;
            _v4Endpoint = v4Endpoint;
            _v4ApiKey = apiKey;
            _v4ApiSecret = apiSecret;
            ConfigureV4HttpClient();

            var v4Config = new Dictionary<string, string?>
            {
                ["Checkfront:ApiEndpoint"] = v4Endpoint,
                ["Checkfront:ProductionApiEndpoint"] = v4Endpoint,
                ["Checkfront:DevelopmentApiEndpoint"] = string.Empty,
                ["Checkfront:DefaultEndpointMode"] = "Production",
                ["Checkfront:ApiKey"] = apiKey,
                ["Checkfront:ApiSecret"] = apiSecret
            };

            var mergedConfiguration = new ConfigurationBuilder()
                .AddConfiguration(configuration)
                .AddInMemoryCollection(v4Config)
                .Build();

            var v4Client = new CheckfrontService(
                httpClient,
                mergedConfiguration,
                loggerFactory.CreateLogger<CheckfrontService>());

            var configuredV3Endpoint = NormalizeEndpoint(FirstNonEmpty(boundConfig.ProductionApiEndpoint, boundConfig.ApiEndpoint));
            var derivedV3Endpoint = DeriveV3Endpoint(v4Endpoint);
            var productionV3Endpoint = !string.IsNullOrWhiteSpace(configuredV3Endpoint) &&
                                       !string.Equals(configuredV3Endpoint, v4Endpoint, StringComparison.OrdinalIgnoreCase)
                ? configuredV3Endpoint
                : derivedV3Endpoint;

            if (!string.IsNullOrWhiteSpace(productionV3Endpoint) &&
                !string.Equals(productionV3Endpoint, v4Endpoint, StringComparison.OrdinalIgnoreCase))
            {
                var v3Config = new Dictionary<string, string?>
                {
                    ["Checkfront:ApiEndpoint"] = productionV3Endpoint,
                    ["Checkfront:ProductionApiEndpoint"] = productionV3Endpoint,
                    ["Checkfront:DevelopmentApiEndpoint"] = string.Empty,
                    ["Checkfront:DefaultEndpointMode"] = "Production",
                    ["Checkfront:ApiKey"] = apiKey,
                    ["Checkfront:ApiSecret"] = apiSecret
                };

                var v3MergedConfiguration = new ConfigurationBuilder()
                    .AddConfiguration(configuration)
                    .AddInMemoryCollection(v3Config)
                    .Build();

                _routeCompatibleClient = new CheckfrontService(
                    httpClientFactory.CreateClient(),
                    v3MergedConfiguration,
                    loggerFactory.CreateLogger<CheckfrontService>());
                ActiveEndpoint = productionV3Endpoint;
                return;
            }

            _routeCompatibleClient = v4Client;
            ActiveEndpoint = v4Endpoint;
        }

        public Task<CheckfrontConnectionTest> TestConnectionAsync()
            => _routeCompatibleClient.TestConnectionAsync();

        public async Task<CheckfrontV4BookingsListResponse> ListV4BookingsAsync(int limit = 25, int offset = 0)
        {
            var safeLimit = Math.Clamp(limit <= 0 ? 25 : limit, 1, 200);
            var safeOffset = Math.Max(offset, 0);
            var operationId = Guid.NewGuid().ToString("N");
            var endpoint = $"bookings?limit={safeLimit}&offset={safeOffset}";
            var requestUri = new Uri(new Uri(_v4Endpoint, UriKind.Absolute), endpoint);

            WriteConsole(
                $"Operation=ListV4Bookings OperationId={operationId} Start Endpoint={requestUri}");

            try
            {
                ApplyV4AuthorizationHeader();
                using var response = await _v4HttpClient.GetAsync(requestUri);
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Checkfront v4 bookings list failed. OperationId={OperationId} Endpoint={Endpoint} HttpStatus={HttpStatus} Reason={Reason} Body={Body}",
                        operationId,
                        requestUri,
                        (int)response.StatusCode,
                        response.ReasonPhrase ?? string.Empty,
                        body ?? string.Empty);
                    WriteConsole(
                        $"Operation=ListV4Bookings OperationId={operationId} Error HttpStatus={(int)response.StatusCode} Reason={response.ReasonPhrase ?? string.Empty} Body={body ?? string.Empty}");
                    return new CheckfrontV4BookingsListResponse();
                }

                var parsed = new CheckfrontV4BookingsListResponse();
                using (var doc = JsonDocument.Parse(body))
                {
                    if (doc.RootElement.TryGetProperty("data", out var dataElement) &&
                        dataElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var bookingElement in dataElement.EnumerateArray())
                        {
                            var raw = bookingElement.GetRawText();
                            var booking = JsonSerializer.Deserialize<CheckfrontV4Booking>(raw, V4JsonOptions);
                            if (booking == null)
                            {
                                continue;
                            }

                            booking.RawJson = raw;
                            parsed.Data.Add(booking);
                        }
                    }
                }
                var first = parsed.Data.Count > 0 ? parsed.Data[0] : null;

                WriteConsole(
                    $"Operation=ListV4Bookings OperationId={operationId} End Count={parsed.Data.Count} FirstCode={first?.Code ?? string.Empty} FirstId={first?.Id ?? string.Empty}");

                return parsed;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Checkfront v4 bookings list threw. OperationId={OperationId} Endpoint={Endpoint}",
                    operationId,
                    requestUri);
                WriteConsole(
                    $"Operation=ListV4Bookings OperationId={operationId} Exception={ex.Message}");
                return new CheckfrontV4BookingsListResponse();
            }
        }

        public async Task<CheckfrontBooking?> GetBookingByCodeOrIdAsync(string codeOrId)
        {
            var routeBooking = await _routeCompatibleClient.GetBookingByCodeOrIdAsync(codeOrId);
            if (!NeedsV4TimeFallback(routeBooking))
            {
                return routeBooking;
            }

            var v4Detail = await TryGetV4BookingDetailAsync(codeOrId);
            if (v4Detail == null)
            {
                return routeBooking;
            }

            var v4Legacy = MapV4BookingToLegacy(v4Detail);
            if (routeBooking == null)
            {
                return v4Legacy;
            }

            MergeMissingFromV4(routeBooking, v4Legacy);
            return routeBooking;
        }

        public async Task<List<CheckfrontV4BookingNote>> GetBookingNotesByCodeOrIdAsync(string codeOrId)
        {
            if (string.IsNullOrWhiteSpace(codeOrId))
            {
                return new List<CheckfrontV4BookingNote>();
            }

            var safeCode = Uri.EscapeDataString(codeOrId.Trim());
            var requestUri = new Uri(new Uri(_v4Endpoint, UriKind.Absolute), $"bookings/{safeCode}/notes/");
            var operationId = Guid.NewGuid().ToString("N");

            WriteConsole(
                $"Operation=GetV4BookingNotes OperationId={operationId} Start Endpoint={requestUri}");

            try
            {
                ApplyV4AuthorizationHeader();
                using var response = await _v4HttpClient.GetAsync(requestUri);
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Checkfront v4 booking notes failed. OperationId={OperationId} Endpoint={Endpoint} HttpStatus={HttpStatus} Body={Body}",
                        operationId,
                        requestUri,
                        (int)response.StatusCode,
                        body ?? string.Empty);
                    WriteConsole(
                        $"Operation=GetV4BookingNotes OperationId={operationId} Error HttpStatus={(int)response.StatusCode} Endpoint={requestUri} Body={body ?? string.Empty}");
                    return new List<CheckfrontV4BookingNote>();
                }

                var parsedNotes = ParseV4BookingNotes(body);
                WriteConsole(
                    $"Operation=GetV4BookingNotes OperationId={operationId} End Count={parsedNotes.Count} Endpoint={requestUri}");
                return parsedNotes;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Checkfront v4 booking notes exception. OperationId={OperationId} Endpoint={Endpoint}",
                    operationId,
                    requestUri);
                WriteConsole(
                    $"Operation=GetV4BookingNotes OperationId={operationId} Exception={ex.Message} Endpoint={requestUri}");
                return new List<CheckfrontV4BookingNote>();
            }
        }

        public Task<List<CheckfrontBooking>> GetBookingsAsync(
            DateTime? startDate = null,
            DateTime? endDate = null,
            string? status = null,
            int? limit = null,
            int? page = null)
            => _routeCompatibleClient.GetBookingsAsync(startDate, endDate, status, limit, page);

        public Task<List<CheckfrontCustomerMatch>> SearchCustomersByEmailAsync(string emailAddress)
            => _routeCompatibleClient.SearchCustomersByEmailAsync(emailAddress);

        public Task<List<CheckfrontCustomerMatch>> SearchCustomersByNameAsync(string customerName)
            => _routeCompatibleClient.SearchCustomersByNameAsync(customerName);

        public Task<List<CheckfrontBooking>> GetBookingsByCustomerIdAsync(string customerId, int limit = 25, int page = 1)
            => _routeCompatibleClient.GetBookingsByCustomerIdAsync(customerId, limit, page);

        public Task<List<CheckfrontBooking>> GetBookingsByCustomerIdAsync(int customerId, int limit = 25, int page = 1)
            => _routeCompatibleClient.GetBookingsByCustomerIdAsync(customerId, limit, page);

        private async Task<CheckfrontV4Booking?> TryGetV4BookingDetailAsync(string codeOrId)
        {
            if (string.IsNullOrWhiteSpace(codeOrId))
            {
                return null;
            }

            var safeCode = Uri.EscapeDataString(codeOrId.Trim());
            var requestUri = new Uri(new Uri(_v4Endpoint, UriKind.Absolute), $"bookings/{safeCode}");
            var operationId = Guid.NewGuid().ToString("N");

            try
            {
                ApplyV4AuthorizationHeader();
                using var response = await _v4HttpClient.GetAsync(requestUri);
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Checkfront v4 booking detail failed. OperationId={OperationId} Endpoint={Endpoint} HttpStatus={HttpStatus} Body={Body}",
                        operationId,
                        requestUri,
                        (int)response.StatusCode,
                        body ?? string.Empty);
                    WriteConsole(
                        $"Operation=GetV4BookingDetail OperationId={operationId} Error HttpStatus={(int)response.StatusCode} Endpoint={requestUri} Body={body ?? string.Empty}");
                    return null;
                }

                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("data", out var dataElement) ||
                    dataElement.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                var raw = dataElement.GetRawText();
                var parsed = JsonSerializer.Deserialize<CheckfrontV4Booking>(raw, V4JsonOptions);
                if (parsed != null)
                {
                    parsed.RawJson = raw;
                }
                return parsed;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Checkfront v4 booking detail exception. OperationId={OperationId} Endpoint={Endpoint}",
                    operationId,
                    requestUri);
                WriteConsole(
                    $"Operation=GetV4BookingDetail OperationId={operationId} Exception={ex.Message} Endpoint={requestUri}");
                return null;
            }
        }

        private static List<CheckfrontV4BookingNote> ParseV4BookingNotes(string? responseBody)
        {
            var notes = new List<CheckfrontV4BookingNote>();
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return notes;
            }

            using var doc = JsonDocument.Parse(responseBody);
            if (!doc.RootElement.TryGetProperty("data", out var dataElement))
            {
                return notes;
            }

            if (dataElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var noteElement in dataElement.EnumerateArray())
                {
                    var parsedNote = JsonSerializer.Deserialize<CheckfrontV4BookingNote>(noteElement.GetRawText(), V4JsonOptions);
                    if (parsedNote != null)
                    {
                        notes.Add(parsedNote);
                    }
                }

                return notes;
            }

            if (dataElement.ValueKind == JsonValueKind.Object)
            {
                var parsedNote = JsonSerializer.Deserialize<CheckfrontV4BookingNote>(dataElement.GetRawText(), V4JsonOptions);
                if (parsedNote != null)
                {
                    notes.Add(parsedNote);
                }
            }

            return notes;
        }

        private static bool NeedsV4TimeFallback(CheckfrontBooking? booking)
        {
            if (booking == null)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(booking.StartTimeRaw))
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(booking.DateDescription) ||
                   booking.DateDescription.IndexOf(':') < 0;
        }

        private static CheckfrontBooking MapV4BookingToLegacy(CheckfrontV4Booking source)
        {
            var bookingCode = FirstNonEmpty(source.Code, source.Id);
            var customerName = BuildCustomerName(source.FirstName, source.LastName);
            var customerEmail = FirstNonEmpty(source.Email, GetFieldValue(source.Fields, "customer_email"));
            var customerPhone = GetFieldValue(source.Fields, "customer_phone");
            var tourName = FirstNonEmpty(source.ItemSummary, GetFieldValue(source.Fields, "item_summary"));

            var mapped = new CheckfrontBooking
            {
                BookingId = TryParseInt(source.Id),
                Code = bookingCode,
                BookingReference = source.Id ?? string.Empty,
                StatusId = source.StatusId ?? string.Empty,
                StatusName = source.Status?.Name ?? string.Empty,
                Status = source.Status?.Name ?? source.StatusId ?? string.Empty,
                CreatedDateTimestamp = TryParseUnixTimestamp(source.Created),
                CustomerName = customerName,
                CustomerEmail = customerEmail,
                CustomerPhone = customerPhone,
                CustomerId = source.CustomerId,
                Summary = tourName,
                ItemName = tourName,
                ItemTitle = tourName,
                Total = source.Total.ToString(CultureInfo.InvariantCulture),
                TaxTotal = source.TaxTotal.ToString(CultureInfo.InvariantCulture),
                PaidTotal = source.PaidTotal.ToString(CultureInfo.InvariantCulture)
            };

            HydrateLegacyTimeFields(mapped, FirstNonEmpty(source.Start, source.CheckIn));
            return mapped;
        }

        private static void MergeMissingFromV4(CheckfrontBooking target, CheckfrontBooking fallback)
        {
            target.Code = FirstNonEmpty(target.Code, fallback.Code);
            target.BookingReference = FirstNonEmpty(target.BookingReference, fallback.BookingReference);
            target.CustomerName = FirstNonEmpty(target.CustomerName, fallback.CustomerName);
            target.CustomerEmail = FirstNonEmpty(target.CustomerEmail, fallback.CustomerEmail);
            target.CustomerPhone = FirstNonEmpty(target.CustomerPhone, fallback.CustomerPhone);
            target.Summary = FirstNonEmpty(target.Summary, fallback.Summary);
            target.ItemName = FirstNonEmpty(target.ItemName, fallback.ItemName);
            target.ItemTitle = FirstNonEmpty(target.ItemTitle, fallback.ItemTitle);
            target.StartDateRaw = FirstNonEmpty(target.StartDateRaw, fallback.StartDateRaw);
            target.StartTimeRaw = FirstNonEmpty(target.StartTimeRaw, fallback.StartTimeRaw);
            target.DateDescription = FirstNonEmpty(target.DateDescription, fallback.DateDescription);
            target.DateTimeDescription = FirstNonEmpty(target.DateTimeDescription, fallback.DateTimeDescription);
            target.StatusId = FirstNonEmpty(target.StatusId, fallback.StatusId);
            target.StatusName = FirstNonEmpty(target.StatusName, fallback.StatusName);
            target.Status = FirstNonEmpty(target.Status, fallback.Status);

            if (target.BookingId <= 0 && fallback.BookingId > 0)
            {
                target.BookingId = fallback.BookingId;
            }

            if (target.CustomerId <= 0 && fallback.CustomerId > 0)
            {
                target.CustomerId = fallback.CustomerId;
            }

            if (target.CreatedDateTimestamp <= 0 && fallback.CreatedDateTimestamp > 0)
            {
                target.CreatedDateTimestamp = fallback.CreatedDateTimestamp;
            }
        }

        private static void HydrateLegacyTimeFields(CheckfrontBooking booking, string sourceDateTime)
        {
            if (booking == null || string.IsNullOrWhiteSpace(sourceDateTime))
            {
                return;
            }

            if (!DateTimeOffset.TryParse(sourceDateTime.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) &&
                !DateTimeOffset.TryParse(sourceDateTime.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out parsed) &&
                !DateTimeOffset.TryParse(sourceDateTime.Trim(), out parsed))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(booking.StartDateRaw))
            {
                booking.StartDateRaw = parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            if (string.IsNullOrWhiteSpace(booking.StartTimeRaw))
            {
                booking.StartTimeRaw = parsed.ToString("HH:mm", CultureInfo.InvariantCulture);
            }

            if (string.IsNullOrWhiteSpace(booking.DateDescription))
            {
                booking.DateDescription = parsed.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            }

            if (string.IsNullOrWhiteSpace(booking.DateTimeDescription))
            {
                booking.DateTimeDescription = parsed.ToString("O", CultureInfo.InvariantCulture);
            }
        }

        private static string BuildCustomerName(string? firstName, string? lastName)
        {
            var joined = string.Join(
                " ",
                new[] { firstName, lastName }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
            return string.IsNullOrWhiteSpace(joined) ? string.Empty : joined;
        }

        private static int TryParseInt(string? value)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;
        }

        private static long TryParseUnixTimestamp(string? rawCreatedValue)
        {
            if (string.IsNullOrWhiteSpace(rawCreatedValue))
            {
                return 0;
            }

            return DateTimeOffset.TryParse(rawCreatedValue.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed.ToUnixTimeSeconds()
                : 0;
        }

        private static string GetFieldValue(Dictionary<string, JsonElement>? fields, string key)
        {
            if (fields == null || fields.Count == 0 || string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            if (!fields.TryGetValue(key, out var value))
            {
                return string.Empty;
            }

            return value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : value.ToString();
        }

        private void ConfigureV4HttpClient()
        {
            if (_v4HttpClient.BaseAddress == null)
            {
                _v4HttpClient.BaseAddress = new Uri(_v4Endpoint, UriKind.Absolute);
            }

            _v4HttpClient.DefaultRequestHeaders.Accept.Clear();
            _v4HttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            ApplyV4AuthorizationHeader();
        }

        private void ApplyV4AuthorizationHeader()
        {
            var tokenValue = $"{_v4ApiKey}:{_v4ApiSecret}";
            var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes(tokenValue));
            _v4HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);
        }

        private static string NormalizeEndpoint(string? endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return string.Empty;
            }

            var trimmed = endpoint.Trim();
            return trimmed.EndsWith("/", StringComparison.Ordinal) ? trimmed : trimmed + "/";
        }

        private static string FirstNonEmpty(string? primary, string? fallback)
        {
            if (!string.IsNullOrWhiteSpace(primary))
            {
                return primary.Trim();
            }

            return string.IsNullOrWhiteSpace(fallback) ? string.Empty : fallback.Trim();
        }

        private static string FirstNonEmpty(string? first, string? second, string? third)
        {
            if (!string.IsNullOrWhiteSpace(first))
            {
                return first.Trim();
            }

            if (!string.IsNullOrWhiteSpace(second))
            {
                return second.Trim();
            }

            return string.IsNullOrWhiteSpace(third) ? string.Empty : third.Trim();
        }

        private static string DeriveV3Endpoint(string v4Endpoint)
        {
            if (string.IsNullOrWhiteSpace(v4Endpoint))
            {
                return string.Empty;
            }

            var marker = "/api/4.0/";
            var index = v4Endpoint.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return string.Empty;
            }

            return v4Endpoint[..index] + "/api/3.0/";
        }

        private static void WriteConsole(string message)
        {
            Console.WriteLine($"[CheckfrontReadOnlyV4] {DateTime.UtcNow:O} {message}");
        }
    }
}
