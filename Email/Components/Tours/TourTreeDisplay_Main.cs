// 2025-12-06 15:20 CST - extracted code-behind from TourTreeDisplay.razor
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Email.Models;
using Email.Models.Reports;
using Email.Services;
using Email.TourTreeViewShapedData.Enums;
using Email.TourTreeViewShapedData.Models;

namespace Email.Components.Tours
{
    public partial class TourTreeDisplay : ComponentBase
    {
        // 2025-11-18: Parameters - migrated from workstation TourTreeDisplay
        // NOTE: Email project uses ShapedTreeData/ShapedTreeNode (not TreeData/TreeNode from workstation)
        // Original signature included additional parameters (commented for future implementation):
        // List<Guide> InMemoryGuides, List<InMemoryMessageTemplate> InMemoryMessages,
        // EventCallback<TreeNode> OnTemplateCreate, EventCallback OnOpenGuideManagement,
        // EventCallback OnOpenMessageManagement, EventCallback OnOpenToursManagement

        [Parameter] public ShapedTreeFilterArgs? Args { get; set; }
        [Parameter] public EventCallback<ShapedTreeFilterArgs> OnApply { get; set; }
        [Parameter] public ShapedTreeData? TourData { get; set; }
        [Parameter] public bool IsLoading { get; set; }
        [Parameter] public EventCallback<ShapedTreeNode> OnWalkerSelected { get; set; }
        [Parameter] public EventCallback<ShapedTreeNode> OnVCardDownload { get; set; }
        [Parameter] public EventCallback<ShapedTreeNode> OnSmsSend { get; set; }
        [Parameter] public EventCallback<ShapedTreeNode> OnWhatsAppSend { get; set; }
        [Parameter] public EventCallback<ShapedTreeNode> OnEmailView { get; set; }
        [Parameter] public EventCallback OnDataRefresh { get; set; }
        [Parameter] public EventCallback<BookingPrefillRequest> OnAddBookingFromTour { get; set; }
        [Parameter] public bool UseIPhoneCopyMode { get; set; }

        // Header management callbacks (added step 3 - no UI change yet)
        [Parameter] public EventCallback OnOpenGuideManagement { get; set; }
        [Parameter] public EventCallback OnOpenMessageManagement { get; set; }
        [Parameter] public EventCallback OnOpenToursManagement { get; set; }

        [Inject] protected IVendorsApiService VendorsApi { get; set; } = default!;
        [Inject] protected ITourLinkService TourLinkService { get; set; } = default!;
        [Inject] protected IToastService ToastService { get; set; } = default!;
        [Inject] protected NavigationManager Navigation { get; set; } = default!;

        // Dropdown data sources (guides and in-memory message templates)
        [Parameter] public List<DbGuide> DbGuides { get; set; } = new();
        [Parameter] public List<InMemoryMessageTemplate> InMemoryMessages { get; set; } = new();

        // Per-tour selections (Key: "DateLabel|TourLabel")
        private Dictionary<string, int?> selectedGuideIdByTour = new();
        private Dictionary<string, string?> selectedMessageIdByTour = new();
        private readonly HashSet<string> blankQuickSendTourKeys = new(StringComparer.OrdinalIgnoreCase);
        private Email.Services.TourCatalogSnapshot? tourCatalog;
        private List<string> catalogTourNames = new();
        private ShapedTreeData? _prevTourData;
        private List<DbVendor> availableVendors = new();
        private Dictionary<string, HashSet<string>> activeVendorAssignments = new(StringComparer.OrdinalIgnoreCase);
        private List<TourLink> availableTourLinks = new();
        private bool vendorsLoaded;
        private bool tourLinksLoaded;
        private IReadOnlyList<string> VendorFilterOptions
        {
            get
            {
                var options = availableVendors
                    .Where(v => v.IsActive && !string.IsNullOrWhiteSpace(v.VendorName))
                    .Select(v => v.VendorName.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(v => v)
                    .ToList();

                if (options.Count == 0 && TourData?.VendorCounts != null)
                {
                    options = TourData.VendorCounts.Keys
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                        .Select(v => v.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(v => v)
                        .ToList();
                }

                return options;
            }
        }
       
        //todo this might need to be dynamic
        private static readonly string[] stageOrder = new[] { "welcome", "tomorrow", "dayOf", "thankyou", "promo", "misc" };
        private readonly HashSet<string> contactStateBusyKeys = new(StringComparer.OrdinalIgnoreCase);
        private const string ContactChannelSms = "sms";
        private const string ContactChannelWa = "wa";
        private const string ContactChannelPlatform = "platform";
        private const string ContactChannelSmsLabel = "SMS";
        private const string ContactChannelWaLabel = "WA";
        private static readonly TimeZoneInfo EasternTimeZone = ResolveEasternTimeZone();

        protected override async Task OnParametersSetAsync()
        {
            var firstTreeBind = _prevTourData == null && TourData != null;

            // 2026-02-03 - Clear selection state if data object refreshed so defaults re-apply correctly
            if (TourData != _prevTourData)
            {
                selectedGuideIdByTour.Clear();
                selectedMessageIdByTour.Clear();
                blankQuickSendTourKeys.Clear();
                _prevTourData = TourData;
            }

            if (firstTreeBind)
            {
                // Apply cached expansion before first await to avoid initial expanded flash.
                RestoreTreeCacheStateIfAvailable();
            }

            await EnsureCatalogAsync();
            await EnsureVendorsAsync();
            await EnsureTourLinksAsync();
            await PreselectGuidesFromDefaultsAsync();
            PreselectMessagesFromDefaults();
            if (firstTreeBind)
            {
                RestoreTreeCacheStateIfAvailable();
            }
        }

        // 2025-11-18: Toggle node expansion
        private void ToggleNode(ShapedTreeNode node)
        {
            try { _ = JSRuntime.InvokeVoidAsync("console.log", $"TourTreeDisplay.ToggleNode: {node.Label} -> {(!node.IsExpanded)}"); } catch { }
            node.IsExpanded = !node.IsExpanded;
            PersistTreeCacheState();
        }

        // 2025-12-07 00:00 UTC - Helper to mark booking as messaged (SMS/WhatsApp clicks)
        private async Task MarkBookingMessageSentAsync(ShapedTreeNode walkerNode)
        {
            var w = walkerNode?.WalkerData;
            if (w == null) return;
            if (string.IsNullOrWhiteSpace(w.MessageId) || string.IsNullOrWhiteSpace(w.BookingCode)) return;

            try
            {
                var ok = await EmailsService.SetBookingMessageSentAsync(w.MessageId, w.BookingCode, true);
                if (ok)
                {
                    w.MessageSent = true;
                    w.MessageSentAtUtc = DateTime.UtcNow;
                    StateHasChanged();
                }
            }
            catch
            {
                // silent; UI should not block send
            }
        }

        // 2025-12-07 00:00 UTC - Toggle message sent flag from icon click
        private async Task ToggleMessageSentAsync(ShapedTreeNode walkerNode)
        {
            var w = walkerNode?.WalkerData;
            if (w == null) return;
            if (string.IsNullOrWhiteSpace(w.MessageId) || string.IsNullOrWhiteSpace(w.BookingCode)) return;

            var newValue = !w.MessageSent;
            try
            {
                var ok = await EmailsService.SetBookingMessageSentAsync(w.MessageId, w.BookingCode, newValue);
                if (ok)
                {
                    w.MessageSent = newValue;
                    w.MessageSentAtUtc = newValue ? DateTime.UtcNow : (DateTime?)null;
                    var templateId = ResolveTemplateIdForTour(walkerNode);
                    await LogBookingMessageEventAsync(
                        w,
                        stage: "misc",
                        channel: string.Empty,
                        templateId: templateId,
                        triggerType: "manual_toggle");
                    StateHasChanged();
                }
            }
            catch
            {
                // silent fail
            }
        }

        // 2026-03-13 - Contact outcome toggle (desktop tree). SMS/WA cycle; platform is binary checkbox.
        private async Task ToggleBookingContactChannelStateAsync(ShapedTreeNode walkerNode, string channel)
        {
            var w = walkerNode?.WalkerData;
            if (w == null) return;
            if (string.IsNullOrWhiteSpace(w.MessageId) || string.IsNullOrWhiteSpace(w.BookingCode)) return;

            var normalizedChannel = NormalizeContactChannel(channel);
            if (normalizedChannel == null) return;

            var busyKey = BuildContactStateBusyKey(w, normalizedChannel);
            if (!contactStateBusyKeys.Add(busyKey))
            {
                return;
            }

            try
            {
                ContactChannelStateResult? result;
                if (normalizedChannel == ContactChannelPlatform)
                {
                    var nextState = w.PlatformContactState == 1 ? (byte)0 : (byte)1;
                    result = await EmailsService.SetBookingContactChannelStateAsync(
                        w.MessageId,
                        w.BookingCode,
                        normalizedChannel,
                        nextState,
                        source: "desktop-tree");
                }
                else
                {
                    result = await EmailsService.CycleBookingContactChannelStateAsync(
                        w.MessageId,
                        w.BookingCode,
                        normalizedChannel,
                        source: "desktop-tree");
                }

                if (result != null)
                {
                    ApplyContactState(w, normalizedChannel, result.ContactState);
                    StateHasChanged();
                }
            }
            catch
            {
                // silent fail
            }
            finally
            {
                contactStateBusyKeys.Remove(busyKey);
            }
        }

        // 2026-03-13 - Quick-send dual-write helper to force channel state green in new table.
        private async Task MarkBookingContactChannelQuickSendAsync(ShapedTreeNode walkerNode, string channel, string source = "desktop-tree")
        {
            var w = walkerNode?.WalkerData;
            if (w == null) return;
            if (string.IsNullOrWhiteSpace(w.MessageId) || string.IsNullOrWhiteSpace(w.BookingCode)) return;

            var normalizedChannel = NormalizeContactChannel(channel);
            if (normalizedChannel == null) return;

            try
            {
                var result = await EmailsService.MarkBookingContactChannelQuickSendAsync(
                    w.MessageId,
                    w.BookingCode,
                    normalizedChannel,
                    source: source);

                if (result != null)
                {
                    ApplyContactState(w, normalizedChannel, result.ContactState);
                    StateHasChanged();
                }
            }
            catch
            {
                // silent fail
            }
        }

        private bool IsContactStateBusy(ShapedWalkerData? walker, string channel)
        {
            if (walker == null) return false;
            var normalizedChannel = NormalizeContactChannel(channel);
            if (normalizedChannel == null) return false;
            return contactStateBusyKeys.Contains(BuildContactStateBusyKey(walker, normalizedChannel));
        }

        private static string BuildContactStateBusyKey(ShapedWalkerData walker, string channel)
        {
            return $"{walker.MessageId?.Trim()}||{walker.BookingCode?.Trim()}||{channel}";
        }

        private static string? NormalizeContactChannel(string? channel)
        {
            if (string.IsNullOrWhiteSpace(channel)) return null;
            var normalized = channel.Trim().ToLowerInvariant();
            return normalized is ContactChannelSms or ContactChannelWa or ContactChannelPlatform ? normalized : null;
        }

        private static void ApplyContactState(ShapedWalkerData walker, string channel, byte state)
        {
            var normalizedState = state > 3 ? (byte)0 : state;
            if (channel == ContactChannelSms)
            {
                walker.SmsContactState = normalizedState;
                return;
            }

            if (channel == ContactChannelWa)
            {
                walker.WaContactState = normalizedState;
                return;
            }

            if (channel == ContactChannelPlatform)
            {
                walker.PlatformContactState = normalizedState;
            }
        }

        private static string GetContactStateColorClass(byte state)
        {
            return state switch
            {
                1 => "text-success",
                2 => "text-danger",
                3 => "text-secondary",
                _ => "text-contact-yellow"
            };
        }

        private static string GetContactStateToggleTitle(string channelLabel, byte currentState)
        {
            var currentLabel = GetContactStateLabel(currentState);
            var nextLabel = GetContactStateLabel((byte)((currentState + 1) % 4));
            return $"{channelLabel}: {currentLabel}. Click to set {nextLabel}.";
        }

        private static string GetPlatformContactToggleTitle(byte currentState)
        {
            return currentState == 1
                ? "Platform: contacted. Click to uncheck."
                : "Platform: not contacted. Click to mark contacted.";
        }

        private static string GetPlatformContactIconClass(byte state)
        {
            return state == 1 ? "bi bi-check-square-fill text-success" : "bi bi-square text-secondary";
        }

        private static string GetContactStateLabel(byte state)
        {
            return state switch
            {
                1 => "Green (contacted)",
                2 => "Red (failed)",
                3 => "Gray (skipped / unknown)",
                _ => "Yellow (no outcome)"
            };
        }

        // 2025-12-09 00:00 UTC - Toggle per-stage badge
        private async Task ToggleMessageStageAsync(ShapedTreeNode walkerNode, string stage)
        {
            var w = walkerNode?.WalkerData;
            if (w == null) return;
            if (string.IsNullOrWhiteSpace(w.MessageId) || string.IsNullOrWhiteSpace(w.BookingCode)) return;
            if (string.IsNullOrWhiteSpace(stage)) return;

            var current = false;
            if (w.MessageStages != null && w.MessageStages.TryGetValue(stage, out var status))
            {
                current = status?.SentFlag == true;
            }

            var newValue = !current;
            var templateId = ResolveTemplateIdForTour(walkerNode);

            try
            {
                var ok = await EmailsService.SetBookingMessageStageAsync(w.MessageId, w.BookingCode, NormalizeStage(stage), newValue, null, templateId, w.VendorName);
                if (ok)
                {
                    EnsureMessageStages(w);
                    var normalizedStage = NormalizeStage(stage);
                    w.MessageStages[normalizedStage] = new MessageStageStatus
                    {
                        Stage = normalizedStage,
                        SentFlag = newValue,
                        SentAtUtc = newValue ? DateTime.UtcNow : (DateTime?)null,
                        Channel = null,
                        TemplateId = templateId
                    };
                    await LogBookingMessageEventAsync(
                        w,
                        stage: normalizedStage,
                        channel: string.Empty,
                        templateId: templateId,
                        triggerType: "manual_toggle");
                    StateHasChanged();
                }
            }
            catch
            {
                // silent fail
            }
        }

        private static string NormalizeStage(string stage)
        {
            var s = (stage ?? string.Empty).Trim().ToLowerInvariant();
            return s switch
            {
                "welcome" => "welcome",
                "tomorrow" => "tomorrow",
                "tmrw" => "tomorrow",
                "reminder" => "tomorrow",
                "dayof" => "dayOf",
                "day-of" => "dayOf",
                "day_of" => "dayOf",
                "day of" => "dayOf",
                "thankyou" => "thankyou",
                "thank you" => "thankyou",
                "thanks" => "thankyou",
                "promo" => "promo",
                "promotion" => "promo",
                "marketing" => "promo",
                "basedondate" => "misc",
                "based-on-date" => "misc",
                "based_on_date" => "misc",
                "custom" => "misc",
                "misc" => "misc",
                _ => "misc"
            };
        }

        private static string GetStageAbbreviation(string stage)
        {
            return NormalizeStage(stage) switch
            {
                "welcome" => "W",
                "tomorrow" => "T",
                "dayOf" => "D",
                "thankyou" => "Y",
                "promo" => "P",
                _ => "M"
            };
        }

        private int? ResolveTemplateIdForTour(ShapedTreeNode? tourNode)
        {
            int? templateId = null;

            var tourLabel = tourNode?.Label;
            if (!string.IsNullOrWhiteSpace(tourLabel) &&
                selectedMessageIdByTour.TryGetValue(tourLabel, out var selMsgId) &&
                !string.IsNullOrWhiteSpace(selMsgId))
            {
                if (int.TryParse(selMsgId, out var parsedId))
                {
                    templateId = parsedId;
                }
            }

            return templateId;
        }

        private async Task AutoMarkStageAsync(ShapedTreeNode walkerNode, ShapedTreeNode tourNode, string stage, string channel, int? templateId)
        {
            var w = walkerNode?.WalkerData;
            if (w == null) return;
            if (string.IsNullOrWhiteSpace(w.MessageId) || string.IsNullOrWhiteSpace(w.BookingCode)) return;

            var normalizedStage = NormalizeStage(stage);
            var normalizedChannel = string.IsNullOrWhiteSpace(channel) ? null : channel;
            var vendorName = w.VendorName;

            try
            {
                var ok = await EmailsService.SetBookingMessageStageAsync(w.MessageId, w.BookingCode, normalizedStage, true, normalizedChannel, templateId, vendorName);
                if (ok)
                {
                    EnsureMessageStages(w);
                    w.MessageStages[normalizedStage] = new MessageStageStatus
                    {
                        Stage = normalizedStage,
                        SentFlag = true,
                        SentAtUtc = DateTime.UtcNow,
                        Channel = normalizedChannel,
                        TemplateId = templateId
                    };
                    await LogBookingMessageEventAsync(
                        w,
                        stage: normalizedStage,
                        channel: normalizedChannel,
                        templateId: templateId,
                        triggerType: "open_channel");
                    StateHasChanged();
                }
            }
            catch
            {
                // silent
            }
        }

        private void EnsureMessageStages(ShapedWalkerData w)
        {
            if (w.MessageStages == null)
            {
                w.MessageStages = new Dictionary<string, MessageStageStatus>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private async Task LogBookingMessageEventAsync(
            ShapedWalkerData walker,
            string stage,
            string? channel,
            int? templateId,
            string triggerType)
        {
            if (walker == null)
            {
                return;
            }

            var selectedTemplate = templateId.HasValue
                ? InMemoryMessages?.FirstOrDefault(t => string.Equals(t.Id, templateId.Value.ToString(), StringComparison.Ordinal))
                : null;

            await BookingsInboxService.LogMessageEventAsync(new Email.Models.Reports.BookingMessageEventWriteModel
            {
                BookingId = null,
                CustomerIdentifier = null,
                BookingCode = walker.BookingCode,
                MessageId = walker.MessageId,
                TourName = walker.TourName,
                TourDate = DateTime.TryParse(walker.TourDate, out var parsedDate) ? parsedDate : null,
                TourTime = walker.TourTime,
                VendorName = walker.VendorName,
                Stage = stage,
                Channel = channel ?? string.Empty,
                TemplateId = templateId,
                TemplateType = selectedTemplate?.Type,
                TemplateName = selectedTemplate?.Name,
                TriggerType = triggerType,
                TriggeredBy = "tour-tree",
                SentAtUtc = DateTime.UtcNow
            });
        }

        private string DetermineStageForTour(string? tourLabel)
        {
            if (string.IsNullOrWhiteSpace(tourLabel)) return "misc";

            if (selectedMessageIdByTour.TryGetValue(tourLabel, out var selMsgId) &&
                !string.IsNullOrWhiteSpace(selMsgId))
            {
                var tmpl = InMemoryMessages?.FirstOrDefault(m => string.Equals(m.Id, selMsgId, StringComparison.Ordinal));
                var t = tmpl?.Type ?? string.Empty;
                var normalized = NormalizeStage(t);
                if (!string.Equals(normalized, "misc", StringComparison.OrdinalIgnoreCase))
                {
                    return normalized;
                }

                // Fallback: infer from message name keywords if Type not mapped
                var name = (tmpl?.Name ?? string.Empty).ToLowerInvariant();
                if (name.Contains("welcome")) return "welcome";
                if (name.Contains("tomorrow") || name.Contains("tmrw") || name.Contains("reminder")) return "tomorrow";
                if (name.Contains("dayof") || name.Contains("day of") || name.Contains("day-of")) return "dayOf";
                if (name.Contains("thank")) return "thankyou";
                if (name.Contains("promo") || name.Contains("promotion") || name.Contains("marketing")) return "promo";

                return "misc";
            }

            return "misc";
        }

        // 2025-11-18: Select walker (triggers callback)
        private async Task SelectWalker(ShapedTreeNode walkerNode)
        {
            try { await JSRuntime.InvokeVoidAsync("console.log", $"TourTreeDisplay.SelectWalker: {walkerNode.Label}"); } catch { }
            WalkerSelectionConsoleLogger.LogSelection(TourData, walkerNode);
            if (OnWalkerSelected.HasDelegate)
            {
                await OnWalkerSelected.InvokeAsync(walkerNode);
            }
        }

        // 2025-11-19 00:00 UTC - Download individual vCard (adapted to shaped data, verbatim content)
        private async Task DownloadIndividualVcard(MouseEventArgs e, ShapedTreeNode walkerNode)
        {
            try { await JSRuntime.InvokeVoidAsync("console.log", $"TourTreeDisplay.DownloadIndividualVcard: {walkerNode.Label}"); } catch { }

            var w = walkerNode?.WalkerData;
            if (w == null)
            {
                VCardLogService.LogAction("Desktop Button Clicked", $"DownloadIndividualVcard called but WalkerData is null. Label: {walkerNode?.Label ?? "(no label)"}", null);
                return;
            }

            var safeLabel = (walkerNode.Label ?? "walker").Replace(" ", "_").Replace(",", "");
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"walker_{safeLabel}_{timestamp}.vcf";

            // Primary path (server headers): use messageId for stable lookup.
            string? url = null;
            if (!string.IsNullOrWhiteSpace(w.MessageId))
            {
                url = $"/vcards/walker?messageId={Uri.EscapeDataString(w.MessageId)}";
            }

            VCardLogService.LogAction("Desktop Button Clicked", $"DownloadIndividualVcard button clicked. Walker: {walkerNode.Label ?? "(no label)"}, MessageId: {w.MessageId ?? "(none)"}", url ?? "(no URL - missing MessageId)");

            // 2026-05-31 - Direct blob download (no window.open tab).
            // Previously this opened /vcards/walker via window.open("_blank"), which briefly
            // opened an extra browser tab on desktop. The blob download below produces the same
            // vCard via VCardExportService and downloads it directly with no tab.
            try
            {
                VCardLogService.LogAction("Desktop Fallback Started", $"Starting JS blob fallback download for walker: {walkerNode.Label ?? "(no label)"}", url ?? "(no URL)");
                var formattedDate = FormatDateForVcard(w.TourDate ?? "");
                var vcard = VCardExportService.GenerateVCard(w, dateLabelOverride: formattedDate);
                var content = VCardExportService.GenerateVcf(new[] { vcard });
                await JSRuntime.InvokeVoidAsync("downloadTextFile", content, fileName, "text/vcard");
                VCardLogService.LogAction("Desktop Fallback Success", $"JS blob download completed successfully. Walker: {walkerNode.Label ?? "(no label)"}, File: {fileName}", url ?? "(no URL)");
            }
            catch (Exception ex)
            {
                VCardLogService.LogAction("Desktop Download Exception", $"Exception in DownloadIndividualVcard for walker: {walkerNode.Label ?? "(no label)"}", url ?? "(no URL)", ex);
                Console.WriteLine($"Download failed: {ex.Message}");
            }
        }

        // 2025-12-19 00:00 UTC - PENDING REMOVAL: Replaced by Email.Services.VCardExportService
        // This method is no longer called; all vCard generation now uses VCardExportService for consistency.
        // TODO: Remove this method and EscapeVCardValue after confirming no other references exist.
        /*
        private string GenerateWalkerVcard(Email.TourTreeViewShapedData.Models.ShapedWalkerData w, string formattedDate)
        {
            var name = w.DisplayName ?? "";
            var phone = w.DisplayPhone ?? "";
            var vendorName = w.VendorName ?? "";
            var tourName = w.TourName ?? "";
            var attendees = w.Attendees;

            var vcard = new System.Text.StringBuilder();
            vcard.AppendLine("BEGIN:VCARD");
            vcard.AppendLine("VERSION:3.0");

            vcard.AppendLine($"FN:{EscapeVCardValue(name)}");

            var nameParts = name.Split(' ', 2);
            var firstName = nameParts.Length > 0 ? nameParts[0] : "";
            var lastName = nameParts.Length > 1 ? nameParts[1] : "";
            vcard.AppendLine($"N:{EscapeVCardValue(lastName)};{EscapeVCardValue(firstName)};;;");

            var itemIndex = 1;
            if (!string.IsNullOrWhiteSpace(phone))
            {
                vcard.AppendLine($"item{itemIndex}.TEL;TYPE=CELL,VOICE:{EscapeVCardValue(phone)}");
                vcard.AppendLine($"item{itemIndex}.X-ABLabel:");
                itemIndex++;
            }

            var notesParts = new List<string>
            {
                $"Vendor: {vendorName}",
                $"Tour: {tourName}",
                $"Date: {formattedDate}"
            };
            if (attendees > 0)
            {
                notesParts.Add($"Attendees: {attendees}");
            }
            vcard.AppendLine($"NOTE:{EscapeVCardValue(string.Join(" | ", notesParts))}");

            vcard.AppendLine("END:VCARD");
            return vcard.ToString();
        }
        */

        // 2025-12-19 00:00 UTC - PENDING REMOVAL: Replaced by Email.Services.VCardExportService.EscapeV
        // This method is no longer called; all vCard escaping now uses VCardExportService for consistency.
        // TODO: Remove this method after confirming no other references exist.
        /*
        private static string EscapeVCardValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value
                .Replace("\\", "\\\\")
                .Replace(";", "\\;")
                .Replace(",", "\\,")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r");
        }
        */

        // 2025-11-18: Generic walker event invoker
        private async Task InvokeWalkerEventAsync(MouseEventArgs e, EventCallback<ShapedTreeNode> callback, ShapedTreeNode node)
        {
            try { await JSRuntime.InvokeVoidAsync("console.log", $"TourTreeDisplay.InvokeWalkerEvent: {node.Label}"); } catch { }
            if (callback.HasDelegate)
            {
                await callback.InvokeAsync(node);
            }
        }

        // Guide/message selection handlers
        // Updated: 2025-02-05 - Use composite key (Date|Tour)
        private void OnGuideSelected(string? dateLabel, string? tourLabel, string? value)
        {
            if (string.IsNullOrWhiteSpace(tourLabel)) return;
            var key = GetTourKey(dateLabel, tourLabel);
            if (string.IsNullOrWhiteSpace(value) || value == "none")
            {
                selectedGuideIdByTour[key] = null;
                PersistTreeCacheState();
                return;
            }
            if (int.TryParse(value, out var id))
            {
                selectedGuideIdByTour[key] = id;
                PersistTreeCacheState();
            }
        }

        private void OnMessageSelected(string? dateLabel, string? tourLabel, string? value)
        {
            if (string.IsNullOrWhiteSpace(tourLabel)) return;
            var key = GetTourKey(dateLabel, tourLabel);
            if (string.IsNullOrWhiteSpace(value) || value == "none")
            {
                selectedMessageIdByTour[key] = null;
                PersistTreeCacheState();
                return;
            }
            selectedMessageIdByTour[key] = value;
            PersistTreeCacheState();
        }

        private void ToggleBlankQuickSendForTour(string? dateLabel, string? tourLabel)
        {
            if (string.IsNullOrWhiteSpace(tourLabel))
            {
                return;
            }

            var key = GetTourKey(dateLabel, tourLabel);
            if (!blankQuickSendTourKeys.Add(key))
            {
                blankQuickSendTourKeys.Remove(key);
            }
        }

        private bool IsBlankQuickSendEnabled(string? dateLabel, string? tourLabel)
        {
            if (string.IsNullOrWhiteSpace(tourLabel))
            {
                return false;
            }

            return blankQuickSendTourKeys.Contains(GetTourKey(dateLabel, tourLabel));
        }

        private bool IsBlankQuickSendEnabledForTourNode(ShapedTreeNode? tourNode, string? fallbackDateLabel = null)
        {
            if (tourNode == null)
            {
                return false;
            }

            var dateLabel = fallbackDateLabel;
            if (string.IsNullOrWhiteSpace(dateLabel) && TourData?.TreeNodes != null)
            {
                var dateNode = TourData.TreeNodes.FirstOrDefault(d => d.Children.Contains(tourNode));
                dateLabel = dateNode?.Label;
            }

            return IsBlankQuickSendEnabled(dateLabel, tourNode.Label);
        }

        private async Task CopyConnectMessageToClipboard(ShapedTreeNode? walkerNode, ShapedTreeNode? dateNode)
        {
            var message = BuildConnectPromptMessage(walkerNode, dateNode?.Label);
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            try
            {
                await JSRuntime.InvokeVoidAsync("blazorCopyText", message);
                try { await ToastService.ShowSuccessAsync("Copied", "Connect message copied."); } catch { }
            }
            catch
            {
                // Fallback: open a native prompt with pre-filled text for manual copy.
                try { await JSRuntime.InvokeAsync<string>("prompt", "Copy message:", message); } catch { }
            }
        }

        private string BuildConnectPromptMessage(ShapedTreeNode? walkerNode, string? dateLabel)
        {
            var walkerName = walkerNode?.WalkerData?.DisplayName;
            if (string.IsNullOrWhiteSpace(walkerName))
            {
                var info = ParseWalkerLabel(walkerNode?.Label ?? string.Empty);
                walkerName = info.Name;
            }

            // Inline first-name parse for connect copy text.
            var firstName = walkerName?
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()?
                .Trim('\"', '\'', ',', '.', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}');

            walkerName = string.IsNullOrWhiteSpace(firstName) ? "there" : firstName;

            var formattedDate = FormatDateForVcard(dateLabel ?? walkerNode?.WalkerData?.TourDate ?? string.Empty);
            if (string.IsNullOrWhiteSpace(formattedDate))
            {
                formattedDate = "your tour date";
            }

            return $"Hey {walkerName}, we'd like to connect you with your guide for {formattedDate}. Would you please text us? We're also on WhatsApp. Thank you :) (917) 938-1170";
        }

        // 2025-02-05 - Composite key helper
        protected static string GetTourKey(string? dateLabel, string? tourLabel)
        {
            return $"{dateLabel ?? ""} | {tourLabel ?? ""}";
        }

        private async Task EnsureCatalogAsync()
        {
            if (tourCatalog != null) return;
            try
            {
                tourCatalog = await TourCatalogService.BuildCatalogAsync();
                catalogTourNames = tourCatalog.Entries
                    .Select(e => e.CanonicalName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n)
                    .ToList();
                availableTourNames = catalogTourNames;
            }
            catch
            {
                tourCatalog = new Email.Services.TourCatalogSnapshot();
                catalogTourNames = availableTourNames = new List<string>();
            }
        }

        private async Task EnsureVendorsAsync()
        {
            if (vendorsLoaded) return;
            try
            {
                availableVendors = await VendorsApi.GetVendorsAsync();
                var vendorTours = await VendorsApi.GetVendorToursAsync();
                activeVendorAssignments = VendorLinkResolver.BuildActiveVendorAssignments(vendorTours, availableVendors);
            }
            catch
            {
                availableVendors = new List<DbVendor>();
                activeVendorAssignments = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            }
            vendorsLoaded = true;
        }

        private async Task EnsureTourLinksAsync()
        {
            if (tourLinksLoaded) return;

            try
            {
                availableTourLinks = await TourLinkService.GetAllAsync();
            }
            catch
            {
                availableTourLinks = new List<TourLink>();
            }

            try
            {
                if (availableTours == null || availableTours.Count == 0)
                {
                    availableTours = await ToursApiService.GetToursAsync();
                }
            }
            catch
            {
                if (availableTours == null)
                {
                    availableTours = new List<DbTour>();
                }
            }

            tourLinksLoaded = true;
        }

        private VendorLinkResolution ResolveVendorLinksForStage(
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

        private static bool HasMissingAllToursLinkWarning(string? value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                value.Contains("[missing allToursLink]", StringComparison.OrdinalIgnoreCase);
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

        private Email.Services.TourCatalogEntry? FindCatalogEntry(string? name)
        {
            if (tourCatalog == null || string.IsNullOrWhiteSpace(name)) return null;
            var canonical = TourCatalogService.NormalizeName(name);
            return tourCatalog.Entries.FirstOrDefault(e =>
                string.Equals(e.CanonicalName, canonical, StringComparison.OrdinalIgnoreCase) ||
                e.Aliases.Any(a => string.Equals(TourCatalogService.NormalizeName(a), canonical, StringComparison.OrdinalIgnoreCase)));
        }

        private List<string> GetTimeOptionsFor(string? name)
        {
            var entry = FindCatalogEntry(name);
            var times = entry?.Times ?? new List<string>();
            if (times.Count == 0)
            {
                return GetTourTimeOptions(); // fallback quarter-hour list
            }
            return times;
        }

        private string NormalizeTimeSafe(string? raw)
        {
            return TourCatalogService.NormalizeTime(raw ?? string.Empty);
        }

        // Format times for display as h:mm tt (e.g., 2:05 PM). Falls back to raw when parsing fails.
        private string FormatAsAmPm(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            var trimmed = raw.Trim();

            // Try DateTime first
            if (DateTime.TryParse(trimmed, out var dt))
            {
                return dt.ToString("h:mm tt");
            }

            // Try TimeSpan (e.g., "14:00")
            if (TimeSpan.TryParse(trimmed, out var ts))
            {
                var dt2 = DateTime.Today.Add(ts);
                return dt2.ToString("h:mm tt");
            }

            // Fallback to original string
            return trimmed;
        }

        // Shift a time string by minutes; returns HH:mm when parseable, otherwise original.
        private string ShiftTimeByMinutes(string? raw, int minutes)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var trimmed = raw.Trim();

            if (DateTime.TryParse(trimmed, out var dt))
            {
                return dt.AddMinutes(minutes).ToString("HH:mm");
            }

            if (TimeSpan.TryParse(trimmed, out var ts))
            {
                var dt2 = DateTime.Today.Add(ts).AddMinutes(minutes);
                return dt2.ToString("HH:mm");
            }

            return trimmed;
        }

        // 2025-12-11 00:00 UTC - Compute a temporary meeting time from tour label or walker time, shifted -10 minutes
        private string ComputeTempMeetingTime(ShapedTreeNode? tourNode, ShapedWalkerData? walkerData)
        {
            var labelTime = ExtractTimeFromTourLabel(tourNode?.Label);
            var baseTime = !string.IsNullOrWhiteSpace(labelTime)
                ? labelTime
                : (walkerData?.TourTime ?? walkerData?.TourStartTime ?? string.Empty);

            var shifted = ShiftTimeByMinutes(baseTime, -10);
            var display = FormatAsAmPm(string.IsNullOrWhiteSpace(shifted) ? baseTime : shifted);
            return display ?? string.Empty;
        }

        // 2025-12-11 00:00 UTC - Pull time component from tour label formatted as "Name — 9:30 AM"
        private string ExtractTimeFromTourLabel(string? label)
        {
            if (string.IsNullOrWhiteSpace(label)) return string.Empty;
            var trimmed = label.Trim();

            foreach (var separator in new[] { "\u00E2\u20AC\u201D", "\u2014", "\u2013" })
            {
                var idx = trimmed.LastIndexOf(separator, StringComparison.Ordinal);
                if (idx >= 0 && idx + separator.Length < trimmed.Length)
                {
                    var candidate = trimmed[(idx + separator.Length)..].Trim();
                    if (!string.IsNullOrWhiteSpace(candidate))
                    {
                        return candidate;
                    }
                }
            }

            var dashParts = trimmed.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (dashParts.Length >= 2)
            {
                return dashParts.LastOrDefault() ?? string.Empty;
            }

            var match = System.Text.RegularExpressions.Regex.Match(
                trimmed,
                @"(\d{1,2}:\d{2}\s*(?:AM|PM|am|pm)?|\d{1,2}\s*(?:AM|PM|am|pm))\s*$");
            if (match.Success)
            {
                return match.Value.Trim();
            }

            return string.Empty;
        }
        private Email.Services.TourCatalogEntry? ResolveCatalogEntryForMessage()
        {
            if (tourCatalog == null) return null;

            if (newMessageTourId.HasValue)
            {
                var byId = tourCatalog.Entries.FirstOrDefault(e => e.TourId == newMessageTourId.Value);
                if (byId != null) return byId;
            }

            if (!string.IsNullOrWhiteSpace(newMessageTourName))
            {
                var canonical = TourCatalogService.NormalizeName(newMessageTourName);
                return tourCatalog.Entries.FirstOrDefault(e =>
                    string.Equals(e.CanonicalName, canonical, StringComparison.OrdinalIgnoreCase) ||
                    e.Aliases.Any(a => string.Equals(TourCatalogService.NormalizeName(a), canonical, StringComparison.OrdinalIgnoreCase)));
            }

            return null;
        }

        private void MaybeSetDefaultGuideForMessage()
        {
            if (newMessageGuideId.HasValue) return;
            if (tourCatalog == null) return;

            var entry = ResolveCatalogEntryForMessage();
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
            }
        }

        // 2026-02-05: Converted to async to support Override lookup
        private async Task PreselectGuidesFromDefaultsAsync()
        {
            if (tourCatalog == null || TourData?.TreeNodes == null) return;
            var jonNotoGuideId = ResolveJonNotoGuideId();

            foreach (var dateNode in TourData.TreeNodes)
            {
                var dateForDow = ParseDateFromLabel(dateNode.Label);
                var dowInt = dateForDow.HasValue ? (int)dateForDow.Value.DayOfWeek : (int?)null;

                foreach (var tourNode in dateNode.Children)
                {
                    // 2026-01-31 - Check for explicit assignment from DB first
                    var key = GetTourKey(dateNode.Label, tourNode.Label);

                    if (tourNode.AssignedGuideId.HasValue)
                    {
                        selectedGuideIdByTour[key] = tourNode.AssignedGuideId;
                        continue;
                    }

                    var (rawName, rawTime) = SplitNameAndTimeLabel(tourNode.Label);
                    var canonical = TourCatalogService.NormalizeName(rawName);
                    var normalizedTime = NormalizeTimeSafe(rawTime);
                    
                    // 2026-02-05: Check for Override schedule FIRST (query-time resolution)
                    if (dateForDow.HasValue)
                    {
                        try
                        {
                            var overrideGuideId = await ToursApiService.GetOverrideGuideForDateAsync(dateForDow.Value, rawName, rawTime);
                            if (overrideGuideId.HasValue)
                            {
                                selectedGuideIdByTour[key] = overrideGuideId;
                                continue; // Found Override, skip catalog/defaults
                            }
                        }
                        catch (Exception ex)
                        {
                            try { Console.WriteLine($"[PreselectGuides] Override lookup failed: {ex.Message}"); } catch {}
                        }
                    }
                    
                    // Fallback to catalog defaults
                    var entry = FindCatalogEntry(canonical);
                    int? guideId = null;

                    if (entry?.TourId is int tourId &&
                        tourCatalog.GuideDefaultsByTourId.TryGetValue(tourId, out var defaults) &&
                        defaults.Count > 0)
                    {
                        var match = defaults.FirstOrDefault(d =>
                            string.Equals(NormalizeTimeSafe(d.StartTime), normalizedTime, StringComparison.OrdinalIgnoreCase) &&
                            (!dowInt.HasValue || d.DayOfWeek == dowInt.Value));

                        if (match == null && dowInt.HasValue)
                        {
                            match = defaults.FirstOrDefault(d => d.DayOfWeek == dowInt.Value);
                        }

                        guideId = match?.GuideId;
                    }

                    if (!guideId.HasValue && entry?.DefaultGuideId.HasValue == true)
                    {
                        guideId = entry.DefaultGuideId;
                    }

                    if (!guideId.HasValue && jonNotoGuideId.HasValue)
                    {
                        guideId = jonNotoGuideId;
                    }

                    if (guideId.HasValue && !selectedGuideIdByTour.ContainsKey(key))
                    {
                        selectedGuideIdByTour[key] = guideId;
                    }
                }
            }
        }

        private int? ResolveJonNotoGuideId()
        {
            if (DbGuides == null || DbGuides.Count == 0)
            {
                return null;
            }

            var match = DbGuides.FirstOrDefault(g =>
                string.Equals((g.FirstName ?? string.Empty).Trim(), "Jon", StringComparison.OrdinalIgnoreCase) &&
                string.Equals((g.LastName ?? string.Empty).Trim(), "Noto", StringComparison.OrdinalIgnoreCase) &&
                g.IsActive);

            if (match != null)
            {
                return match.Id;
            }

            match = DbGuides.FirstOrDefault(g =>
                string.Equals($"{(g.FirstName ?? string.Empty).Trim()} {(g.LastName ?? string.Empty).Trim()}".Trim(), "Jon Noto", StringComparison.OrdinalIgnoreCase));

            return match?.Id;
        }

        private void PreselectMessagesFromDefaults()
        {
            if (TourData?.TreeNodes == null || InMemoryMessages == null) return;

            var today = GetCurrentEasternTime().Date;
            var tomorrow = today.AddDays(1);

            // Find message IDs for defaults (case-insensitive)
            var todayMsg = InMemoryMessages.FirstOrDefault(m => string.Equals(m.Name, "Today", StringComparison.OrdinalIgnoreCase));
            var tomorrowMsg = InMemoryMessages.FirstOrDefault(m => string.Equals(m.Name, "Tomorrow", StringComparison.OrdinalIgnoreCase));
            var dateMsg = InMemoryMessages.FirstOrDefault(m => string.Equals(m.Name, "Date", StringComparison.OrdinalIgnoreCase));
            
            // 2026-02-03 - Added ThankYou for past tours
            var thankYouMsg = InMemoryMessages.FirstOrDefault(m => string.Equals(m.Name, "ThankYou", StringComparison.OrdinalIgnoreCase) 
                                                                || string.Equals(m.Name, "Thank You", StringComparison.OrdinalIgnoreCase));

            if (todayMsg == null && tomorrowMsg == null && dateMsg == null && thankYouMsg == null) return;

            foreach (var dateNode in TourData.TreeNodes)
            {
                var date = ParseDateFromLabel(dateNode.Label);
                if (!date.HasValue) continue;
                foreach (var tourNode in dateNode.Children ?? new List<ShapedTreeNode>())
                {
                    var hasSubmittedReportForTour = HasSubmittedGuideReportForTour(tourNode);
                    var isGuideReportPastDueForTour = IsGuideReportPastDueForTour(dateNode.Label, tourNode.Label);
                    string? targetMsgId = null;

                    if (thankYouMsg != null && (hasSubmittedReportForTour || isGuideReportPastDueForTour))
                    {
                        // Submitted reports or past-due tours default to ThankYou.
                        targetMsgId = thankYouMsg.Id;
                    }
                    else if (date.Value.Date == today && todayMsg != null)
                    {
                        targetMsgId = todayMsg.Id;
                    }
                    else if (date.Value.Date == tomorrow && tomorrowMsg != null)
                    {
                        targetMsgId = tomorrowMsg.Id;
                    }
                    else if (date.Value.Date > tomorrow && dateMsg != null)
                    {
                        targetMsgId = dateMsg.Id;
                    }
                    else if (date.Value.Date < today && thankYouMsg != null)
                    {
                        // Past tours keep the existing ThankYou behavior.
                        targetMsgId = thankYouMsg.Id;
                    }

                    if (targetMsgId == null)
                    {
                        continue;
                    }

                    var key = GetTourKey(dateNode.Label, tourNode.Label);
                    if (!selectedMessageIdByTour.TryGetValue(key, out var existingSelection) || string.IsNullOrWhiteSpace(existingSelection))
                    {
                        selectedMessageIdByTour[key] = targetMsgId;
                        continue;
                    }

                    // Upgrade overdue/submitted tours from Today/Tomorrow/Date defaults to ThankYou.
                    if (thankYouMsg != null &&
                        string.Equals(targetMsgId, thankYouMsg.Id, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(existingSelection, thankYouMsg.Id, StringComparison.OrdinalIgnoreCase) &&
                        IsDefaultNonThankYouMessageSelection(existingSelection, todayMsg?.Id, tomorrowMsg?.Id, dateMsg?.Id))
                    {
                        selectedMessageIdByTour[key] = thankYouMsg.Id;
                    }
                }
            }
        }

        private static bool IsDefaultNonThankYouMessageSelection(
            string? existingSelection,
            string? todayMsgId,
            string? tomorrowMsgId,
            string? dateMsgId)
        {
            if (string.IsNullOrWhiteSpace(existingSelection))
            {
                return true;
            }

            return string.Equals(existingSelection, todayMsgId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(existingSelection, tomorrowMsgId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(existingSelection, dateMsgId, StringComparison.OrdinalIgnoreCase);
        }

        private static DateTime? ParseDateFromLabel(string? label)
        {
            if (string.IsNullOrWhiteSpace(label)) return null;
            if (DateTime.TryParse(label, out var dt))
            {
                return dt.Date;
            }
            return null;
        }

        private static bool IsGuideReportPastDueForTour(string? dateLabel, string? tourLabel)
        {
            var parsedDate = ParseDateFromLabel(dateLabel);
            if (!parsedDate.HasValue)
            {
                return false;
            }

            var (_, tourTime) = SplitTourNameAndTimeForRoute(tourLabel);
            var nowEastern = GetCurrentEasternTime();

            if (TryParseTourStartTime(tourTime, out var tourStartTime))
            {
                var scheduledTourStart = parsedDate.Value.Date.Add(tourStartTime);
                return scheduledTourStart <= nowEastern;
            }

            // If a tour time is missing/unparseable, only mark as overdue once the day has passed.
            return parsedDate.Value.Date < nowEastern.Date;
        }

        private static bool TryParseTourStartTime(string? tourTime, out TimeSpan startTime)
        {
            startTime = default;
            if (string.IsNullOrWhiteSpace(tourTime))
            {
                return false;
            }

            var normalized = tourTime.Trim();
            var styles = DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.NoCurrentDateDefault;

            DateTime parsed;
            if (DateTime.TryParse(normalized, CultureInfo.InvariantCulture, styles, out parsed) ||
                DateTime.TryParse(normalized, CultureInfo.CurrentCulture, styles, out parsed))
            {
                startTime = parsed.TimeOfDay;
                return true;
            }

            return false;
        }

        private static DateTime GetCurrentEasternTime()
        {
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EasternTimeZone);
        }

        private static TimeZoneInfo ResolveEasternTimeZone()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            }
            catch (TimeZoneNotFoundException)
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
                }
                catch
                {
                    return TimeZoneInfo.Utc;
                }
            }
            catch (InvalidTimeZoneException)
            {
                return TimeZoneInfo.Utc;
            }
        }

        private async Task OpenAddBookingForTourAsync(string? dateLabel, ShapedTreeNode? tourNode)
        {
            if (!OnAddBookingFromTour.HasDelegate || tourNode == null)
            {
                return;
            }

            var parsedDate = ParseDateFromLabel(dateLabel);
            var (tourName, tourTime) = SplitTourNameAndTimeForRoute(tourNode.Label);
            var effectiveTourName = !string.IsNullOrWhiteSpace(tourName)
                ? tourName
                : (tourNode.Label ?? string.Empty).Trim();

            await OnAddBookingFromTour.InvokeAsync(new BookingPrefillRequest
            {
                TourName = effectiveTourName,
                TourDate = parsedDate,
                TourTime = string.IsNullOrWhiteSpace(tourTime) ? null : tourTime
            });
        }

        private static IEnumerable<ShapedWalkerData> EnumerateTourWalkers(ShapedTreeNode? tourNode)
        {
            if (tourNode?.Children == null)
            {
                return Enumerable.Empty<ShapedWalkerData>();
            }

            return tourNode.Children
                .SelectMany(v => v.Children ?? new List<ShapedTreeNode>())
                .Select(w => w.WalkerData)
                .Where(w => w != null)!
                .Cast<ShapedWalkerData>();
        }

        private static bool IsManualWalkerEntry(ShapedTreeNode? walkerNode)
        {
            var messageId = walkerNode?.WalkerData?.MessageId;
            return !string.IsNullOrWhiteSpace(messageId) &&
                messageId.StartsWith("MANUAL-", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasSubmittedGuideReportForTour(ShapedTreeNode? tourNode)
        {
            return EnumerateTourWalkers(tourNode).Any(w => w.HasSubmittedGuideReport);
        }

        private static bool HasGuideGalleryInDbForTour(ShapedTreeNode? tourNode)
        {
            return EnumerateTourWalkers(tourNode).Any(w => w.HasGuideGalleryInDb);
        }

        private static string GetGuideReportPublicIdForTour(ShapedTreeNode? tourNode)
        {
            return EnumerateTourWalkers(tourNode)
                .Select(w => w.GuideReportPublicId)
                .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id))
                ?? string.Empty;
        }

        private string BuildGuideReportGalleryUrl(string? publicId)
        {
            if (string.IsNullOrWhiteSpace(publicId))
            {
                return string.Empty;
            }

            var baseUrl = Configuration["PublicGallery:BaseUrl"] ?? "http://testcity.w41.wh-2.com";
            return $"{baseUrl}/tour-gallery/{publicId.Trim()}.html";
        }

        private void OpenGuideReportForTour(string? dateLabel, string? tourLabel)
        {
            var url = BuildGuideReportUrl(dateLabel, tourLabel);
            Navigation.NavigateTo(url);
        }

        private string BuildGuideReportUrl(string? dateLabel, string? tourLabel)
        {
            var query = new List<string>();
            var parsedDate = ParseDateFromLabel(dateLabel);
            if (parsedDate.HasValue)
            {
                query.Add($"date={Uri.EscapeDataString(parsedDate.Value.ToString("yyyy-MM-dd"))}");
            }

            var (tourName, tourTime) = SplitTourNameAndTimeForRoute(tourLabel);
            var effectiveTourName = !string.IsNullOrWhiteSpace(tourName) ? tourName : (tourLabel ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(effectiveTourName))
            {
                query.Add($"tourName={Uri.EscapeDataString(effectiveTourName)}");
            }

            if (!string.IsNullOrWhiteSpace(tourTime))
            {
                query.Add($"tourTime={Uri.EscapeDataString(tourTime)}");
            }

            return query.Count == 0 ? "/guide-report" : $"/guide-report?{string.Join("&", query)}";
        }

        private static (string name, string time) SplitTourNameAndTimeForRoute(string? label)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                return (string.Empty, string.Empty);
            }

            var normalizedLabel = label.Trim();
            var separators = new[] { " \u2014 ", " \u2013 ", " - ", " \u00E2\u20AC\u201D ", " \u00E2\u20AC\u201C " };
            foreach (var separator in separators)
            {
                var separatorIndex = normalizedLabel.LastIndexOf(separator, StringComparison.Ordinal);
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var candidateName = normalizedLabel[..separatorIndex].Trim();
                var candidateTime = normalizedLabel[(separatorIndex + separator.Length)..].Trim();
                if (!string.IsNullOrWhiteSpace(candidateName) && LooksLikeTourTime(candidateTime))
                {
                    return (candidateName, candidateTime);
                }
            }

            return (normalizedLabel, string.Empty);
        }

        private static bool LooksLikeTourTime(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var normalized = value.Trim();
            if (!normalized.Any(char.IsDigit))
            {
                return false;
            }

            return normalized.Contains(':') ||
                normalized.IndexOf("am", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("pm", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static (string name, string time) SplitNameAndTimeLabel(string? label)
        {
            if (string.IsNullOrWhiteSpace(label)) return (string.Empty, string.Empty);
            var parts = label.Split(new[] { " — " }, StringSplitOptions.None);
            if (parts.Length >= 2)
            {
                return (parts[0].Trim(), parts[1].Trim());
            }
            return (label.Trim(), string.Empty);
        }

        // 2025-12-05 00:00 UTC - Helper: strip trailing time from tour label for messaging
        private static string GetTourNameWithoutTime(string? label)
        {
            var (name, _) = SplitNameAndTimeLabel(label);
            return name;
        }

        private static string NormalizeTourNameForMessage(string? tourName)
        {
            return TourNameDisplaySanitizer.Normalize(tourName);
        }

        // Build a message from a template string using tour and selected guide context
        private string ProcessTemplateForTour(string? template, ShapedTreeNode tourNode)
        {
            var text = template ?? string.Empty;

            var tourLabel = tourNode.Label ?? string.Empty;
            var tourLabelWithoutTime = GetTourNameWithoutTime(tourLabel);
            var dateNode = TourData?.TreeNodes?.FirstOrDefault(d => d.Children.Contains(tourNode));
            var dateLabel = dateNode?.Label ?? string.Empty;
            var formattedDate = FormatDateForVcard(string.IsNullOrWhiteSpace(dateLabel) ? "" : dateLabel);

            // Use first walker under this tour to infer tour name if available
            var firstWalker = tourNode.Children.SelectMany(v => v.Children)
                .Select(n => n.WalkerData)
                .FirstOrDefault(w => w != null);
            var tourName = NormalizeTourNameForMessage(firstWalker?.TourName ?? tourLabelWithoutTime);

            // Selected guide for this tour (if any)
            string guideFirst = string.Empty, guideLast = string.Empty, guideFull = string.Empty, guidePhone = string.Empty, guideEmail = string.Empty;
            
            // 2025-02-05 - Use composite key for lookup
            var tourKey = GetTourKey(dateLabel, tourLabel);
            
            if (!string.IsNullOrWhiteSpace(tourLabel) &&
                selectedGuideIdByTour.TryGetValue(tourKey, out var selGuideId) &&
                selGuideId.HasValue &&
                DbGuides != null && DbGuides.Count > 0)
            {
                var g = DbGuides.FirstOrDefault(x => x.Id == selGuideId.Value);
                if (g != null)
                {
                    guideFirst = g.FirstName ?? string.Empty;
                    guideLast = g.LastName ?? string.Empty;
                    guideFull = string.IsNullOrWhiteSpace(guideLast) ? guideFirst : $"{guideFirst} {guideLast}";
                    guidePhone = g.Phone ?? string.Empty;
                    guideEmail = g.Email ?? string.Empty;
                }
            }

            // Apply known values; leave other placeholders intact for later passes
            text = text
                .Replace("{guide}", guideFull)
                .Replace("{guideFirstName}", guideFirst)
                .Replace("{guideLastName}", guideLast)
                .Replace("{guidePhone}", guidePhone)
                .Replace("{guideEmail}", guideEmail)
                .Replace("{tour}", tourName)
                .Replace("{tourname}", tourName)
                .Replace("{date}", formattedDate);

            return text;
        }

        // 2025-11-20 00:00 UTC - Bulk actions (tour-level) adapted to shaped nodes
        private async Task DownloadVcardsForTour(ShapedTreeNode tourNode)
        {
            try { await JSRuntime.InvokeVoidAsync("console.log", $"DownloadVcardsForTour: tour={tourNode?.Label}"); } catch { }
            if (tourNode == null)
            {
                VCardLogService.LogAction("Desktop Button Clicked", "DownloadVcardsForTour called but tourNode is null", null);
                return;
            }

            var dateNode = TourData?.TreeNodes?.FirstOrDefault(d => d.Children.Contains(tourNode));
            var formattedDate = FormatDateForVcard(dateNode?.Label ?? "");

            // Primary path (server headers): open endpoint URL.
            string? url = null;
            try
            {
                var dayParam = TryParseDateParam(dateNode?.Label, Args?.StartDate);
                if (!string.IsNullOrWhiteSpace(dayParam))
                {
                    var ft = (Args?.FilterType ?? TreeDataFilterType.ActiveBookings).ToString();
                    url = $"/vcards/tour?date={Uri.EscapeDataString(dayParam)}&tourLabel={Uri.EscapeDataString(tourNode.Label ?? string.Empty)}&filterType={Uri.EscapeDataString(ft)}";
                    if (!string.IsNullOrWhiteSpace(Args?.Vendor))
                    {
                        url += $"&vendor={Uri.EscapeDataString(Args.Vendor)}";
                    }

                    VCardLogService.LogAction("Desktop Button Clicked", $"DownloadVcardsForTour button clicked. Tour: {tourNode.Label ?? "(no label)"}, Date: {dayParam}, FilterType: {ft}", url);
                    VCardLogService.LogAction("Desktop Primary Path Attempt", $"Attempting to open URL via window.open for tour: {tourNode.Label ?? "(no label)"}", url);
                    await JSRuntime.InvokeVoidAsync("open", url, "_blank");
                    VCardLogService.LogAction("Desktop Primary Path Success", $"Successfully opened URL via window.open for tour: {tourNode.Label ?? "(no label)"}", url);
                    return;
                }
            }
            catch (Exception ex)
            {
                VCardLogService.LogAction("Desktop Primary Path Failed", $"window.open failed, falling back to JS blob download. Tour: {tourNode.Label ?? "(no label)"}", url ?? "(no URL)", ex);
                Console.WriteLine($"[TourTreeVCard] window.open failed, falling back to JS blob download: {ex.Message}");
            }

            var vcards = new List<string>();
            var walkerCount = 0;
            foreach (var vendorNode in tourNode.Children)
            {
                foreach (var walkerNode in vendorNode.Children)
                {
                    var w = walkerNode?.WalkerData;
                    if (w == null) continue;
                    vcards.Add(VCardExportService.GenerateVCard(w, dateLabelOverride: formattedDate));
                    walkerCount++;
                }
            }

            if (vcards.Count == 0) return;

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var safeTour = (tourNode.Label ?? "tour").Replace(" ", "_").Replace(",", "");
            var fileName = $"walkers_{safeTour}_{timestamp}.vcf";
            var content = VCardExportService.GenerateVcf(vcards);
            try
            {
                await JSRuntime.InvokeVoidAsync("downloadTextFile", content, fileName, "text/vcard");
                VCardLogService.LogAction("Desktop Fallback Success", $"JS blob download completed successfully. Tour: {tourNode.Label ?? "(no label)"}, File: {fileName}, Walkers: {walkerCount}", url ?? "(no URL)");
            }
            catch (Exception ex)
            {
                VCardLogService.LogAction("Desktop Fallback Exception", $"Exception during JS blob download for tour: {tourNode.Label ?? "(no label)"}", url ?? "(no URL)", ex);
            }
        }

        private async Task CopyWalkerInfo(ShapedTreeNode tourNode)
        {
            // 2025-12-04 00:00 UTC - Align Email copy behavior with Workstation (mobile-style + iPhone manual copy)
            if (tourNode == null) return;

            var sb = new System.Text.StringBuilder();

            // Compute tour-level totals
            var totalWalkers = 0;
            var totalGuests = 0;
            foreach (var vendorNode in tourNode.Children)
            {
                var vendorWalkerCount = vendorNode.Children?.Count ?? 0;
                totalWalkers += vendorWalkerCount;
                var vendorGuestTotalForSum = vendorNode.Children?.Sum(w =>
                {
                    var info = ParseWalkerLabel(w.Label);
                    return info.Attendees;
                }) ?? 0;
                totalGuests += vendorGuestTotalForSum;
            }

            // Tour header with booking/guest totals (mobile style)
            sb.AppendLine($"*{tourNode.Label}*");
            sb.AppendLine($"{totalWalkers} bookings {totalGuests} guests");
            sb.AppendLine();

            // Vendor sections
            foreach (var vendorNode in tourNode.Children)
            {
                var vendorWalkerCount = vendorNode.Children?.Count ?? 0;
                var vendorGuestTotal = vendorNode.Children?.Sum(w =>
                {
                    var info = ParseWalkerLabel(w.Label);
                    return info.Attendees;
                }) ?? 0;

                sb.AppendLine($"*{vendorNode.Label} {vendorWalkerCount}/{vendorGuestTotal}*");
                sb.AppendLine();

                if (vendorNode.Children != null)
                {
                    foreach (var walkerNode in vendorNode.Children)
                    {
                        var info = ParseWalkerLabel(walkerNode.Label);
                        sb.AppendLine($"{info.Name} {info.Attendees}");
                        sb.AppendLine(string.IsNullOrWhiteSpace(info.Phone) ? "No number" : info.Phone);
                        sb.AppendLine();
                    }
                }
            }

            var text = sb.ToString().TrimEnd();
            if (UseIPhoneCopyMode)
            {
                copyDialogTitle = tourNode.Label ?? "";
                copyDialogContent = text;
                showCopyDialog = true;
                StateHasChanged();
            }
            else
            {
                try { await JSRuntime.InvokeVoidAsync("blazorCopyText", text); } catch { }
            }
        }

        // 2025-11-22 00:00 UTC - Day-level actions (download all vcards and copy walker info for the whole day)
        private async Task DownloadVcardsForDay(ShapedTreeNode dateNode)
        {
            try { await JSRuntime.InvokeVoidAsync("console.log", $"DownloadVcardsForDay: date={dateNode?.Label}"); } catch { }
            if (dateNode == null)
            {
                VCardLogService.LogAction("Desktop Button Clicked", "DownloadVcardsForDay called but dateNode is null", null);
                return;
            }

            var formattedDate = FormatDateForVcard(dateNode.Label ?? "");

            // Primary path (server headers): open endpoint URL.
            string? url = null;
            try
            {
                var dayParam = TryParseDateParam(dateNode.Label, Args?.StartDate);
                if (!string.IsNullOrWhiteSpace(dayParam))
                {
                    var ft = (Args?.FilterType ?? TreeDataFilterType.ActiveBookings).ToString();
                    url = $"/vcards/day?date={Uri.EscapeDataString(dayParam)}&filterType={Uri.EscapeDataString(ft)}";
                    if (!string.IsNullOrWhiteSpace(Args?.Vendor))
                    {
                        url += $"&vendor={Uri.EscapeDataString(Args.Vendor)}";
                    }

                    VCardLogService.LogAction("Desktop Button Clicked", $"DownloadVcardsForDay button clicked. DateLabel: {dateNode.Label ?? "(no label)"}, Date: {dayParam}, FilterType: {ft}", url);
                    VCardLogService.LogAction("Desktop Primary Path Attempt", $"Attempting to open URL via window.open for date: {dateNode.Label ?? "(no label)"}", url);
                    await JSRuntime.InvokeVoidAsync("open", url, "_blank");
                    VCardLogService.LogAction("Desktop Primary Path Success", $"Successfully opened URL via window.open for date: {dateNode.Label ?? "(no label)"}", url);
                    return;
                }
            }
            catch (Exception ex)
            {
                VCardLogService.LogAction("Desktop Primary Path Failed", $"window.open failed, falling back to JS blob download. DateLabel: {dateNode.Label ?? "(no label)"}", url ?? "(no URL)", ex);
                Console.WriteLine($"[TourTreeVCard] window.open failed, falling back to JS blob download: {ex.Message}");
            }

            VCardLogService.LogAction("Desktop Fallback Started", $"Starting JS blob fallback download for date: {dateNode.Label ?? "(no label)"}", url ?? "(no URL)");
            var vcards = new List<string>();
            var walkerCount = 0;

            foreach (var tourNode in dateNode.Children)
            {
                foreach (var vendorNode in tourNode.Children)
                {
                    foreach (var walkerNode in vendorNode.Children)
                    {
                        var w = walkerNode?.WalkerData;
                        if (w == null) continue;
                        vcards.Add(VCardExportService.GenerateVCard(w, dateLabelOverride: formattedDate));
                        walkerCount++;
                    }
                }
            }

            if (vcards.Count == 0)
            {
                VCardLogService.LogAction("Desktop Fallback No Data", $"No walkers found for date: {dateNode.Label ?? "(no label)"}", url ?? "(no URL)");
                return;
            }

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var safeDate = (dateNode.Label ?? "day").Replace(" ", "_").Replace(",", "");
            var fileName = $"walkers_{safeDate}_{timestamp}.vcf";
            var content = VCardExportService.GenerateVcf(vcards);
            
            try
            {
                await JSRuntime.InvokeVoidAsync("downloadTextFile", content, fileName, "text/vcard");
                VCardLogService.LogAction("Desktop Fallback Success", $"JS blob download completed successfully. DateLabel: {dateNode.Label ?? "(no label)"}, File: {fileName}, Walkers: {walkerCount}", url ?? "(no URL)");
            }
            catch (Exception ex)
            {
                VCardLogService.LogAction("Desktop Fallback Exception", $"Exception during JS blob download for date: {dateNode.Label ?? "(no label)"}", url ?? "(no URL)", ex);
                // Log this so you can see if iPhones are hitting a specific error
                Console.WriteLine($"Download failed: {ex.Message}");
            }
        }

        // 2025-12-19 00:00 UTC - Helper to convert a Date label ("dddd, MMM d, yyyy") into yyyy-MM-dd for API params.
        private static string? TryParseDateParam(string? dateLabel, DateTime? fallback)
        {
            if (!string.IsNullOrWhiteSpace(dateLabel) && DateTime.TryParse(dateLabel, out var dt))
            {
                return dt.Date.ToString("yyyy-MM-dd");
            }
            if (fallback.HasValue)
            {
                return fallback.Value.Date.ToString("yyyy-MM-dd");
            }
            return null;
        }

        private async Task CopyWalkerInfoForDay(ShapedTreeNode dateNode)
        {
            // 2025-12-04 00:00 UTC - Align Email day-level copy with Workstation (mobile-style + iPhone manual copy)
            if (dateNode == null) return;

            var sb = new System.Text.StringBuilder();
            var tourCount = 0;
            var totalWalkerCount = 0;

            // Iterate through all tours on this date
            foreach (var tourNode in dateNode.Children)
            {
                tourCount++;

                // Compute tour-level totals
                var tourWalkerCount = 0;
                var tourGuestTotal = 0;
                foreach (var vendorNode in tourNode.Children)
                {
                    var vendorWalkerCount = vendorNode.Children?.Count ?? 0;
                    tourWalkerCount += vendorWalkerCount;
                    var vendorGuestTotalForSum = vendorNode.Children?.Sum(w =>
                    {
                        var info = ParseWalkerLabel(w.Label);
                        return info.Attendees;
                    }) ?? 0;
                    tourGuestTotal += vendorGuestTotalForSum;
                }
                totalWalkerCount += tourWalkerCount;

                // Tour header with booking/guest totals (mobile style)
                sb.AppendLine($"*{tourNode.Label}*");
                sb.AppendLine($"{tourWalkerCount} bookings {tourGuestTotal} guests");
                sb.AppendLine();

                // Vendor sections
                foreach (var vendorNode in tourNode.Children)
                {
                    var vendorWalkerCount = vendorNode.Children?.Count ?? 0;
                    var vendorGuestTotal = vendorNode.Children?.Sum(w =>
                    {
                        var info = ParseWalkerLabel(w.Label);
                        return info.Attendees;
                    }) ?? 0;

                    sb.AppendLine($"*{vendorNode.Label} {vendorWalkerCount}/{vendorGuestTotal}*");
                    sb.AppendLine();

                    if (vendorNode.Children != null)
                    {
                        foreach (var walkerNode in vendorNode.Children)
                        {
                            var info = ParseWalkerLabel(walkerNode.Label);
                            sb.AppendLine($"{info.Name} {info.Attendees}");
                            sb.AppendLine(string.IsNullOrWhiteSpace(info.Phone) ? "No number" : info.Phone);
                            sb.AppendLine();
                        }
                    }
                }
            }

            var text = sb.ToString().TrimEnd();

            if (UseIPhoneCopyMode)
            {
                copyDialogTitle = dateNode.Label ?? "";
                copyDialogContent = text;
                showCopyDialog = true;
                StateHasChanged();
            }
            else
            {
                try { await JSRuntime.InvokeVoidAsync("blazorCopyText", text); } catch { }
            }
        }

        // 2025-12-07 00:00 UTC - Show day message log (includes message sent status/time in EST)
        private async Task OpenDayMessageLogDialog(ShapedTreeNode dateNode)
        {
            if (dateNode == null) return;

            var sb = new System.Text.StringBuilder();

            foreach (var tourNode in dateNode.Children)
            {
                sb.AppendLine($"*{tourNode.Label}*");
                sb.AppendLine();

                foreach (var vendorNode in tourNode.Children)
                {
                    sb.AppendLine($"*{vendorNode.Label}*");
                    sb.AppendLine();

                    foreach (var walkerNode in vendorNode.Children)
                    {
                        var info = ParseWalkerLabel(walkerNode.Label);
                        sb.AppendLine($"{info.Name} {info.Attendees}");

                        var phoneLine = string.IsNullOrWhiteSpace(info.Phone) ? "No number" : info.Phone;
                        var w = walkerNode.WalkerData;
                        if (w?.MessageSent == true && w.MessageSentAtUtc.HasValue)
                        {
                            var estTime = ConvertUtcToEstString(w.MessageSentAtUtc.Value);
                            phoneLine += $" - message sent - {estTime}";
                        }
                        else if (w?.MessageSent == true)
                        {
                            phoneLine += " - message sent";
                        }

                        sb.AppendLine(phoneLine);
                        sb.AppendLine();
                    }
                }
            }

            copyDialogTitle = $"{dateNode.Label ?? "Day"} - Message Log";
            copyDialogContent = sb.ToString().TrimEnd();
            showCopyDialog = true;
            StateHasChanged();
        }

        private void OpenBulkSmsDialog(ShapedTreeNode tourNode)
        {
            try { _ = JSRuntime.InvokeVoidAsync("console.log", $"OpenBulkSmsDialog: tour={tourNode?.Label}"); } catch { }
            if (tourNode == null) return;

            selectedBulkSmsTourNode = tourNode;
            bulkSmsActiveTabIndex = 0; // default to WhatsApp App tab
            bulkSmsWalkerList = new List<string>();

            foreach (var vendorNode in tourNode.Children)
            {
                foreach (var walkerNode in vendorNode.Children)
                {
                    var name = walkerNode?.WalkerData?.DisplayName ?? walkerNode?.Label ?? "";
                    var phone = walkerNode?.WalkerData?.DisplayPhone ?? "";
                    var line = $"{name} {phone}".Trim();
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        bulkSmsWalkerList.Add(line);
                    }
                }
            }

            // Preselect guide phone from tour-level selection if available
            var tourLabel = tourNode.Label ?? string.Empty;
            var tourLabelWithoutTime = GetTourNameWithoutTime(tourLabel);
            bulkSmsGuideName = "";
            
            // 2025-02-05 - Use composite key for lookup
            var dateNode = TourData?.TreeNodes?.FirstOrDefault(d => d.Children.Contains(tourNode));
            var dateLabel = dateNode?.Label;
            var tourKey = GetTourKey(dateLabel, tourLabel);

            if (!string.IsNullOrWhiteSpace(tourLabel) &&
                selectedGuideIdByTour.TryGetValue(tourKey, out var selGuideId) &&
                selGuideId.HasValue &&
                DbGuides != null && DbGuides.Count > 0)
            {
                bulkSmsSelectedGuideId = selGuideId;
                var g = DbGuides.FirstOrDefault(x => x.Id == selGuideId.Value);
                if (g != null)
                {
                    bulkSmsPhone = g.Phone ?? string.Empty;
                    bulkSmsGuideName = string.IsNullOrWhiteSpace(g.LastName) ? g.FirstName : $"{g.FirstName} {g.LastName}";
                }
            }
            else
            {
                bulkSmsSelectedGuideId = null;
                bulkSmsGuideName = "";
            }

            bulkSmsCopyText = BuildBulkSmsCopyText(tourNode);
            // Prefill with full walker/tour info so users can type wherever they want
            bulkSmsAppMessage = bulkSmsCopyText;
            bulkSmsMessage = bulkSmsCopyText;
            showBulkSmsDialog = true;
            RefreshBulkWhatsAppUrls();
            StateHasChanged();
        }

        private void CloseBulkSmsDialog()
        {
            showBulkSmsDialog = false;
            selectedBulkSmsTourNode = null;
            bulkSmsMessage = "";
            bulkSmsAppMessage = "";
            bulkSmsPhone = "";
            bulkSmsGuideName = "";
            bulkSmsSelectedGuideId = null;
            bulkSmsActiveTabIndex = 0;
            bulkSmsCopyText = "";
            bulkSmsWalkerList = new List<string>();
            StateHasChanged();
        }


        // 2025-12-11 00:00 UTC - Send guide walker list via WhatsApp app instead of SMS
        private async Task OpenBulkWhatsAppApp()
        {
            var waPhone = GetWhatsappPhoneDigits(bulkSmsPhone);
            if (string.IsNullOrWhiteSpace(waPhone)) return;

            var body = BuildBulkWhatsAppBody(useAppMessage: true);
            var encodedBody = Uri.EscapeDataString(body);
            var url = string.IsNullOrWhiteSpace(body)
                ? $"whatsapp://send?phone={waPhone}"
                : $"whatsapp://send?phone={waPhone}&text={encodedBody}";

            // 2025-12-16 - fixing buttons for iphone: Navigation is now via <a> tag href
            // try { await JSRuntime.InvokeVoidAsync("open", url, "_blank"); } catch { }
            CloseBulkSmsDialog();
        }

        // 2025-12-11 00:00 UTC - Send guide walker list via WhatsApp Web
        private async Task OpenBulkWhatsAppWeb()
        {
            var waPhone = GetWhatsappPhoneDigits(bulkSmsPhone);
            if (string.IsNullOrWhiteSpace(waPhone)) return;

            var body = BuildBulkWhatsAppBody(useAppMessage: false);
            var encodedBody = Uri.EscapeDataString(body);
            var url = string.IsNullOrWhiteSpace(body)
                ? $"https://wa.me/{waPhone}"
                : $"https://wa.me/{waPhone}?text={encodedBody}";

            // 2025-12-16 - fixing buttons for iphone: Navigation is now via <a> tag href
            // try { await JSRuntime.InvokeVoidAsync("open", url, "_blank"); } catch { }
            CloseBulkSmsDialog();
        }

        // 2025-11-19 00:00 UTC - Temporary SMS test dialog state/handlers
        private bool showSmsTestDialog = false;
        private void OpenSmsTestDialog(ShapedTreeNode walkerNode)
        {
            try { _ = JSRuntime.InvokeVoidAsync("console.log", $"OpenSmsTestDialog: walker={walkerNode?.Label}"); } catch { }
            showSmsTestDialog = true;
            StateHasChanged();
        }
        private void CloseSmsTestDialog()
        {
            try { _ = JSRuntime.InvokeVoidAsync("console.log", "CloseSmsTestDialog invoked"); } catch { }
            showSmsTestDialog = false;
            StateHasChanged();
        }

        // 2025-11-19 00:00 UTC - SMS dialog state/handlers (adapted to shaped data, verbatim behavior)
        private bool showSmsDialog = false;
        private string smsMessage = "";
        private WalkerInfo selectedSmsWalkerInfo = new WalkerInfo();
        private ShapedTreeNode? selectedSmsWalkerNode;
        private ShapedTreeNode? selectedSmsTourNode;

        // 2025-11-20 00:00 UTC - Bulk SMS dialog state
        private bool showBulkSmsDialog = false;
        private ShapedTreeNode? selectedBulkSmsTourNode;
        private string bulkSmsPhone = "";
        private string bulkSmsMessage = "";
        private string bulkSmsAppMessage = "";
        private string bulkSmsGuideName = "";
        private List<string> bulkSmsWalkerList = new List<string>();
        private int bulkSmsActiveTabIndex = 0;
        private int? bulkSmsSelectedGuideId = null;
        private string bulkSmsCopyText = "";

        // 2025-12-10 00:00 UTC - Bulk SMS copy tab helpers (preserve counts even without phones)
        private string BuildBulkSmsCopyText(ShapedTreeNode tourNode)
        {
            var sb = new System.Text.StringBuilder();
            var dateNode = TourData?.TreeNodes?.FirstOrDefault(d => d.Children.Contains(tourNode));
            var dateLabel = dateNode?.Label ?? "";
            var tourLabel = tourNode?.Label ?? "";

            sb.AppendLine($"{tourLabel}");
            if (!string.IsNullOrWhiteSpace(dateLabel))
            {
                sb.AppendLine(dateLabel);
            }
            sb.AppendLine();

            var totalWalkers = 0;
            var totalGuests = 0;
            foreach (var vendorNode in tourNode.Children)
            {
                var vendorWalkerCount = vendorNode.Children?.Count ?? 0;
                totalWalkers += vendorWalkerCount;
                var vendorGuestTotalForSum = vendorNode.Children?.Sum(w =>
                {
                    var wd = w.WalkerData;
                    if (wd != null)
                    {
                        // DisplayAttendees is already adults.children or total; use Attendees numeric for totals
                        return wd.Attendees;
                    }
                    var info = ParseWalkerLabel(w.Label);
                    return info.Attendees;
                }) ?? 0;
                totalGuests += vendorGuestTotalForSum;
            }

            sb.AppendLine($"{totalWalkers} bookings {totalGuests} guests");
            sb.AppendLine();

            foreach (var vendorNode in tourNode.Children)
            {
                var vendorWalkerCount = vendorNode.Children?.Count ?? 0;
                var vendorGuestTotal = vendorNode.Children?.Sum(w =>
                {
                    var wd = w.WalkerData;
                    if (wd != null) return wd.Attendees;
                    var info = ParseWalkerLabel(w.Label);
                    return info.Attendees;
                }) ?? 0;

                sb.AppendLine($"*{vendorNode.Label} {vendorWalkerCount}/{vendorGuestTotal}*");
                sb.AppendLine();

                if (vendorNode.Children != null)
                {
                    foreach (var walkerNode in vendorNode.Children)
                    {
                        var wd = walkerNode.WalkerData;
                        var name = wd?.DisplayName ?? walkerNode.Label ?? "";
                        var displayAttendees = wd?.DisplayAttendees;
                        var phone = wd?.DisplayPhone;

                        if (string.IsNullOrWhiteSpace(displayAttendees))
                        {
                            var info = ParseWalkerLabel(walkerNode.Label);
                            displayAttendees = info.Attendees.ToString();
                            if (string.IsNullOrWhiteSpace(name)) name = info.Name;
                            if (string.IsNullOrWhiteSpace(phone)) phone = info.Phone;
                        }

                        sb.AppendLine($"{name} {displayAttendees}");
                        sb.AppendLine(string.IsNullOrWhiteSpace(phone) ? "No number" : phone);
                        sb.AppendLine();
                    }
                }
            }

            return sb.ToString().TrimEnd();
        }

        private void SetBulkSmsActiveTab(int index)
        {
            bulkSmsActiveTabIndex = index;
            StateHasChanged();
        }

        private void OnBulkSmsGuideLookupChanged(Microsoft.AspNetCore.Components.ChangeEventArgs e)
        {
            var val = e.Value?.ToString();
            if (string.IsNullOrWhiteSpace(val))
            {
                bulkSmsSelectedGuideId = null;
                bulkSmsPhone = "";
                bulkSmsGuideName = "";
            }
            else if (int.TryParse(val, out var gid))
            {
                bulkSmsSelectedGuideId = gid;
                var g = DbGuides?.FirstOrDefault(x => x.Id == gid);
                bulkSmsPhone = g?.Phone ?? "";
                if (g != null)
                {
                    bulkSmsGuideName = string.IsNullOrWhiteSpace(g.LastName) ? g.FirstName : $"{g.FirstName} {g.LastName}";
                }
            }
            StateHasChanged();
        }

        private async Task CopyBulkSmsTextAsync()
        {
            var text = bulkSmsCopyText ?? "";
            if (string.IsNullOrWhiteSpace(text)) return;
            try { await JSRuntime.InvokeVoidAsync("blazorCopyText", text); } catch { }
        }

        // 2025-12-11 00:00 UTC - Editable guide name input with lookup; updates phone when matched
        private void OnBulkSmsGuideNameInput(Microsoft.AspNetCore.Components.ChangeEventArgs e)
        {
            var val = e.Value?.ToString() ?? "";
            bulkSmsGuideName = val;
            bulkSmsSelectedGuideId = null;

            if (DbGuides != null && DbGuides.Count > 0)
            {
                var trimmed = val.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    var match = DbGuides.FirstOrDefault(g =>
                    {
                        var label = string.IsNullOrWhiteSpace(g.LastName) ? g.FirstName : $"{g.FirstName} {g.LastName}";
                        return label.Equals(trimmed, StringComparison.OrdinalIgnoreCase);
                    });

                    if (match != null)
                    {
                        bulkSmsSelectedGuideId = match.Id;
                        bulkSmsPhone = match.Phone ?? "";
                        bulkSmsGuideName = string.IsNullOrWhiteSpace(match.LastName) ? match.FirstName : $"{match.FirstName} {match.LastName}";
                    }
                }
            }

            StateHasChanged();
        }
        
        // 2025-12-11 00:00 UTC - Build WhatsApp body for guide bulk send (message + walker list)
        private string BuildBulkWhatsAppBody(bool useAppMessage)
        {
            var msg = useAppMessage ? bulkSmsAppMessage : bulkSmsMessage;
            return msg?.TrimEnd() ?? string.Empty;
        }

        // 2025-12-11 00:00 UTC - Normalize phone for WhatsApp deeplinks (digits only)
        private string GetWhatsappPhoneDigits(string? phone)
        {
            var e164 = FormatPhoneToE164(phone ?? "");
            return new string((e164 ?? "").Where(char.IsDigit).ToArray());
        }
        
        // 2025-11-24 00:00 UTC - Message Walkers dialog (date + tour → confirmed walkers with phones; ClickSend pseudo-send)
        private bool showMessageWalkersDialog = false;
        private DateTime messageWalkersDate = DateTime.Today;
        private List<ShapedTreeNode> messageWalkersAvailableTours = new();
        private ShapedTreeNode? messageWalkersSelectedTour = null;
        private bool messageWalkersAllToursSelected = false;
        private List<MessageWalkerItem> messageWalkersWalkers = new();
        private MessageWalkerItem? messageWalkersActiveWalker = null;
        private string? messageWalkersSelectedMessageId = null;
        private string messageWalkersMessageBody = "";
        private List<DbTour> messageWalkersToursCache = new();
        private class MessageWalkerItem
        {
            public string Name { get; set; } = "";
            public string Phone { get; set; } = "";
            public bool Selected { get; set; } = true;
            public string? TourName { get; set; }
            public string? TourDate { get; set; }
            // 2025-12-10 00:00 UTC - Expose day-of-week/display date/time for tokens
            public string? TourDayOfWeek { get; set; }
            public string? DisplayDate { get; set; }
            public string? DisplayTime { get; set; }
            // 2025-12-10 00:00 UTC - Ordinal/month tokens for messaging
            public string? TourMonthAndDayOrdinal { get; set; }
            public string? MonthOfTour { get; set; }
            public string? DisplayDayOrdinal { get; set; }
            public string? VendorName { get; set; }
        }

        private async Task OpenSmsDialog(ShapedTreeNode walkerNode, ShapedTreeNode vendorNode, ShapedTreeNode tourNode)
        {
            try { _ = JSRuntime.InvokeVoidAsync("console.log", $"OpenSmsDialog: walker={walkerNode?.Label}"); } catch { }
            _ = MarkBookingMessageSentAsync(walkerNode);

            var w = walkerNode?.WalkerData;
            var formattedDate = FormatDateForVcard(w?.TourDate ?? "");
            selectedSmsWalkerNode = walkerNode;
            selectedSmsTourNode = tourNode;
            selectedSmsVendorNode = vendorNode;

            selectedSmsWalkerInfo = new WalkerInfo
            {
                Name = w?.DisplayName ?? walkerNode?.Label ?? "",
                Phone = w?.DisplayPhone ?? "",
                Attendees = w?.Attendees,
                TourName = NormalizeTourNameForMessage(w?.TourName ?? GetTourNameWithoutTime(tourNode?.Label)),
                TourDate = formattedDate,
                VendorName = w?.VendorName ?? vendorNode?.Label ?? ""
            };

            // 2025-12-06 00:00 UTC - Prefill SMS with selected template (if available) else default
            if (!HasTemplateForTour(walkerNode, tourNode, formattedDate))
            {
                await ShowTemplateRequiredAsync(tourNode, "SMS");
            }
            smsMessage = BuildSmsMessageForWalker(walkerNode, vendorNode, tourNode, formattedDate);

            // Pre-load gallery availability for currently selected template.
            var dateLabel = selectedSmsWalkerInfo?.TourDate;
            if (selectedSmsTourNode != null && TourData?.TreeNodes != null)
            {
                var parent = TourData.TreeNodes.FirstOrDefault(d => d.Children.Contains(selectedSmsTourNode));
                if (parent != null)
                {
                    dateLabel = parent.Label;
                }
            }
            var selectedMessageId = GetSelectedMessageId(dateLabel, selectedSmsWalkerInfo?.TourName);
            selectedSmsMessage = InMemoryMessages?.FirstOrDefault(m => string.Equals(m.Id, selectedMessageId, StringComparison.Ordinal));
            var tourTime = w?.TourTime ?? w?.DisplayTime ?? string.Empty;
            await RefreshGalleryUrlForThankYouTemplateAsync(
                selectedSmsMessage,
                dateLabel,
                selectedSmsWalkerInfo?.TourName,
                tourTime,
                selectedSmsWalkerInfo?.VendorName);

            smsMessage = BuildSmsMessageForWalker(walkerNode, vendorNode, tourNode, formattedDate);

            showSmsDialog = true;
            RefreshSmsUrl();
            StateHasChanged();
        }

        private void CloseSmsDialog()
        {
            try { _ = JSRuntime.InvokeVoidAsync("console.log", "CloseSmsDialog invoked"); } catch { }
            showSmsDialog = false;
            smsMessage = "";
            selectedSmsWalkerInfo = new WalkerInfo();
            selectedSmsWalkerNode = null;
            selectedSmsTourNode = null;
            
            // 2026-02-07: Reset gallery link state
            selectedSmsMessage = null;
            includeGalleryLink = false;
            currentGalleryUrl = null;
            isLoadingGalleryUrl = false;

            StateHasChanged();
        }

        private void OnSmsMessageInput(Microsoft.AspNetCore.Components.ChangeEventArgs e)
        {
            smsMessage = e.Value?.ToString() ?? "";
            ResolveSmsDialogTokensInEditor();
            RefreshSmsUrl();
            StateHasChanged();
        }

        private async Task CopySmsMessage()
        {
            if (!string.IsNullOrEmpty(smsMessage))
            {
                try { await JSRuntime.InvokeVoidAsync("blazorCopyText", smsMessage); } catch { }
                if (selectedWalkerInfo != null && selectedSmsWalkerNode != null && selectedSmsTourNode != null)
                {
                    var stage = DetermineStageForTour(selectedSmsTourNode.Label);
                    var templateId = ResolveTemplateIdForTour(selectedSmsTourNode);
                    await AutoMarkStageAsync(selectedSmsWalkerNode, selectedSmsTourNode, stage, "sms", templateId);
                }
            }
        }

        private async Task OpenSmsAppAndTrackAsync()
        {
            if (selectedSmsWalkerNode != null && selectedSmsTourNode != null)
            {
                var stage = DetermineStageForTour(selectedSmsTourNode.Label);
                var templateId = ResolveTemplateIdForTour(selectedSmsTourNode);
                await AutoMarkStageAsync(selectedSmsWalkerNode, selectedSmsTourNode, stage, "sms", templateId);
            }

            CloseSmsDialog();
        }
        
        // 2025-11-24 00:00 UTC - Message Walkers: open/close and data loading
        private async Task OpenMessageWalkersDialog()
        {
            try { _ = JSRuntime.InvokeVoidAsync("console.log", "OpenMessageWalkersDialog"); } catch { }
            messageWalkersDate = DateTime.Today;
            messageWalkersAllToursSelected = false;
            // Load tours cache for meeting/time/signature resolution
            try
            {
                if (messageWalkersToursCache == null || messageWalkersToursCache.Count == 0)
                {
                    var tours = await ToursApiService.GetToursAsync();
                    messageWalkersToursCache = tours ?? new List<DbTour>();
                }
            }
            catch { messageWalkersToursCache = new(); }
            LoadToursForSelectedDate();
            if (messageWalkersAvailableTours.Count > 0)
            {
                messageWalkersSelectedTour = messageWalkersAvailableTours.First();
                LoadWalkersForSelectedTour();
                messageWalkersActiveWalker = messageWalkersWalkers.FirstOrDefault();
            }
            else
            {
                messageWalkersSelectedTour = null;
                messageWalkersWalkers.Clear();
                messageWalkersActiveWalker = null;
            }
            messageWalkersSelectedMessageId = null;
            messageWalkersMessageBody = "";
            showMessageWalkersDialog = true;
            StateHasChanged();
        }
        private void CloseMessageWalkersDialog()
        {
            showMessageWalkersDialog = false;
            messageWalkersWalkers.Clear();
            messageWalkersAvailableTours.Clear();
            messageWalkersSelectedTour = null;
            messageWalkersSelectedMessageId = null;
            messageWalkersMessageBody = "";
            StateHasChanged();
        }
        private void OnMessageWalkersDateChanged(Microsoft.AspNetCore.Components.ChangeEventArgs e)
        {
            if (DateTime.TryParse(e.Value?.ToString(), out var dt))
            {
                messageWalkersDate = dt.Date;
                LoadToursForSelectedDate();
                if (messageWalkersAllToursSelected)
                {
                    messageWalkersSelectedTour = null;
                }
                else
                {
                    messageWalkersSelectedTour = messageWalkersAvailableTours.FirstOrDefault();
                }
                LoadWalkersForSelectedTour();
                messageWalkersActiveWalker = messageWalkersWalkers.FirstOrDefault();
                // Reset message body when context changes
                if (!string.IsNullOrWhiteSpace(messageWalkersSelectedMessageId))
                {
                    OnMessageWalkersTemplateChanged(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = messageWalkersSelectedMessageId });
                }
            }
        }
        private void OnMessageWalkersTourChanged(Microsoft.AspNetCore.Components.ChangeEventArgs e)
        {
            var label = e.Value?.ToString() ?? "";
            if (string.Equals(label, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                messageWalkersAllToursSelected = true;
                messageWalkersSelectedTour = null;
            }
            else
            {
                messageWalkersAllToursSelected = false;
                messageWalkersSelectedTour = messageWalkersAvailableTours.FirstOrDefault(t => string.Equals(t.Label, label, StringComparison.Ordinal));
            }
            LoadWalkersForSelectedTour();
            messageWalkersActiveWalker = messageWalkersWalkers.FirstOrDefault();
            // Reprocess template with new tour context
            if (!string.IsNullOrWhiteSpace(messageWalkersSelectedMessageId))
            {
                OnMessageWalkersTemplateChanged(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = messageWalkersSelectedMessageId });
            }
        }
        private void OnMessageWalkersTemplateChanged(Microsoft.AspNetCore.Components.ChangeEventArgs e)
        {
            messageWalkersSelectedMessageId = e.Value?.ToString();
            if (string.IsNullOrWhiteSpace(messageWalkersSelectedMessageId))
            {
                messageWalkersMessageBody = "";
                return;
            }
            var tmpl = InMemoryMessages?.FirstOrDefault(m => string.Equals(m.Id, messageWalkersSelectedMessageId, StringComparison.Ordinal));
            messageWalkersMessageBody = tmpl?.Content ?? string.Empty; // keep generic template; preview personalizes per walker
        }
        private async Task CopyMessageWalkersBody()
        {
            if (!string.IsNullOrWhiteSpace(messageWalkersMessageBody))
            {
                try
                {
                    await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", messageWalkersMessageBody);
                }
                catch
                {
                    try
                    {
                        await JSRuntime.InvokeVoidAsync("eval", @"
                        if (!window.copyTextFallback) {
                            window.copyTextFallback = function(text) {
                                try {
                                    const ta = document.createElement('textarea');
                                    ta.value = text || '';
                                    ta.setAttribute('readonly', '');
                                    ta.style.position = 'absolute';
                                    ta.style.left = '-9999px';
                                    document.body.appendChild(ta);
                                    ta.select();
                                    document.execCommand('copy');
                                    document.body.removeChild(ta);
                                } catch (e) { console.error(e); }
                            };
                        }");
                        await JSRuntime.InvokeVoidAsync("copyTextFallback", messageWalkersMessageBody);
                    }
                    catch { }
                }
            }
        }
        // 2025-12-04 00:00 UTC - iPhone copy mode dialog state (align with Workstation; no toast)
        private bool showCopyDialog = false;
        private string copyDialogContent = "";
        private string copyDialogTitle = "";
        private void CloseCopyDialog()
        {
            showCopyDialog = false;
            copyDialogContent = "";
            copyDialogTitle = "";
            StateHasChanged();
        }
        private async Task TryAutoCopyFromDialog()
        {
            try
            {
                await JSRuntime.InvokeVoidAsync("blazorCopyText", copyDialogContent);
                CloseCopyDialog();
            }
            catch
            {
                // Silent fail; user can manually copy from the textarea
            }
        }
        private void LoadToursForSelectedDate()
        {
            messageWalkersAvailableTours = new();
            if (TourData?.TreeNodes == null || TourData.TreeNodes.Count == 0) return;
            foreach (var day in TourData.TreeNodes)
            {
                var dt = TryParseDateLabel(day.Label ?? "");
                if (dt.HasValue && dt.Value.Date == messageWalkersDate.Date)
                {
                    messageWalkersAvailableTours = day.Children?.ToList() ?? new();
                    break;
                }
            }
        }
        private void LoadWalkersForSelectedTour()
        {
            messageWalkersWalkers = new();
            // Find the day node for the selected date
            ShapedTreeNode? dayNode = null;
            if (TourData?.TreeNodes != null)
            {
                dayNode = TourData.TreeNodes.FirstOrDefault(d =>
                {
                    var dt = TryParseDateLabel(d.Label ?? "");
                    return dt.HasValue && dt.Value.Date == messageWalkersDate.Date;
                });
            }
            if (dayNode == null) return;

            IEnumerable<ShapedTreeNode> toursToScan = Enumerable.Empty<ShapedTreeNode>();
            if (messageWalkersAllToursSelected || messageWalkersSelectedTour == null)
            {
                toursToScan = dayNode.Children ?? new List<ShapedTreeNode>();
            }
            else
            {
                toursToScan = new[] { messageWalkersSelectedTour };
            }

            foreach (var tour in toursToScan)
            {
                foreach (var vendor in tour.Children)
                {
                    foreach (var walker in vendor.Children)
                    {
                        var w = walker?.WalkerData;
                        if (w == null) continue;
                        var phone = w.DisplayPhone ?? "";
                        var isConfirmed =
                            (!string.IsNullOrWhiteSpace(w.StatusClass) && string.Equals(w.StatusClass, "confirmed", StringComparison.OrdinalIgnoreCase))
                            || ((w.IsConfirmation || (w.Status?.Equals("Confirmed", StringComparison.OrdinalIgnoreCase) == true)) && !w.IsCancellation);
                        if (!string.IsNullOrWhiteSpace(phone) && isConfirmed)
                        {
                            messageWalkersWalkers.Add(new MessageWalkerItem
                            {
                                Name = w.DisplayName ?? walker.Label ?? "",
                                Phone = phone,
                                Selected = true,
                                TourName = w.TourName ?? tour.Label ?? "",
                                TourDate = w.TourDate ?? (dayNode.Label ?? ""),
                                TourDayOfWeek = w.TourDayOfWeek,
                                DisplayDate = w.DisplayDate,
                                DisplayTime = w.DisplayTime,
                                TourMonthAndDayOrdinal = w.TourMonthAndDayOrdinal,
                                MonthOfTour = w.MonthOfTour,
                                DisplayDayOrdinal = w.DisplayDayOrdinal,
                                VendorName = w.VendorName ?? vendor.Label ?? ""
                            });
                        }
                    }
                }
            }
            if (messageWalkersActiveWalker == null)
            {
                messageWalkersActiveWalker = messageWalkersWalkers.FirstOrDefault();
            }
        }
        private DateTime? TryParseDateLabel(string label)
        {
            try
            {
                if (DateTime.TryParse(label, out var dt)) return dt;
                // Reuse FormatDateForVcard parsing paths by roundtripping if helpful
                var formatted = FormatDateForVcard(label);
                if (DateTime.TryParse(formatted, out var dt2)) return dt2;
            }
            catch { }
            return null;
        }
        private async Task SendMessageWalkersViaClickSend()
        {
            // 2025-11-24 00:00 UTC - PSEUDOCODE ONLY (ClickSend)
            // NOTE: Do NOT add external dependencies yet. The following outlines the intended flow:
            // Imports (when wired):
            //   using ClickSendClient;
            //   using ClickSendClient.Api;
            //   using ClickSendClient.Model;
            //
            // Steps:
            //   var api = new SMSApi(); // configured with username/api key
            //   var sms = new SmsMessageCollection
            //   {
            //       Messages = selectedPhones.Select(p => new SmsMessage { To = p, Source = "TourTree", Body = messageWalkersMessageBody }).ToList()
            //   };
            //   var result = await api.SmsSendPostAsync(sms);
            //
            // Reference: ClickSend SMS Quickstart (C#) - see docs linked in chat.
            var selected = messageWalkersWalkers
                .Where(x => x.Selected && !string.IsNullOrWhiteSpace(x.Phone))
                .ToList();
            var baseTemplate = messageWalkersSelectedMessageId != null
                ? (InMemoryMessages.FirstOrDefault(m => string.Equals(m.Id, messageWalkersSelectedMessageId, StringComparison.Ordinal))?.Content ?? messageWalkersMessageBody)
                : messageWalkersMessageBody;
            var messagesToSend = selected.Select(x => new
            {
                To = FormatPhoneToE164(x.Phone),
                Body = BuildBodyForWalker(baseTemplate ?? string.Empty, x)
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.To))
            .ToList();
            try
            {
                await JSRuntime.InvokeVoidAsync("console.log", $"[PSEUDO] ClickSend would send {messagesToSend.Count} messages.");
            }
            catch { }
            // Keep dialog open for now; comment next line to close automatically:
            // CloseMessageWalkersDialog();
        }
        // 2025-11-24 00:00 UTC - Simple "Not Implemented" dialog for service buttons
        private bool showServiceNotImplementedDialog = false;
        private string serviceNotImplementedMessage = "";
        private void ShowServiceNotImplemented(string serviceName)
        {
            serviceNotImplementedMessage = $"{serviceName} is not implemented in this project yet.";
            showServiceNotImplementedDialog = true;
            StateHasChanged();
        }
        private void CloseServiceNotImplementedDialog()
        {
            showServiceNotImplementedDialog = false;
            serviceNotImplementedMessage = "";
            StateHasChanged();
        }
        private string BuildBodyForWalker(string template, MessageWalkerItem w)
        {
            var text = template ?? string.Empty;
            var dateStr = w.TourDate ?? "";
            var formattedDate = FormatDateForVcard(dateStr);
            // 2025-12-06 15:20 CST - Guide lookup now falls back to tour label/time variants for Message Walkers
            var guideInfo = GetSelectedGuideForTour(w.TourName ?? "");
            if (string.IsNullOrWhiteSpace(guideInfo.full) && messageWalkersSelectedTour != null)
            {
                guideInfo = GetSelectedGuideForTour(messageWalkersSelectedTour.Label ?? "");
            }
            if (string.IsNullOrWhiteSpace(guideInfo.full))
            {
                var lookupName = GetTourNameWithoutTime(w.TourName ?? "");
                var altKey = selectedGuideIdByTour.Keys
                    .FirstOrDefault(k => string.Equals(GetTourNameWithoutTime(k), lookupName, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(altKey))
                {
                    guideInfo = GetSelectedGuideForTour(altKey);
                }
            }
            var (guideFirst, guideLast, guideFull, guidePhone, guideEmail) = guideInfo;
            // Selected message (for tour-level fields like meeting time)
            InMemoryMessageTemplate? selectedMsg = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(messageWalkersSelectedMessageId))
                {
                    selectedMsg = InMemoryMessages.FirstOrDefault(m => string.Equals(m.Id, messageWalkersSelectedMessageId, StringComparison.Ordinal));
                }
            }
            catch { }
            // Resolve tour row for effective meeting/times
            var tourRow = ResolveTourByLabelForWalkers(messageWalkersToursCache, w.TourName ?? "")
                ?? ResolveTourForLinkLookup(w.TourName ?? "");
            var effectiveTourStartTime = !string.IsNullOrWhiteSpace(selectedMsg?.TourStartTime)
                ? selectedMsg!.TourStartTime!
                : (tourRow?.TourStartTime ?? tourRow?.MeetingTime ?? "");
                
            var effectiveMeetingTime = !string.IsNullOrWhiteSpace(selectedMsg?.MeetingTime)
                ? selectedMsg!.MeetingTime!
                : (tourRow?.MeetingTime ?? effectiveTourStartTime ?? "");
            var effectiveMeetingInstructions = !string.IsNullOrWhiteSpace(selectedMsg?.MeetingInstructions)
                ? selectedMsg!.MeetingInstructions!
                : (tourRow?.MeetingInstructions ?? "");
            var signature = selectedMsg?.Signature ?? "";
            var stageHint = $"{selectedMsg?.Type} {selectedMsg?.Name}".Trim();
            var vendorName = w.VendorName ?? string.Empty;
            var normalizedTourName = NormalizeTourNameForMessage(w.TourName ?? string.Empty);
            var walkerName = w.Name ?? string.Empty;
            var (walkerFirstName, walkerLastName) = SplitPersonName(walkerName);
            var resolvedLinks = ResolveVendorLinksForStage(
                tourRow?.Id,
                string.IsNullOrWhiteSpace(normalizedTourName)
                    ? (tourRow?.MasterTourName ?? tourRow?.TourName)
                    : normalizedTourName,
                vendorName,
                stageHint,
                selectedMsg?.VendorLink,
                tourRow?.VendorLink,
                tourRow?.ReviewLink);
            var vendorLink = resolvedLinks.VendorLink;
            var vendorTourLink = resolvedLinks.VendorTourLink;
            var vendorReviewLink = resolvedLinks.VendorReviewLink;
            var allToursLink = resolvedLinks.AllToursLink;
            var tempMeetingTime = FormatAsAmPm(ShiftTimeByMinutes(effectiveTourStartTime, -10));
            if (string.IsNullOrWhiteSpace(tempMeetingTime))
            {
                tempMeetingTime = FormatAsAmPm(effectiveTourStartTime);
            }
            var tempSignature = VendorLinkResolver.ResolveTempSignature(stageHint, vendorName, allToursLink);
            // If meeting time is missing or matches tour start, set to 10 minutes before tour start
            if (!string.IsNullOrWhiteSpace(effectiveTourStartTime) &&
                (string.IsNullOrWhiteSpace(effectiveMeetingTime) ||
                 string.Equals(effectiveMeetingTime, effectiveTourStartTime, StringComparison.OrdinalIgnoreCase)))
            {
                var shifted = ShiftTimeByMinutes(effectiveTourStartTime, -10);
                if (!string.IsNullOrWhiteSpace(shifted))
                {
                    effectiveMeetingTime = shifted;
                }
            }

            text = text
                .Replace("{walker}", walkerName)
                .Replace("{walkerFirst}", walkerFirstName)
                .Replace("{walkerLast}", walkerLastName)
                .Replace("{name}", walkerName)
                .Replace("{guest}", string.Empty)
                .Replace("{tour}", normalizedTourName)
                .Replace("{tourname}", normalizedTourName)
                .Replace("{phone}", w.Phone ?? string.Empty)
                .Replace("{date}", formattedDate)
                .Replace("{tourDayOfWeek}", w.TourDayOfWeek ?? string.Empty)
                .Replace("{tourMonthAndDayOrdinal}", w.TourMonthAndDayOrdinal ?? string.Empty)
                .Replace("{monthOfTour}", w.MonthOfTour ?? string.Empty)
                .Replace("{displayDayOrdinal}", w.DisplayDayOrdinal ?? string.Empty)
                .Replace("{displayDate}", w.DisplayDate ?? string.Empty)
                .Replace("{displayTime}", FormatAsAmPm(w.DisplayTime))
                .Replace("{guide}", guideFull)
                .Replace("{guideFirstName}", guideFirst)
                .Replace("{guideLastName}", guideLast)
                .Replace("{guidePhone}", guidePhone)
                .Replace("{guideEmail}", guideEmail)
                .Replace("{meetingTime}", FormatAsAmPm(effectiveMeetingTime) ?? "")
                .Replace("{meetingInstructions}", effectiveMeetingInstructions ?? "")
                .Replace("{tourStartTime}", FormatAsAmPm(effectiveTourStartTime) ?? "")
                .Replace("{tempMeetingTime}", tempMeetingTime ?? string.Empty)
                .Replace("{startMinus10}", tempMeetingTime ?? string.Empty)
                .Replace("{tempSignature}", tempSignature ?? string.Empty)
                .Replace("{signature}", signature ?? "")
                .Replace("{vendorName}", vendorName)
                .Replace("{meetingLocation}", ComputeMeetingLocation(tourRow, guideFull));
            text = ApplyVendorLinkTokenReplacements(text, vendorLink, vendorTourLink, vendorReviewLink, allToursLink);
            text = text.Replace("{galleryLink}", ResolveLinkTokenDisplayValue(currentGalleryUrl, "galleryLink"));
            return text.Replace("&amp;", "&");
        }
        private string GetWalkerDisplayName(MessageWalkerItem w)
        {
            if (messageWalkersAllToursSelected)
            {
                var tn = w.TourName ?? "";
                return string.IsNullOrWhiteSpace(tn) ? (w.Name ?? "") : $"{w.Name} -- {tn}";
            }
            return w.Name ?? "";
        }
        private void SetActiveWalkerForPreview(MessageWalkerItem w)
        {
            messageWalkersActiveWalker = w;
            StateHasChanged();
        }
        private string RenderMessageWalkersPreview()
        {
            if (messageWalkersActiveWalker == null) return "";
            return BuildBodyForWalker(messageWalkersMessageBody ?? string.Empty, messageWalkersActiveWalker);
        }
        private DbTour? ResolveTourByLabelForWalkers(List<DbTour> tours, string treeLabel)
        {
            try
            {
                if (tours == null || tours.Count == 0) return null;
                if (string.IsNullOrWhiteSpace(treeLabel)) return null;
                var exact = tours.FirstOrDefault(t => string.Equals((t.TourName ?? "").Trim(), (treeLabel ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
                if (exact != null) return exact;
                // Alias CSV support
                foreach (var t in tours)
                {
                    var alias = t.TourNameAlias ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(alias)) continue;
                    var parts = alias.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (parts.Any(a => string.Equals(a, treeLabel, StringComparison.OrdinalIgnoreCase))) return t;
                }
                // Normalized compare
                string Normalize(string s)
                {
                    if (string.IsNullOrWhiteSpace(s)) return string.Empty;
                    var lower = s.Trim().ToLowerInvariant().Replace("&", " and ").Replace("\u00a0", " ");
                    if (lower.StartsWith("the ")) lower = lower.Substring(4);
                    lower = System.Text.RegularExpressions.Regex.Replace(lower, "[^a-z0-9]+", " ").Trim();
                    lower = System.Text.RegularExpressions.Regex.Replace(lower, "\\s+", " ");
                    lower = lower.Replace(" walking tour", "");
                    return lower;
                }
                var norm = Normalize(treeLabel);
                foreach (var t in tours)
                {
                    if (Normalize(t.TourName ?? "") == norm) return t;
                    var alias = t.TourNameAlias ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(alias))
                    {
                        var parts = alias.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        if (parts.Any(a => Normalize(a) == norm)) return t;
                    }
                }
            }
            catch { }
            return null;
        }

        private DbTour? ResolveTourForLinkLookup(string? treeLabel)
        {
            var label = treeLabel ?? string.Empty;

            var fromMessageWalkersCache = ResolveTourByLabelForWalkers(messageWalkersToursCache, label);
            if (fromMessageWalkersCache != null)
            {
                return fromMessageWalkersCache;
            }

            if (availableTours != null && availableTours.Count > 0)
            {
                var fromAvailableTours = ResolveTourByLabelForWalkers(availableTours, label);
                if (fromAvailableTours != null)
                {
                    return fromAvailableTours;
                }
            }

            return null;
        }

        private string ComputeMeetingLocation(DbTour? tourRow, string guideFullName)
        {
            // Prefer Tour.MeetingPlace if set; otherwise no reliable guide meeting place in Email project models
            var loc = tourRow?.MeetingPlace ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(loc)) return loc;
            return ""; // leave empty if unknown
        }
        private (string first, string last, string full, string phone, string email) GetSelectedGuideForTour(string tourLabel)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(tourLabel) &&
                    selectedGuideIdByTour.TryGetValue(tourLabel, out var gid) &&
                    gid.HasValue &&
                    DbGuides != null && DbGuides.Count > 0)
                {
                    var g = DbGuides.FirstOrDefault(x => x.Id == gid.Value);
                    if (g != null)
                    {
                        var first = g.FirstName ?? "";
                        var last = g.LastName ?? "";
                        var full = string.IsNullOrWhiteSpace(last) ? first : $"{first} {last}";
                        return (first, last, full, g.Phone ?? "", g.Email ?? "");
                    }
                }
            }
            catch { }
            return ("", "", "", "", "");
        }

        // 2025-11-19 00:00 UTC - Tours management dialog state/handlers (simple)
        private bool showToursManagementDialog = false; // if already defined elsewhere, remove this duplicate
        private void CloseToursManagementDialog()
        {
            showToursManagementDialog = false;
            StateHasChanged();
        }

        private void OpenToursManagementDialog()
        {
            showToursManagementDialog = true;
            StateHasChanged();
        }

        // 2025-11-20 00:00 UTC - Message Management state (VERBATIM from Workstation, adapted to Email)
        private bool showMessageManagementDialog = false;
        private string? newMessageName;
        private string? newMessageType;
        private string? newMessageContent;
        private string? newMessageDescription;
        private string? newMessageTourName;
        private int? newMessageTourId;
        private int? newMessageGuideId;
        private string? newMessageTourStartTime;
        private string? newMessageSignature;
        private bool newMessageIsActive = true;
        private string? lastMessageError;
        private string messageEditorTab = "edit";
        private int? editingMessageId = null;
        private List<string> messageTimeOptions = new();
        // 2025-12-09 00:00 UTC - Help dialog flag
        private bool showMessageHelp = false;

        private List<DbTour> availableTours = new();
        private List<string> distinctTourNames = new();
        private List<DbTour> distinctToursForContext = new();
        private List<DbGuide> availableDbGuides = new();
        private List<DbTourMessage> dbMessages = new();

        private async void OpenMessageManagementDialog()
        {
            // 2025-11-20 00:00 UTC
            showMessageManagementDialog = true;
            showMessageHelp = false;
            try
            {
                await EnsureCatalogAsync();

                if (availableTours == null || availableTours.Count == 0)
                {
                    try { availableTours = await ToursApiService.GetToursAsync(); } catch { availableTours = new(); }
                }

                try
                {
                    // Build tour list for dropdown using catalog entries with concrete times
                    var syntheticTours = new List<DbTour>();
                    if (tourCatalog != null)
                    {
                        foreach (var entry in tourCatalog.Entries)
                        {
                            var times = entry.Times != null && entry.Times.Count > 0 ? entry.Times : new List<string> { string.Empty };
                            foreach (var ttime in times)
                            {
                                syntheticTours.Add(new DbTour
                                {
                                    Id = entry.TourId ?? 0,
                                    TourName = entry.CanonicalName,
                                    TourStartTime = ttime,
                                    MeetingTime = ttime
                                });
                            }
                        }
                    }

                    var toursWithTime = syntheticTours
                        .Where(t => !string.IsNullOrWhiteSpace(t.TourStartTime) || !string.IsNullOrWhiteSpace(t.MeetingTime))
                        .ToList();

                    distinctToursForContext = toursWithTime
                        .GroupBy(t => ((t.TourName ?? string.Empty).Trim().ToLowerInvariant() + "|" +
                                       ((string.IsNullOrWhiteSpace(t.TourStartTime) ? (t.MeetingTime ?? string.Empty) : t.TourStartTime) ?? string.Empty)
                                       .Trim().ToLowerInvariant()))
                        .Select(g => g.First())
                        .OrderBy(t => t.TourName)
                        .ThenBy(t => string.IsNullOrWhiteSpace(t.TourStartTime) ? (t.MeetingTime ?? string.Empty) : t.TourStartTime)
                        .ToList();
                }
                catch { distinctToursForContext = availableTours; }

                distinctTourNames = catalogTourNames;

                try { availableDbGuides = await GuidesApi.GetGuidesAsync(); } catch { availableDbGuides = new(); }

                dbMessages = await TourMessagesApi.GetTourMessagesAsync();
                SyncInMemoryMessagesFromDb();

                messageTimeOptions = GetTimeOptionsFor(newMessageTourName);
                MaybeSetDefaultGuideForMessage();
            }
            catch
            {
            }
            StateHasChanged();
        }

        private void CloseMessageManagementDialog()
        {
            // 2025-11-20 00:00 UTC
            showMessageManagementDialog = false;
            showMessageHelp = false;
            StateHasChanged();
        }

        private void CloseMessageHelpDialog()
        {
            showMessageHelp = false;
            StateHasChanged();
        }

        private void OpenMessageHelp()
        {
            showMessageHelp = true;
            StateHasChanged();
        }

        private void OnMessageTourChanged()
        {
            // 2025-11-20 00:00 UTC
            if (newMessageTourId.HasValue)
            {
                var t = availableTours.FirstOrDefault(x => x.Id == newMessageTourId.Value);
                if (t != null)
                {
                    var entry = tourCatalog?.Entries.FirstOrDefault(e => e.TourId == t.Id);
                    newMessageTourName = entry?.CanonicalName ?? TourCatalogService.NormalizeName(t.TourName ?? string.Empty);
                    var timeCandidate = string.IsNullOrWhiteSpace(t.TourStartTime) ? t.MeetingTime : t.TourStartTime;
                    newMessageTourStartTime = NormalizeTimeSafe(timeCandidate);
                    var options = GetTimeOptionsFor(newMessageTourName);
                    messageTimeOptions = options;
                    if (string.IsNullOrWhiteSpace(newMessageTourStartTime) && options.Count > 0)
                    {
                        newMessageTourStartTime = options.First();
                    }
                    else if (options.Count > 0 && !options.Contains(newMessageTourStartTime ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                    {
                        newMessageTourStartTime = options.First();
                    }
                    MaybeSetDefaultGuideForMessage();
                }
            }
            else
            {
                if (string.Equals(newMessageTourName, "All", StringComparison.OrdinalIgnoreCase))
                {
                    newMessageTourId = null;
                    newMessageTourStartTime = null;
                    messageTimeOptions = new List<string>();
                }
            }
        }

        private void OnMessageTourNameChanged()
        {
            // 2025-11-20 00:00 UTC
            if (string.Equals(newMessageTourName, "All", StringComparison.OrdinalIgnoreCase))
            {
                newMessageTourId = null;
                newMessageTourStartTime = null;
                messageTimeOptions = new List<string>();
                return;
            }
            if (!string.IsNullOrWhiteSpace(newMessageTourName))
            {
                newMessageTourName = TourCatalogService.NormalizeName(newMessageTourName);
                var t = availableTours.FirstOrDefault(x => string.Equals(TourCatalogService.NormalizeName(x.TourName ?? string.Empty), newMessageTourName, StringComparison.OrdinalIgnoreCase));
                if (t != null)
                {
                    newMessageTourId = t.Id;
                    var timeCandidate = string.IsNullOrWhiteSpace(t.TourStartTime) ? t.MeetingTime : t.TourStartTime;
                    newMessageTourStartTime = NormalizeTimeSafe(timeCandidate);
                }
                var options = GetTimeOptionsFor(newMessageTourName);
                messageTimeOptions = options;
                if (string.IsNullOrWhiteSpace(newMessageTourStartTime) && options.Count > 0)
                {
                    newMessageTourStartTime = options.First();
                }
                else if (options.Count > 0 && !options.Contains(newMessageTourStartTime ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                {
                    newMessageTourStartTime = options.First();
                }
                MaybeSetDefaultGuideForMessage();
            }
        }

        private void OnMessageTourTimeChanged(ChangeEventArgs e)
        {
            newMessageTourStartTime = NormalizeTimeSafe(e.Value?.ToString());
            MaybeSetDefaultGuideForMessage();
        }

        private static string FormatTourLabel(DbTour t)
        {
            // 2025-11-20 00:00 UTC
            var time = string.IsNullOrWhiteSpace(t.TourStartTime) ? t.MeetingTime : t.TourStartTime;
            return string.IsNullOrWhiteSpace(time) ? t.TourName : ($"{t.TourName} — {time}");
        }

        private async Task AddMessageTemplate()
        {
            // 2025-11-20 00:00 UTC
            if (string.IsNullOrWhiteSpace(newMessageName)) return;

            var normalizedMessageTourName = TourCatalogService.NormalizeName(newMessageTourName ?? string.Empty);
            var normalizedMessageTourTime = NormalizeTimeSafe(newMessageTourStartTime);
            if (!string.Equals(newMessageTourName, "All", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(normalizedMessageTourName)) return;
                if (string.IsNullOrWhiteSpace(normalizedMessageTourTime))
                {
                    var options = GetTimeOptionsFor(normalizedMessageTourName);
                    if (options.Count == 0) return;
                    normalizedMessageTourTime = options.First();
                }
            }
            else
            {
                normalizedMessageTourName = "All";
                normalizedMessageTourTime = null;
            }

            var payload = new DbTourMessage
            {
                MessageName = newMessageName!.Trim(),
                MessageType = (newMessageType ?? string.Empty).Trim(),
                TourName = normalizedMessageTourName,
                TourId = newMessageTourId,
                TourStartTime = string.IsNullOrWhiteSpace(normalizedMessageTourTime) ? null : normalizedMessageTourTime,
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
                    editingMessageId = null;
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
            // 2025-11-20 00:00 UTC
            newMessageName = newMessageType = newMessageContent = newMessageDescription = null;
            newMessageTourName = newMessageTourStartTime = newMessageSignature = null;
            newMessageGuideId = null; newMessageIsActive = true; editingMessageId = null;
            messageTimeOptions = new List<string>();
        }

        private async Task RemoveDbMessageTemplate(DbTourMessage template)
        {
            // 2025-11-20 00:00 UTC
            var ok = await TourMessagesApi.DeleteTourMessageAsync(template.Id);
            if (ok)
            {
                dbMessages.RemoveAll(x => x.Id == template.Id);
                SyncInMemoryMessagesFromDb();
                StateHasChanged();
            }
        }

        private async Task ClearAllMessages()
        {
            // 2025-11-20 00:00 UTC
            InMemoryMessages.Clear();
            await Task.CompletedTask;
            StateHasChanged();
        }

        private void SyncInMemoryMessagesFromDb()
        {
            // 2025-11-20 00:00 UTC
            InMemoryMessages = dbMessages
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
                    VendorLink = m.VendorLink,
                    IsActive = m.IsActive
                })
                .ToList();
        }

        private void EditDbMessageTemplate(DbTourMessage m)
        {
            // 2025-11-20 00:00 UTC
            editingMessageId = m.Id;
            newMessageName = m.MessageName;
            newMessageType = m.MessageType;
            newMessageTourId = m.TourId;
            newMessageTourName = TourCatalogService.NormalizeName(m.TourName ?? string.Empty);
            newMessageTourStartTime = NormalizeTimeSafe(m.TourStartTime);
            newMessageGuideId = m.GuideId;
            newMessageContent = m.MessageContent;
            newMessageSignature = m.Signature;
            newMessageDescription = m.Description;
            newMessageIsActive = m.IsActive;
            messageTimeOptions = GetTimeOptionsFor(newMessageTourName);
            MaybeSetDefaultGuideForMessage();
            StateHasChanged();
        }

        private void CancelMessageEdit()
        {
            // 2025-11-20 00:00 UTC
            editingMessageId = null;
            ClearMessageForm();
        }

        private void LoadMessageByInMemoryId(string? id)
        {
            // 2025-11-20 00:00 UTC
            if (string.IsNullOrWhiteSpace(id)) return;
            var m = dbMessages.FirstOrDefault(x => string.Equals(x.Id.ToString(), id, StringComparison.Ordinal));
            if (m != null)
            {
                EditDbMessageTemplate(m);
            }
        }

        private void SetMessageEditorTab(string tab)
        {
            // 2025-11-20 00:00 UTC
            messageEditorTab = tab == "preview" ? "preview" : "edit";
            StateHasChanged();
        }
        private void SetMessageEditorTabToEdit() => SetMessageEditorTab("edit");
        private void SetMessageEditorTabToPreview() => SetMessageEditorTab("preview");

        private string RenderMessagePreview()
        {
            // 2025-11-20 00:00 UTC
            var text = newMessageContent ?? string.Empty;

            var tour = newMessageTourId.HasValue ? availableTours.FirstOrDefault(t => t.Id == newMessageTourId.Value) : null;
            var guide = newMessageGuideId.HasValue ? availableDbGuides.FirstOrDefault(g => g.Id == newMessageGuideId.Value) : null;

            string guideFirst = guide?.FirstName ?? string.Empty;
            string guideLast = guide?.LastName ?? string.Empty;
            string guideFull = string.IsNullOrWhiteSpace(guideLast) ? guideFirst : ($"{guideFirst} {guideLast}");
            string guidePhone = guide?.Phone ?? string.Empty;
            string guideEmail = guide?.Email ?? string.Empty;

            string tourName = NormalizeTourNameForMessage(newMessageTourName ?? tour?.TourName ?? string.Empty);
            string tourStart = newMessageTourStartTime ?? tour?.TourStartTime ?? tour?.MeetingTime ?? string.Empty;
            string meetingPlace = tour?.MeetingPlace ?? string.Empty;
            string meetingTime = tour?.MeetingTime ?? string.Empty;
            string meetingInstructions = tour?.MeetingInstructions ?? string.Empty;

            string signature = newMessageSignature ?? string.Empty;
            string vendorName = string.Empty;
            if (!string.IsNullOrWhiteSpace(tour?.VendorNames))
            {
                var raw = tour!.VendorNames!.Trim();
                vendorName = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? raw;
            }
            var stageHint = $"{newMessageType} {newMessageName}".Trim();
            var resolvedLinks = ResolveVendorLinksForStage(
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
            string tempMeetingTime = FormatAsAmPm(ShiftTimeByMinutes(tourStart, -10));
            if (string.IsNullOrWhiteSpace(tempMeetingTime))
            {
                tempMeetingTime = FormatAsAmPm(tourStart);
            }
            string tempSignature = VendorLinkResolver.ResolveTempSignature(stageHint, vendorName, allToursLink);

            text = text.Replace("{guide}", guideFull)
                       .Replace("{guideFirstName}", guideFirst)
                       .Replace("{guideLastName}", guideLast)
                       .Replace("{guidePhone}", guidePhone)
                       .Replace("{guideEmail}", guideEmail)
                       .Replace("{tour}", tourName)
                       .Replace("{tourname}", tourName)
                       .Replace("{tourStartTime}", FormatAsAmPm(tourStart))
                       .Replace("{meetingLocation}", meetingPlace)
                       .Replace("{meetingTime}", FormatAsAmPm(meetingTime))
                       .Replace("{tempMeetingTime}", tempMeetingTime)
                       .Replace("{startMinus10}", tempMeetingTime)
                       .Replace("{tempSignature}", tempSignature)
                       .Replace("{meetingInstructions}", meetingInstructions)
                       .Replace("{signature}", signature);
            text = ApplyVendorLinkTokenReplacements(text, vendorLink, vendorTourLink, vendorReviewLink, allToursLink);
            text = text.Replace("{galleryLink}", ResolveLinkTokenDisplayValue(currentGalleryUrl, "galleryLink"));

            return text;
        }

        private async Task InsertTemplateField(string textareaId, string field)
        {
            // 2025-11-20 00:00 UTC
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
                            }
                        } catch (e) { console.error(e); }
                    };
                }");
                await JSRuntime.InvokeVoidAsync("insertTextAtCursor", textareaId, field);
            }
            catch { }
        }

        // 2025-11-19 00:00 UTC - WhatsApp dialog state (migrated from Workstation; adapted to shaped data)
        private bool showWhatsAppDialog = false;
        private int activeWhatsAppTabIndex = 0; // 0 = App, 1 = Web
        private bool useWhatsAppAppQuickSend = true;
        private string whatsAppAppMessage = "";
        private string whatsAppMessage = "";
        private WalkerInfo selectedWalkerInfo = new WalkerInfo();
        private ShapedTreeNode? selectedWhatsAppWalkerNode;
        private ShapedTreeNode? selectedWhatsAppTourNode;


        // 2025-11-19 00:00 UTC - Email dialog state/handlers (uses ITourEmailsService to fetch inbox email by MessageId)
        private bool showEmailDialog = false;
        private bool isEmailLoading = false;
        private TourTreeEmailContent? emailContent = null;

        private async Task OpenEmailDialog(ShapedTreeNode walkerNode)
        {
            try { await JSRuntime.InvokeVoidAsync("console.log", $"OpenEmailDialog: walker={walkerNode?.Label}"); } catch { }

            var messageId = walkerNode?.WalkerData?.MessageId;
            if (string.IsNullOrWhiteSpace(messageId))
            {
                return;
            }

            showEmailDialog = true;
            isEmailLoading = true;
            emailContent = null;
            StateHasChanged();

            try
            {
                var dto = await EmailsService.GetInboxEmailByMessageIdAsync(messageId);
                if (dto != null)
                {
                    emailContent = new TourTreeEmailContent
                    {
                        MessageId = dto.MessageId,
                        Subject = dto.Subject,
                        FromEmail = dto.FromEmail,
                        FromName = dto.FromName,
                        ToEmail = dto.ToEmail,
                        ReceivedDate = dto.ReceivedDate,
                        TextBody = dto.TextBody,
                        HtmlBody = dto.HtmlBody,
                        AttachmentCount = dto.AttachmentCount,
                        AttachmentNames = dto.AttachmentNames
                    };
                }
            }
            finally
            {
                isEmailLoading = false;
                StateHasChanged();
            }
        }

        private void CloseEmailDialog()
        {
            showEmailDialog = false;
            isEmailLoading = false;
            emailContent = null;
            StateHasChanged();
        }

        // 2025-11-19 00:00 UTC - Open WhatsApp App deeplink
  
        // 2025-11-21 00:00 UTC - Edit dialog variables (adapted to shaped nodes)
        private bool showEditDialog = false;
        private bool isEditLoading = false;
        private ProcessedEmail? selectedProcessedEmail = null;
        private ProcessedEmail? selectedEditOriginalEmail = null;
        private ShapedTreeNode? selectedEditWalkerNode;
        private ShapedTreeNode? selectedEditVendorNode;
        private ShapedTreeNode? selectedEditTourNode;
        private List<string> availableTourNames = new();
        private List<string> editTimeOptions = new();
        private List<string> editVendorOptions = new();
        private string editReportIssueNotes = "";
        private bool isSubmittingEditIssue = false;
        private List<string> reportTimeOptions = new();
        // 2025-12-01 00:00 UTC - Report dialog state (tour data accuracy issue)
        private bool showReportDialog = false;
        private bool isReportLoading = false;
        private ProcessedEmail? reportOriginalEmail = null;
        private ProcessedEmail? reportEditedEmail = null;
        private ShapedTreeNode? selectedReportWalkerNode;
        private ShapedTreeNode? selectedReportVendorNode;
        private ShapedTreeNode? selectedReportTourNode;
        private string reportErrorNotes = "";

        // 2025-12-21 - Photo dialog state
        private bool showPhotoDialog = false;
        private string photoDialogTourDate = string.Empty;
        private string photoDialogTourName = string.Empty;
        private string photoDialogTourTime = string.Empty;

        private void OpenPhotoDialog(ShapedTreeNode tourNode, ShapedTreeNode dateNode)
        {
            photoDialogTourDate = dateNode?.Label ?? string.Empty;
            var (name, time) = SplitNameAndTimeLabel(tourNode?.Label);
            photoDialogTourName = name;
            photoDialogTourTime = time;
            showPhotoDialog = true;
            StateHasChanged();
        }

        private void ClosePhotoDialog()
        {
            showPhotoDialog = false;
            StateHasChanged();
        }

      

        // 2025-12-01 00:00 UTC - Open Report Dialog (original values read-only + editable corrections)
        private async Task OpenReportDialog(ShapedTreeNode walkerNode, ShapedTreeNode vendorNode, ShapedTreeNode tourNode)
        {
            try
            {
                try { await JSRuntime.InvokeVoidAsync("console.log", $"OpenReportDialog: walker={walkerNode?.Label}"); } catch { }
                selectedReportWalkerNode = walkerNode;
                selectedReportVendorNode = vendorNode;
                selectedReportTourNode = tourNode;
                var messageId = walkerNode?.WalkerData?.MessageId;
                if (string.IsNullOrWhiteSpace(messageId))
                {
                    return;
                }
                showReportDialog = true;
                isReportLoading = true;
                StateHasChanged();

                try
                {
                    var catalogTask = EnsureCatalogAsync();
                    var processedEmailTask = TourTreeService.GetProcessedEmailByMessageIdAsync(messageId);
                    await Task.WhenAll(catalogTask, processedEmailTask);

                    var response = await processedEmailTask;
                    if (response != null)
                    {
                        reportOriginalEmail = response;
                        // Deep clone to editable copy
                        try
                        {
                            var json = System.Text.Json.JsonSerializer.Serialize(reportOriginalEmail);
                            reportEditedEmail = System.Text.Json.JsonSerializer.Deserialize<ProcessedEmail>(json) ?? new ProcessedEmail();
                        }
                        catch
                        {
                            reportEditedEmail = new ProcessedEmail
                            {
                                Id = reportOriginalEmail.Id,
                                InboxEmailId = reportOriginalEmail.InboxEmailId,
                                MessageId = reportOriginalEmail.MessageId,
                                VendorName = reportOriginalEmail.VendorName,
                                EmailType = reportOriginalEmail.EmailType,
                                CustomerName = reportOriginalEmail.CustomerName,
                                CustomerEmail = reportOriginalEmail.CustomerEmail,
                                CustomerPhone = reportOriginalEmail.CustomerPhone,
                                NumberOfAttendees = reportOriginalEmail.NumberOfAttendees,
                                NumberOfAdults = reportOriginalEmail.NumberOfAdults,
                                NumberOfChildren = reportOriginalEmail.NumberOfChildren,
                                TourName = TourCatalogService.NormalizeName(reportOriginalEmail.TourName ?? string.Empty),
                                TourDate = reportOriginalEmail.TourDate,
                                TourTime = NormalizeTimeSafe(reportOriginalEmail.TourTime),
                                TourLocation = reportOriginalEmail.TourLocation,
                                Language = reportOriginalEmail.Language,
                                BookingCode = reportOriginalEmail.BookingCode
                            };
                        }
                        reportTimeOptions = GetTimeOptionsFor(reportEditedEmail?.TourName);
                        if (reportEditedEmail != null && string.IsNullOrWhiteSpace(reportEditedEmail.TourTime) && reportTimeOptions.Count > 0)
                        {
                            reportEditedEmail.TourTime = reportTimeOptions.First();
                        }
                        reportErrorNotes = "";
                    }
                    else
                    {
                        CloseReportDialog();
                        return;
                    }
                }
                finally
                {
                    isReportLoading = false;
                    StateHasChanged();
                }
            }
            catch
            {
                showReportDialog = false;
                isReportLoading = false;
                StateHasChanged();
            }
        }

        // 2025-12-01 00:00 UTC - Close Report Dialog
        private void CloseReportDialog()
        {
            showReportDialog = false;
            isReportLoading = false;
            reportOriginalEmail = null;
            reportEditedEmail = null;
            selectedReportWalkerNode = null;
            selectedReportVendorNode = null;
            selectedReportTourNode = null;
            reportErrorNotes = "";
            StateHasChanged();
        }

        // 2025-12-01 00:00 UTC - Submit Report (placeholder – storage not wired yet)
        private async Task SubmitReport()
        {
            try
            {
                var original = reportOriginalEmail;
                var corrected = reportEditedEmail;
                if (original == null || corrected == null)
                {
                    CloseReportDialog();
                    return;
                }

                corrected.TourName = TourCatalogService.NormalizeName(corrected.TourName ?? string.Empty);
                corrected.TourTime = NormalizeTimeSafe(corrected.TourTime);

                if (string.IsNullOrWhiteSpace(corrected.TourName) || string.IsNullOrWhiteSpace(corrected.TourTime))
                {
                    return;
                }

                var report = new Email.Models.TourDataErrorReport
                {
                    ProcessedEmailId = original.Id,
                    MessageId = original.MessageId,
                    VendorName = original.VendorName,
                    BookingCode = original.BookingCode ?? corrected.BookingCode,
                    CustomerIdentifier = original.CustomerIdentifier,
                    ErrorNotes = reportErrorNotes,
                    OriginalSnapshotJson = System.Text.Json.JsonSerializer.Serialize(original),
                    CorrectedSnapshotJson = System.Text.Json.JsonSerializer.Serialize(corrected)
                };

                var id = await ErrorReportsService.CreateAsync(report);
                try { await JSRuntime.InvokeVoidAsync("console.log", $"[Report Submit] Created report Id={id}"); } catch { }
                CloseReportDialog();
            }
            catch
            {
                CloseReportDialog();
            }
        }

        private void OnReportTourNameChanged(ChangeEventArgs e)
        {
            if (reportEditedEmail == null) return;
            reportEditedEmail.TourName = TourCatalogService.NormalizeName(e.Value?.ToString() ?? string.Empty);
            reportTimeOptions = GetTimeOptionsFor(reportEditedEmail.TourName);
            if (reportTimeOptions.Count > 0 && !reportTimeOptions.Contains(reportEditedEmail.TourTime ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                reportEditedEmail.TourTime = reportTimeOptions.First();
            }
        }

        private void OnReportTourTimeChanged(ChangeEventArgs e)
        {
            if (reportEditedEmail == null) return;
            reportEditedEmail.TourTime = NormalizeTimeSafe(e.Value?.ToString());
        }

        private bool IsEditWalkUpVendor()
        {
            return string.Equals(
                selectedProcessedEmail?.VendorName?.Trim(),
                "Walk-Up",
                StringComparison.OrdinalIgnoreCase);
        }

        private void RefreshEditVendorOptions()
        {
            var options = availableVendors
                .Where(v => v.IsActive && !string.IsNullOrWhiteSpace(v.VendorName))
                .Select(v => v.VendorName.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v)
                .ToList();

            var currentVendor = selectedProcessedEmail?.VendorName?.Trim();
            if (!string.IsNullOrWhiteSpace(currentVendor) &&
                !options.Contains(currentVendor, StringComparer.OrdinalIgnoreCase))
            {
                options.Add(currentVendor);
            }

            editVendorOptions = options
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v)
                .ToList();
        }

        private void OnEditTourNameChanged(ChangeEventArgs e)
        {
            if (selectedProcessedEmail == null) return;

            selectedProcessedEmail.TourName = TourCatalogService.NormalizeName(e.Value?.ToString() ?? string.Empty);
            editTimeOptions = GetTimeOptionsFor(selectedProcessedEmail.TourName);
            if (editTimeOptions.Count > 0 &&
                !editTimeOptions.Contains(selectedProcessedEmail.TourTime ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                selectedProcessedEmail.TourTime = editTimeOptions.First();
            }
        }

        private void OnEditTourDateChanged(ChangeEventArgs e)
        {
            if (selectedProcessedEmail == null) return;
            if (DateTime.TryParse(e.Value?.ToString(), out var parsedDate))
            {
                selectedProcessedEmail.TourDate = parsedDate.Date;
            }
        }

        private void OnEditTourTimeChanged(ChangeEventArgs e)
        {
            if (selectedProcessedEmail == null) return;
            selectedProcessedEmail.TourTime = NormalizeTimeSafe(e.Value?.ToString());
        }

        private void OnEditVendorChanged(ChangeEventArgs e)
        {
            if (selectedProcessedEmail == null || IsEditWalkUpVendor()) return;
            selectedProcessedEmail.VendorName = e.Value?.ToString()?.Trim() ?? string.Empty;
        }

        private async Task SubmitEditIssueFromEditDialog()
        {
            if (isSubmittingEditIssue) return;
            if (selectedProcessedEmail == null)
            {
                await ToastService.ShowWarningAsync("No Data", "No booking is loaded.");
                return;
            }

            var notes = (editReportIssueNotes ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(notes))
            {
                await ToastService.ShowWarningAsync("Issue Required", "Enter booking error details before reporting.");
                return;
            }

            isSubmittingEditIssue = true;
            StateHasChanged();

            try
            {
                var original = selectedEditOriginalEmail ?? selectedProcessedEmail;
                var corrected = selectedProcessedEmail;

                var report = new Email.Models.TourDataErrorReport
                {
                    ProcessedEmailId = original.Id,
                    MessageId = original.MessageId,
                    VendorName = original.VendorName,
                    BookingCode = original.BookingCode ?? corrected.BookingCode,
                    CustomerIdentifier = original.CustomerIdentifier,
                    ErrorNotes = notes,
                    OriginalSnapshotJson = System.Text.Json.JsonSerializer.Serialize(original),
                    CorrectedSnapshotJson = System.Text.Json.JsonSerializer.Serialize(corrected)
                };

                var reportId = await ErrorReportsService.CreateAsync(report);
                editReportIssueNotes = string.Empty;
                await ToastService.ShowSuccessAsync("Reported", $"Booking issue reported (Id {reportId}).");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TourTreeDisplay] SubmitEditIssueFromEditDialog error: {ex.Message}");
                await ToastService.ShowErrorAsync("Report Error", "Failed to submit booking issue.");
            }
            finally
            {
                isSubmittingEditIssue = false;
                StateHasChanged();
            }
        }
        // 2025-11-21 00:00 UTC - Close Edit Dialog
 
        // 2025-11-21 00:00 UTC - Helper: tour time options (15-minute increments)
        private List<string> GetTourTimeOptions()
        {
            var times = new List<string>();
            var startTime = new TimeSpan(9, 0, 0);
            var endTime = new TimeSpan(20, 30, 0);
            for (var time = startTime; time <= endTime; time = time.Add(TimeSpan.FromMinutes(15)))
            {
                var timeString = time.ToString(@"hh\:mm");
                times.Add(timeString);
            }
            return times;
        }

        // 2025-11-21 00:00 UTC - Helper: format DateTime? for input[type=date]
        private string GetDateValue(DateTime? tourDate)
        {
            return tourDate.HasValue ? tourDate.Value.ToString("yyyy-MM-dd") : "";
        }

        // 2025-11-20 00:00 UTC - Quick WhatsApp (VERBATIM from Workstation; adapted to ShapedTreeNode; missing deps commented)
        // 2025-11-20 00:00 UTC - Quick WhatsApp (VERBATIM from Workstation; adapted to ShapedTreeNode; missing deps commented)
    

        // 2025-11-19 00:00 UTC - Copied VERBATIM from Workstation (FormatDateForVcard)
        private string FormatDateForVcard(string dateLabel)
        {
            try
            {
                // Try to parse date from format like "Friday, Jun 27, 2025"
                if (DateTime.TryParse(dateLabel, out var date))
                {
                    return date.ToString("M/d/yy");
                }
                
                // Try to extract date from various formats
                var patterns = new[]
                {
                    @"(\w+),\s*(\w+)\s+(\d+),\s*(\d{4})",
                    @"(\w+)\s+(\d{1,2}),\s*(\d{4})",
                    @"(\d{1,2})/(\d{1,2})/(\d{4})",
                    @"(\d{4})-(\d{1,2})-(\d{1,2})"
                };

                foreach (var pattern in patterns)
                {
                    var match = System.Text.RegularExpressions.Regex.Match(dateLabel, pattern);
                    if (match.Success)
                    {
                        var dateStr = match.Value;
                        if (DateTime.TryParse(dateStr, out var parsedDate))
                        {
                            return parsedDate.ToString("M/d/yy");
                        }
                    }
                }

                return dateLabel; // Return original if parsing fails
            }
            catch
            {
                return dateLabel;
            }
        }

        // 2025-12-07 00:00 UTC - Convert UTC to Eastern time display string
        private string ConvertUtcToEstString(DateTime utc)
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                var utcSpecified = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
                var est = TimeZoneInfo.ConvertTimeFromUtc(utcSpecified, tz);
                return est.ToString("yyyy-MM-dd h:mm tt 'EST'");
            }
            catch
            {
                return utc.ToString("yyyy-MM-dd h:mm tt 'UTC'");
            }
        }
        // TODO: Methods to be implemented in future migrations
        /*
        private async Task DownloadVcardsForDay(TreeNode dateNode) { }
        private async Task CopyWalkerInfoForDay(TreeNode dateNode) { }
        private async Task OnGuideSelected(string tourLabel, string? guideId) { }
        private async Task OnMessageSelected(string tourLabel, string? messageId) { }
        private async Task DownloadVcardsForTour(TreeNode tourNode) { }
        private async Task CopyWalkerInfo(TreeNode tourNode) { }
        private async Task OpenBulkSmsDialog(TreeNode tourNode) { }
        private async Task OpenEditDialog(TreeNode walkerNode, TreeNode vendorNode, TreeNode tourNode) { }
        private string? GetSelectedGuideId(string tourLabel) { return null; }
        private string? GetSelectedMessageId(string tourLabel) { return null; }
        */
        
        // 2025-11-20 00:00 UTC - Quick SMS (VERBATIM from Workstation; adapted to ShapedTreeNode; missing deps commented)
        // 2025-11-20 00:00 UTC - Quick SMS (VERBATIM from Workstation; adapted to ShapedTreeNode; missing deps commented)
    
        // 2025-11-20 00:00 UTC - Helper (VERBATIM from Workstation)
    


      

      
    }
}


