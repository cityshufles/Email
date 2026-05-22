using System;
using System.Collections.Generic;

namespace Email.Models.Resources
{
    public sealed class ResourceItem
    {
        public int Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public string FileType { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public string RelativePath { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int UploadedByUserId { get; set; }
        public string UploadedByName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public List<int> TourIds { get; set; } = new();
        public List<string> TourNames { get; set; } = new();

        public string FileSizeDisplay
        {
            get
            {
                if (FileSizeBytes < 1024) return $"{FileSizeBytes} B";
                if (FileSizeBytes < 1024 * 1024) return $"{FileSizeBytes / 1024.0:F1} KB";
                if (FileSizeBytes < 1024 * 1024 * 1024) return $"{FileSizeBytes / (1024.0 * 1024):F1} MB";
                return $"{FileSizeBytes / (1024.0 * 1024 * 1024):F2} GB";
            }
        }

        public bool IsPhoto => FileType == "photo";
        public bool IsVideo => FileType == "video";
        public bool IsPdf => FileType == "pdf";
    }
}
