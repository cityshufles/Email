using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace Email.Services;

// Created: 2025-12-10 - Direct TextBelt API service for simple SMS sending
public class TextBeltSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Sender { get; set; } = string.Empty;
}

public interface ITextBeltDirectService
{
    Task<TextBeltSendResponse?> SendSmsAsync(string phone, string message);
    Task<TextBeltQuotaResponse?> GetQuotaAsync();
}

public record TextBeltSendResponse(bool Success, string? TextId, int? QuotaRemaining, string? Error);
public record TextBeltQuotaResponse(bool Success, int? QuotaRemaining, string? Error);

public class TextBeltDirectService : ITextBeltDirectService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TextBeltDirectService> _logger;
    private readonly TextBeltSettings _settings;
    private readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    public TextBeltDirectService(
        HttpClient httpClient,
        IOptions<TextBeltSettings> settings,
        ILogger<TextBeltDirectService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
        
        // Set base address for TextBelt API
        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = new Uri("https://textbelt.com/");
        }
    }

    public async Task<TextBeltSendResponse?> SendSmsAsync(string phone, string message)
    {
        try
        {
            var payload = new Dictionary<string, string>
            {
                ["phone"] = phone,
                ["message"] = message,
                ["key"] = _settings.ApiKey
            };

            _logger.LogInformation("Sending SMS to {Phone} via TextBelt", phone);
            
            var response = await _httpClient.PostAsJsonAsync("text", payload);
            var json = await response.Content.ReadAsStringAsync();
            
            _logger.LogInformation("TextBelt response: {StatusCode} {Body}", response.StatusCode, json);

            var textbeltResponse = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, _jsonOpts);
            
            if (textbeltResponse == null)
            {
                return new TextBeltSendResponse(false, null, null, "Invalid response from TextBelt");
            }

            var success = textbeltResponse.ContainsKey("success") && textbeltResponse["success"].GetBoolean();
            var textId = textbeltResponse.ContainsKey("textId") ? textbeltResponse["textId"].GetString() : null;
            var quotaRemaining = textbeltResponse.ContainsKey("quotaRemaining") ? (int?)textbeltResponse["quotaRemaining"].GetInt32() : null;
            var error = textbeltResponse.ContainsKey("error") ? textbeltResponse["error"].GetString() : null;

            return new TextBeltSendResponse(success, textId, quotaRemaining, error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending SMS via TextBelt");
            return new TextBeltSendResponse(false, null, null, ex.Message);
        }
    }

    public async Task<TextBeltQuotaResponse?> GetQuotaAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"quota/{_settings.ApiKey}");
            var json = await response.Content.ReadAsStringAsync();
            
            _logger.LogInformation("TextBelt quota response: {StatusCode} {Body}", response.StatusCode, json);

            var textbeltResponse = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, _jsonOpts);
            
            if (textbeltResponse == null)
            {
                return new TextBeltQuotaResponse(false, null, "Invalid response from TextBelt");
            }

            var success = textbeltResponse.ContainsKey("success") && textbeltResponse["success"].GetBoolean();
            var quotaRemaining = textbeltResponse.ContainsKey("quotaRemaining") ? (int?)textbeltResponse["quotaRemaining"].GetInt32() : null;
            var error = textbeltResponse.ContainsKey("error") ? textbeltResponse["error"].GetString() : null;

            return new TextBeltQuotaResponse(success, quotaRemaining, error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting TextBelt quota");
            return new TextBeltQuotaResponse(false, null, ex.Message);
        }
    }
}

