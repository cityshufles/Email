using System;
using Email.Services.GmailCollection.Models;
using Email.Services.GmailProcessing.Models;

namespace Email.Services.GmailProcessing.Dtos
{
	/// <summary>
	/// Created: 2025-11-29 00:00 UTC
	/// Diagnostic composite view for a single email, hydrated by MessageId.
	/// </summary>
	public sealed class HydratedEmailDiagnosticContext
	{
		/// <summary>Inbox row diagnostic snapshot</summary>
		public InboxEmailRecord? InboxDiagnostic { get; set; }

		/// <summary>Processed row diagnostic snapshot (latest by completion time)</summary>
		public ProcessedEmailRecord? ProcessedDiagnostic { get; set; }

		/// <summary>Booking row diagnostic snapshot (by MessageId first, then by BookingCode fallback)</summary>
		public Booking? BookingDiagnostic { get; set; }

		/// <summary>Customer row diagnostic snapshot (from Booking.CustomerId)</summary>
		public Customer? CustomerDiagnostic { get; set; }
	}
}


