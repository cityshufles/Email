// Created: 2026-02-11 11:55 UTC
// Purpose: SQL implementation for tour name variant mapping service

using System.Data;
using Microsoft.Data.SqlClient;

namespace Email.Services
{
    /// <summary>
    /// SQL-based implementation for managing tour name variant mappings
    /// </summary>
    public sealed class TourNameMappingSqlService : ITourNameMappingService
    {
        private readonly SqlConnectionFactory _connectionFactory;

        public TourNameMappingSqlService(SqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<List<TourNameMapping>> GetMappingsForTourAsync(int tourId)
        {
            const string sql = @"
SELECT Id, TourId, IncomingTourName, VendorName, IsActive, CreatedAt, UpdatedAt
FROM dbo.TourNameMappings
WHERE TourId = @TourId AND IsActive = 1
ORDER BY IncomingTourName;";

            var results = new List<TourNameMapping>();
            try
            {
#if DEBUG
                Console.WriteLine($"[GetMappingsForTourAsync] TourId: {tourId}");
#endif
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
                Console.WriteLine($"[GetMappingsForTourAsync] Error: {ex.Message}");
                throw;
            }

            return results;
        }

        public async Task<List<TourNameMapping>> GetAllActiveMappingsAsync()
        {
            const string sql = @"
SELECT Id, TourId, IncomingTourName, VendorName, IsActive, CreatedAt, UpdatedAt
FROM dbo.TourNameMappings
WHERE IsActive = 1
ORDER BY TourId, IncomingTourName;";

            var results = new List<TourNameMapping>();
            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                using var cmd = new SqlCommand(sql, conn);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    results.Add(MapTourNameMapping(reader));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GetAllActiveMappingsAsync] Error: {ex.Message}");
                throw;
            }

            return results;
        }

        public async Task<int> AddMappingAsync(int tourId, string incomingName, string? vendorName = null)
        {
            const string sql = @"
IF EXISTS (SELECT 1 FROM dbo.TourNameMappings 
           WHERE TourId = @TourId
           AND IncomingTourName = @IncomingTourName 
           AND ((@VendorName IS NULL AND VendorName IS NULL) OR VendorName = @VendorName)
           AND IsActive = 1)
BEGIN
    SELECT -1; -- Already exists
END
ELSE
BEGIN
    INSERT INTO dbo.TourNameMappings (TourId, IncomingTourName, VendorName, IsActive, CreatedAt, UpdatedAt)
    VALUES (@TourId, @IncomingTourName, @VendorName, 1, SYSDATETIME(), SYSDATETIME());
    SELECT CAST(SCOPE_IDENTITY() AS int);
END";

            try
            {
#if DEBUG
                Console.WriteLine($"[AddMappingAsync] TourId: {tourId}, Name: {incomingName}, Vendor: {vendorName}");
#endif
                using var conn = _connectionFactory.CreateOpenConnection();
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.Add(new SqlParameter("@TourId", SqlDbType.Int) { Value = tourId });
                cmd.Parameters.Add(new SqlParameter("@IncomingTourName", SqlDbType.NVarChar, 500) { Value = incomingName });
                cmd.Parameters.Add(new SqlParameter("@VendorName", SqlDbType.NVarChar, 100) { Value = (object?)vendorName ?? DBNull.Value });

                var result = await cmd.ExecuteScalarAsync();
                return result is int id ? id : -1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AddMappingAsync] Error: {ex.Message}");
                throw;
            }
        }

        public async Task<bool> DeleteMappingAsync(int id)
        {
            const string sql = @"
UPDATE dbo.TourNameMappings
SET IsActive = 0, UpdatedAt = SYSDATETIME()
WHERE Id = @Id;";

            try
            {
#if DEBUG
                Console.WriteLine($"[DeleteMappingAsync] Id: {id}");
#endif
                using var conn = _connectionFactory.CreateOpenConnection();
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });

                var rows = await cmd.ExecuteNonQueryAsync();
                return rows > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DeleteMappingAsync] Error: {ex.Message}");
                throw;
            }
        }

        public async Task<bool> TouchMappingAsync(int id)
        {
            const string sql = @"
UPDATE dbo.TourNameMappings
SET UpdatedAt = SYSDATETIME()
WHERE Id = @Id AND IsActive = 1;";

            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });

                var rows = await cmd.ExecuteNonQueryAsync();
                return rows > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TouchMappingAsync] Error: {ex.Message}");
                throw;
            }
        }

        public async Task<Dictionary<string, int>> BuildNormalizationDictionaryAsync()
        {
            const string sql = @"
SELECT DISTINCT IncomingTourName, TourId
FROM dbo.TourNameMappings
WHERE IsActive = 1;";

            var dictionary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                using var cmd = new SqlCommand(sql, conn);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var incomingName = reader.GetString(0);
                    var tourId = reader.GetInt32(1);

                    // Normalize the incoming name and add to dictionary
                    var normalized = TourNameNormalization.NormalizeTourNameForGrouping(string.Empty, incomingName);
                    if (!dictionary.ContainsKey(normalized))
                    {
                        dictionary[normalized] = tourId;
                    }
                }
#if DEBUG
                Console.WriteLine($"[BuildNormalizationDictionaryAsync] Built dictionary with {dictionary.Count} entries");
#endif
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BuildNormalizationDictionaryAsync] Error: {ex.Message}");
                throw;
            }

            return dictionary;
        }

        private static TourNameMapping MapTourNameMapping(SqlDataReader reader)
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
    }
}
