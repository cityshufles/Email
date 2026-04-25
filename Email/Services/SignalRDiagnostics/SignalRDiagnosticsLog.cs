using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Options;

namespace Email.Services.SignalRDiagnostics;

public sealed class SignalRDiagnosticsLog : ISignalRDiagnosticsLog
{
    private const int MaxDetailsLength = 2000;
    private const string BrowserEventPrefix = "BrowserLifecycle.";
    private readonly object _writeLock = new();
    private readonly ConcurrentDictionary<string, BrowserLifecycleHint> _browserHintsByUserAndRoute = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<SignalRDiagnosticsLog> _logger;
    private readonly SignalRDiagnosticsOptions _options;
    private readonly IWebHostEnvironment _environment;
    private readonly TimeZoneInfo _easternTimeZone;
    private DateTime _lastRetentionSweepUtcDate = DateTime.MinValue.Date;

    public SignalRDiagnosticsLog(
        IWebHostEnvironment environment,
        IOptions<SignalRDiagnosticsOptions> options,
        ILogger<SignalRDiagnosticsLog> logger)
    {
        _environment = environment;
        _logger = logger;
        _options = options.Value ?? new SignalRDiagnosticsOptions();
        _easternTimeZone = ResolveEasternTimeZone();
    }

    public Task WriteAsync(SignalRDiagnosticsEntry entry, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || entry is null)
        {
            return Task.CompletedTask;
        }

        var normalized = NormalizeEntry(entry);

        if (TryGetBrowserHintType(normalized.EventType, out var hintType))
        {
            StoreBrowserHint(normalized.User, normalized.Route, normalized.TabId, hintType, normalized.OccurredUtc);
        }

        var causeInference = InferCause(normalized);
        var routeMarker = ResolveRouteMarker(normalized.Route);
        var line = BuildLine(normalized, causeInference, routeMarker);

        try
        {
            WriteLine(line);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SignalR diagnostics write failed.");
        }

        return Task.CompletedTask;
    }

    private SignalRDiagnosticsEntry NormalizeEntry(SignalRDiagnosticsEntry entry)
    {
        var normalized = new SignalRDiagnosticsEntry
        {
            OccurredUtc = entry.OccurredUtc == default ? DateTimeOffset.UtcNow : entry.OccurredUtc,
            EventType = Sanitize(entry.EventType, "UnknownEvent"),
            Route = NormalizeRoute(entry.Route),
            Method = Sanitize(entry.Method, "(none)"),
            CircuitId = Sanitize(entry.CircuitId, "(none)"),
            ConnectionId = Sanitize(entry.ConnectionId, "(none)"),
            TabId = Sanitize(entry.TabId, "(none)"),
            User = NormalizeUser(entry.User),
            Details = SanitizeDetails(entry.Details),
            HasException = entry.HasException
        };

        return normalized;
    }

    private string InferCause(SignalRDiagnosticsEntry entry)
    {
        if (entry.EventType.StartsWith(BrowserEventPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return "ClientLifecycleProbe";
        }

        if (entry.HasException)
        {
            return "LikelyServerOrTransportError";
        }

        if (!IsDisconnectEvent(entry.EventType))
        {
            return "Unknown";
        }

        var hint = GetMostRelevantHint(entry.User, entry.Route, entry.OccurredUtc);
        if (hint is null)
        {
            return "Unknown";
        }

        var age = (entry.OccurredUtc - hint.TimestampUtc).Duration();
        var window = TimeSpan.FromSeconds(Math.Max(5, _options.CorrelationWindowSeconds));
        if (age > window)
        {
            return "Unknown";
        }

        return hint.HintType switch
        {
            "offline" => "LikelyClientOffline",
            "beforeunload" => "LikelyClientBackgroundOrClose",
            "pagehide" => "LikelyClientBackgroundOrClose",
            "visibility_hidden" => "LikelyClientBackgroundOrClose",
            _ => "Unknown"
        };
    }

    private static bool IsDisconnectEvent(string eventType)
    {
        return eventType.Equals("CircuitConnectionDown", StringComparison.OrdinalIgnoreCase)
            || eventType.Equals("CircuitClosed", StringComparison.OrdinalIgnoreCase)
            || eventType.Equals("HubDisconnected", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildHintKey(string user, string routePathOnly)
    {
        return $"{NormalizeUser(user)}|{routePathOnly}";
    }

    private static string BuildUserOnlyHintKey(string user)
    {
        return $"{NormalizeUser(user)}|*";
    }

    private void StoreBrowserHint(string user, string route, string tabId, string hintType, DateTimeOffset timestampUtc)
    {
        var routePathOnly = RoutePathOnly(route);
        var hint = new BrowserLifecycleHint
        {
            HintType = hintType,
            TabId = tabId,
            Route = routePathOnly,
            TimestampUtc = timestampUtc
        };

        _browserHintsByUserAndRoute[BuildHintKey(user, routePathOnly)] = hint;
        _browserHintsByUserAndRoute[BuildUserOnlyHintKey(user)] = hint;
    }

    private BrowserLifecycleHint? GetMostRelevantHint(string user, string route, DateTimeOffset occurredUtc)
    {
        var routePathOnly = RoutePathOnly(route);
        var key = BuildHintKey(user, routePathOnly);

        if (_browserHintsByUserAndRoute.TryGetValue(key, out var hint) && hint.TimestampUtc <= occurredUtc)
        {
            return hint;
        }

        var userOnlyKey = BuildUserOnlyHintKey(user);
        if (_browserHintsByUserAndRoute.TryGetValue(userOnlyKey, out var fallbackHint) && fallbackHint.TimestampUtc <= occurredUtc)
        {
            return fallbackHint;
        }

        return null;
    }

    private string BuildLine(SignalRDiagnosticsEntry entry, string cause, string routeMarker)
    {
        var timestampEt = ConvertToEt(entry.OccurredUtc).ToString(
            "yyyy-MM-dd hh:mm:ss.fff tt 'ET' zzz",
            CultureInfo.InvariantCulture);

        return string.Join(" | ", new[]
        {
            $"TimestampET={timestampEt}",
            $"EventType={entry.EventType}",
            $"Cause={cause}",
            $"TargetRouteMarker={routeMarker}",
            $"Route={entry.Route}",
            $"Method={entry.Method}",
            $"CircuitId={entry.CircuitId}",
            $"ConnectionId={entry.ConnectionId}",
            $"TabId={entry.TabId}",
            $"User={entry.User}",
            $"Details={entry.Details}"
        });
    }

    private void WriteLine(string line)
    {
        var directory = ResolveLogDirectory();
        Directory.CreateDirectory(directory);
        MaybePruneOldFiles(directory);
        var filePath = Path.Combine(directory, $"signalr-diagnostics-{DateTime.UtcNow:yyyyMMdd}.log");

        lock (_writeLock)
        {
            File.AppendAllText(filePath, line + Environment.NewLine);
        }
    }

    private string ResolveLogDirectory()
    {
        var configuredDirectory = _options.Directory?.Trim();
        if (string.IsNullOrWhiteSpace(configuredDirectory))
        {
            return Path.Combine(_environment.ContentRootPath, "logs", "signalr-diagnostics");
        }

        return Path.IsPathRooted(configuredDirectory)
            ? configuredDirectory
            : Path.Combine(_environment.ContentRootPath, configuredDirectory);
    }

    private void MaybePruneOldFiles(string directory)
    {
        if (_options.RetentionDays <= 0)
        {
            return;
        }

        var todayUtcDate = DateTime.UtcNow.Date;
        if (_lastRetentionSweepUtcDate == todayUtcDate)
        {
            return;
        }

        lock (_writeLock)
        {
            if (_lastRetentionSweepUtcDate == todayUtcDate)
            {
                return;
            }

            var cutoffUtcDate = todayUtcDate.AddDays(-_options.RetentionDays);
            var files = Directory.GetFiles(directory, "signalr-diagnostics-*.log");
            foreach (var filePath in files)
            {
                try
                {
                    if (ShouldDeleteFile(filePath, cutoffUtcDate))
                    {
                        File.Delete(filePath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "SignalR diagnostics retention cleanup skipped for {FilePath}", filePath);
                }
            }

            _lastRetentionSweepUtcDate = todayUtcDate;
        }
    }

    private static bool ShouldDeleteFile(string filePath, DateTime cutoffUtcDate)
    {
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
        var dateToken = fileNameWithoutExtension.Split('-').LastOrDefault();
        if (!string.IsNullOrWhiteSpace(dateToken)
            && DateTime.TryParseExact(dateToken, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedDate))
        {
            return parsedDate.Date < cutoffUtcDate;
        }

        var fileInfo = new FileInfo(filePath);
        return fileInfo.LastWriteTimeUtc.Date < cutoffUtcDate;
    }

    private static string ResolveRouteMarker(string route, List<string> markers)
    {
        var pathOnly = RoutePathOnly(route);
        foreach (var marker in markers.Where(m => !string.IsNullOrWhiteSpace(m)))
        {
            var normalizedMarker = marker.Trim();
            if (!normalizedMarker.StartsWith('/'))
            {
                normalizedMarker = "/" + normalizedMarker.TrimStart('/');
            }

            if (pathOnly.Equals(normalizedMarker, StringComparison.OrdinalIgnoreCase)
                || pathOnly.StartsWith(normalizedMarker + "/", StringComparison.OrdinalIgnoreCase))
            {
                return normalizedMarker;
            }
        }

        return "(none)";
    }

    private string ResolveRouteMarker(string route)
    {
        return ResolveRouteMarker(route, _options.TargetRouteMarkers ?? new List<string>());
    }

    private static bool TryGetBrowserHintType(string eventType, out string hintType)
    {
        hintType = string.Empty;
        if (!eventType.StartsWith(BrowserEventPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var name = eventType[BrowserEventPrefix.Length..].Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        hintType = name;
        return true;
    }

    private DateTimeOffset ConvertToEt(DateTimeOffset utcTimestamp)
    {
        return TimeZoneInfo.ConvertTime(utcTimestamp, _easternTimeZone);
    }

    private static TimeZoneInfo ResolveEasternTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
            }
            catch
            {
                return TimeZoneInfo.Utc;
            }
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static string NormalizeRoute(string route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return "(unknown)";
        }

        var trimmed = route.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute))
        {
            trimmed = absolute.PathAndQuery;
        }

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return "/";
        }

        if (!trimmed.StartsWith('/'))
        {
            trimmed = "/" + trimmed.TrimStart('/');
        }

        return Sanitize(trimmed, "(unknown)");
    }

    private static string RoutePathOnly(string route)
    {
        var normalizedRoute = NormalizeRoute(route);
        if (normalizedRoute == "(unknown)")
        {
            return normalizedRoute;
        }

        var queryIndex = normalizedRoute.IndexOf('?');
        if (queryIndex >= 0)
        {
            normalizedRoute = normalizedRoute[..queryIndex];
        }

        var hashIndex = normalizedRoute.IndexOf('#');
        if (hashIndex >= 0)
        {
            normalizedRoute = normalizedRoute[..hashIndex];
        }

        return normalizedRoute;
    }

    private static string NormalizeUser(string user)
    {
        if (string.IsNullOrWhiteSpace(user))
        {
            return "(anonymous)";
        }

        return Sanitize(user, "(anonymous)");
    }

    private static string SanitizeDetails(string details)
    {
        var normalized = Sanitize(details, string.Empty);
        if (normalized.Length <= MaxDetailsLength)
        {
            return normalized;
        }

        return normalized[..MaxDetailsLength] + "...(truncated)";
    }

    private static string Sanitize(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('|', '/')
            .Trim();
    }

    private sealed class BrowserLifecycleHint
    {
        public string HintType { get; init; } = string.Empty;

        public string Route { get; init; } = string.Empty;

        public string TabId { get; init; } = string.Empty;

        public DateTimeOffset TimestampUtc { get; init; }
    }
}
