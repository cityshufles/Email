using Email.Models;

namespace Email.Services
{
	/// <summary>
	/// Created: 2025-11-20 (time not specified) - SQL-backed contract for Tours CRUD.
	/// </summary>
	public interface IToursApiService
	{
		        /// <summary>
        /// Saves or updates a tour schedule.
        /// </summary>
        Task<int> SaveTourScheduleAsync(DbTourSchedule schedule);

        /// <summary>
        /// Generates actual TourGuideAssignment records based on the abstract schedule rules.
        /// </summary>
        Task GenerateAssignmentsFromScheduleAsync(DbTourSchedule schedule);

        /// <summary>
        /// Gets all schedules for a specific tour.
        /// </summary>
        Task<List<DbTourSchedule>> GetTourSchedulesAsync(int tourId);

        // 2026-02-03
        Task<List<DbTourSchedule>> GetAllTourSchedulesAsync(CancellationToken ct = default);

        // 2026-02-05 - Conflict Detection
        Task<List<string>> CheckScheduleConflictsAsync(DbTourSchedule schedule);
        
        // 2026-02-05 - Detailed conflict check (Standard vs Override)
        Task<(bool hasStandardConflict, bool hasOverrideConflict, List<string> conflictNames)> CheckScheduleConflictsDetailedAsync(DbTourSchedule schedule);
        
        // 2026-02-05 - Query-time Override lookup
        Task<int?> GetOverrideGuideForDateAsync(DateTime date, string tourName, string tourTime);

        Task<List<DbTour>> GetToursAsync(CancellationToken ct = default);
		Task<DbTour?> CreateTourAsync(DbTour tour, CancellationToken ct = default);
		Task<bool> UpdateTourAsync(int id, DbTour tour, CancellationToken ct = default);
		Task<bool> DeleteTourAsync(int id, CancellationToken ct = default);
        Task<bool> EnsureTourScheduleTimeAsync(int tourId, string tourTime, string? meetingTime = null, string? meetingPlace = null, CancellationToken ct = default);
        Task<bool> DeleteTourScheduleAsync(int id);
	}
}

