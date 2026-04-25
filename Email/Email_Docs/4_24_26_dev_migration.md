# 4/24/26 Dev Migration (Starter)

## Scope
- Migrate `Email` project only from `WhatsAppBusiness` (C:\Users\daves\source\repos\WhatsAppBusiness\Email\Email.csproj) into `cityshufflesguidesproject` (C:\Users\daves\source\repos\cityshufflesguidesproject\Email\Email.csproj).
- Prepare this project for 3rd-party developer handoff.

## Initial Focus
- ultimate goal is to Replace/remove external ASP.NET Core gallery uploader usage in favor of Blazor gallery components that should be existing in the project already.
We are not going to remove it yet, it is a job for the other developers.

- Add guide-facing features (guide knowledge base and related guide tools).
- Hook up the new SQL Server database for this project.
-allow 3rd party devs to access the site but not have access to the google or other service accounts such as checkfront etc.
We should start with getting a skeleton site up and running with login working for admins and guides.
reference C:\Users\daves\source\repos\WhatsAppBusiness\Email\Email.csproj for reference to working production soultion.

## Security/Sanitization Track
- Remove/redact walker PII before external handoff.
- Remove/redact Gmail account settings and other sensitive config values.

## DB Config Reference (masked)
- `Data Source=tcp:s31.winhost.com;Initial Catalog=DB_179500_autogmail;User ID=DB_179500_autogmail_user;Password=******;Integrated Security=False;TrustServerCertificate=true;`

## 4/24/26 Completed (Dev Login Prep)
- Investigated old auth system in `WhatsAppBusiness/Email` and confirmed destination auth flow matches source pattern:
  - login checks `dbo.Users` by username/email,
  - password is plain string match,
  - guides redirect to `/guide-report`,
  - admin/manager access is controlled by role/permissions.
- Updated destination `appsettings.json` `AutomaticGmailSqlServer` to point to `s31 / DB_179500_autogmail`.
- Queried `s31` and confirmed `dbo.Users` existed but had no rows before seeding.
- Seeded dev-only users for login testing (no real production guide account migration):
  - `dave` / `dave_2` / `Admin`
  - `jon` / `jon_2` / `Guide`
  - `user` / `user_2` / `Admin`
  - `nyguide1` / `nyguide1_2` / `Guide`
  - `nyguide2` / `nyguide2_2` / `Guide`
- Added missing `/access-denied` page in destination project so denied policy routes resolve cleanly.
- Sanitized destination config for third-party handoff:
  - kept SQL connection available for dev DB access,
  - replaced Gmail/Checkfront/Textbelt/GoogleCalendar/Local API and Syncfusion credential values with redacted placeholders,
  - switched Checkfront endpoints in appsettings files to disabled placeholder URLs.
- Locked down Google Calendar integration for third-party handoff:
  - removed `Config/cityshufflescalendar-9b0eeccb02df.json` from destination project,
  - updated calendar service initialization to respect `GoogleCalendar:UseServiceAccount` and `GoogleCalendar:ServiceAccountKeyPath` instead of hardcoded credential filename.
- Build + basic auth smoke checks passed:
  - `dave` login redirects to `/mobile-tours`
  - `jon` login redirects to `/guide-report`
