using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Email.Models;
using Email.Models.Mobile;
using Email.TourTreeViewShapedData.Enums;
using Email.TourTreeViewShapedData.Models;

namespace Email.Services
{
    /// <summary>
    /// Created: 12/19/2025 5:12 PM (local) | 2025-12-19T17:12:00
    /// Purpose: Generate vCards for manual export (no API call; runs in Blazor page context).
    /// Dependencies: ITourTreeService, VCardExportService
    /// </summary>
    public sealed class VCardManualExportService
    {
        private readonly ITourTreeService _tourTreeService;
        private readonly VCardExportService _vcardService;

        public VCardManualExportService(ITourTreeService tourTreeService, VCardExportService vcardService)
        {
            _tourTreeService = tourTreeService;
            _vcardService = vcardService;
        }

        /// <summary>
        /// Created: 12/19/2025 5:12 PM (local) | 2025-12-19T17:12:00
        /// Generate vCards for a specific day (Date → all tours/vendors/walkers).
        /// </summary>
        public async Task<List<VCardDisplayItem>> GenerateVCardsForDay(DateTime date, TreeDataFilterType? filterType = null, string? vendor = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            var ft = filterType ?? TreeDataFilterType.ActiveBookings;
            var day = date.Date;

            // Match existing API behavior: endDate is exclusive (day + 1).
            var shaped = await _tourTreeService.GetTourTreeDataShapedAsync(ft, day, day.AddDays(1), string.IsNullOrWhiteSpace(vendor) ? null : vendor);

            var dateNodeLabel = FindDateNodeLabel(shaped, day);
            var walkers = EnumerateAllWalkers(shaped).ToList();

            var items = new List<VCardDisplayItem>(walkers.Count);
            foreach (var w in walkers)
            {
                var vcard = _vcardService.GenerateVCard(w, dateLabelOverride: dateNodeLabel);
                items.Add(new VCardDisplayItem
                {
                    DisplayName = w.DisplayName ?? string.Empty,
                    Phone = w.DisplayPhone ?? string.Empty,
                    TourName = w.TourName ?? string.Empty,
                    DateLabel = dateNodeLabel ?? string.Empty,
                    VCardContent = vcard ?? string.Empty,
                    MessageId = w.MessageId ?? string.Empty
                });
            }

            return items;
        }

        /// <summary>
        /// Created: 12/19/2025 5:12 PM (local) | 2025-12-19T17:12:00
        /// Generate vCards for a specific tour label on a specific day.
        /// </summary>
        public async Task<List<VCardDisplayItem>> GenerateVCardsForTour(DateTime date, string tourLabel, TreeDataFilterType? filterType = null, string? vendor = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(tourLabel))
            {
                return new List<VCardDisplayItem>();
            }

            var ft = filterType ?? TreeDataFilterType.ActiveBookings;
            var day = date.Date;

            // Match existing API behavior: endDate is exclusive (day + 1).
            var shaped = await _tourTreeService.GetTourTreeDataShapedAsync(ft, day, day.AddDays(1), string.IsNullOrWhiteSpace(vendor) ? null : vendor);

            var tourNode = FindTourNode(shaped, tourLabel);
            if (tourNode == null)
            {
                return new List<VCardDisplayItem>();
            }

            var dateNodeLabel = FindDateNodeLabel(shaped, day);
            var walkers = EnumerateWalkersUnderTour(tourNode).ToList();

            var items = new List<VCardDisplayItem>(walkers.Count);
            foreach (var w in walkers)
            {
                var vcard = _vcardService.GenerateVCard(w, dateLabelOverride: dateNodeLabel);
                items.Add(new VCardDisplayItem
                {
                    DisplayName = w.DisplayName ?? string.Empty,
                    Phone = w.DisplayPhone ?? string.Empty,
                    TourName = w.TourName ?? string.Empty,
                    DateLabel = dateNodeLabel ?? string.Empty,
                    VCardContent = vcard ?? string.Empty,
                    MessageId = w.MessageId ?? string.Empty
                });
            }

            return items;
        }

        /// <summary>
        /// Created: 12/19/2025 5:12 PM (local) | 2025-12-19T17:12:00
        /// Generate a vCard for a single walker by MessageId (uses ProcessedEmail lookup to avoid wide scans).
        /// </summary>
        public async Task<VCardDisplayItem> GenerateVCardsForWalker(string messageId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(messageId))
            {
                throw new ArgumentException("messageId is required.", nameof(messageId));
            }

            var pe = await _tourTreeService.GetProcessedEmailByMessageIdAsync(messageId);
            if (pe == null)
            {
                throw new InvalidOperationException($"No processed email found for MessageId: {messageId}");
            }

            var walker = MapProcessedEmailToMobileWalker(pe);
            var vcard = _vcardService.GenerateVCard(walker);

            return new VCardDisplayItem
            {
                DisplayName = walker.DisplayName ?? string.Empty,
                Phone = walker.Phone ?? string.Empty,
                TourName = walker.TourName ?? string.Empty,
                DateLabel = walker.DateLabel ?? string.Empty,
                VCardContent = vcard ?? string.Empty,
                MessageId = walker.MessageId ?? string.Empty
            };
        }

        /// <summary>
        /// Created: 12/19/2025 5:12 PM (local) | 2025-12-19T17:12:00
        /// Format vCard text for display/copy (normalizes line endings to CRLF and trims trailing newlines).
        /// </summary>
        public string GetFormattedVCardText(VCardDisplayItem item)
        {
            var raw = item?.VCardContent ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            // Normalize to CRLF regardless of how the string was created.
            var normalized = raw.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
            return normalized.TrimEnd('\r', '\n');
        }

        private static IEnumerable<ShapedWalkerData> EnumerateAllWalkers(ShapedTreeData? shaped)
        {
            if (shaped?.TreeNodes == null) yield break;
            foreach (var dateNode in shaped.TreeNodes)
            {
                if (dateNode?.Children == null) continue;
                foreach (var tourNode in dateNode.Children)
                {
                    if (tourNode?.Children == null) continue;
                    foreach (var vendorNode in tourNode.Children)
                    {
                        if (vendorNode?.Children == null) continue;
                        foreach (var walkerNode in vendorNode.Children)
                        {
                            var w = walkerNode?.WalkerData;
                            if (w != null) yield return w;
                        }
                    }
                }
            }
        }

        private static ShapedTreeNode? FindTourNode(ShapedTreeData? shaped, string tourLabel)
        {
            if (shaped?.TreeNodes == null) return null;
            foreach (var dateNode in shaped.TreeNodes)
            {
                if (dateNode?.Children == null) continue;
                foreach (var tourNode in dateNode.Children)
                {
                    if (tourNode == null) continue;
                    if (string.Equals(tourNode.Label ?? string.Empty, tourLabel, StringComparison.OrdinalIgnoreCase))
                    {
                        return tourNode;
                    }
                }
            }
            return null;
        }

        private static IEnumerable<ShapedWalkerData> EnumerateWalkersUnderTour(ShapedTreeNode tourNode)
        {
            if (tourNode?.Children == null) yield break;
            foreach (var vendorNode in tourNode.Children)
            {
                if (vendorNode?.Children == null) continue;
                foreach (var walkerNode in vendorNode.Children)
                {
                    var w = walkerNode?.WalkerData;
                    if (w != null) yield return w;
                }
            }
        }

        private static MobileWalkerInfoModel MapProcessedEmailToMobileWalker(ProcessedEmail pe)
        {
            // Use DisplayDate as-is when present (matches Mobile format), else format TourDate.
            var dateLabel = pe.DisplayDate;
            if (string.IsNullOrWhiteSpace(dateLabel) && pe.TourDate.HasValue)
            {
                dateLabel = pe.TourDate.Value.ToString("dddd, MMM d, yyyy", CultureInfo.InvariantCulture);
            }

            return new MobileWalkerInfoModel
            {
                DisplayName = pe.CustomerName ?? string.Empty,
                Phone = pe.CustomerPhone ?? string.Empty,
                Attendees = pe.NumberOfAttendees.GetValueOrDefault(0),
                VendorName = pe.VendorName ?? string.Empty,
                TourName = pe.TourName ?? string.Empty,
                DateLabel = dateLabel ?? string.Empty,
                MessageId = pe.MessageId ?? string.Empty,
                BookingCode = pe.BookingCode ?? string.Empty,
                TourDayOfWeek = pe.TourDayOfWeek,
                DisplayDate = pe.DisplayDate,
                DisplayTime = pe.DisplayTime,
                TourLocation = pe.TourLocation
            };
        }

        private static string? FindDateNodeLabel(ShapedTreeData? shaped, DateTime day)
        {
            if (shaped?.TreeNodes == null) return null;
            foreach (var dateNode in shaped.TreeNodes)
            {
                if (dateNode == null) continue;
                if (DateTime.TryParse(dateNode.Label, out var parsedDate) && parsedDate.Date == day.Date)
                {
                    return dateNode.Label;
                }
            }
            return day.ToString("dddd, MMM d, yyyy", CultureInfo.InvariantCulture);
        }
    }
}


