// Created: 2025-11-19 00:00 UTC - Copied verbatim from CityShufflesWorkStation.Models.TourTreeEmailContent (namespace adjusted)
namespace Email.Models
{
    public class TourTreeEmailContent
    {
        public string MessageId { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string FromEmail { get; set; } = string.Empty;
        public string FromName { get; set; } = string.Empty;
        public string ToEmail { get; set; } = string.Empty;
        public DateTime ReceivedDate { get; set; }
        public string TextBody { get; set; } = string.Empty;
        public string HtmlBody { get; set; } = string.Empty;
        public int AttachmentCount { get; set; }
        public string AttachmentNames { get; set; } = string.Empty;
    }
}

