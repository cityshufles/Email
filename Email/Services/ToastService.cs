using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace Email.Services
{
    public class ToastService : IToastService
    {
        private readonly IJSRuntime _jsRuntime;

        public ToastService(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        public async Task ShowSuccessAsync(string message, string? title = null)
        {
            await ShowToastAsync("success", message, title ?? "Success");
        }

        public async Task ShowErrorAsync(string message, string? title = null)
        {
            await ShowToastAsync("error", message, title ?? "Error");
        }

        public async Task ShowInfoAsync(string message, string? title = null)
        {
            await ShowToastAsync("info", message, title ?? "Information");
        }

        public async Task ShowWarningAsync(string message, string? title = null)
        {
            await ShowToastAsync("warning", message, title ?? "Warning");
        }

        private async Task ShowToastAsync(string type, string message, string title)
        {
            try
            {
                // Check if we're in a prerendering context where JS interop is not available
                if (_jsRuntime is IJSInProcessRuntime)
                {
                    await _jsRuntime.InvokeVoidAsync("showToast", type, title, message);
                }
                else
                {
                    // For server-side rendering, try the call but handle the exception gracefully
                    await _jsRuntime.InvokeVoidAsync("showToast", type, title, message);
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("JavaScript interop calls cannot be issued at this time"))
            {
                // Silently ignore JavaScript interop errors during prerendering
                // The toast will be shown when the component becomes interactive
                Console.WriteLine($"Toast message queued (prerendering): [{type}] {title}: {message}");
            }
            catch (JSDisconnectedException)
            {
                // Handle disconnected client gracefully
                Console.WriteLine($"Toast message lost (disconnected): [{type}] {title}: {message}");
            }
        }
    }
}


