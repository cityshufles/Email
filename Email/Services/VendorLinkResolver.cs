using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Email.Models;
using Email.Models.Reports;

namespace Email.Services
{
    public sealed class VendorLinkResolution
    {
        public string VendorLink { get; init; } = string.Empty;
        public string VendorTourLink { get; init; } = string.Empty;
        public string VendorReviewLink { get; init; } = string.Empty;
        public string AllToursLink { get; init; } = string.Empty;
        public bool IsVendorAssignedToTour { get; init; } = true;
        public bool UsedTemplateVendorLink { get; init; }
    }

    public static class VendorLinkResolver
    {
        public static VendorLinkResolution Resolve(
            int? tourId,
            string? tourName,
            string? vendorName,
            string? stageHint,
            string? templateVendorLink,
            string? legacyTourVendorLink,
            string? legacyReviewLink,
            IReadOnlyList<TourLink>? tourLinks,
            IReadOnlyList<DbVendor>? vendors,
            IReadOnlyList<DbTour>? tours,
            IReadOnlyDictionary<string, HashSet<string>>? activeVendorAssignments)
        {
            var trimmedTemplateVendorLink = TrimToEmpty(templateVendorLink);
            var vendorKey = NormalizeVendorKey(vendorName);
            var isVendorAssigned = IsVendorAssignedToTour(
                tourId,
                tourName,
                vendorKey,
                tours,
                activeVendorAssignments);

            var allToursLink = ResolveAllToursLink(vendorName, vendors);

            TourLink? matchedLink = null;
            if (isVendorAssigned)
            {
                matchedLink = FindTourLinkByTourAndVendor(tourId, tourName, vendorName, tourLinks);
            }

            var vendorTourLink = isVendorAssigned
                ? ChooseFirstNonEmpty(matchedLink?.TourLinkUrl, legacyTourVendorLink)
                : string.Empty;
            var vendorReviewLink = isVendorAssigned
                ? ChooseFirstNonEmpty(matchedLink?.ReviewLink, legacyReviewLink)
                : string.Empty;

            var normalizedStage = NormalizeStageHint(stageHint);
            string vendorLink;
            if (!string.IsNullOrWhiteSpace(trimmedTemplateVendorLink))
            {
                vendorLink = trimmedTemplateVendorLink;
            }
            else if (string.Equals(normalizedStage, "thankyou", StringComparison.OrdinalIgnoreCase))
            {
                vendorLink = !string.IsNullOrWhiteSpace(vendorReviewLink)
                    ? vendorReviewLink
                    : vendorTourLink;
            }
            else
            {
                vendorLink = vendorTourLink;
            }

            return new VendorLinkResolution
            {
                VendorLink = vendorLink,
                VendorTourLink = vendorTourLink,
                VendorReviewLink = vendorReviewLink,
                AllToursLink = allToursLink,
                IsVendorAssignedToTour = isVendorAssigned,
                UsedTemplateVendorLink = !string.IsNullOrWhiteSpace(trimmedTemplateVendorLink)
            };
        }

        public static Dictionary<string, HashSet<string>> BuildActiveVendorAssignments(
            IEnumerable<DbVendorTour>? vendorTours,
            IEnumerable<DbVendor>? vendors)
        {
            var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            if (vendorTours is null || vendors is null)
            {
                return result;
            }

            var vendorsById = vendors
                .Where(v => v.Id > 0)
                .GroupBy(v => v.Id)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(v => v.UpdatedAt ?? v.CreatedAt ?? DateTime.MinValue).First(),
                    comparer: EqualityComparer<int>.Default);

            foreach (var vendorTour in vendorTours.Where(x => x.IsActive && x.VendorId > 0))
            {
                if (!vendorsById.TryGetValue(vendorTour.VendorId, out var vendorRecord))
                {
                    continue;
                }

                var vendorKey = NormalizeVendorKey(vendorRecord.VendorName);
                if (string.IsNullOrWhiteSpace(vendorKey))
                {
                    continue;
                }

                var masterTourKey = NormalizeMasterTourLookupKey(vendorTour.MasterTourName);
                if (string.IsNullOrWhiteSpace(masterTourKey))
                {
                    continue;
                }

                if (!result.TryGetValue(masterTourKey, out var vendorSet))
                {
                    vendorSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    result[masterTourKey] = vendorSet;
                }

                vendorSet.Add(vendorKey);
            }

            return result;
        }

        public static string ResolveAllToursLink(string? vendorName, IReadOnlyList<DbVendor>? vendors)
        {
            if (vendors is null || vendors.Count == 0)
            {
                return string.Empty;
            }

            var vendorKey = NormalizeVendorKey(vendorName);
            if (string.IsNullOrWhiteSpace(vendorKey))
            {
                return string.Empty;
            }

            var match = vendors.FirstOrDefault(v => string.Equals(NormalizeVendorKey(v.VendorName), vendorKey, StringComparison.OrdinalIgnoreCase));
            return TrimToEmpty(match?.AllToursLink);
        }

        public static string NormalizeStageHint(string? stageHint)
        {
            var s = (stageHint ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(s))
            {
                return "misc";
            }

            if (s.Contains("thank", StringComparison.Ordinal)) return "thankyou";
            if (s.Contains("dayof", StringComparison.Ordinal) || s.Contains("day of", StringComparison.Ordinal) || s.Contains("day-of", StringComparison.Ordinal)) return "dayOf";
            if (s.Contains("today", StringComparison.Ordinal)) return "today";
            if (s.Contains("tomorrow", StringComparison.Ordinal) || s.Contains("tmrw", StringComparison.Ordinal) || s.Contains("reminder", StringComparison.Ordinal)) return "tomorrow";
            if (Regex.IsMatch(s, @"\bdate\b", RegexOptions.CultureInvariant)) return "date";
            if (s.Contains("welcome", StringComparison.Ordinal)) return "welcome";
            if (s.Contains("promo", StringComparison.Ordinal) || s.Contains("promotion", StringComparison.Ordinal) || s.Contains("marketing", StringComparison.Ordinal)) return "promo";

            return "misc";
        }

        public static string ResolveTempSignature(string? stageHint, string? vendorName, string? allToursLink = null)
        {
            var normalizedStage = NormalizeStageHint(stageHint);
            var normalizedAllToursLink = TrimToEmpty(allToursLink);

            if (IsDateLikeStage(normalizedStage))
            {
                return string.IsNullOrWhiteSpace(normalizedAllToursLink)
                    ? "[missing allToursLink]"
                    : normalizedAllToursLink;
            }

            if (!string.IsNullOrWhiteSpace(normalizedAllToursLink))
            {
                return normalizedAllToursLink;
            }

            var vendorKey = NormalizeVendorKey(vendorName);
            if (string.Equals(vendorKey, "GURUWALK", StringComparison.OrdinalIgnoreCase))
            {
                return "https://www.guruwalk.com/gurus/5vfugt2iidv27yrsd6fm";
            }

            if (string.Equals(vendorKey, "FREETOUR", StringComparison.OrdinalIgnoreCase))
            {
                return "https://www.freetour.com/company/94254";
            }

            return string.Empty;
        }

        public static string NormalizeVendorKey(string? vendor)
        {
            if (string.IsNullOrWhiteSpace(vendor))
            {
                return string.Empty;
            }

            var compact = Regex.Replace(vendor.Trim().ToUpperInvariant(), "[^A-Z0-9]", "");

            if (compact.Contains("GURUWALK", StringComparison.Ordinal) || compact.StartsWith("GURU", StringComparison.Ordinal))
            {
                return "GURUWALK";
            }

            if (compact.Contains("FREETOUR", StringComparison.Ordinal))
            {
                return "FREETOUR";
            }

            if (compact.Contains("GETYOURGUIDE", StringComparison.Ordinal) || compact == "GYG")
            {
                return "GETYOURGUIDE";
            }

            if (compact.Contains("VIATOR", StringComparison.Ordinal))
            {
                return "VIATOR";
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

        public static string NormalizeMasterTourLookupKey(string? value)
        {
            return NormalizeTourKey(StripTrailingTimeLabel(value));
        }

        private static bool IsDateLikeStage(string normalizedStage)
        {
            return string.Equals(normalizedStage, "date", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedStage, "today", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedStage, "tomorrow", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveMasterTourLookupKey(int? tourId, string? tourName, IReadOnlyList<DbTour>? tours)
        {
            if (tourId.HasValue && tourId.Value > 0 && tours is not null)
            {
                var byId = tours.FirstOrDefault(x => x.Id == tourId.Value);
                if (byId is not null)
                {
                    return NormalizeMasterTourLookupKey(!string.IsNullOrWhiteSpace(byId.MasterTourName) ? byId.MasterTourName : byId.TourName);
                }
            }

            var normalizedInput = NormalizeTourKey(tourName);
            if (!string.IsNullOrWhiteSpace(normalizedInput) && tours is not null && tours.Count > 0)
            {
                var byName = tours
                    .Where(t =>
                        string.Equals(NormalizeTourKey(t.MasterTourName), normalizedInput, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(NormalizeTourKey(t.TourName), normalizedInput, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(t => t.IsActive)
                    .ThenByDescending(t => t.UpdatedAt ?? t.CreatedAt ?? DateTime.MinValue)
                    .ThenByDescending(t => t.Id)
                    .FirstOrDefault();

                if (byName is not null)
                {
                    return NormalizeMasterTourLookupKey(!string.IsNullOrWhiteSpace(byName.MasterTourName) ? byName.MasterTourName : byName.TourName);
                }
            }

            return NormalizeMasterTourLookupKey(tourName);
        }

        private static bool IsVendorAssignedToTour(
            int? tourId,
            string? tourName,
            string vendorKey,
            IReadOnlyList<DbTour>? tours,
            IReadOnlyDictionary<string, HashSet<string>>? activeVendorAssignments)
        {
            if (string.IsNullOrWhiteSpace(vendorKey) || activeVendorAssignments is null || activeVendorAssignments.Count == 0)
            {
                return true;
            }

            var masterTourKey = ResolveMasterTourLookupKey(tourId, tourName, tours);
            if (string.IsNullOrWhiteSpace(masterTourKey))
            {
                return true;
            }

            if (!activeVendorAssignments.TryGetValue(masterTourKey, out var assignedVendors))
            {
                return true;
            }

            return assignedVendors.Contains(vendorKey);
        }

        private static TourLink? FindTourLinkByTourAndVendor(
            int? tourId,
            string? tourName,
            string? vendorName,
            IReadOnlyList<TourLink>? tourLinks)
        {
            if (tourLinks is null || tourLinks.Count == 0)
            {
                return null;
            }

            var vendorKey = NormalizeVendorKey(vendorName);
            if (string.IsNullOrWhiteSpace(vendorKey))
            {
                return null;
            }

            var byVendor = tourLinks
                .Where(x => string.Equals(NormalizeVendorKey(x.Vendor), vendorKey, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (byVendor.Count == 0)
            {
                return null;
            }

            if (tourId.HasValue && tourId.Value > 0)
            {
                var byTourId = byVendor.Where(x => x.TourId == tourId.Value).ToList();
                var preferredById = PickBestTourLink(byTourId);
                if (preferredById is not null)
                {
                    return preferredById;
                }
            }

            var tourKey = NormalizeTourKey(tourName);
            if (!string.IsNullOrWhiteSpace(tourKey))
            {
                var byTourName = byVendor
                    .Where(x => string.Equals(NormalizeTourKey(x.TourName), tourKey, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var preferredByName = PickBestTourLink(byTourName);
                if (preferredByName is not null)
                {
                    return preferredByName;
                }
            }

            return PickBestTourLink(byVendor);
        }

        private static TourLink? PickBestTourLink(List<TourLink> links)
        {
            if (links is null || links.Count == 0)
            {
                return null;
            }

            return links
                .OrderByDescending(ScoreTourLink)
                .ThenByDescending(x => x.UpdatedAt)
                .FirstOrDefault();
        }

        private static int ScoreTourLink(TourLink link)
        {
            var score = 0;
            if (link.TourId.HasValue && link.TourId.Value > 0) score += 4;
            if (link.TourDay is null) score += 2;
            if (string.IsNullOrWhiteSpace(link.TourTime)) score += 2;
            if (!string.IsNullOrWhiteSpace(link.TourLinkUrl)) score += 1;
            if (!string.IsNullOrWhiteSpace(link.ReviewLink)) score += 1;
            return score;
        }

        private static string NormalizeTourKey(string? tourName)
        {
            if (string.IsNullOrWhiteSpace(tourName))
            {
                return string.Empty;
            }

            var cleaned = Regex.Replace(tourName.Trim().ToLowerInvariant(), "[^a-z0-9]+", " ");
            return Regex.Replace(cleaned, "\\s+", " ").Trim();
        }

        private static string StripTrailingTimeLabel(string? tourName)
        {
            var value = (tourName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var separators = new[] { " - ", " \u2014 ", " \u2013 ", " \u00E2\u20AC\u201D ", " \u00E2\u20AC\u201C ", " \u00C3\u00A2\u00E2\u201A\u00AC\u00E2\u20AC\u009D ", " \u00C3\u00A2\u00E2\u201A\u00AC\u00E2\u20AC\u015C " };
            foreach (var separator in separators)
            {
                var index = value.LastIndexOf(separator, StringComparison.Ordinal);
                if (index <= 0)
                {
                    continue;
                }

                var tail = value[(index + separator.Length)..].Trim();
                if (LooksLikeTimeToken(tail))
                {
                    return value[..index].Trim();
                }
            }

            return value;
        }

        private static bool LooksLikeTimeToken(string? value)
        {
            var token = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            if (Regex.IsMatch(token, @"^\d{1,2}:\d{2}$", RegexOptions.CultureInvariant))
            {
                return true;
            }

            if (Regex.IsMatch(token, @"^\d{1,2}(:\d{2})?\s?(AM|PM)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return true;
            }

            return false;
        }

        private static string ChooseFirstNonEmpty(params string?[] values)
        {
            if (values is null || values.Length == 0)
            {
                return string.Empty;
            }

            foreach (var value in values)
            {
                var trimmed = TrimToEmpty(value);
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    return trimmed;
                }
            }

            return string.Empty;
        }

        private static string TrimToEmpty(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}

