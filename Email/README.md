# CityShuffles Guides - Email Project

Sanitized developer handoff project for third-party implementation work.

## Current Delivery Focus
- Primary focus is stabilizing the standalone photo upload flow at `/tour-photos`.
- Known issue: SignalR/circuit timeouts during or after upload, most visible on iPhones, occasionally on Android, and less common on desktop.
- We need support for up to 12 photos per upload session.
- Photos must remain full resolution (often ~5 MB to 12 MB each).
- SignalR diagnostics route exists to troubleshoot disconnects/timeouts: `/signalr-diagnostics`.
- Rollout plan: finish and validate standalone uploader first, then move/integrate that flow into Guide Calendar.

## Current State (April 2026)
- This repo is the destination project migrated from `WhatsAppBusiness/Email`.
- It is actively trimmed for external development (non-essential internal tooling removed).
- SQL Server connectivity is intentionally kept.
- External service credentials are intentionally blanked in config by default.

## Tech Stack
- .NET 8
- ASP.NET Core + Blazor Server
- SQL Server (via `Microsoft.Data.SqlClient` + Dapper)
- Syncfusion Blazor + Bootstrap/Bootswatch

## Quick Start
1. Prereqs:
- .NET 8 SDK
- SQL access to the configured dev database

2. From repo root (`cityshufflesguidesproject`):
```powershell
dotnet restore .\Email.sln
dotnet build .\Email\Email.csproj
```

3. Run app:
```powershell
dotnet run --project .\Email\Email.csproj
```

4. Open in browser:
- `http://localhost:5282` (or URL shown by `dotnet run`)

## Login and Auth
- Login page route: `/`
- POST login endpoint: `/login`
- Logout endpoint: `/logout`
- Cookie auth configured in `Program.cs`.
- Guide users are redirected to `/guide-report` after login.

## Configuration and Secrets
Primary config file:
- `Email/appsettings.json`

### Intentionally present
- `ConnectionStrings:AutomaticGmailSqlServer` is kept for dev DB connectivity.

### Intentionally blanked for handoff
- `Gmail:*`
- `Textbelt:*`
- `Checkfront:*` (including OAuth and V4 keys)
- `GoogleCalendar:*`
- `LocalApiAuth:TextApiKey`

Do not commit live service credentials. Prefer local overrides (`appsettings.Development.json`, environment variables, or user-secrets).

## Intentional Removals / Disabled Areas
The following internal/testing surfaces were removed or disabled for handoff:
- Collect UI and collection control buttons in dashboard/mobile flow
- Checkfront test/manual pages and related test services
- QA/admin links/pages:
  - Tour Setup QA
  - Tour Tree Coverage QA
  - AllToursLink Parity QA
  - School Tour Intake
- Sample email credential docs under `Email_Docs/sample_mails`
- Service-account JSON credential files for calendar integrations

Reference: `Email/Email_Docs/4_24_26_sanitization_changes.md`

## Core Routes in Current Nav
- `/tour-management-dashboard` - Dashboard
- `/guide-report` - Guide calendar/reporting flow
- `/guide-lite` - GuideLite static shell
- `/tour-managment` - Tours
- `/guide-schedule` - Schedule
- `/staff` - Guides
- `/vendors` - Vendors
- `/messages` - Messages
- `/reports-list` - Reports
- `/reports-bookings` - Bookings
- `/tour-photos` - Gallery
- `/vcard-export` - vCard export
- `/signalr-diagnostics` - SignalR diagnostics page

## Tour Photos (Current Behavior)
- Main route: `/tour-photos`
- Includes Blazor upload flow and camera capture modal.
- Uploader has visible `Upload Photos` action in the component.
- Target behavior is reliable upload of up to 12 high-resolution images per session.
- Camera permission failures now show user-facing warnings instead of raw technical errors.
- Upload backend endpoints are under `TourPhotoUploadController` (`/tour-photos/*`).

## Repository Layout
```text
Email/
  Program.cs
  Components/
  Controllers/
  Models/
  Services/
  Pages/
  Calendar/
  Hubs/
  wwwroot/
Email.Test/
Email_Docs/
```

## Testing
```powershell
dotnet test .\Email.Test\Email.Test.csproj
```

## Docs for New Devs
- `Email/Email_Docs/4_24_26_dev_migration.md`
- `Email/Email_Docs/4_24_26_sanitization_changes.md`
- `Email/Email_Docs/email_project_separation_plan_2026-04-19.md`
- `Email/Email_Docs/gallery4_18.md`
- `Email/Email_Docs/sqlserver_schema.txt`
- `Email/Email_Docs/sqlserver_schema_4_22.txt`

## Git History Note
History was rewritten during sanitization handoff. If you have an older clone from before this cleanup, re-clone the repository to avoid stale history containing removed artifacts.
