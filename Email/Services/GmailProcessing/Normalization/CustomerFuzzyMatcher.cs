using System;
using System.Collections.Generic;
using System.Linq;
using Email.Services.GmailProcessing.Models;

namespace Email.Services.GmailProcessing.Normalization
{
    /// <summary>
    /// Created: 2025-11-07 00:00 UTC
    /// Fuzzy matching utilities for customers (Fix D).
    /// </summary>
    public static class CustomerFuzzyMatcher
    {
        public sealed class MatchCandidate
        {
            public Customer Customer { get; set; } = new Customer();
            public double Score { get; set; }
            public string Reason { get; set; } = string.Empty;
        }

        public static IReadOnlyList<MatchCandidate> RankCandidates(
            string customerName,
            string? phoneLast7,
            string normalizedEmailUser,
            string normalizedEmailDomain,
            IReadOnlyList<Customer> candidates)
        {
            var results = new List<MatchCandidate>();

            foreach (var c in candidates)
            {
                double score = 0.0;
                var reasons = new List<string>();

                // Name similarity using normalized Levenshtein over lowercased strings
                var targetName = (customerName ?? string.Empty).Trim().ToLowerInvariant();
                var candidateName = (c.FullName ?? string.Empty).Trim().ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(targetName) && !string.IsNullOrWhiteSpace(candidateName))
                {
                    var dist = LevenshteinDistance(targetName, candidateName);
                    var maxLen = Math.Max(targetName.Length, candidateName.Length);
                    var nameScore = 1.0 - Math.Min(1.0, (double)dist / Math.Max(1, maxLen));
                    score += nameScore * 0.6;
                    reasons.Add($"name={nameScore:F2}");
                }

                // Phone last7 exact match boost
                if (!string.IsNullOrWhiteSpace(phoneLast7) && !string.IsNullOrWhiteSpace(c.PhoneNumber))
                {
                    var digits = new string(c.PhoneNumber.Where(char.IsDigit).ToArray());
                    var last7 = digits.Length > 7 ? digits[^7..] : digits;
                    if (last7 == phoneLast7)
                    {
                        score += 0.3;
                        reasons.Add("phoneLast7=1.00");
                    }
                }

                // Email user/domain similarity (simple)
                if (!string.IsNullOrWhiteSpace(c.Email))
                {
                    var email = c.Email.ToLowerInvariant();
                    var parts = email.Split('@');
                    if (parts.Length == 2)
                    {
                        var user = parts[0];
                        var domain = parts[1];
                        if (!string.IsNullOrWhiteSpace(normalizedEmailUser))
                        {
                            var du = LevenshteinDistance(user, normalizedEmailUser);
                            var max = Math.Max(user.Length, normalizedEmailUser.Length);
                            var userScore = 1.0 - Math.Min(1.0, (double)du / Math.Max(1, max));
                            score += userScore * 0.05;
                            reasons.Add($"emailUser={userScore:F2}");
                        }
                        if (!string.IsNullOrWhiteSpace(normalizedEmailDomain))
                        {
                            score += domain.Equals(normalizedEmailDomain, StringComparison.OrdinalIgnoreCase) ? 0.05 : 0.0;
                            reasons.Add($"emailDomain={(domain.Equals(normalizedEmailDomain, StringComparison.OrdinalIgnoreCase) ? "1.00" : "0.00")}");
                        }
                    }
                }

                results.Add(new MatchCandidate
                {
                    Customer = c,
                    Score = Math.Min(1.0, score),
                    Reason = string.Join(",", reasons)
                });
            }

            // Sort by descending score
            results.Sort((a, b) => b.Score.CompareTo(a.Score));
            return results;
        }

        private static int LevenshteinDistance(string a, string b)
        {
            var n = a.Length;
            var m = b.Length;
            var d = new int[n + 1, m + 1];
            for (int i = 0; i <= n; i++) d[i, 0] = i;
            for (int j = 0; j <= m; j++) d[0, j] = j;
            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }
            return d[n, m];
        }
    }
}


