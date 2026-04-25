using System;

namespace Email.Models
{
    // Created: 2025-12-09 00:00 UTC - Projection for message history grid
    public class MessageHistoryItem
    {
        public string MessageId { get; set; } = string.Empty;
        public string BookingCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerPhone { get; set; } = string.Empty;
        public string TourName { get; set; } = string.Empty;
        public DateTime? TourDate { get; set; }
        public string? DisplayDate { get; set; }
        public string? DisplayTime { get; set; }
        public string VendorName { get; set; } = string.Empty;
        public string Stage { get; set; } = string.Empty;
        public string Channel { get; set; } = string.Empty;
        public bool SentFlag { get; set; }
        public DateTime? SentAtUtc { get; set; }
        public int? TemplateId { get; set; }
        public string? TemplateName { get; set; }
    }
}


