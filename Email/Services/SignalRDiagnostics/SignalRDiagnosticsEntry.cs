namespace Email.Services.SignalRDiagnostics;

public sealed class SignalRDiagnosticsEntry
{
    public DateTimeOffset OccurredUtc { get; set; } = DateTimeOffset.UtcNow;

    public string EventType { get; set; } = string.Empty;

    public string Route { get; set; } = "(unknown)";

    public string Method { get; set; } = "(none)";

    public string CircuitId { get; set; } = "(none)";

    public string ConnectionId { get; set; } = "(none)";

    public string TabId { get; set; } = "(none)";

    public string User { get; set; } = "(anonymous)";

    public string Details { get; set; } = string.Empty;

    public bool HasException { get; set; }
}
