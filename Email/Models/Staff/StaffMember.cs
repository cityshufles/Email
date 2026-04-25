using System;
using Email.Models;
namespace Email.Models.Staff
{
    /// <summary>
    /// 2026-02-09 00:00 UTC - Unified staff DTO for Users + Guides.
    /// </summary>
    public class StaffMember
    {
        // User / Auth Data
        public int? UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string? UserEmail { get; set; }
        public string? UserPhone { get; set; }
        public string Role { get; set; } = "Guide";
        public UserPermissions Permissions { get; set; } = new();
        public bool IsUserActive { get; set; } = true;
        public DateTime? LastLoginAt { get; set; }

        // Guide / Profile Data
        public int? GuideId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string? GuidePhone { get; set; }
        public string ContactEmail { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? ImagePath { get; set; }
        public string? Languages { get; set; }
        public bool IsTouring { get; set; } = true;
        public bool IsGuideActive { get; set; } = true;

        // UI Helpers
        public bool IncludeUserAccount { get; set; }
        public bool IncludeGuideProfile { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public bool IsDimmed { get; set; }
    }
}
