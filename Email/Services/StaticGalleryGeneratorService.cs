using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Email.Models.Reports;
using System.Text.Json;
using System.Linq;
using System.Collections.Generic;
using System.Text;

namespace Email.Services
{
    /// <summary>
    /// Created: 1/29/2026
    /// Generates static HTML gallery files for public viewing.
    /// </summary>
    public class StaticGalleryGeneratorService
    {
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _config;
        private readonly IPublicGalleryService _galleryService;
        private readonly IToursApiService _toursService;
        private readonly ITourLinkService _tourLinkService;

        public StaticGalleryGeneratorService(IWebHostEnvironment env, IConfiguration config, 
            IPublicGalleryService galleryService, IToursApiService toursService, ITourLinkService tourLinkService)
        {
            _env = env;
            _config = config;
            _galleryService = galleryService;
            _toursService = toursService;
            _tourLinkService = tourLinkService;
        }

        public async Task<string> GenerateAndSaveHtmlAsync(TourReport report)
        {
            if (report == null || string.IsNullOrWhiteSpace(report.PublicId)) return "";

            // 1. Get Settings & Data
            var settings = await _galleryService.GetSettingsAsync();
            var displayTourName = NormalizeTourNameForDisplay(report.TourName);
            var baseHeaderHtml = settings?.HeaderText?.Replace("{TourName}", displayTourName)
                             ?? $"<h1 class='display-6'>Thanks for joining us!</h1><p class='lead'>Here are the photos from your <strong>{displayTourName}</strong> tour.</p>";
            
            // 2. Identify Vendors
            // We need to generate a page for EACH vendor present in the walkers list, plus a default one.
            var vendors = report.Walkers
                .Select(w => w.VendorName)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Always ensure we have at least one pass for the "Default" (no specific vendor suffix)
            // If vendors list is empty, we just do one default pass.
            // We will generate:
            // 1. {PublicId}.html (Default - uses first vendor found in Tours definition or just generic)
            // 2. {PublicId}_{SanitizedVendor}.html (For each specific vendor)
            
            // Let's determine the "Default" vendor from the Tour definition for the main file
            string defaultUrlVendor = "";
            try 
            {
                var tours = await _toursService.GetToursAsync();
                var tour = tours.FirstOrDefault(t =>
                    t.TourName.Equals(report.TourName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(NormalizeTourNameForDisplay(t.TourName), displayTourName, StringComparison.OrdinalIgnoreCase));
                if (tour != null && !string.IsNullOrWhiteSpace(tour.VendorNames))
                {
                    defaultUrlVendor = tour.VendorNames.Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
                }
            }
            catch {}
            
            // Add the default pass (represented by null or empty vendor for the file suffix)
            var tasks = new List<Task>();
            
            // Generation logic extracted to local function or loop
            // We'll generate the main one (defaultUrlVendor) and then specific ones for each walker vendor.
            
            var vendorsToGenerate = new HashSet<string>(vendors, StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(defaultUrlVendor)) vendorsToGenerate.Add(defaultUrlVendor);

            // Returns the Base URL of the DEFAULT file
            string mainUrl = "";

            // Helper to sanitize vendor for filename
            string Sanitize(string v) => System.Text.RegularExpressions.Regex.Replace(v, "[^a-zA-Z0-9]", "");

            // GENERATE DEFAULT PAGE (Standard PublicId.html)
            // Uses defaultUrlVendor (from Tour definition) if available, or just empty.
            mainUrl = await GenerateSinglePageAsync(report, baseHeaderHtml, defaultUrlVendor, ""); 

            // GENERATE VENDOR SPECIFIC PAGES
            foreach (var v in vendorsToGenerate)
            {
                var suffix = Sanitize(v);
                if (!string.IsNullOrEmpty(suffix))
                {
                    await GenerateSinglePageAsync(report, baseHeaderHtml, v, "_" + suffix);
                }
            }

            return mainUrl;
        }

        private async Task<string> GenerateSinglePageAsync(TourReport report, string headerHtml, string vendorName, string fileSuffix)
        {
            // Fetch Vendor Links (vendor-specific rules)
            var vendorLinks = new List<TourLink>();
            var isDefaultPage = string.IsNullOrEmpty(fileSuffix);
            var displayTourName = NormalizeTourNameForDisplay(report.TourName);
            var (footerVendors, footerVendorQueries) = ResolveFooterVendors(vendorName, isDefaultPage);
            var footerHeading = BuildFooterHeading(footerVendors, isDefaultPage);
            var showVendorLabels = footerVendors.Count > 1;
            if (footerVendorQueries.Count > 0)
            {
                try
                {
                    vendorLinks = await LoadVendorLinksAsync(footerVendorQueries, displayTourName);
                }
                catch {}
            }

            // Build Photos HTML
            var photosHtml = new StringBuilder();
            var photos = new List<string>();
            if (!string.IsNullOrWhiteSpace(report.ImagePaths))
            {
                photos = report.ImagePaths
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .ToList();
            }

            if (photos.Count == 0)
            {
                photosHtml.AppendLine("<div class='text-center text-muted py-5'><p>No photos available yet.</p></div>");
            }
            else if (photos.Count == 1)
            {
                var photo = photos[0];
                var thumb = BuildThumbnailUrl(photo);
                photosHtml.AppendLine($@"
                    <div class='row g-3'>
                        <div class='col-12 col-md-8 col-lg-6 mx-auto'>
                            <div class='card border-0 shadow-sm h-100 gallery-card'>
                                 <button type='button' class='gallery-photo-trigger' data-photo-index='0' aria-label='View full size photo'>
                                     <img src='{thumb}' 
                                          class='card-img-top rounded' 
                                          alt='Tour Photo' 
                                          loading='lazy'
                                          onerror=""this.onerror=null;this.src='{photo}';""
                                          style='object-fit: cover; height: 360px; width: 100%;'>
                                 </button>
                                 <div class='gallery-overlay'>
                                     <button type='button' class='btn btn-light btn-sm rounded-circle photo-action-btn' data-photo-index='0' title='View full size' aria-label='View full size photo'>
                                         <i class='bi bi-arrows-fullscreen'></i>
                                     </button>
                                 </div>
                            </div>
                        </div>
                    </div>");
            }
            else
            {
                var carouselId = $"galleryCarousel_{SanitizeCarouselId(report.PublicId + fileSuffix)}";
                photosHtml.AppendLine($@"
                    <div class='carousel-wrapper'>
                        <div id='{carouselId}' class='carousel slide' data-bs-touch='true' data-bs-ride='false'>
                            <div class='carousel-indicators'>");

                for (var i = 0; i < photos.Count; i++)
                {
                    var activeClass = i == 0 ? "active" : "";
                    var ariaCurrent = i == 0 ? "true" : "false";
                    photosHtml.AppendLine($@"
                                <button type='button' data-bs-target='#{carouselId}' data-bs-slide-to='{i}' class='{activeClass}' aria-current='{ariaCurrent}' aria-label='Slide {i + 1}'></button>");
                }

                photosHtml.AppendLine(@"
                            </div>
                            <div class='carousel-inner'>");

                for (var i = 0; i < photos.Count; i++)
                {
                    var photo = photos[i];
                    var activeClass = i == 0 ? " active" : "";
                    photosHtml.AppendLine($@"
                                <div class='carousel-item{activeClass}'>
                                    <div class='carousel-image-wrapper'>
                                        <button type='button' class='gallery-photo-trigger d-block w-100 border-0 p-0 bg-transparent' data-photo-index='{i}' aria-label='View full size photo {i + 1}'>
                                            <img src='{photo}'
                                                 class='d-block w-100'
                                                 alt='Tour Photo'
                                                 loading='lazy'>
                                        </button>
                                        <div class='carousel-overlay'>
                                            <button type='button' class='btn btn-light btn-sm rounded-circle photo-action-btn' data-photo-index='{i}' title='View full size' aria-label='View full size photo {i + 1}'>
                                                <i class='bi bi-arrows-fullscreen'></i>
                                            </button>
                                        </div>
                                        <button type='button' class='btn btn-success btn-sm rounded-circle carousel-share-btn' data-inline-share-index='{i}' title='Share or download full-resolution photo' aria-label='Share or download full-resolution photo'>
                                            <i class='bi bi-share-fill'></i>
                                        </button>
                                    </div>
                                </div>");
                }

                photosHtml.AppendLine($@"
                            </div>
                            <button class='carousel-control-prev' type='button' data-bs-target='#{carouselId}' data-bs-slide='prev' aria-label='Previous'>
                                <span class='carousel-control-prev-icon' aria-hidden='true'></span>
                                <span class='visually-hidden'>Previous</span>
                            </button>
                            <button class='carousel-control-next' type='button' data-bs-target='#{carouselId}' data-bs-slide='next' aria-label='Next'>
                                <span class='carousel-control-next-icon' aria-hidden='true'></span>
                                <span class='visually-hidden'>Next</span>
                            </button>
                        </div>
                    </div>
                    <div class='thumb-grid' aria-label='Photo thumbnails'>");

                for (var i = 0; i < photos.Count; i++)
                {
                    var photo = photos[i];
                    var thumb = BuildThumbnailUrl(photo);
                    var activeClass = i == 0 ? " active" : "";
                    photosHtml.AppendLine($@"
                        <button type='button' class='thumb-btn{activeClass}' data-bs-target='#{carouselId}' data-bs-slide-to='{i}' aria-label='Go to photo {i + 1}'>
                            <img src='{thumb}'
                                 alt='Tour photo thumbnail'
                                 loading='lazy'
                                 onerror=""this.onerror=null;this.src='{photo}';"">
                        </button>");
                }

                photosHtml.AppendLine(@"
                    </div>");
            }

            // Build Footer Links HTML
            // 2026-02-15: Temporarily disable footer tour list display.
            var footerHtml = new StringBuilder();
            var photoUrlsJson = JsonSerializer.Serialize(photos);

            // Construct Full HTML
            var template = $@"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='utf-8' />
    <meta name='viewport' content='width=device-width, initial-scale=1.0' />
    <title>Tour Photos - City Shuffles</title>
    <!-- Local Bootstrap -->
    <link rel='stylesheet' href='/css/bootstrap/bootstrap.min.css' />
    <link href='https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.min.css' rel='stylesheet' />
    <style>
        body {{ background-color: #f8f9fa; }}
        .gallery-container {{ 
            max-width: 1200px; margin: 0 auto; min-height: 100vh; 
            background-color: #fff; box-shadow: 0 0 15px rgba(0,0,0,0.05); 
        }}
        @media (prefers-color-scheme: dark) {{
            body {{ background-color: #121212; color: #e0e0e0; }}
            .gallery-container {{ background-color: #1e1e1e; box-shadow: 0 0 15px rgba(0,0,0,0.5); }}
            .card {{ background-color: #2c2c2c; color: #fff; }}
            .text-muted {{ color: #adb5bd !important; }}
            .border-top {{ border-color: #495057 !important; }}
        }}
        
        /* Gallery Card Styles */
        .gallery-card {{ position: relative; overflow: hidden; }}
        .gallery-card:hover .gallery-overlay {{ opacity: 1; }}
        .gallery-card:focus-within .gallery-overlay {{ opacity: 1; }}
        .gallery-photo-trigger {{
            border: none;
            background: transparent;
            padding: 0;
            width: 100%;
            display: block;
            cursor: zoom-in;
        }}
        .gallery-photo-trigger:focus-visible {{
            outline: 2px solid #0d6efd;
            outline-offset: 2px;
        }}
        .gallery-overlay {{
            position: absolute; inset: 0;
            background: linear-gradient(to bottom, rgba(0,0,0,0.35), rgba(0,0,0,0) 60%);
            display: flex; justify-content: flex-end; align-items: flex-start;
            padding: 10px;
            opacity: 0; transition: opacity 0.2s ease;
            pointer-events: none; 
            z-index: 2;
        }}
        .gallery-overlay .photo-action-btn {{ pointer-events: auto; }}
        @media (hover: none) {{
            .gallery-overlay {{ opacity: 1; background: linear-gradient(to bottom, rgba(0,0,0,0.25), rgba(0,0,0,0) 70%); }}
        }}

        .gallery-section {{ margin-bottom: 1.5rem; }}
        .carousel-wrapper {{ max-width: 980px; margin: 0 auto; }}
        .carousel-image-wrapper {{
            position: relative;
            border-radius: 0.75rem;
            overflow: hidden;
            box-shadow: 0 4px 16px rgba(0,0,0,0.2);
        }}
        .carousel-image-wrapper img {{
            width: 100%;
            height: auto;
            max-height: 70vh;
            object-fit: cover;
        }}
        .carousel-overlay {{
            position: absolute; inset: 0;
            background: linear-gradient(to bottom, rgba(0,0,0,0.35), rgba(0,0,0,0) 60%);
            display: flex; justify-content: flex-end; align-items: flex-start;
            padding: 10px;
            opacity: 0; transition: opacity 0.2s ease;
            pointer-events: none; 
            z-index: 2;
        }}
        .carousel-overlay .photo-action-btn {{ pointer-events: auto; }}
        .carousel-image-wrapper:hover .carousel-overlay {{ opacity: 1; }}
        .carousel:hover .carousel-overlay {{ opacity: 1; }}
        .carousel:focus-within .carousel-overlay {{ opacity: 1; }}
        .carousel-control-prev,
        .carousel-control-next {{ z-index: 1; }}
        .carousel-share-btn {{
            position: absolute;
            right: 0.7rem;
            bottom: 0.7rem;
            width: 2.4rem;
            height: 2.4rem;
            padding: 0;
            display: inline-flex;
            align-items: center;
            justify-content: center;
            z-index: 3;
            box-shadow: 0 4px 12px rgba(0,0,0,0.25);
        }}
        .carousel-share-btn i {{
            font-size: 1rem;
            line-height: 1;
        }}
        @media (hover: none) {{
            .carousel-overlay {{ opacity: 1; background: linear-gradient(to bottom, rgba(0,0,0,0.25), rgba(0,0,0,0) 70%); }}
        }}

        .thumb-grid {{
            margin-top: 1rem;
            display: grid;
            grid-template-columns: repeat(auto-fill, minmax(110px, 1fr));
            gap: 0.75rem;
            max-width: 980px;
            margin-left: auto;
            margin-right: auto;
        }}
        .thumb-btn {{
            border: none;
            padding: 0;
            background: transparent;
            border-radius: 0.5rem;
            overflow: hidden;
            box-shadow: 0 2px 8px rgba(0,0,0,0.15);
            transition: transform 0.15s ease, box-shadow 0.15s ease;
        }}
        .thumb-btn:hover,
        .thumb-btn:focus-visible {{
            transform: translateY(-2px);
            box-shadow: 0 4px 14px rgba(0,0,0,0.2);
            outline: none;
        }}
        .thumb-btn.active {{
            box-shadow: 0 0 0 2px #0d6efd, 0 4px 14px rgba(13,110,253,0.35);
        }}
        .thumb-btn img {{
            width: 100%;
            height: 90px;
            object-fit: cover;
            display: block;
        }}

        /* Social + Podcast Links */
        .social-links,
        .podcast-links {{
            display: flex;
            flex-wrap: wrap;
            justify-content: center;
            gap: 0.75rem 1.5rem;
        }}
        .social-link,
        .podcast-link {{
            text-decoration: none;
            color: inherit;
            display: inline-flex;
            align-items: center;
            gap: 0.5rem;
            font-size: 0.95rem;
        }}
        .social-link i,
        .podcast-link i {{
            font-size: 1.4rem;
        }}
        .social-link:hover,
        .podcast-link:hover {{
            color: #0d6efd;
        }}

        .other-tours-table {{
            font-size: 1rem;
            color: inherit;
            --bs-table-color: inherit;
        }}
        .other-tours-table th,
        .other-tours-table td {{
            border: none !important;
            color: inherit;
        }}

        .photo-viewer {{
            position: fixed;
            inset: 0;
            z-index: 1055;
            display: flex;
            align-items: center;
            justify-content: center;
            padding: 1rem;
        }}
        .photo-viewer[hidden] {{
            display: none !important;
        }}
        .photo-viewer-backdrop {{
            position: absolute;
            inset: 0;
            background: rgba(0, 0, 0, 0.88);
        }}
        .photo-viewer-dialog {{
            position: relative;
            max-width: 96vw;
            max-height: 94vh;
            width: auto;
            display: flex;
            flex-direction: column;
            align-items: center;
            gap: 0.75rem;
            z-index: 1;
        }}
        .photo-viewer-image {{
            max-width: 100%;
            max-height: 80vh;
            object-fit: contain;
            border-radius: 0.5rem;
            box-shadow: 0 8px 32px rgba(0,0,0,0.35);
            background: #111;
        }}
        .photo-viewer-close {{
            position: absolute;
            top: -0.25rem;
            right: -0.25rem;
            transform: translate(50%, -50%);
            width: 2.2rem;
            height: 2.2rem;
            border-radius: 999px;
            border: none;
            background: rgba(255,255,255,0.92);
            color: #111;
            display: inline-flex;
            align-items: center;
            justify-content: center;
            font-size: 1rem;
            line-height: 1;
            cursor: pointer;
        }}
        .photo-viewer-toolbar {{
            text-align: center;
            color: #fff;
        }}
        .photo-viewer-hint {{
            background: rgba(0,0,0,0.45);
            border-radius: 0.4rem;
            padding: 0.35rem 0.55rem;
            display: inline-block;
        }}
    </style>
</head>
<body>
    <div class='gallery-container py-4 px-3 px-md-5'>
        <header class='mb-4 mb-md-5 text-center'>
            {headerHtml}
            <div class='text-muted small mt-2'>
                <i class='bi bi-calendar-event me-1'></i>{report.TourDate.ToLongDateString()} <span class='mx-2'>&bull;</span> <i class='bi bi-clock me-1'></i>{report.TourTime}
            </div>
            
            <!-- Social & Podcast Links -->
            <div class='mt-3'>
                <div class='social-links'>
                    <a href='https://instagram.com/jon.noto' target='_blank' rel='noopener' class='social-link' title='Follow us on Instagram'>
                        <i class='bi bi-instagram'></i>
                        <span>Follow us on Instagram</span>
                    </a>
                    <a href='https://youtube.com/jonnoto' target='_blank' rel='noopener' class='social-link' title='Follow us on YouTube'>
                        <i class='bi bi-youtube'></i>
                        <span>Follow us on YouTube</span>
                    </a>
                </div>
                <div class='podcast-links mt-2'>
                    <a href='https://open.spotify.com/episode/5vm4bZ4xS0vzUv9bwoYLwB?si=8xRx561xQFOlOLQUscCzDg' target='_blank' rel='noopener' class='podcast-link' title=""View Jon Noto's 9/11 podcast on Spotify"">
                        <i class='bi bi-spotify text-success'></i>
                        <span>Jon Noto's 9/11 podcast (Spotify)</span>
                    </a>
                    <a href='https://youtu.be/A_j5sVvG6CI?si=X3juc-44jxw0TzPw' target='_blank' rel='noopener' class='podcast-link' title=""View Jon Noto's 9/11 podcast on YouTube"">
                        <i class='bi bi-youtube text-danger'></i>
                        <span>Jon Noto's 9/11 podcast (YouTube)</span>
                    </a>
                </div>
            </div>
        </header>
        
        <div class='gallery-section'>
            {photosHtml}
        </div>

        <div id='photoViewer' class='photo-viewer' hidden aria-hidden='true'>
            <div class='photo-viewer-backdrop' data-photo-close='1'></div>
            <div class='photo-viewer-dialog' role='dialog' aria-modal='true' aria-label='Tour photo viewer'>
                <button type='button' class='photo-viewer-close' data-photo-close='1' aria-label='Close photo viewer'>
                    <i class='bi bi-x-lg' aria-hidden='true'></i>
                </button>
                <img id='photoViewerImage' class='photo-viewer-image' alt='Tour photo full size' />
                <div class='photo-viewer-toolbar'>
                    <button id='photoViewerSave' type='button' class='btn btn-light btn-sm'>
                        <i class='bi bi-share me-1'></i>Save to Phone
                    </button>
                    <div id='photoViewerHint' class='photo-viewer-hint small mt-2' hidden>Press and hold image, then Save Image/Add to Photos.</div>
                </div>
            </div>
        </div>

        {footerHtml}

        <footer class='text-center py-4 mt-5 text-muted border-top'>
            <small>&copy; {DateTime.Now.Year} - City Shuffles Guides</small>
        </footer>
    </div>
    <script src='https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/js/bootstrap.bundle.min.js'></script>
    <script>
        (() => {{
            const photoUrls = {photoUrlsJson};
            const viewer = document.getElementById('photoViewer');
            const viewerImage = document.getElementById('photoViewerImage');
            const viewerHint = document.getElementById('photoViewerHint');
            const saveButton = document.getElementById('photoViewerSave');
            let currentPhotoIndex = -1;

            const hideHint = () => {{
                if (!viewerHint) return;
                viewerHint.hidden = true;
                viewerHint.textContent = '';
            }};

            const showHint = (text) => {{
                if (!viewerHint) return;
                viewerHint.textContent = text;
                viewerHint.hidden = false;
            }};

            const openViewer = (index) => {{
                if (!Array.isArray(photoUrls) || index < 0 || index >= photoUrls.length || !viewer || !viewerImage) {{
                    return;
                }}

                currentPhotoIndex = index;
                viewerImage.src = photoUrls[index];
                viewer.hidden = false;
                viewer.setAttribute('aria-hidden', 'false');
                hideHint();
                document.body.style.overflow = 'hidden';
            }};

            const closeViewer = () => {{
                if (!viewer) return;
                viewer.hidden = true;
                viewer.setAttribute('aria-hidden', 'true');
                currentPhotoIndex = -1;
                hideHint();
                document.body.style.overflow = '';
            }};

            const fileNameFromUrl = (url) => {{
                try {{
                    const parsed = new URL(url, window.location.origin);
                    const segments = parsed.pathname.split('/');
                    const raw = segments.pop() || 'tour-photo.jpg';
                    const decoded = decodeURIComponent(raw);
                    return decoded || 'tour-photo.jpg';
                }} catch {{
                    return 'tour-photo.jpg';
                }}
            }};

            const toAbsoluteUrl = (url) => {{
                try {{
                    return new URL(url, window.location.origin).href;
                }} catch {{
                    return url;
                }}
            }};

            const isUserCancelledShare = (err) => {{
                if (!err) return false;
                const name = (err.name || '').toLowerCase();
                const message = (err.message || '').toLowerCase();
                return name === 'aborterror' || message.includes('cancel');
            }};

            const triggerDownloadFromUrl = (url) => {{
                const absoluteUrl = toAbsoluteUrl(url);
                const link = document.createElement('a');
                link.href = absoluteUrl;
                link.download = fileNameFromUrl(absoluteUrl);
                link.rel = 'noopener';
                link.style.display = 'none';
                document.body.appendChild(link);
                link.click();
                setTimeout(() => {{
                    if (document.body.contains(link)) {{
                        document.body.removeChild(link);
                    }}
                }}, 0);
            }};

            const saveCurrentPhoto = async () => {{
                if (currentPhotoIndex < 0 || currentPhotoIndex >= photoUrls.length) {{
                    return;
                }}

                hideHint();
                const photoUrl = photoUrls[currentPhotoIndex];

                try {{
                    const supportsShareFiles =
                        !!(navigator.share && navigator.canShare && window.File && window.fetch);

                    if (supportsShareFiles) {{
                        const response = await fetch(photoUrl);
                        if (!response.ok) {{
                            throw new Error(`HTTP ${{response.status}}`);
                        }}

                        const blob = await response.blob();
                        const fileName = fileNameFromUrl(photoUrl);
                        const file = new File([blob], fileName, {{
                            type: blob.type || 'image/jpeg'
                        }});

                        if (navigator.canShare({{ files: [file] }})) {{
                            await navigator.share({{
                                files: [file],
                                title: 'Tour photo'
                            }});
                            showHint('If needed, choose Save Image/Add to Photos in the share options.');
                            return;
                        }}
                    }}
                }} catch (err) {{
                    if (isUserCancelledShare(err)) {{
                        return;
                    }}
                }}

                showHint('Press and hold image, then Save Image/Add to Photos.');
            }};

            const shareOrDownloadByIndex = async (index) => {{
                if (index < 0 || index >= photoUrls.length) {{
                    return;
                }}

                const photoUrl = photoUrls[index];
                const absoluteUrl = toAbsoluteUrl(photoUrl);
                try {{
                    const supportsShareFiles =
                        !!(navigator.share && navigator.canShare && window.File && window.fetch);

                    if (supportsShareFiles) {{
                        const response = await fetch(absoluteUrl);
                        if (!response.ok) {{
                            throw new Error(`HTTP ${{response.status}}`);
                        }}

                        const blob = await response.blob();
                        const fileName = fileNameFromUrl(absoluteUrl);
                        const file = new File([blob], fileName, {{
                            type: blob.type || 'image/jpeg'
                        }});

                        if (navigator.canShare({{ files: [file] }})) {{
                            await navigator.share({{
                                files: [file],
                                title: 'Tour photo',
                                url: absoluteUrl
                            }});
                            return;
                        }}
                    }}

                    if (navigator.share) {{
                        await navigator.share({{
                            title: 'Tour photo',
                            url: absoluteUrl
                        }});
                        return;
                    }}
                }} catch (err) {{
                    if (isUserCancelledShare(err)) {{
                        return;
                    }}
                }}

                triggerDownloadFromUrl(absoluteUrl);
            }};

            document.querySelectorAll('[data-photo-index]').forEach((element) => {{
                element.addEventListener('click', (event) => {{
                    event.preventDefault();
                    const value = event.currentTarget.getAttribute('data-photo-index');
                    const index = Number.parseInt(value || '', 10);
                    if (Number.isInteger(index)) {{
                        openViewer(index);
                    }}
                }});
            }});

            document.querySelectorAll('[data-photo-close]').forEach((element) => {{
                element.addEventListener('click', (event) => {{
                    event.preventDefault();
                    closeViewer();
                }});
            }});

            document.querySelectorAll('[data-inline-share-index]').forEach((element) => {{
                element.addEventListener('click', (event) => {{
                    event.preventDefault();
                    event.stopPropagation();
                    const value = event.currentTarget.getAttribute('data-inline-share-index');
                    const index = Number.parseInt(value || '', 10);
                    if (Number.isInteger(index)) {{
                        void shareOrDownloadByIndex(index);
                    }}
                }});
            }});

            document.addEventListener('keydown', (event) => {{
                if (event.key === 'Escape' && viewer && !viewer.hidden) {{
                    closeViewer();
                }}
            }});

            if (saveButton) {{
                saveButton.addEventListener('click', (event) => {{
                    event.preventDefault();
                    void saveCurrentPhoto();
                }});
            }}

            const carousels = document.querySelectorAll('.carousel');
            if (!carousels.length) return;

            carousels.forEach((carousel) => {{
                const thumbGrid = carousel.parentElement?.querySelector('.thumb-grid');
                if (!thumbGrid) return;
                const thumbs = Array.from(thumbGrid.querySelectorAll('.thumb-btn'));
                if (!thumbs.length) return;

                const setActive = (index) => {{
                    thumbs.forEach((btn, i) => btn.classList.toggle('active', i === index));
                }};

                carousel.addEventListener('slid.bs.carousel', (event) => {{
                    if (typeof event.to === 'number') {{
                        setActive(event.to);
                    }}
                }});
            }});
        }})();
    </script>
</body>
</html>";

            // Save File
            var folder = Path.Combine(_env.WebRootPath, "tour-gallery");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            var fileName = $"{report.PublicId}{fileSuffix}.html";
            var filePath = Path.Combine(folder, fileName);
            await File.WriteAllTextAsync(filePath, template);

            // Return URL
            var baseUrl = _config["PublicGallery:BaseUrl"] ?? "";
            return $"{baseUrl}/tour-gallery/{fileName}";
        }

        private static readonly string[] GuruWalkVendorAliases = new[] { "GuruWalk" };
        private static readonly string[] FreeTourVendorAliases = new[] { "FreeTour", "Freetour.com", "Freetour", "Free Tour", "FreeTour.com" };

        private static (List<string> DisplayNames, List<string> QueryNames) ResolveFooterVendors(string vendorName, bool isDefaultPage)
        {
            var displayNames = new List<string>();
            var queryNames = new List<string>();

            void AddVendor(string displayName, IEnumerable<string> aliases)
            {
                displayNames.Add(displayName);
                queryNames.AddRange(aliases);
            }

            if (isDefaultPage)
            {
                AddVendor("GuruWalk", GuruWalkVendorAliases);
                AddVendor("FreeTour", FreeTourVendorAliases);
                return (displayNames, queryNames);
            }

            if (string.IsNullOrWhiteSpace(vendorName))
            {
                return (displayNames, queryNames);
            }

            var key = NormalizeVendorKey(vendorName);
            if (key.Contains("guruwalk"))
            {
                AddVendor("GuruWalk", GuruWalkVendorAliases);
                return (displayNames, queryNames);
            }

            if (key.Contains("freetour"))
            {
                AddVendor("FreeTour", FreeTourVendorAliases);
                return (displayNames, queryNames);
            }

            if (key.Contains("walkon"))
            {
                AddVendor("GuruWalk", GuruWalkVendorAliases);
                AddVendor("FreeTour", FreeTourVendorAliases);
                return (displayNames, queryNames);
            }

            // Other vendors: keep it to GuruWalk + FreeTour to limit links
            AddVendor("GuruWalk", GuruWalkVendorAliases);
            AddVendor("FreeTour", FreeTourVendorAliases);
            return (displayNames, queryNames);
        }

        private static string BuildFooterHeading(List<string> vendorDisplayNames, bool isDefaultPage)
        {
            if (vendorDisplayNames == null || vendorDisplayNames.Count == 0)
            {
                return string.Empty;
            }

            if (vendorDisplayNames.Count == 1)
            {
                return isDefaultPage
                    ? $"See Our Other Tours on {vendorDisplayNames[0]}"
                    : $"Our Other Tours On {vendorDisplayNames[0]}";
            }

            if (vendorDisplayNames.Count == 2)
            {
                var vendors = $"{vendorDisplayNames[0]} & {vendorDisplayNames[1]}";
                return isDefaultPage
                    ? $"See Our Other Tours on {vendors}"
                    : $"Our Other Tours On {vendors}";
            }

            return isDefaultPage ? "See Our Other Tours" : "Our Other Tours On";
        }

        private async Task<List<TourLink>> LoadVendorLinksAsync(IEnumerable<string> vendors, string? currentTourName)
        {
            var links = new List<TourLink>();
            foreach (var vendor in vendors.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var vendorLinks = await _tourLinkService.GetLinksByVendorAsync(vendor);
                    if (vendorLinks != null && vendorLinks.Count > 0)
                    {
                        links.AddRange(vendorLinks);
                    }
                }
                catch {}
            }

            var current = NormalizeTourNameForDisplay(currentTourName);
            return links
                .Where(l => !string.IsNullOrWhiteSpace(l.TourName))
                .Where(l => !string.Equals(NormalizeTourNameForDisplay(l.TourName), current, StringComparison.OrdinalIgnoreCase))
                .GroupBy(l => $"{NormalizeVendorKey(l.Vendor)}|{NormalizeTourNameForDisplay(l.TourName)}|{l.TourLinkUrl?.Trim()}")
                .Select(g => g.First())
                .OrderBy(l => NormalizeVendorKey(l.Vendor))
                .ThenBy(l => NormalizeTourNameForDisplay(l.TourName))
                .ToList();
        }

        private static string NormalizeVendorKey(string? vendor)
        {
            if (string.IsNullOrWhiteSpace(vendor)) return string.Empty;
            var cleaned = System.Text.RegularExpressions.Regex.Replace(vendor, "[^a-zA-Z0-9]", "");
            return cleaned.ToLowerInvariant();
        }

        private static string BuildThumbnailUrl(string photoUrl)
        {
            if (string.IsNullOrWhiteSpace(photoUrl)) return photoUrl;

            var lastSlash = photoUrl.LastIndexOf('/');
            var directory = lastSlash >= 0 ? photoUrl[..lastSlash] : string.Empty;
            var fileName = lastSlash >= 0 ? photoUrl[(lastSlash + 1)..] : photoUrl;

            if (fileName.EndsWith("_thumb.jpg", StringComparison.OrdinalIgnoreCase))
            {
                return photoUrl;
            }

            var dotIndex = fileName.LastIndexOf('.');
            var baseName = dotIndex > 0 ? fileName[..dotIndex] : fileName;
            var thumbFile = $"{baseName}_thumb.jpg";

            return string.IsNullOrEmpty(directory)
                ? thumbFile
                : $"{directory}/{thumbFile}";
        }

        private static string SanitizeCarouselId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return "gallery";
            return System.Text.RegularExpressions.Regex.Replace(id, "[^a-zA-Z0-9_-]", "");
        }

        private static string NormalizeVendorDisplayName(string? vendor)
        {
            var key = NormalizeVendorKey(vendor);
            if (key.Contains("guruwalk")) return "GuruWalk";
            if (key.Contains("freetour")) return "FreeTour";
            if (key.Contains("walkon")) return "WalkOn";
            return vendor?.Trim() ?? string.Empty;
        }

        private static string NormalizeTourNameForDisplay(string? tourName)
        {
            return TourNameDisplaySanitizer.Normalize(tourName);
        }

        private static readonly (int Value, string Label)[] DayOptions = new[]
        {
            (0, "Sunday"),
            (1, "Monday"),
            (2, "Tuesday"),
            (3, "Wednesday"),
            (4, "Thursday"),
            (5, "Friday"),
            (6, "Saturday")
        };

        private static Dictionary<int, List<TourLink>> GroupLinksByDay(List<TourLink> links)
        {
            var result = DayOptions.ToDictionary(d => d.Value, _ => new List<TourLink>());

            foreach (var link in links)
            {
                if (!link.TourDay.HasValue) continue;
                if (!result.TryGetValue(link.TourDay.Value, out var list))
                {
                    list = new List<TourLink>();
                    result[link.TourDay.Value] = list;
                }
                list.Add(link);
            }

            foreach (var day in result.Keys.ToList())
            {
                result[day] = result[day]
                    .OrderBy(l => ParseTourTime(l.TourTime))
                    .ThenBy(l => l.TourName)
                    .ToList();
            }

            return result;
        }

        private static DateTime ParseTourTime(string? time)
        {
            if (string.IsNullOrWhiteSpace(time)) return DateTime.MinValue;
            if (DateTime.TryParse(time, out var parsed))
            {
                return parsed;
            }
            return DateTime.MinValue;
        }
    }
}
