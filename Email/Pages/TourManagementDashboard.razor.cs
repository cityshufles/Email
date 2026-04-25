using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Email.Models;
using Email.Services;
using Email.TourTreeViewShapedData.Enums;
using Email.TourTreeViewShapedData.Models;

namespace Email.Pages
{
    // Created: 2025-11-16 00:00 UTC - Trimmed dashboard logic for the Email project
    public partial class TourManagementDashboard : ComponentBase
    {
        // 2025-12-04 00:00 UTC - Align key with Mobile/Workstation so iPhone mode persists consistently
        private const string IPhoneModeStorageKey = "cityshuffles_iphone_copy_mode";

        [Inject] protected ITourEmailsService EmailsService { get; set; } = default!;
        [Inject] protected ITourTreeService TreeService { get; set; } = default!;
		[Inject] protected IGuidesApiService GuidesApi { get; set; } = default!;
        [Inject] protected IToursApiService ToursApi { get; set; } = default!;
        [Inject] protected ILogger<TourManagementDashboard> Logger { get; set; } = default!;
        [Inject] protected IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] protected NavigationManager Navigation { get; set; } = default!;

        protected CollectionStatusDto? collectionStatus;
        protected bool isStatusLoading;
        protected string? latestSubject;
        protected string activeTab = "tours";
        protected bool isLoading;
        protected ShapedTreeData? tourTreeData;
        protected ShapedTreeData? filteredTourTreeData;
        protected ShapedTreeFilterArgs filterArgs = new()
        {
            FilterType = TreeDataFilterType.ActiveBookings,
            StartDate = DateTime.Today,
            EndDate = DateTime.Today.AddDays(2)
        };
        protected bool useIPhoneCopyMode;
        protected bool showVersionDialog;
        protected bool showNewVersionBadge;
        protected bool showHelpPanel;
		protected bool showGuideManagementDialog = false;
        protected BookingPrefillRequest? addBookingPrefill;

		// 2025-11-20 - Guide management state (migrated)
		protected string? newGuideName;
		protected string? newGuideFirstName;
		protected string? newGuideLastName;
		protected string? newGuidePhone;
		protected string? newGuideEmail;
		protected string? newGuideImage;
		protected string? newGuideMeetingLocation;
		protected string? newGuideDescription;
		protected string? newGuideDefaultTourName;
		protected string? newGuideLanguages;
		protected string? newGuideSpecialties;
		protected DateTime? newGuideHireDate;
		protected DateTime? newGuideTerminationDate;
		protected bool newGuideIsTouring = true;
		protected bool newGuideIsActive = true;
		protected string? newGuideAvailabilityNotes;
		protected string? newGuideNotes;

		protected int? editingGuideId = null;

		// For dropdowns/context
		protected List<DbTour> availableTours = new();
		protected List<string> distinctTourNames = new();
		protected List<DbGuide> availableDbGuides = new();

		// In-memory simplified list for display (mirrors workstation behavior)
		protected List<Guide> inMemoryGuides = new();
        protected override async Task OnInitializedAsync()
        {
            // 2025-11-17: Console trace - page init
            await ConsoleLogAsync("TourManagementDashboard.OnInitializedAsync: start");
            await RestoreIPhoneModeAsync();
            await RefreshStatusesAsync();
            await RefreshTreeDataAsync();
            await LoadLatestSubjectAsync();
            await ConsoleLogAsync("TourManagementDashboard.OnInitializedAsync: complete");
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                try
                {
                    try
                    {
                        // Load messages and sync templates for dropdowns
                        dbMessages = await TourMessagesApi.GetTourMessagesAsync();
                        SyncInMemoryMessagesFromDb();
                    }
                    catch { }

                    try
                    {
                        // Load guides for tour-level guide dropdowns
                        availableDbGuides = await GuidesApi.GetGuidesAsync();
                    }
                    catch { }
                }
                catch { }
            }
        }

        protected async Task RefreshStatusesAsync()
        {
            try
            {
                await ConsoleLogAsync("RefreshStatusesAsync: start");
                isStatusLoading = true;
                collectionStatus = await EmailsService.GetCollectionStatusAsync();
                await ConsoleLogAsync($"RefreshStatusesAsync: ok - status {(collectionStatus is null ? "null" : "loaded")}");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to refresh collection status");
                await ConsoleLogAsync($"RefreshStatusesAsync: error - {ex.Message}");
            }
            finally
            {
                isStatusLoading = false;
            }
        }

        protected async Task RefreshTreeDataAsync(bool forceFresh = false)
        {
            try
            {
                if (ShouldAttemptInitializationRestore(forceFresh) && TryRestoreDashboardFromCache())
                {
                    await ConsoleLogAsync("RefreshTreeDataAsync: restored from cache");
                    return;
                }

                await ConsoleLogAsync($"RefreshTreeDataAsync: start - FilterType={filterArgs.FilterType}, Start={filterArgs.StartDate?.ToString("yyyy-MM-dd") ?? "(null)"}, End={filterArgs.EndDate?.ToString("yyyy-MM-dd") ?? "(null)"}, Vendor={filterArgs.Vendor ?? "(null)"}");
                isLoading = true;
                tourTreeData = await TreeService.GetTourTreeDataShapedAsync(filterArgs.FilterType, filterArgs.StartDate, filterArgs.EndDate, filterArgs.Vendor);
                filteredTourTreeData = tourTreeData;
                SaveDashboardSnapshotToCache();

                var dateCount = filteredTourTreeData?.TreeNodes?.Count ?? 0;
                var vendorCount = 0;
                var walkerCount = 0;
                if (filteredTourTreeData?.TreeNodes != null)
                {
                    foreach (var d in filteredTourTreeData.TreeNodes)
                    {
                        foreach (var t in d.Children)
                        {
                            vendorCount += t.Children.Count;
                            foreach (var v in t.Children)
                            {
                                walkerCount += v.Children.Count;
                            }
                        }
                    }
                }
                await ConsoleLogAsync($"RefreshTreeDataAsync: ok - Dates={dateCount}, Vendors≈{vendorCount}, Walkers≈{walkerCount}");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to refresh shaped tree data");
                await ConsoleLogAsync($"RefreshTreeDataAsync: error - {ex.Message}");
            }
            finally
            {
                isLoading = false;
            }
        }

        protected async Task OnApplyFilters(ShapedTreeFilterArgs args)
        {
            filterArgs = args ?? new ShapedTreeFilterArgs { FilterType = TreeDataFilterType.ActiveBookings };

            if (filterArgs.StartDate.HasValue && filterArgs.EndDate.HasValue && filterArgs.StartDate.Value.Date == filterArgs.EndDate.Value.Date)
            {
                // Mobile requirement: always include selected day and the next day
                filterArgs.EndDate = filterArgs.StartDate.Value.Date.AddDays(2);
            }
            else if (filterArgs.StartDate.HasValue && !filterArgs.EndDate.HasValue)
            {
                // Mobile requirement: single day selection expands to two days
                filterArgs.EndDate = filterArgs.StartDate.Value.Date.AddDays(2);
            }

            SaveDashboardSnapshotToCache();
            await ConsoleLogAsync($"OnApplyFilters: FilterType={filterArgs.FilterType}, Start={filterArgs.StartDate?.ToString("yyyy-MM-dd") ?? "(null)"}, End={filterArgs.EndDate?.ToString("yyyy-MM-dd") ?? "(null)"}, Vendor={filterArgs.Vendor ?? "(null)"}");
            await RefreshTreeDataAsync();
        }

        protected async Task LoadTodayAsync()
        {
            filterArgs.FilterType = TreeDataFilterType.ActiveBookings;
            filterArgs.StartDate = DateTime.Today;
            filterArgs.EndDate = DateTime.Today.AddDays(2);
            SaveDashboardSnapshotToCache();
            await ConsoleLogAsync("LoadTodayAsync invoked");
            await RefreshTreeDataAsync();
        }

        protected async Task ResetFiltersToDefaults()
        {
            filterArgs = new ShapedTreeFilterArgs
            {
                FilterType = TreeDataFilterType.ActiveBookings,
                StartDate = DateTime.Today,
                EndDate = DateTime.Today.AddDays(2)
            };
            SaveDashboardSnapshotToCache();
            await ConsoleLogAsync("ResetFiltersToDefaults invoked");
            await RefreshTreeDataAsync();
        }

        protected void SetTab(string tab)
        {
            activeTab = tab;
            SaveDashboardSnapshotToCache();
            _ = ConsoleLogAsync($"SetTab: {tab}");
        }

        protected Task HandleAddBookingFromTour(BookingPrefillRequest request)
        {
            if (request == null)
            {
                return Task.CompletedTask;
            }

            addBookingPrefill = new BookingPrefillRequest
            {
                TourName = request.TourName,
                TourDate = request.TourDate,
                TourTime = request.TourTime
            };

            activeTab = "addTour";
            SaveDashboardSnapshotToCache();
            _ = ConsoleLogAsync($"HandleAddBookingFromTour: {addBookingPrefill.TourName} {addBookingPrefill.TourDate:yyyy-MM-dd} {addBookingPrefill.TourTime}");
            return Task.CompletedTask;
        }

        // 2025-11-18: Activate live tab (mirrored from workstation)
        protected void ActivateLiveTab()
        {
            activeTab = "live";
            _ = ConsoleLogAsync("ActivateLiveTab invoked");
        }

        // 2025-11-18: Version dialog handlers
        protected void ShowVersionDialog()
        {
            showVersionDialog = true;
            showNewVersionBadge = false;
            _ = ConsoleLogAsync("ShowVersionDialog invoked");
        }

        protected void CloseVersionDialog()
        {
            showVersionDialog = false;
            _ = ConsoleLogAsync("CloseVersionDialog invoked");
        }

        // 2025-11-18: Help panel toggle
        protected void ToggleHelpPanel()
        {
            showHelpPanel = !showHelpPanel;
            _ = ConsoleLogAsync($"ToggleHelpPanel: {showHelpPanel}");
        }

        protected async Task OnEmailProcessingCompleted()
        {
            await RefreshStatusesAsync();
            await RefreshTreeDataAsync(forceFresh: true);
            await LoadLatestSubjectAsync();
            await ConsoleLogAsync("OnEmailProcessingCompleted: refresh done");
        }

        // 2025-12-05 00:00 UTC - Await load so dialog renders with data on first open
        protected async Task OpenGuideManagementDialog()
        {
            showGuideManagementDialog = true;
            StateHasChanged(); // render the modal immediately
            await LoadGuidesAndToursForDialogAsync();
            StateHasChanged(); // refresh once guides/tours finish loading
        }

        protected void CloseGuideManagementDialog()
        {
            showGuideManagementDialog = false;
            StateHasChanged();
        }

        protected void OpenMessageManagementDialog()
        {
            Navigation.NavigateTo("messages");
        }

        private void SyncInMemoryMessagesFromDb()
        {
            inMemoryMessages = dbMessages
                .Select(m => new InMemoryMessageTemplate
                {
                    Id = m.Id.ToString(),
                    Name = m.MessageName,
                    Type = m.MessageType ?? string.Empty,
                    Content = m.MessageContent,
                    Description = m.Description ?? string.Empty,
                    TourName = m.TourName,
                    TourStartTime = m.TourStartTime,
                    GuideId = m.GuideId,
                    MeetingPlace = m.MeetingPlace,
                    MeetingTime = m.MeetingTime,
                    MeetingInstructions = m.MeetingInstructions,
                    Signature = m.Signature,
                    IsActive = m.IsActive
                })
                .ToList();
        }

        // Messages state
        [Inject] protected ITourMessagesApiService TourMessagesApi { get; set; } = default!;
        protected List<DbTourMessage> dbMessages = new();
        protected List<InMemoryMessageTemplate> inMemoryMessages = new();

        #if false
        private void MaybeSetDefaultGuideForMessage()
        {
            if (newMessageGuideId.HasValue) return;
            if (tourCatalog == null) return;

            TourCatalogEntry? entry = null;
            if (newMessageTourId.HasValue)
            {
                entry = tourCatalog.Entries.FirstOrDefault(e => e.TourId == newMessageTourId.Value);
            }
            if (entry == null && !string.IsNullOrWhiteSpace(newMessageTourName))
            {
                var canonical = NormalizeName(newMessageTourName);
                entry = tourCatalog.Entries.FirstOrDefault(e =>
                    string.Equals(e.CanonicalName, canonical, StringComparison.OrdinalIgnoreCase) ||
                    e.Aliases.Any(a => string.Equals(NormalizeName(a), canonical, StringComparison.OrdinalIgnoreCase)));
            }
            if (entry == null) return;

            int? guideId = null;

            if (entry.TourId is int tid &&
                tourCatalog.GuideDefaultsByTourId.TryGetValue(tid, out var defaults) &&
                defaults.Count > 0)
            {
                if (string.IsNullOrWhiteSpace(newMessageTourStartTime) && entry.Times.Count > 0)
                {
                    newMessageTourStartTime = entry.Times.First();
                }

                if (!string.IsNullOrWhiteSpace(newMessageTourStartTime))
                {
                    var normTime = NormalizeTimeSafe(newMessageTourStartTime);
                    var match = defaults.FirstOrDefault(d => string.Equals(NormalizeTimeSafe(d.StartTime), normTime, StringComparison.OrdinalIgnoreCase));
                    guideId = match?.GuideId;
                }

                guideId ??= defaults.FirstOrDefault()?.GuideId;
            }

            if (!guideId.HasValue && entry.DefaultGuideId.HasValue)
            {
                guideId = entry.DefaultGuideId;
            }

            if (guideId.HasValue && availableDbGuides.Any(g => g.Id == guideId.Value))
            {
                newMessageGuideId = guideId;
                StateHasChanged();
            }
        }
        #endif

        protected string FormatTourLabel(DbTour t)
        {
            var time = string.IsNullOrWhiteSpace(t.TourStartTime) ? t.MeetingTime : t.TourStartTime;
            return string.IsNullOrWhiteSpace(time) ? t.TourName : ($"{t.TourName} — {time}");
        }
        // 2025-11-20 - Guide dialog helpers (migrated)
        protected void ComposeNewGuideName(ChangeEventArgs _)
        {
            var first = (newGuideFirstName ?? string.Empty).Trim();
            var last = (newGuideLastName ?? string.Empty).Trim();
            newGuideName = string.IsNullOrWhiteSpace(last) ? first : ($"{first} {last}");
        }

        protected async Task AddGuide()
        {
            if (!string.IsNullOrWhiteSpace(newGuideName))
            {
                var parts = (newGuideName ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var first = parts.Length > 0 ? parts[0] : newGuideName!.Trim();
                var last = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : null;

                if (!string.IsNullOrEmpty(newGuideName) && newGuideName.Contains(','))
                {
                    return;
                }

                var payload = new DbGuide
                {
                    FirstName = string.IsNullOrWhiteSpace(newGuideFirstName) ? first : newGuideFirstName!.Trim(),
                    LastName = string.IsNullOrWhiteSpace(newGuideLastName) ? last : newGuideLastName!.Trim(),
                    Phone = newGuidePhone ?? string.Empty,
                    Email = newGuideEmail,
                    GuideImage = newGuideImage,
                    Description = newGuideDescription,
                    DefaultTourName = newGuideDefaultTourName,
                    DefaultTourId = availableTours.FirstOrDefault(t => string.Equals(t.TourName, (newGuideDefaultTourName ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))?.Id,
                    IsTouring = true,
                    IsActive = newGuideIsActive
                };

                if (editingGuideId.HasValue)
                {
                    var ok = await GuidesApi.UpdateGuideAsync(editingGuideId.Value, payload);
                    if (ok)
                    {
                        await RefreshInMemoryGuidesAsync();
                        ClearGuideForm();
                        editingGuideId = null;
                    }
                }
                else
                {
                    var created = await GuidesApi.CreateGuideAsync(payload);
                    if (created != null)
                    {
                        var newGuide = new Guide
                        {
                            Name = string.IsNullOrWhiteSpace(created.LastName) ? created.FirstName : ($"{created.FirstName} {created.LastName}"),
                            Phone = created.Phone,
                            Email = created.Email ?? "",
                            MeetingLocation = "",
                            Description = created.Description ?? ""
                        };
                        inMemoryGuides.Add(newGuide);
                        ClearGuideForm();
                    }
                }
            }
        }

        protected void ClearGuideForm()
        {
            newGuideName = newGuidePhone = newGuideEmail = newGuideMeetingLocation = newGuideDescription = null;
            newGuideFirstName = newGuideLastName = newGuideImage = newGuideDefaultTourName = null;
            newGuideLanguages = newGuideSpecialties = newGuideAvailabilityNotes = newGuideNotes = null;
            newGuideHireDate = newGuideTerminationDate = null;
            newGuideIsTouring = true; newGuideIsActive = true;
        }

        protected async Task RemoveGuide(Guide guide)
        {
            if (inMemoryGuides.Remove(guide))
            {
                try
                {
                    var list = await GuidesApi.GetGuidesAsync();
                    var parts = (guide.Name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var first = parts.FirstOrDefault() ?? string.Empty;
                    var last = string.Join(' ', parts.Skip(1)).Trim();
                    var match = list.FirstOrDefault(g =>
                        string.Equals(g.FirstName, first, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals((g.LastName ?? string.Empty).Trim(), last, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        await GuidesApi.DeleteGuideAsync(match.Id);
                    }
                }
                catch { }
                StateHasChanged();
            }
        }

        protected async Task EditGuide(Guide guide)
        {
            try
            {
                var list = await GuidesApi.GetGuidesAsync();
                var parts = (guide.Name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var first = parts.FirstOrDefault() ?? string.Empty;
                var last = string.Join(' ', parts.Skip(1)).Trim();
                var match = list.FirstOrDefault(g =>
                    string.Equals(g.FirstName, first, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals((g.LastName ?? string.Empty).Trim(), last, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    editingGuideId = match.Id;
                    newGuideFirstName = match.FirstName;
                    newGuideLastName = match.LastName;
                    newGuideName = string.IsNullOrWhiteSpace(match.LastName) ? match.FirstName : ($"{match.FirstName} {match.LastName}");
                    newGuidePhone = match.Phone;
                    newGuideEmail = match.Email;
                    newGuideImage = match.GuideImage;
                    newGuideDescription = match.Description;
                    newGuideDefaultTourName = match.DefaultTourName;
                    newGuideIsTouring = match.IsTouring;
                    newGuideIsActive = match.IsActive;
                    newGuideAvailabilityNotes = match.AvailabilityNotes;
                    newGuideNotes = match.Notes;
                    StateHasChanged();
                }
            }
            catch { }
        }

        protected void CancelGuideEdit()
        {
            editingGuideId = null;
            ClearGuideForm();
        }

        protected async Task ClearAllGuides()
        {
            inMemoryGuides.Clear();
            StateHasChanged();
            await Task.CompletedTask;
        }
        private async Task LoadGuidesAndToursForDialogAsync()
        {
            try
            {
                try
                {
                    availableTours = await ToursApi.GetToursAsync();
                }
                catch { availableTours = new List<DbTour>(); }

                distinctTourNames = availableTours
                    .Select(t => t.TourName)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n)
                    .ToList();

                await RefreshInMemoryGuidesAsync();
            }
            catch { }
        }

        private async Task RefreshInMemoryGuidesAsync()
        {
            try
            {
                var list = await GuidesApi.GetGuidesAsync();
                inMemoryGuides = list
                    .Select(g => new Guide
                    {
                        Name = string.IsNullOrWhiteSpace(g.LastName) ? g.FirstName : ($"{g.FirstName} {g.LastName}"),
                        Phone = g.Phone,
                        Email = g.Email ?? "",
                        MeetingLocation = "",
                        Description = g.Description ?? ""
                    })
                    .ToList();

                availableDbGuides = list;
            }
            catch
            {
                inMemoryGuides = new();
                availableDbGuides = new();
            }
        }

        // Minimal Guide in-memory view model (for display parity)
        protected class Guide
        {
            public string? Name { get; set; }
            public string? Phone { get; set; }
            public string? Email { get; set; }
            public string? MeetingLocation { get; set; }
            public string? Description { get; set; }
        }




        private async Task LoadLatestSubjectAsync()
        {
            try
            {
                var latest = await EmailsService.GetLatestInboxEmailAsync();
                latestSubject = latest?.Subject;
                await ConsoleLogAsync($"LoadLatestSubjectAsync: {(latestSubject is null ? "no subject" : "subject loaded")}");
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Unable to fetch latest inbox email");
                await ConsoleLogAsync($"LoadLatestSubjectAsync: warning - {ex.Message}");
            }
        }

        protected async Task SaveIPhoneCopyModeAsync()
        {
            try
            {
                await JSRuntime.InvokeVoidAsync("localStorage.setItem", IPhoneModeStorageKey, useIPhoneCopyMode.ToString().ToLowerInvariant());
                await ConsoleLogAsync($"SaveIPhoneCopyModeAsync: {useIPhoneCopyMode}");
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Unable to persist iPhone copy mode");
                await ConsoleLogAsync($"SaveIPhoneCopyModeAsync: warning - {ex.Message}");
            }
        }

        private async Task RestoreIPhoneModeAsync()
        {
            try
            {
                var value = await JSRuntime.InvokeAsync<string>("localStorage.getItem", IPhoneModeStorageKey);
                if (bool.TryParse(value, out var stored))
                {
                    useIPhoneCopyMode = stored;
                }
                await ConsoleLogAsync($"RestoreIPhoneModeAsync: {(useIPhoneCopyMode ? "true" : "false")}");
            }
            catch
            {
                useIPhoneCopyMode = false;
            }
        }

        // 2025-11-18: Updated to use ShapedTreeNode (from ShapedTreeData)
        protected Task OnWalkerSelected(ShapedTreeNode node) => Task.CompletedTask;
        protected Task OnVCardDownload(ShapedTreeNode node) => Task.CompletedTask;
        protected Task OnSmsSend(ShapedTreeNode node) => Task.CompletedTask;
        protected Task OnWhatsAppSend(ShapedTreeNode node) => Task.CompletedTask;
        protected Task OnEmailView(ShapedTreeNode node) => Task.CompletedTask;

        // 2025-11-17: Helper to write to browser console
        private async Task ConsoleLogAsync(string message)
        {
            try
            {
                await JSRuntime.InvokeVoidAsync("cslog", message);
            }
            catch
            {
                // no-op
            }
        }
    }
}

