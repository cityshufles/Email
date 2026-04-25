using System;

namespace Email.Services.GmailProcessing.Dtos
{
    /// <summary>
    /// Created: 2025-11-07 00:00 UTC
    /// Grid row shape for unprocessed inbox emails.
    /// </summary>
    public sealed class UnprocessedEmailRowDto
    {
        public int Id { get; set; }
        public string MessageId { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string FromEmail { get; set; } = string.Empty;
        public DateTime ReceivedDate { get; set; }
    }
}


