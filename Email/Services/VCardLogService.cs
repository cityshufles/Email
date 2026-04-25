using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace Email.Services
{
    /// <summary>
    /// Created: 2025-12-19 00:00 UTC
    /// Purpose: HTML logging service for vCard download tracking (button clicks, API calls, exceptions).
    /// Updated: 2025-12-19 - Added detailed request/response logging with user agent, device info, headers
    /// </summary>
    public sealed class VCardLogService
    {
        private readonly string _logDirectory;
        private readonly object _lockObject = new object();
        private readonly IHttpContextAccessor _httpContextAccessor;

        public VCardLogService(IWebHostEnvironment environment, IHttpContextAccessor httpContextAccessor)
        {
            // Store logs in wwwroot/logs/vcard (accessible but separate from app files)
            _logDirectory = Path.Combine(environment.WebRootPath ?? environment.ContentRootPath ?? ".", "logs", "vcard");
            _httpContextAccessor = httpContextAccessor;
            EnsureLogDirectoryExists();
        }

        private void EnsureLogDirectoryExists()
        {
            try
            {
                if (!Directory.Exists(_logDirectory))
                {
                    Directory.CreateDirectory(_logDirectory);
                }
            }
            catch
            {
                // Silent fail - logging is non-critical
            }
        }

        /// <summary>
        /// Created: 2025-12-19 00:00 UTC
        /// Log a vCard action with detailed request/device info
        /// </summary>
        public void LogAction(string action, string details, string? url = null, Exception? exception = null)
        {
            return;
            try
            {
                var logFile = Path.Combine(_logDirectory, $"vcard_log_{DateTime.UtcNow:yyyy-MM-dd}.html");
                var estTime = ConvertToEstTime(DateTime.UtcNow);
                var clientInfo = GetClientInfo();
                var entry = BuildDetailedLogEntry(estTime, action, details, url, exception, clientInfo);

                lock (_lockObject)
                {
                    var fileExists = File.Exists(logFile);
                    var content = fileExists ? File.ReadAllText(logFile) : GetHtmlHeader();
                    
                    // Remove closing tags to insert new entry
                    if (fileExists && content.Contains("</body>"))
                    {
                        content = content.Replace("</body></html>", "");
                    }
                    else if (!fileExists)
                    {
                        content = GetHtmlHeader();
                    }

                    content += entry + Environment.NewLine + "</body></html>";

                    File.WriteAllText(logFile, content, Encoding.UTF8);
                }
            }
            catch
            {
                // Silent fail - logging is non-critical
            }
        }

        private ClientInfo GetClientInfo()
        {
            var httpContext = _httpContextAccessor?.HttpContext;
            if (httpContext == null)
            {
                return new ClientInfo();
            }

            var userAgent = httpContext.Request.Headers["User-Agent"].ToString() ?? "Unknown";
            var ipAddress = httpContext.Connection?.RemoteIpAddress?.ToString() ?? "Unknown";
            var isIPhone = userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase);
            var isIPad = userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase);
            var isSafari = userAgent.Contains("Safari", StringComparison.OrdinalIgnoreCase);
            var isChrome = userAgent.Contains("Chrome", StringComparison.OrdinalIgnoreCase);
            var isFirefox = userAgent.Contains("Firefox", StringComparison.OrdinalIgnoreCase);

            var browser = isChrome ? "Chrome" : (isFirefox ? "Firefox" : (isSafari ? "Safari" : "Unknown"));
            var device = isIPhone ? "iPhone" : (isIPad ? "iPad" : "Desktop");

            return new ClientInfo
            {
                UserAgent = userAgent,
                IpAddress = ipAddress,
                Device = device,
                Browser = browser,
                IsIPhone = isIPhone,
                IsIPad = isIPad,
                IsSafari = isSafari
            };
        }

        private static string ConvertToEstTime(DateTime utcTime)
        {
            var estZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            var estTime = TimeZoneInfo.ConvertTime(utcTime, estZone);
            return estTime.ToString("MM/dd/yyyy hh:mm:ss.fff tt zzz");
        }

        private static string GetHtmlHeader()
        {
            return @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""utf-8"" />
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
    <title>vCard Download Logs - Detailed</title>
    <style>
        body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; margin: 20px; background: #f5f5f5; color: #333; }
        h1 { color: #007bff; border-bottom: 3px solid #007bff; padding-bottom: 10px; }
        .entry { background: white; border-left: 5px solid #007bff; margin: 15px 0; padding: 20px; border-radius: 4px; box-shadow: 0 2px 8px rgba(0,0,0,0.15); }
        .timestamp { color: #666; font-size: 0.95em; margin-bottom: 10px; font-weight: bold; background: #f9f9f9; padding: 8px; border-radius: 3px; }
        .action { font-weight: bold; color: #007bff; font-size: 1.15em; margin-bottom: 8px; }
        .details { color: #333; margin: 8px 0; line-height: 1.6; }
        .url { color: #28a745; font-family: 'Courier New', monospace; word-break: break-all; margin: 8px 0; background: #f0f8f0; padding: 8px; border-radius: 3px; font-size: 0.9em; }
        .client-info { background: #e7f3ff; border: 1px solid #b3d9ff; padding: 10px; border-radius: 3px; margin: 8px 0; font-size: 0.9em; }
        .client-info strong { color: #004085; }
        .exception { color: #721c24; background: #f8d7da; padding: 12px; border-radius: 3px; margin-top: 10px; font-family: 'Courier New', monospace; white-space: pre-wrap; word-break: break-word; border-left: 4px solid #dc3545; font-size: 0.85em; }
        .success { border-left-color: #28a745; }
        .error { border-left-color: #dc3545; }
        .warning { border-left-color: #ffc107; }
        .info { border-left-color: #17a2b8; }
        .device-iphone { border-left-color: #ff6b6b; background: #fff5f5; }
        .device-ipad { border-left-color: #ffa500; background: #fffbf0; }
    </style>
</head>
<body>
    <h1>📱 vCard Download Activity Log (Detailed)</h1>
    <p style=""color: #666; margin: 10px 0;""><strong>Time Zone:</strong> Eastern Standard Time (EST) | <strong>Format:</strong> MM/DD/YYYY HH:MM:SS.fff AM/PM EST</p>
";
        }

        private static string BuildDetailedLogEntry(string timestamp, string action, string details, string? url, Exception? exception, ClientInfo clientInfo)
        {
            var sb = new StringBuilder();
            var cssClass = "entry";
            
            if (exception != null)
                cssClass += " error";
            else if (action.Contains("Success") || action.Contains("Downloaded"))
                cssClass += " success";
            else if (action.Contains("Failed"))
                cssClass += " warning";
            else if (action.Contains("Clicked"))
                cssClass += " info";
            
            // Add device-specific styling for iPhone/iPad
            if (clientInfo.IsIPhone)
                cssClass += " device-iphone";
            else if (clientInfo.IsIPad)
                cssClass += " device-ipad";
            
            sb.AppendLine($"    <div class=\"{cssClass}\">");
            sb.AppendLine($"        <div class=\"timestamp\">⏰ {EscapeHtml(timestamp)}</div>");
            sb.AppendLine($"        <div class=\"action\">{BuildActionIcon(action)} {EscapeHtml(action)}</div>");
            
            if (!string.IsNullOrWhiteSpace(details))
            {
                sb.AppendLine($"        <div class=\"details\">📝 {EscapeHtml(details)}</div>");
            }
            
            // Client Info Box
            sb.AppendLine($"        <div class=\"client-info\">");
            sb.AppendLine($"            <div><strong>📱 Device:</strong> {EscapeHtml(clientInfo.Device)} {(clientInfo.IsIPhone ? "📲" : clientInfo.IsIPad ? "📱" : "💻")}</div>");
            sb.AppendLine($"            <div><strong>🌐 Browser:</strong> {EscapeHtml(clientInfo.Browser)}</div>");
            sb.AppendLine($"            <div><strong>🔗 IP Address:</strong> {EscapeHtml(clientInfo.IpAddress)}</div>");
            sb.AppendLine($"            <div><strong>👤 User Agent:</strong> <br/><code style=\"font-size: 0.85em; word-break: break-all;\">{EscapeHtml(clientInfo.UserAgent)}</code></div>");
            sb.AppendLine($"        </div>");
            
            if (!string.IsNullOrWhiteSpace(url))
            {
                sb.AppendLine($"        <div class=\"url\">🔗 URL: {EscapeHtml(url)}</div>");
            }
            
            if (exception != null)
            {
                sb.AppendLine($"        <div class=\"exception\">");
                sb.AppendLine($"⚠️ EXCEPTION DETAILS:");
                sb.AppendLine($"Type: {EscapeHtml(exception.GetType().Name)}");
                sb.AppendLine($"Message: {EscapeHtml(exception.Message)}");
                if (exception.StackTrace != null)
                {
                    sb.AppendLine($"\nStack Trace:");
                    sb.AppendLine(EscapeHtml(exception.StackTrace));
                }
                if (exception.InnerException != null)
                {
                    sb.AppendLine($"\nInner Exception:");
                    sb.AppendLine(EscapeHtml(exception.InnerException.ToString()));
                }
                sb.AppendLine($"        </div>");
            }
            
            sb.AppendLine("    </div>");
            return sb.ToString();
        }

        private static string BuildActionIcon(string action)
        {
            return action switch
            {
                _ when action.Contains("Button Clicked") => "🖱️",
                _ when action.Contains("Primary Path Attempt") => "🎯",
                _ when action.Contains("Primary Path Success") => "✅",
                _ when action.Contains("Primary Path Failed") => "❌",
                _ when action.Contains("Fallback Started") => "⚙️",
                _ when action.Contains("Fallback Success") => "✅",
                _ when action.Contains("Download Success") => "⬇️",
                _ when action.Contains("Download Exception") => "💥",
                _ when action.Contains("Database Error") => "🗄️",
                _ when action.Contains("Endpoint Called") => "📡",
                _ when action.Contains("API") => "🔌",
                _ when action.Contains("vCard Generation") => "📋",
                _ when action.Contains("Mapping") => "🗺️",
                _ when action.Contains("Data Retrieved") => "📊",
                _ when action.Contains("No Data") => "⚠️",
                _ when action.Contains("Exception") => "💥",
                _ => "ℹ️"
            };
        }

        private static string EscapeHtml(string? text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&#39;");
        }
    }

    /// <summary>
    /// Client device and browser information
    /// </summary>
    public class ClientInfo
    {
        public string UserAgent { get; set; } = "Unknown";
        public string IpAddress { get; set; } = "Unknown";
        public string Device { get; set; } = "Desktop";
        public string Browser { get; set; } = "Unknown";
        public bool IsIPhone { get; set; }
        public bool IsIPad { get; set; }
        public bool IsSafari { get; set; }
    }
}

