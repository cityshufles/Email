using Microsoft.AspNetCore.Authorization;
using Email.Models;
using System.Security.Claims;

namespace Email.Services.Auth
{
    public class PermissionRequirement : IAuthorizationRequirement
    {
        public string PermissionName { get; }

        public PermissionRequirement(string permissionName)
        {
            PermissionName = permissionName;
        }
    }

    public class PermissionHandler : AuthorizationHandler<PermissionRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            var permissionClaim = context.User.FindFirst("Permissions");
            if (permissionClaim == null)
            {
                return Task.CompletedTask;
            }

            try
            {
                var permissions = System.Text.Json.JsonSerializer.Deserialize<UserPermissions>(permissionClaim.Value);
                if (permissions == null) return Task.CompletedTask;

                bool hasPermission = requirement.PermissionName switch
                {
                    "CanViewLogs" => permissions.CanViewLogs,
                    "CanExportLogs" => permissions.CanExportLogs,
                    "CanViewCalendar" => permissions.CanViewCalendar,
                    "CanEditCalendar" => permissions.CanEditCalendar,
                    "CanViewSettings" => permissions.CanViewSettings,
                    "CanManageUsers" => permissions.CanManageUsers,
                    _ => false
                };

                if (hasPermission)
                {
                    context.Succeed(requirement);
                }
            }
            catch
            {
                // Failed to parse, assume no permission
            }

            return Task.CompletedTask;
        }
    }
}
