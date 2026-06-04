using Microsoft.AspNetCore.Mvc;
using Email.Services;
using System.Globalization;
using System.IO.Compression;

namespace Email.Controllers;

[ApiController]
[Route("tour-photos")]
public class TourPhotoUploadController : ControllerBase
{
    private readonly ITourPhotoService _photoService;
    private readonly IGuideReportService _reportService;
    private readonly IPhotoUploadLog _photoLog;
    private readonly ILogger<TourPhotoUploadController> _logger;
    private readonly IWebHostEnvironment _env;
    private const long RawSaveThresholdBytes = 3_000_000; // ~3MB

    public TourPhotoUploadController(
        ITourPhotoService photoService,
        IGuideReportService reportService,
        IPhotoUploadLog photoLog,
        ILogger<TourPhotoUploadController> logger,
        IWebHostEnvironment env)
    {
        _photoService = photoService;
        _reportService = reportService;
        _photoLog = photoLog;
        _logger = logger;
        _env = env;
    }

    [HttpPost("upload-alt")]
    [RequestFormLimits(MultipartBodyLengthLimit = 200_000_000)]
    [RequestSizeLimit(200_000_000)]
    public async Task<IActionResult> UploadAlt([FromForm] List<IFormFile> files,
        [FromForm] string tourDate,
        [FromForm] string tourName,
        [FromForm] string? tourTime)
    {
        var uploadId = string.IsNullOrWhiteSpace(HttpContext.TraceIdentifier)
            ? Guid.NewGuid().ToString("N")
            : HttpContext.TraceIdentifier;
        var requestTimer = System.Diagnostics.Stopwatch.StartNew();
        var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = Request.Headers.UserAgent.ToString();
        var contentLength = Request.ContentLength ?? 0;

        _logger.LogInformation(
            "UploadAlt START UploadId={UploadId} TourDate={TourDate} TourName={TourName} TourTime={TourTime} ContentLength={ContentLength} RemoteIp={RemoteIp} UserAgent={UserAgent}",
            uploadId, tourDate, tourName, tourTime, contentLength, remoteIp, userAgent);
        _photoLog.Info($"UploadAlt START UploadId={uploadId} TourDate={tourDate} TourName={tourName} TourTime={tourTime} ContentLength={contentLength} RemoteIp={remoteIp} UserAgent={userAgent}");

        if (files == null || files.Count == 0)
        {
            _logger.LogWarning("UploadAlt REJECTED UploadId={UploadId} Reason=NoFiles", uploadId);
            _photoLog.Error($"UploadAlt REJECTED UploadId={uploadId} Reason=NoFiles");
            return BadRequest(new { error = "No files uploaded." });
        }

        if (string.IsNullOrWhiteSpace(tourDate) || string.IsNullOrWhiteSpace(tourName) || string.IsNullOrWhiteSpace(tourTime))
        {
            _logger.LogWarning(
                "UploadAlt REJECTED UploadId={UploadId} Reason=MissingRequiredFields TourDate={TourDate} TourName={TourName} TourTime={TourTime}",
                uploadId, tourDate, tourName, tourTime);
            _photoLog.Error($"UploadAlt REJECTED UploadId={uploadId} Reason=MissingRequiredFields TourDate={tourDate} TourName={tourName} TourTime={tourTime}");
            return BadRequest(new { error = "TourDate, TourName, and TourTime are required. Tour time must match actual tour time." });
        }
        tourTime = tourTime.Trim();

        var declaredTotalBytes = files.Sum(f => f.Length);
        _logger.LogInformation(
            "UploadAlt VALIDATED UploadId={UploadId} FileCount={FileCount} DeclaredTotalBytes={DeclaredTotalBytes}",
            uploadId, files.Count, declaredTotalBytes);
        _photoLog.Info($"UploadAlt VALIDATED UploadId={uploadId} FileCount={files.Count} DeclaredTotalBytes={declaredTotalBytes}");

        var saved = new List<string>();
        var failedCount = 0;

        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            var fileTimer = System.Diagnostics.Stopwatch.StartNew();
            var fileExtension = GetSafeExtension(file);

            _logger.LogInformation(
                "UploadAlt FILE START UploadId={UploadId} FileIndex={FileIndex} FileCount={FileCount} FileName={FileName} FileSizeBytes={FileSizeBytes} ContentType={ContentType}",
                uploadId, index + 1, files.Count, file.FileName, file.Length, file.ContentType);
            _photoLog.Info($"UploadAlt FILE START UploadId={uploadId} FileIndex={index + 1}/{files.Count} FileName={file.FileName} FileSizeBytes={file.Length} ContentType={file.ContentType}");

            if (file.Length <= 0)
            {
                failedCount++;
                _logger.LogWarning(
                    "UploadAlt FILE SKIPPED UploadId={UploadId} FileIndex={FileIndex} Reason=EmptyFile FileName={FileName}",
                    uploadId, index + 1, file.FileName);
                _photoLog.Error($"UploadAlt FILE SKIPPED UploadId={uploadId} FileIndex={index + 1} Reason=EmptyFile FileName={file.FileName}");
                continue;
            }

            try
            {
                string? savedPath;
                var saveTimer = System.Diagnostics.Stopwatch.StartNew();

                _logger.LogInformation(
                    "UploadAlt FILE MODE UploadId={UploadId} FileIndex={FileIndex} Mode=RawStream ThresholdBytes={ThresholdBytes} FileSizeBytes={FileSizeBytes} Extension={Extension}",
                    uploadId, index + 1, RawSaveThresholdBytes, file.Length, fileExtension);
                _photoLog.Info($"UploadAlt FILE MODE UploadId={uploadId} FileIndex={index + 1} Mode=RawStream ThresholdBytes={RawSaveThresholdBytes} FileSizeBytes={file.Length} Extension={fileExtension}");

                await using (var stream = file.OpenReadStream())
                {
                    savedPath = await _photoService.SavePhotoRawAsync(stream, tourDate, tourName, tourTime, fileExtension);
                }

                saveTimer.Stop();

                if (!string.IsNullOrWhiteSpace(savedPath))
                {
                    saved.Add(savedPath);
                    _logger.LogInformation(
                        "UploadAlt FILE SAVED UploadId={UploadId} FileIndex={FileIndex} FileName={FileName} SavedPath={SavedPath} SaveDurationMs={SaveDurationMs}",
                        uploadId, index + 1, file.FileName, savedPath, saveTimer.ElapsedMilliseconds);
                    _photoLog.Info($"UploadAlt FILE SAVED UploadId={uploadId} FileIndex={index + 1} FileName={file.FileName} SavedPath={savedPath} SaveDurationMs={saveTimer.ElapsedMilliseconds}");
                }
                else
                {
                    failedCount++;
                    _logger.LogWarning(
                        "UploadAlt FILE SAVE RETURNED NULL UploadId={UploadId} FileIndex={FileIndex} FileName={FileName} SaveDurationMs={SaveDurationMs}",
                        uploadId, index + 1, file.FileName, saveTimer.ElapsedMilliseconds);
                    _photoLog.Error($"UploadAlt FILE SAVE RETURNED NULL UploadId={uploadId} FileIndex={index + 1} FileName={file.FileName} SaveDurationMs={saveTimer.ElapsedMilliseconds}");
                }
            }
            catch (Exception ex)
            {
                failedCount++;
                _logger.LogError(
                    ex,
                    "UploadAlt FILE ERROR UploadId={UploadId} FileIndex={FileIndex} FileName={FileName} TourDate={TourDate} TourName={TourName} TourTime={TourTime}",
                    uploadId, index + 1, file.FileName, tourDate, tourName, tourTime);
                _photoLog.Error($"UploadAlt FILE ERROR UploadId={uploadId} FileIndex={index + 1} FileName={file.FileName} TourDate={tourDate} TourName={tourName} TourTime={tourTime} Error={ex.Message}");
            }
            finally
            {
                fileTimer.Stop();
                _logger.LogInformation(
                    "UploadAlt FILE END UploadId={UploadId} FileIndex={FileIndex} FileName={FileName} FileDurationMs={FileDurationMs}",
                    uploadId, index + 1, file.FileName, fileTimer.ElapsedMilliseconds);
                _photoLog.Info($"UploadAlt FILE END UploadId={uploadId} FileIndex={index + 1} FileName={file.FileName} FileDurationMs={fileTimer.ElapsedMilliseconds}");
            }
        }

        requestTimer.Stop();
        _logger.LogInformation(
            "UploadAlt SUMMARY UploadId={UploadId} FileCount={FileCount} SavedCount={SavedCount} FailedCount={FailedCount} RequestDurationMs={RequestDurationMs}",
            uploadId, files.Count, saved.Count, failedCount, requestTimer.ElapsedMilliseconds);
        _photoLog.Info($"UploadAlt SUMMARY UploadId={uploadId} FileCount={files.Count} SavedCount={saved.Count} FailedCount={failedCount} RequestDurationMs={requestTimer.ElapsedMilliseconds}");

        if (saved.Count == 0)
        {
            _logger.LogError(
                "UploadAlt RETURN 500 UploadId={UploadId} FilesReceived={FilesReceived} TourDate={TourDate} TourName={TourName} TourTime={TourTime}",
                uploadId, files.Count, tourDate, tourName, tourTime);
            _photoLog.Error($"UploadAlt RETURN 500 UploadId={uploadId} FilesReceived={files.Count} TourDate={tourDate} TourName={tourName} TourTime={tourTime}");
            return StatusCode(500, new { error = "No files were saved." });
        }

        _logger.LogInformation("UploadAlt RETURN 200 UploadId={UploadId} SavedCount={SavedCount}", uploadId, saved.Count);
        _photoLog.Info($"UploadAlt RETURN 200 UploadId={uploadId} SavedCount={saved.Count}");
        return Ok(new { saved });
    }

    [HttpPost("sync-report")]
    public async Task<IActionResult> SyncReport([FromBody] SyncReportRequest? request)
    {
        if (request == null)
        {
            return BadRequest(new { error = "Request body is required." });
        }

        if (string.IsNullOrWhiteSpace(request.TourDate) ||
            string.IsNullOrWhiteSpace(request.TourName) ||
            string.IsNullOrWhiteSpace(request.TourTime))
        {
            return BadRequest(new { error = "TourDate, TourName, and TourTime are required. Tour time must match actual tour time." });
        }

        DateTime parsedTourDate;
        if (!DateTime.TryParseExact(
                request.TourDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out parsedTourDate) &&
            !DateTime.TryParse(request.TourDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedTourDate))
        {
            return BadRequest(new { error = "TourDate must be a valid date (expected yyyy-MM-dd)." });
        }

        var syncId = Guid.NewGuid().ToString("N")[..12];
        var timer = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var syncTourTime = request.TourTime;

            _logger.LogInformation(
                "SyncReport START SyncId={SyncId} TourDate={TourDate} TourName={TourName} TourTime={TourTime}",
                syncId, request.TourDate, request.TourName, syncTourTime);
            _photoLog.Info($"SyncReport START SyncId={syncId} TourDate={request.TourDate} TourName={request.TourName} TourTime={syncTourTime}");

            var photos = await _photoService.GetPhotosForTourAsync(request.TourDate, request.TourName, syncTourTime);
            var imagePaths = string.Join(",", photos);

            var reportId = await _reportService.UpsertReportImagePathsAsync(
                parsedTourDate.Date,
                request.TourName,
                syncTourTime,
                imagePaths);

            timer.Stop();
            _logger.LogInformation(
                "SyncReport SUCCESS SyncId={SyncId} ReportId={ReportId} PhotoCount={PhotoCount} DurationMs={DurationMs}",
                syncId, reportId, photos.Count, timer.ElapsedMilliseconds);
            _photoLog.Info($"SyncReport SUCCESS SyncId={syncId} ReportId={reportId} PhotoCount={photos.Count} DurationMs={timer.ElapsedMilliseconds}");

            return Ok(new
            {
                syncId,
                reportId,
                photoCount = photos.Count,
                success = true
            });
        }
        catch (Exception ex)
        {
            timer.Stop();
            _logger.LogError(
                ex,
                "SyncReport ERROR SyncId={SyncId} TourDate={TourDate} TourName={TourName} TourTime={TourTime} DurationMs={DurationMs}",
                syncId, request.TourDate, request.TourName, request.TourTime, timer.ElapsedMilliseconds);
            _photoLog.Error($"SyncReport ERROR SyncId={syncId} TourDate={request.TourDate} TourName={request.TourName} TourTime={request.TourTime} DurationMs={timer.ElapsedMilliseconds} Error={ex.Message}");
            return StatusCode(500, new { error = "Failed to sync report photos." });
        }
    }

    [HttpGet("list")]
    public async Task<IActionResult> ListPhotos(
        [FromQuery] string tourDate,
        [FromQuery] string tourName,
        [FromQuery] string? tourTime)
    {
        if (string.IsNullOrWhiteSpace(tourDate) || string.IsNullOrWhiteSpace(tourName))
        {
            return BadRequest(new { error = "TourDate and TourName are required." });
        }

        var listId = Guid.NewGuid().ToString("N")[..12];
        var safeTourTime = tourTime ?? string.Empty;

        try
        {
            _logger.LogInformation(
                "ListPhotos START ListId={ListId} TourDate={TourDate} TourName={TourName} TourTime={TourTime}",
                listId, tourDate, tourName, safeTourTime);
            _photoLog.Info($"ListPhotos START ListId={listId} TourDate={tourDate} TourName={tourName} TourTime={safeTourTime}");

            var photos = await _photoService.GetPhotosForTourAsync(tourDate, tourName, safeTourTime);

            _logger.LogInformation(
                "ListPhotos SUCCESS ListId={ListId} PhotoCount={PhotoCount}",
                listId, photos.Count);
            _photoLog.Info($"ListPhotos SUCCESS ListId={listId} PhotoCount={photos.Count}");

            return Ok(new
            {
                listId,
                photoCount = photos.Count,
                photos
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "ListPhotos ERROR ListId={ListId} TourDate={TourDate} TourName={TourName} TourTime={TourTime}",
                listId, tourDate, tourName, safeTourTime);
            _photoLog.Error($"ListPhotos ERROR ListId={listId} TourDate={tourDate} TourName={tourName} TourTime={safeTourTime} Error={ex.Message}");
            return StatusCode(500, new { error = "Failed to load photos." });
        }
    }

    [HttpGet("download-all/{publicId}")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public async Task<IActionResult> DownloadAll(string publicId)
    {
        if (string.IsNullOrWhiteSpace(publicId))
        {
            return BadRequest(new { error = "PublicId is required." });
        }

        try
        {
            var report = await _reportService.GetReportByPublicIdAsync(publicId);
            if (report == null || string.IsNullOrWhiteSpace(report.ImagePaths))
            {
                return NotFound(new { error = "Gallery not found or empty." });
            }

            var photoPaths = report.ImagePaths.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (photoPaths.Length == 0)
            {
                return NotFound(new { error = "No photos in this gallery." });
            }

            var memoryStream = new MemoryStream();
            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
            {
                var fileIndex = 0;
                foreach (var relativePath in photoPaths)
                {
                    var cleanPath = relativePath.Trim().TrimStart('/');
                    if (cleanPath.Contains("_thumb.")) continue;

                    var absolutePath = Path.Combine(_env.WebRootPath, cleanPath.Replace('/', Path.DirectorySeparatorChar));
                    if (!System.IO.File.Exists(absolutePath)) continue;

                    fileIndex++;
                    var fileName = Path.GetFileName(absolutePath);
                    var entryName = $"{fileIndex:D3}_{fileName}";
                    var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                    await using var entryStream = entry.Open();
                    await using var fileStream = System.IO.File.OpenRead(absolutePath);
                    await fileStream.CopyToAsync(entryStream);
                }
            }

            memoryStream.Position = 0;
            var safeTourName = (report.TourName ?? "tour").Replace(" ", "-");
            var zipName = $"{safeTourName}-photos-{report.TourDate:yyyy-MM-dd}.zip";
            return File(memoryStream, "application/zip", zipName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DownloadAll error for PublicId={PublicId}", publicId);
            return StatusCode(500, new { error = "Failed to create download archive." });
        }
    }

    public sealed class SyncReportRequest
    {
        public string TourDate { get; set; } = string.Empty;
        public string TourName { get; set; } = string.Empty;
        public string TourTime { get; set; } = string.Empty;
    }

    private static string GetSafeExtension(IFormFile file)
    {
        var contentType = file.ContentType?.ToLowerInvariant() ?? string.Empty;
        if (contentType.Contains("png"))
        {
            return ".png";
        }

        if (contentType.Contains("jpeg") || contentType.Contains("jpg"))
        {
            return ".jpg";
        }

        // Video (saved raw)
        if (contentType.Contains("mp4")) return ".mp4";
        if (contentType.Contains("quicktime")) return ".mov";
        if (contentType.Contains("webm")) return ".webm";

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(ext))
        {
            return ".jpg";
        }

        ext = ext.ToLowerInvariant();
        if (ext == ".jpeg")
        {
            ext = ".jpg";
        }

        return ext is ".jpg" or ".png" or ".mp4" or ".mov" or ".webm"
            ? ext
            : ".jpg";
    }
}
