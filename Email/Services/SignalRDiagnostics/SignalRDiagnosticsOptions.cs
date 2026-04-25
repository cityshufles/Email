namespace Email.Services.SignalRDiagnostics;

public sealed class SignalRDiagnosticsOptions
{
    public bool Enabled { get; set; }

    public string? Directory { get; set; }

    public int RetentionDays { get; set; } = 14;

    public bool IncludeHubInvocationMethods { get; set; }

    public int CorrelationWindowSeconds { get; set; } = 45;

    public List<string> TargetRouteMarkers { get; set; } = new()
    {
        "/mobile-tours",
        "/tour-management-dashboard",
        "/tour-managment",
        "/guide-report"
    };
}
