// Created: 2026-02-12 00:00 UTC
// Purpose: Contract for master tour CRUD and variant management.

using Email.Models;

namespace Email.Services
{
    public interface IMasterTourService
    {
        Task<List<DbTour>> GetAllMasterToursAsync();
        Task<DbTour?> GetMasterTourByIdAsync(int tourId);
        Task<int> SaveMasterTourAsync(DbTour tour);
        Task<bool> DeleteMasterTourAsync(int tourId);

        Task<List<TourNameMapping>> GetTourVariantsAsync(int tourId);
        Task<int> AddTourVariantAsync(TourNameMapping mapping);
        Task<bool> DeleteTourVariantAsync(int mappingId);

        Task<List<MasterTourTimeSlot>> GetAvailableTourTimeSlotsAsync(int tourId);
        Task<List<string>> GetDistinctVendorsAsync();
    }
}
