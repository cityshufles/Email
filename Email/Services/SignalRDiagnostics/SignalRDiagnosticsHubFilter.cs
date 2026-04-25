using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Email.Services.SignalRDiagnostics;

public sealed class SignalRDiagnosticsHubFilter : IHubFilter
{
    private readonly ISignalRDiagnosticsLog _diagnosticsLog;
    private readonly IOptions<SignalRDiagnosticsOptions> _options;
    private readonly ILogger<SignalRDiagnosticsHubFilter> _logger;

    public SignalRDiagnosticsHubFilter(
        ISignalRDiagnosticsLog diagnosticsLog,
        IOptions<SignalRDiagnosticsOptions> options,
        ILogger<SignalRDiagnosticsHubFilter> logger)
    {
        _diagnosticsLog = diagnosticsLog;
        _options = options;
        _logger = logger;
    }

    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        await _diagnosticsLog.WriteAsync(new SignalRDiagnosticsEntry
        {
            EventType = "HubConnected",
            Method = nameof(OnConnectedAsync),
            Route = ResolveRoute(context.Context),
            ConnectionId = context.Context.ConnectionId ?? "(none)",
            User = ResolveUser(context.Context.User),
            Details = $"Hub={context.Hub.GetType().Name}"
        });

        await next(context);
    }

    public async Task OnDisconnectedAsync(
        HubLifetimeContext context,
        Exception? exception,
        Func<HubLifetimeContext, Exception?, Task> next)
    {
        var details = exception is null
            ? $"Hub={context.Hub.GetType().Name}; Exception=(none)"
            : $"Hub={context.Hub.GetType().Name}; ExceptionType={exception.GetType().Name}; ExceptionMessage={exception.Message}";

        await _diagnosticsLog.WriteAsync(new SignalRDiagnosticsEntry
        {
            EventType = "HubDisconnected",
            Method = nameof(OnDisconnectedAsync),
            Route = ResolveRoute(context.Context),
            ConnectionId = context.Context.ConnectionId ?? "(none)",
            User = ResolveUser(context.Context.User),
            Details = details,
            HasException = exception is not null
        });

        await next(context, exception);
    }

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        if (_options.Value.Enabled && _options.Value.IncludeHubInvocationMethods)
        {
            try
            {
                await _diagnosticsLog.WriteAsync(new SignalRDiagnosticsEntry
                {
                    EventType = "HubInvocation",
                    Method = invocationContext.HubMethodName,
                    Route = ResolveRoute(invocationContext.Context),
                    ConnectionId = invocationContext.Context.ConnectionId ?? "(none)",
                    User = ResolveUser(invocationContext.Context.User),
                    Details = $"Hub={invocationContext.Hub.GetType().Name}; ArgCount={invocationContext.HubMethodArguments.Count}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "SignalR invocation diagnostics write failed for {HubMethod}", invocationContext.HubMethodName);
            }
        }

        return await next(invocationContext);
    }

    private static string ResolveRoute(HubCallerContext context)
    {
        var httpContext = context.GetHttpContext();
        if (httpContext is null)
        {
            return "(unknown)";
        }

        var referer = httpContext.Request.Headers.Referer.ToString();
        if (!string.IsNullOrWhiteSpace(referer))
        {
            return referer;
        }

        var path = httpContext.Request.Path.HasValue
            ? httpContext.Request.Path.Value
            : string.Empty;

        var query = httpContext.Request.QueryString.HasValue
            ? httpContext.Request.QueryString.Value
            : string.Empty;

        return string.IsNullOrWhiteSpace(path) ? "(unknown)" : path + query;
    }

    private static string ResolveUser(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return "(anonymous)";
        }

        return user.Identity?.Name
            ?? user.FindFirst(ClaimTypes.Name)?.Value
            ?? user.FindFirst(ClaimTypes.Email)?.Value
            ?? "(authenticated)";
    }
}
