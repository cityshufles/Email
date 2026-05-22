using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Email.Models.Resources;

namespace Email.Services.Resources
{
    public sealed class ResourceSqlService : IResourceService
    {
        private readonly SqlConnectionFactory _connectionFactory;
        private readonly ILogger<ResourceSqlService> _logger;

        public ResourceSqlService(SqlConnectionFactory connectionFactory, ILogger<ResourceSqlService> logger)
        {
            _connectionFactory = connectionFactory;
            _logger = logger;
        }

        public async Task<List<ResourceItem>> GetAllResourcesAsync(CancellationToken ct = default)
        {
            const string sql = @"
                SELECT r.Id, r.FileName, r.OriginalFileName, r.FileType, r.FileSizeBytes,
                       r.RelativePath, r.Description, r.UploadedByUserId, r.UploadedByName,
                       r.IsActive, r.CreatedAtUtc, r.UpdatedAtUtc
                FROM dbo.Resources r
                WHERE r.IsActive = 1
                ORDER BY r.CreatedAtUtc DESC;

                SELECT rt.ResourceId, rt.TourId, t.TourName
                FROM dbo.ResourceTours rt
                INNER JOIN dbo.Tours t ON t.Id = rt.TourId
                INNER JOIN dbo.Resources r ON r.Id = rt.ResourceId
                WHERE r.IsActive = 1;";

            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                using var multi = await conn.QueryMultipleAsync(sql);
                var resources = (await multi.ReadAsync<ResourceItem>()).ToList();
                var tourMappings = (await multi.ReadAsync<(int ResourceId, int TourId, string TourName)>()).ToList();

                foreach (var resource in resources)
                {
                    var mappings = tourMappings.Where(m => m.ResourceId == resource.Id).ToList();
                    resource.TourIds = mappings.Select(m => m.TourId).ToList();
                    resource.TourNames = mappings.Select(m => m.TourName).ToList();
                }

                return resources;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all resources");
                return new List<ResourceItem>();
            }
        }

        public async Task<List<ResourceItem>> GetResourcesByTourAsync(int tourId, CancellationToken ct = default)
        {
            const string sql = @"
                SELECT r.Id, r.FileName, r.OriginalFileName, r.FileType, r.FileSizeBytes,
                       r.RelativePath, r.Description, r.UploadedByUserId, r.UploadedByName,
                       r.IsActive, r.CreatedAtUtc, r.UpdatedAtUtc
                FROM dbo.Resources r
                INNER JOIN dbo.ResourceTours rt ON rt.ResourceId = r.Id
                WHERE r.IsActive = 1 AND rt.TourId = @TourId
                ORDER BY r.CreatedAtUtc DESC;

                SELECT rt2.ResourceId, rt2.TourId, t.TourName
                FROM dbo.ResourceTours rt2
                INNER JOIN dbo.Tours t ON t.Id = rt2.TourId
                WHERE rt2.ResourceId IN (
                    SELECT r2.Id FROM dbo.Resources r2
                    INNER JOIN dbo.ResourceTours rt3 ON rt3.ResourceId = r2.Id
                    WHERE r2.IsActive = 1 AND rt3.TourId = @TourId
                );";

            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                using var multi = await conn.QueryMultipleAsync(sql, new { TourId = tourId });
                var resources = (await multi.ReadAsync<ResourceItem>()).ToList();
                var tourMappings = (await multi.ReadAsync<(int ResourceId, int TourId, string TourName)>()).ToList();

                foreach (var resource in resources)
                {
                    var mappings = tourMappings.Where(m => m.ResourceId == resource.Id).ToList();
                    resource.TourIds = mappings.Select(m => m.TourId).ToList();
                    resource.TourNames = mappings.Select(m => m.TourName).ToList();
                }

                return resources;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching resources for tour {TourId}", tourId);
                return new List<ResourceItem>();
            }
        }

        public async Task<int> CreateResourceAsync(ResourceItem resource, CancellationToken ct = default)
        {
            const string sql = @"
                INSERT INTO dbo.Resources (FileName, OriginalFileName, FileType, FileSizeBytes,
                    RelativePath, Description, UploadedByUserId, UploadedByName)
                VALUES (@FileName, @OriginalFileName, @FileType, @FileSizeBytes,
                    @RelativePath, @Description, @UploadedByUserId, @UploadedByName);
                SELECT CAST(SCOPE_IDENTITY() AS INT);";

            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                var id = await conn.QuerySingleAsync<int>(sql, new
                {
                    resource.FileName,
                    resource.OriginalFileName,
                    resource.FileType,
                    resource.FileSizeBytes,
                    resource.RelativePath,
                    resource.Description,
                    resource.UploadedByUserId,
                    resource.UploadedByName
                });

                if (resource.TourIds.Count > 0)
                {
                    await InsertTourMappingsAsync(conn, id, resource.TourIds);
                }

                return id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating resource {FileName}", resource.FileName);
                throw;
            }
        }

        public async Task UpdateResourceToursAsync(int resourceId, IEnumerable<int> tourIds, CancellationToken ct = default)
        {
            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                await conn.ExecuteAsync("DELETE FROM dbo.ResourceTours WHERE ResourceId = @ResourceId",
                    new { ResourceId = resourceId });
                await InsertTourMappingsAsync(conn, resourceId, tourIds.ToList());
                await conn.ExecuteAsync(
                    "UPDATE dbo.Resources SET UpdatedAtUtc = SYSUTCDATETIME() WHERE Id = @Id",
                    new { Id = resourceId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating tours for resource {ResourceId}", resourceId);
                throw;
            }
        }

        public async Task DeleteResourceAsync(int resourceId, CancellationToken ct = default)
        {
            const string sql = @"
                UPDATE dbo.Resources SET IsActive = 0, UpdatedAtUtc = SYSUTCDATETIME()
                WHERE Id = @Id;";

            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                await conn.ExecuteAsync(sql, new { Id = resourceId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting resource {ResourceId}", resourceId);
                throw;
            }
        }

        private static async Task InsertTourMappingsAsync(
            Microsoft.Data.SqlClient.SqlConnection conn,
            int resourceId,
            List<int> tourIds)
        {
            if (tourIds.Count == 0) return;
            const string insertSql = @"
                INSERT INTO dbo.ResourceTours (ResourceId, TourId)
                VALUES (@ResourceId, @TourId);";
            await conn.ExecuteAsync(insertSql,
                tourIds.Select(tid => new { ResourceId = resourceId, TourId = tid }));
        }
    }
}
