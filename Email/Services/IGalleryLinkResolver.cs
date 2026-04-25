using System;
using System.Threading;
using System.Threading.Tasks;
using Email.Models.Reports;

namespace Email.Services
{
    public interface IGalleryLinkResolver
    {
        Task<GalleryResolutionInfo> ResolveAsync(
            DateTime tourDate,
            string? tourName,
            string? tourTime,
            string? vendorName,
            CancellationToken ct = default);
    }
}
