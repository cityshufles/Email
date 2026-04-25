using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Email.Models;
using Email.Models.Reports;
using Email.Services;

namespace Email.Components.Messages
{
    public partial class MessagesComponent : ComponentBase
    {
        [Inject] protected ITourMessagesApiService TourMessagesApi { get; set; } = default!;
        [Inject] protected IGuidesApiService GuidesApi { get; set; } = default!;
        [Inject] protected IToursApiService ToursApi { get; set; } = default!;
        [Inject] protected ITourLinkService TourLinkService { get; set; } = default!;
        [Inject] protected ITourCatalogService TourCatalogService { get; set; } = default!;
        [Inject] protected IVendorsApiService VendorsApi { get; set; } = default!;
        [Inject] protected IGalleryLinkResolver GalleryLinkResolver { get; set; } = default!;
        [Inject] protected IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] protected ILogger<MessagesComponent> Logger { get; set; } = default!;

        private string activeTab = "create";
        private string messageEditorTab = "edit";

        private List<DbTourMessage> dbMessages = new();
        private List<InMemoryMessageTemplate> inMemoryMessages = new();
        private List<DbTour> availableTours = new();
        private List<TourLink> availableTourLinks = new();
        private List<DbTour> distinctToursForContext = new();
        private List<DbGuide> availableDbGuides = new();
        private List<DbVendor> availableVendors = new();
        private Dictionary<string, HashSet<string>> activeVendorAssignments = new(StringComparer.OrdinalIgnoreCase);
        private TourCatalogSnapshot? tourCatalog;
        private List<string> messageTimeOptions = new();

        private int? editingMessageId;
        private string? newMessageName;
        private string? newMessageType = "custom";
        private string? newMessageContent;
        private string? newMessageDescription;
        private string? newMessageTourName;
        private int? newMessageTourId;
        private int? newMessageGuideId;
        private string? newMessageTourStartTime;
        private string? newMessageVendorName;
        private string? newMessageSignature;
        private bool newMessageIsActive = true;
        private string? lastMessageError;
        private bool isRefreshingContext;
        private string? createTabGalleryLink;
        private string? previewTabGalleryLink;

        private string previewMessageTime = "TODAY";
        private int? previewTourId;
        private int? previewGuideId;
        private string? previewVendorName;
        private string? previewGuestName;
        private DateTime previewDate = DateTime.Today;
        private string previewDateValue = DateTime.Today.ToString("yyyy-MM-dd");
        private List<DateTime> previewDateOptions = new();
        private List<string> previewVendorOptions = new();
        private IEnumerable<DbVendor> ActiveVendors => availableVendors
            .Where(v => v.IsActive)
            .OrderBy(v => v.VendorName);

        protected override async Task OnInitializedAsync()
        {
            await LoadMessageDataAsync();
        }

        private async Task RefreshMessageContextAsync()
        {
            if (isRefreshingContext)
            {
                return;
            }

            try
            {
                isRefreshingContext = true;
                await LoadMessageDataAsync();
            }
            finally
            {
                isRefreshingContext = false;
            }
        }

        private async Task LoadMessageDataAsync()
        {
            try
            {
                await EnsureCatalogAsync();

                try { availableTours = await ToursApi.GetToursAsync(); }
                catch { availableTours = new(); }

                try { availableTourLinks = await TourLinkService.GetAllAsync(); }
                catch { availableTourLinks = new(); }

                try { distinctToursForContext = BuildMasterTourList(); }
                catch { distinctToursForContext = availableTours; }

                try { availableDbGuides = await GuidesApi.GetGuidesAsync(); }
                catch { availableDbGuides = new(); }

                try { availableVendors = await VendorsApi.GetVendorsAsync(); }
                catch { availableVendors = new(); }

                try
                {
                    var vendorTours = await VendorsApi.GetVendorToursAsync();
                    activeVendorAssignments = VendorLinkResolver.BuildActiveVendorAssignments(vendorTours, availableVendors);
                }
                catch
                {
                    activeVendorAssignments = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                }

                dbMessages = await TourMessagesApi.GetTourMessagesAsync();
                SyncInMemoryMessagesFromDb();
                messageTimeOptions = GetTimeOptionsFor(newMessageTourName);
                MaybeSetDefaultGuideForMessage();

                previewDateOptions = BuildPreviewDateOptions();
                previewVendorOptions = BuildPreviewVendorOptions();
                previewDateValue = previewDate.ToString("yyyy-MM-dd");
                await RefreshGalleryContextAsync();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "MessagesComponent failed to load message data");
            }
        }

        private void SetTab(string tab)
        {
            activeTab = tab == "preview" ? "preview" : "create";
        }

        private void SetMessageEditorTab(string tab)
        {
            messageEditorTab = tab == "preview" ? "preview" : "edit";
        }

        private void SetMessageEditorTabToEdit() => SetMessageEditorTab("edit");
        private void SetMessageEditorTabToPreview() => SetMessageEditorTab("preview");

        private string PreviewGeneratedMessage => BuildPreviewMessage();
        private string PreviewTemplateName => GetPreviewTemplate()?.MessageName ?? "No matching template";
        private string PreviewDateLabel => GetPreviewDate().ToString("yyyy-MM-dd");

        private async Task OnPreviewMessageTimeChanged()
        {
            var date = GetPreviewDate();
            previewDateValue = date.ToString("yyyy-MM-dd");
            await RefreshGalleryContextAsync();
        }

        private async Task OnPreviewTourChanged(ChangeEventArgs e)
        {
            previewTourId = int.TryParse(e.Value?.ToString(), out var id) ? id : null;
            if (previewTourId.HasValue && string.IsNullOrWhiteSpace(previewVendorName))
            {
                var tour = availableTours.FirstOrDefault(t => t.Id == previewTourId.Value);
                previewVendorName = GetPrimaryVendorName(tour);
            }
            await RefreshGalleryContextAsync();
        }

        private void OnPreviewGuideChanged(ChangeEventArgs e)
        {
            previewGuideId = int.TryParse(e.Value?.ToString(), out var id) ? id : null;
        }

        private async Task OnPreviewDateChanged(ChangeEventArgs e)
        {
            previewDateValue = e.Value?.ToString() ?? previewDateValue;
            if (DateTime.TryParse(previewDateValue, out var dt))
            {
                previewDate = dt.Date;
            }
            await RefreshGalleryContextAsync();
        }

        private async Task OnPreviewVendorChanged()
        {
            await RefreshGalleryContextAsync();
        }

        private async Task OnNewMessageVendorChanged()
        {
            await RefreshGalleryContextAsync();
        }

        private void OnMessageTourChanged()
        {
            if (newMessageTourId.HasValue)
            {
                var t = distinctToursForContext.FirstOrDefault(x => x.Id == newMessageTourId.Value);
                if (t != null)
                {
                    newMessageTourName = NormalizeName(GetMasterTourName(t));
                    var timeCandidate = string.IsNullOrWhiteSpace(t.TourStartTime) ? t.MeetingTime : t.TourStartTime;
                    newMessageTourStartTime = NormalizeTimeSafe(timeCandidate);
                    messageTimeOptions = GetTimeOptionsFor(newMessageTourName);
                    if (string.IsNullOrWhiteSpace(newMessageTourStartTime) && messageTimeOptions.Count > 0)
                    {
                        newMessageTourStartTime = messageTimeOptions.First();
                    }
                    else if (messageTimeOptions.Count > 0 && !messageTimeOptions.Contains(newMessageTourStartTime ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                    {
                        newMessageTourStartTime = messageTimeOptions.First();
                    }
                    if (string.IsNullOrWhiteSpace(newMessageVendorName))
                    {
                        var tour = availableTours.FirstOrDefault(tour => tour.Id == newMessageTourId.Value) ?? t;
                        var vendorName = GetPrimaryVendorName(tour);
                        if (!string.IsNullOrWhiteSpace(vendorName))
                        {
                            newMessageVendorName = vendorName;
                        }
                    }
                    newMessageGuideId = null;
                    MaybeSetDefaultGuideForMessage();
                }
            }
            else if (string.Equals(newMessageTourName, "All", StringComparison.OrdinalIgnoreCase))
            {
                newMessageTourId = null;
                newMessageTourStartTime = null;
                messageTimeOptions = new List<string>();
                newMessageGuideId = null;
            }

            _ = RefreshGalleryContextAsync();
        }

        private async Task AddMessageTemplate()
        {
            if (string.IsNullOrWhiteSpace(newMessageName))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(newMessageType))
            {
                newMessageType = "custom";
            }

            newMessageTourName = NormalizeName(newMessageTourName);
            newMessageTourStartTime = NormalizeTimeSafe(newMessageTourStartTime);

            if (!string.Equals(newMessageTourName, "All", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(newMessageTourName))
                {
                    return;
                }
                if (string.IsNullOrWhiteSpace(newMessageTourStartTime))
                {
                    var options = GetTimeOptionsFor(newMessageTourName);
                    if (options.Count == 0) return;
                    newMessageTourStartTime = options.First();
                }
            }
            else
            {
                newMessageTourName = "All";
                newMessageTourStartTime = null;
            }

            var payload = new DbTourMessage
            {
                MessageName = newMessageName.Trim(),
                MessageType = (newMessageType ?? string.Empty).Trim(),
                TourName = (newMessageTourName ?? string.Empty).Trim(),
                TourId = newMessageTourId,
                TourStartTime = string.IsNullOrWhiteSpace(newMessageTourStartTime) ? null : newMessageTourStartTime!.Trim(),
                GuideId = newMessageGuideId,
                MeetingPlaceId = null,
                MeetingPlace = null,
                MeetingTime = null,
                MessageContent = newMessageContent ?? string.Empty,
                Signature = newMessageSignature,
                Description = newMessageDescription,
                IsActive = newMessageIsActive
            };

            lastMessageError = null;

            if (editingMessageId.HasValue)
            {
                var ok = await TourMessagesApi.UpdateTourMessageAsync(editingMessageId.Value, payload);
                if (ok)
                {
                    dbMessages = await TourMessagesApi.GetTourMessagesAsync();
                    SyncInMemoryMessagesFromDb();
                    ClearMessageForm();
                }
                else
                {
                    lastMessageError = TourMessagesApi.LastError ?? "Unknown error updating message template";
                }
            }
            else
            {
                var created = await TourMessagesApi.CreateTourMessageAsync(payload);
                if (created != null)
                {
                    dbMessages.Add(created);
                    SyncInMemoryMessagesFromDb();
                    ClearMessageForm();
                }
                else
                {
                    lastMessageError = TourMessagesApi.LastError ?? "Unknown error creating message template";
                }
            }
        }

        private void ClearMessageForm()
        {
            newMessageName = newMessageContent = newMessageDescription = null;
            newMessageType = "custom";
            newMessageTourName = newMessageTourStartTime = newMessageSignature = null;
            newMessageGuideId = null;
            newMessageVendorName = null;
            newMessageIsActive = true;
            editingMessageId = null;
            lastMessageError = null;
            messageEditorTab = "edit";
            messageTimeOptions = new List<string>();
            _ = RefreshGalleryContextAsync();
        }

        private async Task RemoveDbMessageTemplate(DbTourMessage template)
        {
            var ok = await TourMessagesApi.DeleteTourMessageAsync(template.Id);
            if (ok)
            {
                dbMessages.RemoveAll(x => x.Id == template.Id);
                SyncInMemoryMessagesFromDb();
            }
            else
            {
                lastMessageError = TourMessagesApi.LastError ?? "Unknown error deleting message template";
            }
        }

        private void EditDbMessageTemplate(DbTourMessage m)
        {
            editingMessageId = m.Id;
            newMessageName = m.MessageName;
            newMessageType = m.MessageType;
            newMessageTourId = m.TourId;
            newMessageTourName = NormalizeName(m.TourName);
            newMessageTourStartTime = NormalizeTimeSafe(m.TourStartTime);
            newMessageGuideId = m.GuideId;
            newMessageContent = m.MessageContent;
            newMessageSignature = m.Signature;
            newMessageDescription = m.Description;
            newMessageIsActive = m.IsActive;
            messageTimeOptions = GetTimeOptionsFor(newMessageTourName);
            var tour = newMessageTourId.HasValue
                ? availableTours.FirstOrDefault(t => t.Id == newMessageTourId.Value)
                : null;
            newMessageVendorName = GetPrimaryVendorName(tour);
            MaybeSetDefaultGuideForMessage();
            messageEditorTab = "edit";
            _ = RefreshGalleryContextAsync();
        }

        private void CancelMessageEdit()
        {
            editingMessageId = null;
            ClearMessageForm();
        }

        private void EditMessageById(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            var m = dbMessages.FirstOrDefault(x => string.Equals(x.Id.ToString(), id, StringComparison.Ordinal));
            if (m != null)
            {
                EditDbMessageTemplate(m);
            }
        }

        private async Task RemoveMessageById(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            var m = dbMessages.FirstOrDefault(x => string.Equals(x.Id.ToString(), id, StringComparison.Ordinal));
            if (m != null)
            {
                await RemoveDbMessageTemplate(m);
            }
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

        private async Task EnsureCatalogAsync()
        {
            if (tourCatalog != null) return;
            try
            {
                tourCatalog = await TourCatalogService.BuildCatalogAsync();
            }
            catch
            {
                tourCatalog = new TourCatalogSnapshot();
            }
        }

        private List<DbTour> BuildCatalogToursWithTimes()
        {
            var list = new List<DbTour>();
            if (tourCatalog == null) return list;

            foreach (var entry in tourCatalog.Entries)
            {
                var times = entry.Times != null && entry.Times.Count > 0 ? entry.Times : new List<string> { string.Empty };
                foreach (var ttime in times)
                {
                    list.Add(new DbTour
                    {
                        Id = entry.TourId ?? 0,
                        TourName = entry.CanonicalName,
                        TourStartTime = ttime,
                        MeetingTime = ttime
                    });
                }
            }

            return list;
        }

        private List<DbTour> BuildMasterTourList()
        {
            if (availableTours == null || availableTours.Count == 0) return new List<DbTour>();

            return availableTours
                .Where(t => t.IsActive)
                .GroupBy(t => (GetMasterTourName(t) ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderBy(t => t.Id).First())
                .OrderBy(t => GetMasterTourName(t), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string GetMasterTourName(DbTour t)
        {
            return string.IsNullOrWhiteSpace(t.MasterTourName) ? t.TourName : t.MasterTourName;
        }

        private string NormalizeName(string? raw) => TourCatalogService.NormalizeName(raw ?? string.Empty);
        private string NormalizeTimeSafe(string? raw) => TourCatalogService.NormalizeTime(raw ?? string.Empty);

        private List<string> GetTimeOptionsFor(string? name)
        {
            if (tourCatalog == null || string.IsNullOrWhiteSpace(name)) return new List<string>();
            var canonical = NormalizeName(name);
            var entry = tourCatalog.Entries.FirstOrDefault(e =>
                string.Equals(e.CanonicalName, canonical, StringComparison.OrdinalIgnoreCase) ||
                e.Aliases.Any(a => string.Equals(NormalizeName(a), canonical, StringComparison.OrdinalIgnoreCase)));
            var times = entry?.Times ?? new List<string>();
            return times.Count == 0 ? new List<string>() : times;
        }

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

            guideId ??= entry.DefaultGuideId;

            if (guideId.HasValue && availableDbGuides.Any(g => g.Id == guideId.Value))
            {
                newMessageGuideId = guideId;
            }
        }

        private string FormatTourLabel(DbTour t)
        {
            return GetMasterTourName(t);
        }

        private string RenderMessagePreview()
        {
            var text = newMessageContent ?? string.Empty;

            var tour = newMessageTourId.HasValue ? availableTours.FirstOrDefault(t => t.Id == newMessageTourId.Value) : null;
            var guide = newMessageGuideId.HasValue ? availableDbGuides.FirstOrDefault(g => g.Id == newMessageGuideId.Value) : null;

            string guideFirst = guide?.FirstName ?? string.Empty;
            string guideLast = guide?.LastName ?? string.Empty;
            string guideFull = string.IsNullOrWhiteSpace(guideLast) ? guideFirst : $"{guideFirst} {guideLast}";
            string guidePhone = guide?.Phone ?? string.Empty;
            string guideEmail = guide?.Email ?? string.Empty;

            string tourName = newMessageTourName ?? tour?.TourName ?? string.Empty;
            string tourStart = newMessageTourStartTime ?? tour?.TourStartTime ?? tour?.MeetingTime ?? string.Empty;
            string meetingPlace = tour?.MeetingPlace ?? string.Empty;
            string meetingTimeLocal = tour?.MeetingTime ?? string.Empty;
            string meetingInstructions = tour?.MeetingInstructions ?? string.Empty;
            string vendorName = string.IsNullOrWhiteSpace(newMessageVendorName)
                ? GetPrimaryVendorName(tour)
                : newMessageVendorName!.Trim();

            string signature = newMessageSignature ?? string.Empty;
            var stageHint = $"{newMessageType} {newMessageName}".Trim();
            var resolvedLinks = ResolveVendorLinks(
                tour?.Id,
                tourName,
                vendorName,
                stageHint,
                null,
                tour?.VendorLink,
                tour?.ReviewLink);
            string vendorLink = resolvedLinks.VendorLink;
            string vendorTourLink = resolvedLinks.VendorTourLink;
            string vendorReviewLink = resolvedLinks.VendorReviewLink;
            string allToursLink = resolvedLinks.AllToursLink;
            string tempMeetingTime = ComputeTempMeetingTime(tourStart);
            string tempSignature = VendorLinkResolver.ResolveTempSignature(stageHint, vendorName, allToursLink);

            var previewDate = DateTime.Today;
            string dateText = previewDate.ToString("MMMM d, yyyy");
            string tourDayOfWeek = previewDate.ToString("dddd");
            string monthOfTour = previewDate.ToString("MMMM");
            string tourMonthAndDayOrdinal = $"{monthOfTour} {GetDayOrdinal(previewDate.Day)}";
            string displayDate = previewDate.ToString("M/d/yyyy");
            string displayDayOrdinal = GetDayOrdinal(previewDate.Day);
            string displayTime = FormatAsAmPm(tourStart);
            string tourStartTime = FormatAsAmPm(tourStart);
            string meetingTimeDisplay = FormatAsAmPm(meetingTimeLocal);

            string walkerName = "[Sample Walker]";
            var (walkerFirstName, walkerLastName) = SplitPersonName(walkerName);
            string phoneNumber = "[555-1234]";
            var tempSignatureDisplay = ResolveTokenDisplayValue(tempSignature, "tempSignature");

            text = text.Replace("{walker}", walkerName)
                       .Replace("{walkerFirst}", walkerFirstName)
                       .Replace("{walkerLast}", walkerLastName)
                       .Replace("{name}", walkerName)
                       .Replace("{guest}", string.Empty)
                       .Replace("{phone}", phoneNumber)
                       .Replace("{guide}", guideFull)
                       .Replace("{guideFirstName}", guideFirst)
                       .Replace("{guideLastName}", guideLast)
                       .Replace("{guidePhone}", guidePhone)
                       .Replace("{guideEmail}", guideEmail)
                       .Replace("{tour}", tourName)
                       .Replace("{tourname}", tourName)
                       .Replace("{tourStartTime}", tourStartTime)
                       .Replace("{meetingLocation}", meetingPlace)
                       .Replace("{meetingTime}", meetingTimeDisplay)
                       .Replace("{tempMeetingTime}", tempMeetingTime)
                       .Replace("{startMinus10}", tempMeetingTime)
                       .Replace("{tempSignature}", tempSignatureDisplay)
                       .Replace("{meetingInstructions}", meetingInstructions)
                       .Replace("{signature}", signature ?? string.Empty)
                       .Replace("{vendorName}", vendorName)
                       .Replace("{date}", dateText)
                       .Replace("{tourDayOfWeek}", tourDayOfWeek)
                       .Replace("{tourMonthAndDayOrdinal}", tourMonthAndDayOrdinal)
                       .Replace("{monthOfTour}", monthOfTour)
                       .Replace("{displayDate}", displayDate)
                       .Replace("{displayDayOrdinal}", displayDayOrdinal)
                       .Replace("{displayTime}", displayTime);
            text = ApplyVendorLinkTokenReplacements(text, vendorLink, vendorTourLink, vendorReviewLink, allToursLink);
            text = text.Replace("{galleryLink}", ResolveLinkTokenDisplayValue(createTabGalleryLink, "galleryLink"));

            return text;
        }

        private List<DateTime> BuildPreviewDateOptions()
        {
            var list = new List<DateTime>();
            for (var i = 0; i < 30; i++)
            {
                list.Add(DateTime.Today.AddDays(i));
            }
            return list;
        }

        private List<string> BuildPreviewVendorOptions()
        {
            var vendors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var vendor in availableVendors.Where(v => v.IsActive))
            {
                if (!string.IsNullOrWhiteSpace(vendor.VendorName))
                {
                    vendors.Add(vendor.VendorName.Trim());
                }
            }
            foreach (var tour in availableTours)
            {
                foreach (var vendor in SplitVendors(tour.VendorNames))
                {
                    vendors.Add(vendor);
                }
            }
            return vendors.OrderBy(v => v).ToList();
        }

        private IEnumerable<string> SplitVendors(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) yield break;
            var parts = raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                if (!string.IsNullOrWhiteSpace(part))
                {
                    yield return part.Trim();
                }
            }
        }

        private string BuildPreviewMessage()
        {
            var template = GetPreviewTemplate();
            if (template == null)
            {
                return string.Empty;
            }

            var tour = previewTourId.HasValue ? availableTours.FirstOrDefault(t => t.Id == previewTourId.Value) : null;
            var guide = previewGuideId.HasValue ? availableDbGuides.FirstOrDefault(g => g.Id == previewGuideId.Value) : null;

            var date = GetPreviewDate();
            var guestName = string.IsNullOrWhiteSpace(previewGuestName) ? "[Guest]" : previewGuestName!.Trim();

            var guideFirst = guide?.FirstName ?? string.Empty;
            var guideLast = guide?.LastName ?? string.Empty;
            var guideFull = string.IsNullOrWhiteSpace(guideLast) ? guideFirst : $"{guideFirst} {guideLast}";
            var guidePhone = guide?.Phone ?? "[555-1234]";
            var guideEmail = guide?.Email ?? string.Empty;

            var tourName = tour != null ? GetMasterTourName(tour) : (template.TourName ?? string.Empty);
            var tourStart = template.TourStartTime ?? tour?.TourStartTime ?? tour?.MeetingTime ?? string.Empty;
            var meetingLocation = template.MeetingPlace ?? tour?.MeetingPlace ?? string.Empty;
            var meetingTime = template.MeetingTime ?? tour?.MeetingTime ?? tour?.TourStartTime ?? string.Empty;
            var meetingInstructions = template.MeetingInstructions ?? tour?.MeetingInstructions ?? string.Empty;

            var vendorName = string.IsNullOrWhiteSpace(previewVendorName) ? GetPrimaryVendorName(tour) : previewVendorName!.Trim();
            var signature = template.Signature ?? string.Empty;
            var stageHint = $"{template.MessageType} {template.MessageName}".Trim();
            var resolvedLinks = ResolveVendorLinks(
                tour?.Id,
                tourName,
                vendorName,
                stageHint,
                template.VendorLink,
                tour?.VendorLink,
                tour?.ReviewLink);
            var vendorLink = resolvedLinks.VendorLink;
            var vendorTourLink = resolvedLinks.VendorTourLink;
            var vendorReviewLink = resolvedLinks.VendorReviewLink;
            var allToursLink = resolvedLinks.AllToursLink;
            var tempMeetingTime = ComputeTempMeetingTime(tourStart);
            var tempSignature = VendorLinkResolver.ResolveTempSignature(stageHint, vendorName, allToursLink);
            var tempSignatureDisplay = ResolveTokenDisplayValue(tempSignature, "tempSignature");
            var (walkerFirstName, walkerLastName) = SplitPersonName(guestName);

            var tourDayOfWeek = date.ToString("dddd");
            var monthOfTour = date.ToString("MMMM");
            var tourMonthAndDayOrdinal = $"{monthOfTour} {GetDayOrdinal(date.Day)}";
            var displayDate = date.ToString("M/d/yyyy");
            var displayDayOrdinal = GetDayOrdinal(date.Day);
            var displayTime = FormatAsAmPm(tourStart);
            var dateText = date.ToString("MMMM d, yyyy");

            var text = template.MessageContent ?? string.Empty;
            text = text.Replace("{walker}", guestName)
                       .Replace("{walkerFirst}", walkerFirstName)
                       .Replace("{walkerLast}", walkerLastName)
                       .Replace("{name}", guestName)
                       .Replace("{guest}", string.Empty)
                       .Replace("{phone}", guidePhone)
                       .Replace("{guide}", guideFull)
                       .Replace("{guideFirstName}", guideFirst)
                       .Replace("{guideLastName}", guideLast)
                       .Replace("{guidePhone}", guidePhone)
                       .Replace("{guideEmail}", guideEmail)
                       .Replace("{tour}", tourName)
                       .Replace("{tourname}", tourName)
                       .Replace("{tourStartTime}", FormatAsAmPm(tourStart))
                       .Replace("{meetingLocation}", meetingLocation)
                       .Replace("{meetingTime}", FormatAsAmPm(meetingTime))
                       .Replace("{tempMeetingTime}", tempMeetingTime)
                       .Replace("{startMinus10}", tempMeetingTime)
                       .Replace("{tempSignature}", tempSignatureDisplay)
                       .Replace("{meetingInstructions}", meetingInstructions)
                       .Replace("{signature}", signature)
                       .Replace("{vendorName}", vendorName)
                       .Replace("{date}", dateText)
                       .Replace("{tourDayOfWeek}", tourDayOfWeek)
                       .Replace("{tourMonthAndDayOrdinal}", tourMonthAndDayOrdinal)
                       .Replace("{monthOfTour}", monthOfTour)
                       .Replace("{displayDate}", displayDate)
                       .Replace("{displayDayOrdinal}", displayDayOrdinal)
                       .Replace("{displayTime}", displayTime);
            text = ApplyVendorLinkTokenReplacements(text, vendorLink, vendorTourLink, vendorReviewLink, allToursLink);
            text = text.Replace("{galleryLink}", ResolveLinkTokenDisplayValue(previewTabGalleryLink, "galleryLink"));

            return text.Replace("&amp;", "&");
        }

        private async Task RefreshGalleryContextAsync()
        {
            try
            {
                var createTour = newMessageTourId.HasValue
                    ? availableTours.FirstOrDefault(t => t.Id == newMessageTourId.Value)
                    : null;
                var createTourName = newMessageTourName
                    ?? (createTour != null ? GetMasterTourName(createTour) : string.Empty);
                var createTourTime = newMessageTourStartTime
                    ?? createTour?.TourStartTime
                    ?? createTour?.MeetingTime
                    ?? string.Empty;
                var createVendorName = string.IsNullOrWhiteSpace(newMessageVendorName)
                    ? GetPrimaryVendorName(createTour)
                    : newMessageVendorName!.Trim();
                createTabGalleryLink = await FetchGalleryUrlForTourAsync(
                    DateTime.Today.ToString("yyyy-MM-dd"),
                    createTourName,
                    createTourTime,
                    createVendorName);

                var previewTemplate = GetPreviewTemplate();
                var previewTour = previewTourId.HasValue
                    ? availableTours.FirstOrDefault(t => t.Id == previewTourId.Value)
                    : null;
                var previewTourName = previewTour != null
                    ? GetMasterTourName(previewTour)
                    : (previewTemplate?.TourName ?? string.Empty);
                var previewTourTime = previewTemplate?.TourStartTime
                    ?? previewTour?.TourStartTime
                    ?? previewTour?.MeetingTime
                    ?? string.Empty;
                var previewVendor = string.IsNullOrWhiteSpace(previewVendorName)
                    ? GetPrimaryVendorName(previewTour)
                    : previewVendorName!.Trim();

                previewTabGalleryLink = await FetchGalleryUrlForTourAsync(
                    GetPreviewDate().ToString("yyyy-MM-dd"),
                    previewTourName,
                    previewTourTime,
                    previewVendor);
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Gallery context refresh failed for MessagesComponent");
                createTabGalleryLink = null;
                previewTabGalleryLink = null;
            }
        }

        private DbTourMessage? GetPreviewTemplate()
        {
            if (dbMessages.Count == 0) return null;
            var key = NormalizePreviewKey(previewMessageTime);
            var match = dbMessages.FirstOrDefault(m => NormalizePreviewKey(m.MessageName) == key);
            match ??= dbMessages.FirstOrDefault(m => NormalizePreviewKey(m.MessageType) == key);
            return match ?? dbMessages.FirstOrDefault();
        }

        private string NormalizePreviewKey(string? value)
        {
            var input = (value ?? string.Empty).Trim().ToUpperInvariant();
            return Regex.Replace(input, "[^A-Z0-9]+", string.Empty);
        }

        private DateTime GetPreviewDate()
        {
            return previewMessageTime switch
            {
                "TOMORROW" => DateTime.Today.AddDays(1),
                "DATE" => previewDate.Date,
                _ => DateTime.Today
            };
        }

        private string GetPrimaryVendorName(DbTour? tour)
        {
            if (tour == null || string.IsNullOrWhiteSpace(tour.VendorNames)) return string.Empty;
            var raw = tour.VendorNames.Trim();
            var first = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            return first ?? raw;
        }

        private VendorLinkResolution ResolveVendorLinks(
            int? tourId,
            string? tourName,
            string? vendorName,
            string? stageHint,
            string? templateVendorLink,
            string? legacyTourVendorLink,
            string? legacyReviewLink)
        {
            return VendorLinkResolver.Resolve(
                tourId,
                tourName,
                vendorName,
                stageHint,
                templateVendorLink,
                legacyTourVendorLink,
                legacyReviewLink,
                availableTourLinks,
                availableVendors,
                availableTours,
                activeVendorAssignments);
        }

        private static string ResolveLinkTokenDisplayValue(string? value, string tokenName)
        {
            return string.IsNullOrWhiteSpace(value)
                ? $"[missing {tokenName}]"
                : value.Trim();
        }

        private static string ResolveTokenDisplayValue(string? value, string tokenName)
        {
            return string.IsNullOrWhiteSpace(value)
                ? $"[missing {tokenName}]"
                : value.Trim();
        }

        private async Task<string?> FetchGalleryUrlForTourAsync(string? tourDate, string? tourName, string? tourTime, string? vendorName)
        {
            if (string.IsNullOrWhiteSpace(tourDate) ||
                string.IsNullOrWhiteSpace(tourName))
            {
                return null;
            }

            if (!DateTime.TryParse(tourDate, out var parsedDate))
            {
                return null;
            }

            try
            {
                var normalizedTourTime = string.IsNullOrWhiteSpace(tourTime) ? null : tourTime.Trim();
                var resolved = await GalleryLinkResolver.ResolveAsync(parsedDate.Date, tourName, normalizedTourTime, vendorName);
                return resolved.Found ? resolved.Url : null;
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Gallery lookup failed for tour {TourName} on {TourDate} at {TourTime}", tourName, tourDate, tourTime);
            }

            return null;
        }

        private static (string First, string Last) SplitPersonName(string? fullName)
        {
            var raw = (fullName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return (string.Empty, string.Empty);
            }

            var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1)
            {
                return (parts[0], string.Empty);
            }

            return (parts[0], string.Join(" ", parts.Skip(1)));
        }

        private static string ApplyVendorLinkTokenReplacements(
            string text,
            string? vendorLink,
            string? vendorTourLink,
            string? vendorReviewLink,
            string? allToursLink)
        {
            return text
                .Replace("{vendorLink}", ResolveLinkTokenDisplayValue(vendorLink, "vendorLink"))
                .Replace("{vendorTourLink}", ResolveLinkTokenDisplayValue(vendorTourLink, "vendorTourLink"))
                .Replace("{vendorReviewLink}", ResolveLinkTokenDisplayValue(vendorReviewLink, "vendorReviewLink"))
                .Replace("{allToursLink}", ResolveLinkTokenDisplayValue(allToursLink, "allToursLink"));
        }

        private string FormatAsAmPm(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            if (DateTime.TryParse(raw.Trim(), out var dt))
            {
                return dt.ToString("h:mm tt");
            }
            if (TimeSpan.TryParse(raw.Trim(), out var ts))
            {
                return DateTime.Today.Add(ts).ToString("h:mm tt");
            }
            return raw.Trim();
        }

        private string ShiftTimeByMinutes(string? raw, int minutes)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            if (DateTime.TryParse(raw.Trim(), out var dt))
            {
                return dt.AddMinutes(minutes).ToString("HH:mm");
            }
            if (TimeSpan.TryParse(raw.Trim(), out var ts))
            {
                return DateTime.Today.Add(ts).AddMinutes(minutes).ToString("HH:mm");
            }
            return raw.Trim();
        }

        private string ComputeTempMeetingTime(string? tourStart)
        {
            if (string.IsNullOrWhiteSpace(tourStart)) return string.Empty;
            var shifted = ShiftTimeByMinutes(tourStart, -10);
            return FormatAsAmPm(string.IsNullOrWhiteSpace(shifted) ? tourStart : shifted);
        }

        private string GetDayOrdinal(int day)
        {
            if (day <= 0) return day.ToString();

            return (day % 100) switch
            {
                11 or 12 or 13 => $"{day}th",
                _ => (day % 10) switch
                {
                    1 => $"{day}st",
                    2 => $"{day}nd",
                    3 => $"{day}rd",
                    _ => $"{day}th"
                }
            };
        }

        private async Task InsertTemplateField(string textareaId, string field)
        {
            try
            {
                await JSRuntime.InvokeVoidAsync("eval", @"
                if (!window.insertTextAtCursor) {
                    window.insertTextAtCursor = (textareaId, textToInsert) => {
                        try {
                            const textarea = document.getElementById(textareaId);
                            if (textarea) {
                                const start = textarea.selectionStart;
                                const end = textarea.selectionEnd;
                                const currentValue = textarea.value;
                                const newValue = currentValue.substring(0, start) + textToInsert + currentValue.substring(end);
                                textarea.value = newValue;
                                textarea.selectionStart = start + textToInsert.length;
                                textarea.selectionEnd = start + textToInsert.length;
                                textarea.dispatchEvent(new Event('input', { bubbles: true }));
                                textarea.focus();
                            } else {
                                console.error('Textarea not found with id:', textareaId);
                            }
                        } catch (e) {
                            console.error('insertTextAtCursor error:', e);
                        }
                    };
                }
            ");

                await JSRuntime.InvokeVoidAsync("insertTextAtCursor", textareaId, field);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "InsertTemplateField failed for {TextareaId} with field {Field}", textareaId, field);
            }
        }
    }
}
