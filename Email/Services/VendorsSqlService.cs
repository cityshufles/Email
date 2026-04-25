using System.Data;
using Microsoft.Data.SqlClient;
using Email.Models;

namespace Email.Services
{
    /// <summary>
    /// Created: 2026-02-14 - Direct SQL implementation for Vendors CRUD against dbo.Vendors/VendorTours.
    /// </summary>
    public sealed class VendorsSqlService : IVendorsApiService
    {
        private readonly SqlConnectionFactory _connectionFactory;
        private string? _lastError;
        public string? LastError => _lastError;

        public VendorsSqlService(SqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<List<DbVendor>> GetVendorsAsync(CancellationToken ct = default)
        {
            _lastError = null;
            const string sql = @"
SELECT Id, VendorName, AllToursLink, IsActive, CreatedAt, UpdatedAt
FROM dbo.Vendors
ORDER BY VendorName;";

            var list = new List<DbVendor>();
            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                using var cmd = new SqlCommand(sql, conn);
                using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    list.Add(MapVendor(reader));
                }
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
            }

            return list;
        }

        public async Task<DbVendor?> CreateVendorAsync(DbVendor vendor, CancellationToken ct = default)
        {
            _lastError = null;
            const string sql = @"
INSERT INTO dbo.Vendors (VendorName, AllToursLink, IsActive)
VALUES (@VendorName, @AllToursLink, @IsActive);
SELECT CAST(SCOPE_IDENTITY() AS int);";

            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                using var cmd = new SqlCommand(sql, conn);
                AddVendorParams(cmd, vendor);
                var idObj = await cmd.ExecuteScalarAsync(ct);
                if (idObj is int id)
                {
                    return await GetVendorByIdAsync(conn, id, ct);
                }
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
            }
            return null;
        }

        public async Task<bool> UpdateVendorAsync(int id, DbVendor vendor, CancellationToken ct = default)
        {
            _lastError = null;
            const string sql = @"
UPDATE dbo.Vendors
SET VendorName=@VendorName, AllToursLink=@AllToursLink, IsActive=@IsActive, UpdatedAt=SYSUTCDATETIME()
WHERE Id=@Id;";

            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                using var cmd = new SqlCommand(sql, conn);
                AddVendorParams(cmd, vendor);
                cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
                var rows = await cmd.ExecuteNonQueryAsync(ct);
                return rows > 0;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return false;
            }
        }

        public async Task<bool> DeleteVendorAsync(int id, CancellationToken ct = default)
        {
            _lastError = null;
            const string sql = @"
UPDATE dbo.Vendors
SET IsActive = 0, UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id;

UPDATE dbo.VendorTours
SET IsActive = 0, UpdatedAt = SYSUTCDATETIME()
WHERE VendorId = @Id;";
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
                _lastError = ex.Message;
                return false;
            }
        }

        public async Task<List<DbVendorTour>> GetVendorToursAsync(int? vendorId = null, CancellationToken ct = default)
        {
            _lastError = null;
            const string sql = @"
SELECT Id, VendorId, MasterTourName, IsActive, CreatedAt, UpdatedAt
FROM dbo.VendorTours
WHERE (@VendorId IS NULL OR VendorId = @VendorId) AND IsActive = 1
ORDER BY VendorId, MasterTourName;";

            var list = new List<DbVendorTour>();
            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.Add(new SqlParameter("@VendorId", SqlDbType.Int) { Value = (object?)vendorId ?? DBNull.Value });
                using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    list.Add(MapVendorTour(reader));
                }
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
            }

            return list;
        }

        public async Task<bool> SetVendorTourActiveAsync(int vendorId, string masterTourName, bool isActive, CancellationToken ct = default)
        {
            _lastError = null;
            var trimmedName = masterTourName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmedName)) return false;

            if (!isActive)
            {
                const string deactivateSql = @"
UPDATE dbo.VendorTours
SET IsActive = 0, UpdatedAt = SYSUTCDATETIME()
WHERE VendorId = @VendorId AND MasterTourName = @MasterTourName;";

                try
                {
                    using var conn = _connectionFactory.CreateOpenConnection();
                    using var cmd = new SqlCommand(deactivateSql, conn);
                    cmd.Parameters.Add(new SqlParameter("@VendorId", SqlDbType.Int) { Value = vendorId });
                    cmd.Parameters.Add(new SqlParameter("@MasterTourName", SqlDbType.NVarChar, 200) { Value = trimmedName });
                    await cmd.ExecuteNonQueryAsync(ct);
                    return true;
                }
                catch (Exception ex)
                {
                    _lastError = ex.Message;
                    return false;
                }
            }

            const string upsertSql = @"
MERGE INTO dbo.VendorTours AS Target
USING (VALUES (@VendorId, @MasterTourName)) AS Source (VendorId, MasterTourName)
ON Target.VendorId = Source.VendorId AND Target.MasterTourName = Source.MasterTourName
WHEN MATCHED THEN
    UPDATE SET IsActive = 1, UpdatedAt = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT (VendorId, MasterTourName, IsActive, CreatedAt, UpdatedAt)
    VALUES (@VendorId, @MasterTourName, 1, SYSUTCDATETIME(), SYSUTCDATETIME());";

            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                using var cmd = new SqlCommand(upsertSql, conn);
                cmd.Parameters.Add(new SqlParameter("@VendorId", SqlDbType.Int) { Value = vendorId });
                cmd.Parameters.Add(new SqlParameter("@MasterTourName", SqlDbType.NVarChar, 200) { Value = trimmedName });
                await cmd.ExecuteNonQueryAsync(ct);
                return true;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return false;
            }
        }

        private static DbVendor MapVendor(SqlDataReader r)
        {
            return new DbVendor
            {
                Id = r.GetInt32(r.GetOrdinal("Id")),
                VendorName = r.GetString(r.GetOrdinal("VendorName")),
                AllToursLink = r.IsDBNull(r.GetOrdinal("AllToursLink")) ? null : r.GetString(r.GetOrdinal("AllToursLink")),
                IsActive = r.GetBoolean(r.GetOrdinal("IsActive")),
                CreatedAt = r.IsDBNull(r.GetOrdinal("CreatedAt")) ? null : r.GetDateTime(r.GetOrdinal("CreatedAt")),
                UpdatedAt = r.IsDBNull(r.GetOrdinal("UpdatedAt")) ? null : r.GetDateTime(r.GetOrdinal("UpdatedAt"))
            };
        }

        private static DbVendorTour MapVendorTour(SqlDataReader r)
        {
            return new DbVendorTour
            {
                Id = r.GetInt32(r.GetOrdinal("Id")),
                VendorId = r.GetInt32(r.GetOrdinal("VendorId")),
                MasterTourName = r.GetString(r.GetOrdinal("MasterTourName")),
                IsActive = r.GetBoolean(r.GetOrdinal("IsActive")),
                CreatedAt = r.IsDBNull(r.GetOrdinal("CreatedAt")) ? null : r.GetDateTime(r.GetOrdinal("CreatedAt")),
                UpdatedAt = r.IsDBNull(r.GetOrdinal("UpdatedAt")) ? null : r.GetDateTime(r.GetOrdinal("UpdatedAt"))
            };
        }

        private static void AddVendorParams(SqlCommand cmd, DbVendor v)
        {
            cmd.Parameters.Add(new SqlParameter("@VendorName", SqlDbType.NVarChar, 200) { Value = v.VendorName });
            cmd.Parameters.Add(new SqlParameter("@AllToursLink", SqlDbType.NVarChar, 500) { Value = (object?)v.AllToursLink ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@IsActive", SqlDbType.Bit) { Value = v.IsActive });
        }

        private static async Task<DbVendor?> GetVendorByIdAsync(SqlConnection conn, int id, CancellationToken ct)
        {
            const string sql = @"
SELECT Id, VendorName, AllToursLink, IsActive, CreatedAt, UpdatedAt
FROM dbo.Vendors
WHERE Id=@Id;";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
            using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                return MapVendor(reader);
            }
            return null;
        }
    }
}
