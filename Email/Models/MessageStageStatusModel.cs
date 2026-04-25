using System;

namespace Email.Models
{
    // Created: 2025-12-09 00:00 UTC - DTO for per-stage message status (BookingMessageStatus)
    public class MessageStageStatusModel
    {
        public string MessageId { get; set; } = string.Empty;
        public string Stage { get; set; } = string.Empty;
        public bool SentFlag { get; set; }
        public DateTime? SentAtUtc { get; set; }
        public string? Channel { get; set; }
        public int? TemplateId { get; set; }
        public string? BookingCode { get; set; }
        public string? VendorName { get; set; }
    }
}


