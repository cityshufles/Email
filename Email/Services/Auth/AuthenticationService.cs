using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Email.Models;

namespace Email.Services.Auth
{
    /// <summary>
    /// 2025-11-22 00:00 UTC - Cookie auth service for Email app.
    /// </summary>
    public sealed class AuthenticationService : IAuthenticationService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IUserRepository _userRepository;
        private readonly ILogger<AuthenticationService> _logger;

        public AuthenticationService(
            IHttpContextAccessor httpContextAccessor,
            IUserRepository userRepository,
            ILogger<AuthenticationService> logger)
        {
            _httpContextAccessor = httpContextAccessor;
            _userRepository = userRepository;
            _logger = logger;
        }

        public async Task<bool> LoginAsync(string username, string password)
        {
            // 2025-11-22 00:00 UTC
            try
            {
                var user = await _userRepository.GetUserByUsernameAsync(username);
                if (user == null || !user.IsActive) return false;
                // NOTE: Simple string match per source logic; hashing belongs in a later improvement.
                return user.Password == password;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login for {Username}", username);
                return false;
            }
        }

        public async Task<bool> SignInUserAsync(string username)
        {
            // 2025-11-22 00:00 UTC
            try
            {
                var user = await _userRepository.GetUserByUsernameAsync(username);
                if (user == null || !user.IsActive) return false;

                await _userRepository.UpdateUserLastLoginAsync(user.Id);

                var claims = new List<Claim>
                {
                    new(ClaimTypes.Name, user.Username),
                    new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new(ClaimTypes.Role, user.Role),
                    new("FullName", user.FullName),
                    new(ClaimTypes.MobilePhone, user.PhoneNumber ?? ""),
                    new("Permissions", user.Permissions)
                };

                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var authProps = new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddDays(1)
                };

                var httpContext = _httpContextAccessor.HttpContext;
                if (httpContext != null)
                {
                    await httpContext.SignInAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme,
                        new ClaimsPrincipal(identity),
                        authProps);
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error signing in {Username}", username);
                return false;
            }
        }

        public async Task LogoutAsync()
        {
            // 2025-11-22 00:00 UTC
            try
            {
                var httpContext = _httpContextAccessor.HttpContext;
                if (httpContext != null)
                {
                    await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during logout");
            }
        }

        public Task<bool> IsAuthenticatedAsync()
        {
            // 2025-11-22 00:00 UTC
            var httpContext = _httpContextAccessor.HttpContext;
            return Task.FromResult(httpContext?.User?.Identity?.IsAuthenticated ?? false);
        }

        public async Task<User?> GetCurrentUserAsync()
        {
            // 2025-11-22 00:00 UTC
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext?.User?.Identity?.IsAuthenticated != true) return null;

            var idClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier);
            if (idClaim == null || !int.TryParse(idClaim.Value, out var id)) return null;

            return await _userRepository.GetUserByIdAsync(id);
        }
    }
}


