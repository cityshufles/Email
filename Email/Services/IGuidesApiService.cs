using Email.Models;

namespace Email.Services
{
	/// <summary>
	/// Created: 2025-11-20 (time not specified) - SQL-backed contract for Guides CRUD.
	/// </summary>
	public interface IGuidesApiService
	{
		Task<List<DbGuide>> GetGuidesAsync(CancellationToken ct = default);
		Task<DbGuide?> CreateGuideAsync(DbGuide guide, CancellationToken ct = default);
		Task<bool> UpdateGuideAsync(int id, DbGuide guide, CancellationToken ct = default);
		Task<bool> DeleteGuideAsync(int id, CancellationToken ct = default);
		Task UpdateGuideProfileAsync(int guideId, string email, string phone, string? imagePath);
	}
}


