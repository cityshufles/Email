using Email.Components;
using Email.Models;
using Email.Services;
using Email.Services.SignalRDiagnostics;
using Syncfusion.Blazor;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.Configure<SignalRDiagnosticsOptions>(builder.Configuration.GetSection("SignalRDiagnostics"));
builder.Services.AddSingleton<ISignalRDiagnosticsLog, SignalRDiagnosticsLog>();
builder.Services.AddSingleton<SignalRDiagnosticsHubFilter>();
// 2026-03-08: Make Blazor Server reconnect behavior configurable and more resilient for mobile/background tab usage.
var blazorKeepAliveSeconds = Math.Max(builder.Configuration.GetValue<int?>("BlazorServer:KeepAliveSeconds") ?? 10, 5);
var blazorClientTimeoutSeconds = Math.Max(builder.Configuration.GetValue<int?>("BlazorServer:ClientTimeoutSeconds") ?? 900, 60);
var blazorDisconnectedRetentionMinutes = Math.Max(builder.Configuration.GetValue<int?>("BlazorServer:DisconnectedCircuitRetentionMinutes") ?? 20, 1);
var blazorDisconnectedCircuitMaxRetained = Math.Max(builder.Configuration.GetValue<int?>("BlazorServer:DisconnectedCircuitMaxRetained") ?? 300, 1);

var blazorBuilder = builder.Services.AddServerSideBlazor();
blazorBuilder.AddCircuitOptions(opt =>
{
    // Keep disconnected circuits longer so mobile tab/app switching can reconnect without full page reload.
    opt.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(blazorDisconnectedRetentionMinutes);
    opt.DisconnectedCircuitMaxRetained = blazorDisconnectedCircuitMaxRetained;
});
blazorBuilder.AddHubOptions(opt =>
{
    opt.MaximumReceiveMessageSize = 102400000; // ~100MB for large images
    opt.KeepAliveInterval = TimeSpan.FromSeconds(blazorKeepAliveSeconds);
    opt.ClientTimeoutInterval = TimeSpan.FromSeconds(blazorClientTimeoutSeconds);
    opt.AddFilter<SignalRDiagnosticsHubFilter>();
});
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler, BlazorCircuitLoggingHandler>();
builder.Services.AddControllers();
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 200_000_000; // 200MB for alt photo uploads
});

// Authentication configuration
builder.Services.Configure<Email.Models.AuthenticationSettings>(builder.Configuration.GetSection("Authentication"));

// 2026-02-03 - Granular Permissions Policy
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, Email.Services.Auth.PermissionHandler>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CanViewLogs", policy => policy.AddRequirements(new Email.Services.Auth.PermissionRequirement("CanViewLogs")));
    options.AddPolicy("CanExportLogs", policy => policy.AddRequirements(new Email.Services.Auth.PermissionRequirement("CanExportLogs")));
    options.AddPolicy("CanViewCalendar", policy => policy.AddRequirements(new Email.Services.Auth.PermissionRequirement("CanViewCalendar")));
    options.AddPolicy("CanEditCalendar", policy => policy.AddRequirements(new Email.Services.Auth.PermissionRequirement("CanEditCalendar")));
    options.AddPolicy("CanViewSettings", policy => policy.AddRequirements(new Email.Services.Auth.PermissionRequirement("CanViewSettings")));
    options.AddPolicy("CanManageUsers", policy => policy.AddRequirements(new Email.Services.Auth.PermissionRequirement("CanManageUsers")));
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        var authSettings = builder.Configuration.GetSection("Authentication").Get<Email.Models.AuthenticationSettings>() ?? new Email.Models.AuthenticationSettings();
        options.Cookie.Name = authSettings.CookieName;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(authSettings.SessionTimeoutMinutes);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        // 2025-11-29 00:00 UTC - Explicit SameSite for Safari/mobile on HTTP
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = authSettings.RequireHttps ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/access-denied";
    });
builder.Services.AddHttpContextAccessor();

builder.Services.Configure<GmailSettings>(builder.Configuration.GetSection("Gmail"));
builder.Services.Configure<TextBeltSettings>(builder.Configuration.GetSection("Textbelt"));
builder.Services.Configure<LocalApiAuthSettings>(builder.Configuration.GetSection("LocalApiAuth"));
builder.Services.AddScoped<ITourEmailsService, TourEmailsService>();
builder.Services.AddScoped<ITourTreeService, TourTreeService>();
builder.Services.AddScoped<Email.Services.State.TourDashboardCacheState>();
builder.Services.AddScoped<Email.Services.State.MobileTourDashboardCacheState>();
builder.Services.AddScoped<IToastService, ToastService>();
builder.Services.AddScoped<Email.Services.GmailCollection.GmailCollectionV2Service>();
builder.Services.AddScoped<Email.Services.GmailCollection.IGmailCollectionRepository, Email.Services.GmailCollection.SqlServerGmailCollectionRepository>();

// Gmail Processing V2 (service + repositories)
builder.Services.AddScoped<Email.Services.GmailProcessing.GmailProcessingV2Service>();
builder.Services.AddScoped<Email.Services.GmailProcessing.IGmailProcessingRepository, Email.Services.GmailProcessing.SqlServerGmailProcessingRepository>();
builder.Services.AddScoped<Email.Services.GmailProcessing.Repositories.IProcessedEmailRepository, Email.Services.GmailProcessing.Repositories.SqlServerProcessedEmailRepository>();
builder.Services.AddScoped<Email.Services.GmailProcessing.Repositories.IClassificationRepository, Email.Services.GmailProcessing.Repositories.SqlServerClassificationRepository>();
builder.Services.AddScoped<Email.Services.GmailProcessing.Repositories.IBookingRepository, Email.Services.GmailProcessing.Repositories.SqlServerBookingRepository>();
builder.Services.AddScoped<Email.Services.GmailProcessing.Repositories.ICustomerRepository, Email.Services.GmailProcessing.Repositories.SqlServerCustomerRepository>();
builder.Services.AddScoped<Email.Services.GmailProcessing.CheckfrontRecentDbCompareService>();
builder.Services.AddScoped<CheckfrontV4BookingsSqlService>();
builder.Services.AddScoped<CheckfrontV4SnapshotMapper>();
builder.Services.AddScoped<CheckfrontV4TreeMaterializerService>();

// Auth services DI
builder.Services.AddScoped<Email.Services.Auth.IUserRepository, Email.Services.Auth.SqlServerUserRepository>();
builder.Services.AddScoped<Email.Services.Auth.IAuthenticationService, Email.Services.Auth.AuthenticationService>();

// SQL data layer (step 2)
builder.Services.AddSingleton<SqlConnectionFactory>();
builder.Services.AddScoped<IGuidesApiService, GuidesSqlService>();
// 2026-02-09 00:00 UTC - Staff management service
builder.Services.AddScoped<Email.Services.Staff.IStaffService, Email.Services.Staff.StaffSqlService>();
builder.Services.AddScoped<IToursApiService, ToursSqlService>();
builder.Services.AddScoped<ITourGuideDefaultsApiService, TourGuideDefaultsSqlService>();
builder.Services.AddScoped<ITourMessagesApiService, TourMessagesSqlService>();
builder.Services.AddScoped<IVendorsApiService, VendorsSqlService>();
builder.Services.AddScoped<ITourSetupQaService, TourSetupQaSqlService>();
builder.Services.AddScoped<ITourProductionWorkbenchService, TourProductionWorkbenchSqlService>();
builder.Services.AddScoped<ITourTreeCoverageQaService, TourTreeCoverageQaSqlService>();
builder.Services.AddScoped<IAllToursLinkParityQaService, AllToursLinkParityQaService>();
builder.Services.AddScoped<ISchoolTourIntakeService, SchoolTourIntakeSqlService>();
builder.Services.AddScoped<TourDataErrorReportsSqlService>();
// 2026-02-11 - Master Tour Name services
builder.Services.AddScoped<ITourNameMappingService, TourNameMappingSqlService>();
builder.Services.AddScoped<ITourNameNormalizer, TourNameNormalizer>();
builder.Services.AddScoped<IMasterTourService, MasterTourSqlService>();
builder.Services.AddScoped<ITourCatalogService, TourCatalogService>();
builder.Services.AddScoped<IMessageStatusApiService, MessageStatusSqlService>();
builder.Services.AddScoped<MessageTemplateService>();
builder.Services.AddScoped<IBookingsInboxService, BookingsInboxSqlService>();
builder.Services.AddScoped<IGalleryLinkResolver, GalleryLinkResolver>();
// 2025-12-19 00:00 UTC - Centralized vCard export for iPhone/iPad compatibility
builder.Services.AddScoped<VCardExportService>();
// 12/19/2025 5:12 PM (local) | 2025-12-19T17:12:00 - Manual vCard export (no API endpoints)
builder.Services.AddScoped<VCardManualExportService>();
builder.Services.AddScoped<SmtpVCardEmailService>();
// 2025-12-19 00:00 UTC - HTML logging service for vCard download tracking (detailed with user agent, device, timezone)
builder.Services.AddSingleton<VCardLogService>();
builder.Services.AddHttpClient<ITextbeltApiService, TextbeltApiService>();
builder.Services.AddHttpClient<ITextBeltDirectService, TextBeltDirectService>();
// 2026-03-26 - Checkfront API client for vendor booking verification/enrichment
builder.Services.AddScoped<CheckfrontEndpointModeState>();
builder.Services.AddHttpClient<CheckfrontService>();
builder.Services.AddHttpClient<ICheckfrontReadOnlyApi, CheckfrontReadOnlyV4ApiClient>();
builder.Services.AddHttpClient<CheckfrontOAuthService>();
builder.Services.AddSingleton<CheckfrontOAuthStateStore>();
// 2026-03-26 - Request-triggered sync for environments without cron/webjobs.
builder.Services.AddSingleton<Email.Services.Automation.CheckfrontSyncTriggerService>();
// Configure Syncfusion to ignore script isolation (for global script usage)
builder.Services.AddSyncfusionBlazor();

// Calendar Service (Phase 1)
builder.Services.AddScoped<Email.Calendar.Services.ICalendarDataService, Email.Calendar.Services.CalendarSqlDataService>();
builder.Services.AddSingleton<Email.Calendar.Services.IGoogleCalendarService, Email.Calendar.Services.GoogleCalendarService>();

// 2025-12-21 - Tour Photo Service (group photo upload/edit)
builder.Services.AddScoped<ITourPhotoService, TourPhotoService>();
// 2026-01-08 - Tour Guide Assignment Service (per-tour-instance guide assignment)
builder.Services.AddScoped<ITourGuideAssignmentService, TourGuideAssignmentService>();
// 2026-01-09 - Guide Calculator Service (calculate guide owed/earned amounts)
builder.Services.AddScoped<IGuideCalculatorService, GuideCalculatorService>();
// 2026-01-28 - Guide Report Service
builder.Services.AddScoped<IGuideReportService, GuideReportService>();
builder.Services.AddSingleton<IPhotoUploadLog, PhotoUploadLog>();
// 2026-01-29 - Public Gallery Settings
builder.Services.AddScoped<IPublicGalleryService, PublicGalleryService>(); 
builder.Services.AddScoped<ITourLinkService, TourLinkService>();
builder.Services.AddScoped<StaticGalleryGeneratorService>();
var app = builder.Build();
var syncfusionLicenseKey = builder.Configuration["Syncfusion:LicenseKey"];
if (!string.IsNullOrEmpty(syncfusionLicenseKey))
{
    Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(syncfusionLicenseKey);
}
using (var diagnosticsScope = app.Services.CreateScope())
{
    var diagnosticsLog = diagnosticsScope.ServiceProvider.GetRequiredService<ISignalRDiagnosticsLog>();
    await diagnosticsLog.WriteAsync(new SignalRDiagnosticsEntry
    {
        EventType = "BlazorTimeoutConfig",
        Method = "ProgramStartup",
        Route = "/_blazor",
        User = "system",
        Details =
            $"KeepAliveSeconds={blazorKeepAliveSeconds}; ClientTimeoutSeconds={blazorClientTimeoutSeconds}; " +
            $"DisconnectedCircuitRetentionMinutes={blazorDisconnectedRetentionMinutes}; " +
            $"DisconnectedCircuitMaxRetained={blazorDisconnectedCircuitMaxRetained}"
    });
}
// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseRouting();

// 2025-11-29 00:00 UTC - Enforce Lax SameSite for Safari/mobile on HTTP
app.UseCookiePolicy(new CookiePolicyOptions
{
    MinimumSameSitePolicy = SameSiteMode.Lax
});

// 2025-11-22 00:00 UTC - Add authentication/authorization middleware
app.UseAuthentication();
app.UseAuthorization();

// 2026-03-26 - Opportunistic Checkfront sync trigger for shared hosting without schedulers.
var checkfrontRequestTriggerEnabled = builder.Configuration.GetValue<bool?>("CheckfrontSync:EnableRequestTrigger") ?? false;
if (checkfrontRequestTriggerEnabled)
{
    app.Use(async (context, next) =>
    {
        try
        {
            var trigger = context.RequestServices.GetRequiredService<Email.Services.Automation.CheckfrontSyncTriggerService>();
            trigger.TryTriggerForRequest(context.Request.Path.Value);
        }
        catch (Exception ex)
        {
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("CheckfrontSyncTriggerMiddleware");
            logger.LogError(ex, "CheckfrontSyncTriggerMiddleware failed to dispatch sync trigger.");
            Console.WriteLine($"[DEBUG] CheckfrontSyncTriggerMiddleware error: {ex.Message}");
        }

        await next();
    });
}

// 2025-11-22 00:00 UTC - Login/Logout endpoints (form POST)
app.MapPost("/login", async (HttpContext context, Email.Services.Auth.IAuthenticationService authService, Email.Services.Auth.IUserRepository userRepository) =>
{
    var form = await context.Request.ReadFormAsync();
    var username = form["username"].ToString();
    var password = form["password"].ToString();

    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
    {
        return Results.Redirect("/?error=invalid");
    }

    var valid = await authService.LoginAsync(username, password);
    if (!valid)
    {
        return Results.Redirect("/?error=invalid");
    }

    var signedIn = await authService.SignInUserAsync(username);
    if (!signedIn)
    {
        return Results.Redirect("/?error=signin");
    }

    // 2026-03-12 - Redirect guides to Guide Report by default
    var user = await userRepository.GetUserByUsernameAsync(username);
    if (user != null && string.Equals(user.Role, "Guide", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Redirect("/guide-report");
    }

    return Results.Redirect("/mobile-tours");
});

app.MapPost("/logout", async (HttpContext context, Email.Services.Auth.IAuthenticationService authService) =>
{
    await authService.LogoutAsync();
    return Results.Redirect("/");
});

// 2026-03-08 - Authenticated GuideLite shell (static HTML served from same app)
app.MapGet("/guide-lite", (IWebHostEnvironment env) =>
{
    var shellPath = Path.Combine(env.WebRootPath, "guide-lite", "index.html");
    if (!File.Exists(shellPath))
    {
        return Results.NotFound();
    }

    return Results.File(shellPath, "text/html; charset=utf-8");
})
.RequireAuthorization(new AuthorizeAttribute { Roles = "Guide,Admin,Manager" });

// GET /checkfront/oauth/start
// Starts OAuth2 authorize flow and redirects to Checkfront.
app.MapGet("/checkfront/oauth/start", (
    HttpContext context,
    CheckfrontOAuthService oauthService,
    CheckfrontOAuthStateStore stateStore) =>
{
    if (!oauthService.IsOAuthConfigured)
    {
        return Results.BadRequest(new
        {
            Error = "Checkfront OAuth2 is not configured.",
            Hint = "Set Checkfront:OAuth2 ConsumerKey/ConsumerSecret/AuthorizeUrl/TokenUrl in appsettings."
        });
    }

    var callbackUrl = oauthService.ResolveCallbackUrl(context.Request);
    var state = stateStore.Issue();
    var authorizeUrl = oauthService.BuildAuthorizeUrl(callbackUrl, state);
    return Results.Redirect(authorizeUrl);
});

// GET /checkfront/oauth/callback
// Receives authorization code, exchanges for access/refresh token, and stores in memory.
app.MapGet("/checkfront/oauth/callback", async (
    HttpContext context,
    CheckfrontOAuthService oauthService,
    CheckfrontOAuthStateStore stateStore,
    CancellationToken ct) =>
{
    var query = context.Request.Query;
    var callbackUrl = oauthService.ResolveCallbackUrl(context.Request);

    var error = query["error"].ToString();
    if (!string.IsNullOrWhiteSpace(error))
    {
        return Results.BadRequest(new
        {
            Error = error,
            ErrorDescription = query["error_description"].ToString(),
            CallbackUrl = callbackUrl
        });
    }

    var code = query["code"].ToString();
    if (string.IsNullOrWhiteSpace(code))
    {
        return Results.BadRequest(new
        {
            Error = "Missing 'code' query parameter.",
            CallbackUrl = callbackUrl
        });
    }

    var state = query["state"].ToString();
    if (!string.IsNullOrWhiteSpace(state) && !stateStore.TryConsume(state))
    {
        return Results.BadRequest(new
        {
            Error = "Invalid or expired OAuth state.",
            Hint = "Restart flow using /checkfront/oauth/start",
            CallbackUrl = callbackUrl
        });
    }

    var snapshot = await oauthService.ExchangeCodeAsync(code, callbackUrl, ct);
    return Results.Ok(new
    {
        Message = "Checkfront OAuth2 token exchange completed.",
        CallbackUrl = callbackUrl,
        AccessToken = snapshot.AccessToken,
        RefreshToken = snapshot.RefreshToken,
        ExpiresInSeconds = snapshot.ExpiresInSeconds,
        AccessTokenExpiresAtUtc = snapshot.AccessTokenExpiresAtUtc?.ToString("O")
    });
});

// GET /checkfront/oauth/status
// Returns OAuth2 config/token state (token values are masked).
app.MapGet("/checkfront/oauth/status", (CheckfrontOAuthService oauthService) =>
{
    return Results.Ok(oauthService.GetStatus());
});

// POST /checkfront/oauth/refresh
// Manually refreshes token using current refresh token.
app.MapPost("/checkfront/oauth/refresh", async (CheckfrontOAuthService oauthService, CancellationToken ct) =>
{
    var refreshed = await oauthService.RefreshAccessTokenAsync(ct);
    return Results.Ok(new
    {
        Message = "Checkfront OAuth2 token refreshed.",
        AccessToken = refreshed.AccessToken,
        RefreshToken = refreshed.RefreshToken,
        ExpiresInSeconds = refreshed.ExpiresInSeconds,
        AccessTokenExpiresAtUtc = refreshed.AccessTokenExpiresAtUtc?.ToString("O")
    });
});

// === 2025-11-25 16:12 UTC: Automation Endpoints for Gmail Collection/Processing/SMS ===

// POST /automation/gmail/collect
// Body: { window?: string, daysPrior?: int, limit?: int, batchSize?: int, ignoreWatermark?: bool }
// When limit is null, no limit is applied (collects all matching emails)
app.MapPost("/automation/gmail/collect", async (
    Email.Services.Automation.Dtos.AutomationCollectRequest request,
    Email.Services.GmailCollection.GmailCollectionV2Service collectionService,
    CancellationToken ct) =>
{
    var collectionRequest = new Email.Services.GmailCollection.Dtos.CollectionRequestDto
    {
        Window = MapWindowToCollectionWindow(request.Window),
        DaysPrior = request.DaysPrior ?? (request.Window?.Equals("year", StringComparison.OrdinalIgnoreCase) == true ? 365 : null),
        Limit = request.Limit, // null = no limit
        BatchSize = request.BatchSize > 0 ? request.BatchSize : 150,
        IgnoreWatermark = request.IgnoreWatermark
    };
    var result = await collectionService.CollectAsync(collectionRequest, ct);
    return Results.Ok(result);
});

// POST /automation/gmail/process
// Body: { } (empty)
// Processes all collected (unprocessed) emails
app.MapPost("/automation/gmail/process", async (
    Email.Services.GmailProcessing.GmailProcessingV2Service processingService,
    CancellationToken ct) =>
{
    var result = await processingService.ProcessCollectedAsync(ct);
    return Results.Ok(result);
});

// POST /automation/gmail/auto
// Body: same as /collect
// Runs Collect then Process sequentially
app.MapPost("/automation/gmail/auto", async (
    Email.Services.Automation.Dtos.AutomationCollectRequest request,
    Email.Services.GmailCollection.GmailCollectionV2Service collectionService,
    Email.Services.GmailProcessing.GmailProcessingV2Service processingService,
    CancellationToken ct) =>
{
    var collectionRequest = new Email.Services.GmailCollection.Dtos.CollectionRequestDto
    {
        Window = MapWindowToCollectionWindow(request.Window),
        DaysPrior = request.DaysPrior ?? (request.Window?.Equals("year", StringComparison.OrdinalIgnoreCase) == true ? 365 : null),
        Limit = request.Limit, // null = no limit
        BatchSize = request.BatchSize > 0 ? request.BatchSize : 150,
        IgnoreWatermark = request.IgnoreWatermark
    };
    var collectResult = await collectionService.CollectAsync(collectionRequest, ct);
    var processResult = await processingService.ProcessCollectedAsync(ct);
    return Results.Ok(new Email.Services.Automation.Dtos.AutomationCombinedResult
    {
        Collect = collectResult,
        Process = processResult
    });
});

// POST /automation/checkfront/sync
// Body: { daysBack?: int, limitPerPage?: int, maxPages?: int }
// Pulls bookings from Checkfront and reconciles them against existing email-derived data.
app.MapPost("/automation/checkfront/sync", async (
    Email.Services.Automation.Dtos.CheckfrontSyncRequest? request,
    Email.Services.GmailProcessing.GmailProcessingV2Service processingService,
    CancellationToken ct) =>
{
    request ??= new Email.Services.Automation.Dtos.CheckfrontSyncRequest();
    var result = await processingService.SyncCheckfrontBookingsAsync(
        request.DaysBack,
        request.LimitPerPage,
        request.MaxPages,
        ct);
    return Results.Ok(result);
});

// GET /automation/gmail/walkers?limit=&sinceUtc=
// When limit is null, returns all bookings
app.MapGet("/automation/gmail/walkers", async (
    int? limit,
    string? sinceUtc,
    Email.Services.GmailProcessing.GmailProcessingV2Service processingService,
    CancellationToken ct) =>
{
    // Use int.MaxValue when no limit specified to get all records
    var bookings = await processingService.GetRecentBookingsAsync(limit ?? int.MaxValue, ct);
    DateTime? since = null;
    if (!string.IsNullOrWhiteSpace(sinceUtc) && DateTime.TryParse(sinceUtc, out var parsed))
    {
        since = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
    }
    var filtered = bookings
        .Where(b => since == null || b.CreatedAt >= since || b.UpdatedAt >= since)
        .Select(b => new Email.Services.Automation.Dtos.WalkerDto
        {
            Id = b.Id,
            CustomerName = b.CustomerName,
            CustomerPhone = b.CustomerPhone,
            TourName = b.TourName,
            TourDate = b.TourDate,
            DisplayDate = b.DisplayDate,
            DisplayTime = b.DisplayTime,
            VendorName = b.VendorName,
            MessageId = b.MessageId,
            IsConfirmation = b.IsConfirmation,
            IsActive = b.IsActive,
            CreatedAt = b.CreatedAt,
            UpdatedAt = b.UpdatedAt
        })
        .ToList();
    return Results.Ok(filtered);
});

// POST /automation/gmail/sms/auto
// Body: { templateId: int, sinceUtc?: string, limit?: int }
// When limit is null, processes all matching bookings
app.MapPost("/automation/gmail/sms/auto", async (
    Email.Services.Automation.Dtos.AutoSmsRequest request,
    Email.Services.GmailProcessing.GmailProcessingV2Service processingService,
    Email.Services.ITourMessagesApiService messagesService,
    CancellationToken ct) =>
{
    var template = await messagesService.GetTourMessageByIdAsync(request.TemplateId, ct);
    if (template == null)
    {
        return Results.BadRequest(new { error = "Template not found" });
    }

    // Use int.MaxValue when no limit specified to get all records
    var bookings = await processingService.GetRecentBookingsAsync(request.Limit ?? int.MaxValue, ct);
    DateTime? since = null;
    if (!string.IsNullOrWhiteSpace(request.SinceUtc) && DateTime.TryParse(request.SinceUtc, out var parsed))
    {
        since = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
    }

    var result = new Email.Services.Automation.Dtos.AutoSmsResult();
    foreach (var b in bookings)
    {
        // Skip if sinceUtc filter is active and booking is older
        if (since.HasValue && b.CreatedAt < since && b.UpdatedAt < since)
        {
            continue;
        }

        result.Attempted++;

        if (!b.IsConfirmation || !b.IsActive)
        {
            result.SkippedNotConfirmation++;
            result.Results.Add(new Email.Services.Automation.Dtos.AutoSmsItemResult
            {
                BookingId = b.Id,
                Status = "skipped",
                SkipReason = "Not a confirmation or not active"
            });
            continue;
        }

        if (string.IsNullOrWhiteSpace(b.CustomerPhone))
        {
            result.SkippedNoPhone++;
            result.Results.Add(new Email.Services.Automation.Dtos.AutoSmsItemResult
            {
                BookingId = b.Id,
                Status = "skipped",
                SkipReason = "No phone number"
            });
            continue;
        }

        var body = RenderSmsTemplate(template.MessageContent, template.Signature, b);
        result.SimulatedSent++;
        result.Results.Add(new Email.Services.Automation.Dtos.AutoSmsItemResult
        {
            BookingId = b.Id,
            To = b.CustomerPhone,
            Body = body,
            Status = "simulated"
        });
    }

    return Results.Ok(result);
});

// Browser lifecycle diagnostics endpoint for SignalR disconnect correlation.
app.MapPost("/diagnostics/signalr/browser-event", async (
    BrowserLifecycleEventRequest request,
    HttpContext context,
    ISignalRDiagnosticsLog diagnosticsLog) =>
{
    if (request is null || string.IsNullOrWhiteSpace(request.EventType))
    {
        return Results.BadRequest(new { error = "EventType is required." });
    }

    var user = context.User?.Identity?.IsAuthenticated == true
        ? context.User.Identity?.Name ?? "(authenticated)"
        : "(anonymous)";

    var route = !string.IsNullOrWhiteSpace(request.Route)
        ? request.Route
        : request.PageUrl;

    var detailsParts = new List<string>
    {
        $"VisibilityState={request.VisibilityState}",
        $"Hidden={(request.Hidden.HasValue ? request.Hidden.Value.ToString().ToLowerInvariant() : "unknown")}",
        $"Online={(request.Online.HasValue ? request.Online.Value.ToString().ToLowerInvariant() : "unknown")}",
        $"ClientTimestampUtc={request.ClientTimestampUtc?.ToString("O") ?? "(none)"}"
    };

    if (!string.IsNullOrWhiteSpace(request.Details))
    {
        detailsParts.Add($"ClientDetails={request.Details}");
    }

    await diagnosticsLog.WriteAsync(new SignalRDiagnosticsEntry
    {
        EventType = $"BrowserLifecycle.{request.EventType.Trim().ToLowerInvariant()}",
        Method = "BrowserLifecycleProbe",
        Route = route,
        TabId = request.TabId,
        User = user ?? "(anonymous)",
        Details = string.Join("; ", detailsParts),
        OccurredUtc = DateTimeOffset.UtcNow
    });

    return Results.NoContent();
});

// Helper: Map window string to collection service window format
static string MapWindowToCollectionWindow(string? window)
{
    return window?.ToLowerInvariant() switch
    {
        "day" => "day",
        "week" => "week",
        "month" => "month",
        "year" => "daysPrior", // year maps to daysPrior with 365 days
        "daysprior" => "daysPrior",
        "all" => "all",
        _ => "all" // default to all (no date filter)
    };
}

// Helper: Render SMS template with booking data placeholders
//todo make sure this is not used 2/16/26
static string RenderSmsTemplate(string? content, string? signature, Email.Services.GmailProcessing.Models.Booking b)
{
    var text = content ?? string.Empty;
    var formattedDate = b.DisplayDate ?? b.TourDate?.ToString("MM/dd/yyyy") ?? "";
    text = text
        .Replace("{walker}", b.CustomerName ?? "")
        .Replace("{name}", b.CustomerName ?? "")
        .Replace("{tour}", b.TourName ?? "")
        .Replace("{tourname}", b.TourName ?? "")
        .Replace("{date}", formattedDate)
        .Replace("{vendorName}", b.VendorName ?? "")
        .Replace("{phone}", b.CustomerPhone ?? "");

    if (!string.IsNullOrWhiteSpace(signature))
    {
        text = text + "\n\n" + signature;
    }
    return text;
}

app.MapControllers();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();

