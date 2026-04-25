using Email.Models;

namespace Email.Services.Auth
{
    /// <summary>
    /// 2025-11-22 00:00 UTC - SQL Server-backed user repository interface.
    /// </summary>
    public interface IUserRepository
    {
        Task<User?> GetUserByUsernameAsync(string username);
        Task<User?> GetUserByIdAsync(int id);
        Task UpdateUserLastLoginAsync(int userId);
    }
}


