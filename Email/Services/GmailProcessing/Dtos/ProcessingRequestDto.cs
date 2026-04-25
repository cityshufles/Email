using System;
using System.Collections.Generic;

namespace Email.Services.GmailProcessing.Dtos
{
    /// <summary>
    /// Created: 2025-11-07 00:00 UTC
    /// Request for processing specific inbox email ids.
    /// </summary>
    public sealed class ProcessingRequestDto
    {
        public List<int> InboxEmailIds { get; set; } = new();
    }
}


