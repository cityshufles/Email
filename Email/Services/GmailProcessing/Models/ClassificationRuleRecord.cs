using System;

namespace Email.Services.GmailProcessing.Models
{
    /// <summary>
    /// Created: 2025-11-07 00:00 UTC
    /// SQL Server representation of dbo.AutomaticGmail_EmailClassificationRules.
    /// </summary>
    public sealed class ClassificationRuleRecord
    {
        public int Id { get; set; }
        public string VendorName { get; set; } = string.Empty;
        public string Domain { get; set; } = string.Empty;
        public string SubjectPhrase { get; set; } = string.Empty;
        public string EmailType { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int Priority { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}


