using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Email.Models
{
    /// <summary>
    /// 2025-11-22 00:00 UTC - Auth user model mirrored from source project.
    /// </summary>
    public class User
    {
        public int Id { get; set; }

        [Required]
        public string Username { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;

        [Required]
        public string Role { get; set; } = "Guide";

        public string? Email { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PhoneNumber { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime? LastLoginAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // JSON string for extensible permissions
        public string Permissions { get; set; } = "{}";

        public string FullName => $"{FirstName} {LastName}".Trim();

        public UserPermissions GetPermissions()
        {
            try
            {
                return JsonSerializer.Deserialize<UserPermissions>(Permissions) ?? new UserPermissions();
            }
            catch
            {
                return new UserPermissions();
            }
        }

        public void SetPermissions(UserPermissions permissions)
        {
            Permissions = JsonSerializer.Serialize(permissions);
        }
    }

    /// <summary>
    /// 2025-11-22 00:00 UTC - JSON permissions model.
    /// </summary>
    public class UserPermissions
    {
        public bool CanViewLogs { get; set; }
        public bool CanExportLogs { get; set; }
        public bool CanViewCalendar { get; set; }
        public bool CanEditCalendar { get; set; }
        public bool CanViewSettings { get; set; }
        public bool CanManageUsers { get; set; }
    }
}


