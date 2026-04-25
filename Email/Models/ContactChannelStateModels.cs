using System;

namespace Email.Models
{
    // 2026-03-13 - Composite key for booking-level channel state lookups.
    public sealed class ContactChannelStateKey
    {
        public string MessageId { get; set; } = string.Empty;
        public string BookingCode { get; set; } = string.Empty;
    }

    // 2026-03-13 - Persisted per-channel contact outcome state with legacy mirror fields.
    public sealed class ContactChannelStateResult
    {
        public string MessageId { get; set; } = string.Empty;
        public string BookingCode { get; set; } = string.Empty;
        public string Channel { get; set; } = string.Empty;
        public byte ContactState { get; set; }
        public bool LegacyMessageSent { get; set; }
        public DateTime? LegacyMessageSentAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public string? UpdatedBy { get; set; }
        public string? UpdatedSource { get; set; }
    }
}
