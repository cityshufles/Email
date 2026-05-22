using System;
using System.Collections.Generic;

namespace Email.Models.Faq
{
    public sealed class FaqCategory
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public List<FaqItem> Items { get; set; } = new();
    }

    public sealed class FaqItem
    {
        public int Id { get; set; }
        public int CategoryId { get; set; }
        public string Question { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public int? CreatedByUserId { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public List<FaqAnswer> Answers { get; set; } = new();
    }

    public sealed class FaqAnswer
    {
        public int Id { get; set; }
        public int FaqItemId { get; set; }
        public int AuthorUserId { get; set; }
        public string AuthorName { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public bool IsAccepted { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
}
