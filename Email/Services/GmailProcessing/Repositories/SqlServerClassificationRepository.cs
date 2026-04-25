using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Email.Services.GmailProcessing.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Email.Services.GmailProcessing.Repositories
{
    /// <summary>
    /// Created: 2025-11-07 00:00 UTC
    /// SQL Server implementation for classification rules repository.
    /// </summary>
    public sealed class SqlServerClassificationRepository : IClassificationRepository
    {
        private readonly string _connectionString;

        public SqlServerClassificationRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("AutomaticGmailSqlServer") ?? throw new InvalidOperationException("Missing ConnectionStrings:AutomaticGmailSqlServer");
        }

        private IDbConnection CreateConnection() => new SqlConnection(_connectionString);

        public async Task<IReadOnlyList<ClassificationRuleRecord>> LoadActiveClassificationRulesAsync(CancellationToken cancellationToken)
        {
            const string sql = @"
SELECT Id, VendorName, Domain, SubjectPhrase, EmailType, IsActive, Priority, CreatedAt, UpdatedAt
FROM dbo.AutomaticGmail_EmailClassificationRules
WHERE IsActive = 1
ORDER BY Priority DESC, Id ASC;";
            using var conn = CreateConnection();
            var rows = await conn.QueryAsync<ClassificationRuleRecord>(new CommandDefinition(sql, cancellationToken: cancellationToken));
            return rows.ToList();
        }
    }
}


