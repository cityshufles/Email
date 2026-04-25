using System;
using System.Collections.Generic;

namespace Email.Models
{
    /// <summary>
    /// Created: 12/19/2025 5:12 PM (local) | 2025-12-19T17:12:00
    /// Purpose: DTO for vCard email requests from the manual export UI.
    /// Notes: No API endpoints required; used directly by Blazor page/services.
    /// </summary>
    public sealed class VCardEmailRequest
    {
        public string ToAddress { get; set; } = string.Empty;
        public List<string> VCardTexts { get; set; } = new List<string>();
        public string? Subject { get; set; }
    }
}


