using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Email.Models.Reports;

namespace Email.Services
{
    public interface IPublicGalleryService
    {
        Task<GallerySettings> GetSettingsAsync();
        Task SaveSettingsAsync(GallerySettings settings);
    }

    public class PublicGalleryService : IPublicGalleryService
    {
        private readonly SqlConnectionFactory _sqlConnectionFactory;

        public PublicGalleryService(SqlConnectionFactory sqlConnectionFactory)
        {
            _sqlConnectionFactory = sqlConnectionFactory;
        }

        private IDbConnection CreateConnection() => _sqlConnectionFactory.CreateOpenConnection();

        public async Task<GallerySettings> GetSettingsAsync()
        {
            using var conn = CreateConnection();
            const string sql = "SELECT TOP 1 * FROM dbo.GallerySettings ORDER BY Id DESC";
            var settings = await conn.QueryFirstOrDefaultAsync<GallerySettings>(sql);
            
            if (settings == null)
            {
                // Return default settings if DB is empty
                return new GallerySettings
                {
                    HeaderText = "<h1 class=\"display-6\">Thanks for joining us!</h1><p class=\"lead\">Here are the photos from your <strong>{TourName}</strong> tour.</p>",
                    FooterLinksJson = "[{\"Title\":\"Food Tours\",\"Url\":\"#\",\"CardText\":\"Taste the best of the city.\"},{\"Title\":\"History Walks\",\"Url\":\"#\",\"CardText\":\"Dive deep into the past.\"}]"
                };
            }
            return settings;
        }

        public async Task SaveSettingsAsync(GallerySettings settings)
        {
            using var conn = CreateConnection();
            
            // Check if exists
            const string checkSql = "SELECT TOP 1 Id FROM dbo.GallerySettings ORDER BY Id DESC";
            var existingId = await conn.ExecuteScalarAsync<int?>(checkSql);

            if (existingId.HasValue)
            {
                const string updateSql = @"
                    UPDATE dbo.GallerySettings 
                    SET HeaderText = @HeaderText, 
                        FooterLinksJson = @FooterLinksJson, 
                        UpdatedAt = SYSUTCDATETIME()
                    WHERE Id = @Id";
                settings.Id = existingId.Value;
                await conn.ExecuteAsync(updateSql, settings);
            }
            else
            {
                const string insertSql = @"
                    INSERT INTO dbo.GallerySettings (HeaderText, FooterLinksJson, UpdatedAt)
                    VALUES (@HeaderText, @FooterLinksJson, SYSUTCDATETIME())";
                await conn.ExecuteAsync(insertSql, settings);
            }
        }
    }
}
