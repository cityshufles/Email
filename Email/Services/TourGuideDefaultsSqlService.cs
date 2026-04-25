using System.Data;
using Microsoft.Data.SqlClient;
using Email.Models;

namespace Email.Services
{
	/// <summary>
	/// Created: 2025-11-20 (time not specified) - Direct SQL implementation for TourGuideDefaults.
	/// </summary>
	public sealed class TourGuideDefaultsSqlService : ITourGuideDefaultsApiService
	{
		private readonly SqlConnectionFactory _connectionFactory;

		public TourGuideDefaultsSqlService(SqlConnectionFactory connectionFactory)
		{
			_connectionFactory = connectionFactory;
		}

		public async Task<List<TourGuideDefault>> GetByTourAsync(int tourId, CancellationToken ct = default)
		{
			const string sql = @"
SELECT Id, TourId, DayOfWeek, StartTime, GuideId, IsActive, CreatedAt, UpdatedAt
FROM dbo.TourGuideDefaults
WHERE TourId=@TourId AND IsActive=1
ORDER BY DayOfWeek, StartTime;";
			var list = new List<TourGuideDefault>();
			using var conn = _connectionFactory.CreateOpenConnection();
			using var cmd = new SqlCommand(sql, conn);
			cmd.Parameters.Add(new SqlParameter("@TourId", SqlDbType.Int) { Value = tourId });
			using var reader = await cmd.ExecuteReaderAsync(ct);
			while (await reader.ReadAsync(ct))
			{
				list.Add(Map(reader));
			}
			return list;
		}

		public async Task<TourGuideDefault?> ResolveAsync(int tourId, int dayOfWeek, string startTime, CancellationToken ct = default)
		{
			const string sql = @"
SELECT TOP 1 Id, TourId, DayOfWeek, StartTime, GuideId, IsActive, CreatedAt, UpdatedAt
FROM dbo.TourGuideDefaults
WHERE TourId=@TourId AND DayOfWeek=@DayOfWeek AND StartTime=@StartTime AND IsActive=1
ORDER BY Id DESC;";
			using var conn = _connectionFactory.CreateOpenConnection();
			using var cmd = new SqlCommand(sql, conn);
			cmd.Parameters.Add(new SqlParameter("@TourId", SqlDbType.Int) { Value = tourId });
			cmd.Parameters.Add(new SqlParameter("@DayOfWeek", SqlDbType.Int) { Value = dayOfWeek });
			cmd.Parameters.Add(new SqlParameter("@StartTime", SqlDbType.NVarChar, 50) { Value = startTime });
			using var reader = await cmd.ExecuteReaderAsync(ct);
			if (await reader.ReadAsync(ct))
			{
				return Map(reader);
			}
			return null;
		}

		public async Task<TourGuideDefault?> CreateAsync(TourGuideDefault row, CancellationToken ct = default)
		{
			const string sql = @"
INSERT INTO dbo.TourGuideDefaults (TourId, DayOfWeek, StartTime, GuideId, IsActive)
VALUES (@TourId, @DayOfWeek, @StartTime, @GuideId, @IsActive);
SELECT CAST(SCOPE_IDENTITY() AS int);";
			using var conn = _connectionFactory.CreateOpenConnection();
			using var cmd = new SqlCommand(sql, conn);
			AddParams(cmd, row);
			var idObj = await cmd.ExecuteScalarAsync(ct);
			if (idObj is int id)
			{
				return await GetByIdAsync(conn, id, ct);
			}
			return null;
		}

		public async Task<bool> UpdateAsync(int id, TourGuideDefault row, CancellationToken ct = default)
		{
			const string sql = @"
UPDATE dbo.TourGuideDefaults
SET TourId=@TourId, DayOfWeek=@DayOfWeek, StartTime=@StartTime, GuideId=@GuideId, IsActive=@IsActive
WHERE Id=@Id;";
			using var conn = _connectionFactory.CreateOpenConnection();
			using var cmd = new SqlCommand(sql, conn);
			AddParams(cmd, row);
			cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
			var rows = await cmd.ExecuteNonQueryAsync(ct);
			return rows > 0;
		}

		public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
		{
			const string sql = "DELETE FROM dbo.TourGuideDefaults WHERE Id=@Id;";
			using var conn = _connectionFactory.CreateOpenConnection();
			using var cmd = new SqlCommand(sql, conn);
			cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
			var rows = await cmd.ExecuteNonQueryAsync(ct);
			return rows > 0;
		}

		private static TourGuideDefault Map(SqlDataReader r)
		{
			return new TourGuideDefault
			{
				Id = r.GetInt32(r.GetOrdinal("Id")),
				TourId = r.GetInt32(r.GetOrdinal("TourId")),
				DayOfWeek = r.GetInt32(r.GetOrdinal("DayOfWeek")),
				StartTime = r.GetString(r.GetOrdinal("StartTime")),
				GuideId = r.GetInt32(r.GetOrdinal("GuideId")),
				IsActive = r.GetBoolean(r.GetOrdinal("IsActive")),
				CreatedAt = r.IsDBNull(r.GetOrdinal("CreatedAt")) ? null : r.GetDateTime(r.GetOrdinal("CreatedAt")),
				UpdatedAt = r.IsDBNull(r.GetOrdinal("UpdatedAt")) ? null : r.GetDateTime(r.GetOrdinal("UpdatedAt"))
			};
		}

		private static void AddParams(SqlCommand cmd, TourGuideDefault row)
		{
			cmd.Parameters.Add(new SqlParameter("@TourId", SqlDbType.Int) { Value = row.TourId });
			cmd.Parameters.Add(new SqlParameter("@DayOfWeek", SqlDbType.Int) { Value = row.DayOfWeek });
			cmd.Parameters.Add(new SqlParameter("@StartTime", SqlDbType.NVarChar, 50) { Value = row.StartTime });
			cmd.Parameters.Add(new SqlParameter("@GuideId", SqlDbType.Int) { Value = row.GuideId });
			cmd.Parameters.Add(new SqlParameter("@IsActive", SqlDbType.Bit) { Value = row.IsActive });
		}

		private static async Task<TourGuideDefault?> GetByIdAsync(SqlConnection conn, int id, CancellationToken ct)
		{
			const string sql = @"
SELECT Id, TourId, DayOfWeek, StartTime, GuideId, IsActive, CreatedAt, UpdatedAt
FROM dbo.TourGuideDefaults
WHERE Id=@Id;";
			using var cmd = new SqlCommand(sql, conn);
			cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
			using var reader = await cmd.ExecuteReaderAsync(ct);
			if (await reader.ReadAsync(ct))
			{
				return Map(reader);
			}
			return null;
		}
	}
}


