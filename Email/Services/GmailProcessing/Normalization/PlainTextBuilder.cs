using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace Email.Services.GmailProcessing.Normalization
{
    /// <summary>
    /// Created: 2025-11-07 00:00 UTC
    /// Builds condensed plain text from HTML.
    /// </summary>
    public static class PlainTextBuilder
    {
        public static string FromHtml(string? html)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var sb = new StringBuilder();
            void AppendWithNewline(string? value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    sb.AppendLine(value.Trim());
                }
            }

            foreach (var node in doc.DocumentNode.DescendantsAndSelf())
            {
                if (node.NodeType == HtmlNodeType.Text)
                {
                    var text = Regex.Replace(node.InnerText, @"\s+", " ").Trim();
                    if (!string.IsNullOrEmpty(text))
                    {
                        sb.Append(text);
                    }
                }
                else if (node.Name.Equals("br", System.StringComparison.OrdinalIgnoreCase) ||
                         node.Name.Equals("p", System.StringComparison.OrdinalIgnoreCase) ||
                         node.Name.Equals("div", System.StringComparison.OrdinalIgnoreCase) ||
                         node.Name.StartsWith("h", System.StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine();
                }
            }

            var result = sb.ToString();
            // Collapse excessive blank lines
            result = Regex.Replace(result, @"[ \t]+\r?\n", "\n");
            result = Regex.Replace(result, @"(\r?\n){3,}", "\n\n");
            return result.Trim();
        }
    }
}


