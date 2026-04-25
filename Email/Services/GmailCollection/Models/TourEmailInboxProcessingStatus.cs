using System;

namespace Email.Services.GmailCollection.Models
{
	/// <summary>
	/// Created: 2025-11-28 00:00 UTC
	/// Inbox processing status; maps to SMALLINT [dbo].[AutomaticGmail_InboxEmails].[ProcessingStatus].
	/// </summary>
	public enum TourEmailInboxProcessingStatus : short
	{
		Processed = 0,
		NonBooking = 1,
		Failed = 2,
		Pending = 3
	}
}


