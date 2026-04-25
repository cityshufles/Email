using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Email.Models;

namespace Email.Services
{
	/// <summary>
	/// Created: 2025-11-20 (time not specified) - Direct SQL implementation for Tours CRUD against dbo.Tours.
	/// Modified: 2026-02-11 - Added master tour name fields support
	/// </summary>
	public sealed class ToursSqlService : IToursApiService
	{
		private readonly SqlConnectionFactory _connectionFactory;
        private readonly ITourCatalogService _tourCatalogService;

		public ToursSqlService(SqlConnectionFactory connectionFactory, ITourCatalogService tourCatalogService)
		{
			_connectionFactory = connectionFactory;
            _tourCatalogService = tourCatalogService;
		}

		public async Task<List<DbTour>> GetToursAsync(CancellationToken ct = default)
		{
			const string sql = @"
SELECT Id, TourName, TourNameAlias, VendorNames, MeetingPlace, MeetingTime, MeetingInstructions,
       DefaultGuideId, Duration, MaxCapacity, IsActive, Notes, CreatedAt, UpdatedAt, TourStartTime, VendorLink,
       MasterTourName, MasterTourNameDesktop, MasterTourNameMobile,
       VendorTourId, VendorScheduleLink, ReviewLink
FROM dbo.Tours
ORDER BY TourName;";
			var results = new List<DbTour>();
			try
			{
				using var conn = _connectionFactory.CreateOpenConnection();
				using var cmd = new SqlCommand(sql, conn);
				using var reader = await cmd.ExecuteReaderAsync(ct);
				while (await reader.ReadAsync(ct))
				{
					results.Add(MapTour(reader));
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[GetToursAsync] Error: {ex.Message}");
				throw;
			}
			return results;
		}

		public async Task<DbTour?> CreateTourAsync(DbTour tour, CancellationToken ct = default)
		{
			const string sql = @"
INSERT INTO dbo.Tours
(TourName, TourNameAlias, VendorNames, MeetingPlace, MeetingTime, MeetingInstructions,
 DefaultGuideId, Duration, MaxCapacity, IsActive, Notes, TourStartTime, VendorLink,
 MasterTourName, MasterTourNameDesktop, MasterTourNameMobile,
 VendorTourId, VendorScheduleLink, ReviewLink)
VALUES
(@TourName, @TourNameAlias, @VendorNames, @MeetingPlace, @MeetingTime, @MeetingInstructions,
 @DefaultGuideId, @Duration, @MaxCapacity, @IsActive, @Notes, @TourStartTime, @VendorLink,
 @MasterTourName, @MasterTourNameDesktop, @MasterTourNameMobile,
 @VendorTourId, @VendorScheduleLink, @ReviewLink);
SELECT CAST(SCOPE_IDENTITY() AS int);";
			try
			{
				using var conn = _connectionFactory.CreateOpenConnection();
				using var cmd = new SqlCommand(sql, conn);
				AddTourParams(cmd, tour);
				var idObj = await cmd.ExecuteScalarAsync(ct);
				if (idObj is int id)
				{
					return await GetTourByIdAsync(conn, id, ct);
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[CreateTourAsync] Error: {ex.Message}");
				throw;
			}
			return null;
		}

		public async Task<bool> UpdateTourAsync(int id, DbTour tour, CancellationToken ct = default)
		{
			const string sql = @"
UPDATE dbo.Tours
SET TourName=@TourName, TourNameAlias=@TourNameAlias, VendorNames=@VendorNames,
    MeetingPlace=@MeetingPlace, MeetingTime=@MeetingTime, MeetingInstructions=@MeetingInstructions,
    DefaultGuideId=@DefaultGuideId, Duration=@Duration, MaxCapacity=@MaxCapacity,
    IsActive=@IsActive, Notes=@Notes, TourStartTime=@TourStartTime, VendorLink=@VendorLink,
    MasterTourName=@MasterTourName, MasterTourNameDesktop=@MasterTourNameDesktop, MasterTourNameMobile=@MasterTourNameMobile,
    VendorTourId=@VendorTourId, VendorScheduleLink=@VendorScheduleLink, ReviewLink=@ReviewLink
WHERE Id=@Id;";
			try
			{
				using var conn = _connectionFactory.CreateOpenConnection();
				using var cmd = new SqlCommand(sql, conn);
				AddTourParams(cmd, tour);
				cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
				var rows = await cmd.ExecuteNonQueryAsync(ct);
				return rows > 0;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[UpdateTourAsync] Error: {ex.Message}");
				throw;
			}
		}

		public async Task<bool> DeleteTourAsync(int id, CancellationToken ct = default)
		{
			const string sql = "DELETE FROM dbo.Tours WHERE Id=@Id;";
			try
			{
				using var conn = _connectionFactory.CreateOpenConnection();
				using var cmd = new SqlCommand(sql, conn);
				cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
				var rows = await cmd.ExecuteNonQueryAsync(ct);
				return rows > 0;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[DeleteTourAsync] Error: {ex.Message}");
				throw;
			}
		}

        /// <summary>
        /// 2/11/26 new addition to handle mapping from SqlDataReader to DbTour,  MasterTourName fields. 
        /// 
        /// </summary>
        /// <param name="r"></param>
        /// <returns></returns>
		private static DbTour MapTour(SqlDataReader r)
		{
			return new DbTour
			{
				Id = r.GetInt32(r.GetOrdinal("Id")),
				TourName = r.GetString(r.GetOrdinal("TourName")),
				TourNameAlias = r.IsDBNull(r.GetOrdinal("TourNameAlias")) ? null : r.GetString(r.GetOrdinal("TourNameAlias")),
				VendorNames = r.IsDBNull(r.GetOrdinal("VendorNames")) ? null : r.GetString(r.GetOrdinal("VendorNames")),
				MeetingPlace = r.IsDBNull(r.GetOrdinal("MeetingPlace")) ? null : r.GetString(r.GetOrdinal("MeetingPlace")),
				MeetingTime = r.IsDBNull(r.GetOrdinal("MeetingTime")) ? null : r.GetString(r.GetOrdinal("MeetingTime")),
				MeetingInstructions = r.IsDBNull(r.GetOrdinal("MeetingInstructions")) ? null : r.GetString(r.GetOrdinal("MeetingInstructions")),
				DefaultGuideId = r.IsDBNull(r.GetOrdinal("DefaultGuideId")) ? null : r.GetInt32(r.GetOrdinal("DefaultGuideId")),
				Duration = r.IsDBNull(r.GetOrdinal("Duration")) ? null : r.GetString(r.GetOrdinal("Duration")),
				MaxCapacity = r.IsDBNull(r.GetOrdinal("MaxCapacity")) ? null : r.GetInt32(r.GetOrdinal("MaxCapacity")),
				IsActive = r.GetBoolean(r.GetOrdinal("IsActive")),
				Notes = r.IsDBNull(r.GetOrdinal("Notes")) ? null : r.GetString(r.GetOrdinal("Notes")),
				CreatedAt = r.IsDBNull(r.GetOrdinal("CreatedAt")) ? null : r.GetDateTime(r.GetOrdinal("CreatedAt")),
				UpdatedAt = r.IsDBNull(r.GetOrdinal("UpdatedAt")) ? null : r.GetDateTime(r.GetOrdinal("UpdatedAt")),
				TourStartTime = r.IsDBNull(r.GetOrdinal("TourStartTime")) ? null : r.GetString(r.GetOrdinal("TourStartTime")),
				//VendorLink = r.IsDBNull(r.GetOrdinal("VendorLink")) ? null : r.GetString(r.GetOrdinal("VendorLink"))
                VendorLink = r.IsDBNull(r.GetOrdinal("VendorLink")) ? null : r.GetString(r.GetOrdinal("VendorLink")),  // Change this line to add a comma at the end
                MasterTourName = r.IsDBNull(r.GetOrdinal("MasterTourName")) ? string.Empty : r.GetString(r.GetOrdinal("MasterTourName")),
                MasterTourNameDesktop = r.IsDBNull(r.GetOrdinal("MasterTourNameDesktop")) ? null : r.GetString(r.GetOrdinal("MasterTourNameDesktop")),
                MasterTourNameMobile = r.IsDBNull(r.GetOrdinal("MasterTourNameMobile")) ? null : r.GetString(r.GetOrdinal("MasterTourNameMobile")),
                VendorTourId = r.IsDBNull(r.GetOrdinal("VendorTourId")) ? null : r.GetString(r.GetOrdinal("VendorTourId")),
                VendorScheduleLink = r.IsDBNull(r.GetOrdinal("VendorScheduleLink")) ? null : r.GetString(r.GetOrdinal("VendorScheduleLink")),
                ReviewLink = r.IsDBNull(r.GetOrdinal("ReviewLink")) ? null : r.GetString(r.GetOrdinal("ReviewLink"))

            };
		}

		private static void AddTourParams(SqlCommand cmd, DbTour t)
		{
			cmd.Parameters.Add(new SqlParameter("@TourName", SqlDbType.NVarChar, 200) { Value = t.TourName });
			cmd.Parameters.Add(new SqlParameter("@TourNameAlias", SqlDbType.NVarChar, 200) { Value = (object?)t.TourNameAlias ?? DBNull.Value });
			cmd.Parameters.Add(new SqlParameter("@VendorNames", SqlDbType.NVarChar, 500) { Value = (object?)t.VendorNames ?? DBNull.Value });
			cmd.Parameters.Add(new SqlParameter("@MeetingPlace", SqlDbType.NVarChar, 200) { Value = (object?)t.MeetingPlace ?? DBNull.Value });
			cmd.Parameters.Add(new SqlParameter("@MeetingTime", SqlDbType.NVarChar, 50) { Value = (object?)t.MeetingTime ?? DBNull.Value });
			cmd.Parameters.Add(new SqlParameter("@MeetingInstructions", SqlDbType.NVarChar, -1) { Value = (object?)t.MeetingInstructions ?? DBNull.Value });
			cmd.Parameters.Add(new SqlParameter("@DefaultGuideId", SqlDbType.Int) { Value = (object?)t.DefaultGuideId ?? DBNull.Value });
			cmd.Parameters.Add(new SqlParameter("@Duration", SqlDbType.NVarChar, 50) { Value = (object?)t.Duration ?? DBNull.Value });
			cmd.Parameters.Add(new SqlParameter("@MaxCapacity", SqlDbType.Int) { Value = (object?)t.MaxCapacity ?? DBNull.Value });
			cmd.Parameters.Add(new SqlParameter("@IsActive", SqlDbType.Bit) { Value = t.IsActive });
			cmd.Parameters.Add(new SqlParameter("@Notes", SqlDbType.NVarChar, -1) { Value = (object?)t.Notes ?? DBNull.Value });
			cmd.Parameters.Add(new SqlParameter("@TourStartTime", SqlDbType.NVarChar, 50) { Value = (object?)t.TourStartTime ?? DBNull.Value });
			cmd.Parameters.Add(new SqlParameter("@VendorLink", SqlDbType.NVarChar, 500) { Value = (object?)t.VendorLink ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@MasterTourName", SqlDbType.NVarChar, 200) { Value = (object?)t.MasterTourName ?? string.Empty });
            cmd.Parameters.Add(new SqlParameter("@MasterTourNameDesktop", SqlDbType.NVarChar, 100) { Value = (object?)t.MasterTourNameDesktop ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@MasterTourNameMobile", SqlDbType.NVarChar, 50) { Value = (object?)t.MasterTourNameMobile ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@VendorTourId", SqlDbType.NVarChar, 100) { Value = (object?)t.VendorTourId ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@VendorScheduleLink", SqlDbType.NVarChar, 1000) { Value = (object?)t.VendorScheduleLink ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@ReviewLink", SqlDbType.NVarChar, 1000) { Value = (object?)t.ReviewLink ?? DBNull.Value });
        }

		private static async Task<DbTour?> GetTourByIdAsync(SqlConnection conn, int id, CancellationToken ct)
		{
			const string sql = @"
SELECT Id, TourName, TourNameAlias, VendorNames, MeetingPlace, MeetingTime, MeetingInstructions,
       DefaultGuideId, Duration, MaxCapacity, IsActive, Notes, CreatedAt, UpdatedAt, TourStartTime, VendorLink,
       MasterTourName, MasterTourNameDesktop, MasterTourNameMobile,
       VendorTourId, VendorScheduleLink, ReviewLink
FROM dbo.Tours
WHERE Id=@Id;";
			try
			{
				using var cmd = new SqlCommand(sql, conn);
				cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
				using var reader = await cmd.ExecuteReaderAsync(ct);
				if (await reader.ReadAsync(ct))
				{
					return MapTour(reader);
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[GetTourByIdAsync] Error: {ex.Message}");
				throw;
			}
			return null;
		}
        public async Task<int> SaveTourScheduleAsync(DbTourSchedule s)
        {
            // Created: 2026-01-31 - Save/Update logic for TourSchedules
            // 2026-02-05: Added IsOverride column
            const string sql = @"
MERGE INTO dbo.TourSchedules AS Target
USING (VALUES (@Id, @TourId)) AS Source (Id, TourId)
ON Target.Id = Source.Id
WHEN MATCHED THEN
    UPDATE SET 
        TourId = @TourId,
        ScheduleName = @ScheduleName,
        StartDate = @StartDate,
        EndDate = @EndDate,
        MeetingPlace = @MeetingPlace,
        MeetingTime = @MeetingTime,
        MeetingInstructions = @MeetingInstructions,
        VendorLink = @VendorLink,
        DayAssignmentsJson = @DayAssignmentsJson,
        TimeSlotsJson = @TimeSlotsJson,
        IsActive = @IsActive,
        IsOverride = @IsOverride,
        UpdatedAt = GETUTCDATE()
WHEN NOT MATCHED THEN
    INSERT (TourId, ScheduleName, StartDate, EndDate, MeetingPlace, MeetingTime, MeetingInstructions, VendorLink, DayAssignmentsJson, TimeSlotsJson, IsActive, IsOverride, CreatedAt, UpdatedAt)
    VALUES (@TourId, @ScheduleName, @StartDate, @EndDate, @MeetingPlace, @MeetingTime, @MeetingInstructions, @VendorLink, @DayAssignmentsJson, @TimeSlotsJson, @IsActive, @IsOverride, GETUTCDATE(), GETUTCDATE())
OUTPUT inserted.Id;";

            Console.WriteLine($"[ToursSqlService] Saving Schedule '{s.ScheduleName}' (Id={s.Id}) for TourId={s.TourId}...");
            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                using var cmd = new SqlCommand(sql, conn);
                
                cmd.Parameters.AddWithValue("@Id", s.Id);
                cmd.Parameters.AddWithValue("@TourId", s.TourId);
                cmd.Parameters.AddWithValue("@ScheduleName", s.ScheduleName ?? "New Schedule");
                cmd.Parameters.AddWithValue("@StartDate", s.StartDate);
                cmd.Parameters.AddWithValue("@EndDate", s.EndDate);
                cmd.Parameters.AddWithValue("@MeetingPlace", (object?)s.MeetingPlace ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@MeetingTime", (object?)s.MeetingTime ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@MeetingInstructions", (object?)s.MeetingInstructions ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@VendorLink", (object?)s.VendorLink ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@DayAssignmentsJson", (object?)s.DayAssignmentsJson ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@TimeSlotsJson", (object?)s.TimeSlotsJson ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@IsActive", s.IsActive);
                cmd.Parameters.AddWithValue("@IsOverride", s.IsOverride);

                var result = await cmd.ExecuteScalarAsync();
                return result != null ? Convert.ToInt32(result) : 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SaveTourScheduleAsync] Error: {ex.Message}");
                throw;
            }
        }

        public async Task<bool> DeleteTourScheduleAsync(int id)
        {
            const string sql = "DELETE FROM dbo.TourSchedules WHERE Id = @Id";
            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Id", id);
                var rows = await cmd.ExecuteNonQueryAsync();
                return rows > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DeleteTourScheduleAsync] Error: {ex.Message}");
                throw;
            }
        }

        public async Task<bool> EnsureTourScheduleTimeAsync(int tourId, string tourTime, string? meetingTime = null, string? meetingPlace = null, CancellationToken ct = default)
        {
            if (tourId <= 0 || string.IsNullOrWhiteSpace(tourTime))
            {
                return false;
            }

            var normalizedTourTime = _tourCatalogService.NormalizeTime(tourTime);
            if (string.IsNullOrWhiteSpace(normalizedTourTime))
            {
                return false;
            }

            var normalizedMeetingTime = string.IsNullOrWhiteSpace(meetingTime)
                ? null
                : _tourCatalogService.NormalizeTime(meetingTime);

            using var conn = _connectionFactory.CreateOpenConnection();

            int? scheduleId = null;
            string? scheduleMeetingTime = null;
            string? scheduleMeetingPlace = null;
            string? scheduleMeetingInstructions = null;
            string? timeSlotsJson = null;

            const string latestScheduleSql = @"
SELECT TOP 1 Id, MeetingTime, MeetingPlace, MeetingInstructions, TimeSlotsJson
FROM dbo.TourSchedules
WHERE TourId = @TourId AND IsActive = 1 AND IsOverride = 0
ORDER BY UpdatedAt DESC, Id DESC;";

            using (var cmd = new SqlCommand(latestScheduleSql, conn))
            {
                cmd.Parameters.AddWithValue("@TourId", tourId);
                using var reader = await cmd.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                {
                    scheduleId = reader.GetInt32(0);
                    scheduleMeetingTime = reader.IsDBNull(1) ? null : reader.GetString(1);
                    scheduleMeetingPlace = reader.IsDBNull(2) ? null : reader.GetString(2);
                    scheduleMeetingInstructions = reader.IsDBNull(3) ? null : reader.GetString(3);
                    timeSlotsJson = reader.IsDBNull(4) ? null : reader.GetString(4);
                }
            }

            if (scheduleId.HasValue)
            {
                var slots = ParseScheduleSlotsJson(timeSlotsJson, scheduleMeetingTime, scheduleMeetingPlace, scheduleMeetingInstructions);
                var existing = slots.FirstOrDefault(slot =>
                    string.Equals(_tourCatalogService.NormalizeTime(slot.TourTime), normalizedTourTime, StringComparison.OrdinalIgnoreCase));

                var changed = false;
                if (existing is null)
                {
                    slots.Add(new TimeSlotModel
                    {
                        TourTime = normalizedTourTime,
                        MeetingTime = normalizedMeetingTime ?? scheduleMeetingTime,
                        MeetingPlace = string.IsNullOrWhiteSpace(meetingPlace) ? scheduleMeetingPlace : meetingPlace,
                        MeetingInstructions = scheduleMeetingInstructions
                    });
                    changed = true;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(existing.MeetingTime) && !string.IsNullOrWhiteSpace(normalizedMeetingTime))
                    {
                        existing.MeetingTime = normalizedMeetingTime;
                        changed = true;
                    }

                    if (string.IsNullOrWhiteSpace(existing.MeetingPlace) && !string.IsNullOrWhiteSpace(meetingPlace))
                    {
                        existing.MeetingPlace = meetingPlace;
                        changed = true;
                    }
                }

                if (!changed)
                {
                    return true;
                }

                var payload = slots
                    .Where(slot => !string.IsNullOrWhiteSpace(slot.TourTime))
                    .Select(slot => new TimeSlotModel
                    {
                        TourTime = _tourCatalogService.NormalizeTime(slot.TourTime),
                        MeetingTime = string.IsNullOrWhiteSpace(slot.MeetingTime) ? null : _tourCatalogService.NormalizeTime(slot.MeetingTime),
                        MeetingPlace = string.IsNullOrWhiteSpace(slot.MeetingPlace) ? null : slot.MeetingPlace,
                        MeetingInstructions = string.IsNullOrWhiteSpace(slot.MeetingInstructions) ? null : slot.MeetingInstructions,
                        VendorLink = slot.VendorLink
                    })
                    .DistinctBy(slot => slot.TourTime, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(slot => slot.TourTime)
                    .ToList();

                var serialized = JsonSerializer.Serialize(payload);

                const string updateSql = @"
UPDATE dbo.TourSchedules
SET TimeSlotsJson = @TimeSlotsJson,
    MeetingTime = COALESCE(@MeetingTime, MeetingTime),
    MeetingPlace = COALESCE(@MeetingPlace, MeetingPlace),
    UpdatedAt = GETUTCDATE()
WHERE Id = @Id;";

                using var updateCmd = new SqlCommand(updateSql, conn);
                updateCmd.Parameters.AddWithValue("@Id", scheduleId.Value);
                updateCmd.Parameters.AddWithValue("@TimeSlotsJson", serialized);
                updateCmd.Parameters.AddWithValue("@MeetingTime", (object?)normalizedMeetingTime ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@MeetingPlace", (object?)meetingPlace ?? DBNull.Value);
                var updatedRows = await updateCmd.ExecuteNonQueryAsync(ct);
                return updatedRows > 0;
            }

            // No active standard schedule exists for this tour.
            // Intentionally do not auto-create schedules from this feature.
            return false;
        }

        private List<TimeSlotModel> ParseScheduleSlotsJson(string? timeSlotsJson, string? fallbackMeetingTime, string? fallbackMeetingPlace, string? fallbackMeetingInstructions)
        {
            var slots = new List<TimeSlotModel>();
            if (string.IsNullOrWhiteSpace(timeSlotsJson))
            {
                return slots;
            }

            try
            {
                if (timeSlotsJson.TrimStart().StartsWith("[", StringComparison.Ordinal))
                {
                    var parsed = JsonSerializer.Deserialize<List<TimeSlotModel>>(timeSlotsJson) ?? new List<TimeSlotModel>();
                    foreach (var slot in parsed)
                    {
                        if (string.IsNullOrWhiteSpace(slot.TourTime))
                        {
                            continue;
                        }

                        slots.Add(new TimeSlotModel
                        {
                            TourTime = _tourCatalogService.NormalizeTime(slot.TourTime),
                            MeetingTime = string.IsNullOrWhiteSpace(slot.MeetingTime)
                                ? fallbackMeetingTime
                                : _tourCatalogService.NormalizeTime(slot.MeetingTime),
                            MeetingPlace = string.IsNullOrWhiteSpace(slot.MeetingPlace) ? fallbackMeetingPlace : slot.MeetingPlace,
                            MeetingInstructions = string.IsNullOrWhiteSpace(slot.MeetingInstructions) ? fallbackMeetingInstructions : slot.MeetingInstructions,
                            VendorLink = slot.VendorLink
                        });
                    }
                }
                else
                {
                    var values = timeSlotsJson.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var value in values)
                    {
                        var normalized = _tourCatalogService.NormalizeTime(value);
                        if (string.IsNullOrWhiteSpace(normalized))
                        {
                            continue;
                        }

                        slots.Add(new TimeSlotModel
                        {
                            TourTime = normalized,
                            MeetingTime = fallbackMeetingTime,
                            MeetingPlace = fallbackMeetingPlace,
                            MeetingInstructions = fallbackMeetingInstructions
                        });
                    }
                }
            }
            catch
            {
                // no-op
            }

            return slots;
        }

        public async Task<List<DbTourSchedule>> GetTourSchedulesAsync(int tourId)
        {
            // 2026-02-05: Added IsOverride column
            const string sql = @"
SELECT ts.Id, ts.TourId, ts.ScheduleName, ts.StartDate, ts.EndDate, ts.MeetingPlace, ts.MeetingTime, ts.MeetingInstructions, ts.VendorLink, ts.DayAssignmentsJson, ts.TimeSlotsJson, ts.IsActive, ts.CreatedAt, ts.UpdatedAt,
       t.TourName, ts.IsOverride
FROM dbo.TourSchedules ts
JOIN dbo.Tours t ON ts.TourId = t.Id
WHERE ts.TourId = @TourId AND ts.IsActive = 1
ORDER BY ts.StartDate DESC;";

            var list = new List<DbTourSchedule>();
            using var conn = _connectionFactory.CreateOpenConnection();
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@TourId", tourId);

            Console.WriteLine($"[ToursSqlService] Loading schedules for TourId={tourId}...");

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                try 
                {
                    list.Add(new DbTourSchedule
                    {
                        Id = reader.GetInt32(0),
                        TourId = reader.GetInt32(1),
                        ScheduleName = reader.GetString(2),
                        StartDate = reader.GetDateTime(3),
                        EndDate = reader.GetDateTime(4),
                        MeetingPlace = reader.IsDBNull(5) ? null : reader.GetString(5),
                        MeetingTime = reader.IsDBNull(6) ? null : reader.GetString(6),
                        MeetingInstructions = reader.IsDBNull(7) ? null : reader.GetString(7),
                        VendorLink = reader.IsDBNull(8) ? null : reader.GetString(8),
                        DayAssignmentsJson = reader.IsDBNull(9) ? null : reader.GetString(9),
                        TimeSlotsJson = reader.IsDBNull(10) ? null : reader.GetString(10),
                        IsActive = reader.GetBoolean(11),
                        CreatedAt = reader.GetDateTime(12),
                        UpdatedAt = reader.GetDateTime(13),
                        TourName = reader.IsDBNull(14) ? null : reader.GetString(14),
                        IsOverride = reader.GetBoolean(15)
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GetTourSchedulesAsync] Error reading row: {ex.Message}");
                    // Log specific column checks if needed, or user can step through
                    throw; // Re-throw to let user see it
                }
            }
            return list;
        }

        public async Task<List<DbTourSchedule>> GetAllTourSchedulesAsync(CancellationToken ct = default)
        {
            // 2026-02-03 - Get all schedules for consolidated view
            // 2026-02-05: Added IsOverride column
            const string sql = @"
SELECT ts.Id, ts.TourId, ts.ScheduleName, ts.StartDate, ts.EndDate, ts.MeetingPlace, ts.MeetingTime, ts.MeetingInstructions, ts.VendorLink, ts.DayAssignmentsJson, ts.TimeSlotsJson, ts.IsActive, ts.CreatedAt, ts.UpdatedAt,
       t.TourName, ts.IsOverride
FROM dbo.TourSchedules ts
LEFT JOIN dbo.Tours t ON ts.TourId = t.Id
WHERE ts.IsActive = 1
ORDER BY t.TourName, ts.StartDate DESC;";

            var list = new List<DbTourSchedule>();
            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                using var cmd = new SqlCommand(sql, conn);
                using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                     list.Add(new DbTourSchedule
                     {
                        Id = reader.GetInt32(0),
                        TourId = reader.GetInt32(1),
                        ScheduleName = reader.GetString(2),
                        StartDate = reader.GetDateTime(3),
                        EndDate = reader.GetDateTime(4),
                        MeetingPlace = reader.IsDBNull(5) ? null : reader.GetString(5),
                        MeetingTime = reader.IsDBNull(6) ? null : reader.GetString(6),
                        MeetingInstructions = reader.IsDBNull(7) ? null : reader.GetString(7),
                        VendorLink = reader.IsDBNull(8) ? null : reader.GetString(8),
                        DayAssignmentsJson = reader.IsDBNull(9) ? null : reader.GetString(9),
                        TimeSlotsJson = reader.IsDBNull(10) ? null : reader.GetString(10),
                        IsActive = reader.GetBoolean(11),
                        CreatedAt = reader.GetDateTime(12),
                        UpdatedAt = reader.GetDateTime(13),
                        TourName = reader.IsDBNull(14) ? "Unknown Tour" : reader.GetString(14),
                        IsOverride = reader.GetBoolean(15)
                     });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GetAllTourSchedulesAsync] Error: {ex.Message}");
                throw;
            }
            return list;
        }
        public async Task GenerateAssignmentsFromScheduleAsync(DbTourSchedule s)
        {
            if (string.IsNullOrWhiteSpace(s.DayAssignmentsJson)) return;

            // 1. Deserialize assignments (DayName -> GuideId string)
            Dictionary<string, string>? dayMap = null;
            try
            {
                // The frontend binds <select> values as strings
                dayMap = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(s.DayAssignmentsJson);
            }
            catch { /* ignore invalid json */ }

            if (dayMap == null || dayMap.Count == 0) return;

            // ... (rest of method)

            // 2. Get Tour Aliases
            using var conn = _connectionFactory.CreateOpenConnection();
            var tour = await GetTourByIdAsync(conn, s.TourId, CancellationToken.None);
            if (tour == null) return;

            var aliases = new List<string> { tour.TourName };
            if (!string.IsNullOrWhiteSpace(tour.TourNameAlias))
            {
                aliases.AddRange(tour.TourNameAlias.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));
            }

            // 3. Identify relevant dates/times from ProcessedEmails
            // We look for actual tour instances that match this schedule's Tour (via aliases) and Time (if specified)
            // within the date range.

            var sql = @"
SELECT DISTINCT TourDate, TourName, TourTime
FROM AutomaticGmail_ProcessedEmails
WHERE TourDate >= @StartDate AND TourDate <= @EndDate
  AND (IsCancellation = 0)";

            // Filter by name (fuzzy match or exact match on aliases)
            // Since we can't easily do 'IN' with LIKE, we might fetch candidate rows and filter in C# 
            // OR use strict equality if aliases are precise.
            // For now, let's fetch matching date range and filter in memory to be safe with normalization.

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@StartDate", s.StartDate.Date);
            cmd.Parameters.AddWithValue("@EndDate", s.EndDate.Date);

            // Fetch generic matches
            var candidates = new List<(DateTime date, string name, string time)>();
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    if (reader.IsDBNull(0)) continue;
                    
                    // 2026-02-01 Fix: AutomaticGmail_ProcessedEmails.TourDate is NVARCHAR(50), not DATE.
                    // Must parse manually to avoid InvalidCastException.
                    var dateStr = reader.GetString(0);
                    if (!DateTime.TryParse(dateStr, out var d)) continue;

                    var n = reader.IsDBNull(1) ? "" : reader.GetString(1);
                    var t = reader.IsDBNull(2) ? "" : reader.GetString(2);
                    candidates.Add((d, n, t));
                }
            }
            try { Console.WriteLine($"[GenerateAssignments] Found {candidates.Count} candidates in range {s.StartDate:MM/dd}-{s.EndDate:MM/dd}"); } catch {}

            // 1.5 Parse TimeSlotsJson to get list of valid tour times
            // This must be done BEFORE iterating candidates
            var validTourTimes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(s.TimeSlotsJson))
            {
                try
                {
                    if (s.TimeSlotsJson.Trim().StartsWith("["))
                    {
                        var slots = System.Text.Json.JsonSerializer.Deserialize<List<TimeSlotModel>>(s.TimeSlotsJson);
                        if (slots != null)
                        {
                            foreach (var slot in slots) 
                            { 
                                if (!string.IsNullOrWhiteSpace(slot.TourTime))
                                {
                                    var norm = _tourCatalogService.NormalizeTime(slot.TourTime);
                                    validTourTimes.Add(norm);
                                    try { Console.WriteLine($"[GenerateAssignments] Valid Time: '{slot.TourTime}' -> '{norm}'"); } catch {}
                                }
                            }
                        }
                    }
                    else
                    {
                        // Legacy CSV
                        var parts = s.TimeSlotsJson.Split(',', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var p in parts) validTourTimes.Add(_tourCatalogService.NormalizeTime(p.Trim()));
                    }
                }
                catch { /* ignore */ }
            }

            // 4. Filter and Assign
            // Prepare upsert sql
            // We use a separate connection/command for updates to avoid busy reader
            using var updateConn = _connectionFactory.CreateOpenConnection();
            
            int upserts = 0;
            foreach (var (date, name, time) in candidates)
            {
                // Name Check
                var normalizedCand = _tourCatalogService.NormalizeName(name);
                bool nameMatch = aliases.Any(a => string.Equals(_tourCatalogService.NormalizeName(a), normalizedCand, StringComparison.OrdinalIgnoreCase));
                
                if (!nameMatch) 
                {
                     // excessive logging? maybe just log failures for first few
                     // try { Console.WriteLine($"[GenerateAssignments] Skip Name Mismatch: '{name}' vs aliases"); } catch {}
                     continue;
                }

                // Time Check
                // If the schedule has specific time slots defined, the candidate MUST match one of them.
                if (validTourTimes.Count > 0)
                {
                    var candTime = _tourCatalogService.NormalizeTime(time);
                    if (!validTourTimes.Contains(candTime)) 
                    {
                        try { Console.WriteLine($"[GenerateAssignments] Skip Time Mismatch: Date={date:MM/dd} Time='{time}' -> '{candTime}'. Expected: {string.Join(",", validTourTimes)}"); } catch {}
                        continue;
                    }
                }

                // Find the specific slot model for logistics
                TimeSlotModel? matchedSlot = null;
                if (!string.IsNullOrWhiteSpace(s.TimeSlotsJson) && s.TimeSlotsJson.Trim().StartsWith("["))
                {
                    try {
                        var slots = System.Text.Json.JsonSerializer.Deserialize<List<TimeSlotModel>>(s.TimeSlotsJson);
                        if (slots != null)
                        {
                            var candTime = _tourCatalogService.NormalizeTime(time);
                            matchedSlot = slots.FirstOrDefault(x => string.Equals(_tourCatalogService.NormalizeTime(x.TourTime), candTime, StringComparison.OrdinalIgnoreCase));
                        }
                    } catch {}
                }

                // If no specific slot found (legacy CSV or matching failed), fallback to defaults from Schedule
                string? meetPlace = matchedSlot?.MeetingPlace ?? s.MeetingPlace;
                string? meetTime = matchedSlot?.MeetingTime ?? s.MeetingTime;
                string? meetInstr = matchedSlot?.MeetingInstructions ?? s.MeetingInstructions;

                var dow = date.DayOfWeek.ToString(); // "Monday", etc.
                if (dayMap.TryGetValue(dow, out var guideIdStr) && int.TryParse(guideIdStr, out var guideId))
                {
                    if (guideId <= 0) 
                    {
                        Console.WriteLine($"[GenerateAssignments] Skip Invalid GuideId={guideId} for {dow}");
                        continue;
                    }

                    // Safety truncate to match schema
                    var safeMeetPlace = !string.IsNullOrEmpty(meetPlace) && meetPlace.Length > 255 ? meetPlace.Substring(0, 255) : meetPlace;
                    var safeMeetTime = !string.IsNullOrEmpty(meetTime) && meetTime.Length > 50 ? meetTime.Substring(0, 50) : meetTime;

                    // UPSERT Assignment
                    var upsertSql = @"
MERGE INTO dbo.TourGuideAssignments AS Target
USING (VALUES (@Date, @Name, @Time)) AS Source (TourDate, TourName, TourTime)
ON Target.TourDate = Source.TourDate AND Target.TourName = Source.TourName AND Target.TourTime = Source.TourTime
WHEN MATCHED THEN
    UPDATE SET GuideId = @GuideId, 
               MeetingPlace = @MeetingPlace, 
               MeetingTime = @MeetingTime, 
               MeetingInstructions = @MeetingInstructions,
               UpdatedAt = GETUTCDATE()
WHEN NOT MATCHED THEN
    INSERT (TourDate, TourName, TourTime, GuideId, MeetingPlace, MeetingTime, MeetingInstructions, CreatedAt, UpdatedAt)
    VALUES (@Date, @Name, @Time, @GuideId, @MeetingPlace, @MeetingTime, @MeetingInstructions, GETUTCDATE(), GETUTCDATE());";

                    using var upsertCmd = new SqlCommand(upsertSql, updateConn);
                    upsertCmd.Parameters.AddWithValue("@Date", date);
                    upsertCmd.Parameters.AddWithValue("@Name", name);
                    upsertCmd.Parameters.AddWithValue("@Time", time);
                    upsertCmd.Parameters.AddWithValue("@GuideId", guideId);
                    upsertCmd.Parameters.AddWithValue("@MeetingPlace", (object?)safeMeetPlace ?? DBNull.Value);
                    upsertCmd.Parameters.AddWithValue("@MeetingTime", (object?)safeMeetTime ?? DBNull.Value);
                    upsertCmd.Parameters.AddWithValue("@MeetingInstructions", (object?)meetInstr ?? DBNull.Value);
                    
                    try 
                    {
                        await upsertCmd.ExecuteNonQueryAsync();
                        upserts++;
                    }
                    catch (SqlException sqlEx)
                    {
                        Console.WriteLine($"[GenerateAssignments] SQL ERROR executing MERGE for {date:MM/dd} {name}: {sqlEx.Message}");
                    }
                    catch (Exception ex)
                    {
                         Console.WriteLine($"[GenerateAssignments] GENERIC ERROR executing MERGE for {date:MM/dd} {name}: {ex.Message}");
                    }
                }
            }
            try { Console.WriteLine($"[GenerateAssignments] Completed. Upserts: {upserts}"); } catch {}
        }

        public async Task<List<string>> CheckScheduleConflictsAsync(DbTourSchedule s)
        {
            // 2026-02-05 - Logic to detect other active schedules that overlap in Date + Day + Time
            var conflicts = new List<string>();
            try
            {
                // 1. Get other active schedules for this tour
                var allSchedules = await GetTourSchedulesAsync(s.TourId);
                var candidates = allSchedules
                    .Where(x => x.Id != s.Id) // Exclude self
                    .Where(x => x.IsActive)
                    // Date Overlap: StartA <= EndB && EndA >= StartB
                    .Where(x => s.StartDate <= x.EndDate && s.EndDate >= x.StartDate)
                    .ToList();

                if (candidates.Count == 0) return conflicts;

                // 2. Check for Day overlap
                Dictionary<string, string> myDays = new();
                if (!string.IsNullOrWhiteSpace(s.DayAssignmentsJson))
                {
                    try { myDays = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(s.DayAssignmentsJson) ?? new(); } catch {}
                }
                // If I have no active days defined, theoretically no conflict? Or maybe I'm active everyday?
                // Assuming empty map = no days active.
                if (myDays.Count == 0) return conflicts; // I'm not assigning anything

                // 3. Time Parsing
                var myTimes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(s.TimeSlotsJson))
                {
                    try 
                    {
                        if (s.TimeSlotsJson.Trim().StartsWith("["))
                        {
                            var slots = System.Text.Json.JsonSerializer.Deserialize<List<TimeSlotModel>>(s.TimeSlotsJson);
                            if (slots != null) foreach(var slot in slots) myTimes.Add(_tourCatalogService.NormalizeTime(slot.TourTime));
                        }
                    } catch {}
                }

                foreach (var other in candidates)
                {
                    // Day Overlap check
                    Dictionary<string, string> otherDays = new();
                    if (!string.IsNullOrWhiteSpace(other.DayAssignmentsJson))
                    {
                        try { otherDays = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(other.DayAssignmentsJson) ?? new(); } catch {}
                    }
                    
                    // Do we share any active day?
                    bool dayOverlap = myDays.Keys.Any(k => otherDays.ContainsKey(k));
                    if (!dayOverlap) continue;

                    // Time Overlap check
                    // If either schedule applies to "All Times" (empty TimeSlotsJson), then we have conflict
                    // If both have specific times, check intersection
                    bool timeOverlap = true;
                    
                    var otherTimes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (!string.IsNullOrWhiteSpace(other.TimeSlotsJson))
                    {
                         try 
                        {
                            if (other.TimeSlotsJson.Trim().StartsWith("["))
                            {
                                var slots = System.Text.Json.JsonSerializer.Deserialize<List<TimeSlotModel>>(other.TimeSlotsJson);
                                if (slots != null) foreach(var slot in slots) otherTimes.Add(_tourCatalogService.NormalizeTime(slot.TourTime));
                            }
                        } catch {}
                    }

                    if (myTimes.Count > 0 && otherTimes.Count > 0)
                    {
                        // Check intersection
                        timeOverlap = myTimes.Any(t => otherTimes.Contains(t));
                    }
                    // Else: one or both have no specific times -> Conflict on all times for that day

                    if (timeOverlap)
                    {
                        conflicts.Add(other.ScheduleName);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CheckScheduleConflictsAsync] Error: {ex.Message}");
            }
            return conflicts.Distinct().ToList();
        }

        // 2026-02-05: Query-time Override lookup - checks if there's an active Override schedule for this exact date/tour/time
        // Returns the GuideId from the Override schedule if found, null otherwise
        public async Task<int?> GetOverrideGuideForDateAsync(DateTime date, string tourName, string tourTime)
        {
            try
            {
                var normalizedTourName = tourName?.Trim();
                if (string.IsNullOrWhiteSpace(normalizedTourName))
                {
                    return null;
                }

                // Get DayOfWeek name for this date
                string dayName = date.DayOfWeek.ToString();
                string normalizedTime = _tourCatalogService.NormalizeTime(tourTime);

                using var conn = _connectionFactory.CreateOpenConnection();

                // Resolve one canonical TourId first to avoid ambiguous same-name rows.
                const string resolveTourSql = @"
SELECT TOP 1 Id
FROM dbo.Tours
WHERE TourName = @TourName OR MasterTourName = @TourName
ORDER BY IsActive DESC, UpdatedAt DESC, CreatedAt DESC, Id DESC;";

                int? resolvedTourId = null;
                using (var resolveCmd = new SqlCommand(resolveTourSql, conn))
                {
                    resolveCmd.Parameters.AddWithValue("@TourName", normalizedTourName);
                    var resolved = await resolveCmd.ExecuteScalarAsync();
                    if (resolved is int id)
                    {
                        resolvedTourId = id;
                    }
                }

                // Find active override schedules for the resolved TourId/date window.
                const string byTourIdSql = @"
SELECT ts.Id, ts.DayAssignmentsJson, ts.TimeSlotsJson, ts.StartDate, ts.EndDate
FROM dbo.TourSchedules ts
WHERE ts.TourId = @TourId
  AND ts.IsActive = 1 
  AND ts.IsOverride = 1
  AND @Date >= ts.StartDate 
  AND @Date <= ts.EndDate;";

                const string byNameFallbackSql = @"
SELECT ts.Id, ts.DayAssignmentsJson, ts.TimeSlotsJson, ts.StartDate, ts.EndDate
FROM dbo.TourSchedules ts
JOIN dbo.Tours t ON ts.TourId = t.Id
WHERE (t.TourName = @TourName OR t.MasterTourName = @TourName)
  AND ts.IsActive = 1 
  AND ts.IsOverride = 1
  AND @Date >= ts.StartDate 
  AND @Date <= ts.EndDate;";

                using var cmd = new SqlCommand(resolvedTourId.HasValue ? byTourIdSql : byNameFallbackSql, conn);
                if (resolvedTourId.HasValue)
                {
                    cmd.Parameters.AddWithValue("@TourId", resolvedTourId.Value);
                }
                else
                {
                    cmd.Parameters.AddWithValue("@TourName", normalizedTourName);
                }
                cmd.Parameters.AddWithValue("@Date", date.Date);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var dayAssignmentsJson = reader.IsDBNull(1) ? null : reader.GetString(1);
                    var timeSlotsJson = reader.IsDBNull(2) ? null : reader.GetString(2);

                    if (string.IsNullOrWhiteSpace(dayAssignmentsJson)) continue;

                    // Parse day assignments
                    Dictionary<string, string> dayAssignments = new();
                    try { dayAssignments = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(dayAssignmentsJson) ?? new(); } catch { continue; }

                    // Check if this override has an assignment for today's day of week
                    if (!dayAssignments.TryGetValue(dayName, out var guideIdStr)) continue;
                    if (!int.TryParse(guideIdStr, out int guideId)) continue;

                    // Check time match (empty times = all times)
                    if (!string.IsNullOrWhiteSpace(timeSlotsJson))
                    {
                        try
                        {
                            if (timeSlotsJson.Trim().StartsWith("["))
                            {
                                var slots = System.Text.Json.JsonSerializer.Deserialize<List<TimeSlotModel>>(timeSlotsJson);
                                if (slots != null && slots.Count > 0)
                                {
                                    bool timeMatch = slots.Any(slot => _tourCatalogService.NormalizeTime(slot.TourTime) == normalizedTime);
                                    if (!timeMatch) continue;
                                }
                            }
                        }
                        catch { }
                    }

                    // Found a matching Override!
                    Console.WriteLine($"[GetOverrideGuide] Found Override for {date:MM/dd} {tourName} {tourTime} -> GuideId={guideId}");
                    return guideId;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GetOverrideGuideForDateAsync] Error: {ex.Message}");
            }
            return null;
        }

        // 2026-02-05: Check if there's already an Override for the same slot
        public async Task<(bool hasStandardConflict, bool hasOverrideConflict, List<string> conflictNames)> CheckScheduleConflictsDetailedAsync(DbTourSchedule s)
        {
            bool hasStandardConflict = false;
            bool hasOverrideConflict = false;
            var conflictNames = new List<string>();

            try
            {
                var allSchedules = await GetTourSchedulesAsync(s.TourId);
                var candidates = allSchedules
                    .Where(x => x.Id != s.Id)
                    .Where(x => x.IsActive)
                    .Where(x => s.StartDate <= x.EndDate && s.EndDate >= x.StartDate)
                    .ToList();

                if (candidates.Count == 0) return (false, false, conflictNames);

                Dictionary<string, string> myDays = new();
                if (!string.IsNullOrWhiteSpace(s.DayAssignmentsJson))
                {
                    try { myDays = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(s.DayAssignmentsJson) ?? new(); } catch {}
                }
                if (myDays.Count == 0) return (false, false, conflictNames);

                var myTimes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(s.TimeSlotsJson))
                {
                    try 
                    {
                        if (s.TimeSlotsJson.Trim().StartsWith("["))
                        {
                            var slots = System.Text.Json.JsonSerializer.Deserialize<List<TimeSlotModel>>(s.TimeSlotsJson);
                            if (slots != null) foreach(var slot in slots) myTimes.Add(_tourCatalogService.NormalizeTime(slot.TourTime));
                        }
                    } catch {}
                }

                foreach (var other in candidates)
                {
                    Dictionary<string, string> otherDays = new();
                    if (!string.IsNullOrWhiteSpace(other.DayAssignmentsJson))
                    {
                        try { otherDays = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(other.DayAssignmentsJson) ?? new(); } catch {}
                    }
                    
                    bool dayOverlap = myDays.Keys.Any(k => otherDays.ContainsKey(k));
                    if (!dayOverlap) continue;

                    bool timeOverlap = true;
                    var otherTimes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (!string.IsNullOrWhiteSpace(other.TimeSlotsJson))
                    {
                        try 
                        {
                            if (other.TimeSlotsJson.Trim().StartsWith("["))
                            {
                                var slots = System.Text.Json.JsonSerializer.Deserialize<List<TimeSlotModel>>(other.TimeSlotsJson);
                                if (slots != null) foreach(var slot in slots) otherTimes.Add(_tourCatalogService.NormalizeTime(slot.TourTime));
                            }
                        } catch {}
                    }

                    if (myTimes.Count > 0 && otherTimes.Count > 0)
                    {
                        timeOverlap = myTimes.Any(t => otherTimes.Contains(t));
                    }

                    if (timeOverlap)
                    {
                        conflictNames.Add(other.ScheduleName);
                        if (other.IsOverride) hasOverrideConflict = true;
                        else hasStandardConflict = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CheckScheduleConflictsDetailedAsync] Error: {ex.Message}");
            }
            return (hasStandardConflict, hasOverrideConflict, conflictNames.Distinct().ToList());
        }
    }
}


