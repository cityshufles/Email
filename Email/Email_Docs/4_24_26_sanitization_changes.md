# 4/24/26 Sanitization Changes

## Scope
Prepared destination project for 3rd-party developer handoff while keeping DB connectivity intact.

## Changes Made
- Kept SQL Server connection string unchanged in `appsettings.json` (`ConnectionStrings:AutomaticGmailSqlServer`).
- Blanked Gmail settings in `appsettings.json`:
  - `Gmail:Email`
  - `Gmail:Password`
  - `Gmail:ImapServer`
  - `Gmail:ImapPort` set to `0`
  - `Gmail:EnableSsl` set to `false`
- Blanked Textbelt settings in `appsettings.json`:
  - `Textbelt:ApiKey`
  - `Textbelt:Sender`
- Blanked Checkfront settings in both `appsettings.json` and `appsettings.Development.json`:
  - endpoints (`ApiEndpoint`, `ProductionApiEndpoint`, `DevelopmentApiEndpoint`)
  - `DefaultEndpointMode`
  - API keys/secrets (`ApiKey`, `ApiSecret`, `V4:ApiKey`, `V4:ApiSecret`)
  - OAuth fields (`OAuth2:*`, including `ApplicationName`, `ConsumerKey`, `ConsumerSecret`, `AccessToken`, `RefreshToken`)
  - `Checkfront:CompanyName`
- Blanked Google Calendar settings in `appsettings.json`:
  - `GoogleCalendar:CalendarId`
  - `GoogleCalendar:ServiceAccountKeyPath`
  - `GoogleCalendar:ApplicationName`
  - `GoogleCalendar:UseServiceAccount` remains `false`
- Blanked local text API auth key in `appsettings.json`:
  - `LocalApiAuth:TextApiKey`
- Deleted calendar service-account credential file (no backup):
  - `Config/whatsapp-business-calendar-69f259007ff0.json`
- Deleted sample email docs directory (no backup):
  - `Email_Docs/sample_mails/` (including `cs upwork.txt`, `freetourmodification.txt`, `getyourguide.txt`, `mod parse.txt`)

## Notes
- No backup copies were created.

## Gallery/Uploader Domain + Folder Safeguard (4/25/26)
- Set gallery/uploader base domain for dev handoff to:
  - `http://testcity.w41.wh-2.com`
- Updated config:
  - `appsettings.json` -> `PublicGallery:BaseUrl`
  - `appsettings.Development.json` -> `PublicGallery:BaseUrl`
- Updated Guide Report uploader link generation to use the configured base domain (absolute URL):
  - `Components/Pages/GuideReportPage.razor` (`GetMobileUploaderUrl`)
- Updated gallery link fallback logic to test domain in:
  - `Components/Mobile/MobileTourCard.razor`
  - `Components/Pages/GuideReportPage.razor`
  - `Components/Pages/ReportsListPage.razor`
  - `Components/Tours/TourTreeDisplay_Main.cs`
  - `Components/Tours/TourTreeDisplay_SMS_WhatsApp.cs`
  - `Controllers/GuideLite/GuideLiteController.cs`
  - `Services/GalleryLinkResolver.cs`
- Added upload folder auto-create safeguard:
  - `Services/TourPhotoService.cs` now ensures `wwwroot/tour-photos` exists at startup, and per-date folders continue to be created on save.

## Checkfront Test Surface Cleanup (4/24/26)
- Removed Checkfront test/manual navigation links from `Components/Layout/NavMenu.razor`:
  - `checkfront-test`
  - `checkfront-manual`
- Deleted Checkfront test/manual pages:
  - `Pages/CheckfrontTestPage.razor`
  - `Pages/CheckfrontManualPage.razor`
- Deleted Checkfront test/manual UI components:
  - `Components/Checkfront/CheckfrontTestComponent.razor`
  - `Components/Checkfront/CheckfrontTestComponent.razor.cs`
  - `Components/Checkfront/CheckfrontTestComponent.razor.css`
  - `Components/Checkfront/CheckfrontDbLookupComponent.razor`
  - `Components/Checkfront/CheckfrontViatorBackfillComponent.razor`
  - `Components/Checkfront/CheckfrontViatorBackfillComponent.razor.cs`
  - `Components/Checkfront/CheckfrontManualBookComponent.razor`
  - `Components/Checkfront/CheckfrontManualBookComponent.razor.cs`
- Deleted test-only backend services and DTOs:
  - `Services/CheckfrontDbLookupService.cs`
  - `Services/ViatorInboxBackfillService.cs`
  - `Services/GmailProcessing/LiveEmailReadOnlyFixtureService.cs`
  - `Services/GmailProcessing/LiveEmailReadOnlyLogger.cs`
  - `Services/GmailProcessing/LiveEmailReadOnlyTestService.cs`
  - `Services/GmailProcessing/Dtos/LiveEmailReadOnlyDtos.cs`
- Removed corresponding DI registrations from `Program.cs`.
- Removed unused `LiveEmailReadOnlyTest` config sections from:
  - `appsettings.json`
  - `appsettings.Development.json`

## Collection UI Cleanup (Mobile + Dashboard)
- Removed desktop nav collect button and related run logic from:
  - `Components/Layout/NavMenu.razor`
- Removed dashboard collection controls panel (status/window/collect/process-after):
  - `Pages/TourManagementDashboard.razor`
- Removed collection UI components/routes:
  - `Components/TourEmailCollectionUI/EmailCollectionToolbar.razor`
  - `Components/CollectionV2/GmailCollection.razor` (`/gmail-collection`)
  - `Components/CollectionV2/GmailAutoCollection.razor` (`/gmail-auto-collection`)
- Removed mobile collect button and collection action-sheet/handlers from:
  - `Pages/Mobile/MobileTourManagementDashboard.razor`
  - removed mobile collection inject/use of `GmailCollectionV2Service`, `GmailProcessingV2Service`, and `ITourEmailsService` for collect actions

## QA/Admin Link Cleanup (Requested)
- Removed sidebar links from `Components/Layout/NavMenu.razor`:
  - `tour-setup-qa`
  - `tour-tree-coverage-qa`
  - `all-tours-link-parity-qa`
  - `school-tour-intake`
- Deleted route wrapper pages:
  - `Pages/Admin/TourSetupQaReview.razor`
  - `Pages/Admin/TourTreeCoverageQaOverview.razor`
  - `Pages/Admin/AllToursLinkParityQa.razor`
  - `Pages/Admin/SchoolTourIntake.razor`
- Deleted backing admin components:
  - `Components/Pages/Admin/TourSetupQaReview.razor`
  - `Components/Pages/Admin/TourTreeCoverageQaOverview.razor`
  - `Components/Pages/Admin/AllToursLinkParityQa.razor`
  - `Components/Pages/Admin/SchoolTourIntake.razor`
