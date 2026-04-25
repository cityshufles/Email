using Email.Models;

namespace Email.Services
{
	/// <summary>
	/// Created: 2025-11-20 (time not specified) - SQL-backed contract for default guide scheduling.
	/// </summary>
	public interface ITourGuideDefaultsApiService
	{
		Task<List<TourGuideDefault>> GetByTourAsync(int tourId, CancellationToken ct = default);
		Task<TourGuideDefault?> ResolveAsync(int tourId, int dayOfWeek, string startTime, CancellationToken ct = default);
		Task<TourGuideDefault?> CreateAsync(TourGuideDefault row, CancellationToken ct = default);
		Task<bool> UpdateAsync(int id, TourGuideDefault row, CancellationToken ct = default);
		Task<bool> DeleteAsync(int id, CancellationToken ct = default);
	}
}


