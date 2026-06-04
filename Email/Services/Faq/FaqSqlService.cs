using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Email.Models.Faq;

namespace Email.Services.Faq
{
    public sealed class FaqSqlService : IFaqService
    {
        private readonly SqlConnectionFactory _connectionFactory;
        private readonly ILogger<FaqSqlService> _logger;

        public FaqSqlService(SqlConnectionFactory connectionFactory, ILogger<FaqSqlService> logger)
        {
            _connectionFactory = connectionFactory;
            _logger = logger;
        }

        public async Task<List<FaqCategory>> GetCategoriesWithItemsAsync(CancellationToken ct = default)
        {
            const string sql = @"
                SELECT Id, Name, SortOrder, IsActive, CreatedAtUtc, UpdatedAtUtc
                FROM dbo.FaqCategories
                WHERE IsActive = 1
                ORDER BY SortOrder, Name;

                SELECT Id, CategoryId, Question, SortOrder, IsActive, CreatedByUserId, IsLocked, CreatedAtUtc, UpdatedAtUtc
                FROM dbo.FaqItems
                WHERE IsActive = 1
                ORDER BY SortOrder, CreatedAtUtc;

                SELECT Id, FaqItemId, AuthorUserId, AuthorName, Content, IsAccepted, IsActive, CreatedAtUtc, UpdatedAtUtc
                FROM dbo.FaqAnswers
                WHERE IsActive = 1
                ORDER BY IsAccepted DESC, CreatedAtUtc;";

            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                using var multi = await conn.QueryMultipleAsync(sql);
                var categories = (await multi.ReadAsync<FaqCategory>()).ToList();
                var items = (await multi.ReadAsync<FaqItem>()).ToList();
                var answers = (await multi.ReadAsync<FaqAnswer>()).ToList();

                var answersByItem = answers.GroupBy(a => a.FaqItemId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                foreach (var item in items)
                {
                    if (answersByItem.TryGetValue(item.Id, out var itemAnswers))
                        item.Answers = itemAnswers;
                }

                var itemsByCategory = items.GroupBy(i => i.CategoryId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                foreach (var cat in categories)
                {
                    if (itemsByCategory.TryGetValue(cat.Id, out var catItems))
                        cat.Items = catItems;
                }

                return categories;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching FAQ categories");
                return new List<FaqCategory>();
            }
        }

        public async Task<List<FaqAnswer>> GetAnswersAsync(int faqItemId, CancellationToken ct = default)
        {
            const string sql = @"
                SELECT Id, FaqItemId, AuthorUserId, AuthorName, Content, IsAccepted, IsActive, CreatedAtUtc, UpdatedAtUtc
                FROM dbo.FaqAnswers
                WHERE FaqItemId = @FaqItemId AND IsActive = 1
                ORDER BY IsAccepted DESC, CreatedAtUtc;";

            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                return (await conn.QueryAsync<FaqAnswer>(sql, new { FaqItemId = faqItemId })).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching answers for FAQ item {ItemId}", faqItemId);
                return new List<FaqAnswer>();
            }
        }

        public async Task<int> CreateCategoryAsync(string name, CancellationToken ct = default)
        {
            const string sql = @"
                DECLARE @maxSort INT = (SELECT ISNULL(MAX(SortOrder), 0) FROM dbo.FaqCategories);
                INSERT INTO dbo.FaqCategories (Name, SortOrder) VALUES (@Name, @maxSort + 1);
                SELECT CAST(SCOPE_IDENTITY() AS INT);";

            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                return await conn.QuerySingleAsync<int>(sql, new { Name = name });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating FAQ category");
                throw;
            }
        }

        public async Task UpdateCategoryAsync(int categoryId, string name, CancellationToken ct = default)
        {
            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                await conn.ExecuteAsync(
                    "UPDATE dbo.FaqCategories SET Name = @Name, UpdatedAtUtc = SYSUTCDATETIME() WHERE Id = @Id",
                    new { Id = categoryId, Name = name });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating FAQ category {CategoryId}", categoryId);
                throw;
            }
        }

        public async Task DeleteCategoryAsync(int categoryId, CancellationToken ct = default)
        {
            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                await conn.ExecuteAsync(
                    "UPDATE dbo.FaqCategories SET IsActive = 0, UpdatedAtUtc = SYSUTCDATETIME() WHERE Id = @Id",
                    new { Id = categoryId });
                await conn.ExecuteAsync(
                    "UPDATE dbo.FaqItems SET IsActive = 0, UpdatedAtUtc = SYSUTCDATETIME() WHERE CategoryId = @CategoryId",
                    new { CategoryId = categoryId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting FAQ category {CategoryId}", categoryId);
                throw;
            }
        }

        public async Task<int> CreateFaqItemAsync(int categoryId, string question, int? createdByUserId, CancellationToken ct = default)
        {
            const string sql = @"
                DECLARE @maxSort INT = (SELECT ISNULL(MAX(SortOrder), 0) FROM dbo.FaqItems WHERE CategoryId = @CategoryId);
                INSERT INTO dbo.FaqItems (CategoryId, Question, SortOrder, CreatedByUserId)
                VALUES (@CategoryId, @Question, @maxSort + 1, @CreatedByUserId);
                SELECT CAST(SCOPE_IDENTITY() AS INT);";

            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                return await conn.QuerySingleAsync<int>(sql, new { CategoryId = categoryId, Question = question, CreatedByUserId = createdByUserId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating FAQ item");
                throw;
            }
        }

        public async Task MoveFaqItemAsync(int faqItemId, int newCategoryId, CancellationToken ct = default)
        {
            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                await conn.ExecuteAsync(
                    "UPDATE dbo.FaqItems SET CategoryId = @CategoryId, UpdatedAtUtc = SYSUTCDATETIME() WHERE Id = @Id",
                    new { Id = faqItemId, CategoryId = newCategoryId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error moving FAQ item {ItemId}", faqItemId);
                throw;
            }
        }

        public async Task ToggleFaqItemLockAsync(int faqItemId, bool isLocked, CancellationToken ct = default)
        {
            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                await conn.ExecuteAsync(
                    "UPDATE dbo.FaqItems SET IsLocked = @IsLocked, UpdatedAtUtc = SYSUTCDATETIME() WHERE Id = @Id",
                    new { Id = faqItemId, IsLocked = isLocked });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling lock for FAQ item {ItemId}", faqItemId);
                throw;
            }
        }

        public async Task DeleteFaqItemAsync(int faqItemId, CancellationToken ct = default)
        {
            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                await conn.ExecuteAsync(
                    "UPDATE dbo.FaqItems SET IsActive = 0, UpdatedAtUtc = SYSUTCDATETIME() WHERE Id = @Id",
                    new { Id = faqItemId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting FAQ item {ItemId}", faqItemId);
                throw;
            }
        }

        public async Task<int> PostAnswerAsync(int faqItemId, int authorUserId, string authorName, string content, CancellationToken ct = default)
        {
            const string sql = @"
                INSERT INTO dbo.FaqAnswers (FaqItemId, AuthorUserId, AuthorName, Content)
                VALUES (@FaqItemId, @AuthorUserId, @AuthorName, @Content);
                SELECT CAST(SCOPE_IDENTITY() AS INT);";

            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                return await conn.QuerySingleAsync<int>(sql, new
                {
                    FaqItemId = faqItemId,
                    AuthorUserId = authorUserId,
                    AuthorName = authorName,
                    Content = content
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error posting FAQ answer");
                throw;
            }
        }

        public async Task ToggleAcceptedAsync(int answerId, bool isAccepted, CancellationToken ct = default)
        {
            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                await conn.ExecuteAsync(
                    "UPDATE dbo.FaqAnswers SET IsAccepted = @IsAccepted, UpdatedAtUtc = SYSUTCDATETIME() WHERE Id = @Id",
                    new { Id = answerId, IsAccepted = isAccepted });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling accepted for answer {AnswerId}", answerId);
                throw;
            }
        }

        public async Task DeleteAnswerAsync(int answerId, CancellationToken ct = default)
        {
            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                await conn.ExecuteAsync(
                    "UPDATE dbo.FaqAnswers SET IsActive = 0, UpdatedAtUtc = SYSUTCDATETIME() WHERE Id = @Id",
                    new { Id = answerId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting answer {AnswerId}", answerId);
                throw;
            }
        }
    }
}
