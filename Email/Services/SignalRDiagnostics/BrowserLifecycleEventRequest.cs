namespace Email.Services.SignalRDiagnostics;

public sealed class BrowserLifecycleEventRequest
{
    public string EventType { get; set; } = string.Empty;

    public string Route { get; set; } = string.Empty;

    public string PageUrl { get; set; } = string.Empty;

    public string TabId { get; set; } = string.Empty;

    public string VisibilityState { get; set; } = string.Empty;

    public bool? Hidden { get; set; }

    public bool? Online { get; set; }

    public string Details { get; set; } = string.Empty;

    public DateTimeOffset? ClientTimestampUtc { get; set; }
}
