using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Email.Models.Community;
using Email.Models.Staff;
using Email.Services.Staff;
using Microsoft.Extensions.Configuration;

namespace Email.Services.Community
{
    public sealed class CommunitySqlService : ICommunityService
    {
        private readonly SqlConnectionFactory _connectionFactory;
        private readonly IStaffService _staffService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<CommunitySqlService> _logger;

        public CommunitySqlService(
            SqlConnectionFactory connectionFactory,
            IStaffService staffService,
            IConfiguration configuration,
            ILogger<CommunitySqlService> logger)
        {
            _connectionFactory = connectionFactory;
            _staffService = staffService;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<List<CommunityPost>> GetPostsAsync(int limit = 100, CancellationToken ct = default)
        {
            const string sql = @"
                SELECT Id, AuthorUserId, AuthorName, Content, IsPinned, IsHighlighted,
                       IsAnnouncement, AnnouncementEmailSentAt, ParentPostId, Depth,
                       IsActive, CreatedAtUtc, UpdatedAtUtc
                FROM dbo.CommunityPosts
                WHERE IsActive = 1
                ORDER BY IsPinned DESC, CreatedAtUtc DESC;";

            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                var allPosts = (await conn.QueryAsync<CommunityPost>(sql)).ToList();
                return BuildPostTree(allPosts, limit);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching community posts");
                return new List<CommunityPost>();
            }
        }

        public async Task<int> CreatePostAsync(CommunityPostWriteModel post, bool isAdmin = false, CancellationToken ct = default)
        {
            // Server-side guard: only admins may create announcements (defense-in-depth; UI also hidden).
            if (post.IsAnnouncement && !isAdmin)
            {
                post.IsAnnouncement = false;
            }

            var depth = 0;
            if (post.ParentPostId.HasValue)
            {
                using var connCheck = _connectionFactory.CreateOpenConnection();
                var parentDepth = await connCheck.QuerySingleOrDefaultAsync<int?>(
                    "SELECT Depth FROM dbo.CommunityPosts WHERE Id = @Id AND IsActive = 1",
                    new { Id = post.ParentPostId.Value });
                depth = (parentDepth ?? 0) + 1;
            }

            const string sql = @"
                INSERT INTO dbo.CommunityPosts (AuthorUserId, AuthorName, Content, ParentPostId, Depth, IsAnnouncement)
                VALUES (@AuthorUserId, @AuthorName, @Content, @ParentPostId, @Depth, @IsAnnouncement);
                SELECT CAST(SCOPE_IDENTITY() AS INT);";

            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                return await conn.QuerySingleAsync<int>(sql, new
                {
                    post.AuthorUserId,
                    post.AuthorName,
                    post.Content,
                    post.ParentPostId,
                    Depth = depth,
                    post.IsAnnouncement
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating community post");
                throw;
            }
        }

        public async Task TogglePinAsync(int postId, bool isPinned, CancellationToken ct = default)
        {
            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                await conn.ExecuteAsync(
                    "UPDATE dbo.CommunityPosts SET IsPinned = @IsPinned, UpdatedAtUtc = SYSUTCDATETIME() WHERE Id = @Id",
                    new { Id = postId, IsPinned = isPinned });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling pin for post {PostId}", postId);
                throw;
            }
        }

        public async Task ToggleHighlightAsync(int postId, bool isHighlighted, CancellationToken ct = default)
        {
            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                await conn.ExecuteAsync(
                    "UPDATE dbo.CommunityPosts SET IsHighlighted = @IsHighlighted, UpdatedAtUtc = SYSUTCDATETIME() WHERE Id = @Id",
                    new { Id = postId, IsHighlighted = isHighlighted });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling highlight for post {PostId}", postId);
                throw;
            }
        }

        public async Task DeletePostAsync(int postId, CancellationToken ct = default)
        {
            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                await conn.ExecuteAsync(
                    "UPDATE dbo.CommunityPosts SET IsActive = 0, UpdatedAtUtc = SYSUTCDATETIME() WHERE Id = @Id OR ParentPostId = @Id",
                    new { Id = postId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting post {PostId}", postId);
                throw;
            }
        }

        public async Task SendAnnouncementEmailsAsync(int postId, bool isAdmin = false, CancellationToken ct = default)
        {
            // Server-side guard: only admins may trigger announcement emails.
            if (!isAdmin)
            {
                _logger.LogWarning("SendAnnouncementEmailsAsync blocked for non-admin (post {PostId})", postId);
                return;
            }
            try
            {
                using var conn = _connectionFactory.CreateOpenConnection();
                var post = await conn.QuerySingleOrDefaultAsync<CommunityPost>(
                    "SELECT * FROM dbo.CommunityPosts WHERE Id = @Id AND IsActive = 1",
                    new { Id = postId });

                if (post == null) return;

                var allStaff = await _staffService.GetAllStaffAsync(ct);
                var guides = allStaff
                    .Where(s => s.Role == "Guide" && !string.IsNullOrWhiteSpace(s.UserEmail))
                    .ToList();

                if (guides.Count == 0)
                {
                    _logger.LogWarning("No guides with email addresses found for announcement");
                    return;
                }

                var fromEmail = (_configuration["Gmail:Email"] ?? string.Empty).Trim();
                var password = (_configuration["Gmail:Password"] ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(fromEmail) || string.IsNullOrWhiteSpace(password))
                {
                    _logger.LogError("Gmail SMTP credentials not configured for announcement emails");
                    return;
                }

                var htmlBody = BuildAnnouncementHtml(post);
                var sentCount = 0;

                using var client = new SmtpClient("smtp.gmail.com", 587)
                {
                    EnableSsl = true,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(fromEmail, password),
                    Timeout = 1000 * 60 * 2
                };

                foreach (var guide in guides)
                {
                    try
                    {
                        using var message = new MailMessage();
                        message.From = new MailAddress(fromEmail, "CityShuffles Team");
                        message.To.Add(new MailAddress(guide.UserEmail!));
                        message.Subject = $"Announcement from {post.AuthorName}";
                        message.IsBodyHtml = true;
                        message.Body = htmlBody;
                        await client.SendMailAsync(message);
                        sentCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send announcement to {Email}", guide.UserEmail);
                    }
                }

                await conn.ExecuteAsync(
                    "UPDATE dbo.CommunityPosts SET AnnouncementEmailSentAt = SYSUTCDATETIME(), UpdatedAtUtc = SYSUTCDATETIME() WHERE Id = @Id",
                    new { Id = postId });

                _logger.LogInformation("Announcement emails sent: {SentCount}/{TotalGuides} for post {PostId}",
                    sentCount, guides.Count, postId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending announcement emails for post {PostId}", postId);
                throw;
            }
        }

        private static string BuildAnnouncementHtml(CommunityPost post)
        {
            return $@"
<!DOCTYPE html>
<html>
<head><meta charset='utf-8'></head>
<body style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px;'>
    <div style='background: #2c3e50; color: white; padding: 20px; text-align: center; border-radius: 8px 8px 0 0;'>
        <h1 style='margin: 0; font-size: 24px;'>CityShuffles</h1>
        <p style='margin: 5px 0 0 0; opacity: 0.8; font-size: 14px;'>Team Announcement</p>
    </div>
    <div style='background: #f8f9fa; padding: 24px; border: 1px solid #dee2e6; border-top: none;'>
        <p style='color: #6c757d; font-size: 13px; margin: 0 0 12px 0;'>
            Posted by <strong>{System.Net.WebUtility.HtmlEncode(post.AuthorName)}</strong>
            on {post.CreatedAtUtc:MMMM d, yyyy h:mm tt} UTC
        </p>
        <div style='background: white; padding: 16px; border-radius: 6px; border: 1px solid #e9ecef; line-height: 1.6;'>
            {System.Net.WebUtility.HtmlEncode(post.Content).Replace("\n", "<br/>")}
        </div>
    </div>
    <div style='text-align: center; padding: 16px; color: #6c757d; font-size: 12px;'>
        <p>This is an automated message from CityShuffles Guides Portal.</p>
    </div>
</body>
</html>";
        }

        private static List<CommunityPost> BuildPostTree(List<CommunityPost> allPosts, int topLevelLimit)
        {
            var topLevel = allPosts.Where(p => p.ParentPostId == null).ToList();
            var byParent = allPosts
                .Where(p => p.ParentPostId != null)
                .GroupBy(p => p.ParentPostId!.Value)
                .ToDictionary(g => g.Key, g => g.OrderBy(p => p.CreatedAtUtc).ToList());

            foreach (var post in topLevel)
            {
                AttachReplies(post, byParent);
            }

            return topLevel.Take(topLevelLimit).ToList();
        }

        private static void AttachReplies(CommunityPost post, Dictionary<int, List<CommunityPost>> byParent)
        {
            if (byParent.TryGetValue(post.Id, out var replies))
            {
                post.Replies = replies;
                post.ReplyCount = replies.Count;
                foreach (var reply in replies)
                {
                    AttachReplies(reply, byParent);
                    post.ReplyCount += reply.ReplyCount;
                }
            }
        }
    }
}
