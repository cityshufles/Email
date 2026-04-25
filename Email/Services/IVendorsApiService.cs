using Email.Models;

namespace Email.Services
{
    /// <summary>
    /// Created: 2026-02-14 - SQL-backed contract for Vendors and VendorTours CRUD.
    /// </summary>
    public interface IVendorsApiService
    {
        string? LastError { get; }

        Task<List<DbVendor>> GetVendorsAsync(CancellationToken ct = default);
        Task<DbVendor?> CreateVendorAsync(DbVendor vendor, CancellationToken ct = default);
        Task<bool> UpdateVendorAsync(int id, DbVendor vendor, CancellationToken ct = default);
        Task<bool> DeleteVendorAsync(int id, CancellationToken ct = default);

        Task<List<DbVendorTour>> GetVendorToursAsync(int? vendorId = null, CancellationToken ct = default);
        Task<bool> SetVendorTourActiveAsync(int vendorId, string masterTourName, bool isActive, CancellationToken ct = default);
    }
}
