using System.Text.Json;
using System.Net.Http.Json;

namespace Email.Services;

// Public DTOs so interface can reference them (2025-12-09 00:00 UTC)
public record ApiSmsResponse(string Status, string? TextId, int? QuotaRemaining, string? Error);
public record ApiQuotaResponse(int QuotaRemaining);
public record ApiStatusResponse(string Status);

public interface ITextbeltApiService
{
    // 2025-12-09 00:00 UTC - Send SMS via local Textbelt controller
    Task<ApiSmsResponse?> SendSmsAsync(string phone, string message, string? displayName = null, string? webhookData = null);

    // 2025-12-09 00:00 UTC - Get remaining SMS quota
    Task<int?> GetQuotaAsync();

    // 2025-12-09 00:00 UTC - Get delivery status by textId
    Task<string?> GetStatusAsync(string textId);
}

public class TextbeltApiService : ITextbeltApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TextbeltApiService> _logger;
    private readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    // 2025-12-09 00:00 UTC - Use relative endpoints; host is the same site
    public TextbeltApiService(HttpClient httpClient, ILogger<TextbeltApiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ApiSmsResponse?> SendSmsAsync(string phone, string message, string? displayName = null, string? webhookData = null)
    {
        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["phone"] = phone,
                ["message"] = message,
                ["displayName"] = displayName
            };

            if (!string.IsNullOrWhiteSpace(webhookData))
            {
                payload["webhookData"] = webhookData;
            }

            var response = await _httpClient.PostAsJsonAsync("api/textbelt/send", payload);
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("SMS send failed: {Status} {Body}", response.StatusCode, json);
            }

            return JsonSerializer.Deserialize<ApiSmsResponse>(json, _jsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending SMS");
            return null;
        }
    }

    public async Task<int?> GetQuotaAsync()
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<ApiQuotaResponse>("api/textbelt/quota");
            return response?.QuotaRemaining;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting SMS quota");
            return null;
        }
    }

    public async Task<string?> GetStatusAsync(string textId)
    {
        try
        {
            var res = await _httpClient.GetFromJsonAsync<ApiStatusResponse>($"api/textbelt/status/{textId}");
            return res?.Status;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting SMS status");
            return null;
        }
    }
}

