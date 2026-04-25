# Gallery / Photo Upload (as of 2026-04-18)

This doc describes the tour-photo "gallery" system: which pages/routes are involved, what data is stored in the DB, and the technical upload flow (including how SignalR relates).

## Pages / Routes Used

- `GET /guide-report`
  - Blazor Server page: `Email/Components/Pages/GuideReportPage.razor`
  - Shows "Tour Photos" preview via `<TourPhotoGallery Photos="ReportPhotos" ... />`.
  - "Upload photos" button navigates to the external uploader via `GetMobileUploaderUrl()`.

- `GET /mobile-photo-upload.html`
  - Static ASP.NET Core-served page: `Email/wwwroot/mobile-photo-upload.html`
  - Client logic: `Email/wwwroot/js/mobile-photo-uploader.js`
  - Uses query string (`tourDate`, `tourName`, `tourTime`, `returnUrl`) and a "Back" link to return to `/guide-report`.

- `GET /tour-photos`
  - Blazor Server "Tour Photos" management page: `Email/Components/Pages/TourPhotos.razor`
  - Uses `Email/Components/Tours/TourPhotoComponent.razor` for uploading/management UI.
  - This is the closest thing to the "original integrated uploader UI" (in-app page, not the external HTML).

## HTTP Endpoints (Uploader API)

Controller: `Email/Controllers/TourPhotoUploadController.cs` (`[Route("tour-photos")]`)

- `POST /tour-photos/upload-alt`
  - Multipart form upload.
  - Form fields: `tourDate`, `tourName`, `tourTime`, and one or more `files`.
  - Saves each file via `ITourPhotoService` (raw stream for larger files, otherwise normal save/re-encode).

- `GET /tour-photos/list?tourDate=...&tourName=...&tourTime=...`
  - Returns the current list of saved photo paths for that tour (relative `/tour-photos/...` URLs).
  - Used by `mobile-photo-uploader.js` to refresh the gallery preview on the upload page.

- `POST /tour-photos/sync-report`
  - JSON body: `{ tourDate: "yyyy-MM-dd", tourName: "...", tourTime: "..." }`
  - Reads current filesystem photos for the tour and updates the report's DB `ImagePaths` field via `IGuideReportService.UpsertReportImagePathsAsync(...)`.

## Filesystem Storage

Service: `Email/Services/TourPhotoService.cs`

- Photos are stored under `wwwroot/tour-photos/{tourDate}/...`
  - Returned paths are relative URLs like `/tour-photos/2026-04-18/My_Tour_11-00_...jpg`.
- The service also generates thumbnails (suffix `_thumb`) asynchronously.
- Most uploads are re-encoded to JPEG for consistency/compatibility; raw-stream save is used for large files to reduce CPU/EXIF work during upload.

## DB Fields Used

Table: `dbo.TourReports` (see `Email/GuideReport_Migration.sql`)

Primary identification in code (logical key):
- `TourDate` (date)
- `TourName` (nvarchar)
- `TourTime` (nvarchar)

Gallery-related fields:
- `ImagePaths` (nvarchar(max))
  - Stored as a comma-separated list of relative photo URLs (same strings returned by `ITourPhotoService.GetPhotosForTourAsync`).
  - Updated by:
    - `Email/Controllers/TourPhotoUploadController.cs` (`POST /tour-photos/sync-report`)
    - `Email/Components/Pages/GuideReportPage.razor` (auto-sync and manual refresh)
    - `Email/Services/GuideReportService.cs` (`UpsertReportImagePathsAsync`)
- `PublicId` (nvarchar(50))
  - Used to build shareable static gallery page URLs.
  - If `UpsertReportImagePathsAsync` has to create a new report row, it generates a new `PublicId`.
- `IsSubmitted` / `SubmittedAt`
- `GuideReportPage.razor` uses `IsSubmitted` as the "gate" for showing/copying share links.

## Static Gallery HTML (Share Links)

- Generation happens on report submit:
  - `Email/Components/Pages/GuideReportPage.razor` calls `StaticGalleryGeneratorService.GenerateAndSaveHtmlAsync(Report)` in `SendReport()`.
- Output files are written to `wwwroot/tour-gallery/`:
  - `GetGalleryUrl(...)` builds URLs like `{PublicGallery:BaseUrl}/tour-gallery/{PublicId}.html`
  - Vendor-specific variants are `{PublicId}_{SanitizedVendor}.html`
  - Generator logic lives in `Email/Services/StaticGalleryGeneratorService.cs`.

## How Upload Works (Technical Flow)

### A) Typical guide flow (from `/guide-report`)

1. User clicks "Upload photos" on `GuideReportPage.razor` -> navigates to:
   - `/mobile-photo-upload.html?tourDate=...&tourName=...&tourTime=...&returnUrl=...`
   - The `returnUrl` points back to `/guide-report?...&photoReturn=1`.
2. On `/mobile-photo-upload.html`:
   - `mobile-photo-uploader.js` collects selected files into a queue.
   - Optional rotation happens client-side (canvas → new JPEG blob) before upload.
3. Upload:
   - For each queued image: `XMLHttpRequest` multipart POST to `/tour-photos/upload-alt`.
4. After uploading:
   - Client calls `POST /tour-photos/sync-report` to upsert `dbo.TourReports.ImagePaths`.
5. Return:
   - User clicks "Back" -> goes to `/guide-report?...&photoReturn=1`.
   - `GuideReportPage.razor` reads `photoReturn=1` and auto-runs a "sync photos" step (`SyncReportPhotosAsync`) to refresh UI + re-upsert `ImagePaths`.

### B) Admin/management flow (from `/tour-photos`)

- Uses Blazor components to manage photos and preview; still ultimately relies on:
  - `ITourPhotoService` for filesystem reads/writes/deletes
  - `UpsertReportImagePathsAsync(...)` when syncing report fields

*** THIS IS MOST COMMON ON MOBILE DEVICES.
## SignalR Notes (Why the External Uploader Exists)

- `/guide-report` is Blazor Server, so UI state rides on a SignalR circuit (WebSockets).
- Large post-upload work (thumbnailing / refreshing photos / saving report state) can contribute to circuit disconnects (historical issue tracked in `Email/Email_Docs/2026-02-21_upload_disconnect_plan.md`).  
- The external uploader (`/mobile-photo-upload.html`) uses plain HTTP requests (no Blazor circuit), which reduces the chance that uploads "white screen" the guide UI.

Diagnostics:
- `GET /signalr-diagnostics` page: `Email/Pages/SignalRDiagnostics.razor`
- Controlled by config: `SignalRDiagnostics:Enabled` in `Email/appsettings*.json`
