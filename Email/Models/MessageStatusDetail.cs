using System;

namespace Email.Models
{
    /// <summary>
    /// Created: 2025-12-09 - DTO for message status with booking details
    /// </summary>
    public class MessageStatusDetail
    {
        public int Id { get; set; }
        public string MessageId { get; set; } = string.Empty;
        public string Stage { get; set; } = string.Empty;
        public bool SentFlag { get; set; }
        public DateTime? SentAtUtc { get; set; }
        public string? Notes { get; set; }
        public string? CustomerName { get; set; }
        public string? TourName { get; set; }
        public DateTime? TourDate { get; set; }
        public string? BookingCode { get; set; }
        public string? VendorName { get; set; }

        public string SentAtDisplay
        {
            get
            {
                if (!SentAtUtc.HasValue) return string.Empty;
                
                // Convert UTC to EST (Eastern Standard Time)
                // Note: FindSystemTimeZoneById("Eastern Standard Time") handles both EST and EDT correctly
                var estZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                var estTime = TimeZoneInfo.ConvertTimeFromUtc(SentAtUtc.Value, estZone);
                
                return $"{estTime:MM/dd/yyyy h:mm tt} (EST) {SentAtUtc.Value:h:mm tt} (UTC)";
            }
        }
    }
}

