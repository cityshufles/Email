using System;

namespace Email.Services.GmailProcessing.Dtos
{
	/// <summary>
	/// Created: 2025-11-28 00:00 UTC
	/// Aggregated count of inbox emails by ProcessingStatus (SMALLINT).
	/// </summary>
	public sealed class InboxProcessingStatusCountDto
	{
		public short Status { get; set; }
		public long Count { get; set; }
	}
}


