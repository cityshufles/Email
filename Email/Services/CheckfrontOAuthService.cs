using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Email.Models;
using Microsoft.AspNetCore.Http;

namespace Email.Services
{
    /// <summary>
    /// Created: 2026-03-29 00:00 UTC
    /// OAuth2 helper for Checkfront authorize/code/token/refresh flow.
    /// Stores tokens in-memory for current app lifetime.
    /// </summary>
    public sealed class CheckfrontOAuthService
    {
        private readonly HttpClient _httpClient;
        private readonly CheckfrontConfig _config;
        private readonly ILogger<CheckfrontOAuthService> _logger;
        private readonly SemaphoreSlim _refreshGate = new(1, 1);

        private string _accessToken = string.Empty;
        private string _refreshToken = string.Empty;
        private DateTimeOffset? _accessTokenExpiresAtUtc;

        public CheckfrontOAuthService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<CheckfrontOAuthService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;

            _config = new CheckfrontConfig();
            configuration.GetSection("Checkfront").Bind(_config);

            var oauth = _config.OAuth2 ?? new CheckfrontOAuth2Config();
            _accessToken = oauth.AccessToken?.Trim() ?? string.Empty;
            _refreshToken = oauth.RefreshToken?.Trim() ?? string.Empty;

            if (DateTimeOffset.TryParse(
                    oauth.AccessTokenExpiresAtUtc,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsedExpiry))
            {
                _accessTokenExpiresAtUtc = parsedExpiry;
            }
        }

        public bool IsOAuthConfigured =>
            !string.IsNullOrWhiteSpace(_config.OAuth2?.AuthorizeUrl) &&
            !string.IsNullOrWhiteSpace(_config.OAuth2?.TokenUrl) &&
            !string.IsNullOrWhiteSpace(_config.OAuth2?.ConsumerKey) &&
            !string.IsNullOrWhiteSpace(_config.OAuth2?.ConsumerSecret);

        public string ResolveCallbackUrl(HttpRequest request)
        {
            var configured = _config.OAuth2?.CallbackUrl?.Trim();
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            return $"{request.Scheme}://{request.Host}/checkfront/oauth/callback";
        }

        public string BuildAuthorizeUrl(string callbackUrl, string state)
        {
            if (!IsOAuthConfigured)
            {
                throw new InvalidOperationException("Checkfront OAuth2 is not configured.");
            }

            if (string.IsNullOrWhiteSpace(callbackUrl))
            {
                throw new InvalidOperationException("CallbackUrl is required for OAuth2 authorize flow.");
            }

            var authorizeUrl = _config.OAuth2!.AuthorizeUrl.Trim();
            var separator = authorizeUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            return string.Concat(
                authorizeUrl,
                separator,
                "client_id=", Uri.EscapeDataString(_config.OAuth2.ConsumerKey.Trim()),
                "&response_type=code",
                "&redirect_uri=", Uri.EscapeDataString(callbackUrl.Trim()),
                "&state=", Uri.EscapeDataString(state ?? string.Empty));
        }

        public CheckfrontOAuthStatus GetStatus()
        {
            var now = DateTimeOffset.UtcNow;
            var expiresIn = _accessTokenExpiresAtUtc.HasValue
                ? (int?)Math.Max(0, (_accessTokenExpiresAtUtc.Value - now).TotalSeconds)
                : null;

            return new CheckfrontOAuthStatus
            {
                IsOAuthConfigured = IsOAuthConfigured,
                ApplicationName = _config.OAuth2?.ApplicationName ?? string.Empty,
                AuthorizeUrl = _config.OAuth2?.AuthorizeUrl ?? string.Empty,
                TokenUrl = _config.OAuth2?.TokenUrl ?? string.Empty,
                CallbackUrl = _config.OAuth2?.CallbackUrl ?? string.Empty,
                HasAccessToken = !string.IsNullOrWhiteSpace(_accessToken),
                HasRefreshToken = !string.IsNullOrWhiteSpace(_refreshToken),
                AccessTokenExpiresAtUtc = _accessTokenExpiresAtUtc,
                AccessTokenExpiresInSeconds = expiresIn,
                AccessTokenPreview = BuildTokenPreview(_accessToken),
                RefreshTokenPreview = BuildTokenPreview(_refreshToken)
            };
        }

        public async Task<CheckfrontOAuthTokenSnapshot> ExchangeCodeAsync(
            string code,
            string callbackUrl,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new InvalidOperationException("OAuth2 code is required.");
            }

            var form = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code.Trim(),
                ["redirect_uri"] = callbackUrl.Trim()
            };

            return await RequestAndStoreTokenAsync(form, cancellationToken);
        }

        public async Task<CheckfrontOAuthTokenSnapshot> RefreshAccessTokenAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_refreshToken))
            {
                throw new InvalidOperationException("Refresh token is not available.");
            }

            var form = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = _refreshToken
            };

            return await RequestAndStoreTokenAsync(form, cancellationToken);
        }

        public async Task<string?> GetValidAccessTokenAsync(CancellationToken cancellationToken)
        {
            if (!IsOAuthConfigured)
            {
                return null;
            }

            if (HasUsableAccessToken())
            {
                return _accessToken;
            }

            if (string.IsNullOrWhiteSpace(_refreshToken))
            {
                return string.IsNullOrWhiteSpace(_accessToken) ? null : _accessToken;
            }

            await _refreshGate.WaitAsync(cancellationToken);
            try
            {
                if (HasUsableAccessToken())
                {
                    return _accessToken;
                }

                try
                {
                    var refreshed = await RefreshAccessTokenAsync(cancellationToken);
                    return refreshed.AccessToken;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Checkfront OAuth2 refresh failed.");
                    return string.IsNullOrWhiteSpace(_accessToken) ? null : _accessToken;
                }
            }
            finally
            {
                _refreshGate.Release();
            }
        }

        private bool HasUsableAccessToken()
        {
            if (string.IsNullOrWhiteSpace(_accessToken))
            {
                return false;
            }

            if (!_accessTokenExpiresAtUtc.HasValue)
            {
                return true;
            }

            return _accessTokenExpiresAtUtc.Value > DateTimeOffset.UtcNow.AddSeconds(60);
        }

        private async Task<CheckfrontOAuthTokenSnapshot> RequestAndStoreTokenAsync(
            Dictionary<string, string> formFields,
            CancellationToken cancellationToken)
        {
            if (!IsOAuthConfigured)
            {
                throw new InvalidOperationException("Checkfront OAuth2 is not configured.");
            }

            var oauth = _config.OAuth2!;
            var tokenUrl = oauth.TokenUrl?.Trim();
            if (string.IsNullOrWhiteSpace(tokenUrl))
            {
                throw new InvalidOperationException("Checkfront OAuth2 TokenUrl is not configured.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl);
            var basicAuthRaw = $"{oauth.ConsumerKey}:{oauth.ConsumerSecret}";
            var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes(basicAuthRaw));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
            request.Content = new FormUrlEncodedContent(formFields);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Token request failed: {(int)response.StatusCode} {response.ReasonPhrase}; Body={TrimForLog(body)}");
            }

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (!TryGetString(root, "access_token", out var accessToken) || string.IsNullOrWhiteSpace(accessToken))
            {
                throw new InvalidOperationException("Token response missing access_token.");
            }

            var refreshToken = _refreshToken;
            if (TryGetString(root, "refresh_token", out var responseRefreshToken) &&
                !string.IsNullOrWhiteSpace(responseRefreshToken))
            {
                refreshToken = responseRefreshToken.Trim();
            }

            var tokenType = "bearer";
            if (TryGetString(root, "token_type", out var responseTokenType) &&
                !string.IsNullOrWhiteSpace(responseTokenType))
            {
                tokenType = responseTokenType.Trim();
            }

            var expiresInSeconds = TryGetInt(root, "expires_in") ?? 0;
            DateTimeOffset? expiresAtUtc = null;
            if (expiresInSeconds > 0)
            {
                expiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds);
            }

            _accessToken = accessToken.Trim();
            _refreshToken = refreshToken;
            _accessTokenExpiresAtUtc = expiresAtUtc;

            return new CheckfrontOAuthTokenSnapshot
            {
                AccessToken = _accessToken,
                RefreshToken = _refreshToken,
                TokenType = tokenType,
                ExpiresInSeconds = expiresInSeconds,
                AccessTokenExpiresAtUtc = _accessTokenExpiresAtUtc
            };
        }

        private static int? TryGetInt(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var intValue))
            {
                return intValue;
            }

            if (value.ValueKind == JsonValueKind.String &&
                int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var stringValue))
            {
                return stringValue;
            }

            return null;
        }

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

            if (property.ValueKind == JsonValueKind.Number ||
                property.ValueKind == JsonValueKind.True ||
                property.ValueKind == JsonValueKind.False)
            {
                value = property.ToString();
                return true;
            }

            return false;
        }

        private static string BuildTokenPreview(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return string.Empty;
            }

            if (token.Length <= 12)
            {
                return $"{token[..3]}...{token[^3..]}";
            }

            return $"{token[..6]}...{token[^6..]}";
        }

        private static string TrimForLog(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return string.Empty;
            }

            return body.Length <= 600 ? body : body[..600];
        }
    }

    /// <summary>
    /// Created: 2026-03-29 00:00 UTC
    /// Ephemeral OAuth state manager for authorization redirect flow.
    /// </summary>
    public sealed class CheckfrontOAuthStateStore
    {
        private readonly ConcurrentDictionary<string, DateTimeOffset> _issuedStates = new(StringComparer.Ordinal);

        public string Issue()
        {
            CleanupExpired();
            var bytes = RandomNumberGenerator.GetBytes(24);
            var state = Convert.ToBase64String(bytes)
                .Replace("+", "-", StringComparison.Ordinal)
                .Replace("/", "_", StringComparison.Ordinal)
                .TrimEnd('=');

            _issuedStates[state] = DateTimeOffset.UtcNow.AddMinutes(10);
            return state;
        }

        public bool TryConsume(string? state)
        {
            if (string.IsNullOrWhiteSpace(state))
            {
                return false;
            }

            if (!_issuedStates.TryRemove(state, out var expiresAt))
            {
                return false;
            }

            return expiresAt > DateTimeOffset.UtcNow;
        }

        private void CleanupExpired()
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var kv in _issuedStates)
            {
                if (kv.Value <= now)
                {
                    _issuedStates.TryRemove(kv.Key, out _);
                }
            }
        }
    }

    public sealed class CheckfrontOAuthStatus
    {
        public bool IsOAuthConfigured { get; set; }
        public string ApplicationName { get; set; } = string.Empty;
        public string AuthorizeUrl { get; set; } = string.Empty;
        public string TokenUrl { get; set; } = string.Empty;
        public string CallbackUrl { get; set; } = string.Empty;
        public bool HasAccessToken { get; set; }
        public bool HasRefreshToken { get; set; }
        public DateTimeOffset? AccessTokenExpiresAtUtc { get; set; }
        public int? AccessTokenExpiresInSeconds { get; set; }
        public string AccessTokenPreview { get; set; } = string.Empty;
        public string RefreshTokenPreview { get; set; } = string.Empty;
    }

    public sealed class CheckfrontOAuthTokenSnapshot
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public string TokenType { get; set; } = "bearer";
        public int ExpiresInSeconds { get; set; }
        public DateTimeOffset? AccessTokenExpiresAtUtc { get; set; }
    }
}
