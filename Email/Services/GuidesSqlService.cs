using System.Data;
using Microsoft.Data.SqlClient;
using Email.Models;

namespace Email.Services
{
	/// <summary>
	/// Created: 2025-11-20 (time not specified) - Direct SQL implementation for Guides CRUD against dbo.Guides.
	/// </summary>
	public sealed class GuidesSqlService : IGuidesApiService
	{
		private readonly SqlConnectionFactory _connectionFactory;

		public GuidesSqlService(SqlConnectionFactory connectionFactory)
		{
			_connectionFactory = connectionFactory;
		}

		public async Task<List<DbGuide>> GetGuidesAsync(CancellationToken ct = default)
		{
			const string sql = @"
SELECT Id, FirstName, LastName, Phone, Email, GuideImage, Description,
       DefaultTourName, DefaultTourId, IsTouring, AvailabilityNotes, AvailabilityUpdatedAt,
       IsActive, HireDate, TerminationDate, Languages, Specialties, Notes,
       CreatedAt, UpdatedAt
FROM dbo.Guides
ORDER BY FirstName, LastName;";
			var results = new List<DbGuide>();
			using var conn = _connectionFactory.CreateOpenConnection();
			using var cmd = new SqlCommand(sql, conn);
			using var reader = await cmd.ExecuteReaderAsync(ct);
			while (await reader.ReadAsync(ct))
			{
				results.Add(MapGuide(reader));
			}
			var deduped = DeduplicateGuides(results);
			return deduped
				.OrderBy(g => g.FirstName)
				.ThenBy(g => g.LastName)
				.ToList();
		}

		public async Task<DbGuide?> CreateGuideAsync(DbGuide guide, CancellationToken ct = default)
		{
			const string sql = @"
INSERT INTO dbo.Guides
(FirstName, LastName, Phone, Email, GuideImage, Description, DefaultTourName, DefaultTourId,
 IsTouring, AvailabilityNotes, AvailabilityUpdatedAt, IsActive, HireDate, TerminationDate,
 Languages, Specialties, Notes, CreatedAt, UpdatedAt)
VALUES
(@FirstName, @LastName, @Phone, @Email, @GuideImage, @Description, @DefaultTourName, @DefaultTourId,
 @IsTouring, @AvailabilityNotes, @AvailabilityUpdatedAt, @IsActive, @HireDate, @TerminationDate,
 @Languages, @Specialties, @Notes, GETUTCDATE(), GETUTCDATE());
SELECT CAST(SCOPE_IDENTITY() AS int);";
			using var conn = _connectionFactory.CreateOpenConnection();
			using var cmd = new SqlCommand(sql, conn);
			AddGuideParams(cmd, guide);
			var idObj = await cmd.ExecuteScalarAsync(ct);
			if (idObj is int id)
			{
				return await GetGuideByIdAsync(conn, id, ct);
			}
			return null;
		}

		public async Task<bool> UpdateGuideAsync(int id, DbGuide guide, CancellationToken ct = default)
		{
			const string sql = @"
UPDATE dbo.Guides
SET FirstName=@FirstName, LastName=@LastName, Phone=@Phone, Email=@Email, GuideImage=@GuideImage,
    Description=@Description, DefaultTourName=@DefaultTourName, DefaultTourId=@DefaultTourId,
    IsTouring=@IsTouring, AvailabilityNotes=@AvailabilityNotes, AvailabilityUpdatedAt=@AvailabilityUpdatedAt,
    IsActive=@IsActive, HireDate=@HireDate, TerminationDate=@TerminationDate,
    Languages=@Languages, Specialties=@Specialties, Notes=@Notes
    , UpdatedAt=GETUTCDATE()
WHERE Id=@Id;";
			using var conn = _connectionFactory.CreateOpenConnection();
			using var cmd = new SqlCommand(sql, conn);
			AddGuideParams(cmd, guide);
			cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
			var rows = await cmd.ExecuteNonQueryAsync(ct);
			return rows > 0;
		}

		public async Task<bool> DeleteGuideAsync(int id, CancellationToken ct = default)
		{
			const string sql = "DELETE FROM dbo.Guides WHERE Id=@Id;";
			using var conn = _connectionFactory.CreateOpenConnection();
			using var cmd = new SqlCommand(sql, conn);
			cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
			var rows = await cmd.ExecuteNonQueryAsync(ct);
			return rows > 0;
		}

		private static DbGuide MapGuide(SqlDataReader r)
		{
			return new DbGuide
			{
				Id = r.GetInt32(r.GetOrdinal("Id")),
				FirstName = r.GetString(r.GetOrdinal("FirstName")),
				LastName = r.IsDBNull(r.GetOrdinal("LastName")) ? null : r.GetString(r.GetOrdinal("LastName")),
				Phone = r.GetString(r.GetOrdinal("Phone")),
				Email = r.IsDBNull(r.GetOrdinal("Email")) ? null : r.GetString(r.GetOrdinal("Email")),
				GuideImage = r.IsDBNull(r.GetOrdinal("GuideImage")) ? null : r.GetString(r.GetOrdinal("GuideImage")),
				Description = r.IsDBNull(r.GetOrdinal("Description")) ? null : r.GetString(r.GetOrdinal("Description")),
				DefaultTourName = r.IsDBNull(r.GetOrdinal("DefaultTourName")) ? null : r.GetString(r.GetOrdinal("DefaultTourName")),
				DefaultTourId = r.IsDBNull(r.GetOrdinal("DefaultTourId")) ? null : r.GetInt32(r.GetOrdinal("DefaultTourId")),
				IsTouring = r.GetBoolean(r.GetOrdinal("IsTouring")),
				AvailabilityNotes = r.IsDBNull(r.GetOrdinal("AvailabilityNotes")) ? null : r.GetString(r.GetOrdinal("AvailabilityNotes")),
				AvailabilityUpdatedAt = r.IsDBNull(r.GetOrdinal("AvailabilityUpdatedAt")) ? null : r.GetDateTime(r.GetOrdinal("AvailabilityUpdatedAt")),
				IsActive = r.GetBoolean(r.GetOrdinal("IsActive")),
				HireDate = r.IsDBNull(r.GetOrdinal("HireDate")) ? null : r.GetDateTime(r.GetOrdinal("HireDate")),
				TerminationDate = r.IsDBNull(r.GetOrdinal("TerminationDate")) ? null : r.GetDateTime(r.GetOrdinal("TerminationDate")),
				Languages = r.IsDBNull(r.GetOrdinal("Languages")) ? null : r.GetString(r.GetOrdinal("Languages")),
				Specialties = r.IsDBNull(r.GetOrdinal("Specialties")) ? null : r.GetString(r.GetOrdinal("Specialties")),
				Notes = r.IsDBNull(r.GetOrdinal("Notes")) ? null : r.GetString(r.GetOrdinal("Notes")),
				CreatedAt = r.IsDBNull(r.GetOrdinal("CreatedAt")) ? null : r.GetDateTime(r.GetOrdinal("CreatedAt")),
				UpdatedAt = r.IsDBNull(r.GetOrdinal("UpdatedAt")) ? null : r.GetDateTime(r.GetOrdinal("UpdatedAt")),
			};
		}

		private static void AddGuideParams(SqlCommand cmd, DbGuide guide)
		{
			cmd.Parameters.Add(new SqlParameter("@FirstName", SqlDbType.NVarChar, 100) { Value = guide.FirstName });
			cmd.Parameters.Add(new SqlParameter("@LastName", SqlDbType.NVarChar, 100) { Value = (object?)guide.LastName ?? DBNull.Value });
			cmd.Parameters.Add(new SqlParameter("@Phone", SqlDbType.NVarChar, 50) { Value = guide.Phone });
			cmd.Parameters.Add(new SqlParameter("@Email", SqlDbType.NVarChar, 200) { Value = (object?)guide.Email ?? DBNull.Value });
			cmd.Parameters.Add(new SqlParameter("@GuideImage", SqlDbType.NVarChar, 500) { Value = (object?)guide.GuideImage ?? DBNull.Value });
			cmd.Parameters.Add(new SqlParameter("@Description", SqlDbType.NVarChar, -1) { Value = (object?)guide.Description ?? DBNull.Value });
			cmd.Parameters.Add(new SqlParameter("@DefaultTourName", SqlDbType.NVarChar, 200) { Value = (object?)guide.DefaultTourName ?? DBNull.Value });
			cmd.Parameters.Add(new SqlParameter("@DefaultTourId", SqlDbType.Int) { Value = (object?)guide.DefaultTourId ?? DBNull.Value });
			cmd.Parameters.Add(new SqlParameter("@IsTouring", SqlDbType.Bit) { Value = guide.IsTouring });
			cmd.Parameters.Add(new SqlParameter("@AvailabilityNotes", SqlDbType.NVarChar, -1) { Value = (object?)guide.AvailabilityNotes ?? DBNull.Value });
			cmd.Parameters.Add(new SqlParameter("@AvailabilityUpdatedAt", SqlDbType.DateTime2) { Value = (object?)guide.AvailabilityUpdatedAt ?? DBNull.Value });
			cmd.Parameters.Add(new SqlParameter("@IsActive", SqlDbType.Bit) { Value = guide.IsActive });
			cmd.Parameters.Add(new SqlParameter("@HireDate", SqlDbType.Date) { Value = (object?)guide.HireDate ?? DBNull.Value });
			cmd.Parameters.Add(new SqlParameter("@TerminationDate", SqlDbType.Date) { Value = (object?)guide.TerminationDate ?? DBNull.Value });
			cmd.Parameters.Add(new SqlParameter("@Languages", SqlDbType.NVarChar, 200) { Value = (object?)guide.Languages ?? DBNull.Value });
			cmd.Parameters.Add(new SqlParameter("@Specialties", SqlDbType.NVarChar, 200) { Value = (object?)guide.Specialties ?? DBNull.Value });
			cmd.Parameters.Add(new SqlParameter("@Notes", SqlDbType.NVarChar, -1) { Value = (object?)guide.Notes ?? DBNull.Value });
		}

		private static async Task<DbGuide?> GetGuideByIdAsync(SqlConnection conn, int id, CancellationToken ct)
		{
			const string sql = @"
SELECT Id, FirstName, LastName, Phone, Email, GuideImage, Description,
       DefaultTourName, DefaultTourId, IsTouring, AvailabilityNotes, AvailabilityUpdatedAt,
       IsActive, HireDate, TerminationDate, Languages, Specialties, Notes,
       CreatedAt, UpdatedAt
FROM dbo.Guides
WHERE Id=@Id;";
			using var cmd = new SqlCommand(sql, conn);
			cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
			using var reader = await cmd.ExecuteReaderAsync(ct);
			if (await reader.ReadAsync(ct))
			{
				return MapGuide(reader);
			}
			return null;
		}

		public async Task UpdateGuideProfileAsync(int guideId, string email, string phone, string? imagePath)
		{
			using var conn = _connectionFactory.CreateOpenConnection();
			
			// 1. Get current guide to find old email (which is the username)
			var currentGuide = await GetGuideByIdAsync(conn, guideId, default);
			string? oldEmail = currentGuide?.Email;
			// 2026-02-20 00:00 UTC - Also sync users by phone when email is missing or mismatched.
			string? oldPhone = currentGuide?.Phone;

			// 2. Update Guide Table
			const string updateGuideSql = @"
UPDATE dbo.Guides
SET Email = @Email, Phone = @Phone, GuideImage = @GuideImage, UpdatedAt = GETUTCDATE()
WHERE Id = @Id;";

			using (var cmd = new SqlCommand(updateGuideSql, conn))
			{
				cmd.Parameters.AddWithValue("@Id", guideId);
				cmd.Parameters.AddWithValue("@Email", (object?)email ?? DBNull.Value);
				cmd.Parameters.AddWithValue("@Phone", (object?)phone ?? DBNull.Value);
				cmd.Parameters.AddWithValue("@GuideImage", (object?)imagePath ?? DBNull.Value);
				await cmd.ExecuteNonQueryAsync();
			}

			// 3. Update User Table (Sync Email and Phone)
			// Only if we had an old email to match against Username or Email
			if (!string.IsNullOrEmpty(oldEmail) || !string.IsNullOrWhiteSpace(oldPhone))
			{
				const string updateUserSql = @"
UPDATE dbo.Users
SET Email = @NewEmail,
    PhoneNumber = @NewPhone
WHERE Username = @OldEmail OR Email = @OldEmail OR PhoneNumber = @OldPhone;";

				using (var cmd = new SqlCommand(updateUserSql, conn))
				{
					cmd.Parameters.AddWithValue("@NewEmail", email);
					cmd.Parameters.AddWithValue("@NewPhone", (object?)phone ?? DBNull.Value);
					cmd.Parameters.AddWithValue("@OldEmail", (object?)oldEmail ?? DBNull.Value);
					cmd.Parameters.AddWithValue("@OldPhone", (object?)oldPhone ?? DBNull.Value);
					await cmd.ExecuteNonQueryAsync();
				}
			}
		}

		private static List<DbGuide> DeduplicateGuides(List<DbGuide> guides)
		{
			// 2026-02-21 00:00 UTC - De-dupe guides by phone/email to reduce duplicates in dropdowns.
			var byKey = new Dictionary<string, DbGuide>(StringComparer.OrdinalIgnoreCase);
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

		private static string BuildGuideKey(DbGuide guide)
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

			var name = NormalizeNameKey(guide.FirstName, guide.LastName);
			if (!string.IsNullOrWhiteSpace(name))
			{
				return $"name:{name}";
			}

			return $"id:{guide.Id}";
		}

		private static DbGuide PickPreferredGuide(DbGuide a, DbGuide b)
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

			var aUpdated = a.UpdatedAt ?? a.CreatedAt ?? DateTime.MinValue;
			var bUpdated = b.UpdatedAt ?? b.CreatedAt ?? DateTime.MinValue;
			if (aUpdated != bUpdated)
			{
				return aUpdated >= bUpdated ? a : b;
			}

			return a.Id >= b.Id ? a : b;
		}

		private static string? NormalizeOrNull(string? value)
		{
			if (string.IsNullOrWhiteSpace(value)) return null;
			return value.Trim();
		}

		private static string NormalizeNameKey(string? first, string? last)
		{
			var name = $"{NormalizeOrNull(first)} {NormalizeOrNull(last)}".Trim();
			return name.ToLowerInvariant();
		}

		private static string NormalizePhoneKey(string? value)
		{
			if (string.IsNullOrWhiteSpace(value)) return string.Empty;
			var digits = new string(value.Where(char.IsDigit).ToArray());
			if (string.IsNullOrWhiteSpace(digits)) return string.Empty;
			if (digits.Length >= 10)
			{
				return digits[^10..];
			}
			return digits;
		}

		private static bool IsLikelyEmail(string? value)
		{
			return !string.IsNullOrWhiteSpace(value) && value.Contains("@", StringComparison.Ordinal);
		}
	}
}


