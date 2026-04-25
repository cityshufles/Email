using Email.Models;

namespace Email.Services.Auth
{
    /// <summary>
    /// 2025-11-22 00:00 UTC - Cookie-based authentication service interface.
    /// </summary>
    public interface IAuthenticationService
    {
        Task<bool> LoginAsync(string username, string password);
        Task<bool> SignInUserAsync(string username);
        Task LogoutAsync();
        Task<bool> IsAuthenticatedAsync();
        Task<User?> GetCurrentUserAsync();
    }
}


