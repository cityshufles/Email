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
    /// Updated: 2025-11-26 00:00 UTC - Added GetByIdAsync for modification/cancellation flows
    /// SQL Server implementation for customers repository.
    /// </summary>
    public sealed class SqlServerCustomerRepository : ICustomerRepository
    {
        private readonly string _connectionString;

        public SqlServerCustomerRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("AutomaticGmailSqlServer") ?? throw new InvalidOperationException("Missing ConnectionStrings:AutomaticGmailSqlServer");
        }

        private IDbConnection CreateConnection() => new SqlConnection(_connectionString);

        /// <summary>
        /// Get customer by primary key ID
        /// Added: 2025-11-26 00:00 UTC
        /// </summary>
        public async Task<Customer?> GetByIdAsync(int customerId, CancellationToken cancellationToken)
        {
            const string sql = @"
SELECT *
FROM dbo.Customers
WHERE Id = @Id;";
            using var conn = CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<Customer>(new CommandDefinition(sql, new { Id = customerId }, cancellationToken: cancellationToken));
        }

        public async Task<Customer?> GetByBookingCodeAsync(string bookingCode, CancellationToken cancellationToken)
        {
            const string sql = @"
SELECT TOP 1 c.*
FROM dbo.Customers c
INNER JOIN dbo.Bookings b ON b.CustomerId = c.Id
WHERE b.BookingCode = @BookingCode
ORDER BY b.UpdatedAt DESC;";
            using var conn = CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<Customer>(new CommandDefinition(sql, new { BookingCode = bookingCode }, cancellationToken: cancellationToken));
        }

        public async Task<Customer?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
        {
            const string sql = @"
SELECT TOP 1 *
FROM dbo.Customers
WHERE LOWER(Email) = LOWER(@Email)
ORDER BY UpdatedAt DESC;";
            using var conn = CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<Customer>(new CommandDefinition(sql, new { Email = normalizedEmail }, cancellationToken: cancellationToken));
        }

        public async Task<Customer?> GetByPhoneAsync(string normalizedPhone, CancellationToken cancellationToken)
        {
            const string sql = @"
SELECT TOP 1 *
FROM dbo.Customers
WHERE REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(PhoneNumber,''),'-',''),'(',''),')',''),' ','') LIKE '%' + @Digits
ORDER BY UpdatedAt DESC;";
            using var conn = CreateConnection();
            // Use last 7 digits for matching
            var digits = new string(normalizedPhone.Where(char.IsDigit).ToArray());
            var last7 = digits.Length > 7 ? digits[^7..] : digits;
            return await conn.QueryFirstOrDefaultAsync<Customer>(new CommandDefinition(sql, new { Digits = last7 }, cancellationToken: cancellationToken));
        }

        public async Task<int> CreateAsync(Customer customer, CancellationToken cancellationToken)
        {
            const string sql = @"
INSERT INTO dbo.Customers (
    FullName, FirstName, LastName, PhoneNumber, Email, CustomerIdentifier, BookingIds, TotalBookings, CreatedAt, UpdatedAt
)
OUTPUT inserted.Id
VALUES (
    @FullName, @FirstName, @LastName, @PhoneNumber, @Email, @CustomerIdentifier, @BookingIds, @TotalBookings, SYSUTCDATETIME(), SYSUTCDATETIME()
);";
            using var conn = CreateConnection();
            return await conn.QuerySingleAsync<int>(new CommandDefinition(sql, customer, cancellationToken: cancellationToken));
        }

        public async Task UpdateAsync(Customer customer, CancellationToken cancellationToken)
        {
            const string sql = @"
UPDATE dbo.Customers
SET
    FullName = @FullName,
    FirstName = @FirstName,
    LastName = @LastName,
    PhoneNumber = @PhoneNumber,
    Email = @Email,
    CustomerIdentifier = @CustomerIdentifier,
    BookingIds = @BookingIds,
    TotalBookings = @TotalBookings,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id;";
            using var conn = CreateConnection();
            await conn.ExecuteAsync(new CommandDefinition(sql, customer, cancellationToken: cancellationToken));
        }

        public async Task<IReadOnlyList<Customer>> FindCandidatesByNameAndPhoneAsync(string customerName, string? phoneLast7, CancellationToken cancellationToken)
        {
            const string sql = @"
SELECT TOP 25 *
FROM dbo.Customers
WHERE LOWER(FullName) LIKE '%' + LOWER(@Name) + '%'
  AND (@Phone IS NULL OR REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(PhoneNumber,''),'-',''),'(',''),')',''),' ','') LIKE '%' + @Phone)
ORDER BY UpdatedAt DESC;";
            using var conn = CreateConnection();
            var rows = await conn.QueryAsync<Customer>(new CommandDefinition(sql, new { Name = customerName, Phone = phoneLast7 }, cancellationToken: cancellationToken));
            return rows.ToList();
        }

        public async Task<IReadOnlyList<Customer>> FindCandidatesByEmailSimilarityAsync(string emailUser, string emailDomain, CancellationToken cancellationToken)
        {
            const string sql = @"
SELECT TOP 25 *
FROM dbo.Customers
WHERE (Email IS NOT NULL)
  AND (
        LOWER(Email) LIKE LOWER(@UserLike)
     OR LOWER(Email) LIKE LOWER(@DomainLike)
  )
ORDER BY UpdatedAt DESC;";
            using var conn = CreateConnection();
            var rows = await conn.QueryAsync<Customer>(new CommandDefinition(sql, new
            {
                UserLike = $"{emailUser}%",
                DomainLike = $"%@{emailDomain}"
            }, cancellationToken: cancellationToken));
            return rows.ToList();
        }

        public async Task<IReadOnlyList<Customer>> GetRecentAsync(int limit, CancellationToken cancellationToken)
        {
            const string sql = @"
SELECT TOP (@Limit) *
FROM dbo.Customers
ORDER BY UpdatedAt DESC, Id DESC;";
            using var conn = CreateConnection();
            var rows = await conn.QueryAsync<Customer>(new CommandDefinition(sql, new { Limit = limit }, cancellationToken: cancellationToken));
            return rows.ToList();
        }

        public async Task DeleteAsync(IReadOnlyList<int> customerIds, CancellationToken cancellationToken)
        {
            if (customerIds == null || customerIds.Count == 0) return;
            const string sql = @"
DELETE FROM dbo.Bookings WHERE CustomerId IN @Ids;
DELETE FROM dbo.Customers WHERE Id IN @Ids;";
            using var conn = CreateConnection();
            await conn.ExecuteAsync(new CommandDefinition(sql, new { Ids = customerIds }, cancellationToken: cancellationToken));
        }
    }
}


