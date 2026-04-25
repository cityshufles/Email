using System;

namespace Email.Models
{
    /// <summary>
    /// Created: 2026-02-14 - API-aligned model for VendorTours join table
    /// </summary>
    public class DbVendorTour
    {
        public int Id { get; set; }
        public int VendorId { get; set; }
        public string MasterTourName { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
