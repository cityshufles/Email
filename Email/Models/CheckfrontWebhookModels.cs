using System.Text.Json.Serialization;

namespace Email.Models
{
    public class WebhookLogInfo
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string EventType { get; set; } = string.Empty;
        public string HttpMethod { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
        public string RawPayload { get; set; } = string.Empty;
        public string Headers { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string UserAgent { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
        public string BookingCode { get; set; } = string.Empty;
        public string? CustomerEmail { get; set; }
        public bool IsValid { get; set; }
        public string ProcessingStatus { get; set; } = string.Empty;
        public int ResponseStatusCode { get; set; }
        public string ResponseMessage { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public int ProcessingTimeMs { get; set; }
    }

    public class WebhookLogResponse
    {
        public List<WebhookLogInfo> Logs { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public bool HasMore { get; set; }
    }

    public class WebhookStats
    {
        public int TotalWebhooks { get; set; }
        public int SuccessfulWebhooks { get; set; }
        public int FailedWebhooks { get; set; }
        public double SuccessRate { get; set; }
        public int AverageProcessingTime { get; set; }
        public List<WebhookLogInfo> RecentActivity { get; set; } = new();
        public List<WebhookEventTypeStats> EventTypes { get; set; } = new();
    }

    public class WebhookEventTypeStats
    {
        public string EventType { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class CheckfrontWebhookPayload
    {
        [JsonPropertyName("booking")]
        public CheckfrontWebhookBooking? Booking { get; set; }
    }

    public class CheckfrontWebhookBooking
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("customer")]
        public CheckfrontWebhookCustomer? Customer { get; set; }
    }

    public class CheckfrontWebhookCustomer
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;
    }
}
