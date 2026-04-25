using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading;
using Email.Models;

namespace Email.Services
{
    /// <summary>
    /// Created: 2026-03-26 23:07 UTC
    /// Checkfront API client for querying bookings, customers, items, and managing booking state.
    /// Stripped from WhatsAppBusinessAPI prototype — removed webhook and GuruWalkExtractionResult dependencies.
    /// </summary>
    public class CheckfrontService
    {
        private const int MinimumRequestSpacingMs = 250;
        private static readonly JsonSerializerOptions LenientJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };
        private static readonly SemaphoreSlim RequestThrottleLock = new(1, 1);
        private static DateTime _lastRequestSentUtc = DateTime.MinValue;

        private readonly HttpClient _httpClient;
        private readonly CheckfrontConfig _config;
        private readonly CheckfrontEndpointModeState? _endpointModeState;
        private readonly CheckfrontOAuthService? _oauthService;
        private readonly ILogger<CheckfrontService> _logger;

        /// <summary>
        /// Created: 2026-03-26 23:07 UTC
        /// </summary>
        public CheckfrontService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<CheckfrontService> logger,
            CheckfrontEndpointModeState? endpointModeState = null,
            CheckfrontOAuthService? oauthService = null)
        {
            _httpClient = httpClient;
            _logger = logger;
            _endpointModeState = endpointModeState;
            _oauthService = oauthService;
            
            // Load configuration
            _config = new CheckfrontConfig();
            configuration.GetSection("Checkfront").Bind(_config);

            // Configure HttpClient
            ConfigureHttpClient();
        }

        /// <summary>
        /// Created: 2026-03-26 23:07 UTC
        /// </summary>
        private void ConfigureHttpClient()
        {
            try
            {
                // Set base address
                var activeEndpoint = ResolveActiveEndpoint();
                if (!string.IsNullOrWhiteSpace(activeEndpoint))
                {
                    _httpClient.BaseAddress = new Uri(activeEndpoint);
                }

                ApplyBasicAuthorizationHeader();

                // Set required headers per Checkfront documentation
                _httpClient.DefaultRequestHeaders.Accept.Clear();
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Email-CheckfrontIntegration/1.0");
                
                // Add Checkfront-specific headers
                _httpClient.DefaultRequestHeaders.Add("X-Forwarded-For", "44.214.139.11");
                _httpClient.DefaultRequestHeaders.Add("X-On-Behalf", "1");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG] CheckfrontService.ConfigureHttpClient error: {ex.Message}");
            }
        }

        /// <summary>
        /// Created: 2026-03-29 00:00 UTC
        /// </summary>
        private void ApplyBasicAuthorizationHeader()
        {
            if (string.IsNullOrWhiteSpace(_config.ApiKey) || string.IsNullOrWhiteSpace(_config.ApiSecret))
            {
                return;
            }

            var tokenValue = $"{_config.ApiKey}:{_config.ApiSecret}";
            var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes(tokenValue));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);
        }

        /// <summary>
        /// Created: 2026-03-29 00:00 UTC
        /// </summary>
        private async Task EnsureAuthorizationHeaderAsync(CancellationToken cancellationToken = default)
        {
            if (_oauthService != null && _oauthService.IsOAuthConfigured)
            {
                var accessToken = await _oauthService.GetValidAccessTokenAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(accessToken))
                {
                    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    return;
                }

                _logger.LogWarning("Checkfront OAuth2 is configured but no access token is available; using API key/secret fallback auth.");
            }

            ApplyBasicAuthorizationHeader();
        }

        /// <summary>
        /// Created: 2026-03-29 00:00 UTC
        /// </summary>
        private async Task<HttpResponseMessage> GetAsync(string endpoint, CancellationToken cancellationToken = default)
        {
            return await SendAsync(HttpMethod.Get, endpoint, content: null, cancellationToken);
        }

        /// <summary>
        /// Created: 2026-03-29 00:00 UTC
        /// </summary>
        private async Task<HttpResponseMessage> PostAsync(string endpoint, HttpContent content, CancellationToken cancellationToken = default)
        {
            return await SendAsync(HttpMethod.Post, endpoint, content, cancellationToken);
        }

        /// <summary>
        /// Created: 2026-03-29 00:00 UTC
        /// </summary>
        private async Task<HttpResponseMessage> PutAsync(string endpoint, HttpContent content, CancellationToken cancellationToken = default)
        {
            return await SendAsync(HttpMethod.Put, endpoint, content, cancellationToken);
        }

        /// <summary>
        /// Created: 2026-03-30 00:00 UTC
        /// Unified request sender with request/response logging.
        /// </summary>
        private async Task<HttpResponseMessage> SendAsync(
            HttpMethod method,
            string endpoint,
            HttpContent? content,
            CancellationToken cancellationToken = default)
        {
            await WaitForRequestSlotAsync(cancellationToken);
            await EnsureAuthorizationHeaderAsync(cancellationToken);
            var requestUri = ResolveRequestUri(endpoint);
            var endpointForLog = requestUri.ToString();
            var authMode = GetAuthModeForLog();
            _logger.LogInformation(
                "Checkfront API request {Method} {Endpoint} Auth={AuthMode}",
                method.Method,
                endpointForLog,
                authMode);
            WriteCheckfrontConsole(
                $"Request {method.Method} {endpointForLog} Auth={authMode}");

            var request = new HttpRequestMessage(method, requestUri);
            if (content != null)
            {
                request.Content = content;
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);
            _logger.LogInformation(
                "Checkfront API response {Method} {Endpoint} HttpStatus={StatusCode}",
                method.Method,
                endpointForLog,
                (int)response.StatusCode);
            WriteCheckfrontConsole(
                $"Response {method.Method} {endpointForLog} HttpStatus={(int)response.StatusCode}");

            return response;
        }

        /// <summary>
        /// Created: 2026-04-12 00:00 UTC
        /// Prevents burst traffic that can trigger vendor-side temporary bans.
        /// </summary>
        private static async Task WaitForRequestSlotAsync(CancellationToken cancellationToken)
        {
            await RequestThrottleLock.WaitAsync(cancellationToken);
            try
            {
                if (_lastRequestSentUtc != DateTime.MinValue)
                {
                    var elapsedMs = (DateTime.UtcNow - _lastRequestSentUtc).TotalMilliseconds;
                    var waitMs = MinimumRequestSpacingMs - (int)Math.Round(elapsedMs, MidpointRounding.AwayFromZero);
                    if (waitMs > 0)
                    {
                        await Task.Delay(waitMs, cancellationToken);
                    }
                }

                _lastRequestSentUtc = DateTime.UtcNow;
            }
            finally
            {
                RequestThrottleLock.Release();
            }
        }

        /// <summary>
        /// Created: 2026-03-30 00:00 UTC
        /// </summary>
        private Uri ResolveRequestUri(string endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                throw new ArgumentException("Endpoint cannot be empty.", nameof(endpoint));
            }

            if (Uri.TryCreate(endpoint, UriKind.Absolute, out var absoluteEndpoint))
            {
                return absoluteEndpoint;
            }

            var activeEndpoint = ResolveActiveEndpoint();
            if (!string.IsNullOrWhiteSpace(activeEndpoint) &&
                Uri.TryCreate(activeEndpoint, UriKind.Absolute, out var activeBaseUri))
            {
                return new Uri(activeBaseUri, endpoint.TrimStart('/'));
            }

            if (_httpClient.BaseAddress != null)
            {
                return new Uri(_httpClient.BaseAddress, endpoint.TrimStart('/'));
            }

            throw new InvalidOperationException("No valid Checkfront API endpoint is configured.");
        }

        /// <summary>
        /// Created: 2026-03-30 00:00 UTC
        /// </summary>
        private string ResolveActiveEndpoint()
        {
            var endpointFromMode = _endpointModeState?.GetActiveEndpoint();
            if (!string.IsNullOrWhiteSpace(endpointFromMode))
            {
                return endpointFromMode;
            }

            if (!string.IsNullOrWhiteSpace(_config.ApiEndpoint))
            {
                return _config.ApiEndpoint;
            }

            if (!string.IsNullOrWhiteSpace(_config.ProductionApiEndpoint))
            {
                return _config.ProductionApiEndpoint;
            }

            return _config.DevelopmentApiEndpoint;
        }

        /// <summary>
        /// Created: 2026-03-26 23:07 UTC
        /// Test the connection to Checkfront API
        /// </summary>
        public async Task<CheckfrontConnectionTest> TestConnectionAsync()
        {
            try
            {
                _logger.LogInformation("Testing Checkfront API connection...");

                // Test with ping endpoint first
                var pingResponse = await GetAsync("ping");
                
                if (!pingResponse.IsSuccessStatusCode)
                {
                    return new CheckfrontConnectionTest
                    {
                        IsConnected = false,
                        Message = $"Ping failed: {pingResponse.StatusCode} - {pingResponse.ReasonPhrase}"
                    };
                }

                // Get company information
                var companyResponse = await GetAsync("company");
                
                if (!companyResponse.IsSuccessStatusCode)
                {
                    return new CheckfrontConnectionTest
                    {
                        IsConnected = false,
                        Message = $"Company info failed: {companyResponse.StatusCode} - {companyResponse.ReasonPhrase}"
                    };
                }

                var companyJson = await companyResponse.Content.ReadAsStringAsync();
                var companyData = JsonSerializer.Deserialize<CheckfrontCompanyResponse>(companyJson, LenientJsonOptions);

                _logger.LogInformation("Checkfront API connection successful");

                return new CheckfrontConnectionTest
                {
                    IsConnected = true,
                    Message = "Connection successful",
                    CompanyInfo = companyData?.Company
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing Checkfront API connection");
                Console.WriteLine($"[DEBUG] CheckfrontService.TestConnectionAsync error: {ex.Message}");
                return new CheckfrontConnectionTest
                {
                    IsConnected = false,
                    Message = $"Connection error: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Created: 2026-03-26 23:07 UTC
        /// Get company information from Checkfront
        /// </summary>
        public async Task<CheckfrontCompany?> GetCompanyInfoAsync()
        {
            try
            {
                var response = await GetAsync("company");
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                _logger.LogInformation($"Raw Checkfront company response: {json}");

                var data = JsonSerializer.Deserialize<CheckfrontCompanyResponse>(json, LenientJsonOptions);
                _logger.LogInformation($"Deserialized data - Status: {data?.Request?.Status}, Company: {data?.Company?.Name}");

                return data?.Company;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting company information from Checkfront");
                Console.WriteLine($"[DEBUG] CheckfrontService.GetCompanyInfoAsync error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Created: 2026-03-26 23:07 UTC
        /// Get raw company response for debugging
        /// </summary>
        public async Task<string> GetRawCompanyResponseAsync()
        {
            try
            {
                var response = await GetAsync("company");
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                return json;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting raw company response from Checkfront");
                Console.WriteLine($"[DEBUG] CheckfrontService.GetRawCompanyResponseAsync error: {ex.Message}");
                return $"Error: {ex.Message}";
            }
        }

        /// <summary>
        /// Created: 2026-03-26 23:07 UTC
        /// Get all items from Checkfront inventory
        /// </summary>
        public async Task<List<CheckfrontItem>> GetItemsAsync()
        {
            try
            {
                var response = await GetAsync("item");
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<CheckfrontResponse<Dictionary<string, CheckfrontItem>>>(json, LenientJsonOptions);

                return data?.Data?.Values.ToList() ?? new List<CheckfrontItem>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting items from Checkfront");
                Console.WriteLine($"[DEBUG] CheckfrontService.GetItemsAsync error: {ex.Message}");
                return new List<CheckfrontItem>();
            }
        }

        /// <summary>
        /// Created: 2026-03-26 23:07 UTC
        /// Get bookings from Checkfront with optional filters
        /// </summary>
        public async Task<List<CheckfrontBooking>> GetBookingsAsync(DateTime? startDate = null, DateTime? endDate = null, string? status = null, int? limit = null, int? page = null)
        {
            try
            {
                var queryParams = new List<string>();
                
                if (startDate.HasValue)
                    queryParams.Add($"start_date={startDate.Value:yyyy-MM-dd}");
                
                if (endDate.HasValue)
                    queryParams.Add($"end_date={endDate.Value:yyyy-MM-dd}");
                
                if (!string.IsNullOrEmpty(status))
                    queryParams.Add($"status_id={status}");

                if (limit.HasValue)
                    queryParams.Add($"limit={limit.Value}");

                if (page.HasValue)
                    queryParams.Add($"page={page.Value}");

                var queryString = queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : "";
                var endpoint = $"booking{queryString}";
                var response = await GetAsync(endpoint);

                if (!response.IsSuccessStatusCode)
                {
                    await LogHttpFailureAsync(response, "GetBookings", endpoint);
                    return new List<CheckfrontBooking>();
                }

                var json = await response.Content.ReadAsStringAsync();
                _logger.LogDebug($"Raw booking response: {json}");

                if (!IsRequestStatusOk(json, "GetBookings", endpoint))
                {
                    return new List<CheckfrontBooking>();
                }

                return ParseBookingSearchResponse(json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting bookings from Checkfront");
                Console.WriteLine($"[DEBUG] CheckfrontService.GetBookingsAsync error: {ex.Message}");
                return new List<CheckfrontBooking>();
            }
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// Updated: 2026-03-26 00:00 UTC - Added booking lookup by code/id for pull-on-parse enrichment.
        /// Updated: 2026-03-26 00:00 UTC - Merge booking/{code} detail with booking?code= index row to fill sparse fields.
        /// </summary>
        public async Task<CheckfrontBooking?> GetBookingByCodeOrIdAsync(string codeOrId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(codeOrId))
                {
                    return null;
                }

                var escaped = Uri.EscapeDataString(codeOrId.Trim());
                var endpoint = $"booking/{escaped}";
                var response = await GetAsync(endpoint);

                if (!response.IsSuccessStatusCode)
                {
                    await LogHttpFailureAsync(response, "GetBookingByCodeOrId", endpoint, codeOrId);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                if (!IsRequestStatusOk(json, "GetBookingByCodeOrId", endpoint, codeOrId))
                {
                    return null;
                }

                var detailBooking = ParseBookingDetailResponse(json);
                if (detailBooking == null)
                {
                    _logger.LogInformation("Checkfront booking detail parse returned null for {CodeOrId}.", codeOrId);
                }

                var indexBooking = await TryGetBookingByCodeFromIndexAsync(codeOrId);
                var merged = MergeBookingSnapshots(detailBooking, indexBooking, codeOrId);
                if (merged == null)
                {
                    _logger.LogInformation("Checkfront booking merge returned null for {CodeOrId}.", codeOrId);
                }

                return merged;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Checkfront booking detail by code/id: {CodeOrId}", codeOrId);
                Console.WriteLine($"[DEBUG] CheckfrontService.GetBookingByCodeOrIdAsync error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// Updated: 2026-03-26 00:00 UTC - Added bookings lookup by customer id for email fallback enrichment.
        /// Updated: 2026-03-26 00:00 UTC - Keep int overload for backward compatibility.
        /// </summary>
        public async Task<List<CheckfrontBooking>> GetBookingsByCustomerIdAsync(int customerId, int limit = 25, int page = 1)
        {
            try
            {
                if (customerId <= 0)
                {
                    return new List<CheckfrontBooking>();
                }

                return await GetBookingsByCustomerIdAsync(
                    customerId.ToString(CultureInfo.InvariantCulture),
                    limit,
                    page);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Checkfront bookings by numeric customer id: {CustomerId}", customerId);
                Console.WriteLine($"[DEBUG] CheckfrontService.GetBookingsByCustomerIdAsync(int) error: {ex.Message}");
                return new List<CheckfrontBooking>();
            }
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// Updated: 2026-03-26 00:00 UTC - Support string customer IDs (Checkfront may return alphanumeric values).
        /// </summary>
        public async Task<List<CheckfrontBooking>> GetBookingsByCustomerIdAsync(string customerId, int limit = 25, int page = 1)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(customerId))
                {
                    return new List<CheckfrontBooking>();
                }

                var safeLimit = Math.Max(1, limit);
                var safePage = Math.Max(1, page);
                var endpoint = $"booking?customer_id={Uri.EscapeDataString(customerId.Trim())}&limit={safeLimit}&page={safePage}";
                var response = await GetAsync(endpoint);

                if (!response.IsSuccessStatusCode)
                {
                    await LogHttpFailureAsync(response, "GetBookingsByCustomerId", endpoint, customerId);
                    return new List<CheckfrontBooking>();
                }

                var json = await response.Content.ReadAsStringAsync();
                if (!IsRequestStatusOk(json, "GetBookingsByCustomerId", endpoint, customerId))
                {
                    return new List<CheckfrontBooking>();
                }

                return ParseBookingSearchResponse(json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Checkfront bookings by customer id: {CustomerId}", customerId);
                Console.WriteLine($"[DEBUG] CheckfrontService.GetBookingsByCustomerIdAsync(string) error: {ex.Message}");
                return new List<CheckfrontBooking>();
            }
        }

        /// <summary>
        /// Created: 2026-03-26 23:07 UTC
        /// Search customers by phone number
        /// </summary>
        public async Task<List<CheckfrontCustomerMatch>> SearchCustomersByPhoneAsync(string phoneNumber)
        {
            try
            {
                if (string.IsNullOrEmpty(phoneNumber))
                    return new List<CheckfrontCustomerMatch>();

                _logger.LogInformation($"Searching customers by phone: {phoneNumber}");

                var endpoint = $"customer?customer_phone={Uri.EscapeDataString(phoneNumber)}";
                var response = await GetAsync(endpoint);
                
                if (!response.IsSuccessStatusCode)
                {
                    await LogHttpFailureAsync(response, "SearchCustomersByPhone", endpoint, phoneNumber);
                    return new List<CheckfrontCustomerMatch>();
                }

                var json = await response.Content.ReadAsStringAsync();
                if (!IsRequestStatusOk(json, "SearchCustomersByPhone", endpoint, phoneNumber))
                {
                    return new List<CheckfrontCustomerMatch>();
                }

                _logger.LogDebug($"Customer search response: {json}");

                var customers = ParseCustomerSearchResponse(json);
                
                // Calculate confidence scores for phone matches
                var matches = customers.Select(c => new CheckfrontCustomerMatch
                {
                    Customer = c,
                    MatchType = "phone",
                    ConfidenceScore = CalculatePhoneMatchConfidence(phoneNumber, c.CustomerPhone),
                    MatchReason = $"Phone number match: {c.CustomerPhone}"
                }).Where(m => m.ConfidenceScore > 50).ToList();

                _logger.LogInformation($"Found {matches.Count} customer matches by phone");
                return matches.OrderByDescending(m => m.ConfidenceScore).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error searching customers by phone: {phoneNumber}");
                Console.WriteLine($"[DEBUG] CheckfrontService.SearchCustomersByPhoneAsync error: {ex.Message}");
                return new List<CheckfrontCustomerMatch>();
            }
        }

        /// <summary>
        /// Created: 2026-03-26 23:07 UTC
        /// Search customers by email address
        /// </summary>
        public async Task<List<CheckfrontCustomerMatch>> SearchCustomersByEmailAsync(string emailAddress)
        {
            try
            {
                if (string.IsNullOrEmpty(emailAddress))
                    return new List<CheckfrontCustomerMatch>();

                _logger.LogInformation($"Searching customers by email: {emailAddress}");

                var endpoint = $"customer?customer_email={Uri.EscapeDataString(emailAddress)}";
                var response = await GetAsync(endpoint);
                
                if (!response.IsSuccessStatusCode)
                {
                    await LogHttpFailureAsync(response, "SearchCustomersByEmail", endpoint, emailAddress);
                    return new List<CheckfrontCustomerMatch>();
                }

                var json = await response.Content.ReadAsStringAsync();
                if (!IsRequestStatusOk(json, "SearchCustomersByEmail", endpoint, emailAddress))
                {
                    return new List<CheckfrontCustomerMatch>();
                }

                _logger.LogDebug($"Customer search response: {json}");

                var customers = ParseCustomerSearchResponse(json);
                
                // Calculate confidence scores for email matches
                var matches = customers.Select(c => new CheckfrontCustomerMatch
                {
                    Customer = c,
                    MatchType = "email",
                    ConfidenceScore = string.Equals(emailAddress, c.CustomerEmail, StringComparison.OrdinalIgnoreCase) ? 95 : 70,
                    MatchReason = $"Email address match: {c.CustomerEmail}"
                }).Where(m => m.ConfidenceScore > 50).ToList();

                _logger.LogInformation($"Found {matches.Count} customer matches by email");
                return matches.OrderByDescending(m => m.ConfidenceScore).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error searching customers by email: {emailAddress}");
                Console.WriteLine($"[DEBUG] CheckfrontService.SearchCustomersByEmailAsync error: {ex.Message}");
                return new List<CheckfrontCustomerMatch>();
            }
        }

        /// <summary>
        /// Created: 2026-03-26 23:07 UTC
        /// Search customers by name (with fuzzy matching)
        /// </summary>
        public async Task<List<CheckfrontCustomerMatch>> SearchCustomersByNameAsync(string customerName)
        {
            try
            {
                if (string.IsNullOrEmpty(customerName))
                    return new List<CheckfrontCustomerMatch>();

                _logger.LogInformation($"Searching customers by name: {customerName}");

                var endpoint = "customer?limit=100";
                var response = await GetAsync(endpoint);
                
                if (!response.IsSuccessStatusCode)
                {
                    await LogHttpFailureAsync(response, "SearchCustomersByName", endpoint, customerName);
                    return new List<CheckfrontCustomerMatch>();
                }

                var json = await response.Content.ReadAsStringAsync();
                if (!IsRequestStatusOk(json, "SearchCustomersByName", endpoint, customerName))
                {
                    return new List<CheckfrontCustomerMatch>();
                }

                var customers = ParseCustomerSearchResponse(json);
                
                // Perform fuzzy name matching
                var matches = customers.Select(c => new CheckfrontCustomerMatch
                {
                    Customer = c,
                    MatchType = "name",
                    ConfidenceScore = CalculateNameMatchConfidence(customerName, c.CustomerName),
                    MatchReason = $"Name similarity match: {c.CustomerName}"
                }).Where(m => m.ConfidenceScore > 60).ToList();

                _logger.LogInformation($"Found {matches.Count} customer matches by name");
                return matches.OrderByDescending(m => m.ConfidenceScore).Take(10).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error searching customers by name: {customerName}");
                Console.WriteLine($"[DEBUG] CheckfrontService.SearchCustomersByNameAsync error: {ex.Message}");
                return new List<CheckfrontCustomerMatch>();
            }
        }

        /// <summary>
        /// Created: 2026-03-26 23:07 UTC
        /// Update booking status (for cancellations, confirmations, etc.)
        /// </summary>
        public async Task<bool> UpdateBookingStatusAsync(int bookingId, string statusId, string reason = "")
        {
            try
            {
                _logger.LogInformation($"Updating booking {bookingId} status to {statusId}");

                var payload = new
                {
                    status_id = statusId
                };

                var jsonContent = JsonSerializer.Serialize(payload);
                var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

                var response = await PutAsync($"booking/{bookingId}", content);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Failed to update booking status: {response.StatusCode} - {errorContent}");
                    return false;
                }

                _logger.LogInformation($"Successfully updated booking {bookingId} status to {statusId}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating booking status: {bookingId}");
                Console.WriteLine($"[DEBUG] CheckfrontService.UpdateBookingStatusAsync error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Created: 2026-03-26 23:07 UTC
        /// Add a note to a booking
        /// </summary>
        public async Task<bool> AddBookingNoteAsync(int bookingId, string note)
        {
            try
            {
                _logger.LogInformation($"Adding note to booking {bookingId}");

                var payload = new
                {
                    note = note,
                    date = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
                };

                var jsonContent = JsonSerializer.Serialize(payload);
                var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

                var response = await PostAsync($"booking/{bookingId}/note", content);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Failed to add booking note: {response.StatusCode} - {errorContent}");
                    return false;
                }

                _logger.LogInformation($"Successfully added note to booking {bookingId}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error adding booking note: {bookingId}");
                Console.WriteLine($"[DEBUG] CheckfrontService.AddBookingNoteAsync error: {ex.Message}");
                return false;
            }
        }

        // ── Helper methods ──────────────────────────────────────────────

        /// <summary>
        /// Created: 2026-03-26 23:07 UTC
        /// </summary>
        private List<CheckfrontCustomerInfo> ParseCustomerSearchResponse(string json)
        {
            var customers = new List<CheckfrontCustomerInfo>();
            
            try
            {
                using var document = JsonDocument.Parse(json);
                
                if (document.RootElement.TryGetProperty("customers", out var customersElement))
                {
                    foreach (var customerProperty in customersElement.EnumerateObject())
                    {
                        try
                        {
                            var customer = JsonSerializer.Deserialize<CheckfrontCustomerInfo>(
                                customerProperty.Value.GetRawText(), 
                                LenientJsonOptions);
                            
                            if (customer != null)
                            {
                                customer.Code = customerProperty.Name;
                                if (TryGetString(customerProperty.Value, "customer_id", out var rawCustomerId))
                                {
                                    customer.CustomerIdRaw = rawCustomerId ?? string.Empty;
                                    if (customer.CustomerId <= 0 &&
                                        int.TryParse(rawCustomerId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCustomerId))
                                    {
                                        customer.CustomerId = parsedCustomerId;
                                    }
                                }
                                customers.Add(customer);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, $"Failed to parse customer {customerProperty.Name}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing customer search response");
                Console.WriteLine($"[DEBUG] CheckfrontService.ParseCustomerSearchResponse error: {ex.Message}");
            }

            return customers;
        }

        /// <summary>
        /// Created: 2026-03-26 23:07 UTC
        /// </summary>
        private List<CheckfrontBooking> ParseBookingSearchResponse(string json)
        {
            var bookings = new List<CheckfrontBooking>();
            
            try
            {
                using var document = JsonDocument.Parse(json);
                 
                if (document.RootElement.TryGetProperty("booking/index", out var bookingIndex))
                {
                    ParseBookingContainer(bookings, bookingIndex);
                }
                else if (document.RootElement.TryGetProperty("booking", out var booking))
                {
                    // Some endpoints return "booking" instead of "booking/index".
                    ParseBookingContainer(bookings, booking);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing booking search response");
                Console.WriteLine($"[DEBUG] CheckfrontService.ParseBookingSearchResponse error: {ex.Message}");
            }

            return bookings;
        }

        /// <summary>
        /// Created: 2026-03-29 00:00 UTC
        /// Supports object and array booking/index shapes from Checkfront responses.
        /// </summary>
        private void ParseBookingContainer(List<CheckfrontBooking> bookings, JsonElement bookingContainer)
        {
            if (bookingContainer.ValueKind == JsonValueKind.Object)
            {
                foreach (var bookingProperty in bookingContainer.EnumerateObject())
                {
                    try
                    {
                        var parsedBooking = ParseBookingElement(bookingProperty.Value, bookingProperty.Name);
                        if (parsedBooking != null)
                        {
                            bookings.Add(parsedBooking);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse booking object node {BookingNode}.", bookingProperty.Name);
                    }
                }

                return;
            }

            if (bookingContainer.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var bookingElement in bookingContainer.EnumerateArray())
                {
                    try
                    {
                        if (bookingElement.ValueKind != JsonValueKind.Object)
                        {
                            index++;
                            continue;
                        }

                        string? fallbackCode = null;
                        if (TryGetString(bookingElement, "code", out var code) && !string.IsNullOrWhiteSpace(code))
                        {
                            fallbackCode = code;
                        }
                        else if (TryGetString(bookingElement, "id", out var idCode) && !string.IsNullOrWhiteSpace(idCode))
                        {
                            fallbackCode = idCode;
                        }
                        else if (TryGetString(bookingElement, "booking_id", out var bookingId) && !string.IsNullOrWhiteSpace(bookingId))
                        {
                            fallbackCode = bookingId;
                        }

                        var parsedBooking = ParseBookingElement(bookingElement, fallbackCode ?? string.Empty);
                        if (parsedBooking != null)
                        {
                            bookings.Add(parsedBooking);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse booking array node at index {Index}.", index);
                    }

                    index++;
                }
            }
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// Updated: 2026-03-26 00:00 UTC - Added parser for booking/{idOrCode} payload shape.
        /// </summary>
        private CheckfrontBooking? ParseBookingDetailResponse(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                if (!document.RootElement.TryGetProperty("booking", out var bookingElement))
                {
                    return null;
                }

                if (bookingElement.ValueKind == JsonValueKind.Object)
                {
                    return ParseBookingElement(bookingElement, string.Empty);
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing booking detail response");
                Console.WriteLine($"[DEBUG] CheckfrontService.ParseBookingDetailResponse error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// Updated: 2026-03-26 00:00 UTC - Fetch booking/index row by code to fill fields omitted by booking/{code} payloads.
        /// </summary>
        private async Task<CheckfrontBooking?> TryGetBookingByCodeFromIndexAsync(string codeOrId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(codeOrId))
                {
                    return null;
                }

                var endpoint = $"booking?code={Uri.EscapeDataString(codeOrId.Trim())}&limit=1&page=1";
                var response = await GetAsync(endpoint);
                if (!response.IsSuccessStatusCode)
                {
                    await LogHttpFailureAsync(response, "TryGetBookingByCodeFromIndex", endpoint, codeOrId);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                if (!IsRequestStatusOk(json, "TryGetBookingByCodeFromIndex", endpoint, codeOrId))
                {
                    return null;
                }

                var lookup = codeOrId.Trim();
                var matches = ParseBookingSearchResponse(json);
                return matches.FirstOrDefault(b =>
                    string.Equals(b.Code, lookup, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(b.BookingReference, lookup, StringComparison.OrdinalIgnoreCase) ||
                    (b.BookingId > 0 &&
                     string.Equals(b.BookingId.ToString(CultureInfo.InvariantCulture), lookup, StringComparison.OrdinalIgnoreCase)));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TryGetBookingByCodeFromIndexAsync failed for {CodeOrId}", codeOrId);
                Console.WriteLine($"[DEBUG] CheckfrontService.TryGetBookingByCodeFromIndexAsync error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// Updated: 2026-03-26 00:00 UTC - Merge detail and index booking snapshots, preferring non-empty values.
        /// </summary>
        private static CheckfrontBooking? MergeBookingSnapshots(CheckfrontBooking? detail, CheckfrontBooking? index, string fallbackCode)
        {
            if (detail == null && index == null)
            {
                return null;
            }

            if (detail == null)
            {
                if (index != null && string.IsNullOrWhiteSpace(index.Code))
                {
                    index.Code = fallbackCode?.Trim() ?? string.Empty;
                }
                return index;
            }

            if (index == null)
            {
                if (string.IsNullOrWhiteSpace(detail.Code))
                {
                    detail.Code = fallbackCode?.Trim() ?? string.Empty;
                }
                return detail;
            }

            detail.Code = FirstNonEmpty(detail.Code, index.Code, fallbackCode) ?? string.Empty;
            detail.BookingReference = FirstNonEmpty(detail.BookingReference, index.BookingReference) ?? string.Empty;
            detail.Status = FirstNonEmpty(detail.Status, index.Status) ?? string.Empty;
            detail.StatusId = FirstNonEmpty(detail.StatusId, index.StatusId) ?? string.Empty;
            detail.StatusName = FirstNonEmpty(detail.StatusName, index.StatusName) ?? string.Empty;
            detail.CustomerName = FirstNonEmpty(detail.CustomerName, index.CustomerName) ?? string.Empty;
            detail.CustomerEmail = FirstNonEmpty(detail.CustomerEmail, index.CustomerEmail) ?? string.Empty;
            detail.CustomerPhone = FirstNonEmpty(detail.CustomerPhone, index.CustomerPhone) ?? string.Empty;
            detail.Summary = FirstNonEmpty(detail.Summary, index.Summary) ?? string.Empty;
            detail.DateDescription = FirstNonEmpty(detail.DateDescription, index.DateDescription) ?? string.Empty;
            detail.StartDateRaw = FirstNonEmpty(detail.StartDateRaw, index.StartDateRaw) ?? string.Empty;
            detail.StartTimeRaw = FirstNonEmpty(detail.StartTimeRaw, index.StartTimeRaw) ?? string.Empty;
            detail.DateTimeDescription = FirstNonEmpty(detail.DateTimeDescription, index.DateTimeDescription) ?? string.Empty;
            detail.ItemName = FirstNonEmpty(detail.ItemName, index.ItemName) ?? string.Empty;
            detail.ItemTitle = FirstNonEmpty(detail.ItemTitle, index.ItemTitle) ?? string.Empty;
            detail.Total = FirstNonEmpty(detail.Total, index.Total) ?? string.Empty;
            detail.TaxTotal = FirstNonEmpty(detail.TaxTotal, index.TaxTotal) ?? string.Empty;
            detail.PaidTotal = FirstNonEmpty(detail.PaidTotal, index.PaidTotal) ?? string.Empty;
            detail.Token = FirstNonEmpty(detail.Token, index.Token) ?? string.Empty;
            detail.TrackingId = FirstNonEmpty(detail.TrackingId, index.TrackingId);

            if (detail.BookingId <= 0 && index.BookingId > 0)
            {
                detail.BookingId = index.BookingId;
            }

            if (detail.CustomerId <= 0 && index.CustomerId > 0)
            {
                detail.CustomerId = index.CustomerId;
            }

            if (detail.CreatedDateTimestamp <= 0 && index.CreatedDateTimestamp > 0)
            {
                detail.CreatedDateTimestamp = index.CreatedDateTimestamp;
            }

            if (detail.Quantity <= 0 && index.Quantity > 0)
            {
                detail.Quantity = index.Quantity;
            }

            if (detail.TotalPax <= 0 && index.TotalPax > 0)
            {
                detail.TotalPax = index.TotalPax;
            }

            if (detail.NumberOfAdults <= 0 && index.NumberOfAdults > 0)
            {
                detail.NumberOfAdults = index.NumberOfAdults;
            }

            if (detail.NumberOfChildren <= 0 && index.NumberOfChildren > 0)
            {
                detail.NumberOfChildren = index.NumberOfChildren;
            }

            if (detail.NumberOfAttendees <= 0 && index.NumberOfAttendees > 0)
            {
                detail.NumberOfAttendees = index.NumberOfAttendees;
            }

            if (detail.Items.Count == 0 && index.Items.Count > 0)
            {
                detail.Items = index.Items;
            }

            return detail;
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return null;
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        private CheckfrontBooking? ParseBookingElement(JsonElement bookingElement, string fallbackCode)
        {
            try
            {
                CheckfrontBooking? booking;
                try
                {
                    booking = JsonSerializer.Deserialize<CheckfrontBooking>(
                        bookingElement.GetRawText(),
                        LenientJsonOptions);
                }
                catch (JsonException ex)
                {
                    // Some Checkfront payloads send numeric fields as strings or empty strings.
                    // Fall back to manual field extraction instead of dropping the booking.
                    _logger.LogWarning(ex, "Lenient deserialize failed for booking element, using manual field extraction fallback.");
                    booking = new CheckfrontBooking();
                }

                if (booking == null)
                {
                    return null;
                }

                if (booking.BookingId == 0 && TryGetInt(bookingElement, "booking_id", out var bookingId))
                {
                    booking.BookingId = bookingId;
                }

                if (string.IsNullOrWhiteSpace(booking.Code) && !string.IsNullOrWhiteSpace(fallbackCode))
                {
                    booking.Code = fallbackCode;
                }

                if (string.IsNullOrWhiteSpace(booking.Code) && !string.IsNullOrWhiteSpace(booking.BookingReference))
                {
                    booking.Code = booking.BookingReference;
                }

                if (string.IsNullOrWhiteSpace(booking.Code) && TryGetString(bookingElement, "id", out var idCode))
                {
                    booking.Code = idCode!;
                }

                if (booking.CreatedDateTimestamp == 0 && TryGetLong(bookingElement, "created_date", out var createdDate))
                {
                    booking.CreatedDateTimestamp = createdDate;
                }
                else if (booking.CreatedDateTimestamp == 0 &&
                         TryGetString(bookingElement, "created", out var createdIso) &&
                         !string.IsNullOrWhiteSpace(createdIso) &&
                         DateTimeOffset.TryParse(createdIso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt))
                {
                    booking.CreatedDateTimestamp = createdAt.ToUnixTimeSeconds();
                }

                if (string.IsNullOrWhiteSpace(booking.StartDateRaw) && TryGetString(bookingElement, "start_date", out var startDate))
                {
                    booking.StartDateRaw = startDate!;
                }

                if (string.IsNullOrWhiteSpace(booking.StartTimeRaw) && TryGetString(bookingElement, "start_time", out var startTime))
                {
                    booking.StartTimeRaw = startTime!;
                }

                if (string.IsNullOrWhiteSpace(booking.DateDescription) && TryGetString(bookingElement, "date_desc", out var dateDesc))
                {
                    booking.DateDescription = dateDesc!;
                }

                if (string.IsNullOrWhiteSpace(booking.DateTimeDescription) && TryGetString(bookingElement, "date_time", out var dateTimeDescription))
                {
                    booking.DateTimeDescription = dateTimeDescription!;
                }

                HydrateStartFieldsFromDateTime(
                    booking,
                    FirstNonEmpty(
                        GetStringValue(bookingElement, "start"),
                        GetStringValue(bookingElement, "startAt"),
                        GetStringValue(bookingElement, "checkIn"),
                        GetStringValue(bookingElement, "check_in"),
                        GetStringValue(bookingElement, "checkInAt"),
                        GetStringValue(bookingElement, "check_in_at"),
                        GetStringValue(bookingElement, "dateTime")));

                if (string.IsNullOrWhiteSpace(booking.ItemName) && TryGetString(bookingElement, "item_name", out var itemName))
                {
                    booking.ItemName = itemName!;
                }

                if (string.IsNullOrWhiteSpace(booking.ItemTitle) && TryGetString(bookingElement, "item_title", out var itemTitle))
                {
                    booking.ItemTitle = itemTitle!;
                }

                if (string.IsNullOrWhiteSpace(booking.StatusId) && TryGetString(bookingElement, "status_id", out var statusId))
                {
                    booking.StatusId = statusId!;
                }

                if (string.IsNullOrWhiteSpace(booking.StatusName) && TryGetString(bookingElement, "status_name", out var statusName))
                {
                    booking.StatusName = statusName!;
                }

                if (string.IsNullOrWhiteSpace(booking.Status) && TryGetString(bookingElement, "status", out var status))
                {
                    booking.Status = status!;
                }

                if (string.IsNullOrWhiteSpace(booking.CustomerName) && TryGetString(bookingElement, "customer_name", out var customerName))
                {
                    booking.CustomerName = customerName!;
                }

                if (string.IsNullOrWhiteSpace(booking.CustomerEmail) && TryGetString(bookingElement, "customer_email", out var customerEmail))
                {
                    booking.CustomerEmail = customerEmail!;
                }

                if (string.IsNullOrWhiteSpace(booking.CustomerPhone) && TryGetString(bookingElement, "customer_phone", out var customerPhone))
                {
                    booking.CustomerPhone = customerPhone!;
                }

                if (string.IsNullOrWhiteSpace(booking.CustomerName))
                {
                    var firstName = FirstNonEmpty(
                        GetStringValue(bookingElement, "first_name"),
                        GetStringValue(bookingElement, "firstName"));
                    var lastName = FirstNonEmpty(
                        GetStringValue(bookingElement, "last_name"),
                        GetStringValue(bookingElement, "lastName"));
                    var fullName = string.Join(
                        " ",
                        new[] { firstName, lastName }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
                    if (!string.IsNullOrWhiteSpace(fullName))
                    {
                        booking.CustomerName = fullName;
                    }
                }

                if (string.IsNullOrWhiteSpace(booking.Summary) && TryGetString(bookingElement, "summary", out var summary))
                {
                    booking.Summary = summary!;
                }

                if (string.IsNullOrWhiteSpace(booking.Summary) && TryGetString(bookingElement, "itemSummary", out var itemSummary))
                {
                    booking.Summary = itemSummary!;
                }

                if (string.IsNullOrWhiteSpace(booking.ItemName) && !string.IsNullOrWhiteSpace(booking.Summary))
                {
                    booking.ItemName = booking.Summary;
                }

                if (string.IsNullOrWhiteSpace(booking.ItemTitle) && !string.IsNullOrWhiteSpace(booking.Summary))
                {
                    booking.ItemTitle = booking.Summary;
                }

                if (string.IsNullOrWhiteSpace(booking.DateDescription) && TryGetString(bookingElement, "date_desc", out var dateDescription))
                {
                    booking.DateDescription = dateDescription!;
                }

                if (booking.Quantity == 0 && TryGetInt(bookingElement, "qty", out var qty))
                {
                    booking.Quantity = qty;
                }

                if (booking.TotalPax == 0 && TryGetInt(bookingElement, "total_pax", out var totalPax))
                {
                    booking.TotalPax = totalPax;
                }

                HydrateBookingItems(booking, bookingElement);
                HydrateBookingAttendees(booking);

                return booking;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse booking element.");
                return null;
            }
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        private void HydrateBookingItems(CheckfrontBooking booking, JsonElement bookingElement)
        {
            try
            {
                if (!bookingElement.TryGetProperty("items", out var itemsElement))
                {
                    return;
                }

                if (itemsElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var itemProperty in itemsElement.EnumerateObject())
                    {
                        if (itemProperty.Value.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        var item = ParseBookingItem(itemProperty.Value, itemProperty.Name);
                        if (item != null)
                        {
                            booking.Items.Add(item);
                        }
                    }
                    return;
                }

                if (itemsElement.ValueKind == JsonValueKind.Array)
                {
                    var index = 0;
                    foreach (var itemElement in itemsElement.EnumerateArray())
                    {
                        if (itemElement.ValueKind != JsonValueKind.Object)
                        {
                            index++;
                            continue;
                        }

                        var item = ParseBookingItem(itemElement, index.ToString(CultureInfo.InvariantCulture));
                        if (item != null)
                        {
                            booking.Items.Add(item);
                        }

                        index++;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse booking items.");
            }
        }

        /// <summary>
        /// Created: 2026-04-09 00:00 UTC
        /// </summary>
        private static CheckfrontBookingItem? ParseBookingItem(JsonElement itemElement, string fallbackKey)
        {
            var item = new CheckfrontBookingItem
            {
                Key = string.IsNullOrWhiteSpace(fallbackKey) ? string.Empty : fallbackKey,
                Name = FirstNonEmpty(
                    GetStringValue(itemElement, "name"),
                    GetStringValue(itemElement, "item_name"),
                    GetStringValue(itemElement, "itemName")) ?? string.Empty,
                Summary = FirstNonEmpty(
                    GetStringValue(itemElement, "summary"),
                    GetStringValue(itemElement, "itemSummary"),
                    GetStringValue(itemElement, "item_summary")) ?? string.Empty,
                StartDateRaw = FirstNonEmpty(
                    GetStringValue(itemElement, "start_date"),
                    GetStringValue(itemElement, "startDate")) ?? string.Empty,
                StartTimeRaw = FirstNonEmpty(
                    GetStringValue(itemElement, "start_time"),
                    GetStringValue(itemElement, "startTime")) ?? string.Empty,
                Quantity = TryGetFirstInt(itemElement, "qty", "quantity", "attendees", "guests"),
                Adults = TryGetFirstInt(itemElement, "adults", "adult"),
                Children = TryGetFirstInt(itemElement, "children", "child"),
                TotalPax = TryGetFirstInt(itemElement, "total_pax", "totalPax", "pax", "attendees", "guests")
            };

            HydrateBookingItemStartFieldsFromDateTime(
                item,
                FirstNonEmpty(
                    GetStringValue(itemElement, "start"),
                    GetStringValue(itemElement, "startAt"),
                    GetStringValue(itemElement, "checkIn"),
                    GetStringValue(itemElement, "check_in"),
                    GetStringValue(itemElement, "dateTime"),
                    GetStringValue(itemElement, "date_time")));

            return item;
        }

        /// <summary>
        /// Created: 2026-04-09 00:00 UTC
        /// </summary>
        private static void HydrateStartFieldsFromDateTime(CheckfrontBooking booking, string? rawDateTime)
        {
            if (booking == null || string.IsNullOrWhiteSpace(rawDateTime))
            {
                return;
            }

            if (!TryParseDateTimeOffset(rawDateTime, out var parsed))
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

        /// <summary>
        /// Created: 2026-04-09 00:00 UTC
        /// </summary>
        private static void HydrateBookingItemStartFieldsFromDateTime(CheckfrontBookingItem item, string? rawDateTime)
        {
            if (item == null || string.IsNullOrWhiteSpace(rawDateTime))
            {
                return;
            }

            if (!TryParseDateTimeOffset(rawDateTime, out var parsed))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(item.StartDateRaw))
            {
                item.StartDateRaw = parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            if (string.IsNullOrWhiteSpace(item.StartTimeRaw))
            {
                item.StartTimeRaw = parsed.ToString("HH:mm", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// Created: 2026-04-09 00:00 UTC
        /// </summary>
        private static bool TryParseDateTimeOffset(string rawValue, out DateTimeOffset parsed)
        {
            if (DateTimeOffset.TryParse(rawValue.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed))
            {
                return true;
            }

            if (DateTimeOffset.TryParse(rawValue.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out parsed))
            {
                return true;
            }

            return DateTimeOffset.TryParse(rawValue.Trim(), out parsed);
        }

        /// <summary>
        /// Created: 2026-04-09 00:00 UTC
        /// </summary>
        private static int TryGetFirstInt(JsonElement element, params string[] propertyNames)
        {
            foreach (var propertyName in propertyNames)
            {
                if (TryGetInt(element, propertyName, out var value) && value > 0)
                {
                    return value;
                }
            }

            return 0;
        }

        /// <summary>
        /// Created: 2026-04-09 00:00 UTC
        /// </summary>
        private static string? GetStringValue(JsonElement element, string propertyName)
        {
            return TryGetString(element, propertyName, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : null;
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        private void HydrateBookingAttendees(CheckfrontBooking booking)
        {
            try
            {
                if (booking.Items.Count > 0)
                {
                    var firstItem = booking.Items.FirstOrDefault();
                    if (firstItem != null)
                    {
                        if (string.IsNullOrWhiteSpace(booking.ItemName) && !string.IsNullOrWhiteSpace(firstItem.Name))
                        {
                            booking.ItemName = firstItem.Name;
                        }

                        if (string.IsNullOrWhiteSpace(booking.Summary) && !string.IsNullOrWhiteSpace(firstItem.Summary))
                        {
                            booking.Summary = firstItem.Summary;
                        }

                        if (string.IsNullOrWhiteSpace(booking.StartDateRaw) && !string.IsNullOrWhiteSpace(firstItem.StartDateRaw))
                        {
                            booking.StartDateRaw = firstItem.StartDateRaw;
                        }

                        if (string.IsNullOrWhiteSpace(booking.StartTimeRaw) && !string.IsNullOrWhiteSpace(firstItem.StartTimeRaw))
                        {
                            booking.StartTimeRaw = firstItem.StartTimeRaw;
                        }
                    }

                    booking.NumberOfAdults = booking.Items.Sum(i => i.Adults);
                    booking.NumberOfChildren = booking.Items.Sum(i => i.Children);
                    booking.NumberOfAttendees = booking.Items.Sum(i => i.TotalPax > 0 ? i.TotalPax : (i.Quantity > 0 ? i.Quantity : 0));
                }

                if (booking.NumberOfAttendees == 0 && booking.TotalPax > 0)
                {
                    booking.NumberOfAttendees = booking.TotalPax;
                }

                if (booking.NumberOfAttendees == 0 && booking.Quantity > 0)
                {
                    booking.NumberOfAttendees = booking.Quantity;
                }

                if (booking.NumberOfAttendees == 0 && (booking.NumberOfAdults > 0 || booking.NumberOfChildren > 0))
                {
                    booking.NumberOfAttendees = booking.NumberOfAdults + booking.NumberOfChildren;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to hydrate booking attendee totals.");
            }
        }

        /// <summary>
        /// Created: 2026-03-30 00:00 UTC
        /// Logs HTTP-level failures with endpoint + response preview for easier debugging.
        /// </summary>
        private async Task LogHttpFailureAsync(
            HttpResponseMessage response,
            string operation,
            string endpoint,
            string? contextValue = null)
        {
            var responseBody = await TryReadBodyForLogAsync(response);
            var endpointForLog = BuildEndpointForLog(endpoint);
            _logger.LogWarning(
                "Checkfront API HTTP failure. Operation={Operation} Endpoint={Endpoint} Context={Context} HttpStatus={StatusCode} Reason={ReasonPhrase} Body={Body}",
                operation,
                endpointForLog,
                contextValue ?? string.Empty,
                (int)response.StatusCode,
                response.ReasonPhrase ?? string.Empty,
                responseBody);
            WriteCheckfrontConsole(
                $"HTTP failure Operation={operation} Endpoint={endpointForLog} Context={contextValue ?? string.Empty} HttpStatus={(int)response.StatusCode} Reason={response.ReasonPhrase ?? string.Empty} Body={responseBody}");
        }

        /// <summary>
        /// Created: 2026-03-30 00:00 UTC
        /// </summary>
        private async Task<string> TryReadBodyForLogAsync(HttpResponseMessage response)
        {
            if (response.Content == null)
            {
                return string.Empty;
            }

            try
            {
                var body = await response.Content.ReadAsStringAsync();
                return body ?? string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to read Checkfront response body for log preview.");
                return "(body-unavailable)";
            }
        }

        /// <summary>
        /// Created: 2026-03-30 00:00 UTC
        /// </summary>
        private bool IsRequestStatusOk(
            string json,
            string operation,
            string endpoint,
            string? contextValue = null)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                if (!document.RootElement.TryGetProperty("request", out var requestElement))
                {
                    return true;
                }

                if (!requestElement.TryGetProperty("status", out var statusElement))
                {
                    return true;
                }

                var status = statusElement.GetString();
                if (string.IsNullOrWhiteSpace(status) ||
                    string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                var resource = requestElement.TryGetProperty("resource", out var resourceElement)
                    ? resourceElement.ToString()
                    : string.Empty;
                var requestId = requestElement.TryGetProperty("id", out var idElement)
                    ? idElement.ToString()
                    : string.Empty;
                var requestMessage = requestElement.TryGetProperty("message", out var messageElement)
                    ? messageElement.ToString()
                    : string.Empty;
                var endpointForLog = BuildEndpointForLog(endpoint);
                var responseBody = json ?? string.Empty;

                _logger.LogWarning(
                    "Checkfront API non-OK status. Operation={Operation} Endpoint={Endpoint} Context={Context} ApiStatus={ApiStatus} Resource={Resource} RequestId={RequestId} RequestMessage={RequestMessage} Body={Body}",
                    operation,
                    endpointForLog,
                    contextValue ?? string.Empty,
                    status,
                    resource,
                    requestId,
                    requestMessage,
                    responseBody);
                WriteCheckfrontConsole(
                    $"Non-OK API status Operation={operation} Endpoint={endpointForLog} Context={contextValue ?? string.Empty} ApiStatus={status} Resource={resource} RequestId={requestId} RequestMessage={requestMessage} Body={responseBody}");

                return false;
            }
            catch (Exception ex)
            {
                var endpointForLog = BuildEndpointForLog(endpoint);
                _logger.LogWarning(
                    ex,
                    "Failed to parse Checkfront request status metadata. Operation={Operation} Endpoint={Endpoint} Context={Context}",
                    operation,
                    endpointForLog,
                    contextValue ?? string.Empty);
                WriteCheckfrontConsole(
                    $"Status-parse warning Operation={operation} Endpoint={endpointForLog} Context={contextValue ?? string.Empty} Error={ex.Message}");
                return true;
            }
        }

        /// <summary>
        /// Created: 2026-03-30 00:00 UTC
        /// </summary>
        private string BuildEndpointForLog(string endpoint)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(endpoint))
                {
                    return ResolveActiveEndpoint();
                }

                if (Uri.TryCreate(endpoint, UriKind.Absolute, out var absoluteUri))
                {
                    return absoluteUri.ToString();
                }

                var activeEndpoint = ResolveActiveEndpoint();
                if (!string.IsNullOrWhiteSpace(activeEndpoint) &&
                    Uri.TryCreate(activeEndpoint, UriKind.Absolute, out var activeBaseUri))
                {
                    return new Uri(activeBaseUri, endpoint.TrimStart('/')).ToString();
                }

                if (_httpClient.BaseAddress != null)
                {
                    return new Uri(_httpClient.BaseAddress, endpoint.TrimStart('/')).ToString();
                }

                if (endpoint.StartsWith("/", StringComparison.Ordinal))
                {
                    return endpoint.TrimStart('/');
                }

                return endpoint;
            }
            catch
            {
                return endpoint;
            }
        }

        /// <summary>
        /// Created: 2026-03-30 00:00 UTC
        /// </summary>
        private string GetAuthModeForLog()
        {
            var auth = _httpClient.DefaultRequestHeaders.Authorization;
            if (auth == null || string.IsNullOrWhiteSpace(auth.Scheme))
            {
                return "None";
            }

            if (string.Equals(auth.Scheme, "Basic", StringComparison.OrdinalIgnoreCase))
            {
                return "BasicTokenPair";
            }

            if (string.Equals(auth.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
            {
                return "BearerOAuth2";
            }

            return auth.Scheme;
        }

        /// <summary>
        /// Created: 2026-03-30 00:00 UTC
        /// </summary>
        private static void WriteCheckfrontConsole(string message)
        {
            Console.WriteLine($"[CheckfrontApiDiag] {DateTime.UtcNow:O} {message}");
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        private static bool TryGetString(JsonElement element, string propertyName, out string? value)
        {
            value = null;
            if (!element.TryGetProperty(propertyName, out var property))
            {
                return false;
            }

            if (property.ValueKind == JsonValueKind.String)
            {
                value = property.GetString();
                return true;
            }

            value = property.ToString();
            return !string.IsNullOrWhiteSpace(value);
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        private static bool TryGetInt(JsonElement element, string propertyName, out int value)
        {
            value = 0;
            if (!element.TryGetProperty(propertyName, out var property))
            {
                return false;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value))
            {
                return true;
            }

            return property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out value);
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        private static bool TryGetLong(JsonElement element, string propertyName, out long value)
        {
            value = 0;
            if (!element.TryGetProperty(propertyName, out var property))
            {
                return false;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out value))
            {
                return true;
            }

            return property.ValueKind == JsonValueKind.String && long.TryParse(property.GetString(), out value);
        }

        /// <summary>
        /// Created: 2026-03-26 23:07 UTC
        /// </summary>
        private decimal CalculatePhoneMatchConfidence(string searchPhone, string customerPhone)
        {
            if (string.IsNullOrEmpty(searchPhone) || string.IsNullOrEmpty(customerPhone))
                return 0;

            var normalizedSearch = NormalizePhoneNumber(searchPhone);
            var normalizedCustomer = NormalizePhoneNumber(customerPhone);

            if (normalizedSearch == normalizedCustomer)
                return 95;

            if (normalizedSearch.Contains(normalizedCustomer) || normalizedCustomer.Contains(normalizedSearch))
                return 85;

            return 0;
        }

        /// <summary>
        /// Created: 2026-03-26 23:07 UTC
        /// </summary>
        private decimal CalculateNameMatchConfidence(string searchName, string customerName)
        {
            if (string.IsNullOrEmpty(searchName) || string.IsNullOrEmpty(customerName))
                return 0;

            var search = searchName.ToLowerInvariant().Trim();
            var customer = customerName.ToLowerInvariant().Trim();

            if (search == customer)
                return 90;

            if (search.Contains(customer) || customer.Contains(search))
                return 80;

            var searchWords = search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var customerWords = customer.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            
            var matchingWords = searchWords.Intersect(customerWords).Count();
            var totalWords = Math.Max(searchWords.Length, customerWords.Length);
            
            if (totalWords > 0)
            {
                var wordMatchPercentage = (decimal)matchingWords / totalWords;
                return Math.Max(60, wordMatchPercentage * 100);
            }

            return 0;
        }

        /// <summary>
        /// Created: 2026-03-26 23:07 UTC
        /// </summary>
        private string NormalizePhoneNumber(string phoneNumber)
        {
            if (string.IsNullOrEmpty(phoneNumber))
                return string.Empty;

            return System.Text.RegularExpressions.Regex.Replace(phoneNumber, @"[^\d+]", "");
        }
    }
}
