using Email.Models;

namespace Email.Services
{
	/// <summary>
	/// Created: 2025-11-20 (time not specified) - SQL-backed contract for TourMessages CRUD.
	/// </summary>
	public interface ITourMessagesApiService
	{
		string? LastError { get; }
		Task<List<DbTourMessage>> GetTourMessagesAsync(CancellationToken ct = default);
		Task<DbTourMessage?> GetTourMessageByIdAsync(int id, CancellationToken ct = default);
		Task<DbTourMessage?> CreateTourMessageAsync(DbTourMessage message, CancellationToken ct = default);
		Task<bool> UpdateTourMessageAsync(int id, DbTourMessage message, CancellationToken ct = default);
		Task<bool> DeleteTourMessageAsync(int id, CancellationToken ct = default);
	}
}


