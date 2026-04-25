namespace Email.Services.SignalRDiagnostics;

public interface ISignalRDiagnosticsLog
{
    Task WriteAsync(SignalRDiagnosticsEntry entry, CancellationToken cancellationToken = default);
}
