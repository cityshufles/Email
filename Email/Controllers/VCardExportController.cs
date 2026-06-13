using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Email.Models;
using Email.Models.Mobile;
using Email.Models.Reports;
using Email.Services;
using Email.TourTreeViewShapedData.Enums;
using Email.TourTreeViewShapedData.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Email.Controllers
{
    /// <summary>
    /// Created: 2025-12-19 00:00 UTC
    /// Purpose: Serve vCard (.vcf) downloads with explicit HTTP headers for iPhone/iPad reliability.
    /// Notes:
    /// - iOS is sensitive to MIME type and newline formatting. We return Content-Type=text/vcard and attachment filename.
    /// - vCard content is generated via Email.Services.VCardExportService (CRLF, UID, escaping).
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("vcards")]
    public sealed class VCardExportController : ControllerBase
    {
        private readonly ITourTreeService _tourTreeService;
        private readonly VCardExportService _vcardService;
        private readonly VCardLogService _logService;
        private readonly IBookingsInboxService _bookingsService;

        public VCardExportController(ITourTreeService tourTreeService, VCardExportService vcardService, VCardLogService logService, IBookingsInboxService bookingsService)
        {
            _tourTreeService = tourTreeService;
            _vcardService = vcardService;
            _logService = logService;
            _bookingsService = bookingsService;
        }

        // 2026-06-04 - Booking-based vCard: works for ANY booking (no ProcessedEmail dependency).
        [HttpGet("booking")]
        public async Task<IActionResult> DownloadBooking([FromQuery] int bookingId, CancellationToken ct)
        {
            if (bookingId <= 0) return BadRequest(new { error = "bookingId is required." });
            var bookings = await _bookingsService.GetBookingsByIdsAsync(new[] { bookingId }, ct);
            var b = bookings.FirstOrDefault();
            if (b == null) return NotFound(new { error = "Booking not found." });
            var vcf = _vcardService.GenerateVcf(new[] { _vcardService.GenerateVCard(MapBookingToWalker(b)) });
            var bytes = _vcardService.GetUtf8Bytes(vcf, includeBom: true);
            SetNoStore();
            return File(bytes, "text/vcard", $"contact_{SanitizeFileStem(b.CustomerName)}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.vcf");
        }

        [HttpGet("bookings")]
        public async Task<IActionResult> DownloadBookings([FromQuery] string bookingIds, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(bookingIds)) return BadRequest(new { error = "bookingIds is required." });
            var ids = bookingIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => int.TryParse(x, out var n) ? n : 0)
                .Where(n => n > 0).Distinct().ToList();
            if (ids.Count == 0) return BadRequest(new { error = "No valid booking ids." });
            var bookings = await _bookingsService.GetBookingsByIdsAsync(ids, ct);
            if (bookings.Count == 0) return NotFound(new { error = "No bookings found." });
            var vcards = bookings.Select(b => _vcardService.GenerateVCard(MapBookingToWalker(b))).ToList();
            var bytes = _vcardService.GetUtf8Bytes(_vcardService.GenerateVcf(vcards), includeBom: true);
            SetNoStore();
            return File(bytes, "text/vcard", $"contacts_{vcards.Count}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.vcf");
        }

        private static MobileWalkerInfoModel MapBookingToWalker(BookingsLiveListItem b) => new()
        {
            DisplayName = b.CustomerName,
            Phone = b.CustomerPhone ?? string.Empty,
            Attendees = b.NumberOfAttendees ?? 0,
            VendorName = b.VendorName,
            TourName = b.TourName,
            DateLabel = b.TourDate?.ToString("MMM d, yyyy", CultureInfo.InvariantCulture) ?? string.Empty,
            BookingCode = b.BookingCode,
            MessageId = b.MessageId,
            CustomerId = b.CustomerId
        };

        /// <summary>
        /// Created: 2025-12-19 00:00 UTC
        /// GET /vcards/day?date=yyyy-MM-dd&filterType=ActiveBookings&vendor=
        /// </summary>
        [HttpGet("day")]
        public async Task<IActionResult> DownloadDay([FromQuery] string date, [FromQuery] string? filterType, [FromQuery] string? vendor, CancellationToken ct)
        {
            var requestUrl = $"{Request.Scheme}://{Request.Host}{Request.Path}{Request.QueryString}";
            _logService.LogAction("API Endpoint Called", $"DownloadDay endpoint invoked. Date: {date}, FilterType: {filterType}, Vendor: {vendor ?? "(none)"}", requestUrl);

            try
            {
                if (!TryParseDate(date, out var day))
                {
                    _logService.LogAction("API Validation Error", $"Invalid date format: {date}. Expected yyyy-MM-dd.", requestUrl);
                    return BadRequest("Invalid date. Expected yyyy-MM-dd.");
                }

                var ft = ParseFilterTypeOrDefault(filterType);
                _logService.LogAction("API Processing Started", $"Fetching tour tree data. Date: {day:yyyy-MM-dd}, FilterType: {ft}, Vendor: {vendor ?? "(none)"}", requestUrl);

                ShapedTreeData? shaped;
                try
                {
                    shaped = await _tourTreeService.GetTourTreeDataShapedAsync(ft, day.Date, day.Date.AddDays(1), string.IsNullOrWhiteSpace(vendor) ? null : vendor);
                }
                catch (Exception ex)
                {
                    _logService.LogAction("API Database Error", $"Failed to fetch tour tree data. Date: {day:yyyy-MM-dd}, FilterType: {ft}", requestUrl, ex);
                    throw;
                }

                var walkers = EnumerateAllWalkers(shaped).ToList();
                _logService.LogAction("API Data Retrieved", $"Found {walkers.Count} walkers for date {day:yyyy-MM-dd}", requestUrl);

                if (walkers.Count == 0)
                {
                    _logService.LogAction("API No Data", $"No walkers found for date {day:yyyy-MM-dd}", requestUrl);
                    return NotFound("No walkers found for that day.");
                }

                // Option B: Use DateLabel as-is from tree structure (matches Mobile format "Friday, Jun 27, 2025")
                // Find the date node label from the tree to match Mobile's DateLabel format
                var dateNodeLabel = FindDateNodeLabel(shaped, day);
                _logService.LogAction("API vCard Generation Started", $"Generating {walkers.Count} vCards with date label: {dateNodeLabel ?? "(none)"}", requestUrl);

                List<string> vcards;
                string vcf;
                byte[] bytes;
                try
                {
                    vcards = walkers.Select(w => _vcardService.GenerateVCard(w, dateLabelOverride: dateNodeLabel)).ToList();
                    vcf = _vcardService.GenerateVcf(vcards);
                    bytes = _vcardService.GetUtf8Bytes(vcf, includeBom: true);
                    _logService.LogAction("API vCard Generation Complete", $"Generated {vcards.Count} vCards, total size: {bytes.Length} bytes", requestUrl);
                }
                catch (Exception ex)
                {
                    _logService.LogAction("API vCard Generation Error", $"Failed to generate vCards for {walkers.Count} walkers", requestUrl, ex);
                    throw;
                }

                SetNoStore();

                var fileName = $"walkers_{day:yyyyMMdd}.vcf";
                _logService.LogAction("API Download Success", $"Returning file: {fileName}, Size: {bytes.Length} bytes, Content-Type: text/vcard", requestUrl);
                return File(bytes, "text/vcard", fileName);
            }
            catch (Exception ex)
            {
                _logService.LogAction("API Endpoint Exception", $"Unhandled exception in DownloadDay endpoint", requestUrl, ex);
                throw;
            }
        }

        /// <summary>
        /// Created: 2025-12-19 00:00 UTC
        /// GET /vcards/tour?date=yyyy-MM-dd&tourLabel=...&filterType=ActiveBookings&vendor=
        /// </summary>
        [HttpGet("tour")]
        public async Task<IActionResult> DownloadTour([FromQuery] string date, [FromQuery] string tourLabel, [FromQuery] string? filterType, [FromQuery] string? vendor, CancellationToken ct)
        {
            var requestUrl = $"{Request.Scheme}://{Request.Host}{Request.Path}{Request.QueryString}";
            _logService.LogAction("API Endpoint Called", $"DownloadTour endpoint invoked. Date: {date}, TourLabel: {tourLabel}, FilterType: {filterType}, Vendor: {vendor ?? "(none)"}", requestUrl);

            try
            {
                if (!TryParseDate(date, out var day))
                {
                    _logService.LogAction("API Validation Error", $"Invalid date format: {date}. Expected yyyy-MM-dd.", requestUrl);
                    return BadRequest("Invalid date. Expected yyyy-MM-dd.");
                }

                if (string.IsNullOrWhiteSpace(tourLabel))
                {
                    _logService.LogAction("API Validation Error", "tourLabel is required but was empty", requestUrl);
                    return BadRequest("tourLabel is required.");
                }

                var ft = ParseFilterTypeOrDefault(filterType);
                _logService.LogAction("API Processing Started", $"Fetching tour tree data. Date: {day:yyyy-MM-dd}, TourLabel: {tourLabel}, FilterType: {ft}, Vendor: {vendor ?? "(none)"}", requestUrl);

                ShapedTreeData? shaped;
                try
                {
                    shaped = await _tourTreeService.GetTourTreeDataShapedAsync(ft, day.Date, day.Date.AddDays(1), string.IsNullOrWhiteSpace(vendor) ? null : vendor);
                }
                catch (Exception ex)
                {
                    _logService.LogAction("API Database Error", $"Failed to fetch tour tree data. Date: {day:yyyy-MM-dd}, TourLabel: {tourLabel}", requestUrl, ex);
                    throw;
                }

                var tourNode = FindTourNode(shaped, tourLabel);
                if (tourNode == null)
                {
                    _logService.LogAction("API Tour Not Found", $"Tour '{tourLabel}' not found for date {day:yyyy-MM-dd}", requestUrl);
                    return NotFound("Tour not found for that day.");
                }

                var walkers = EnumerateWalkersUnderTour(tourNode).ToList();
                _logService.LogAction("API Data Retrieved", $"Found {walkers.Count} walkers for tour '{tourLabel}' on date {day:yyyy-MM-dd}", requestUrl);

                if (walkers.Count == 0)
                {
                    _logService.LogAction("API No Data", $"No walkers found for tour '{tourLabel}' on date {day:yyyy-MM-dd}", requestUrl);
                    return NotFound("No walkers found for that tour.");
                }

                // Option B: Use DateLabel as-is from tree structure (matches Mobile format "Friday, Jun 27, 2025")
                // Find the date node label from the tree to match Mobile's DateLabel format
                var dateNodeLabel = FindDateNodeLabel(shaped, day);
                _logService.LogAction("API vCard Generation Started", $"Generating {walkers.Count} vCards for tour '{tourLabel}' with date label: {dateNodeLabel ?? "(none)"}", requestUrl);

                List<string> vcards;
                string vcf;
                byte[] bytes;
                try
                {
                    vcards = walkers.Select(w => _vcardService.GenerateVCard(w, dateLabelOverride: dateNodeLabel)).ToList();
                    vcf = _vcardService.GenerateVcf(vcards);
                    bytes = _vcardService.GetUtf8Bytes(vcf, includeBom: true);
                    _logService.LogAction("API vCard Generation Complete", $"Generated {vcards.Count} vCards for tour '{tourLabel}', total size: {bytes.Length} bytes", requestUrl);
                }
                catch (Exception ex)
                {
                    _logService.LogAction("API vCard Generation Error", $"Failed to generate vCards for {walkers.Count} walkers in tour '{tourLabel}'", requestUrl, ex);
                    throw;
                }

                SetNoStore();

                var safeTour = SanitizeFileStem(tourLabel);
                var fileName = $"walkers_{safeTour}_{day:yyyyMMdd}.vcf";
                _logService.LogAction("API Download Success", $"Returning file: {fileName}, Size: {bytes.Length} bytes, Content-Type: text/vcard", requestUrl);
                return File(bytes, "text/vcard", fileName);
            }
            catch (Exception ex)
            {
                _logService.LogAction("API Endpoint Exception", $"Unhandled exception in DownloadTour endpoint", requestUrl, ex);
                throw;
            }
        }

        /// <summary>
        /// Created: 2025-12-19 00:00 UTC
        /// GET /vcards/walker?messageId=...
        /// </summary>
        [HttpGet("walker")]
        public async Task<IActionResult> DownloadWalker([FromQuery] string messageId, CancellationToken ct)
        {
            var requestUrl = $"{Request.Scheme}://{Request.Host}{Request.Path}{Request.QueryString}";
            _logService.LogAction("API Endpoint Called", $"DownloadWalker endpoint invoked. MessageId: {messageId}", requestUrl);

            try
            {
                if (string.IsNullOrWhiteSpace(messageId))
                {
                    _logService.LogAction("API Validation Error", "messageId is required but was empty", requestUrl);
                    return BadRequest("messageId is required.");
                }

                _logService.LogAction("API Processing Started", $"Fetching processed email by MessageId: {messageId}", requestUrl);

                // Use existing service method to avoid wide scans.
                ProcessedEmail? pe;
                try
                {
                    pe = await _tourTreeService.GetProcessedEmailByMessageIdAsync(messageId);
                }
                catch (Exception ex)
                {
                    _logService.LogAction("API Database Error", $"Failed to fetch processed email by MessageId: {messageId}", requestUrl, ex);
                    throw;
                }

                if (pe == null)
                {
                    _logService.LogAction("API Walker Not Found", $"No processed email found for MessageId: {messageId}", requestUrl);
                    return NotFound("Walker not found.");
                }

                _logService.LogAction("API Data Retrieved", $"Found walker: {pe.CustomerName ?? "(no name)"}, BookingCode: {pe.BookingCode ?? "(none)"}", requestUrl);

                MobileWalkerInfoModel walker;
                string vcard;
                string vcf;
                byte[] bytes;
                try
                {
                    walker = MapProcessedEmailToMobileWalker(pe);
                    _logService.LogAction("API Mapping Complete", $"Mapped to MobileWalkerInfoModel. DisplayName: {walker.DisplayName}, Phone: {walker.Phone ?? "(none)"}", requestUrl);

                    vcard = _vcardService.GenerateVCard(walker);
                    vcf = _vcardService.GenerateVcf(new[] { vcard });
                    bytes = _vcardService.GetUtf8Bytes(vcf, includeBom: true);
                    _logService.LogAction("API vCard Generation Complete", $"Generated vCard for {walker.DisplayName}, size: {bytes.Length} bytes", requestUrl);
                }
                catch (Exception ex)
                {
                    _logService.LogAction("API vCard Generation Error", $"Failed to generate vCard for walker with MessageId: {messageId}", requestUrl, ex);
                    throw;
                }

                SetNoStore();

                var safeName = SanitizeFileStem(walker.DisplayName);
                var fileName = $"walker_{safeName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.vcf";
                _logService.LogAction("API Download Success", $"Returning file: {fileName}, Size: {bytes.Length} bytes, Content-Type: text/vcard", requestUrl);
                return File(bytes, "text/vcard", fileName);
            }
            catch (Exception ex)
            {
                _logService.LogAction("API Endpoint Exception", $"Unhandled exception in DownloadWalker endpoint", requestUrl, ex);
                throw;
            }
        }

        /// <summary>
        /// 2026-06-03 - GET /vcards/bulk?messageIds=a,b,c
        /// Returns one .vcf containing all requested contacts (bulk export from the Bookings page).
        /// </summary>
        [HttpGet("bulk")]
        public async Task<IActionResult> DownloadBulk([FromQuery] string messageIds, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(messageIds))
            {
                return BadRequest("messageIds is required.");
            }

            var ids = messageIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (ids.Count == 0)
            {
                return BadRequest("No message ids supplied.");
            }

            var vcards = new List<string>();
            foreach (var id in ids)
            {
                try
                {
                    var pe = await _tourTreeService.GetProcessedEmailByMessageIdAsync(id);
                    if (pe == null) continue;
                    var walker = MapProcessedEmailToMobileWalker(pe);
                    vcards.Add(_vcardService.GenerateVCard(walker));
                }
                catch
                {
                    // skip an individual bad id; continue with the rest
                }
            }

            if (vcards.Count == 0)
            {
                return NotFound("No contacts found for the selected bookings.");
            }

            var vcf = _vcardService.GenerateVcf(vcards);
            var bytes = _vcardService.GetUtf8Bytes(vcf, includeBom: true);
            SetNoStore();
            var fileName = $"contacts_{vcards.Count}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.vcf";
            return File(bytes, "text/vcard", fileName);
        }

        private static bool TryParseDate(string raw, out DateTime day)
        {
            day = default;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            return DateTime.TryParseExact(raw.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out day);
        }

        private static TreeDataFilterType ParseFilterTypeOrDefault(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return TreeDataFilterType.ActiveBookings;
            return Enum.TryParse<TreeDataFilterType>(raw, ignoreCase: true, out var ft) ? ft : TreeDataFilterType.ActiveBookings;
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
            // Option B: Use DateLabel as-is (matches Mobile format "Friday, Jun 27, 2025")
            // If DisplayDate is missing, format TourDate to match tree structure format
            var dateLabel = pe.DisplayDate;
            if (string.IsNullOrWhiteSpace(dateLabel) && pe.TourDate.HasValue)
            {
                dateLabel = pe.TourDate.Value.ToString("dddd, MMM d, yyyy");
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
                BookingCode = pe.BookingCode ?? string.Empty
            };
        }

        private void SetNoStore()
        {
            try
            {
                Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
                Response.Headers["Pragma"] = "no-cache";
                Response.Headers["Expires"] = "0";
            }
            catch
            {
                // no-op
            }
        }

        /// <summary>
        /// Created: 2025-12-19 00:00 UTC
        /// Find the date node label from the tree structure to match Mobile's DateLabel format ("Friday, Jun 27, 2025").
        /// </summary>
        private static string? FindDateNodeLabel(ShapedTreeData? shaped, DateTime day)
        {
            if (shaped?.TreeNodes == null) return null;
            foreach (var dateNode in shaped.TreeNodes)
            {
                if (dateNode == null) continue;
                // Date node labels are formatted as "dddd, MMM d, yyyy" (e.g., "Friday, Jun 27, 2025")
                // Try to match by parsing the label or checking if it contains the day
                if (DateTime.TryParse(dateNode.Label, out var parsedDate) && parsedDate.Date == day.Date)
                {
                    return dateNode.Label;
                }
            }
            // Fallback: format the date to match tree structure format
            return day.ToString("dddd, MMM d, yyyy");
        }

        private static string SanitizeFileStem(string? raw)
        {
            var s = (raw ?? string.Empty).Trim();
            if (s.Length == 0) s = "export";

            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (var ch in s)
            {
                sb.Append(invalid.Contains(ch) ? '_' : ch);
            }

            s = sb.ToString()
                .Replace(" ", "_")
                .Replace(",", "")
                .Replace("\"", "")
                .Replace("'", "");

            // Keep filenames reasonable.
            if (s.Length > 80) s = s.Substring(0, 80);
            return s;
        }
    }
}


