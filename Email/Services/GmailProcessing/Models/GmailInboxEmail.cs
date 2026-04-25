using System;

namespace Email.Services.GmailProcessing.Models
{
    /// <summary>
    /// Created: 2025-11-07 00:00 UTC
    /// Local representation for inbox emails (processing scope).
    /// Note: v2 also contains gmailcollection InboxEmailRecord; this model is reserved for processing isolation if needed.
    /// </summary>
    public sealed class GmailInboxEmail
    {
        public int Id { get; set; }
        public string MessageId { get; set; } = string.Empty;
        public string? Subject { get; set; }
        public string FromEmail { get; set; } = string.Empty;
        public string? FromName { get; set; }
        public DateTime? ReceivedDate { get; set; }
        public string? HtmlBody { get; set; }
        public string? TextBody { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}


