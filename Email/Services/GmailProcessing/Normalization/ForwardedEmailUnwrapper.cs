using System;
using System.Text.RegularExpressions;

namespace Email.Services.GmailProcessing.Normalization
{
    /// <summary>
    /// Created: 2025-11-07 00:00 UTC
    /// Unwraps forwarded email chains to reveal original vendor content.
    /// </summary>
    public static class ForwardedEmailUnwrapper
    {
        public static (string PlainText, string Html) Unwrap(string? rawHtml, string? rawText)
        {
            var plain = rawText ?? string.Empty;
            var html = rawHtml ?? string.Empty;

            // Detect common forwarded markers and try to extract the first segment (original content)
            // Patterns: "Begin forwarded message", "Forwarded message", "-------- Forwarded Message --------"
            var markers = new[]
            {
                "begin forwarded message",
                "forwarded message",
                "-------- forwarded message --------",
                "---------- forwarded message ----------"
            };

            string unwrapPlain(string text)
            {
                if (string.IsNullOrWhiteSpace(text)) return string.Empty;
                var lowered = text.ToLowerInvariant();
                foreach (var m in markers)
                {
                    var idx = lowered.IndexOf(m, StringComparison.Ordinal);
                    if (idx >= 0)
                    {
                        // Take content before the forwarded header if present, else after header
                        var before = text[..idx].Trim();
                        if (!string.IsNullOrWhiteSpace(before))
                        {
                            return before;
                        }
                        // Else, try to cut the header line and take subsequent content
                        var after = text[(idx + m.Length)..];
                        return after.Trim();
                    }
                }
                return text.Trim();
            }

            string unwrapHtml(string htmlIn)
            {
                if (string.IsNullOrWhiteSpace(htmlIn)) return string.Empty;
                // Coarse strip of blockquote sections often used in forwards
                // Remove nested "blockquote" forwarding chains conservatively
                var withoutBlockquotes = Regex.Replace(htmlIn, @"<blockquote[\s\S]*?>[\s\S]*?<\/blockquote>", string.Empty, RegexOptions.IgnoreCase);
                return withoutBlockquotes;
            }

            return (unwrapPlain(plain), unwrapHtml(html));
        }
    }
}


