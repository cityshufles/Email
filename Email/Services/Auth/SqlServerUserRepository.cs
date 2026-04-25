using System.Data;
using Microsoft.Data.SqlClient;
using Email.Models;

namespace Email.Services.Auth
{
    /// <summary>
    /// 2025-11-22 00:00 UTC - SQL Server repository for Users (Email).
    /// Uses ConnectionStrings:AutomaticGmailSqlServer.
    /// </summary>
    public sealed class SqlServerUserRepository : IUserRepository
    {
        private readonly string _connectionString;
        private readonly ILogger<SqlServerUserRepository> _logger;

        public SqlServerUserRepository(IConfiguration configuration, ILogger<SqlServerUserRepository> logger)
        {
            // 2025-11-22 00:00 UTC - Reads existing Email connection string
            _connectionString = configuration.GetConnectionString("AutomaticGmailSqlServer")
                               ?? configuration["ConnectionStrings:AutomaticGmailSqlServer"]
                               ?? string.Empty;
            _logger = logger;
        }

        public async Task<User?> GetUserByUsernameAsync(string username)
        {
            // 2025-11-22 00:00 UTC
            if (string.IsNullOrWhiteSpace(_connectionString)) return null;

            const string sql = @"
SELECT TOP 1 Id, Username, [Password], [Role], Email, Permissions, FirstName, LastName, IsActive, LastLoginAt, CreatedAt, PhoneNumber
FROM dbo.Users
WHERE (Username = @Username OR Email = @Username) AND IsActive = 1;";

            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();
                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.Add(new SqlParameter("@Username", SqlDbType.NVarChar, 256) { Value = username });
                await using var reader = await cmd.ExecuteReaderAsync();
                if (!reader.Read()) return null;

                return MapUser(reader);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching user by username {Username}", username);
                return null;
            }
        }

        public async Task<User?> GetUserByIdAsync(int id)
        {
            // 2025-11-22 00:00 UTC
            if (string.IsNullOrWhiteSpace(_connectionString)) return null;

            const string sql = @"
SELECT TOP 1 Id, Username, [Password], [Role], Email, Permissions, FirstName, LastName, IsActive, LastLoginAt, CreatedAt, PhoneNumber
FROM dbo.Users
WHERE Id = @Id AND IsActive = 1;";

            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();
                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
                await using var reader = await cmd.ExecuteReaderAsync();
                if (!reader.Read()) return null;

                return MapUser(reader);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching user by id {Id}", id);
                return null;
            }
        }

        public async Task UpdateUserLastLoginAsync(int userId)
        {
            // 2025-11-22 00:00 UTC
            if (string.IsNullOrWhiteSpace(_connectionString)) return;

            const string sql = @"UPDATE dbo.Users SET LastLoginAt = SYSUTCDATETIME() WHERE Id = @Id;";
            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();
                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = userId });
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating LastLoginAt for user {UserId}", userId);
            }
        }

        private static User MapUser(SqlDataReader reader)
        {
            // 2025-11-22 00:00 UTC
            return new User
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                Username = reader.GetString(reader.GetOrdinal("Username")),
                Password = reader.GetString(reader.GetOrdinal("Password")),
                Role = reader.GetString(reader.GetOrdinal("Role")),
                Email = reader.IsDBNull(reader.GetOrdinal("Email")) ? null : reader.GetString(reader.GetOrdinal("Email")),
                Permissions = reader.IsDBNull(reader.GetOrdinal("Permissions")) ? "{}" : reader.GetString(reader.GetOrdinal("Permissions")),
                FirstName = reader.IsDBNull(reader.GetOrdinal("FirstName")) ? null : reader.GetString(reader.GetOrdinal("FirstName")),
                LastName = reader.IsDBNull(reader.GetOrdinal("LastName")) ? null : reader.GetString(reader.GetOrdinal("LastName")),
                IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive")),
                LastLoginAt = reader.IsDBNull(reader.GetOrdinal("LastLoginAt")) ? null : reader.GetDateTime(reader.GetOrdinal("LastLoginAt")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                PhoneNumber = reader.IsDBNull(reader.GetOrdinal("PhoneNumber")) ? null : reader.GetString(reader.GetOrdinal("PhoneNumber"))
            };
        }
    }
}


