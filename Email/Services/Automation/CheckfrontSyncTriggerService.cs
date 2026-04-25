using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Email.Services.Automation
{
    /// <summary>
    /// Created: 2026-03-26 00:00 UTC
    /// Updated: 2026-03-26 00:00 UTC - Throttled on-request trigger for Checkfront sync on hosts without cron/webjobs.
    /// </summary>
    public sealed class CheckfrontSyncTriggerService
    {
        private readonly ILogger<CheckfrontSyncTriggerService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private long _lastCompletedTicksUtc;
        private long _lastStartedTicksUtc;
        private int _isRunning;

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        public CheckfrontSyncTriggerService(
            ILogger<CheckfrontSyncTriggerService> logger,
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration)
        {
            try
            {
                _logger = logger;
                _scopeFactory = scopeFactory;
                _configuration = configuration;
                _lastCompletedTicksUtc = 0;
                _lastStartedTicksUtc = 0;
                _isRunning = 0;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "CheckfrontSyncTriggerService constructor failed");
                Console.WriteLine($"[DEBUG] CheckfrontSyncTriggerService.ctor error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// Updated: 2026-03-26 00:00 UTC - Runs sync opportunistically on request flow with interval and concurrency guard.
        /// </summary>
        public void TryTriggerForRequest(string? requestPath)
        {
            try
            {
                var requestTriggerEnabled = _configuration.GetValue<bool?>("CheckfrontSync:EnableRequestTrigger") ?? false;
                if (!requestTriggerEnabled)
                {
                    return;
                }

                var path = requestPath ?? string.Empty;
                if (path.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("/_content", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("/css", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("/js", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("/lib", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("/diagnostics/signalr", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var nowUtc = DateTime.UtcNow;
                var minIntervalMinutes = Math.Clamp(_configuration.GetValue<int?>("CheckfrontSync:MinIntervalMinutes") ?? 20, 1, 1440);
                var lastCompletedTicks = Interlocked.Read(ref _lastCompletedTicksUtc);
                if (lastCompletedTicks > 0)
                {
                    var lastCompletedUtc = new DateTime(lastCompletedTicks, DateTimeKind.Utc);
                    if (nowUtc - lastCompletedUtc < TimeSpan.FromMinutes(minIntervalMinutes))
                    {
                        return;
                    }
                }

                if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
                {
                    return;
                }

                Interlocked.Exchange(ref _lastStartedTicksUtc, nowUtc.Ticks);
                _ = Task.Run(() => RunSyncAsync(nowUtc));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TryTriggerForRequest failed for path {Path}", requestPath ?? string.Empty);
                Console.WriteLine($"[DEBUG] CheckfrontSyncTriggerService.TryTriggerForRequest error: {ex.Message}");
            }
        }

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// </summary>
        private async Task RunSyncAsync(DateTime startedUtc)
        {
            try
            {
                var daysBack = Math.Clamp(_configuration.GetValue<int?>("CheckfrontSync:DaysBack") ?? 30, 1, 365);
                var limitPerPage = Math.Clamp(_configuration.GetValue<int?>("CheckfrontSync:LimitPerPage") ?? 50, 1, 200);
                var maxPages = Math.Clamp(_configuration.GetValue<int?>("CheckfrontSync:MaxPages") ?? 5, 1, 50);

                using var scope = _scopeFactory.CreateScope();
                var processingService = scope.ServiceProvider.GetRequiredService<Email.Services.GmailProcessing.GmailProcessingV2Service>();
                var result = await processingService.SyncCheckfrontBookingsAsync(daysBack, limitPerPage, maxPages, CancellationToken.None);
                _logger.LogInformation(
                    "Request-triggered Checkfront sync completed. Processed={Processed}, Errors={Errors}, Message={Message}",
                    result.ProcessedCount,
                    result.ErrorCount,
                    result.Message);
                Console.WriteLine(
                    $"[CheckfrontSyncTrigger] started={startedUtc:O} processed={result.ProcessedCount} errors={result.ErrorCount} message={result.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RunSyncAsync failed");
                Console.WriteLine($"[DEBUG] CheckfrontSyncTriggerService.RunSyncAsync error: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _lastCompletedTicksUtc, DateTime.UtcNow.Ticks);
                Interlocked.Exchange(ref _isRunning, 0);
            }
        }
    }
}
