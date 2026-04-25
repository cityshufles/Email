# Email Project Separation Plan (standalone-ready)

Date: 2026-04-19  
Scope: Make `Email/Email.csproj` independently runnable/buildable, then move it into an "Email-only" solution (and optionally a separate repo) without relying on other projects in `WhatsAppBusiness.sln`.

## Goals

- Open/build/run `Email/Email.csproj` without loading unrelated projects.
- Capture *all* runtime config + required files so the Email app is fully functional standalone.
- Create a new solution containing only the Email app (and the minimal supporting projects/tests).

## Non-goals (for this phase)

- Refactoring features or redesigning the app.
- Changing database schema or external integrations.

## Current Email app "shape" (what we're separating)

- Project: `Email/Email.csproj` (`net8.0`, ASP.NET Core / Blazor Server).
- Tests: `Email.Test/Email.Test.csproj` (references `Email/Email.csproj`).
- Key local config files:
  - `Email/appsettings.json`
  - `Email/appsettings.Development.json`
  - `Email/Properties/launchSettings.json`
  - `Email/web.config` (IIS)
- Key local runtime files/directories:
  - `Email/Config/cityshufflescalendar-9b0eeccb02df.json` (Google service account key file currently used by code)
  - `Email/Config/whatsapp-business-calendar-69f259007ff0.json` (present; confirm usage)
  - `Email/SqlScripts/`, `Email/*.sql`, `Email/sqlserver_schema.sql` (DB bootstrap / schema references)
  - `Email/wwwroot/` (static assets)
  - `Email/Logs/`, `Email/TestData/` (created/used at runtime in some flows)

## Phase 1 - Stop loading "unneeded projects" immediately (no extraction yet)

This gives fast iteration *today* without touching the main solution structure.

1) Create a Visual Studio Solution Filter (`.slnf`)
- In Visual Studio: open `WhatsAppBusiness.sln` -> **File -> Save As Solution Filter**
- Keep only:
  - `Email` (`Email/Email.csproj`)
  - `Email.Test` (`Email.Test/Email.Test.csproj`)
  - (Optional) `Email.StarterApp` if you still use it
- Commit the `.slnf` so anyone can open the Email subset quickly.

2) Confirm Email builds without building the world
- `dotnet build .\\Email\\Email.csproj`
- `dotnet test .\\Email.Test\\Email.Test.csproj`

## Phase 2 - Config & dependency inventory (make it standalone-safe)

### A) External services Email depends on (from code + config)

1) SQL Server (primary persistence)
- Connection string name used widely: `ConnectionStrings:AutomaticGmailSqlServer`
- Code paths: `Email/Services/**` uses `configuration.GetConnectionString("AutomaticGmailSqlServer")`

2) Gmail IMAP (MailKit)
- Config section: `Gmail:*` (email, app-password, server, port, SSL, timing)

3) Textbelt (SMS)
- Config section: `Textbelt:*` (API key, sender)

4) Checkfront API (booking platform)
- Config section: `Checkfront:*` and `Checkfront:V4:*`
- Also uses `Checkfront:OAuth2:*` for OAuth2 mode + state/tokens
- Request-trigger toggle: `CheckfrontSync:EnableRequestTrigger` (+ interval/window knobs)

5) Google Calendar
- Config section exists: `GoogleCalendar:*`
- **Important:** `Email/Calendar/Services/GoogleCalendarService.cs` currently loads a credential file by *hardcoded filename* in `Email/Config/` (not the `ServiceAccountKeyPath` value).

6) Syncfusion Blazor
- License read from config: `Syncfusion:LicenseKey`

7) Authentication (cookie auth)
- Config section: `Authentication:*` (cookie name, session timeout, RequireHttps)

8) Public gallery URL building
- Config key used in UI: `PublicGallery:BaseUrl` (defaults to `https://cityshufflesguides.com` in code if missing)

9) SignalR/Blazor runtime tuning
- `BlazorServer:*` and `SignalRDiagnostics:*` sections

### B) Secrets handling (must-have for standalone + safer repos)

`Email/appsettings.json` currently contains real secrets (DB credentials, Gmail password, API keys, Syncfusion license key, Checkfront secrets, etc.).

Before extracting to a new repo/solution, decide where secrets live:

- Recommended for local dev:
  - `dotnet user-secrets` (per-developer, not committed)
  - Environment variables (CI/hosting)
- Recommended for servers:
  - Hosting provider "App Settings" (environment variables)
  - Or a secret store (Azure Key Vault/etc.) if you want that complexity

Practical rule: keep `appsettings.json` committed with **placeholders**, and rely on:
- `appsettings.Development.json` (non-secret overrides only)
- user-secrets / env vars for secrets

### C) Filesystem/runtime assumptions to capture

Confirm these exist / are created on startup when running standalone:

- `Email/Logs/` and subfolders (ex: `logs/signalr-diagnostics` from config)
- `Email/TestData/` paths referenced by any "live read only test" code paths
- `Email/Config/*.json` credential files (Google Calendar service account)

### D) Package & platform dependencies to document

- .NET SDK: `.NET 8` (build/run)
- NuGet packages (non-exhaustive): Dapper, Microsoft.Data.SqlClient, MailKit/MimeKit, Google APIs, Syncfusion, SkiaSharp
- Hosting target(s) to support:
  - Local dev: `dotnet run`
  - IIS: `Email/web.config` (if you deploy there)

## Phase 3 - Make a new Email-only solution

Once Phase 2 inventory is captured, create a new solution that only contains what Email needs.

Option A (preferred): new solution file at repo root
- Create `EmailOnly.sln` (or `Email.sln`) with only:
  - `Email/Email.csproj`
  - `Email.Test/Email.Test.csproj`
  - (Optional) any truly-required helper project(s) if they appear later

Commands:

```powershell
dotnet new sln -n EmailOnly
dotnet sln .\\EmailOnly.sln add .\\Email\\Email.csproj
dotnet sln .\\EmailOnly.sln add .\\Email.Test\\Email.Test.csproj
```

Option B: extract to a new repo/folder
- Copy the `Email/` folder and any minimal shared docs/scripts you want.
- Recreate the solution as above in the new repo.

## Phase 4 - Remove unrelated projects from `WhatsAppBusiness.sln` (only after new solution is verified)

Once `EmailOnly.sln` works for all Email work:

- Decide whether `WhatsAppBusiness.sln` remains "monorepo master" or becomes "WhatsApp-only".
- If you truly want it to stop loading extra projects, either:
  - Keep `WhatsAppBusiness.sln` as-is and use `.slnf` files (lowest risk), or
  - Remove projects from `WhatsAppBusiness.sln` and rely on multiple solutions (`EmailOnly.sln`, `WhatsAppOnly.sln`, etc.)

Recommendation: prefer multiple solutions and/or `.slnf` instead of repeatedly editing the same `.sln`.

## Phase 5 - Standalone validation checklist (definition of "fully functional")

Run these from repo root:

Note: If `dotnet build` fails with file-lock errors (ex: cannot copy `Email.dll` / `Email.exe`), stop the running Email app and end any active Visual Studio debugging session for `Email` first.

1) Restore/build/test
- `dotnet restore .\\Email\\Email.csproj`
- `dotnet build .\\Email\\Email.csproj`
- `dotnet test .\\Email.Test\\Email.Test.csproj`

2) Run locally
- `dotnet run --project .\\Email\\Email.csproj`
- Confirm it starts on the expected port(s) from `Email/Properties/launchSettings.json` (currently `http://localhost:5282`)

3) Smoke-check key features (manual)
- Login works (cookie auth configured)
- DB-backed pages load (connection string set)
- Gmail collection/processing flows can connect (if enabled/configured)
- Checkfront API calls succeed (if enabled/configured)
- Google Calendar pages don't error (credential file present)
- Syncfusion components render (license configured; otherwise confirm "degraded but usable" behavior)

## Config map (what must be provided in the standalone environment)

Minimum required for most environments:

- `ConnectionStrings:AutomaticGmailSqlServer`
- `Authentication:*`
- `Syncfusion:LicenseKey` (if you want to avoid license warnings/limits)

Feature-dependent:

- `Gmail:*`
- `Textbelt:*`
- `Checkfront:*`, `Checkfront:V4:*`, `Checkfront:OAuth2:*`
- `GoogleCalendar:*` **and** `Email/Config/<service-account>.json` file(s)
- `PublicGallery:BaseUrl`
- `SignalRDiagnostics:*`, `BlazorServer:*`, `CheckfrontSync:*`

Environment-variable equivalents (ASP.NET Core convention):

- Example: `ConnectionStrings__AutomaticGmailSqlServer`
- Example: `Syncfusion__LicenseKey`
- Example: `Gmail__Password`
