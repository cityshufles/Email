// 2025-12-21 - Tour photo service implementation - filesystem storage
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Email.Services;

/// <summary>
/// Filesystem-based implementation of tour photo storage.
/// Photos are stored in wwwroot/tour-photos/{tourDate}/{sanitizedTourName}_{sanitizedTime}_{timestamp}.jpg
/// Images are re-encoded using SkiaSharp to ensure compatibility with all image viewers.
/// </summary>
public class TourPhotoService : ITourPhotoService
{
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<TourPhotoService> _logger;
    private readonly IPhotoUploadLog _photoLog;
    private const string PhotosFolder = "tour-photos";
    private const int ThumbnailMaxDimension = 640;
    private const int ThumbnailJpegQuality = 70;
    private const string ThumbnailSuffix = "_thumb";

    public TourPhotoService(IWebHostEnvironment environment, ILogger<TourPhotoService> logger, IPhotoUploadLog photoLog)
    {
        _environment = environment;
        _logger = logger;
        _photoLog = photoLog;
    }

    public async Task<string?> SavePhotoAsync(string imageData, string tourDate, string tourName, string tourTime, string fileExtension = "jpg")
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var resultPath = (string?)null;
        try
        {
            // Parse base64 data (handle data URI format)
            if (string.IsNullOrWhiteSpace(imageData))
            {
                _logger.LogWarning("SavePhotoAsync called with empty base64 image data");
                return null;
            }

            _logger.LogInformation(
                "SavePhotoAsync(base64) START TourDate={TourDate} TourName={TourName} TourTime={TourTime} DataLength={DataLength}",
                tourDate, tourName, tourTime, imageData.Length);

            var base64Data = imageData;
            if (imageData.Contains(","))
            {
                var header = imageData.Split(',')[0];
                base64Data = imageData.Split(',')[1];
                _logger.LogInformation("Detected image data URI header: {Header}", header);
            }

            byte[] inputBytes;
            try
            {
                inputBytes = Convert.FromBase64String(base64Data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decode base64 image data");
                return null;
            }

            resultPath = await SavePhotoAsync(inputBytes, tourDate, tourName, tourTime, fileExtension);
            return resultPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save tour photo for {TourName} on {TourDate}", tourName, tourDate);
            return null;
        }
        finally
        {
            timer.Stop();
            _logger.LogInformation(
                "SavePhotoAsync(base64) END TourDate={TourDate} TourName={TourName} TourTime={TourTime} ResultPath={ResultPath} DurationMs={DurationMs}",
                tourDate, tourName, tourTime, resultPath ?? "(null)", timer.ElapsedMilliseconds);
        }
    }

    public async Task<string?> SavePhotoAsync(byte[] imageBytes, string tourDate, string tourName, string tourTime, string fileExtension = "jpg")
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var resultPath = (string?)null;
        try
        {
            if (imageBytes == null || imageBytes.Length == 0)
            {
                _logger.LogWarning("SavePhotoAsync called with empty image bytes");
                return null;
            }

            _logger.LogInformation(
                "SavePhotoAsync(bytes) START TourDate={TourDate} TourName={TourName} TourTime={TourTime} BytesLength={BytesLength}",
                tourDate, tourName, tourTime, imageBytes.Length);
            _photoLog.Info($"SavePhotoAsync(bytes) START TourDate={tourDate} TourName={tourName} TourTime={tourTime} BytesLength={imageBytes.Length}");

            var inputSizeKB = imageBytes.Length / 1024.0;
            var inputSizeMB = inputSizeKB / 1024.0;
            _logger.LogInformation("Image bytes received: {Size} bytes ({SizeKB:F0}KB, {SizeMB:F2}MB)",
                imageBytes.Length, inputSizeKB, inputSizeMB);

            // Re-encode image using SkiaSharp for universal compatibility
            byte[] outputBytes;
            int width = 0, height = 0;
            string orientation = "unknown";
            SKCodecOrigin exifOrigin = SKCodecOrigin.TopLeft;

            using (var codecStream = new MemoryStream(imageBytes))
            using (var codec = SKCodec.Create(codecStream))
            {
                if (codec != null)
                {
                    exifOrigin = codec.Origin;
                }
                else
                {
                    _logger.LogWarning("SkiaSharp codec could not be created. Skipping EXIF orientation.");
                }
            }

            if (exifOrigin == SKCodecOrigin.TopLeft && TryReadExifOrientation(imageBytes, out var exifOrientationValue))
            {
                var mapped = MapExifOrientation(exifOrientationValue);
                if (mapped != SKCodecOrigin.TopLeft)
                {
                    exifOrigin = mapped;
                    _logger.LogInformation("EXIF orientation read from JPEG metadata: {ExifValue} -> {ExifOrigin}", exifOrientationValue, exifOrigin);
                }
            }

            using (var decodeStream = new MemoryStream(imageBytes))
            using (var decodedBitmap = SKBitmap.Decode(decodeStream))
            {
                if (decodedBitmap == null)
                {
                    _logger.LogError("SkiaSharp failed to decode image");
                    return null;
                }

                SKBitmap? orientedBitmap = null;
                try
                {
                    if (exifOrigin != SKCodecOrigin.TopLeft)
                    {
                        orientedBitmap = ApplyExifOrientation(decodedBitmap, exifOrigin);
                    }

                    var bitmapToEncode = orientedBitmap ?? decodedBitmap;
                    width = bitmapToEncode.Width;
                    height = bitmapToEncode.Height;

                    // Determine orientation
                    if (width > height)
                        orientation = "landscape";
                    else if (height > width)
                        orientation = "portrait";
                    else
                        orientation = "square";

                    var megapixels = (width * height) / 1_000_000.0;

                    _logger.LogInformation("Image metadata: {Width}x{Height} ({Megapixels:F1}MP), {Orientation} (EXIF {ExifOrigin})",
                        width, height, megapixels, orientation, exifOrigin);

                    using (var skImage = SKImage.FromBitmap(bitmapToEncode))
                    using (var outputStream = new MemoryStream())
                    {
                        // Encode as JPEG at 90% quality for good quality/size balance
                        skImage.Encode(SKEncodedImageFormat.Jpeg, 90).SaveTo(outputStream);
                        outputBytes = outputStream.ToArray();
                    }
                }
                finally
                {
                    orientedBitmap?.Dispose();
                }
            }

            var outputSizeKB = outputBytes.Length / 1024.0;
            var outputSizeMB = outputSizeKB / 1024.0;
            var compressionRatio = (1.0 - (outputBytes.Length / (double)imageBytes.Length)) * 100;

            _logger.LogInformation("Re-encoded to JPEG: {Size} bytes ({SizeKB:F0}KB, {SizeMB:F2}MB) - {Compression:F1}% compression",
                outputBytes.Length, outputSizeKB, outputSizeMB, compressionRatio);

            // Sanitize inputs for filesystem safety
            var sanitizedDate = SanitizeForFilename(tourDate);
            var sanitizedName = SanitizeForFilename(tourName);
            var sanitizedTime = SanitizeForFilename(tourTime);
            // Always save as .jpg since we re-encode to JPEG
            // 2026-02-06: Added milliseconds and GUID to prevent collisions during batch upload
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            var uniqueId = Guid.NewGuid().ToString("N").Substring(0, 8);

            // Build path: wwwroot/tour-photos/{date}/{name}_{time}_{timestamp}_{uniqueId}.jpg
            var dateFolder = Path.Combine(_environment.WebRootPath, PhotosFolder, sanitizedDate);
            Directory.CreateDirectory(dateFolder);

            var fileName = $"{sanitizedName}_{sanitizedTime}_{timestamp}_{uniqueId}.jpg";
            var fullPath = Path.Combine(dateFolder, fileName);

            await File.WriteAllBytesAsync(fullPath, outputBytes);
            QueueThumbnailGeneration(fullPath);

            // Return path relative to wwwroot for web access
            var relativePath = $"/{PhotosFolder}/{sanitizedDate}/{fileName}";

            _logger.LogInformation("Saved tour photo: {Path} - {Width}x{Height} {Orientation}, {SizeMB:F2}MB",
                relativePath, width, height, orientation, outputSizeMB);
            _photoLog.Info($"SavePhotoAsync(bytes) SAVED Path={relativePath} Width={width} Height={height} Orientation={orientation} SizeBytes={outputBytes.Length}");
            resultPath = relativePath;
            return resultPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save tour photo for {TourName} on {TourDate}", tourName, tourDate);
            _photoLog.Error($"SavePhotoAsync(bytes) ERROR TourDate={tourDate} TourName={tourName} TourTime={tourTime} Error={ex.Message}");
            return null;
        }
        finally
        {
            timer.Stop();
            _logger.LogInformation(
                "SavePhotoAsync(bytes) END TourDate={TourDate} TourName={TourName} TourTime={TourTime} ResultPath={ResultPath} DurationMs={DurationMs}",
                tourDate, tourName, tourTime, resultPath ?? "(null)", timer.ElapsedMilliseconds);
        }
    }

    public async Task<string?> SavePhotoRawAsync(Stream inputStream, string tourDate, string tourName, string tourTime, string fileExtension = "jpg")
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var resultPath = (string?)null;
        try
        {
            if (inputStream == null)
            {
                _logger.LogWarning("SavePhotoRawAsync called with null input stream");
                return null;
            }

            long? streamLength = null;
            if (inputStream.CanSeek)
            {
                try { streamLength = inputStream.Length; } catch { /* ignore */ }
            }
            _logger.LogInformation(
                "SavePhotoRawAsync START TourDate={TourDate} TourName={TourName} TourTime={TourTime} Extension={Extension} StreamLength={StreamLength}",
                tourDate, tourName, tourTime, fileExtension, streamLength?.ToString() ?? "unknown");
            _photoLog.Info($"SavePhotoRawAsync START TourDate={tourDate} TourName={tourName} TourTime={tourTime} Extension={fileExtension} StreamLength={streamLength?.ToString() ?? "unknown"}");

            var sanitizedDate = SanitizeForFilename(tourDate);
            var sanitizedName = SanitizeForFilename(tourName);
            var sanitizedTime = SanitizeForFilename(tourTime);
            var normalizedExtension = NormalizeExtension(fileExtension);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            var uniqueId = Guid.NewGuid().ToString("N").Substring(0, 8);

            var dateFolder = Path.Combine(_environment.WebRootPath, PhotosFolder, sanitizedDate);
            Directory.CreateDirectory(dateFolder);

            var fileName = $"{sanitizedName}_{sanitizedTime}_{timestamp}_{uniqueId}{normalizedExtension}";
            var fullPath = Path.Combine(dateFolder, fileName);

            await using (var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await inputStream.CopyToAsync(fileStream);
            }

            QueueThumbnailGeneration(fullPath);

            var fileInfo = new FileInfo(fullPath);
            var relativePath = $"/{PhotosFolder}/{sanitizedDate}/{fileName}";

            _logger.LogInformation("Saved raw tour photo: {Path} - {SizeBytes} bytes ({SizeKB:F0}KB, {SizeMB:F2}MB) Extension={Extension}",
                relativePath,
                fileInfo.Length,
                fileInfo.Length / 1024.0,
                fileInfo.Length / 1024.0 / 1024.0,
                normalizedExtension);
            _photoLog.Info($"SavePhotoRawAsync SAVED Path={relativePath} SizeBytes={fileInfo.Length} Extension={normalizedExtension}");

            resultPath = relativePath;
            return resultPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save raw tour photo for {TourName} on {TourDate}", tourName, tourDate);
            _photoLog.Error($"SavePhotoRawAsync ERROR TourDate={tourDate} TourName={tourName} TourTime={tourTime} Error={ex.Message}");
            return null;
        }
        finally
        {
            timer.Stop();
            _logger.LogInformation(
                "SavePhotoRawAsync END TourDate={TourDate} TourName={TourName} TourTime={TourTime} ResultPath={ResultPath} DurationMs={DurationMs}",
                tourDate, tourName, tourTime, resultPath ?? "(null)", timer.ElapsedMilliseconds);
        }
    }

    private static SKBitmap ApplyExifOrientation(SKBitmap bitmap, SKCodecOrigin origin)
    {
        if (origin == SKCodecOrigin.TopLeft)
        {
            return bitmap;
        }

        var width = bitmap.Width;
        var height = bitmap.Height;
        var newWidth = width;
        var newHeight = height;

        float a;
        float b;
        float c;
        float d;
        float e;
        float f;

        switch (origin)
        {
            case SKCodecOrigin.TopRight:
                a = -1; b = 0; c = 0; d = 1; e = width; f = 0;
                break;
            case SKCodecOrigin.BottomRight:
                a = -1; b = 0; c = 0; d = -1; e = width; f = height;
                break;
            case SKCodecOrigin.BottomLeft:
                a = 1; b = 0; c = 0; d = -1; e = 0; f = height;
                break;
            case SKCodecOrigin.LeftTop:
                a = 0; b = 1; c = 1; d = 0; e = 0; f = 0;
                newWidth = height;
                newHeight = width;
                break;
            case SKCodecOrigin.RightTop:
                a = 0; b = 1; c = -1; d = 0; e = height; f = 0;
                newWidth = height;
                newHeight = width;
                break;
            case SKCodecOrigin.RightBottom:
                a = 0; b = -1; c = -1; d = 0; e = height; f = width;
                newWidth = height;
                newHeight = width;
                break;
            case SKCodecOrigin.LeftBottom:
                a = 0; b = -1; c = 1; d = 0; e = 0; f = width;
                newWidth = height;
                newHeight = width;
                break;
            default:
                return bitmap;
        }

        var matrix = new SKMatrix
        {
            ScaleX = a,
            SkewY = b,
            SkewX = c,
            ScaleY = d,
            TransX = e,
            TransY = f,
            Persp0 = 0,
            Persp1 = 0,
            Persp2 = 1
        };

        var rotated = new SKBitmap(newWidth, newHeight);
        using (var canvas = new SKCanvas(rotated))
        {
            canvas.SetMatrix(matrix);
            canvas.DrawBitmap(bitmap, 0, 0);
            canvas.Flush();
        }

        return rotated;
    }

    private static bool TryReadExifOrientation(byte[] data, out int orientation)
    {
        orientation = 1;

        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
        {
            return false;
        }

        int index = 2;
        while (index + 3 < data.Length)
        {
            if (data[index] != 0xFF)
            {
                index++;
                continue;
            }

            byte marker = data[index + 1];

            // Start of Scan or End of Image: stop parsing metadata
            if (marker == 0xDA || marker == 0xD9)
            {
                break;
            }

            if (index + 3 >= data.Length) break;
            int segmentLength = (data[index + 2] << 8) + data[index + 3];
            if (segmentLength < 2 || index + 2 + segmentLength > data.Length) break;

            if (marker == 0xE1) // APP1 - EXIF
            {
                int segmentStart = index + 4;
                if (segmentStart + 6 >= data.Length) return false;

                if (data[segmentStart] == (byte)'E' &&
                    data[segmentStart + 1] == (byte)'x' &&
                    data[segmentStart + 2] == (byte)'i' &&
                    data[segmentStart + 3] == (byte)'f' &&
                    data[segmentStart + 4] == 0 &&
                    data[segmentStart + 5] == 0)
                {
                    int tiffStart = segmentStart + 6;
                    if (tiffStart + 8 >= data.Length) return false;

                    bool littleEndian = data[tiffStart] == (byte)'I' && data[tiffStart + 1] == (byte)'I';
                    int ifdOffset = ReadInt32(data, tiffStart + 4, littleEndian);
                    int ifdStart = tiffStart + ifdOffset;

                    if (ifdStart + 2 > data.Length) return false;
                    int entryCount = ReadInt16(data, ifdStart, littleEndian);

                    for (int i = 0; i < entryCount; i++)
                    {
                        int entryOffset = ifdStart + 2 + (12 * i);
                        if (entryOffset + 12 > data.Length) break;

                        int tag = ReadInt16(data, entryOffset, littleEndian);
                        if (tag != 0x0112) continue;

                        int type = ReadInt16(data, entryOffset + 2, littleEndian);
                        int count = ReadInt32(data, entryOffset + 4, littleEndian);
                        if (type != 3 || count != 1) return false;

                        orientation = ReadInt16(data, entryOffset + 8, littleEndian);
                        return orientation >= 1 && orientation <= 8;
                    }
                }
            }

            index += 2 + segmentLength;
        }

        return false;
    }

    private static int ReadInt16(byte[] data, int offset, bool littleEndian)
    {
        if (offset + 1 >= data.Length) return 0;
        return littleEndian
            ? data[offset] | (data[offset + 1] << 8)
            : (data[offset] << 8) | data[offset + 1];
    }

    private static int ReadInt32(byte[] data, int offset, bool littleEndian)
    {
        if (offset + 3 >= data.Length) return 0;
        if (littleEndian)
        {
            return data[offset]
                | (data[offset + 1] << 8)
                | (data[offset + 2] << 16)
                | (data[offset + 3] << 24);
        }

        return (data[offset] << 24)
            | (data[offset + 1] << 16)
            | (data[offset + 2] << 8)
            | data[offset + 3];
    }

    private static SKCodecOrigin MapExifOrientation(int orientation)
    {
        return orientation switch
        {
            1 => SKCodecOrigin.TopLeft,
            2 => SKCodecOrigin.TopRight,
            3 => SKCodecOrigin.BottomRight,
            4 => SKCodecOrigin.BottomLeft,
            5 => SKCodecOrigin.LeftTop,
            6 => SKCodecOrigin.RightTop,
            7 => SKCodecOrigin.RightBottom,
            8 => SKCodecOrigin.LeftBottom,
            _ => SKCodecOrigin.TopLeft
        };
    }

    public Task<string?> GetPhotoAsync(string tourDate, string tourName, string tourTime)
    {
        try
        {
            var photos = GetPhotosForTourSync(tourDate, tourName, tourTime);
            // Return most recent photo (last in sorted list by timestamp)
            var mostRecent = photos.OrderByDescending(p => p).FirstOrDefault();
            return Task.FromResult(mostRecent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get photo for {TourName} on {TourDate}", tourName, tourDate);
            return Task.FromResult<string?>(null);
        }
    }

    public Task<List<string>> GetPhotosForTourAsync(string tourDate, string tourName, string tourTime)
    {
        try
        {
            var photos = GetPhotosForTourSync(tourDate, tourName, tourTime);
            return Task.FromResult(photos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get photos for {TourName} on {TourDate}", tourName, tourDate);
            return Task.FromResult(new List<string>());
        }
    }

    public Task<List<string>> GetPhotosForDateAsync(string tourDate)
    {
        try
        {
            var sanitizedDate = SanitizeForFilename(tourDate);
            var dateFolder = Path.Combine(_environment.WebRootPath, PhotosFolder, sanitizedDate);
            
            if (!Directory.Exists(dateFolder))
            {
                return Task.FromResult(new List<string>());
            }
            
            var files = Directory.GetFiles(dateFolder, "*.jpg")
                .Concat(Directory.GetFiles(dateFolder, "*.png"))
                .Where(f => !IsThumbnailFile(f))
                .Select(f => $"/{PhotosFolder}/{sanitizedDate}/{Path.GetFileName(f)}")
                .OrderByDescending(f => f)
                .ToList();
            
            return Task.FromResult(files);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get photos for date {TourDate}", tourDate);
            return Task.FromResult(new List<string>());
        }
    }

    public Task<bool> DeletePhotoAsync(string photoPath)
    {
        try
        {
            // Convert relative path to full path
            var relativePath = photoPath.TrimStart('/');
            var fullPath = Path.Combine(_environment.WebRootPath, relativePath);
            var existed = File.Exists(fullPath);
            
            if (existed)
            {
                File.Delete(fullPath);
                _logger.LogInformation("Deleted tour photo: {Path}", photoPath);
            }
            
            DeleteThumbnailForPath(fullPath);
            if (!existed)
            {
                _logger.LogWarning("Photo not found for deletion: {Path}. Treating as deleted for UI sync.", photoPath);
            }
            return Task.FromResult(true); // Return true so UI can sync removal from DB
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete photo: {Path}", photoPath);
            return Task.FromResult(false);
        }
    }

    public async Task<bool> RotatePhotoAsync(string photoPath, int degrees)
    {
        try
        {
            var relativePath = photoPath.TrimStart('/');
            var fullPath = Path.Combine(_environment.WebRootPath, relativePath);

            if (!File.Exists(fullPath))
            {
                _logger.LogWarning("Photo not found for rotation: {Path}", photoPath);
                return false;
            }

            var bytes = File.ReadAllBytes(fullPath);
            using var inputStream = new MemoryStream(bytes);
            using var bitmap = SKBitmap.Decode(inputStream);
            if (bitmap == null)
            {
                _logger.LogError("SkiaSharp failed to decode photo for rotation: {Path}", photoPath);
                return false;
            }

            using var rotated = RotateBitmap(bitmap, degrees);
            using var image = SKImage.FromBitmap(rotated);
            using var outputStream = new MemoryStream();
            image.Encode(SKEncodedImageFormat.Jpeg, 90).SaveTo(outputStream);

            File.WriteAllBytes(fullPath, outputStream.ToArray());
            await GenerateThumbnailFromFileAsync(fullPath);
            _logger.LogInformation("Rotated photo {Path} by {Degrees} degrees", photoPath, degrees);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rotate photo: {Path}", photoPath);
            return false;
        }
    }

    public Task<List<PhotoInfo>> GetAllPhotosAsync()
    {
        try
        {
            var photosFolder = Path.Combine(_environment.WebRootPath, PhotosFolder);
            var result = new List<PhotoInfo>();
            
            if (!Directory.Exists(photosFolder))
            {
                _logger.LogInformation("Photos folder does not exist: {Path}", photosFolder);
                return Task.FromResult(result);
            }
            
            // Get all date folders
            var dateFolders = Directory.GetDirectories(photosFolder);
            _logger.LogInformation("Found {Count} date folders in {Path}", dateFolders.Length, photosFolder);
            
            foreach (var dateFolder in dateFolders)
            {
                var folderName = Path.GetFileName(dateFolder);
                var files = Directory.GetFiles(dateFolder, "*.jpg")
                    .Concat(Directory.GetFiles(dateFolder, "*.png"))
                    .Where(f => !IsThumbnailFile(f))
                    .ToList();
                
                _logger.LogInformation("Found {Count} photos in {Folder}", files.Count, folderName);
                
                foreach (var file in files)
                {
                    var fileName = Path.GetFileName(file);
                    var fileInfo = new FileInfo(file);
                    
                    result.Add(new PhotoInfo
                    {
                        Path = $"/{PhotosFolder}/{folderName}/{fileName}",
                        DateFolder = folderName,
                        Filename = fileName,
                        CreatedDate = fileInfo.CreationTime
                    });
                }
            }
            
            // Sort by creation date, newest first
            result = result.OrderByDescending(p => p.CreatedDate).ToList();
            
            _logger.LogInformation("GetAllPhotosAsync returning {Count} total photos", result.Count);
            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all photos");
            return Task.FromResult(new List<PhotoInfo>());
        }
    }

    private List<string> GetPhotosForTourSync(string tourDate, string tourName, string tourTime)
    {
        var sanitizedDate = SanitizeForFilename(tourDate);
        var sanitizedName = SanitizeForFilename(tourName);
        var sanitizedTime = SanitizeForFilename(tourTime);
        
        var dateFolder = Path.Combine(_environment.WebRootPath, PhotosFolder, sanitizedDate);
        
        if (!Directory.Exists(dateFolder))
        {
            _logger.LogInformation("Date folder does not exist: {DateFolder}", dateFolder);
            return new List<string>();
        }
        
        // Match files starting with tourName_tourTime pattern
        var pattern = $"{sanitizedName}_{sanitizedTime}_*";
        var allFiles = Directory.GetFiles(dateFolder, $"{pattern}.jpg")
            .Concat(Directory.GetFiles(dateFolder, $"{pattern}.png"))
            .Where(f => !IsThumbnailFile(f))
            .ToList();
        
        _logger.LogInformation("Found {Count} photo files matching pattern '{Pattern}' in {DateFolder}", 
            allFiles.Count, pattern, dateFolder);
        
        // Verify each file exists and build relative paths
        var validFiles = new List<string>();
        foreach (var fullPath in allFiles)
        {
            if (File.Exists(fullPath))
            {
                var relativePath = $"/{PhotosFolder}/{sanitizedDate}/{Path.GetFileName(fullPath)}";
                validFiles.Add(relativePath);
                _logger.LogDebug("Valid photo: {Path}", relativePath);
            }
            else
            {
                _logger.LogWarning("Photo file not found (skipping): {Path}", fullPath);
            }
        }
        
        var sortedFiles = validFiles.OrderByDescending(f => f).ToList();
        _logger.LogInformation("Returning {Count} valid photos for tour {TourName} at {TourTime} on {TourDate}", 
            sortedFiles.Count, tourName, tourTime, tourDate);
        
        return sortedFiles;
    }

    private static string SanitizeForFilename(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "unknown";
        
        // Replace common problematic characters
        var sanitized = input
            .Replace(" ", "_")
            .Replace(",", "")      // Remove commas
            .Replace("(", "")      // Remove parentheses
            .Replace(")", "")
            .Replace("[", "")      // Remove brackets
            .Replace("]", "")
            .Replace("{", "")      // Remove braces
            .Replace("}", "")
            .Replace("/", "-")
            .Replace("\\", "-")
            .Replace(":", "-")
            .Replace(";", "-")
            .Replace("*", "")
            .Replace("?", "")
            .Replace("\"", "")
            .Replace("'", "")      // Remove apostrophes
            .Replace("<", "")
            .Replace(">", "")
            .Replace("|", "")
            .Replace("—", "-")     // Em dash
            .Replace("–", "-")     // En dash
            .Replace("&", "and")   // Ampersand
            .Replace("@", "at")    // At symbol
            .Replace("#", "")      // Hash
            .Replace("%", "pct")   // Percent
            .Replace("$", "")      // Dollar
            .Replace("!", "")      // Exclamation
            .Replace("~", "")      // Tilde
            .Replace("`", "")      // Backtick
            .Replace("^", "")      // Caret
            .Replace("=", "-")     // Equals
            .Replace("+", "-")     // Plus
            .Trim('_', '-', '.');  // Trim leading/trailing special chars
        
        // Collapse multiple underscores/dashes into single
        while (sanitized.Contains("__"))
            sanitized = sanitized.Replace("__", "_");
        while (sanitized.Contains("--"))
            sanitized = sanitized.Replace("--", "-");
        
        // Remove any remaining invalid characters
        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var c in invalidChars)
        {
            sanitized = sanitized.Replace(c.ToString(), "");
        }
        
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

    private static bool IsThumbnailFile(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.EndsWith($"{ThumbnailSuffix}.jpg", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetThumbnailPath(string fullPath)
    {
        var directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
        var baseName = Path.GetFileNameWithoutExtension(fullPath);
        if (baseName.EndsWith(ThumbnailSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        return Path.Combine(directory, $"{baseName}{ThumbnailSuffix}.jpg");
    }

    private void QueueThumbnailGeneration(string fullPath)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await GenerateThumbnailFromFileAsync(fullPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background thumbnail generation failed for {Path}", fullPath);
            }
        });
    }

    private void DeleteThumbnailForPath(string fullPath)
    {
        try
        {
            if (IsThumbnailFile(fullPath))
            {
                var directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
                var baseName = Path.GetFileNameWithoutExtension(fullPath);
                if (baseName.EndsWith(ThumbnailSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    baseName = baseName.Substring(0, baseName.Length - ThumbnailSuffix.Length);
                }

                var candidateJpg = Path.Combine(directory, $"{baseName}.jpg");
                var candidatePng = Path.Combine(directory, $"{baseName}.png");
                if (File.Exists(candidateJpg)) File.Delete(candidateJpg);
                if (File.Exists(candidatePng)) File.Delete(candidatePng);
                return;
            }

            var thumbPath = GetThumbnailPath(fullPath);
            if (File.Exists(thumbPath))
            {
                File.Delete(thumbPath);
                _logger.LogInformation("Deleted thumbnail: {Path}", thumbPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete thumbnail for {Path}", fullPath);
        }
    }

    private async Task GenerateThumbnailFromFileAsync(string fullPath)
    {
        try
        {
            if (!File.Exists(fullPath))
            {
                _logger.LogWarning("Thumbnail generation skipped. File not found: {Path}", fullPath);
                return;
            }

            if (IsThumbnailFile(fullPath))
            {
                return;
            }

            var thumbPath = GetThumbnailPath(fullPath);
            var bytes = await File.ReadAllBytesAsync(fullPath);
            if (bytes.Length == 0)
            {
                _logger.LogWarning("Thumbnail generation skipped. Empty file: {Path}", fullPath);
                return;
            }

            SKCodecOrigin exifOrigin = SKCodecOrigin.TopLeft;
            using (var codecStream = new MemoryStream(bytes))
            using (var codec = SKCodec.Create(codecStream))
            {
                if (codec != null)
                {
                    exifOrigin = codec.Origin;
                }
            }

            if (exifOrigin == SKCodecOrigin.TopLeft && TryReadExifOrientation(bytes, out var exifOrientationValue))
            {
                var mapped = MapExifOrientation(exifOrientationValue);
                if (mapped != SKCodecOrigin.TopLeft)
                {
                    exifOrigin = mapped;
                }
            }

            using var decodeStream = new MemoryStream(bytes);
            using var decodedBitmap = SKBitmap.Decode(decodeStream);
            if (decodedBitmap == null)
            {
                _logger.LogWarning("Thumbnail generation failed to decode image: {Path}", fullPath);
                return;
            }

            SKBitmap? orientedBitmap = null;
            SKBitmap? resizedBitmap = null;
            try
            {
                if (exifOrigin != SKCodecOrigin.TopLeft)
                {
                    orientedBitmap = ApplyExifOrientation(decodedBitmap, exifOrigin);
                }

                var source = orientedBitmap ?? decodedBitmap;
                var maxDim = Math.Max(source.Width, source.Height);
                var scale = maxDim > ThumbnailMaxDimension
                    ? (float)ThumbnailMaxDimension / maxDim
                    : 1f;

                var thumbWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
                var thumbHeight = Math.Max(1, (int)Math.Round(source.Height * scale));

                if (scale < 1f)
                {
                    resizedBitmap = source.Resize(new SKImageInfo(thumbWidth, thumbHeight), SKFilterQuality.Medium);
                    if (resizedBitmap == null)
                    {
                        resizedBitmap = new SKBitmap(thumbWidth, thumbHeight);
                        using var canvas = new SKCanvas(resizedBitmap);
                        canvas.DrawBitmap(source, new SKRect(0, 0, thumbWidth, thumbHeight));
                    }
                }

                var finalBitmap = resizedBitmap ?? source;
                using var image = SKImage.FromBitmap(finalBitmap);
                using var outputStream = new MemoryStream();
                image.Encode(SKEncodedImageFormat.Jpeg, ThumbnailJpegQuality).SaveTo(outputStream);
                await File.WriteAllBytesAsync(thumbPath, outputStream.ToArray());

                _logger.LogInformation(
                    "Generated thumbnail: {ThumbPath} ({Width}x{Height}) from {SourcePath}",
                    thumbPath,
                    finalBitmap.Width,
                    finalBitmap.Height,
                    fullPath);
            }
            finally
            {
                resizedBitmap?.Dispose();
                orientedBitmap?.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate thumbnail for {Path}", fullPath);
        }
    }

    private static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return ".jpg";
        }

        var trimmed = extension.Trim().ToLowerInvariant();
        if (!trimmed.StartsWith("."))
        {
            trimmed = "." + trimmed;
        }

        if (trimmed == ".jpeg")
        {
            trimmed = ".jpg";
        }

        return trimmed == ".jpg" || trimmed == ".png"
            ? trimmed
            : ".jpg";
    }

    private static SKBitmap RotateBitmap(SKBitmap bitmap, int degrees)
    {
        var normalized = ((degrees % 360) + 360) % 360;
        if (normalized == 0)
        {
            return bitmap.Copy();
        }

        int width = bitmap.Width;
        int height = bitmap.Height;
        int newWidth = (normalized == 180) ? width : height;
        int newHeight = (normalized == 180) ? height : width;

        var rotated = new SKBitmap(newWidth, newHeight);
        using var canvas = new SKCanvas(rotated);

        switch (normalized)
        {
            case 90:
                canvas.Translate(newWidth, 0);
                canvas.RotateDegrees(90);
                break;
            case 180:
                canvas.Translate(newWidth, newHeight);
                canvas.RotateDegrees(180);
                break;
            case 270:
                canvas.Translate(0, newHeight);
                canvas.RotateDegrees(270);
                break;
        }

        canvas.DrawBitmap(bitmap, 0, 0);
        canvas.Flush();
        return rotated;
    }
}
