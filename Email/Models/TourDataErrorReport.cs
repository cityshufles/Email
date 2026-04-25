using System;

namespace Email.Models
{
	/// <summary>
	/// Created: 2025-12-01 00:00 UTC - Minimal model for storing tour data accuracy reports.
	/// </summary>
	public class TourDataErrorReport
	{
		public int Id { get; set; }
		public int ProcessedEmailId { get; set; }
		public string MessageId { get; set; } = string.Empty;
		public string? VendorName { get; set; }
		public string? BookingCode { get; set; }
		public string? CustomerIdentifier { get; set; }
		public string? ErrorNotes { get; set; }
		public string OriginalSnapshotJson { get; set; } = string.Empty;
		public string? CorrectedSnapshotJson { get; set; }
		public DateTime CreatedAt { get; set; }
	}
}



