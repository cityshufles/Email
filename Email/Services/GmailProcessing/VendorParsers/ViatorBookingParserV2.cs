using Email.Services.GmailProcessing.Models;
using Microsoft.Extensions.Logging;

namespace Email.Services.GmailProcessing.VendorParsers
{
    /// <summary>
    /// Created: 2026-04-03 00:00 UTC
    /// V2 Viator booking parser entry point. Delegates to the upgraded ViatorBookingParser implementation.
    /// </summary>
    public sealed class ViatorBookingParserV2 : BaseParser
    {
        private readonly ViatorBookingParser _inner;

        public ViatorBookingParserV2(ILogger<ViatorBookingParser>? logger = null)
        {
            _inner = new ViatorBookingParser(logger);
        }

        public override VendorParseResult Parse(string? subject, string? htmlBody, string? textBody)
            => _inner.Parse(subject, htmlBody, textBody);
    }
}
