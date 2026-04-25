using System;

namespace Email.Models
{
    /// <summary>
    /// Payload used to prefill manual booking form fields from a selected tour row.
    /// </summary>
    public sealed class BookingPrefillRequest
    {
        public Guid RequestId { get; set; } = Guid.NewGuid();
        public string? TourName { get; set; }
        public DateTime? TourDate { get; set; }
        public string? TourTime { get; set; }
    }
}
