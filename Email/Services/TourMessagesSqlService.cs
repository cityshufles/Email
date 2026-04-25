using System.Data;
using Microsoft.Data.SqlClient;
using Email.Models;

namespace Email.Services
{
	/// <summary>
	/// Created: 2025-11-20 (time not specified) - Direct SQL implementation for TourMessages CRUD against dbo.TourMessages.
	/// </summary>
	public sealed class TourMessagesSqlService : ITourMessagesApiService
	{
		private readonly SqlConnectionFactory _connectionFactory;
		private string? _lastError;
		public string? LastError => _lastError;

		public TourMessagesSqlService(SqlConnectionFactory connectionFactory)
		{
			_connectionFactory = connectionFactory;
		}

		public async Task<List<DbTourMessage>> GetTourMessagesAsync(CancellationToken ct = default)
		{
			_lastError = null;
			const string sql = @"
SELECT Id, MessageName, MessageType, TourName, TourId, TourStartTime, GuideId,
       MeetingPlaceId, MeetingPlace, MeetingTime, MessageContent, Signature,
       Description, VendorLink, IsActive, CreatedAt, UpdatedAt
FROM dbo.TourMessages
ORDER BY MessageName;";
			var list = new List<DbTourMessage>();
			try
			{
				using var conn = _connectionFactory.CreateOpenConnection();
				using var cmd = new SqlCommand(sql, conn);
				using var reader = await cmd.ExecuteReaderAsync(ct);
				while (await reader.ReadAsync(ct))
				{
					list.Add(Map(reader));
				}
			}
			catch (Exception ex)
			{
				_lastError = ex.Message;
			}
			return list;
		}

		public async Task<DbTourMessage?> GetTourMessageByIdAsync(int id, CancellationToken ct = default)
		{
			_lastError = null;
			const string sql = @"
SELECT Id, MessageName, MessageType, TourName, TourId, TourStartTime, GuideId,
       MeetingPlaceId, MeetingPlace, MeetingTime, MessageContent, Signature,
       Description, VendorLink, IsActive, CreatedAt, UpdatedAt
FROM dbo.TourMessages
WHERE Id=@Id;";
			try
			{
				using var conn = _connectionFactory.CreateOpenConnection();
				using var cmd = new SqlCommand(sql, conn);
				cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
				using var reader = await cmd.ExecuteReaderAsync(ct);
				if (await reader.ReadAsync(ct))
				{
					return Map(reader);
				}
			}
			catch (Exception ex)
			{
				_lastError = ex.Message;
			}
			return null;
		}

		public async Task<DbTourMessage?> CreateTourMessageAsync(DbTourMessage message, CancellationToken ct = default)
		{
			_lastError = null;
			const string sql = @"
INSERT INTO dbo.TourMessages
(MessageName, MessageType, TourName, TourId, TourStartTime, GuideId,
 MeetingPlaceId, MeetingPlace, MeetingTime, MessageContent, Signature,
 Description, VendorLink, IsActive)
VALUES
(@MessageName, @MessageType, @TourName, @TourId, @TourStartTime, @GuideId,
 @MeetingPlaceId, @MeetingPlace, @MeetingTime, @MessageContent, @Signature,
 @Description, @VendorLink, @IsActive);
SELECT CAST(SCOPE_IDENTITY() AS int);";
			try
			{
				using var conn = _connectionFactory.CreateOpenConnection();
				using var cmd = new SqlCommand(sql, conn);
				AddParams(cmd, message);
				var idObj = await cmd.ExecuteScalarAsync(ct);
				if (idObj is int id)
				{
					return await GetTourMessageByIdAsync(id, ct);
				}
			}
			catch (Exception ex)
			{
				_lastError = ex.Message;
			}
			return null;
		}

		public async Task<bool> UpdateTourMessageAsync(int id, DbTourMessage message, CancellationToken ct = default)
		{
			_lastError = null;
			const string sql = @"
UPDATE dbo.TourMessages
SET MessageName=@MessageName, MessageType=@MessageType, TourName=@TourName, TourId=@TourId,
    TourStartTime=@TourStartTime, GuideId=@GuideId, MeetingPlaceId=@MeetingPlaceId,
    MeetingPlace=@MeetingPlace, MeetingTime=@MeetingTime, MessageContent=@MessageContent,
    Signature=@Signature, Description=@Description, VendorLink=@VendorLink, IsActive=@IsActive
WHERE Id=@Id;";
			try
			{
				using var conn = _connectionFactory.CreateOpenConnection();
				using var cmd = new SqlCommand(sql, conn);
				AddParams(cmd, message);
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

		public async Task<bool> DeleteTourMessageAsync(int id, CancellationToken ct = default)
		{
			_lastError = null;
			const string sql = "DELETE FROM dbo.TourMessages WHERE Id=@Id;";
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

		private static DbTourMessage Map(SqlDataReader r)
		{
			return new DbTourMessage
			{
				Id = r.GetInt32(r.GetOrdinal("Id")),
				MessageName = r.GetString(r.GetOrdinal("MessageName")),
				MessageType = r.GetString(r.GetOrdinal("MessageType")),
				TourName = r.GetString(r.GetOrdinal("TourName")),
				TourId = r.IsDBNull(r.GetOrdinal("TourId")) ? null : r.GetInt32(r.GetOrdinal("TourId")),
				TourStartTime = r.IsDBNull(r.GetOrdinal("TourStartTime")) ? null : r.GetString(r.GetOrdinal("TourStartTime")),
				GuideId = r.IsDBNull(r.GetOrdinal("GuideId")) ? null : r.GetInt32(r.GetOrdinal("GuideId")),
				MeetingPlaceId = r.IsDBNull(r.GetOrdinal("MeetingPlaceId")) ? null : r.GetInt32(r.GetOrdinal("MeetingPlaceId")),
				MeetingPlace = r.IsDBNull(r.GetOrdinal("MeetingPlace")) ? null : r.GetString(r.GetOrdinal("MeetingPlace")),
				MeetingTime = r.IsDBNull(r.GetOrdinal("MeetingTime")) ? null : r.GetString(r.GetOrdinal("MeetingTime")),
				MessageContent = r.GetString(r.GetOrdinal("MessageContent")),
				Signature = r.IsDBNull(r.GetOrdinal("Signature")) ? null : r.GetString(r.GetOrdinal("Signature")),
				Description = r.IsDBNull(r.GetOrdinal("Description")) ? null : r.GetString(r.GetOrdinal("Description")),
				VendorLink = r.IsDBNull(r.GetOrdinal("VendorLink")) ? null : r.GetString(r.GetOrdinal("VendorLink")),
				IsActive = r.GetBoolean(r.GetOrdinal("IsActive")),
				CreatedAt = r.IsDBNull(r.GetOrdinal("CreatedAt")) ? null : r.GetDateTime(r.GetOrdinal("CreatedAt")),
				UpdatedAt = r.IsDBNull(r.GetOrdinal("UpdatedAt")) ? null : r.GetDateTime(r.GetOrdinal("UpdatedAt")),
			};
		}

		private static void AddParams(SqlCommand cmd, DbTourMessage m)
		{
			cmd.Parameters.Add(new SqlParameter("@MessageName", SqlDbType.NVarChar, 200) { Value = m.MessageName });
			cmd.Parameters.Add(new SqlParameter("@MessageType", SqlDbType.NVarChar, 50) { Value = m.MessageType });
			cmd.Parameters.Add(new SqlParameter("@TourName", SqlDbType.NVarChar, 200) { Value = m.TourName });
			cmd.Parameters.Add(new SqlParameter("@TourId", SqlDbType.Int) { Value = (object?)m.TourId ?? DBNull.Value });
			cmd.Parameters.Add(new SqlParameter("@TourStartTime", SqlDbType.NVarChar, 50) { Value = (object?)m.TourStartTime ?? DBNull.Value });
			cmd.Parameters.Add(new SqlParameter("@GuideId", SqlDbType.Int) { Value = (object?)m.GuideId ?? DBNull.Value });
			cmd.Parameters.Add(new SqlParameter("@MeetingPlaceId", SqlDbType.Int) { Value = (object?)m.MeetingPlaceId ?? DBNull.Value });
			cmd.Parameters.Add(new SqlParameter("@MeetingPlace", SqlDbType.NVarChar, 200) { Value = (object?)m.MeetingPlace ?? DBNull.Value });
			cmd.Parameters.Add(new SqlParameter("@MeetingTime", SqlDbType.NVarChar, 50) { Value = (object?)m.MeetingTime ?? DBNull.Value });
			cmd.Parameters.Add(new SqlParameter("@MessageContent", SqlDbType.NVarChar, -1) { Value = m.MessageContent });
			cmd.Parameters.Add(new SqlParameter("@Signature", SqlDbType.NVarChar, 500) { Value = (object?)m.Signature ?? DBNull.Value });
			cmd.Parameters.Add(new SqlParameter("@Description", SqlDbType.NVarChar, 500) { Value = (object?)m.Description ?? DBNull.Value });
			cmd.Parameters.Add(new SqlParameter("@VendorLink", SqlDbType.NVarChar, 500) { Value = (object?)m.VendorLink ?? DBNull.Value });
			cmd.Parameters.Add(new SqlParameter("@IsActive", SqlDbType.Bit) { Value = m.IsActive });
		}
	}
}


