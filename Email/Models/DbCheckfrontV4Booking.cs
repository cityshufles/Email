using System;

namespace Email.Models
{
    /// <summary>
    /// Created: 2026-04-03 00:00 UTC
    /// Storage model for Checkfront v4 bookings (full snapshot + key extracted fields).
    /// </summary>
    public sealed class DbCheckfrontV4Booking
    {
        public int Id { get; set; }
        public string CheckfrontBookingId { get; set; } = string.Empty;
        public string BookingCode { get; set; } = string.Empty;
        public DateTimeOffset? CreatedAtLocal { get; set; }
        public DateTimeOffset? StartAtLocal { get; set; }
        public DateTimeOffset? EndAtLocal { get; set; }
        public DateTimeOffset? CheckInAtLocal { get; set; }
        public DateTimeOffset? CheckOutAtLocal { get; set; }
        public int? CustomerId { get; set; }
        public string? CustomerCode { get; set; }
        public string? CustomerFirstName { get; set; }
        public string? CustomerLastName { get; set; }
        public string? CustomerEmail { get; set; }
        public string? CustomerPhone { get; set; }
        public string? Language { get; set; }
        public decimal? SubTotal { get; set; }
        public decimal? InclusiveTaxTotal { get; set; }
        public decimal? TaxTotal { get; set; }
        public decimal? Total { get; set; }
        public decimal? PaidTotal { get; set; }
        public string? StatusId { get; set; }
        public string? StatusName { get; set; }
        public string? ItemSummary { get; set; }
        public string? DiscountCode { get; set; }
        public int? AccountId { get; set; }
        public string? PartnerId { get; set; }
        public string? Cfx { get; set; }
        public string? Gcfx { get; set; }
        public string? FieldsJson { get; set; }
        public string? StatusJson { get; set; }
        public string? CustomerJson { get; set; }
        public string? NotesJson { get; set; }
        public string SnapshotJson { get; set; } = string.Empty;
        public string? SourceEndpoint { get; set; }
        public DateTime PulledAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
}
