// Created: 2026-02-11 11:55 UTC
// Purpose: Interface for managing tour name variant mappings to master tours

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Email.Services
{
    /// <summary>
    /// Service for managing tour name variant mappings
    /// </summary>
    public interface ITourNameMappingService
    {
        /// <summary>
        /// Get all mappings for a specific tour
        /// </summary>
        Task<List<TourNameMapping>> GetMappingsForTourAsync(int tourId);

        /// <summary>
        /// Get all active mappings (for cache building)
        /// </summary>
        Task<List<TourNameMapping>> GetAllActiveMappingsAsync();

        /// <summary>
        /// Add a new tour name variant mapping
        /// </summary>
        /// <returns>The ID of the created mapping</returns>
        Task<int> AddMappingAsync(int tourId, string incomingName, string? vendorName = null);

        /// <summary>
        /// Delete (deactivate) a tour name mapping
        /// </summary>
        Task<bool> DeleteMappingAsync(int id);

        /// <summary>
        /// Mark a mapping as most recently used/preferred by updating UpdatedAt.
        /// </summary>
        Task<bool> TouchMappingAsync(int id);

        /// <summary>
        /// Build a dictionary of normalized names to TourIds for normalization logic
        /// </summary>
        Task<Dictionary<string, int>> BuildNormalizationDictionaryAsync();
    }

    /// <summary>
    /// Tour name mapping model
    /// </summary>
    public class TourNameMapping
    {
        public int Id { get; set; }
        public int TourId { get; set; }
        public string IncomingTourName { get; set; } = string.Empty;
        public string? VendorName { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
