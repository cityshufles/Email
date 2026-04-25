using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Email.Services;

public interface IPhotoUploadLog
{
    void Info(string message);
    void Error(string message);
}

public sealed class PhotoUploadLog : IPhotoUploadLog
{
    private readonly string _logDirectory;
    private readonly object _lock = new();
    private readonly ILogger<PhotoUploadLog> _logger;

    public PhotoUploadLog(IWebHostEnvironment env, IConfiguration config, ILogger<PhotoUploadLog> logger)
    {
        _logger = logger;
        var configuredPath = config["PhotoUploadLog:Directory"];
        var envPath = Environment.GetEnvironmentVariable("PHOTO_UPLOAD_LOG_DIR");

        _logDirectory = !string.IsNullOrWhiteSpace(configuredPath)
            ? configuredPath
            : !string.IsNullOrWhiteSpace(envPath)
                ? envPath
                : Path.Combine(env.ContentRootPath, "logs", "photo-upload");

        try
        {
            Directory.CreateDirectory(_logDirectory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create photo upload log directory {Path}", _logDirectory);
        }
    }

    public void Info(string message) => Write("INFO", message);

    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        var line = $"{DateTime.UtcNow:O} [{level}] {message}{Environment.NewLine}";
        try
        {
            var filePath = GetLogPath();
            lock (_lock)
            {
                File.AppendAllText(filePath, line);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write photo upload log line");
        }
    }

    private string GetLogPath()
    {
        var fileName = $"photo-upload-{DateTime.UtcNow:yyyyMMdd}.log";
        return Path.Combine(_logDirectory, fileName);
    }
}
