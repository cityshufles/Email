using System.Data;
using Microsoft.Data.SqlClient;
using Email.Models;

namespace Email.Services
{
	/// <summary>
	/// Created: 2025-12-01 00:00 UTC - Repository for dbo.AutomaticGmail_TourDataErrorReports.
	/// </summary>
	public sealed class TourDataErrorReportsSqlService
	{
		private readonly SqlConnectionFactory _connectionFactory;
		private string? _lastError;
		public string? LastError => _lastError;

		public TourDataErrorReportsSqlService(SqlConnectionFactory connectionFactory)
		{
			_connectionFactory = connectionFactory;
		}

		public async Task<TourDataErrorReport?> GetByIdAsync(int id, CancellationToken ct = default)
		{
			_lastError = null;
			const string sql = @"
SELECT Id, ProcessedEmailId, MessageId, VendorName, ErrorNotes,
       BookingCode, CustomerIdentifier,
       OriginalSnapshotJson, CorrectedSnapshotJson, CreatedAt
FROM dbo.AutomaticGmail_TourDataErrorReports
WHERE Id=@Id;";
			try
			{
				using var conn = _connectionFactory.CreateOpenConnection();
				using var cmd = new SqlCommand(sql, conn);
				cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
				using var r = await cmd.ExecuteReaderAsync(ct);
				if (await r.ReadAsync(ct))
				{
					return Map(r);
				}
			}
			catch (Exception ex)
			{
				_lastError = ex.Message;
			}
			return null;
		}

		public async Task<List<TourDataErrorReport>> GetByMessageIdAsync(string messageId, CancellationToken ct = default)
		{
			_lastError = null;
			const string sql = @"
SELECT Id, ProcessedEmailId, MessageId, VendorName, ErrorNotes,
       BookingCode, CustomerIdentifier,
       OriginalSnapshotJson, CorrectedSnapshotJson, CreatedAt
FROM dbo.AutomaticGmail_TourDataErrorReports
WHERE MessageId=@MessageId
ORDER BY CreatedAt DESC;";
			var list = new List<TourDataErrorReport>();
			try
			{
				using var conn = _connectionFactory.CreateOpenConnection();
				using var cmd = new SqlCommand(sql, conn);
				cmd.Parameters.Add(new SqlParameter("@MessageId", SqlDbType.NVarChar, 255) { Value = messageId });
				using var r = await cmd.ExecuteReaderAsync(ct);
				while (await r.ReadAsync(ct))
				{
					list.Add(Map(r));
				}
			}
			catch (Exception ex)
			{
				_lastError = ex.Message;
			}
			return list;
		}

		public async Task<int?> CreateAsync(TourDataErrorReport report, CancellationToken ct = default)
		{
			_lastError = null;
			const string sql = @"
INSERT INTO dbo.AutomaticGmail_TourDataErrorReports
(ProcessedEmailId, MessageId, VendorName, BookingCode, CustomerIdentifier, ErrorNotes, OriginalSnapshotJson, CorrectedSnapshotJson)
VALUES
(@ProcessedEmailId, @MessageId, @VendorName, @BookingCode, @CustomerIdentifier, @ErrorNotes, @OriginalSnapshotJson, @CorrectedSnapshotJson);
SELECT CAST(SCOPE_IDENTITY() AS int);";
			try
			{
				using var conn = _connectionFactory.CreateOpenConnection();
				using var cmd = new SqlCommand(sql, conn);
				AddParams(cmd, report);
				var idObj = await cmd.ExecuteScalarAsync(ct);
				if (idObj is int id)
				{
					return id;
				}
			}
			catch (Exception ex)
			{
				_lastError = ex.Message;
			}
			return null;
		}

		private static TourDataErrorReport Map(SqlDataReader r)
		{
			return new TourDataErrorReport
			{
				Id = r.GetInt32(r.GetOrdinal("Id")),
				ProcessedEmailId = r.GetInt32(r.GetOrdinal("ProcessedEmailId")),
				MessageId = r.GetString(r.GetOrdinal("MessageId")),
				VendorName = r.IsDBNull(r.GetOrdinal("VendorName")) ? null : r.GetString(r.GetOrdinal("VendorName")),
				ErrorNotes = r.IsDBNull(r.GetOrdinal("ErrorNotes")) ? null : r.GetString(r.GetOrdinal("ErrorNotes")),
				BookingCode = r.IsDBNull(r.GetOrdinal("BookingCode")) ? null : r.GetString(r.GetOrdinal("BookingCode")),
				CustomerIdentifier = r.IsDBNull(r.GetOrdinal("CustomerIdentifier")) ? null : r.GetString(r.GetOrdinal("CustomerIdentifier")),
				OriginalSnapshotJson = r.GetString(r.GetOrdinal("OriginalSnapshotJson")),
				CorrectedSnapshotJson = r.IsDBNull(r.GetOrdinal("CorrectedSnapshotJson")) ? null : r.GetString(r.GetOrdinal("CorrectedSnapshotJson")),
				CreatedAt = r.GetDateTime(r.GetOrdinal("CreatedAt"))
			};
		}

		private static void AddParams(SqlCommand cmd, TourDataErrorReport m)
		{
			cmd.Parameters.Add(new SqlParameter("@ProcessedEmailId", SqlDbType.Int) { Value = m.ProcessedEmailId });
			cmd.Parameters.Add(new SqlParameter("@MessageId", SqlDbType.NVarChar, 255) { Value = m.MessageId });
			cmd.Parameters.Add(new SqlParameter("@VendorName", SqlDbType.NVarChar, 200) { Value = (object?)m.VendorName ?? DBNull.Value });
			cmd.Parameters.Add(new SqlParameter("@BookingCode", SqlDbType.NVarChar, 200) { Value = (object?)m.BookingCode ?? DBNull.Value });
			cmd.Parameters.Add(new SqlParameter("@CustomerIdentifier", SqlDbType.NVarChar, 200) { Value = (object?)m.CustomerIdentifier ?? DBNull.Value });
			cmd.Parameters.Add(new SqlParameter("@ErrorNotes", SqlDbType.NVarChar, -1) { Value = (object?)m.ErrorNotes ?? DBNull.Value });
			cmd.Parameters.Add(new SqlParameter("@OriginalSnapshotJson", SqlDbType.NVarChar, -1) { Value = m.OriginalSnapshotJson });
			cmd.Parameters.Add(new SqlParameter("@CorrectedSnapshotJson", SqlDbType.NVarChar, -1) { Value = (object?)m.CorrectedSnapshotJson ?? DBNull.Value });
		}

		public async Task<List<TourDataErrorReport>> GetRecentAsync(int limit = 200, CancellationToken ct = default)
		{
			_lastError = null;
			const string sql = @"
SELECT TOP (@Limit)
       Id, ProcessedEmailId, MessageId, VendorName, ErrorNotes,
       BookingCode, CustomerIdentifier,
       OriginalSnapshotJson, CorrectedSnapshotJson, CreatedAt
FROM dbo.AutomaticGmail_TourDataErrorReports
ORDER BY CreatedAt DESC;";
			var list = new List<TourDataErrorReport>();
			try
			{
				using var conn = _connectionFactory.CreateOpenConnection();
				using var cmd = new SqlCommand(sql, conn);
				cmd.Parameters.Add(new SqlParameter("@Limit", SqlDbType.Int) { Value = Math.Max(1, limit) });
				using var r = await cmd.ExecuteReaderAsync(ct);
				while (await r.ReadAsync(ct))
				{
					list.Add(Map(r));
				}
			}
			catch (Exception ex)
			{
				_lastError = ex.Message;
			}
			return list;
		}
	}
}


