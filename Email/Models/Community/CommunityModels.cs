using System;
using System.Collections.Generic;

namespace Email.Models.Community
{
    public sealed class CommunityPost
    {
        public int Id { get; set; }
        public int AuthorUserId { get; set; }
        public string AuthorName { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public bool IsPinned { get; set; }
        public bool IsHighlighted { get; set; }
        public bool IsAnnouncement { get; set; }
        public DateTime? AnnouncementEmailSentAt { get; set; }
        public int? ParentPostId { get; set; }
        public int Depth { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public List<CommunityPost> Replies { get; set; } = new();
        public int ReplyCount { get; set; }
    }

    public sealed class CommunityPostWriteModel
    {
        public int AuthorUserId { get; set; }
        public string AuthorName { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public int? ParentPostId { get; set; }
        public bool IsAnnouncement { get; set; }
    }
}
