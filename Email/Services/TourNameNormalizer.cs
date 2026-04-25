// Created: 2026-02-11 14:50 UTC
// Purpose: DB-driven tour name normalization service - single source of truth for tour name variant lookups

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Email.Models;
using Microsoft.Data.SqlClient;

namespace Email.Services
{
    /// <summary>
    /// Service for resolving tour name variants to master tour names using database lookups
    /// </summary>
    public interface ITourNameNormalizer
    {
        /// <summary>
        /// Resolve an incoming tour name to its master tour name and TourId
        /// </summary>
        /// <param name="incomingTourName">Raw tour name from vendor/email</param>
        /// <param name="vendorName">Optional vendor name for more specific matching</param>
        /// <returns>TourId and MasterTourName if found, null otherwise</returns>
        Task<TourNameResolution?> ResolveToMasterTourAsync(string incomingTourName, string? vendorName = null);

        /// <summary>
        /// Get the master tour name for a specific TourId
        /// </summary>
        Task<string?> GetMasterTourNameAsync(int tourId);
    }

    /// <summary>
    /// Result of tour name resolution
    /// </summary>
    public class TourNameResolution
    {
        public int TourId { get; set; }
        public string MasterTourName { get; set; } = string.Empty;
        public string? MasterTourNameDesktop { get; set; }
        public string? MasterTourNameMobile { get; set; }
    }

    /// <summary>
    /// SQL implementation of tour name normalization using TourNameMappings table
    /// </summary>
    public sealed class TourNameNormalizer : ITourNameNormalizer
    {
        private readonly SqlConnectionFactory _connectionFactory;

        public TourNameNormalizer(SqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<TourNameResolution?> ResolveToMasterTourAsync(string incomingTourName, string? vendorName = null)
        {
            if (string.IsNullOrWhiteSpace(incomingTourName))
            {
                return null;
            }

            try
            {
                var incomingVendorKey = NormalizeVendorKey(vendorName);
                var lookupCandidates = BuildLookupCandidates(incomingTourName);

                if (lookupCandidates.Count == 0)
                {
                    return null;
                }

                var candidatePredicates = string.Join(
                    " OR ",
                    lookupCandidates.Select((_, index) => $"LOWER(LTRIM(RTRIM(ISNULL(m.IncomingTourName, N'')))) = @Name{index}"));

                var sql = $@"
SELECT
    t.Id,
    t.MasterTourName,
    t.MasterTourNameDesktop,
    t.MasterTourNameMobile,
    m.VendorName,
    m.UpdatedAt,
    m.Id
FROM dbo.TourNameMappings m
INNER JOIN dbo.Tours t ON t.Id = m.TourId AND t.IsActive = 1
WHERE m.IsActive = 1
  AND ({candidatePredicates});";

                using var conn = _connectionFactory.CreateOpenConnection();
                using var cmd = new SqlCommand(sql, conn);

                for (var i = 0; i < lookupCandidates.Count; i++)
                {
                    cmd.Parameters.AddWithValue($"@Name{i}", lookupCandidates[i]);
                }

                var candidates = new List<MappingCandidateRow>();

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var mappedVendorName = reader.IsDBNull(4) ? null : reader.GetString(4);
                    candidates.Add(new MappingCandidateRow
                    {
                        TourId = reader.GetInt32(0),
                        MasterTourName = reader.GetString(1),
                        MasterTourNameDesktop = reader.IsDBNull(2) ? null : reader.GetString(2),
                        MasterTourNameMobile = reader.IsDBNull(3) ? null : reader.GetString(3),
                        VendorKey = NormalizeVendorKey(mappedVendorName),
                        UpdatedAt = reader.IsDBNull(5) ? DateTime.MinValue : reader.GetDateTime(5),
                        MappingId = reader.IsDBNull(6) ? 0 : reader.GetInt32(6)
                    });
                }

                if (candidates.Count == 0)
                {
                    return null;
                }

                var bestMatch = candidates
                    .OrderBy(c => GetVendorMatchPriority(incomingVendorKey, c.VendorKey))
                    .ThenByDescending(c => c.UpdatedAt)
                    .ThenByDescending(c => c.MappingId)
                    .First();

                return new TourNameResolution
                {
                    TourId = bestMatch.TourId,
                    MasterTourName = bestMatch.MasterTourName,
                    MasterTourNameDesktop = bestMatch.MasterTourNameDesktop,
                    MasterTourNameMobile = bestMatch.MasterTourNameMobile
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TourNameNormalizer] Error resolving tour name: {ex.Message}");
                return null;
            }
        }

        public async Task<string?> GetMasterTourNameAsync(int tourId)
        {
            try
            {
                const string sql = "SELECT MasterTourName FROM dbo.Tours WHERE Id = @TourId AND IsActive = 1;";

                using var conn = _connectionFactory.CreateOpenConnection();
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@TourId", tourId);

                var result = await cmd.ExecuteScalarAsync();
                return result as string;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TourNameNormalizer] Error getting master tour name: {ex.Message}");
                return null;
            }
        }

        private static List<string> BuildLookupCandidates(string incomingTourName)
        {
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddNameVariant(candidates, incomingTourName);

            var trimmed = incomingTourName.Trim();
            AddNameVariant(candidates, trimmed);

            var decoded = WebUtility.HtmlDecode(trimmed);
            AddNameVariant(candidates, decoded);

            var compactWhitespace = NormalizeWhitespace(decoded);
            AddNameVariant(candidates, compactWhitespace);

            return candidates
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.ToLowerInvariant())
                .ToList();
        }

        private static void AddNameVariant(HashSet<string> candidates, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            var value = raw.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            candidates.Add(value);

            if (value.Contains("&amp;", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(value.Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase));
            }

            if (value.Contains('&'))
            {
                candidates.Add(value.Replace("&", "&amp;", StringComparison.Ordinal));
            }
        }

        private static string NormalizeWhitespace(string value)
        {
            var prepared = value
                .Replace('\t', ' ')
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Replace('\u00A0', ' ');

            var sb = new StringBuilder(prepared.Length);
            var previousWasWhitespace = false;

            foreach (var ch in prepared)
            {
                if (char.IsWhiteSpace(ch))
                {
                    if (!previousWasWhitespace)
                    {
                        sb.Append(' ');
                        previousWasWhitespace = true;
                    }

                    continue;
                }

                sb.Append(ch);
                previousWasWhitespace = false;
            }

            return sb.ToString().Trim();
        }

        private static string NormalizeVendorKey(string? vendorName)
        {
            if (string.IsNullOrWhiteSpace(vendorName))
            {
                return string.Empty;
            }

            var compact = new string(vendorName
                .Trim()
                .ToUpperInvariant()
                .Where(char.IsLetterOrDigit)
                .ToArray());

            if (compact.Contains("VIATOR", StringComparison.Ordinal))
            {
                return "VIATOR";
            }

            if (compact.Contains("GURUWALK", StringComparison.Ordinal) || compact.StartsWith("GURU", StringComparison.Ordinal))
            {
                return "GURUWALK";
            }

            if (compact.Contains("FREETOUR", StringComparison.Ordinal))
            {
                return "FREETOUR";
            }

            if (compact.Contains("GETYOURGUIDE", StringComparison.Ordinal) || compact.Equals("GYG", StringComparison.Ordinal))
            {
                return "GETYOURGUIDE";
            }

            if (compact.Contains("AIRBNB", StringComparison.Ordinal))
            {
                return "AIRBNB";
            }

            if (compact.Contains("CIVITATIS", StringComparison.Ordinal) || compact.Contains("CIVATASIS", StringComparison.Ordinal))
            {
                return "CIVITATIS";
            }

            if (compact.Contains("CITYSHUFFLES", StringComparison.Ordinal) || compact.Contains("WEBSITE", StringComparison.Ordinal))
            {
                return "WEBSITE";
            }

            return compact;
        }

        private static int GetVendorMatchPriority(string incomingVendorKey, string mappedVendorKey)
        {
            if (!string.IsNullOrWhiteSpace(incomingVendorKey))
            {
                if (string.Equals(incomingVendorKey, mappedVendorKey, StringComparison.OrdinalIgnoreCase))
                {
                    return 0;
                }

                if (string.IsNullOrWhiteSpace(mappedVendorKey))
                {
                    return 1;
                }

                return 2;
            }

            return string.IsNullOrWhiteSpace(mappedVendorKey) ? 0 : 1;
        }

        private sealed class MappingCandidateRow
        {
            public int TourId { get; set; }
            public string MasterTourName { get; set; } = string.Empty;
            public string? MasterTourNameDesktop { get; set; }
            public string? MasterTourNameMobile { get; set; }
            public string VendorKey { get; set; } = string.Empty;
            public DateTime UpdatedAt { get; set; }
            public int MappingId { get; set; }
        }
    }
}
