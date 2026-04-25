using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Email.Services;
using Email.Models;

namespace Email.Controllers;

[ApiController]
[Route("api/textbelt")]
public class TextbeltLocalController : ControllerBase
{
    private const string ApiKeyHeader = "X-Local-Api-Key";
    private readonly ITextbeltApiService _textbeltService;
    private readonly ILogger<TextbeltLocalController> _logger;
    private readonly LocalApiAuthSettings _authSettings;

    // 2025-12-09 00:00 UTC - Local Textbelt endpoints secured by LocalApiAuth:TextApiKey
    public TextbeltLocalController(
        ITextbeltApiService textbeltService,
        IOptions<LocalApiAuthSettings> authOptions,
        ILogger<TextbeltLocalController> logger)
    {
        _textbeltService = textbeltService;
        _logger = logger;
        _authSettings = authOptions.Value;
    }

    public class SendSmsRequest
    {
        public string Phone { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string? WebhookData { get; set; }
    }

    [HttpPost("send")]
    public async Task<IActionResult> Send([FromBody] SendSmsRequest request)
    {
        // 2025-12-09 00:00 UTC - Validate API key and dispatch SMS
        if (!IsAuthorized())
        {
            return Unauthorized(new { error = "Invalid or missing API key" });
        }

        if (string.IsNullOrWhiteSpace(request.Phone) || string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new { error = "Phone and message are required" });
        }

        var result = await _textbeltService.SendSmsAsync(request.Phone, request.Message, request.DisplayName, request.WebhookData);
        if (result == null)
        {
            return StatusCode(500, new { status = "failed", error = "Unable to send SMS" });
        }

        if (string.Equals(result.Status, "sent", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(result.Status, "success", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(new { status = result.Status, textId = result.TextId, quotaRemaining = result.QuotaRemaining });
        }

        return StatusCode(500, new { status = result.Status, error = result.Error ?? "Unknown error" });
    }

    [HttpGet("quota")]
    public async Task<IActionResult> GetQuota()
    {
        // 2025-12-09 00:00 UTC - Return remaining quota
        if (!IsAuthorized())
        {
            return Unauthorized(new { error = "Invalid or missing API key" });
        }

        var quota = await _textbeltService.GetQuotaAsync();
        if (quota == null)
        {
            return StatusCode(500, new { error = "Unable to retrieve quota" });
        }

        return Ok(new { quotaRemaining = quota });
    }

    [HttpGet("status/{textId}")]
    public async Task<IActionResult> GetStatus(string textId)
    {
        // 2025-12-09 00:00 UTC - Return SMS delivery status
        if (!IsAuthorized())
        {
            return Unauthorized(new { error = "Invalid or missing API key" });
        }

        var status = await _textbeltService.GetStatusAsync(textId);
        if (status == null)
        {
            return StatusCode(500, new { error = "Unable to retrieve status" });
        }

        return Ok(new { status });
    }

    private bool IsAuthorized()
    {
        var provided = Request.Headers[ApiKeyHeader].FirstOrDefault() ?? Request.Query["key"].FirstOrDefault();
        var expected = _authSettings.TextApiKey;
        return !string.IsNullOrWhiteSpace(expected) && string.Equals(provided, expected, StringComparison.Ordinal);
    }
}

