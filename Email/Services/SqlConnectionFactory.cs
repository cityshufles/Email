using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Email.Services
{
	/// <summary>
	/// Created: 2025-11-20 (time not specified) - Simple factory to create open SQL connections from configuration.
	/// </summary>
	public sealed class SqlConnectionFactory
	{
		private readonly string _connectionString;

		public SqlConnectionFactory(IConfiguration configuration)
		{
			_connectionString = configuration.GetConnectionString("AutomaticGmailSqlServer")
				?? throw new InvalidOperationException("Missing connection string: AutomaticGmailSqlServer");
		}

		public SqlConnection CreateOpenConnection()
		{
			var conn = new SqlConnection(_connectionString);
			conn.Open();
			return conn;
		}
	}
}


