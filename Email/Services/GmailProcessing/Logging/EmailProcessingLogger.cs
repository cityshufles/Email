using Microsoft.Extensions.Logging;

namespace Email.Services.GmailProcessing.Logging
{
    /// <summary>
    /// Created: 2025-11-07 00:00 UTC
    /// Centralized logging helpers for Gmail processing v2 (Fix E).
    /// </summary>
    public static class EmailProcessingLogger
    {
        public static void LogFuzzyMatchUsed(ILogger logger, string context, double score, string reason)
        {
            logger.LogWarning("Fuzzy match used in {Context}. Score={Score:F3}. Reason={Reason}", context, score, reason);
        }

        public static void LogCustomerCreatedWithoutCode(ILogger logger, string normalizedEmail, string? normalizedPhone)
        {
            logger.LogWarning("Customer created without booking code context. Email={Email}, Phone={Phone}", normalizedEmail, normalizedPhone ?? "(null)");
        }
    }
}


