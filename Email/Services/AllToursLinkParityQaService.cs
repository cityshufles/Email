using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Email.Models;
using Email.Models.Mobile;
using Email.Models.Reports;
using Email.TourTreeViewShapedData.Enums;
using Email.TourTreeViewShapedData.Models;

namespace Email.Services
{
    /// <summary>
    /// Read-only diagnostics implementation for verifying AllToursLink parity
    /// across vendors, tours, messages, and mobile rendering paths.
    /// </summary>
    public sealed class AllToursLinkParityQaService : IAllToursLinkParityQaService
    {
        private readonly ITourTreeService _tourTreeService;
        private readonly IVendorsApiService _vendorsApi;
        private readonly IToursApiService _toursApi;
        private readonly ITourLinkService _tourLinkService;
        private readonly ITourMessagesApiService _tourMessagesApi;
        private readonly MessageTemplateService _messageTemplateService;

        public AllToursLinkParityQaService(
            ITourTreeService tourTreeService,
            IVendorsApiService vendorsApi,
            IToursApiService toursApi,
            ITourLinkService tourLinkService,
            ITourMessagesApiService tourMessagesApi,
            MessageTemplateService messageTemplateService)
        {
            _tourTreeService = tourTreeService;
            _vendorsApi = vendorsApi;
            _toursApi = toursApi;
            _tourLinkService = tourLinkService;
            _tourMessagesApi = tourMessagesApi;
            _messageTemplateService = messageTemplateService;
        }

        public async Task<AllToursLinkParityQaSnapshot> GetSnapshotAsync(AllToursLinkParityQaQuery? query = null, CancellationToken ct = default)
        {
            var effective = NormalizeQuery(query);

            var vendorsTask = _vendorsApi.GetVendorsAsync(ct);
            var vendorToursTask = _vendorsApi.GetVendorToursAsync(null, ct);
            var toursTask = _toursApi.GetToursAsync(ct);
            var linksTask = _tourLinkService.GetAllAsync();
            var messagesTask = _tourMessagesApi.GetTourMessagesAsync(ct);
            var treeTask = _tourTreeService.GetTourTreeDataShapedAsync(
                TreeDataFilterType.ActiveBookings,
                effective.DateFrom,
                effective.DateTo,
                string.IsNullOrWhiteSpace(effective.Vendor) ? null : effective.Vendor!.Trim());

            await Task.WhenAll(vendorsTask, vendorToursTask, toursTask, linksTask, messagesTask, treeTask);

            var vendors = vendorsTask.Result ?? new List<DbVendor>();
            var vendorTours = vendorToursTask.Result ?? new List<DbVendorTour>();
            var tours = toursTask.Result ?? new List<DbTour>();
            var tourLinks = linksTask.Result ?? new List<TourLink>();
            var messages = messagesTask.Result ?? new List<DbTourMessage>();
            var tree = treeTask.Result;

            var activeVendorAssignments = VendorLinkResolver.BuildActiveVendorAssignments(vendorTours, vendors);
            var templates = SelectTemplates(messages, effective.MaxTemplates);
            var allContexts = ExtractWalkerContexts(tree, tours, effective.Vendor);
            var sampledContexts = SampleWalkerContexts(allContexts, effective.MaxVendors, effective.MaxWalkersPerVendor);

            var rows = BuildRows(
                sampledContexts,
                templates,
                vendors,
                tours,
                tourLinks,
                activeVendorAssignments);

            var gaps = BuildGaps(vendors, vendorTours, sampledContexts, effective.Vendor);
            var summary = BuildSummary(rows, gaps);

            return new AllToursLinkParityQaSnapshot
            {
                GeneratedAtUtc = DateTime.UtcNow,
                EffectiveQuery = effective,
                Summary = summary,
                Rows = rows,
                Gaps = gaps
            };
        }

        private List<AllToursLinkParityQaRow> BuildRows(
            List<WalkerContext> contexts,
            List<DbTourMessage> templates,
            List<DbVendor> vendors,
            List<DbTour> tours,
            List<TourLink> tourLinks,
            IReadOnlyDictionary<string, HashSet<string>> activeVendorAssignments)
        {
            var rows = new List<AllToursLinkParityQaRow>();

            foreach (var context in contexts)
            {
                var matchedVendor = vendors.FirstOrDefault(v =>
                    string.Equals(
                        VendorLinkResolver.NormalizeVendorKey(v.VendorName),
                        context.VendorKey,
                        StringComparison.OrdinalIgnoreCase));

                foreach (var template in templates)
                {
                    var templateContainsAllTours = ContainsToken(template.MessageContent, "{allToursLink}");
                    var templateContainsTempSignature = ContainsToken(template.MessageContent, "{tempSignature}");

                    var messageStageHint = $"{template.MessageType} {template.MessageName}".Trim();
                    var mobileStageHint = !string.IsNullOrWhiteSpace(template.MessageType)
                        ? template.MessageType
                        : template.MessageName;

                    var messageResolved = VendorLinkResolver.Resolve(
                        context.TourId,
                        context.TourName,
                        context.VendorName,
                        messageStageHint,
                        null,
                        context.LegacyTourVendorLink,
                        context.LegacyReviewLink,
                        tourLinks,
                        vendors,
                        tours,
                        activeVendorAssignments);

                    var toursResolved = VendorLinkResolver.Resolve(
                        context.TourId,
                        context.TourName,
                        context.VendorName,
                        messageStageHint,
                        template.VendorLink,
                        context.LegacyTourVendorLink,
                        context.LegacyReviewLink,
                        tourLinks,
                        vendors,
                        tours,
                        activeVendorAssignments);

                    var mobileResolved = VendorLinkResolver.Resolve(
                        null,
                        context.TourName,
                        context.VendorName,
                        mobileStageHint,
                        template.VendorLink,
                        null,
                        null,
                        tourLinks,
                        vendors,
                        tours,
                        activeVendorAssignments);

                    var dbAllToursLink = TrimToEmpty(matchedVendor?.AllToursLink);
                    var dbOutput = ResolveLinkTokenDisplayValue(dbAllToursLink, "allToursLink");
                    var messagesOutput = RenderAllToursProbe(messageResolved.AllToursLink);
                    var toursOutput = RenderAllToursProbe(toursResolved.AllToursLink);
                    var mobileOutput = RenderMobileAllToursProbe(template, context, mobileResolved);

                    var hasDbValue = !string.IsNullOrWhiteSpace(dbAllToursLink);
                    var isCrossSurfaceMatch =
                        string.Equals(messagesOutput, toursOutput, StringComparison.Ordinal) &&
                        string.Equals(messagesOutput, mobileOutput, StringComparison.Ordinal);

                    var matchesDbValue = hasDbValue &&
                        string.Equals(messagesOutput, dbOutput, StringComparison.Ordinal) &&
                        string.Equals(toursOutput, dbOutput, StringComparison.Ordinal) &&
                        string.Equals(mobileOutput, dbOutput, StringComparison.Ordinal);

                    var status = "Pass";
                    var notes = "OK";
                    if (!hasDbValue)
                    {
                        status = "Gap";
                        notes = "Vendor record has empty AllToursLink.";
                    }
                    else if (!isCrossSurfaceMatch)
                    {
                        status = "Mismatch";
                        notes = "Messages/Tours/Mobile outputs differ.";
                    }
                    else if (!matchesDbValue)
                    {
                        status = "Mismatch";
                        notes = "Cross-surface value does not match vendor DB value.";
                    }

                    rows.Add(new AllToursLinkParityQaRow
                    {
                        VendorName = context.VendorName,
                        VendorKey = context.VendorKey,
                        VendorId = matchedVendor?.Id,
                        WalkerName = context.WalkerName,
                        BookingCode = context.BookingCode,
                        MessageId = context.MessageId,
                        TourName = context.TourName,
                        TemplateId = template.Id,
                        TemplateName = template.MessageName ?? string.Empty,
                        TemplateType = template.MessageType ?? string.Empty,
                        TemplateContainsAllToursLinkToken = templateContainsAllTours,
                        TemplateContainsTempSignatureToken = templateContainsTempSignature,
                        DbAllToursLink = dbOutput,
                        MessagesAllToursOutput = messagesOutput,
                        ToursAllToursOutput = toursOutput,
                        MobileAllToursOutput = mobileOutput,
                        HasDbValue = hasDbValue,
                        IsCrossSurfaceMatch = isCrossSurfaceMatch,
                        MatchesDbValue = matchesDbValue,
                        Status = status,
                        Notes = notes
                    });
                }
            }

            return rows
                .OrderBy(r => r.Status == "Mismatch" ? 0 : r.Status == "Gap" ? 1 : 2)
                .ThenBy(r => r.VendorName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.TemplateName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.WalkerName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<AllToursLinkParityQaGapRow> BuildGaps(
            List<DbVendor> vendors,
            List<DbVendorTour> vendorTours,
            List<WalkerContext> sampledContexts,
            string? vendorFilter)
        {
            var gaps = new List<AllToursLinkParityQaGapRow>();
            var vendorsById = vendors.ToDictionary(v => v.Id);
            var normalizedFilter = NormalizeFilter(vendorFilter);

            var activeVendorTourGroups = vendorTours
                .Where(vt => vt.IsActive)
                .GroupBy(vt => vt.VendorId);

            foreach (var group in activeVendorTourGroups)
            {
                if (!vendorsById.TryGetValue(group.Key, out var vendor))
                {
                    continue;
                }

                var vendorName = vendor.VendorName ?? string.Empty;
                var vendorKey = VendorLinkResolver.NormalizeVendorKey(vendorName);
                if (!MatchesFilter(vendorName, vendorKey, normalizedFilter))
                {
                    continue;
                }

                var allTours = TrimToEmpty(vendor.AllToursLink);
                if (!string.IsNullOrWhiteSpace(allTours))
                {
                    continue;
                }

                var masterTours = group
                    .Select(x => x.MasterTourName ?? string.Empty)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .Take(8)
                    .ToList();

                gaps.Add(new AllToursLinkParityQaGapRow
                {
                    VendorName = vendorName,
                    VendorKey = vendorKey,
                    VendorId = vendor.Id,
                    CurrentAllToursLink = ResolveLinkTokenDisplayValue(allTours, "allToursLink"),
                    Source = "ActiveVendorTours",
                    ActiveVendorTourCount = group.Count(),
                    SampledWalkerCount = sampledContexts.Count(x => string.Equals(x.VendorKey, vendorKey, StringComparison.OrdinalIgnoreCase)),
                    MasterTours = string.Join(", ", masterTours)
                });
            }

            var knownGapKeys = new HashSet<string>(gaps.Select(g => g.VendorKey), StringComparer.OrdinalIgnoreCase);
            foreach (var contextGroup in sampledContexts.GroupBy(x => x.VendorKey))
            {
                var vendorKey = contextGroup.Key;
                if (string.IsNullOrWhiteSpace(vendorKey) || knownGapKeys.Contains(vendorKey))
                {
                    continue;
                }

                var match = vendors.FirstOrDefault(v => string.Equals(VendorLinkResolver.NormalizeVendorKey(v.VendorName), vendorKey, StringComparison.OrdinalIgnoreCase));
                var allTours = TrimToEmpty(match?.AllToursLink);
                if (!string.IsNullOrWhiteSpace(allTours))
                {
                    continue;
                }

                var vendorName = contextGroup.Select(x => x.VendorName).FirstOrDefault() ?? vendorKey;
                if (!MatchesFilter(vendorName, vendorKey, normalizedFilter))
                {
                    continue;
                }

                gaps.Add(new AllToursLinkParityQaGapRow
                {
                    VendorName = vendorName,
                    VendorKey = vendorKey,
                    VendorId = match?.Id,
                    CurrentAllToursLink = ResolveLinkTokenDisplayValue(allTours, "allToursLink"),
                    Source = "SampledWalkers",
                    ActiveVendorTourCount = 0,
                    SampledWalkerCount = contextGroup.Count(),
                    MasterTours = string.Join(", ", contextGroup.Select(x => x.TourName).Distinct(StringComparer.OrdinalIgnoreCase).Take(8))
                });
            }

            return gaps
                .OrderByDescending(g => g.ActiveVendorTourCount)
                .ThenByDescending(g => g.SampledWalkerCount)
                .ThenBy(g => g.VendorName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static AllToursLinkParityQaSummary BuildSummary(
            List<AllToursLinkParityQaRow> rows,
            List<AllToursLinkParityQaGapRow> gaps)
        {
            return new AllToursLinkParityQaSummary
            {
                TotalRows = rows.Count,
                RowsWithDbValue = rows.Count(r => r.HasDbValue),
                PassCount = rows.Count(r => string.Equals(r.Status, "Pass", StringComparison.Ordinal)),
                MismatchCount = rows.Count(r => string.Equals(r.Status, "Mismatch", StringComparison.Ordinal)),
                GapCount = rows.Count(r => string.Equals(r.Status, "Gap", StringComparison.Ordinal)),
                UniqueVendorsTested = rows.Select(r => r.VendorKey).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                UniqueTemplatesTested = rows.Select(r => r.TemplateId).Distinct().Count(),
                UniqueWalkersSampled = rows.Select(r => $"{r.MessageId}|{r.BookingCode}|{r.WalkerName}").Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                MissingAllToursLinkVendorCount = gaps.Select(g => g.VendorKey).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            };
        }

        private List<WalkerContext> ExtractWalkerContexts(ShapedTreeData? tree, List<DbTour> tours, string? vendorFilter)
        {
            var normalizedFilter = NormalizeFilter(vendorFilter);
            var contexts = new List<WalkerContext>();

            if (tree?.TreeNodes == null || tree.TreeNodes.Count == 0)
            {
                return contexts;
            }

            foreach (var dateNode in tree.TreeNodes)
            {
                foreach (var tourNode in dateNode.Children ?? new List<ShapedTreeNode>())
                {
                    foreach (var vendorNode in tourNode.Children ?? new List<ShapedTreeNode>())
                    {
                        foreach (var walkerNode in vendorNode.Children ?? new List<ShapedTreeNode>())
                        {
                            var walker = walkerNode.WalkerData;
                            if (walker == null)
                            {
                                continue;
                            }

                            var vendorName = FirstNonEmpty(walker.VendorName, vendorNode.Label);
                            var vendorKey = VendorLinkResolver.NormalizeVendorKey(vendorName);
                            if (string.IsNullOrWhiteSpace(vendorName) || string.IsNullOrWhiteSpace(vendorKey))
                            {
                                continue;
                            }

                            if (!MatchesFilter(vendorName, vendorKey, normalizedFilter))
                            {
                                continue;
                            }

                            var tourName = FirstNonEmpty(walker.TourName, tourNode.Label);
                            var matchedTour = ResolveTourByName(tourName, tours);

                            contexts.Add(new WalkerContext
                            {
                                VendorName = vendorName,
                                VendorKey = vendorKey,
                                WalkerName = FirstNonEmpty(walker.DisplayName, walkerNode.Label),
                                BookingCode = walker.BookingCode ?? string.Empty,
                                MessageId = walker.MessageId ?? string.Empty,
                                TourName = tourName,
                                TourId = matchedTour?.Id,
                                LegacyTourVendorLink = matchedTour?.VendorLink,
                                LegacyReviewLink = matchedTour?.ReviewLink,
                                TourDateSort = walker.BookingDate ?? TryParseDate(walker.TourDate) ?? TryParseDate(dateNode.Label),
                                Walker = walker
                            });
                        }
                    }
                }
            }

            return contexts;
        }

        private static List<WalkerContext> SampleWalkerContexts(
            List<WalkerContext> contexts,
            int maxVendors,
            int maxWalkersPerVendor)
        {
            var sampled = contexts
                .GroupBy(c => c.VendorKey)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Take(maxVendors)
                .SelectMany(g => g
                    .OrderByDescending(c => c.TourDateSort ?? DateTime.MinValue)
                    .ThenBy(c => c.WalkerName, StringComparer.OrdinalIgnoreCase)
                    .Take(maxWalkersPerVendor))
                .ToList();

            return sampled;
        }

        private static List<DbTourMessage> SelectTemplates(List<DbTourMessage> messages, int maxTemplates)
        {
            var active = messages
                .Where(m => m.IsActive)
                .ToList();

            var withAllToursSemantics = active
                .Where(m => ContainsToken(m.MessageContent, "{allToursLink}") || ContainsToken(m.MessageContent, "{tempSignature}"))
                .ToList();

            var selected = (withAllToursSemantics.Count > 0 ? withAllToursSemantics : active)
                .OrderBy(m => GetTemplatePriority(m))
                .ThenBy(m => m.MessageName, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, maxTemplates))
                .ToList();

            if (selected.Count == 0)
            {
                selected.Add(new DbTourMessage
                {
                    Id = -1,
                    MessageName = "AllToursLink Probe",
                    MessageType = "date",
                    MessageContent = "{allToursLink}",
                    IsActive = true
                });
            }

            return selected;
        }

        private static int GetTemplatePriority(DbTourMessage message)
        {
            var name = (message.MessageName ?? string.Empty).Trim().ToLowerInvariant();
            if (name == "today") return 0;
            if (name == "tomorrow") return 1;
            if (name == "date") return 2;
            if (name == "thankyou" || name == "thank you") return 3;
            return 4;
        }

        private string RenderMobileAllToursProbe(DbTourMessage template, WalkerContext context, VendorLinkResolution resolved)
        {
            var probeTemplate = new InMemoryMessageTemplate
            {
                Name = template.MessageName ?? string.Empty,
                Type = template.MessageType ?? string.Empty,
                Content = "{allToursLink}",
                VendorLink = template.VendorLink,
                Signature = template.Signature,
                TourName = context.TourName,
                TourStartTime = context.Walker?.TourStartTime,
                MeetingPlace = context.Walker?.MeetingPlace,
                MeetingLocation = context.Walker?.MeetingPlace,
                MeetingTime = context.Walker?.MeetingTime,
                MeetingInstructions = context.Walker?.MeetingInstructions
            };

            var mobileWalker = new MobileWalkerInfoModel
            {
                DisplayName = context.WalkerName,
                Phone = context.Walker?.DisplayPhone ?? string.Empty,
                VendorName = context.VendorName,
                TourName = context.TourName,
                DateLabel = context.Walker?.DisplayDate ?? context.Walker?.TourDate ?? string.Empty,
                MessageId = context.MessageId,
                BookingCode = context.BookingCode,
                TourDayOfWeek = context.Walker?.TourDayOfWeek,
                TourMonthAndDayOrdinal = context.Walker?.TourMonthAndDayOrdinal,
                MonthOfTour = context.Walker?.MonthOfTour,
                DisplayDayOrdinal = context.Walker?.DisplayDayOrdinal,
                DisplayDate = context.Walker?.DisplayDate,
                DisplayTime = context.Walker?.DisplayTime,
                TourStartTime = context.Walker?.TourStartTime,
                MeetingTime = context.Walker?.MeetingTime,
                MeetingPlace = context.Walker?.MeetingPlace,
                MeetingInstructions = context.Walker?.MeetingInstructions,
                TourLocation = context.Walker?.TourLocation
            };

            var output = _messageTemplateService.PopulateMessageTemplate(
                probeTemplate,
                mobileWalker,
                guide: null,
                allToursLink: resolved.AllToursLink,
                vendorLinks: resolved,
                galleryLink: null);

            return (output ?? string.Empty).Trim();
        }

        private static string RenderAllToursProbe(string? allToursLink)
        {
            return "{allToursLink}"
                .Replace("{allToursLink}", ResolveLinkTokenDisplayValue(allToursLink, "allToursLink"), StringComparison.Ordinal)
                .Trim();
        }

        private static string ResolveLinkTokenDisplayValue(string? value, string tokenName)
        {
            return string.IsNullOrWhiteSpace(value)
                ? $"[missing {tokenName}]"
                : value.Trim();
        }

        private static DbTour? ResolveTourByName(string? tourName, List<DbTour> tours)
        {
            var normalizedInput = NormalizeTourLookupKey(tourName);
            if (string.IsNullOrWhiteSpace(normalizedInput))
            {
                return null;
            }

            return tours
                .Where(t => string.Equals(NormalizeTourLookupKey(FirstNonEmpty(t.MasterTourName, t.TourName)), normalizedInput, StringComparison.OrdinalIgnoreCase)
                         || string.Equals(NormalizeTourLookupKey(t.TourName), normalizedInput, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(t => t.IsActive)
                .ThenByDescending(t => t.UpdatedAt ?? t.CreatedAt ?? DateTime.MinValue)
                .ThenByDescending(t => t.Id)
                .FirstOrDefault();
        }

        private static string NormalizeTourLookupKey(string? value)
        {
            return VendorLinkResolver.NormalizeMasterTourLookupKey(value);
        }

        private static bool ContainsToken(string? content, string token)
        {
            return !string.IsNullOrWhiteSpace(content) &&
                content.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }

        private static DateTime? TryParseDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return DateTime.TryParse(value, out var parsed)
                ? parsed
                : null;
        }

        private static string NormalizeFilter(string? filter)
        {
            return (filter ?? string.Empty).Trim();
        }

        private static bool MatchesFilter(string vendorName, string vendorKey, string normalizedFilter)
        {
            if (string.IsNullOrWhiteSpace(normalizedFilter))
            {
                return true;
            }

            var filterKey = VendorLinkResolver.NormalizeVendorKey(normalizedFilter);
            return vendorName.Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(filterKey) && string.Equals(vendorKey, filterKey, StringComparison.OrdinalIgnoreCase));
        }

        private static string TrimToEmpty(string? value)
        {
            return value?.Trim() ?? string.Empty;
        }

        private static AllToursLinkParityQaQuery NormalizeQuery(AllToursLinkParityQaQuery? query)
        {
            var q = query ?? new AllToursLinkParityQaQuery();
            var start = (q.DateFrom ?? DateTime.Today).Date;
            var end = (q.DateTo ?? start.AddDays(2)).Date;
            if (end < start)
            {
                (start, end) = (end, start);
            }

            return new AllToursLinkParityQaQuery
            {
                DateFrom = start,
                DateTo = end,
                Vendor = string.IsNullOrWhiteSpace(q.Vendor) ? null : q.Vendor!.Trim(),
                MaxVendors = Math.Clamp(q.MaxVendors, 1, 50),
                MaxWalkersPerVendor = Math.Clamp(q.MaxWalkersPerVendor, 1, 20),
                MaxTemplates = Math.Clamp(q.MaxTemplates, 1, 20)
            };
        }

        private sealed class WalkerContext
        {
            public string VendorName { get; set; } = string.Empty;
            public string VendorKey { get; set; } = string.Empty;
            public string WalkerName { get; set; } = string.Empty;
            public string BookingCode { get; set; } = string.Empty;
            public string MessageId { get; set; } = string.Empty;
            public string TourName { get; set; } = string.Empty;
            public int? TourId { get; set; }
            public string? LegacyTourVendorLink { get; set; }
            public string? LegacyReviewLink { get; set; }
            public DateTime? TourDateSort { get; set; }
            public ShapedWalkerData? Walker { get; set; }
        }
    }
}
