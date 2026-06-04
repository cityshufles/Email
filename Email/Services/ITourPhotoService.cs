// 2025-12-21 - Tour photo service interface for managing tour group photos
namespace Email.Services;

/// <summary>
/// Service for managing tour group photos - upload, storage, and retrieval.
/// </summary>
public interface ITourPhotoService
{
    /// <summary>
    /// Saves a photo to the filesystem for a specific tour.
    /// </summary>
    /// <param name="imageData">Base64-encoded image data</param>
    /// <param name="tourDate">Tour date (e.g., "2025-12-21")</param>
    /// <param name="tourName">Tour name</param>
    /// <param name="tourTime">Tour time (e.g., "10:00 AM")</param>
    /// <param name="fileExtension">File extension (e.g., "jpg", "png")</param>
    /// <returns>The saved photo path relative to wwwroot, or null if failed</returns>
    Task<string?> SavePhotoAsync(string imageData, string tourDate, string tourName, string tourTime, string fileExtension = "jpg");

    /// <summary>
    /// Saves a photo to the filesystem for a specific tour using raw bytes.
    /// </summary>
    /// <param name="imageBytes">Raw image bytes</param>
    /// <param name="tourDate">Tour date (e.g., "2025-12-21")</param>
    /// <param name="tourName">Tour name</param>
    /// <param name="tourTime">Tour time (e.g., "10:00 AM")</param>
    /// <param name="fileExtension">File extension (e.g., "jpg", "png")</param>
    /// <returns>The saved photo path relative to wwwroot, or null if failed</returns>
    Task<string?> SavePhotoAsync(byte[] imageBytes, string tourDate, string tourName, string tourTime, string fileExtension = "jpg");

    /// <summary>
    /// Saves a photo to the filesystem for a specific tour using a stream (no re-encode).
    /// </summary>
    /// <param name="inputStream">Input image stream</param>
    /// <param name="tourDate">Tour date (e.g., "2025-12-21")</param>
    /// <param name="tourName">Tour name</param>
    /// <param name="tourTime">Tour time (e.g., "10:00 AM")</param>
    /// <param name="fileExtension">File extension (e.g., "jpg", "png")</param>
    /// <returns>The saved photo path relative to wwwroot, or null if failed</returns>
    Task<string?> SavePhotoRawAsync(Stream inputStream, string tourDate, string tourName, string tourTime, string fileExtension = "jpg");

    /// <summary>
    /// Gets the most recent photo for a specific tour.
    /// </summary>
    /// <param name="tourDate">Tour date</param>
    /// <param name="tourName">Tour name</param>
    /// <param name="tourTime">Tour time</param>
    /// <returns>Photo path relative to wwwroot, or null if not found</returns>
    Task<string?> GetPhotoAsync(string tourDate, string tourName, string tourTime);

    /// <summary>
    /// Gets all photos for a specific tour.
    /// </summary>
    /// <param name="tourDate">Tour date</param>
    /// <param name="tourName">Tour name</param>
    /// <param name="tourTime">Tour time</param>
    /// <returns>List of photo paths relative to wwwroot</returns>
    Task<List<string>> GetPhotosForTourAsync(string tourDate, string tourName, string tourTime);

    /// <summary>
    /// Gets all photos for a specific date.
    /// </summary>
    /// <param name="tourDate">Tour date</param>
    /// <returns>List of photo paths relative to wwwroot</returns>
    Task<List<string>> GetPhotosForDateAsync(string tourDate);

    /// <summary>
    /// Deletes a photo from the filesystem.
    /// </summary>
    /// <param name="photoPath">Photo path relative to wwwroot</param>
    /// <returns>True if deleted successfully</returns>
    Task<bool> DeletePhotoAsync(string photoPath);

    /// <summary>
    /// Rotates an existing photo by the specified degrees (clockwise).
    /// </summary>
    /// <param name="photoPath">Photo path relative to wwwroot</param>
    /// <param name="degrees">Rotation degrees (e.g., 90, -90, 180)</param>
    /// <returns>True if rotated successfully</returns>
    Task<bool> RotatePhotoAsync(string photoPath, int degrees);

    /// <summary>
    /// Gets all photos across all dates.
    /// </summary>
    /// <returns>List of photo paths relative to wwwroot with folder info</returns>
    Task<List<PhotoInfo>> GetAllPhotosAsync();
}

/// <summary>
/// Photo information including path and date folder.
/// </summary>
public class PhotoInfo
{
    public string Path { get; set; } = string.Empty;
    public string DateFolder { get; set; } = string.Empty;
    public string Filename { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public DateTime? CreatedDate { get; set; }
    public bool IsVideo => Extension is ".mp4" or ".mov" or ".webm";
}
