using System.Data;
using System.Text.Json;
using Email.Models;
using Email.Models.Staff;
using Microsoft.Data.SqlClient;

namespace Email.Services.Staff
{
    /// <summary>
    /// 2026-02-09 00:00 UTC - SQL-backed staff service for Users + Guides management.
    /// </summary>
    public sealed class StaffSqlService : IStaffService
    {
        private readonly SqlConnectionFactory _connectionFactory;
        private readonly ILogger<StaffSqlService> _logger;

        public StaffSqlService(SqlConnectionFactory connectionFactory, ILogger<StaffSqlService> logger)
        {
            // 2026-02-09 00:00 UTC - Created.
            try
            {
                _connectionFactory = connectionFactory;
                _logger = logger;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to construct StaffSqlService.");
#if DEBUG
                Console.WriteLine($"[StaffSqlService] Constructor failed: {ex}");
#endif
                throw;
            }
        }

        public async Task<List<StaffMember>> GetAllStaffAsync(CancellationToken ct = default)
        {
            // 2026-02-09 00:00 UTC - Created.
            // 2026-02-20 00:00 UTC - Modified: de-duplicate users/guides to reduce duplicate staff rows.
            // 2026-02-21 00:00 UTC - Modified: suppress user-only rows that match guides to avoid duplicates.
            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                var users = await LoadUsersAsync(conn, ct);
                var guides = await LoadGuidesAsync(conn, ct);

                users = DeduplicateUsers(users);
                guides = DeduplicateGuides(guides);

                // 2026-02-20 00:00 UTC - Include phone matching to link users and guides.
                var userLookup = new Dictionary<string, UserRow>(StringComparer.OrdinalIgnoreCase);
                var userPhoneLookup = new Dictionary<string, UserRow>(StringComparer.OrdinalIgnoreCase);
                foreach (var user in users)
                {
                    TryAddUserLookup(userLookup, user, user.Email);
                    TryAddUserLookup(userLookup, user, user.Username);
                    TryAddUserPhoneLookup(userPhoneLookup, user, user.PhoneNumber);
                }

                var results = new List<StaffMember>();
                var matchedUserIds = new HashSet<int>();

                foreach (var guide in guides)
                {
                    UserRow? user = null;
                    if (!string.IsNullOrWhiteSpace(guide.Email))
                    {
                        userLookup.TryGetValue(guide.Email.Trim(), out user);
                    }

                    if (user == null && !string.IsNullOrWhiteSpace(guide.Phone))
                    {
                        var normalizedGuidePhone = NormalizePhoneDigits(guide.Phone);
                        if (!string.IsNullOrWhiteSpace(normalizedGuidePhone))
                        {
                            if (!userPhoneLookup.TryGetValue(normalizedGuidePhone, out user) && normalizedGuidePhone.Length >= 10)
                            {
                                var last10 = normalizedGuidePhone[^10..];
                                userPhoneLookup.TryGetValue(last10, out user);
                            }
                        }
                    }

                    if (user != null)
                    {
                        matchedUserIds.Add(user.Id);
                    }

                    results.Add(MapStaff(user, guide));
                }

                var guideMatchKeys = BuildGuideMatchKeys(guides);
                foreach (var user in users)
                {
                    if (matchedUserIds.Contains(user.Id)) continue;
                    if (ShouldSuppressUserRow(user, guideMatchKeys)) continue;
                    results.Add(MapStaff(user, null));
                }

                return results
                    .OrderBy(s => string.IsNullOrWhiteSpace(s.DisplayName) ? s.Username : s.DisplayName)
                    .ThenBy(s => s.Username)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading staff list.");
#if DEBUG
                Console.WriteLine($"[StaffSqlService] GetAllStaffAsync failed: {ex}");
#endif
                return new List<StaffMember>();
            }
        }

        public async Task<StaffMember?> GetStaffMemberAsync(int? userId, int? guideId, CancellationToken ct = default)
        {
            // 2026-02-09 00:00 UTC - Created.
            try
            {
                var staff = await GetAllStaffAsync(ct);
                return staff.FirstOrDefault(s => (userId.HasValue && s.UserId == userId) || (guideId.HasValue && s.GuideId == guideId));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading staff member.");
#if DEBUG
                Console.WriteLine($"[StaffSqlService] GetStaffMemberAsync failed: {ex}");
#endif
                return null;
            }
        }

        public async Task SaveStaffMemberAsync(StaffMember staff, CancellationToken ct = default)
        {
            // 2026-02-09 00:00 UTC - Created.
            // 2026-02-20 00:00 UTC - Modified: normalize user/guide fields for guide login linkage.
            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                using var tx = conn.BeginTransaction();

                // 2026-02-20 00:00 UTC - Always create both user and guide profiles.
                var shouldHaveUser = true;
                var shouldHaveGuide = true;

                // 2026-02-20 00:00 UTC - Normalize and backfill user fields for guide login linkage.
                var username = NormalizeOrNull(staff.Username);
                var userEmail = NormalizeOrNull(staff.UserEmail);
                var contactEmail = NormalizeOrNull(staff.ContactEmail);

                if (string.IsNullOrWhiteSpace(userEmail) && !string.IsNullOrWhiteSpace(contactEmail))
                {
                    userEmail = contactEmail;
                }
                if (string.IsNullOrWhiteSpace(userEmail) && !string.IsNullOrWhiteSpace(username) && username.Contains("@"))
                {
                    userEmail = username;
                }
                if (string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(userEmail))
                {
                    username = userEmail;
                }

                if (shouldHaveGuide && (string.IsNullOrWhiteSpace(staff.Role) || staff.Role.Equals("User", StringComparison.OrdinalIgnoreCase)))
                {
                    staff.Role = "Guide";
                }

                staff.Username = username ?? string.Empty;
                staff.UserEmail = userEmail;

                var guideEmail = userEmail ?? contactEmail ?? username;
                var userPhone = NormalizeOrNull(staff.UserPhone);
                var guidePhone = NormalizeOrNull(staff.GuidePhone);

                if (string.IsNullOrWhiteSpace(userPhone) && !string.IsNullOrWhiteSpace(guidePhone))
                {
                    userPhone = guidePhone;
                    staff.UserPhone = guidePhone;
                }

                if (string.IsNullOrWhiteSpace(guidePhone) && !string.IsNullOrWhiteSpace(userPhone))
                {
                    guidePhone = userPhone;
                    staff.GuidePhone = userPhone;
                }

                if (shouldHaveUser && string.IsNullOrWhiteSpace(username))
                {
                    throw new InvalidOperationException("Username is required for user accounts.");
                }

                // 2026-02-20 00:00 UTC - Email required for user accounts.
                if (shouldHaveUser && string.IsNullOrWhiteSpace(userEmail))
                {
                    throw new InvalidOperationException("Email address is required for user accounts.");
                }

                if (shouldHaveUser && string.IsNullOrWhiteSpace(userPhone))
                {
                    throw new InvalidOperationException("User phone number is required.");
                }

                if (shouldHaveGuide && string.IsNullOrWhiteSpace(guidePhone))
                {
                    throw new InvalidOperationException("Guide phone number is required.");
                }

                var permissionsJson = JsonSerializer.Serialize(staff.Permissions ?? new UserPermissions());

                if (shouldHaveUser)
                {
                    if (!staff.UserId.HasValue && !string.IsNullOrWhiteSpace(userPhone))
                    {
                        var matchUserId = await FindUserIdByPhoneAsync(conn, tx, userPhone, ct);
                        if (matchUserId.HasValue)
                        {
                            staff.UserId = matchUserId.Value;
                        }
                    }

                    if (staff.UserId.HasValue)
                    {
                        await UpdateUserAsync(conn, tx, staff, username, userEmail, permissionsJson, ct);
                    }
                    else
                    {
                        var newUserId = await CreateUserAsync(conn, tx, staff, username, userEmail, permissionsJson, ct);
                        staff.UserId = newUserId;
                    }
                }

                if (shouldHaveGuide)
                {
                    if (!staff.GuideId.HasValue && !string.IsNullOrWhiteSpace(guidePhone))
                    {
                        var matchGuideId = await FindGuideIdByPhoneAsync(conn, tx, guidePhone, ct);
                        if (matchGuideId.HasValue)
                        {
                            staff.GuideId = matchGuideId.Value;
                        }
                    }

                    if (staff.GuideId.HasValue)
                    {
                        await UpdateGuideAsync(conn, tx, staff, guideEmail, ct);
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(staff.FirstName) || string.IsNullOrWhiteSpace(guidePhone))
                        {
                            throw new InvalidOperationException("First Name and Phone are required for guide profiles.");
                        }

                        var newGuideId = await CreateGuideAsync(conn, tx, staff, guideEmail, ct);
                        staff.GuideId = newGuideId;
                    }
                }

                tx.Commit();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving staff member.");
#if DEBUG
                Console.WriteLine($"[StaffSqlService] SaveStaffMemberAsync failed: {ex}");
#endif
                throw;
            }
        }

        public async Task DeleteStaffMemberAsync(int? userId, int? guideId, CancellationToken ct = default)
        {
            // 2026-02-09 00:00 UTC - Created.
            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                using var tx = conn.BeginTransaction();

                if (userId.HasValue)
                {
                    const string userSql = "UPDATE dbo.Users SET IsActive = 0 WHERE Id = @Id;";
                    using var cmd = new SqlCommand(userSql, conn, tx);
                    cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = userId.Value });
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                if (guideId.HasValue)
                {
                    const string guideSql = "UPDATE dbo.Guides SET IsActive = 0, UpdatedAt = GETUTCDATE() WHERE Id = @Id;";
                    using var cmd = new SqlCommand(guideSql, conn, tx);
                    cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = guideId.Value });
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                tx.Commit();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error soft deleting staff member.");
#if DEBUG
                Console.WriteLine($"[StaffSqlService] DeleteStaffMemberAsync failed: {ex}");
#endif
                throw;
            }
        }

        private async Task<List<UserRow>> LoadUsersAsync(SqlConnection conn, CancellationToken ct)
        {
            // 2026-02-09 00:00 UTC - Created.
            try
            {
                const string sql = @"
SELECT Id, Username, [Password], [Role], Email, Permissions, FirstName, LastName, PhoneNumber, IsActive, LastLoginAt
FROM dbo.Users
ORDER BY Username;";
                var results = new List<UserRow>();
                using var cmd = new SqlCommand(sql, conn);
                using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    results.Add(new UserRow
                    {
                        Id = reader.GetInt32(reader.GetOrdinal("Id")),
                        Username = reader.GetString(reader.GetOrdinal("Username")),
                        Password = reader.GetString(reader.GetOrdinal("Password")),
                        Role = reader.GetString(reader.GetOrdinal("Role")),
                        Email = reader.IsDBNull(reader.GetOrdinal("Email")) ? null : reader.GetString(reader.GetOrdinal("Email")),
                        Permissions = reader.IsDBNull(reader.GetOrdinal("Permissions")) ? "{}" : reader.GetString(reader.GetOrdinal("Permissions")),
                        FirstName = reader.IsDBNull(reader.GetOrdinal("FirstName")) ? null : reader.GetString(reader.GetOrdinal("FirstName")),
                        LastName = reader.IsDBNull(reader.GetOrdinal("LastName")) ? null : reader.GetString(reader.GetOrdinal("LastName")),
                        PhoneNumber = reader.IsDBNull(reader.GetOrdinal("PhoneNumber")) ? null : reader.GetString(reader.GetOrdinal("PhoneNumber")),
                        IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive")),
                        LastLoginAt = reader.IsDBNull(reader.GetOrdinal("LastLoginAt")) ? null : reader.GetDateTime(reader.GetOrdinal("LastLoginAt"))
                    });
                }

                return results;
            }
            catch (Exception ex)
            {
#if DEBUG
                Console.WriteLine($"[StaffSqlService] LoadUsersAsync failed: {ex}");
#endif
                throw;
            }
        }

        private async Task<List<GuideRow>> LoadGuidesAsync(SqlConnection conn, CancellationToken ct)
        {
            // 2026-02-09 00:00 UTC - Created.
            try
            {
                const string sql = @"
SELECT Id, FirstName, LastName, Phone, Email, GuideImage, Description, Languages, IsTouring, IsActive
FROM dbo.Guides
ORDER BY FirstName, LastName;";
                var results = new List<GuideRow>();
                using var cmd = new SqlCommand(sql, conn);
                using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    results.Add(new GuideRow
                    {
                        Id = reader.GetInt32(reader.GetOrdinal("Id")),
                        FirstName = reader.GetString(reader.GetOrdinal("FirstName")),
                        LastName = reader.IsDBNull(reader.GetOrdinal("LastName")) ? null : reader.GetString(reader.GetOrdinal("LastName")),
                        Phone = reader.GetString(reader.GetOrdinal("Phone")),
                        Email = reader.IsDBNull(reader.GetOrdinal("Email")) ? null : reader.GetString(reader.GetOrdinal("Email")),
                        GuideImage = reader.IsDBNull(reader.GetOrdinal("GuideImage")) ? null : reader.GetString(reader.GetOrdinal("GuideImage")),
                        Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString(reader.GetOrdinal("Description")),
                        Languages = reader.IsDBNull(reader.GetOrdinal("Languages")) ? null : reader.GetString(reader.GetOrdinal("Languages")),
                        IsTouring = reader.GetBoolean(reader.GetOrdinal("IsTouring")),
                        IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive"))
                    });
                }

                return results;
            }
            catch (Exception ex)
            {
#if DEBUG
                Console.WriteLine($"[StaffSqlService] LoadGuidesAsync failed: {ex}");
#endif
                throw;
            }
        }

        private async Task<int> CreateUserAsync(SqlConnection conn, SqlTransaction tx, StaffMember staff, string username, string? userEmail, string permissionsJson, CancellationToken ct)
        {
            // 2026-02-09 00:00 UTC - Created.
            try
            {
                const string sql = @"
INSERT INTO dbo.Users
(Username, [Password], [Role], Email, Permissions, FirstName, LastName, PhoneNumber, IsActive, CreatedAt)
VALUES
(@Username, @Password, @Role, @Email, @Permissions, @FirstName, @LastName, @PhoneNumber, @IsActive, SYSUTCDATETIME());
SELECT CAST(SCOPE_IDENTITY() AS int);";

                using var cmd = new SqlCommand(sql, conn, tx);
                cmd.Parameters.Add(new SqlParameter("@Username", SqlDbType.NVarChar, 256) { Value = username });
                cmd.Parameters.Add(new SqlParameter("@Password", SqlDbType.NVarChar, 256) { Value = staff.Password ?? string.Empty });
                cmd.Parameters.Add(new SqlParameter("@Role", SqlDbType.NVarChar, 64) { Value = staff.Role ?? "Guide" });
                cmd.Parameters.Add(new SqlParameter("@Email", SqlDbType.NVarChar, 256) { Value = (object?)NormalizeOrNull(userEmail) ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@Permissions", SqlDbType.NVarChar, -1) { Value = permissionsJson });
                cmd.Parameters.Add(new SqlParameter("@FirstName", SqlDbType.NVarChar, 128) { Value = (object?)NormalizeOrNull(staff.FirstName) ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@LastName", SqlDbType.NVarChar, 128) { Value = (object?)NormalizeOrNull(staff.LastName) ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@PhoneNumber", SqlDbType.NVarChar, 50) { Value = (object?)NormalizeOrNull(staff.UserPhone) ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@IsActive", SqlDbType.Bit) { Value = staff.IsUserActive });

                var idObj = await cmd.ExecuteScalarAsync(ct);
                if (idObj is int id)
                {
                    return id;
                }

                throw new InvalidOperationException("Failed to create user.");
            }
            catch (Exception ex)
            {
#if DEBUG
                Console.WriteLine($"[StaffSqlService] CreateUserAsync failed: {ex}");
#endif
                throw;
            }
        }

        private async Task UpdateUserAsync(SqlConnection conn, SqlTransaction tx, StaffMember staff, string username, string? userEmail, string permissionsJson, CancellationToken ct)
        {
            // 2026-02-09 00:00 UTC - Created.
            try
            {
                const string sql = @"
UPDATE dbo.Users
SET Username = @Username,
    [Password] = CASE WHEN @Password IS NULL OR @Password = '' THEN [Password] ELSE @Password END,
    [Role] = @Role,
    Email = @Email,
    Permissions = @Permissions,
    FirstName = @FirstName,
    LastName = @LastName,
    PhoneNumber = @PhoneNumber,
    IsActive = @IsActive
WHERE Id = @Id;";

                using var cmd = new SqlCommand(sql, conn, tx);
                cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = staff.UserId!.Value });
                cmd.Parameters.Add(new SqlParameter("@Username", SqlDbType.NVarChar, 256) { Value = username });
                cmd.Parameters.Add(new SqlParameter("@Password", SqlDbType.NVarChar, 256) { Value = string.IsNullOrWhiteSpace(staff.Password) ? DBNull.Value : staff.Password });
                cmd.Parameters.Add(new SqlParameter("@Role", SqlDbType.NVarChar, 64) { Value = staff.Role ?? "Guide" });
                cmd.Parameters.Add(new SqlParameter("@Email", SqlDbType.NVarChar, 256) { Value = (object?)NormalizeOrNull(userEmail) ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@Permissions", SqlDbType.NVarChar, -1) { Value = permissionsJson });
                cmd.Parameters.Add(new SqlParameter("@FirstName", SqlDbType.NVarChar, 128) { Value = (object?)NormalizeOrNull(staff.FirstName) ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@LastName", SqlDbType.NVarChar, 128) { Value = (object?)NormalizeOrNull(staff.LastName) ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@PhoneNumber", SqlDbType.NVarChar, 50) { Value = (object?)NormalizeOrNull(staff.UserPhone) ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@IsActive", SqlDbType.Bit) { Value = staff.IsUserActive });

                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (Exception ex)
            {
#if DEBUG
                Console.WriteLine($"[StaffSqlService] UpdateUserAsync failed: {ex}");
#endif
                throw;
            }
        }

        private async Task<int> CreateGuideAsync(SqlConnection conn, SqlTransaction tx, StaffMember staff, string? guideEmail, CancellationToken ct)
        {
            // 2026-02-09 00:00 UTC - Created.
            try
            {
                const string sql = @"
INSERT INTO dbo.Guides
(FirstName, LastName, Phone, Email, GuideImage, Description, Languages, IsTouring, IsActive, CreatedAt, UpdatedAt)
VALUES
(@FirstName, @LastName, @Phone, @Email, @GuideImage, @Description, @Languages, @IsTouring, @IsActive, GETUTCDATE(), GETUTCDATE());
SELECT CAST(SCOPE_IDENTITY() AS int);";

                using var cmd = new SqlCommand(sql, conn, tx);
                cmd.Parameters.Add(new SqlParameter("@FirstName", SqlDbType.NVarChar, 100) { Value = staff.FirstName });
                cmd.Parameters.Add(new SqlParameter("@LastName", SqlDbType.NVarChar, 100) { Value = (object?)NormalizeOrNull(staff.LastName) ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@Phone", SqlDbType.NVarChar, 50) { Value = staff.GuidePhone ?? string.Empty });
                cmd.Parameters.Add(new SqlParameter("@Email", SqlDbType.NVarChar, 200) { Value = (object?)NormalizeOrNull(guideEmail) ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@GuideImage", SqlDbType.NVarChar, 500) { Value = (object?)NormalizeOrNull(staff.ImagePath) ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@Description", SqlDbType.NVarChar, -1) { Value = (object?)NormalizeOrNull(staff.Description) ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@Languages", SqlDbType.NVarChar, 200) { Value = (object?)NormalizeOrNull(staff.Languages) ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@IsTouring", SqlDbType.Bit) { Value = staff.IsTouring });
                cmd.Parameters.Add(new SqlParameter("@IsActive", SqlDbType.Bit) { Value = staff.IsGuideActive });

                var idObj = await cmd.ExecuteScalarAsync(ct);
                if (idObj is int id)
                {
                    return id;
                }

                throw new InvalidOperationException("Failed to create guide profile.");
            }
            catch (Exception ex)
            {
#if DEBUG
                Console.WriteLine($"[StaffSqlService] CreateGuideAsync failed: {ex}");
#endif
                throw;
            }
        }

        private async Task UpdateGuideAsync(SqlConnection conn, SqlTransaction tx, StaffMember staff, string? guideEmail, CancellationToken ct)
        {
            // 2026-02-09 00:00 UTC - Created.
            try
            {
                const string sql = @"
UPDATE dbo.Guides
SET FirstName = @FirstName,
    LastName = @LastName,
    Phone = @Phone,
    Email = @Email,
    GuideImage = @GuideImage,
    Description = @Description,
    Languages = @Languages,
    IsTouring = @IsTouring,
    IsActive = @IsActive,
    UpdatedAt = GETUTCDATE()
WHERE Id = @Id;";

                using var cmd = new SqlCommand(sql, conn, tx);
                cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = staff.GuideId!.Value });
                cmd.Parameters.Add(new SqlParameter("@FirstName", SqlDbType.NVarChar, 100) { Value = staff.FirstName });
                cmd.Parameters.Add(new SqlParameter("@LastName", SqlDbType.NVarChar, 100) { Value = (object?)NormalizeOrNull(staff.LastName) ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@Phone", SqlDbType.NVarChar, 50) { Value = staff.GuidePhone ?? string.Empty });
                cmd.Parameters.Add(new SqlParameter("@Email", SqlDbType.NVarChar, 200) { Value = (object?)NormalizeOrNull(guideEmail) ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@GuideImage", SqlDbType.NVarChar, 500) { Value = (object?)NormalizeOrNull(staff.ImagePath) ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@Description", SqlDbType.NVarChar, -1) { Value = (object?)NormalizeOrNull(staff.Description) ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@Languages", SqlDbType.NVarChar, 200) { Value = (object?)NormalizeOrNull(staff.Languages) ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@IsTouring", SqlDbType.Bit) { Value = staff.IsTouring });
                cmd.Parameters.Add(new SqlParameter("@IsActive", SqlDbType.Bit) { Value = staff.IsGuideActive });

                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (Exception ex)
            {
#if DEBUG
                Console.WriteLine($"[StaffSqlService] UpdateGuideAsync failed: {ex}");
#endif
                throw;
            }
        }

        private static StaffMember MapStaff(UserRow? user, GuideRow? guide)
        {
            // 2026-02-09 00:00 UTC - Created.
            try
            {
                var staff = new StaffMember
                {
                    UserId = user?.Id,
                    Username = user?.Username ?? guide?.Email ?? string.Empty,
                    Password = user?.Password ?? string.Empty,
                    Role = user?.Role ?? "Guide",
                    Permissions = DeserializePermissions(user?.Permissions),
                    IsUserActive = user?.IsActive ?? false,
                    LastLoginAt = user?.LastLoginAt,
                    GuideId = guide?.Id,
                    FirstName = guide?.FirstName ?? user?.FirstName ?? string.Empty,
                    LastName = guide?.LastName ?? user?.LastName ?? string.Empty,
                    Phone = guide?.Phone ?? user?.PhoneNumber ?? string.Empty,
                    UserPhone = user?.PhoneNumber,
                    GuidePhone = guide?.Phone,
                    UserEmail = user?.Email,
                    ContactEmail = user != null
                        ? (user.Email ?? user.Username ?? string.Empty)
                        : (guide?.Email ?? user?.Email ?? user?.Username ?? string.Empty),
                    Description = guide?.Description,
                    ImagePath = guide?.GuideImage,
                    Languages = guide?.Languages,
                    IsTouring = guide?.IsTouring ?? false,
                    IsGuideActive = guide?.IsActive ?? false,
                    IncludeUserAccount = user != null,
                    IncludeGuideProfile = guide != null
                };

                var displayName = $"{staff.FirstName} {staff.LastName}".Trim();
                staff.DisplayName = string.IsNullOrWhiteSpace(displayName) ? staff.Username : displayName;
                staff.IsDimmed = (user != null && !staff.IsUserActive) || (guide != null && !staff.IsGuideActive);

                return staff;
            }
            catch (Exception ex)
            {
#if DEBUG
                Console.WriteLine($"[StaffSqlService] MapStaff failed: {ex}");
#endif
                throw;
            }
        }


        private static UserPermissions DeserializePermissions(string? json)
        {
            // 2026-02-09 00:00 UTC - Created.
            try
            {
                if (string.IsNullOrWhiteSpace(json)) return new UserPermissions();
                return JsonSerializer.Deserialize<UserPermissions>(json) ?? new UserPermissions();
            }
            catch (Exception ex)
            {
#if DEBUG
                Console.WriteLine($"[StaffSqlService] DeserializePermissions failed: {ex}");
#endif
                return new UserPermissions();
            }
        }

        private static string? NormalizeOrNull(string? value)
        {
            // 2026-02-09 00:00 UTC - Created.
            try
            {
                if (string.IsNullOrWhiteSpace(value)) return null;
                return value.Trim();
            }
            catch (Exception ex)
            {
#if DEBUG
                Console.WriteLine($"[StaffSqlService] NormalizeOrNull failed: {ex}");
#endif
                return null;
            }
        }

        private static string NormalizePhoneDigits(string? value)
        {
            // 2026-02-14 00:00 UTC - Created.
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var chars = value.Where(char.IsDigit).ToArray();
            return new string(chars);
        }

        private static async Task<int?> FindUserIdByPhoneAsync(SqlConnection conn, SqlTransaction tx, string phone, CancellationToken ct)
        {
            // 2026-02-14 00:00 UTC - Created.
            var normalized = NormalizePhoneKey(phone);
            if (string.IsNullOrWhiteSpace(normalized)) return null;

            const string sql = "SELECT Id, PhoneNumber FROM dbo.Users WHERE PhoneNumber IS NOT NULL;";
            using var cmd = new SqlCommand(sql, conn, tx);
            using var reader = await cmd.ExecuteReaderAsync(ct);
            var matches = new List<int>();
            while (await reader.ReadAsync(ct))
            {
                var id = reader.GetInt32(reader.GetOrdinal("Id"));
                var phoneValue = reader.IsDBNull(reader.GetOrdinal("PhoneNumber")) ? null : reader.GetString(reader.GetOrdinal("PhoneNumber"));
                if (NormalizePhoneKey(phoneValue) == normalized)
                {
                    matches.Add(id);
                }
            }

            if (matches.Count == 0) return null;
            if (matches.Count > 1)
            {
                throw new InvalidOperationException("Multiple users found with the same phone number.");
            }

            return matches[0];
        }

        private static async Task<int?> FindGuideIdByPhoneAsync(SqlConnection conn, SqlTransaction tx, string phone, CancellationToken ct)
        {
            // 2026-02-14 00:00 UTC - Created.
            var normalized = NormalizePhoneKey(phone);
            if (string.IsNullOrWhiteSpace(normalized)) return null;

            const string sql = "SELECT Id, Phone FROM dbo.Guides WHERE Phone IS NOT NULL;";
            using var cmd = new SqlCommand(sql, conn, tx);
            using var reader = await cmd.ExecuteReaderAsync(ct);
            var matches = new List<int>();
            while (await reader.ReadAsync(ct))
            {
                var id = reader.GetInt32(reader.GetOrdinal("Id"));
                var phoneValue = reader.IsDBNull(reader.GetOrdinal("Phone")) ? null : reader.GetString(reader.GetOrdinal("Phone"));
                if (NormalizePhoneKey(phoneValue) == normalized)
                {
                    matches.Add(id);
                }
            }

            if (matches.Count == 0) return null;
            if (matches.Count > 1)
            {
                throw new InvalidOperationException("Multiple guides found with the same phone number.");
            }

            return matches[0];
        }

        private static void TryAddUserLookup(Dictionary<string, UserRow> lookup, UserRow user, string? value)
        {
            // 2026-02-14 00:00 UTC - Created.
            if (string.IsNullOrWhiteSpace(value)) return;
            var key = value.Trim();
            if (!lookup.ContainsKey(key))
            {
                lookup.Add(key, user);
            }
        }

        private static List<UserRow> DeduplicateUsers(List<UserRow> users)
        {
            // 2026-02-20 00:00 UTC - Created.
            try
            {
                var byKey = new Dictionary<string, UserRow>(StringComparer.OrdinalIgnoreCase);
                foreach (var user in users)
                {
                    var key = BuildUserKey(user);
                    if (byKey.TryGetValue(key, out var existing))
                    {
                        byKey[key] = PickPreferredUser(existing, user);
                    }
                    else
                    {
                        byKey.Add(key, user);
                    }
                }

                return byKey.Values.ToList();
            }
            catch (Exception ex)
            {
#if DEBUG
                Console.WriteLine($"[StaffSqlService] DeduplicateUsers failed: {ex}");
#endif
                return users;
            }
        }

        private static List<GuideRow> DeduplicateGuides(List<GuideRow> guides)
        {
            // 2026-02-20 00:00 UTC - Created.
            try
            {
                var byKey = new Dictionary<string, GuideRow>(StringComparer.OrdinalIgnoreCase);
                foreach (var guide in guides)
                {
                    var key = BuildGuideKey(guide);
                    if (byKey.TryGetValue(key, out var existing))
                    {
                        byKey[key] = PickPreferredGuide(existing, guide);
                    }
                    else
                    {
                        byKey.Add(key, guide);
                    }
                }

                return byKey.Values.ToList();
            }
            catch (Exception ex)
            {
#if DEBUG
                Console.WriteLine($"[StaffSqlService] DeduplicateGuides failed: {ex}");
#endif
                return guides;
            }
        }

        private static string BuildUserKey(UserRow user)
        {
            // 2026-02-20 00:00 UTC - Created.
            try
            {
                var phoneKey = NormalizePhoneKey(user.PhoneNumber);
                if (!string.IsNullOrWhiteSpace(phoneKey))
                {
                    return $"phone:{phoneKey}";
                }

                var email = NormalizeOrNull(user.Email);
                var username = NormalizeOrNull(user.Username);
                if (string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(username) && username.Contains("@"))
                {
                    email = username;
                }

                if (IsLikelyEmail(email) && !IsPlaceholderEmail(email))
                {
                    return $"email:{email.ToLowerInvariant()}";
                }

                if (!string.IsNullOrWhiteSpace(email))
                {
                    return $"email:{email.ToLowerInvariant()}";
                }

                if (!string.IsNullOrWhiteSpace(username) && !username.EndsWith("@placeholder.com", StringComparison.OrdinalIgnoreCase))
                {
                    return $"user:{username.ToLowerInvariant()}";
                }

                if (!string.IsNullOrWhiteSpace(username))
                {
                    return $"user:{username.ToLowerInvariant()}";
                }

                return $"id:{user.Id}";
            }
            catch (Exception ex)
            {
#if DEBUG
                Console.WriteLine($"[StaffSqlService] BuildUserKey failed: {ex}");
#endif
                return $"id:{user.Id}";
            }
        }

        private static string BuildGuideKey(GuideRow guide)
        {
            // 2026-02-20 00:00 UTC - Created.
            try
            {
                var phoneKey = NormalizePhoneKey(guide.Phone);
                if (!string.IsNullOrWhiteSpace(phoneKey))
                {
                    return $"phone:{phoneKey}";
                }

                var email = NormalizeOrNull(guide.Email);
                if (IsLikelyEmail(email))
                {
                    return $"email:{email!.ToLowerInvariant()}";
                }

                var name = $"{guide.FirstName} {guide.LastName}".Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return $"name:{name.ToLowerInvariant()}";
                }

                return $"id:{guide.Id}";
            }
            catch (Exception ex)
            {
#if DEBUG
                Console.WriteLine($"[StaffSqlService] BuildGuideKey failed: {ex}");
#endif
                return $"id:{guide.Id}";
            }
        }

        private static HashSet<string> BuildGuideMatchKeys(IEnumerable<GuideRow> guides)
        {
            // 2026-02-21 00:00 UTC - Created.
            try
            {
                var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var guide in guides)
                {
                    foreach (var key in BuildGuideMatchKeys(guide))
                    {
                        keys.Add(key);
                    }
                }

                return keys;
            }
            catch (Exception ex)
            {
#if DEBUG
                Console.WriteLine($"[StaffSqlService] BuildGuideMatchKeys failed: {ex}");
#endif
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static IEnumerable<string> BuildGuideMatchKeys(GuideRow guide)
        {
            // 2026-02-21 00:00 UTC - Created.
            var email = NormalizeOrNull(guide.Email);
            if (IsLikelyEmail(email))
            {
                yield return $"email:{email!.ToLowerInvariant()}";
            }

            var phoneKey = NormalizePhoneKey(guide.Phone);
            if (!string.IsNullOrWhiteSpace(phoneKey))
            {
                yield return $"phone:{phoneKey}";
            }

            var name = NormalizeNameKey(guide.FirstName, guide.LastName);
            if (!string.IsNullOrWhiteSpace(name))
            {
                yield return $"name:{name}";
            }
        }

        private static UserRow PickPreferredUser(UserRow a, UserRow b)
        {
            // 2026-02-20 00:00 UTC - Created.
            try
            {
                if (a.IsActive != b.IsActive)
                {
                    return a.IsActive ? a : b;
                }

                var aStrongEmail = HasStrongEmail(a.Email, a.Username);
                var bStrongEmail = HasStrongEmail(b.Email, b.Username);
                if (aStrongEmail != bStrongEmail)
                {
                    return aStrongEmail ? a : b;
                }

                if (a.LastLoginAt.HasValue || b.LastLoginAt.HasValue)
                {
                    var al = a.LastLoginAt ?? DateTime.MinValue;
                    var bl = b.LastLoginAt ?? DateTime.MinValue;
                    return al >= bl ? a : b;
                }

                return a.Id >= b.Id ? a : b;
            }
            catch (Exception ex)
            {
#if DEBUG
                Console.WriteLine($"[StaffSqlService] PickPreferredUser failed: {ex}");
#endif
                return a;
            }
        }

        private static GuideRow PickPreferredGuide(GuideRow a, GuideRow b)
        {
            // 2026-02-20 00:00 UTC - Created.
            try
            {
                if (a.IsActive != b.IsActive)
                {
                    return a.IsActive ? a : b;
                }

                var aStrongEmail = IsLikelyEmail(a.Email);
                var bStrongEmail = IsLikelyEmail(b.Email);
                if (aStrongEmail != bStrongEmail)
                {
                    return aStrongEmail ? a : b;
                }

                return a.Id >= b.Id ? a : b;
            }
            catch (Exception ex)
            {
#if DEBUG
                Console.WriteLine($"[StaffSqlService] PickPreferredGuide failed: {ex}");
#endif
                return a;
            }
        }

        private static bool ShouldSuppressUserRow(UserRow user, HashSet<string> guideMatchKeys)
        {
            // 2026-02-21 00:00 UTC - Created.
            try
            {
                if (!IsGuideRole(user.Role))
                {
                    return false;
                }

                foreach (var key in BuildUserMatchKeys(user))
                {
                    if (guideMatchKeys.Contains(key))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
#if DEBUG
                Console.WriteLine($"[StaffSqlService] ShouldSuppressUserRow failed: {ex}");
#endif
                return false;
            }
        }

        private static IEnumerable<string> BuildUserMatchKeys(UserRow user)
        {
            // 2026-02-21 00:00 UTC - Created.
            var email = NormalizeOrNull(user.Email);
            var username = NormalizeOrNull(user.Username);
            if (string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(username) && username.Contains("@"))
            {
                email = username;
            }

            if (IsLikelyEmail(email))
            {
                yield return $"email:{email!.ToLowerInvariant()}";
            }

            var phoneKey = NormalizePhoneKey(user.PhoneNumber);
            if (!string.IsNullOrWhiteSpace(phoneKey))
            {
                yield return $"phone:{phoneKey}";
            }

            var name = NormalizeNameKey(user.FirstName, user.LastName);
            if (!string.IsNullOrWhiteSpace(name))
            {
                yield return $"name:{name}";
            }
        }

        private static bool IsGuideRole(string? role)
        {
            // 2026-02-21 00:00 UTC - Created.
            return string.IsNullOrWhiteSpace(role) || role.Equals("Guide", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeNameKey(string? first, string? last)
        {
            // 2026-02-21 00:00 UTC - Created.
            var name = $"{NormalizeOrNull(first)} {NormalizeOrNull(last)}".Trim();
            return name.ToLowerInvariant();
        }

        private static string NormalizePhoneKey(string? value)
        {
            // 2026-02-21 00:00 UTC - Created.
            var digits = NormalizePhoneDigits(value);
            if (string.IsNullOrWhiteSpace(digits)) return string.Empty;
            if (digits.Length >= 10)
            {
                return digits[^10..];
            }
            return digits;
        }

        private static bool IsLikelyEmail(string? value)
        {
            // 2026-02-21 00:00 UTC - Created.
            return !string.IsNullOrWhiteSpace(value) && value.Contains("@", StringComparison.Ordinal);
        }

        private static bool IsPlaceholderEmail(string? value)
        {
            // 2026-02-21 00:00 UTC - Created.
            return !string.IsNullOrWhiteSpace(value)
                   && value.EndsWith("@placeholder.com", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasStrongEmail(string? email, string? username)
        {
            // 2026-02-21 00:00 UTC - Created.
            var candidate = NormalizeOrNull(email);
            if (string.IsNullOrWhiteSpace(candidate) && !string.IsNullOrWhiteSpace(username) && username.Contains("@"))
            {
                candidate = username;
            }
            return IsLikelyEmail(candidate) && !IsPlaceholderEmail(candidate);
        }

        private static void TryAddUserPhoneLookup(Dictionary<string, UserRow> lookup, UserRow user, string? value)
        {
            // 2026-02-20 00:00 UTC - Created.
            try
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                var normalized = NormalizePhoneDigits(value);
                if (string.IsNullOrWhiteSpace(normalized)) return;

                if (!lookup.ContainsKey(normalized))
                {
                    lookup.Add(normalized, user);
                }

                if (normalized.Length >= 10)
                {
                    var last10 = normalized[^10..];
                    if (!lookup.ContainsKey(last10))
                    {
                        lookup.Add(last10, user);
                    }
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                Console.WriteLine($"[StaffSqlService] TryAddUserPhoneLookup failed: {ex}");
#endif
            }
        }

        private sealed class UserRow
        {
            public int Id { get; set; }
            public string Username { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
            public string Role { get; set; } = string.Empty;
            public string? Email { get; set; }
            public string Permissions { get; set; } = "{}";
            public string? FirstName { get; set; }
            public string? LastName { get; set; }
            public string? PhoneNumber { get; set; }
            public bool IsActive { get; set; }
            public DateTime? LastLoginAt { get; set; }
        }

        private sealed class GuideRow
        {
            public int Id { get; set; }
            public string FirstName { get; set; } = string.Empty;
            public string? LastName { get; set; }
            public string Phone { get; set; } = string.Empty;
            public string? Email { get; set; }
            public string? GuideImage { get; set; }
            public string? Description { get; set; }
            public string? Languages { get; set; }
            public bool IsTouring { get; set; }
            public bool IsActive { get; set; }
        }
    }
}
