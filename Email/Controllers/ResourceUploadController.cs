using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Email.Models.Resources;
using Email.Services.Resources;
using System.Security.Claims;

namespace Email.Controllers;

[ApiController]
[Route("resources")]
[Authorize]
public class ResourceUploadController : ControllerBase
{
    private readonly IResourceService _resourceService;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ResourceUploadController> _logger;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp",
        ".mp4", ".mov", ".webm",
        ".pdf"
    };

    public ResourceUploadController(
        IResourceService resourceService,
        IWebHostEnvironment env,
        ILogger<ResourceUploadController> logger)
    {
        _resourceService = resourceService;
        _env = env;
        _logger = logger;
    }

    [HttpPost("upload")]
    [RequestFormLimits(MultipartBodyLengthLimit = 200_000_000)]
    [RequestSizeLimit(200_000_000)]
    public async Task<IActionResult> Upload(
        [FromForm] IFormFile file,
        [FromForm] string? description,
        [FromForm] string? tourIds)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "No file uploaded." });
        }

        var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant() ?? string.Empty;
        if (!AllowedExtensions.Contains(ext))
        {
            return BadRequest(new { error = $"File type '{ext}' is not supported. Allowed: {string.Join(", ", AllowedExtensions)}" });
        }

        var userId = GetCurrentUserId();
        var userName = GetCurrentUserName();
        var fileType = ClassifyFileType(ext);
        var yearMonth = DateTime.UtcNow.ToString("yyyy-MM");
        var uniqueName = $"{Guid.NewGuid():N}{ext}";
        var relativeDir = $"resources/{yearMonth}";
        var absoluteDir = Path.Combine(_env.WebRootPath, relativeDir);

        Directory.CreateDirectory(absoluteDir);

        var absolutePath = Path.Combine(absoluteDir, uniqueName);
        var relativePath = $"{relativeDir}/{uniqueName}";

        try
        {
            await using var stream = new FileStream(absolutePath, FileMode.Create);
            await file.CopyToAsync(stream);

            var parsedTourIds = ParseTourIds(tourIds);

            var resource = new ResourceItem
            {
                FileName = uniqueName,
                OriginalFileName = file.FileName,
                FileType = fileType,
                FileSizeBytes = file.Length,
                RelativePath = relativePath,
                Description = description,
                UploadedByUserId = userId,
                UploadedByName = userName,
                TourIds = parsedTourIds
            };

            var id = await _resourceService.CreateResourceAsync(resource);

            _logger.LogInformation(
                "Resource uploaded: Id={ResourceId} FileName={FileName} Type={FileType} Size={Size} User={User}",
                id, file.FileName, fileType, file.Length, userName);

            return Ok(new { id, relativePath, fileType });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading resource {FileName}", file.FileName);
            if (System.IO.File.Exists(absolutePath))
            {
                System.IO.File.Delete(absolutePath);
            }
            return StatusCode(500, new { error = "Failed to upload resource." });
        }
    }

    private int GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? User.FindFirst("UserId")?.Value;
        return int.TryParse(claim, out var id) ? id : 0;
    }

    private string GetCurrentUserName()
    {
        return User.FindFirst(ClaimTypes.Name)?.Value
               ?? User.Identity?.Name
               ?? "Unknown";
    }

    private static string ClassifyFileType(string ext)
    {
        return ext.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" => "photo",
            ".mp4" or ".mov" or ".webm" => "video",
            ".pdf" => "pdf",
            _ => "other"
        };
    }

    private static List<int> ParseTourIds(string? tourIdsString)
    {
        if (string.IsNullOrWhiteSpace(tourIdsString)) return new List<int>();
        return tourIdsString
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var id) ? id : 0)
            .Where(id => id > 0)
            .ToList();
    }
}
