using System.Threading.Tasks;

namespace Email.Services
{
    public interface IToastService
    {
        Task ShowSuccessAsync(string message, string? title = null);
        Task ShowErrorAsync(string message, string? title = null);
        Task ShowInfoAsync(string message, string? title = null);
        Task ShowWarningAsync(string message, string? title = null);
    }
}


