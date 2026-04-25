using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Email.Models.Reports;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Email.Services
{
    public sealed class GalleryLinkResolver : IGalleryLinkResolver
    {
        private readonly IGuideReportService _guideReportService;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<GalleryLinkResolver> _logger;

        public GalleryLinkResolver(
            IGuideReportService guideReportService,
            IConfiguration configuration,
            IWebHostEnvironment environment,
            ILogger<GalleryLinkResolver> logger)
        {
            _guideReportService = guideReportService;
            _configuration = configuration;
            _environment = environment;
            _logger = logger;
        }

        public async Task<GalleryResolutionInfo> ResolveAsync(
            DateTime tourDate,
            string? tourName,
            string? tourTime,
            string? vendorName,
            CancellationToken ct = default)
        {
            var result = new GalleryResolutionInfo();
            if (string.IsNullOrWhiteSpace(tourName))
            {
                return result;
            }

            try
            {
                var normalizedTourName = tourName.Trim();
                var normalizedTourTime = string.IsNullOrWhiteSpace(tourTime) ? string.Empty : tourTime.Trim();
                var report = await _guideReportService.GetReportAsync(tourDate.Date, normalizedTourName, normalizedTourTime);
                if (report == null || string.IsNullOrWhiteSpace(report.PublicId))
                {
                    return result;
                }

                var baseUrl = _configuration["PublicGallery:BaseUrl"] ?? "http://testcity.w41.wh-2.com";
                var publicId = report.PublicId.Trim();
                result.PublicId = publicId;

                var vendorSuffix = Regex.Replace(vendorName ?? string.Empty, "[^a-zA-Z0-9]", string.Empty);
                if (!string.IsNullOrWhiteSpace(vendorSuffix))
                {
                    var vendorFile = $"{publicId}_{vendorSuffix}.html";
                    if (GalleryFileExists(vendorFile))
                    {
                        result.Found = true;
                        result.Url = $"{baseUrl}/tour-gallery/{vendorFile}";
                        result.Source = "vendor_static";
                        return result;
                    }
                }

                var defaultFile = $"{publicId}.html";
                if (GalleryFileExists(defaultFile))
                {
                    result.Found = true;
                    result.Url = $"{baseUrl}/tour-gallery/{defaultFile}";
                    result.Source = "default_static";
                    return result;
                }

                result.Found = true;
                result.Url = $"{baseUrl}/tour-gallery/{publicId}";
                result.Source = "dynamic_route";
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Gallery resolution failed for {TourName} {TourDate:d} {TourTime}", tourName, tourDate, tourTime);
                return result;
            }
        }

        private bool GalleryFileExists(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            var webRoot = _environment.WebRootPath;
            if (string.IsNullOrWhiteSpace(webRoot))
            {
                return false;
            }

            var fullPath = Path.Combine(webRoot, "tour-gallery", fileName);
            return File.Exists(fullPath);
        }
    }
}
