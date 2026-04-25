// Created: 2026-02-12 00:00 UTC
// Purpose: SQL implementation for master tour CRUD, variants, and schedule-based time options.

using System.Data;
using System.Linq;
using System.Text.Json;
using Email.Models;
using Microsoft.Data.SqlClient;

namespace Email.Services
{
    public sealed class MasterTourSqlService : IMasterTourService
    {
        private readonly SqlConnectionFactory _connectionFactory;
        private readonly ITourCatalogService _tourCatalogService;

        public MasterTourSqlService(SqlConnectionFactory connectionFactory, ITourCatalogService tourCatalogService)
        {
            _connectionFactory = connectionFactory;
            _tourCatalogService = tourCatalogService;
        }

        public async Task<List<DbTour>> GetAllMasterToursAsync()
        {
            // Created: 2026-02-12 00:00 UTC - Load active master tours for the management UI.
            const string sql = @"
SELECT Id, TourName, MasterTourName, MasterTourNameDesktop, MasterTourNameMobile,
       MeetingTime, MeetingPlace, MeetingInstructions, TourStartTime, DefaultGuideId,
       IsActive, CreatedAt, UpdatedAt
FROM dbo.Tours
WHERE IsActive = 1
ORDER BY MasterTourName, TourName;";

            var results = new List<DbTour>();
            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                using var cmd = new SqlCommand(sql, conn);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    results.Add(MapMasterTour(reader));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MasterTourSqlService] GetAllMasterToursAsync error: {ex.Message}");
                throw;
            }

            return results;
        }

        public async Task<DbTour?> GetMasterTourByIdAsync(int tourId)
        {
            // Created: 2026-02-12 00:00 UTC - Get a single master tour by Id.
            const string sql = @"
SELECT Id, TourName, MasterTourName, MasterTourNameDesktop, MasterTourNameMobile,
       MeetingTime, MeetingPlace, MeetingInstructions, TourStartTime, DefaultGuideId,
       IsActive, CreatedAt, UpdatedAt
FROM dbo.Tours
WHERE Id = @Id AND IsActive = 1;";

            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = tourId });
                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return MapMasterTour(reader);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MasterTourSqlService] GetMasterTourByIdAsync error: {ex.Message}");
                throw;
            }

            return null;
        }

        public async Task<int> SaveMasterTourAsync(DbTour tour)
        {
            // Created: 2026-02-12 00:00 UTC - Save or update a master tour record.
            try
            {
                if (string.IsNullOrWhiteSpace(tour.MasterTourName))
                {
                    throw new InvalidOperationException("Master tour name is required.");
                }

                return tour.Id == 0
                    ? await CreateMasterTourAsync(tour)
                    : await UpdateMasterTourAsync(tour);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MasterTourSqlService] SaveMasterTourAsync error: {ex.Message}");
                throw;
            }
        }

        public async Task<bool> DeleteMasterTourAsync(int tourId)
        {
            // Created: 2026-02-12 00:00 UTC - Soft delete a master tour by deactivating it.
            const string sql = @"
UPDATE dbo.Tours
SET IsActive = 0, UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id;";

            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = tourId });
                var rows = await cmd.ExecuteNonQueryAsync();
                return rows > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MasterTourSqlService] DeleteMasterTourAsync error: {ex.Message}");
                throw;
            }
        }

        public async Task<List<TourNameMapping>> GetTourVariantsAsync(int tourId)
        {
            // Created: 2026-02-12 00:00 UTC - Load active tour name variants for a tour.
            const string sql = @"
SELECT Id, TourId, IncomingTourName, VendorName, IsActive, CreatedAt, UpdatedAt
FROM dbo.TourNameMappings
WHERE TourId = @TourId AND IsActive = 1
ORDER BY VendorName, IncomingTourName;";

            var results = new List<TourNameMapping>();
            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.Add(new SqlParameter("@TourId", SqlDbType.Int) { Value = tourId });
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    results.Add(MapTourNameMapping(reader));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MasterTourSqlService] GetTourVariantsAsync error: {ex.Message}");
                throw;
            }

            return results;
        }

        public async Task<int> AddTourVariantAsync(TourNameMapping mapping)
        {
            // Created: 2026-02-12 00:00 UTC - Add a new tour name variant mapping.
            const string sql = @"
IF EXISTS (SELECT 1 FROM dbo.TourNameMappings 
           WHERE IncomingTourName = @IncomingTourName
           AND (@VendorName IS NULL OR VendorName = @VendorName)
           AND IsActive = 1)
BEGIN
    SELECT -1;
END
ELSE
BEGIN
    INSERT INTO dbo.TourNameMappings (TourId, IncomingTourName, VendorName, IsActive, CreatedAt, UpdatedAt)
    VALUES (@TourId, @IncomingTourName, @VendorName, 1, SYSDATETIME(), SYSDATETIME());
    SELECT CAST(SCOPE_IDENTITY() AS int);
END";

            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.Add(new SqlParameter("@TourId", SqlDbType.Int) { Value = mapping.TourId });
                cmd.Parameters.Add(new SqlParameter("@IncomingTourName", SqlDbType.NVarChar, 500) { Value = mapping.IncomingTourName });
                cmd.Parameters.Add(new SqlParameter("@VendorName", SqlDbType.NVarChar, 100) { Value = (object?)mapping.VendorName ?? DBNull.Value });

                var result = await cmd.ExecuteScalarAsync();
                return result is int id ? id : -1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MasterTourSqlService] AddTourVariantAsync error: {ex.Message}");
                throw;
            }
        }

        public async Task<bool> DeleteTourVariantAsync(int mappingId)
        {
            // Created: 2026-02-12 00:00 UTC - Soft delete a tour name variant mapping.
            const string sql = @"
UPDATE dbo.TourNameMappings
SET IsActive = 0, UpdatedAt = SYSDATETIME()
WHERE Id = @Id;";

            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = mappingId });
                var rows = await cmd.ExecuteNonQueryAsync();
                return rows > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MasterTourSqlService] DeleteTourVariantAsync error: {ex.Message}");
                throw;
            }
        }

        public async Task<List<MasterTourTimeSlot>> GetAvailableTourTimeSlotsAsync(int tourId)
        {
            // Created: 2026-02-12 00:00 UTC - Load all schedule time slots (standard + override).
            // NOTE: Override labeling is UI-only for now; future override dialog behavior may change.
            const string sql = @"
SELECT TimeSlotsJson, MeetingPlace, MeetingInstructions, MeetingTime, IsOverride
FROM dbo.TourSchedules
WHERE TourId = @TourId AND IsActive = 1;";

            var slotsByTime = new Dictionary<string, MasterTourTimeSlot>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.Add(new SqlParameter("@TourId", SqlDbType.Int) { Value = tourId });
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var timeSlotsJson = reader.IsDBNull(0) ? null : reader.GetString(0);
                    var meetingPlace = reader.IsDBNull(1) ? null : reader.GetString(1);
                    var meetingInstructions = reader.IsDBNull(2) ? null : reader.GetString(2);
                    var meetingTime = reader.IsDBNull(3) ? null : reader.GetString(3);
                    var isOverride = reader.GetBoolean(4);

                    var parsedSlots = ParseScheduleSlots(timeSlotsJson, meetingPlace, meetingInstructions, meetingTime, isOverride);
                    foreach (var slot in parsedSlots)
                    {
                        UpsertTimeSlot(slotsByTime, slot);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MasterTourSqlService] GetAvailableTourTimeSlotsAsync error: {ex.Message}");
                throw;
            }

            return slotsByTime.Values
                .OrderBy(s => s.TourTime)
                .ToList();
        }

        public async Task<List<string>> GetDistinctVendorsAsync()
        {
            // Created: 2026-02-12 00:00 UTC - Load distinct vendor names for variant entry.
            const string sql = @"
SELECT DISTINCT LTRIM(RTRIM(VendorName)) AS VendorName
FROM dbo.TourNameMappings
WHERE IsActive = 1 AND VendorName IS NOT NULL AND LTRIM(RTRIM(VendorName)) <> ''
ORDER BY VendorName;";

            var results = new List<string>();
            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                using var cmd = new SqlCommand(sql, conn);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    results.Add(reader.GetString(0));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MasterTourSqlService] GetDistinctVendorsAsync error: {ex.Message}");
                throw;
            }

            return results;
        }

        private async Task<int> CreateMasterTourAsync(DbTour tour)
        {
            // Created: 2026-02-12 00:00 UTC - Insert a new master tour record.
            const string sql = @"
INSERT INTO dbo.Tours
(TourName, MasterTourName, MasterTourNameDesktop, MasterTourNameMobile,
 MeetingTime, MeetingPlace, MeetingInstructions, TourStartTime, DefaultGuideId,
 IsActive, CreatedAt, UpdatedAt)
VALUES
(@TourName, @MasterTourName, @MasterTourNameDesktop, @MasterTourNameMobile,
 @MeetingTime, @MeetingPlace, @MeetingInstructions, @TourStartTime, @DefaultGuideId,
 @IsActive, SYSUTCDATETIME(), SYSUTCDATETIME());
SELECT CAST(SCOPE_IDENTITY() AS int);";

            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                using var cmd = new SqlCommand(sql, conn);
                AddMasterTourParams(cmd, tour);
                var result = await cmd.ExecuteScalarAsync();
                return result is int id ? id : 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MasterTourSqlService] CreateMasterTourAsync error: {ex.Message}");
                throw;
            }
        }

        private async Task<int> UpdateMasterTourAsync(DbTour tour)
        {
            // Created: 2026-02-12 00:00 UTC - Update an existing master tour record.
            const string sql = @"
UPDATE dbo.Tours
SET TourName = @TourName,
    MasterTourName = @MasterTourName,
    MasterTourNameDesktop = @MasterTourNameDesktop,
    MasterTourNameMobile = @MasterTourNameMobile,
    MeetingTime = @MeetingTime,
    MeetingPlace = @MeetingPlace,
    MeetingInstructions = @MeetingInstructions,
    TourStartTime = @TourStartTime,
    DefaultGuideId = @DefaultGuideId,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id;";

            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                using var cmd = new SqlCommand(sql, conn);
                AddMasterTourParams(cmd, tour);
                cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = tour.Id });
                var rows = await cmd.ExecuteNonQueryAsync();
                return rows > 0 ? tour.Id : 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MasterTourSqlService] UpdateMasterTourAsync error: {ex.Message}");
                throw;
            }
        }

        private static DbTour MapMasterTour(SqlDataReader reader)
        {
            // Created: 2026-02-12 00:00 UTC - Map SqlDataReader row to DbTour.
            try
            {
                return new DbTour
                {
                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                    TourName = reader.GetString(reader.GetOrdinal("TourName")),
                    MasterTourName = reader.GetString(reader.GetOrdinal("MasterTourName")),
                    MasterTourNameDesktop = reader.IsDBNull(reader.GetOrdinal("MasterTourNameDesktop")) ? null : reader.GetString(reader.GetOrdinal("MasterTourNameDesktop")),
                    MasterTourNameMobile = reader.IsDBNull(reader.GetOrdinal("MasterTourNameMobile")) ? null : reader.GetString(reader.GetOrdinal("MasterTourNameMobile")),
                    MeetingTime = reader.IsDBNull(reader.GetOrdinal("MeetingTime")) ? null : reader.GetString(reader.GetOrdinal("MeetingTime")),
                    MeetingPlace = reader.IsDBNull(reader.GetOrdinal("MeetingPlace")) ? null : reader.GetString(reader.GetOrdinal("MeetingPlace")),
                    MeetingInstructions = reader.IsDBNull(reader.GetOrdinal("MeetingInstructions")) ? null : reader.GetString(reader.GetOrdinal("MeetingInstructions")),
                    TourStartTime = reader.IsDBNull(reader.GetOrdinal("TourStartTime")) ? null : reader.GetString(reader.GetOrdinal("TourStartTime")),
                    DefaultGuideId = reader.IsDBNull(reader.GetOrdinal("DefaultGuideId")) ? null : reader.GetInt32(reader.GetOrdinal("DefaultGuideId")),
                    IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive")),
                    CreatedAt = reader.IsDBNull(reader.GetOrdinal("CreatedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                    UpdatedAt = reader.IsDBNull(reader.GetOrdinal("UpdatedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("UpdatedAt"))
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MasterTourSqlService] MapMasterTour error: {ex.Message}");
                throw;
            }
        }

        private static TourNameMapping MapTourNameMapping(SqlDataReader reader)
        {
            // Created: 2026-02-12 00:00 UTC - Map SqlDataReader row to TourNameMapping.
            try
            {
                return new TourNameMapping
                {
                    Id = reader.GetInt32(0),
                    TourId = reader.GetInt32(1),
                    IncomingTourName = reader.GetString(2),
                    VendorName = reader.IsDBNull(3) ? null : reader.GetString(3),
                    IsActive = reader.GetBoolean(4),
                    CreatedAt = reader.GetDateTime(5),
                    UpdatedAt = reader.GetDateTime(6)
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MasterTourSqlService] MapTourNameMapping error: {ex.Message}");
                throw;
            }
        }

        private void AddMasterTourParams(SqlCommand cmd, DbTour tour)
        {
            // Created: 2026-02-12 00:00 UTC - Add parameters for master tour insert/update.
            try
            {
                var masterName = (tour.MasterTourName ?? string.Empty).Trim();
                var desktopName = string.IsNullOrWhiteSpace(tour.MasterTourNameDesktop) ? masterName : tour.MasterTourNameDesktop.Trim();
                var tourName = string.IsNullOrWhiteSpace(tour.TourName) ? desktopName : tour.TourName.Trim();
                var meetingPlace = tour.MeetingPlace ?? string.Empty;
                var meetingTime = string.IsNullOrWhiteSpace(tour.MeetingTime) ? null : tour.MeetingTime;
                var meetingInstructions = string.IsNullOrWhiteSpace(tour.MeetingInstructions) ? null : tour.MeetingInstructions;
                var mobileName = string.IsNullOrWhiteSpace(tour.MasterTourNameMobile) ? null : tour.MasterTourNameMobile;
                var defaultGuideId = tour.DefaultGuideId.HasValue && tour.DefaultGuideId.Value > 0
                    ? tour.DefaultGuideId
                    : null;

                cmd.Parameters.Add(new SqlParameter("@TourName", SqlDbType.NVarChar, 200) { Value = tourName });
                cmd.Parameters.Add(new SqlParameter("@MasterTourName", SqlDbType.NVarChar, 200) { Value = masterName });
                cmd.Parameters.Add(new SqlParameter("@MasterTourNameDesktop", SqlDbType.NVarChar, 100) { Value = (object?)desktopName ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@MasterTourNameMobile", SqlDbType.NVarChar, 50) { Value = (object?)mobileName ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@MeetingTime", SqlDbType.NVarChar, 50) { Value = (object?)meetingTime ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@MeetingPlace", SqlDbType.NVarChar, 200) { Value = meetingPlace });
                cmd.Parameters.Add(new SqlParameter("@MeetingInstructions", SqlDbType.NVarChar, -1) { Value = (object?)meetingInstructions ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@TourStartTime", SqlDbType.NVarChar, 50) { Value = (object?)tour.TourStartTime ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@DefaultGuideId", SqlDbType.Int) { Value = (object?)defaultGuideId ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@IsActive", SqlDbType.Bit) { Value = tour.IsActive });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MasterTourSqlService] AddMasterTourParams error: {ex.Message}");
                throw;
            }
        }

        private List<MasterTourTimeSlot> ParseScheduleSlots(
            string? timeSlotsJson,
            string? scheduleMeetingPlace,
            string? scheduleMeetingInstructions,
            string? scheduleMeetingTime,
            bool isOverride)
        {
            // Created: 2026-02-12 00:00 UTC - Parse a schedule's TimeSlotsJson into tour time slots.
            var slots = new List<MasterTourTimeSlot>();
            try
            {
                if (string.IsNullOrWhiteSpace(timeSlotsJson))
                {
                    return slots;
                }

                if (timeSlotsJson.Trim().StartsWith("["))
                {
                    var parsed = JsonSerializer.Deserialize<List<TimeSlotModel>>(timeSlotsJson) ?? new List<TimeSlotModel>();
                    foreach (var slot in parsed)
                    {
                        var normalized = NormalizeTime(slot.TourTime);
                        if (string.IsNullOrWhiteSpace(normalized)) continue;

                        slots.Add(new MasterTourTimeSlot
                        {
                            TourTime = normalized,
                            MeetingTime = string.IsNullOrWhiteSpace(slot.MeetingTime) ? scheduleMeetingTime : slot.MeetingTime,
                            MeetingPlace = string.IsNullOrWhiteSpace(slot.MeetingPlace) ? scheduleMeetingPlace : slot.MeetingPlace,
                            MeetingInstructions = string.IsNullOrWhiteSpace(slot.MeetingInstructions) ? scheduleMeetingInstructions : slot.MeetingInstructions,
                            IsOverride = isOverride
                        });
                    }
                }
                else
                {
                    var parts = timeSlotsJson.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var part in parts)
                    {
                        var normalized = NormalizeTime(part);
                        if (string.IsNullOrWhiteSpace(normalized)) continue;

                        slots.Add(new MasterTourTimeSlot
                        {
                            TourTime = normalized,
                            MeetingTime = scheduleMeetingTime,
                            MeetingPlace = scheduleMeetingPlace,
                            MeetingInstructions = scheduleMeetingInstructions,
                            IsOverride = isOverride
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MasterTourSqlService] ParseScheduleSlots error: {ex.Message}");
            }

            return slots;
        }

        private void UpsertTimeSlot(Dictionary<string, MasterTourTimeSlot> slotsByTime, MasterTourTimeSlot slot)
        {
            // Created: 2026-02-12 00:00 UTC - Merge a time slot, preferring override details when present.
            try
            {
                if (!slotsByTime.TryGetValue(slot.TourTime, out var existing))
                {
                    slotsByTime[slot.TourTime] = slot;
                    return;
                }

                if (slot.IsOverride && !existing.IsOverride)
                {
                    slotsByTime[slot.TourTime] = slot;
                    return;
                }

                if (string.IsNullOrWhiteSpace(existing.MeetingPlace) && !string.IsNullOrWhiteSpace(slot.MeetingPlace))
                {
                    existing.MeetingPlace = slot.MeetingPlace;
                }

                if (string.IsNullOrWhiteSpace(existing.MeetingInstructions) && !string.IsNullOrWhiteSpace(slot.MeetingInstructions))
                {
                    existing.MeetingInstructions = slot.MeetingInstructions;
                }

                if (string.IsNullOrWhiteSpace(existing.MeetingTime) && !string.IsNullOrWhiteSpace(slot.MeetingTime))
                {
                    existing.MeetingTime = slot.MeetingTime;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MasterTourSqlService] UpsertTimeSlot error: {ex.Message}");
            }
        }

        private string NormalizeTime(string rawTime)
        {
            // Created: 2026-02-12 00:00 UTC - Normalize time strings to "HH:mm" for comparison.
            try
            {
                return _tourCatalogService.NormalizeTime(rawTime);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MasterTourSqlService] NormalizeTime error: {ex.Message}");
                return rawTime ?? string.Empty;
            }
        }
    }
}
