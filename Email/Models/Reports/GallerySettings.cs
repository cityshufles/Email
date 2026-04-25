using System;

namespace Email.Models.Reports
{
    /// <summary>
    /// Created: 1/28/2026
    /// Settings for the Public Tour Gallery templates.
    /// </summary>
    public class GallerySettings
    {
        public int Id { get; set; }
        
        /// <summary>
        /// HTML content for the "Thanks for joining us" section.
        /// Supports placeholder {TourName}.
        /// </summary>
        public string? HeaderText { get; set; }
        
        /// <summary>
        /// JSON array of FooterLink objects.
        /// </summary>
        public string? FooterLinksJson { get; set; }
        
        public DateTime UpdatedAt { get; set; }
    }

    public class FooterLink
    {
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string CardText { get; set; } = string.Empty;
    }
}
