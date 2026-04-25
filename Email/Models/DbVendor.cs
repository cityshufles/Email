using System;

namespace Email.Models
{
    /// <summary>
    /// Created: 2026-02-14 - API-aligned model for Vendors CRUD
    /// </summary>
    public class DbVendor
    {
        public int Id { get; set; }
        public string VendorName { get; set; } = string.Empty;
        public string? AllToursLink { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
