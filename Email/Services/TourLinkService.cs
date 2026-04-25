using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Email.Models.Reports;

namespace Email.Services
{
    public interface ITourLinkService
    {
        Task<List<TourLink>> GetAllAsync();
        Task<TourLink?> GetByIdAsync(int id);
        Task<List<TourLink>> GetLinksByVendorAsync(string vendor);
        Task<List<TourLink>> GetLinksForTourAsync(int? tourId, string? tourName);
        Task<int> SaveAsync(TourLink link);
        Task<bool> DeleteAsync(int id);
    }

    public class TourLinkService : ITourLinkService
    {
        private readonly SqlConnectionFactory _sqlConnectionFactory;

        public TourLinkService(SqlConnectionFactory sqlConnectionFactory)
        {
            _sqlConnectionFactory = sqlConnectionFactory;
        }

        private IDbConnection CreateConnection() => _sqlConnectionFactory.CreateOpenConnection();

        public async Task<List<TourLink>> GetAllAsync()
        {
            using var conn = CreateConnection();
            const string sql = @"
                SELECT 
                    Id,
                    Vendor,
                    TourName,
                    DaysTimesAvailable,
                    TourDay,
                    TourTime,
                    ProductId,
                    ReviewLink,
                    TourLink AS TourLinkUrl,
                    Notes,
                    CreatedAt,
                    UpdatedAt,
                    TourId
                FROM dbo.TourLinks
                ORDER BY Vendor, TourName";
            var result = await conn.QueryAsync<TourLink>(sql);
            return result.AsList();
        }

        public async Task<TourLink?> GetByIdAsync(int id)
        {
            using var conn = CreateConnection();
            const string sql = @"
                SELECT 
                    Id,
                    Vendor,
                    TourName,
                    DaysTimesAvailable,
                    TourDay,
                    TourTime,
                    ProductId,
                    ReviewLink,
                    TourLink AS TourLinkUrl,
                    Notes,
                    CreatedAt,
                    UpdatedAt,
                    TourId
                FROM dbo.TourLinks
                WHERE Id = @Id";
            return await conn.QueryFirstOrDefaultAsync<TourLink>(sql, new { Id = id });
        }

        public async Task<List<TourLink>> GetLinksByVendorAsync(string vendor)
        {
            using var conn = CreateConnection();
            // Simple LIKE match or exact match? Requirement says "same vendor". 
            // Let's do case-insensitive exact match or simple LIKE. 
            // Users might type "Viator" or "Viator Inc". 
            // Let's try exact match first for safety, or assume consistent data entry.
            const string sql = @"
                SELECT 
                    Id,
                    Vendor,
                    TourName,
                    DaysTimesAvailable,
                    TourDay,
                    TourTime,
                    ProductId,
                    ReviewLink,
                    TourLink AS TourLinkUrl,
                    Notes,
                    CreatedAt,
                    UpdatedAt,
                    TourId
                FROM dbo.TourLinks
                WHERE Vendor = @Vendor
                ORDER BY TourName";
            var result = await conn.QueryAsync<TourLink>(sql, new { Vendor = vendor });
            return result.AsList();
        }

        public async Task<List<TourLink>> GetLinksForTourAsync(int? tourId, string? tourName)
        {
            using var conn = CreateConnection();
            const string selectSql = @"
                SELECT 
                    Id,
                    Vendor,
                    TourName,
                    DaysTimesAvailable,
                    TourDay,
                    TourTime,
                    ProductId,
                    ReviewLink,
                    TourLink AS TourLinkUrl,
                    Notes,
                    CreatedAt,
                    UpdatedAt,
                    TourId
                FROM dbo.TourLinks";

            if (tourId.HasValue && tourId.Value > 0)
            {
                var byIdSql = $@"
{selectSql}
                WHERE TourId = @TourId
                ORDER BY Vendor, TourName";

                var byIdResult = await conn.QueryAsync<TourLink>(byIdSql, new { TourId = tourId.Value });
                return byIdResult.AsList();
            }

            var normalizedTourName = string.IsNullOrWhiteSpace(tourName) ? null : tourName.Trim();
            if (!string.IsNullOrWhiteSpace(normalizedTourName))
            {
                var byNameSql = $@"
{selectSql}
                WHERE TourName = @TourName
                ORDER BY Vendor, TourName";

                var byNameResult = await conn.QueryAsync<TourLink>(byNameSql, new { TourName = normalizedTourName });
                return byNameResult.AsList();
            }

            return new List<TourLink>();
        }

        public async Task<int> SaveAsync(TourLink link)
        {
            using var conn = CreateConnection();
            
            if (link.Id > 0)
            {
                const string updateSql = @"
                    UPDATE dbo.TourLinks 
                    SET Vendor = @Vendor,
                        TourName = @TourName,
                        TourId = @TourId,
                        DaysTimesAvailable = @DaysTimesAvailable,
                        TourDay = @TourDay,
                        TourTime = @TourTime,
                        ProductId = @ProductId,
                        ReviewLink = @ReviewLink,
                        TourLink = @TourLinkUrl,
                        Notes = @Notes,
                        UpdatedAt = SYSUTCDATETIME()
                    WHERE Id = @Id";
                await conn.ExecuteAsync(updateSql, link);
                return link.Id;
            }
            else
            {
                const string insertSql = @"
                    INSERT INTO dbo.TourLinks (Vendor, TourName, TourId, DaysTimesAvailable, TourDay, TourTime, ProductId, ReviewLink, TourLink, Notes, UpdatedAt)
                    VALUES (@Vendor, @TourName, @TourId, @DaysTimesAvailable, @TourDay, @TourTime, @ProductId, @ReviewLink, @TourLinkUrl, @Notes, SYSUTCDATETIME());
                    SELECT CAST(SCOPE_IDENTITY() as int)";
                return await conn.ExecuteScalarAsync<int>(insertSql, link);
            }
        }

        public async Task<bool> DeleteAsync(int id)
        {
            using var conn = CreateConnection();
            const string sql = "DELETE FROM dbo.TourLinks WHERE Id = @Id";
            var rows = await conn.ExecuteAsync(sql, new { Id = id });
            return rows > 0;
        }
    }
}
