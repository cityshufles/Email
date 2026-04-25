using System.Security.Claims;
using Email.Services.SignalRDiagnostics;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Email.Services;

/// <summary>
/// Logs circuit lifecycle events to help diagnose reconnect failures in Blazor Server.
/// </summary>
public sealed class BlazorCircuitLoggingHandler : CircuitHandler
{
    private readonly ILogger<BlazorCircuitLoggingHandler> _logger;
    private readonly ISignalRDiagnosticsLog _diagnosticsLog;
    private readonly NavigationManager _navigationManager;
    private readonly AuthenticationStateProvider _authenticationStateProvider;

    public BlazorCircuitLoggingHandler(
        ILogger<BlazorCircuitLoggingHandler> logger,
        ISignalRDiagnosticsLog diagnosticsLog,
        NavigationManager navigationManager,
        AuthenticationStateProvider authenticationStateProvider)
    {
        _logger = logger;
        _diagnosticsLog = diagnosticsLog;
        _navigationManager = navigationManager;
        _authenticationStateProvider = authenticationStateProvider;
    }

    public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        await WriteCircuitEventAsync(circuit, "CircuitOpened", nameof(OnCircuitOpenedAsync));
        _logger.LogInformation("Blazor circuit opened: {CircuitId}", circuit.Id);
    }

    public override async Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        await WriteCircuitEventAsync(circuit, "CircuitConnectionDown", nameof(OnConnectionDownAsync));
        _logger.LogWarning("Blazor circuit connection down: {CircuitId}", circuit.Id);
    }

    public override async Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        await WriteCircuitEventAsync(circuit, "CircuitConnectionUp", nameof(OnConnectionUpAsync));
        _logger.LogInformation("Blazor circuit connection restored: {CircuitId}", circuit.Id);
    }

    public override async Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        await WriteCircuitEventAsync(circuit, "CircuitClosed", nameof(OnCircuitClosedAsync));
        _logger.LogWarning("Blazor circuit closed: {CircuitId}", circuit.Id);
    }

    private async Task WriteCircuitEventAsync(Circuit circuit, string eventType, string methodName)
    {
        var route = ResolveRoute();
        var user = await ResolveUserAsync();

        await _diagnosticsLog.WriteAsync(new SignalRDiagnosticsEntry
        {
            EventType = eventType,
            Method = methodName,
            CircuitId = circuit.Id,
            Route = route,
            User = user,
            Details = "Source=BlazorCircuitHandler"
        });
    }

    private string ResolveRoute()
    {
        try
        {
            var currentUri = _navigationManager.Uri;
            if (Uri.TryCreate(currentUri, UriKind.Absolute, out var absoluteUri))
            {
                return absoluteUri.PathAndQuery;
            }

            if (string.IsNullOrWhiteSpace(currentUri))
            {
                return "(unknown)";
            }

            return currentUri.StartsWith('/')
                ? currentUri
                : "/" + currentUri.TrimStart('/');
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to resolve current route from NavigationManager.");
            return "(unknown)";
        }
    }

    private async Task<string> ResolveUserAsync()
    {
        try
        {
            var authState = await _authenticationStateProvider.GetAuthenticationStateAsync();
            var principal = authState.User;
            if (principal?.Identity?.IsAuthenticated == true)
            {
                return principal.Identity?.Name
                    ?? principal.FindFirst(ClaimTypes.Name)?.Value
                    ?? principal.FindFirst(ClaimTypes.Email)?.Value
                    ?? "(authenticated)";
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to resolve authenticated user for circuit diagnostics.");
        }

        return "(anonymous)";
    }
}
