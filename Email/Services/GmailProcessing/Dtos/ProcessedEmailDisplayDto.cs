using System;

namespace Email.Services.GmailProcessing.Dtos
{
    /// <summary>
    /// Created: 2025-11-07 00:00 UTC
    /// Display shape for a processed email row.
    /// </summary>
    public sealed class ProcessedEmailDisplayDto
    {
        public int Id { get; set; }
        public int InboxEmailId { get; set; }
        public string MessageId { get; set; } = string.Empty;
        public string VendorName { get; set; } = string.Empty;
        public string EmailType { get; set; } = string.Empty;
        public string? CustomerName { get; set; }
        public string? CustomerEmail { get; set; }
        public string? CustomerPhone { get; set; }
        public string? BookingCode { get; set; }
        public string ProcessingStatus { get; set; } = "pending";
        public DateTime? ExtractedAt { get; set; }
        public bool IsLatestAction { get; set; }
        public DateTime? ProcessingCompletedAt { get; set; }
        public string? ProcessingError { get; set; }
    }
}


