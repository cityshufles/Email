using System;
using Microsoft.Extensions.Configuration;

namespace Email.Services
{
    /// <summary>
    /// Runtime endpoint mode state for Checkfront calls within the current DI scope (Blazor circuit/request scope).
    /// </summary>
    public sealed class CheckfrontEndpointModeState
    {
        private readonly object _sync = new();

        public string ProductionEndpoint { get; }
        public string DevelopmentEndpoint { get; }
        public string CurrentMode { get; private set; }

        public CheckfrontEndpointModeState(IConfiguration configuration)
        {
            var section = configuration.GetSection("Checkfront");

            var configuredApiEndpoint = NormalizeEndpoint(section["ApiEndpoint"]);
            var configuredProduction = NormalizeEndpoint(section["ProductionApiEndpoint"]);
            var configuredDevelopment = NormalizeEndpoint(section["DevelopmentApiEndpoint"]);
            var configuredDefaultMode = NormalizeMode(section["DefaultEndpointMode"]);

            ProductionEndpoint = !string.IsNullOrWhiteSpace(configuredProduction)
                ? configuredProduction
                : configuredApiEndpoint;

            DevelopmentEndpoint = configuredDevelopment;

            if (!string.IsNullOrWhiteSpace(configuredDefaultMode))
            {
                CurrentMode = configuredDefaultMode;
            }
            else if (!string.IsNullOrWhiteSpace(DevelopmentEndpoint) &&
                     string.Equals(configuredApiEndpoint, DevelopmentEndpoint, StringComparison.OrdinalIgnoreCase))
            {
                CurrentMode = "Development";
            }
            else
            {
                CurrentMode = "Production";
            }
        }

        public bool SetMode(string? mode)
        {
            var normalized = NormalizeMode(mode);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            if (normalized.Equals("Development", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(DevelopmentEndpoint))
            {
                return false;
            }

            lock (_sync)
            {
                CurrentMode = normalized;
            }

            return true;
        }

        public CheckfrontEndpointModeSnapshot GetSnapshot()
        {
            lock (_sync)
            {
                var currentEndpoint = ResolveEndpoint(CurrentMode);
                return new CheckfrontEndpointModeSnapshot
                {
                    CurrentMode = CurrentMode,
                    CurrentEndpoint = currentEndpoint,
                    ProductionEndpoint = ProductionEndpoint,
                    DevelopmentEndpoint = DevelopmentEndpoint
                };
            }
        }

        public string GetActiveEndpoint()
        {
            lock (_sync)
            {
                return ResolveEndpoint(CurrentMode);
            }
        }

        private string ResolveEndpoint(string mode)
        {
            if (mode.Equals("Development", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(DevelopmentEndpoint))
            {
                return DevelopmentEndpoint;
            }

            return ProductionEndpoint;
        }

        private static string NormalizeMode(string? mode)
        {
            if (string.IsNullOrWhiteSpace(mode))
            {
                return string.Empty;
            }

            return mode.Trim().Equals("Development", StringComparison.OrdinalIgnoreCase)
                ? "Development"
                : mode.Trim().Equals("Production", StringComparison.OrdinalIgnoreCase)
                    ? "Production"
                    : string.Empty;
        }

        private static string NormalizeEndpoint(string? endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return string.Empty;
            }

            var trimmed = endpoint.Trim();
            return trimmed.EndsWith("/", StringComparison.Ordinal) ? trimmed : trimmed + "/";
        }
    }

    public sealed class CheckfrontEndpointModeSnapshot
    {
        public string CurrentMode { get; set; } = "Production";
        public string CurrentEndpoint { get; set; } = string.Empty;
        public string ProductionEndpoint { get; set; } = string.Empty;
        public string DevelopmentEndpoint { get; set; } = string.Empty;
    }
}

