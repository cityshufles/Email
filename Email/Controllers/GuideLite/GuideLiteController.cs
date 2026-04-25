using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Email.Calendar.Models;
using Email.Calendar.Services;
using Email.Models;
using Email.Models.Reports;
using Email.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Email.Controllers.GuideLite;

[ApiController]
[Authorize(Roles = "Guide,Admin,Manager")]
[Route("guide-lite/api")]
public sealed class GuideLiteController : ControllerBase
{
    private readonly ICalendarDataService _calendarService;
    private readonly IGuidesApiService _guidesService;
    private readonly ITourGuideAssignmentService _assignmentService;
    private readonly IGuideReportService _reportService;
    private readonly ITourPhotoService _photoService;
    private readonly ITourNameNormalizer _tourNameNormalizer;
    private readonly StaticGalleryGeneratorService _galleryGenerator;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GuideLiteController> _logger;

    private const long MaxUploadBytes = 200_000_000;

    public GuideLiteController(
        ICalendarDataService calendarService,
        IGuidesApiService guidesService,
        ITourGuideAssignmentService assignmentService,
        IGuideReportService reportService,
        ITourPhotoService photoService,
        ITourNameNormalizer tourNameNormalizer,
        StaticGalleryGeneratorService galleryGenerator,
        IConfiguration configuration,
        ILogger<GuideLiteController> logger)
    {
        _calendarService = calendarService;
        _guidesService = guidesService;
        _assignmentService = assignmentService;
        _reportService = reportService;
        _photoService = photoService;
        _tourNameNormalizer = tourNameNormalizer;
        _galleryGenerator = galleryGenerator;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet("tours")]
    public async Task<IActionResult> GetTours(
        [FromQuery] string? date,
        [FromQuery] string? startDate,
        [FromQuery] string? endDate,
        [FromQuery] string? guideFilter)
    {
        if (!TryResolveDateRange(date, startDate, endDate, out var rangeStart, out var rangeEnd, out var rangeError))
        {
            return BadRequest(new { error = rangeError });
        }

        var safeGuideFilter = NormalizeGuideFilter(guideFilter);
        if (!TryParseGuideFilter(safeGuideFilter, out var selectedGuideId, out var onlyUnassigned, out var guideFilterError))
        {
            return BadRequest(new { error = guideFilterError });
        }

        var familyCache = new Dictionary<string, TourFamilySnapshot>(StringComparer.OrdinalIgnoreCase);
        var bookings = await _calendarService.GetBookingsAsync(rangeStart, rangeEnd);

        var assignments = new List<TourGuideAssignment>();
        for (var dateCursor = rangeStart.Date; dateCursor <= rangeEnd.Date; dateCursor = dateCursor.AddDays(1))
        {
            var dayAssignments = await _assignmentService.GetAssignmentsForDateAsync(dateCursor);
            if (dayAssignments.Count > 0)
            {
                assignments.AddRange(dayAssignments);
            }
        }

        var reports = await _reportService.GetAllReportsAsync(rangeStart, rangeEnd);

        var reportGroups = new Dictionary<(DateTime TourDate, string TimeKey, string FamilyKey), List<TourReportSummary>>();
        foreach (var report in reports)
        {
            var family = await ResolveTourFamilyAsync(report.TourName, vendorName: null, familyCache);
            var key = (
                TourDate: report.TourDate.Date,
                TimeKey: NormalizeTourTimeKey(report.TourTime),
                FamilyKey: family.FamilyKey);

            if (!reportGroups.TryGetValue(key, out var list))
            {
                list = new List<TourReportSummary>();
                reportGroups[key] = list;
            }

            list.Add(report);
        }

        var assignmentGroups = new Dictionary<(DateTime TourDate, string TimeKey, string FamilyKey), List<TourGuideAssignment>>();
        foreach (var assignment in assignments)
        {
            var family = await ResolveTourFamilyAsync(assignment.TourName, vendorName: null, familyCache);
            var key = (
                TourDate: assignment.TourDate.Date,
                TimeKey: NormalizeTourTimeKey(assignment.TourTime),
                FamilyKey: family.FamilyKey);

            if (!assignmentGroups.TryGetValue(key, out var list))
            {
                list = new List<TourGuideAssignment>();
                assignmentGroups[key] = list;
            }

            list.Add(assignment);
        }

        var assignmentLookup = assignmentGroups.ToDictionary(
            g => g.Key,
            g => g.Value
                .OrderByDescending(x => x.GuideId.HasValue)
                .ThenByDescending(x => x.UpdatedAt ?? DateTime.MinValue)
                .ThenByDescending(x => x.CreatedAt ?? DateTime.MinValue)
                .ThenByDescending(x => x.Id)
                .FirstOrDefault()?.GuideId);

        var resolvedBookings = new List<(CalendarBookingModel Booking, TourFamilySnapshot Family, DateTime TourDate, string TimeKey)>();
        foreach (var booking in bookings.Where(b => b.TourDate.HasValue))
        {
            var family = await ResolveTourFamilyAsync(booking.TourName, booking.VendorName, familyCache);
            resolvedBookings.Add((
                Booking: booking,
                Family: family,
                TourDate: booking.TourDate!.Value.Date,
                TimeKey: NormalizeTourTimeKey(booking.TourTime)));
        }

        var tours = resolvedBookings
            .GroupBy(x => (x.TourDate, x.TimeKey, x.Family.FamilyKey))
            .Select(g =>
            {
                var key = g.Key;
                reportGroups.TryGetValue(key, out var groupReports);
                groupReports ??= new List<TourReportSummary>();
                var preferredReport = SelectPreferredReportCandidate(groupReports);

                var rawTourName = !string.IsNullOrWhiteSpace(preferredReport?.TourName)
                    ? preferredReport!.TourName.Trim()
                    : g.Select(x => x.Booking.TourName?.Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault()
                        ?? "Unknown Tour";

                var rawTourTime = !string.IsNullOrWhiteSpace(preferredReport?.TourTime)
                    ? preferredReport!.TourTime.Trim()
                    : g.Select(x => x.Booking.TourTime?.Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault()
                        ?? key.TimeKey;

                var desktopFamilyName = groupReports
                        .Select(r => r.MasterTourName?.Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault()
                    ?? g.Select(x => x.Family.DesktopName?.Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault()
                    ?? rawTourName;

                var mobileFamilyName = groupReports
                        .Select(r => r.MasterTourNameMobile?.Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault()
                    ?? g.Select(x => x.Family.MobileName?.Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault()
                    ?? desktopFamilyName;

                return new GuideLiteTourDto
                {
                    TourDate = key.TourDate.ToString("yyyy-MM-dd"),
                    TourName = rawTourName,
                    TourTime = rawTourTime,
                    DisplayTime = FormatTourTimeDisplay(rawTourTime, key.TimeKey),
                    TotalGuests = g.Sum(x => x.Booking.NumberOfAttendees ?? 1),
                    GuideId = assignmentLookup.TryGetValue(key, out var guideId) ? guideId : null,
                    IsSubmitted = groupReports.Any(r => r.IsSubmitted),
                    GroupFamilyKey = key.FamilyKey,
                    GroupTimeKey = key.TimeKey,
                    ListDisplayName = mobileFamilyName
                };
            })
            .Where(tour =>
            {
                if (onlyUnassigned)
                {
                    return !tour.GuideId.HasValue;
                }

                if (selectedGuideId.HasValue)
                {
                    return tour.GuideId == selectedGuideId.Value;
                }

                return true;
            })
            .OrderBy(t => t.TourDate, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.GroupTimeKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.ListDisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var guides = (await _guidesService.GetGuidesAsync())
            .Where(g => g.Id > 0)
            .GroupBy(g =>
            {
                var first = (g.FirstName ?? string.Empty).Trim();
                var last = (g.LastName ?? string.Empty).Trim();
                return $"{first} {last}".Trim().ToLowerInvariant();
            })
            .Select(g => g.First())
            .OrderBy(g => g.FirstName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.LastName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new GuideLiteGuideOptionDto
            {
                Id = g.Id,
                DisplayName = FormatGuideDisplayName(g.FirstName, g.LastName)
            })
            .ToList();

        return Ok(new GuideLiteToursResponseDto
        {
            RangeStart = rangeStart.ToString("yyyy-MM-dd"),
            RangeEnd = rangeEnd.ToString("yyyy-MM-dd"),
            GuideFilter = safeGuideFilter,
            Guides = guides,
            Tours = tours
        });
    }

    [HttpGet("report")]
    public async Task<IActionResult> GetReport(
        [FromQuery] string tourDate,
        [FromQuery] string tourName,
        [FromQuery] string? tourTime)
    {
        if (!TryParseTourDate(tourDate, out var parsedDate))
        {
            return BadRequest(new { error = "tourDate is required in yyyy-MM-dd format." });
        }

        if (string.IsNullOrWhiteSpace(tourName))
        {
            return BadRequest(new { error = "tourName is required." });
        }

        var safeTourTime = tourTime?.Trim() ?? string.Empty;
        var report = await _reportService.GetReportAsync(parsedDate, tourName.Trim(), safeTourTime);
        var photos = await _photoService.GetPhotosForTourAsync(tourDate, tourName.Trim(), safeTourTime);

        if (photos.Count > 0 || string.IsNullOrWhiteSpace(report.ImagePaths))
        {
            report.ImagePaths = string.Join(",", photos);
        }

        var canShareLinks = report.IsSubmitted && !string.IsNullOrWhiteSpace(report.PublicId);
        var vendorGalleryLinks = report.Walkers
            .Where(w => !string.IsNullOrWhiteSpace(w.VendorName))
            .Select(w => w.VendorName!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                vendor => vendor,
                vendor => canShareLinks ? GetGalleryUrl(report, vendor) : string.Empty,
                StringComparer.OrdinalIgnoreCase);

        var walkerDtos = report.Walkers
            .Select(walker =>
            {
                var cleanedPhone = NormalizePhoneDigits(walker.Phone);
                return new GuideLiteWalkerDto
                {
                    BookingId = walker.BookingId,
                    CustomerName = walker.CustomerName,
                    Phone = walker.Phone,
                    Email = walker.Email,
                    VendorName = walker.VendorName,
                    BookedAdults = walker.BookedAdults,
                    BookedChildren = walker.BookedChildren,
                    ActualAdults = walker.ActualAdults,
                    ActualChildren = walker.ActualChildren,
                    ActualAttendees = walker.ActualAttendees,
                    IsCheckedIn = walker.IsCheckedIn,
                    DoNotContact = walker.DoNotContact,
                    ReviewStatus = walker.ReviewStatus,
                    ReviewNotes = walker.ReviewNotes,
                    IsWalkUp = string.Equals(walker.VendorName, "Walk-Up", StringComparison.OrdinalIgnoreCase),
                    GalleryUrl = canShareLinks ? GetGalleryUrl(report, walker.VendorName) : string.Empty,
                    WhatsAppLink = canShareLinks && !string.IsNullOrWhiteSpace(cleanedPhone)
                        ? GetWhatsAppLink(report, cleanedPhone)
                        : string.Empty,
                    SmsLink = canShareLinks && !string.IsNullOrWhiteSpace(walker.Phone)
                        ? GetSmsLink(report, walker.Phone ?? string.Empty)
                        : string.Empty
                };
            })
            .ToList();

        return Ok(new GuideLiteReportDto
        {
            Id = report.Id,
            TourDate = report.TourDate.ToString("yyyy-MM-dd"),
            TourName = report.TourName,
            TourTime = report.TourTime,
            DisplayTime = FormatTourTimeDisplay(report.TourTime, NormalizeTourTimeKey(report.TourTime)),
            GuideId = report.GuideId,
            GuideName = report.GuideName,
            PublicId = report.PublicId,
            GeneralNotes = report.GeneralNotes,
            ImagePaths = report.ImagePaths,
            IsSubmitted = report.IsSubmitted,
            SubmittedAt = report.SubmittedAt,
            CanShareLinks = canShareLinks,
            GalleryUrl = canShareLinks ? GetGalleryUrl(report) : string.Empty,
            VendorGalleryLinks = vendorGalleryLinks,
            Photos = photos,
            Walkers = walkerDtos
        });
    }

    [HttpPatch("bookings/{bookingId:int}/attendees")]
    public async Task<IActionResult> UpdateBookingAttendees(int bookingId, [FromBody] GuideLiteAttendeesRequest request)
    {
        if (bookingId <= 0)
        {
            return BadRequest(new { error = "Invalid booking id." });
        }

        if (request.ActualAdults < 0 || request.ActualChildren < 0)
        {
            return BadRequest(new { error = "Adults and children cannot be negative." });
        }

        await _reportService.UpdateBookingAttendeesAsync(bookingId, request.ActualAdults, request.ActualChildren);
        return Ok(new
        {
            success = true,
            bookingId,
            actualAdults = request.ActualAdults,
            actualChildren = request.ActualChildren
        });
    }

    [HttpPatch("bookings/{bookingId:int}/state")]
    public async Task<IActionResult> UpdateBookingState(int bookingId, [FromBody] GuideLiteBookingStateRequest request)
    {
        if (bookingId <= 0)
        {
            return BadRequest(new { error = "Invalid booking id." });
        }

        await _reportService.UpdateBookingGuideStateAsync(
            bookingId,
            request.ReviewStatus,
            request.ReviewNotes,
            request.IsCheckedIn,
            request.DoNotContact);

        return Ok(new
        {
            success = true,
            bookingId
        });
    }

    [HttpPatch("bookings/{bookingId:int}/phone")]
    public async Task<IActionResult> UpdateBookingPhone(int bookingId, [FromBody] GuideLitePhoneRequest request)
    {
        if (bookingId <= 0)
        {
            return BadRequest(new { error = "Invalid booking id." });
        }

        if (string.IsNullOrWhiteSpace(request.Phone))
        {
            return BadRequest(new { error = "Phone is required." });
        }

        await _reportService.UpdateBookingPhoneAsync(bookingId, request.Phone.Trim());
        return Ok(new
        {
            success = true,
            bookingId
        });
    }

    [HttpPost("walkups")]
    public async Task<IActionResult> AddWalkUp([FromBody] GuideLiteWalkUpCreateRequest request)
    {
        if (!TryParseTourDate(request.TourDate, out var parsedDate))
        {
            return BadRequest(new { error = "tourDate is required in yyyy-MM-dd format." });
        }

        if (string.IsNullOrWhiteSpace(request.TourName))
        {
            return BadRequest(new { error = "tourName is required." });
        }

        if (string.IsNullOrWhiteSpace(request.CustomerName) || string.IsNullOrWhiteSpace(request.Phone))
        {
            return BadRequest(new { error = "CustomerName and Phone are required." });
        }

        var walker = new GuideReportWalker
        {
            CustomerName = request.CustomerName.Trim(),
            Phone = request.Phone.Trim(),
            Email = request.Email?.Trim(),
            VendorName = "Walk-Up",
            ActualAdults = request.ActualAdults > 0 ? request.ActualAdults : 1,
            ActualChildren = Math.Max(0, request.ActualChildren),
            ActualAttendees = (request.ActualAdults > 0 ? request.ActualAdults : 1) + Math.Max(0, request.ActualChildren),
            ReviewNotes = request.ReviewNotes?.Trim()
        };

        var bookingId = await _reportService.AddWalkUpBookingAsync(
            walker,
            parsedDate,
            request.TourName.Trim(),
            request.TourTime?.Trim() ?? string.Empty);

        return Ok(new
        {
            success = true,
            bookingId
        });
    }

    [HttpPut("walkups/{bookingId:int}")]
    public async Task<IActionResult> UpdateWalkUp(int bookingId, [FromBody] GuideLiteWalkUpUpdateRequest request)
    {
        if (bookingId <= 0)
        {
            return BadRequest(new { error = "Invalid booking id." });
        }

        if (string.IsNullOrWhiteSpace(request.CustomerName) || string.IsNullOrWhiteSpace(request.Phone))
        {
            return BadRequest(new { error = "CustomerName and Phone are required." });
        }

        var walker = new GuideReportWalker
        {
            BookingId = bookingId,
            CustomerName = request.CustomerName.Trim(),
            Phone = request.Phone.Trim(),
            Email = request.Email?.Trim(),
            VendorName = "Walk-Up",
            ActualAdults = request.ActualAdults > 0 ? request.ActualAdults : 1,
            ActualChildren = Math.Max(0, request.ActualChildren),
            ActualAttendees = (request.ActualAdults > 0 ? request.ActualAdults : 1) + Math.Max(0, request.ActualChildren),
            ReviewNotes = request.ReviewNotes?.Trim()
        };

        await _reportService.UpdateWalkUpBookingAsync(walker);
        return Ok(new
        {
            success = true,
            bookingId
        });
    }

    [HttpDelete("walkups/{bookingId:int}")]
    public async Task<IActionResult> DeleteWalkUp(int bookingId)
    {
        if (bookingId <= 0)
        {
            return BadRequest(new { error = "Invalid booking id." });
        }

        await _reportService.DeleteWalkUpBookingAsync(bookingId);
        return Ok(new
        {
            success = true,
            bookingId
        });
    }

    [HttpPost("photos/upload-and-sync")]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<IActionResult> UploadAndSyncPhotos(
        [FromForm] List<IFormFile> files,
        [FromForm] string tourDate,
        [FromForm] string tourName,
        [FromForm] string? tourTime)
    {
        if (!TryParseTourDate(tourDate, out var parsedDate))
        {
            return BadRequest(new { error = "tourDate is required in yyyy-MM-dd format." });
        }

        if (string.IsNullOrWhiteSpace(tourName))
        {
            return BadRequest(new { error = "tourName is required." });
        }

        if (files == null || files.Count == 0)
        {
            return BadRequest(new { error = "No files uploaded." });
        }

        var safeTourTime = tourTime?.Trim() ?? string.Empty;
        var savedPaths = new List<string>();

        foreach (var file in files)
        {
            if (file.Length <= 0)
            {
                continue;
            }

            await using var stream = file.OpenReadStream();
            var extension = GetSafeExtension(file);
            var savedPath = await _photoService.SavePhotoRawAsync(stream, tourDate, tourName.Trim(), safeTourTime, extension);
            if (!string.IsNullOrWhiteSpace(savedPath))
            {
                savedPaths.Add(savedPath);
            }
        }

        if (savedPaths.Count == 0)
        {
            return StatusCode(500, new { error = "No files were saved." });
        }

        try
        {
            var photos = await _photoService.GetPhotosForTourAsync(tourDate, tourName.Trim(), safeTourTime);
            var imagePaths = string.Join(",", photos);
            var reportId = await _reportService.UpsertReportImagePathsAsync(
                parsedDate.Date,
                tourName.Trim(),
                safeTourTime,
                imagePaths);

            return Ok(new
            {
                success = true,
                syncSucceeded = true,
                savedCount = savedPaths.Count,
                photoCount = photos.Count,
                reportId,
                photos
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "GuideLite photo sync failed after upload. TourDate={TourDate} TourName={TourName} TourTime={TourTime}",
                tourDate, tourName, safeTourTime);

            return Ok(new
            {
                success = true,
                syncSucceeded = false,
                savedCount = savedPaths.Count,
                retry = new
                {
                    tourDate,
                    tourName,
                    tourTime = safeTourTime
                },
                error = "Upload succeeded, but sync failed. Try again."
            });
        }
    }

    [HttpPost("photos/sync")]
    public async Task<IActionResult> SyncPhotos([FromBody] GuideLitePhotoSyncRequest request)
    {
        if (!TryParseTourDate(request.TourDate, out var parsedDate))
        {
            return BadRequest(new { error = "tourDate is required in yyyy-MM-dd format." });
        }

        if (string.IsNullOrWhiteSpace(request.TourName))
        {
            return BadRequest(new { error = "tourName is required." });
        }

        var safeTourTime = request.TourTime?.Trim() ?? string.Empty;
        var photos = await _photoService.GetPhotosForTourAsync(request.TourDate, request.TourName.Trim(), safeTourTime);
        var imagePaths = string.Join(",", photos);
        var reportId = await _reportService.UpsertReportImagePathsAsync(
            parsedDate.Date,
            request.TourName.Trim(),
            safeTourTime,
            imagePaths);

        return Ok(new
        {
            success = true,
            syncSucceeded = true,
            reportId,
            photoCount = photos.Count,
            photos
        });
    }

    [HttpPost("reports/submit")]
    public async Task<IActionResult> SubmitReport([FromBody] GuideLiteSubmitReportRequest request)
    {
        if (!TryParseTourDate(request.TourDate, out var parsedDate))
        {
            return BadRequest(new { error = "tourDate is required in yyyy-MM-dd format." });
        }

        if (string.IsNullOrWhiteSpace(request.TourName))
        {
            return BadRequest(new { error = "tourName is required." });
        }

        var safeTourTime = request.TourTime?.Trim() ?? string.Empty;
        var report = await _reportService.GetReportAsync(parsedDate.Date, request.TourName.Trim(), safeTourTime);

        if (request.GuideId.HasValue)
        {
            report.GuideId = request.GuideId;
        }

        report.GeneralNotes = request.GeneralNotes?.Trim();
        report.IsSubmitted = true;
        report.SubmittedAt = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(request.PublicId))
        {
            report.PublicId = request.PublicId.Trim();
        }

        if (request.Walkers.Count > 0)
        {
            var updatesByBookingId = request.Walkers
                .Where(w => w.BookingId > 0)
                .ToDictionary(w => w.BookingId);

            foreach (var walker in report.Walkers)
            {
                if (!updatesByBookingId.TryGetValue(walker.BookingId, out var update))
                {
                    continue;
                }

                walker.ActualAdults = Math.Max(0, update.ActualAdults);
                walker.ActualChildren = Math.Max(0, update.ActualChildren);
                walker.ActualAttendees = walker.ActualAdults + walker.ActualChildren;
                walker.IsCheckedIn = update.IsCheckedIn;
                walker.DoNotContact = update.DoNotContact;
                walker.ReviewStatus = update.ReviewStatus;
                walker.ReviewNotes = update.ReviewNotes;
            }
        }

        if (!string.IsNullOrWhiteSpace(request.ImagePaths))
        {
            report.ImagePaths = request.ImagePaths;
        }
        else
        {
            var photos = await _photoService.GetPhotosForTourAsync(
                request.TourDate,
                request.TourName.Trim(),
                safeTourTime);

            if (photos.Count > 0 || string.IsNullOrWhiteSpace(report.ImagePaths))
            {
                report.ImagePaths = string.Join(",", photos);
            }
        }

        await _reportService.SaveReportAsync(report);
        var generatedUrl = await _galleryGenerator.GenerateAndSaveHtmlAsync(report);

        return Ok(new
        {
            success = true,
            id = report.Id,
            publicId = report.PublicId,
            isSubmitted = report.IsSubmitted,
            submittedAt = report.SubmittedAt,
            galleryUrl = GetGalleryUrl(report),
            generatedUrl
        });
    }

    private async Task<TourFamilySnapshot> ResolveTourFamilyAsync(
        string? tourName,
        string? vendorName,
        Dictionary<string, TourFamilySnapshot> cache)
    {
        var safeTourName = (tourName ?? string.Empty).Trim();
        var normalizedRaw = TourNameNormalization.NormalizeTourNameForGrouping(vendorName ?? string.Empty, safeTourName);
        var cacheKey = $"{NormalizeVendorLookupKey(vendorName)}|{normalizedRaw}";
        if (cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        TourNameResolution? resolution = null;
        if (!string.IsNullOrWhiteSpace(safeTourName))
        {
            resolution = await _tourNameNormalizer.ResolveToMasterTourAsync(safeTourName, vendorName);
        }

        var desktopName = !string.IsNullOrWhiteSpace(resolution?.MasterTourNameDesktop)
            ? resolution!.MasterTourNameDesktop!.Trim()
            : !string.IsNullOrWhiteSpace(resolution?.MasterTourName)
                ? resolution!.MasterTourName.Trim()
                : TourNameNormalization.NormalizeTourNameForDisplay(safeTourName);

        if (string.IsNullOrWhiteSpace(desktopName))
        {
            desktopName = string.IsNullOrWhiteSpace(safeTourName) ? "Unknown Tour" : safeTourName;
        }

        var mobileName = !string.IsNullOrWhiteSpace(resolution?.MasterTourNameMobile)
            ? resolution!.MasterTourNameMobile!.Trim()
            : desktopName;

        var familyKey = TourNameNormalization.NormalizeTourNameForGrouping(string.Empty, desktopName);
        if (string.IsNullOrWhiteSpace(familyKey))
        {
            familyKey = normalizedRaw;
        }

        if (string.IsNullOrWhiteSpace(familyKey))
        {
            familyKey = "__unknown__";
        }

        var snapshot = new TourFamilySnapshot
        {
            FamilyKey = familyKey,
            DesktopName = desktopName,
            MobileName = mobileName
        };

        cache[cacheKey] = snapshot;
        return snapshot;
    }

    private static bool TryParseTourDate(string raw, out DateTime parsedDate)
    {
        return DateTime.TryParseExact(
                   raw,
                   "yyyy-MM-dd",
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.None,
                   out parsedDate)
               || DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate);
    }

    private string GetGalleryUrl(TourReport? report, string? vendor = null)
    {
        if (report == null || string.IsNullOrWhiteSpace(report.PublicId))
        {
            return string.Empty;
        }

        var baseUrl = _configuration["PublicGallery:BaseUrl"] ?? "http://testcity.w41.wh-2.com";
        var fileName = $"{report.PublicId}.html";

        if (!string.IsNullOrWhiteSpace(vendor))
        {
            var suffix = Regex.Replace(vendor, "[^a-zA-Z0-9]", string.Empty);
            if (!string.IsNullOrWhiteSpace(suffix))
            {
                fileName = $"{report.PublicId}_{suffix}.html";
            }
        }

        return $"{baseUrl}/tour-gallery/{fileName}";
    }

    private string GetWhatsAppLink(TourReport report, string phone)
    {
        var walker = report.Walkers.FirstOrDefault(w =>
            NormalizePhoneDigits(w.Phone) == NormalizePhoneDigits(phone));

        var message = BuildGalleryMessage(report, walker?.VendorName);
        return $"https://wa.me/{phone}?text={Uri.EscapeDataString(message)}";
    }

    private string GetSmsLink(TourReport report, string phone)
    {
        var walker = report.Walkers.FirstOrDefault(w =>
            NormalizePhoneDigits(w.Phone) == NormalizePhoneDigits(phone));

        var message = BuildGalleryMessage(report, walker?.VendorName);
        return $"sms:{phone}?body={Uri.EscapeDataString(message)}";
    }

    private string BuildGalleryMessage(TourReport report, string? vendorName)
    {
        var url = GetGalleryUrl(report, vendorName);
        var tourName = NormalizeMessageTourName(report.TourName);
        var suffix = tourName.EndsWith("tour", StringComparison.OrdinalIgnoreCase) ? string.Empty : " tour";
        return $"Thanks for taking our {tourName}{suffix}! Here are the photos: {url}";
    }

    private static string NormalizeMessageTourName(string? rawTourName)
    {
        var decoded = WebUtility.HtmlDecode(rawTourName ?? string.Empty).Trim();
        decoded = Regex.Replace(decoded, "\\s+", " ");

        if (decoded.EndsWith("reservation", StringComparison.OrdinalIgnoreCase))
        {
            decoded = decoded[..^"reservation".Length].TrimEnd(' ', '-', ':', '|', '/');
        }

        return string.IsNullOrWhiteSpace(decoded) ? "tour" : decoded;
    }

    private static string FormatGuideDisplayName(string? firstName, string? lastName)
    {
        var first = (firstName ?? string.Empty).Trim();
        var last = (lastName ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(last) ? first : $"{first} {last}";
    }

    private static string NormalizeGuideFilter(string? guideFilter)
    {
        if (string.IsNullOrWhiteSpace(guideFilter))
        {
            return "all";
        }

        return guideFilter.Trim();
    }

    private static bool TryParseGuideFilter(
        string guideFilter,
        out int? selectedGuideId,
        out bool onlyUnassigned,
        out string? error)
    {
        selectedGuideId = null;
        onlyUnassigned = false;
        error = null;

        if (string.Equals(guideFilter, "all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(guideFilter, "unassigned", StringComparison.OrdinalIgnoreCase))
        {
            onlyUnassigned = true;
            return true;
        }

        if (guideFilter.StartsWith("guide-", StringComparison.OrdinalIgnoreCase))
        {
            var idText = guideFilter["guide-".Length..];
            if (int.TryParse(idText, out var parsedId) && parsedId > 0)
            {
                selectedGuideId = parsedId;
                return true;
            }
        }

        error = "guideFilter must be one of: all, unassigned, guide-{id}.";
        return false;
    }

    private static bool TryResolveDateRange(
        string? date,
        string? startDate,
        string? endDate,
        out DateTime rangeStart,
        out DateTime rangeEnd,
        out string? error)
    {
        error = null;
        rangeStart = DateTime.Today;
        rangeEnd = DateTime.Today;

        if (!string.IsNullOrWhiteSpace(startDate) || !string.IsNullOrWhiteSpace(endDate))
        {
            if (!TryParseTourDate(startDate ?? string.Empty, out var parsedStart))
            {
                error = "startDate is required in yyyy-MM-dd format when using date range.";
                return false;
            }

            if (!TryParseTourDate(endDate ?? string.Empty, out var parsedEnd))
            {
                error = "endDate is required in yyyy-MM-dd format when using date range.";
                return false;
            }

            rangeStart = parsedStart.Date;
            rangeEnd = parsedEnd.Date;
            if (rangeEnd < rangeStart)
            {
                error = "endDate cannot be earlier than startDate.";
                return false;
            }

            return true;
        }

        if (!string.IsNullOrWhiteSpace(date))
        {
            if (!TryParseTourDate(date, out var parsedDate))
            {
                error = "date is required in yyyy-MM-dd format.";
                return false;
            }

            rangeStart = parsedDate.Date;
            rangeEnd = parsedDate.Date;
            return true;
        }

        return true;
    }

    private static string NormalizePhoneDigits(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return string.Empty;
        }

        return new string(phone.Where(char.IsDigit).ToArray());
    }

    private static TourReportSummary? SelectPreferredReportCandidate(IEnumerable<TourReportSummary> candidates)
    {
        return candidates
            .OrderByDescending(x => x.IsSubmitted)
            .ThenByDescending(x => x.SubmittedAt ?? DateTime.MinValue)
            .ThenByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();
    }

    private static string NormalizeTourTimeKey(string? rawTime)
    {
        if (string.IsNullOrWhiteSpace(rawTime))
        {
            return string.Empty;
        }

        var text = rawTime.Trim();
        if (DateTime.TryParse(text, out var parsedDateTime))
        {
            return parsedDateTime.ToString("HH:mm");
        }

        if (TimeSpan.TryParse(text, out var parsedTimeSpan))
        {
            return $"{parsedTimeSpan.Hours:00}:{parsedTimeSpan.Minutes:00}";
        }

        var compact = text.ToLowerInvariant().Replace(".", string.Empty);
        if (DateTime.TryParse(compact, out parsedDateTime))
        {
            return parsedDateTime.ToString("HH:mm");
        }

        if (TimeSpan.TryParse(compact, out parsedTimeSpan))
        {
            return $"{parsedTimeSpan.Hours:00}:{parsedTimeSpan.Minutes:00}";
        }

        return compact;
    }

    private static string FormatTourTimeDisplay(string? rawTime, string? normalizedTimeKey)
    {
        if (!string.IsNullOrWhiteSpace(rawTime))
        {
            var trimmed = rawTime.Trim();
            if (DateTime.TryParse(trimmed, out var parsedDateTime))
            {
                return parsedDateTime.ToString("h:mm tt");
            }

            if (TimeSpan.TryParse(trimmed, out var parsedTimeSpan))
            {
                return DateTime.Today.Add(parsedTimeSpan).ToString("h:mm tt");
            }

            return trimmed;
        }

        if (!string.IsNullOrWhiteSpace(normalizedTimeKey) && TimeSpan.TryParse(normalizedTimeKey, out var normalizedTimeSpan))
        {
            return DateTime.Today.Add(normalizedTimeSpan).ToString("h:mm tt");
        }

        return string.Empty;
    }

    private static string NormalizeVendorLookupKey(string? vendorName)
    {
        if (string.IsNullOrWhiteSpace(vendorName))
        {
            return string.Empty;
        }

        return new string(vendorName.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
    }

    private static string GetSafeExtension(IFormFile file)
    {
        var contentType = file.ContentType?.ToLowerInvariant() ?? string.Empty;
        if (contentType.Contains("png", StringComparison.Ordinal))
        {
            return ".png";
        }

        if (contentType.Contains("jpeg", StringComparison.Ordinal) || contentType.Contains("jpg", StringComparison.Ordinal))
        {
            return ".jpg";
        }

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(ext))
        {
            return ".jpg";
        }

        ext = ext.ToLowerInvariant();
        if (ext == ".jpeg")
        {
            return ".jpg";
        }

        return ext is ".jpg" or ".png" ? ext : ".jpg";
    }

    private sealed class TourFamilySnapshot
    {
        public string FamilyKey { get; set; } = string.Empty;
        public string DesktopName { get; set; } = string.Empty;
        public string MobileName { get; set; } = string.Empty;
    }
}
