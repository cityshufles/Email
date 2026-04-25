using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Email.Models;
using Email.Services;
using Email.TourTreeViewShapedData.Enums;
using Email.TourTreeViewShapedData.Models;

namespace Email.Components.Tours
{
    public partial class TourTreeDisplay : ComponentBase 
    {
        [Inject] protected IGalleryLinkResolver GalleryLinkResolver { get; set; } = default!;

        private string smsAppUrl = "";
        private string whatsAppAppUrl = "";
        private string whatsAppWebUrl = "";
        private string bulkSmsAppUrl = "";
        private string bulkSmsWebUrl = "";

        // 2026-02-07: Gallery link for THANKYOU messages
        private bool includeGalleryLink = false;
        private string? currentGalleryUrl = null;
        private bool isLoadingGalleryUrl = false;
        private InMemoryMessageTemplate? selectedSmsMessage;

        private InMemoryMessageTemplate? ResolveSelectedTemplateForTour(ShapedTreeNode? walkerNode, ShapedTreeNode? tourNode, string? fallbackDateLabel = null)
        {
            var tourLabel = tourNode?.Label ?? string.Empty;
            if (string.IsNullOrWhiteSpace(tourLabel) || InMemoryMessages == null || InMemoryMessages.Count == 0)
            {
                return null;
            }

            var dateLabelForLookup = walkerNode?.WalkerData?.TourDate ?? fallbackDateLabel;
            if (tourNode != null && TourData?.TreeNodes != null)
            {
                var parent = TourData.TreeNodes.FirstOrDefault(d => d.Children.Contains(tourNode));
                if (parent != null)
                {
                    dateLabelForLookup = parent.Label;
                }
            }

            var tourKey = GetTourKey(dateLabelForLookup, tourLabel);
            if (!selectedMessageIdByTour.TryGetValue(tourKey, out var selMsgId) || string.IsNullOrWhiteSpace(selMsgId))
            {
                return null;
            }

            return InMemoryMessages.FirstOrDefault(m => string.Equals(m.Id, selMsgId, StringComparison.Ordinal));
        }

        private bool HasTemplateForTour(ShapedTreeNode? walkerNode, ShapedTreeNode? tourNode, string? fallbackDateLabel = null)
        {
            var template = ResolveSelectedTemplateForTour(walkerNode, tourNode, fallbackDateLabel);
            return !string.IsNullOrWhiteSpace(template?.Content);
        }

        private async Task ShowTemplateRequiredAsync(ShapedTreeNode? tourNode, string channelLabel)
        {
            var tourName = NormalizeTourNameForMessage(GetTourNameWithoutTime(tourNode?.Label));
            var message = string.IsNullOrWhiteSpace(tourName)
                ? $"Create a template before sending {channelLabel}."
                : $"No template selected for \"{tourName}\". Create one before sending {channelLabel}.";

            try
            {
                await ToastService.ShowWarningAsync(message, "Template Required");
            }
            catch
            {
                // no-op
            }
        }

        private async Task QuickSendWhatsApp(ShapedTreeNode walkerNode, ShapedTreeNode vendorNode, ShapedTreeNode tourNode)
        {
            // Logger.LogInformation("QuickSendWhatsApp called for walker: {WalkerLabel}", walkerNode.Label);
            try
            {
                // 2025-12-16 - fixing buttons for iphone
                // note that we may remove the db service call (if the method updates the message sent via service) in the comments
                _ = MarkBookingMessageSentAsync(walkerNode);

                // 1. Parse walker information and ensure a phone number exists.
                var walkerInfo = ParseWalkerLabel(walkerNode.Label);
                if (string.IsNullOrEmpty(walkerInfo.Phone))
                {
                    // await ToastService.ShowWarningAsync("Missing Phone", "No phone number available for this walker.");
                    return;
                }

                // 2. Gather all necessary information for the message.
                var dateNode = TourData?.TreeNodes?.FirstOrDefault(d => d.Children.Contains(tourNode));
                var tourDate = dateNode?.Label ?? "Unknown Date";
                var formattedDate = FormatDateForVcard(tourDate);
                var tourLabelNoTime = NormalizeTourNameForMessage(GetTourNameWithoutTime(tourNode?.Label));
                var forceBlankQuickSend = IsBlankQuickSendEnabledForTourNode(tourNode, dateNode?.Label);

                var walkerInfoForMessage = new WalkerInfo
                {
                    Name = walkerInfo.Name,
                    Phone = walkerInfo.Phone,
                    Attendees = walkerInfo.Attendees,
                    TourName = tourLabelNoTime,
                    TourDate = formattedDate,
                    VendorName = vendorNode.Label
                };

                // 3. Process the message content (template if selected; otherwise default WhatsApp message)
                var processedMessage = forceBlankQuickSend
                    ? string.Empty
                    : BuildWhatsAppMessageForWalker(walkerNode, vendorNode, tourNode, formattedDate, tourLabelNoTime, dateNode?.Label);
                if (!forceBlankQuickSend && !HasTemplateForTour(walkerNode, tourNode, dateNode?.Label))
                {
                    await ShowTemplateRequiredAsync(tourNode, "WhatsApp");
                    processedMessage = string.Empty;
                }

                // 4. Prepare for sending (toggle between app/web)
                var cleanPhone = walkerInfo.Phone.Replace("+", "").Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "");
                string waUrl;
                if (useWhatsAppAppQuickSend)
                {
                    if (string.IsNullOrEmpty(processedMessage))
                    {
                        waUrl = $"whatsapp://send?phone={cleanPhone}";
                    }
                    else
                    {
                        var encodedMessage = Uri.EscapeDataString(processedMessage);
                        waUrl = $"whatsapp://send?phone={cleanPhone}&text={encodedMessage}";
                    }
                }
                else
                {
                    if (string.IsNullOrEmpty(processedMessage))
                    {
                        waUrl = $"https://wa.me/{cleanPhone}";
                    }
                    else
                    {
                        var encodedMessage = Uri.EscapeDataString(processedMessage);
                        waUrl = $"https://wa.me/{cleanPhone}?text={encodedMessage}";
                    }
                }

                // 2025-12-16 - fixing buttons for iphone
                // Fire-and-forget stage tracking to keep user gesture context strict for window.open
                var stage = DetermineStageForTour(tourNode?.Label);
                var templateId = ResolveTemplateIdForTour(tourNode);
                _ = AutoMarkStageAsync(walkerNode, tourNode, stage, "wa", templateId);

                // Logger.LogInformation("Opening WhatsApp quick-send URL: {Url}", waUrl);
                // 2025-12-16 - fixing buttons for iphone: Navigation is now via <a> tag href
                // await JSRuntime.InvokeVoidAsync("open", waUrl, "_blank");
                // await ToastService.ShowSuccessAsync("WhatsApp Opened", $"Quick send for {walkerInfo.Name} opened in WhatsApp!");
            }
            catch
            {
                // Logger.LogError(ex, "Error during QuickSendWhatsApp for walker: {WalkerLabel}", walkerNode.Label);
                // await ToastService.ShowErrorAsync("Error", "Failed to quick send WhatsApp message.");
            }
        }


        private string GetQuickSmsUrl(ShapedTreeNode walkerNode, ShapedTreeNode vendorNode, ShapedTreeNode tourNode)
        {
            try
            {
                var walkerInfo = ParseWalkerLabel(walkerNode.Label);
                if (string.IsNullOrEmpty(walkerInfo.Phone)) return "";

                var dateNode = TourData?.TreeNodes?.FirstOrDefault(d => d.Children.Contains(tourNode));
                var tourDate = dateNode?.Label ?? "Unknown Date";
                var formattedDate = FormatDateForVcard(tourDate);
                var forceBlankQuickSend = IsBlankQuickSendEnabledForTourNode(tourNode, dateNode?.Label);

                var message = forceBlankQuickSend
                    ? string.Empty
                    : BuildSmsMessageForWalker(walkerNode, vendorNode, tourNode, formattedDate);
                if (!forceBlankQuickSend && !HasTemplateForTour(walkerNode, tourNode, dateNode?.Label))
                {
                    message = string.Empty;
                }
                return BuildSmsAppUrl(walkerInfo.Phone, message);
            }
            catch { return ""; }
        }

        private string GetQuickWhatsAppUrl(ShapedTreeNode walkerNode, ShapedTreeNode vendorNode, ShapedTreeNode tourNode)
        {
            try
            {
                var walkerInfo = ParseWalkerLabel(walkerNode.Label);
                if (string.IsNullOrEmpty(walkerInfo.Phone)) return "";

                var dateNode = TourData?.TreeNodes?.FirstOrDefault(d => d.Children.Contains(tourNode));
                var tourDate = dateNode?.Label ?? "Unknown Date";
                var formattedDate = FormatDateForVcard(tourDate);
                var tourLabelNoTime = GetTourNameWithoutTime(tourNode?.Label);
                var forceBlankQuickSend = IsBlankQuickSendEnabledForTourNode(tourNode, dateNode?.Label);

                var processedMessage = forceBlankQuickSend
                    ? string.Empty
                    : BuildWhatsAppMessageForWalker(walkerNode, vendorNode, tourNode, formattedDate, tourLabelNoTime, dateNode?.Label);
                if (!forceBlankQuickSend && !HasTemplateForTour(walkerNode, tourNode, dateNode?.Label))
                {
                    processedMessage = string.Empty;
                }
                var cleanPhone = walkerInfo.Phone.Replace("+", "").Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "");
                
                if (useWhatsAppAppQuickSend)
                {
                    return string.IsNullOrEmpty(processedMessage)
                        ? $"whatsapp://send?phone={cleanPhone}"
                        : $"whatsapp://send?phone={cleanPhone}&text={Uri.EscapeDataString(processedMessage)}";
                }
                else
                {
                    return string.IsNullOrEmpty(processedMessage)
                        ? $"https://wa.me/{cleanPhone}"
                        : $"https://wa.me/{cleanPhone}?text={Uri.EscapeDataString(processedMessage)}";
                }
            }
            catch { return ""; }
        }

        private async Task TrackQuickSend(ShapedTreeNode walkerNode, ShapedTreeNode vendorNode, ShapedTreeNode tourNode, string type)
        {
            if (!IsBlankQuickSendEnabledForTourNode(tourNode) && !HasTemplateForTour(walkerNode, tourNode))
            {
                await ShowTemplateRequiredAsync(tourNode, type == "wa" ? "WhatsApp" : "SMS");
            }

            _ = MarkBookingMessageSentAsync(walkerNode);
            _ = MarkBookingContactChannelQuickSendAsync(walkerNode, type, "desktop-tree");
            var stage = DetermineStageForTour(tourNode?.Label);
            var templateId = ResolveTemplateIdForTour(tourNode);
            _ = AutoMarkStageAsync(walkerNode, tourNode, stage, type, templateId);
            await Task.CompletedTask;
        }

        private async Task QuickSendSms(ShapedTreeNode walkerNode, ShapedTreeNode vendorNode, ShapedTreeNode tourNode)
        {
            var url = GetQuickSmsUrl(walkerNode, vendorNode, tourNode);
            if (!string.IsNullOrEmpty(url))
            {
                await TrackQuickSend(walkerNode, vendorNode, tourNode, "sms");
                // 2025-12-16 - fixing buttons for iphone: Navigation is now via <a> tag href
                // await JSRuntime.InvokeVoidAsync("open", url, "_blank");
            }
        }

        // 2025-12-06 00:00 UTC - Helper: build SMS body using selected tour template when available
        // 2025-12-10: Added meeting location/time/instructions token support
        private string BuildSmsMessageForWalker(
            ShapedTreeNode walkerNode,
            ShapedTreeNode vendorNode,
            ShapedTreeNode tourNode,
            string formattedDate,
            string? contentOverride = null)
        {
            var tourLabel = tourNode?.Label ?? string.Empty;
            var selectedMsg = ResolveSelectedTemplateForTour(walkerNode, tourNode, formattedDate);
            var templateContent = contentOverride ?? selectedMsg?.Content;
            var templateSignature = selectedMsg?.Signature;
            var templateVendorLink = selectedMsg?.VendorLink;

            var walkerData = walkerNode?.WalkerData;
            var name = walkerData?.DisplayName ?? walkerNode?.Label ?? "";
            var (walkerFirstName, walkerLastName) = SplitPersonName(name);
            var phone = walkerData?.DisplayPhone ?? "";
            var vendorName = walkerData?.VendorName ?? vendorNode?.Label ?? "";
            var tourName = NormalizeTourNameForMessage(walkerData?.TourName ?? GetTourNameWithoutTime(tourLabel));

            if (string.IsNullOrWhiteSpace(templateContent))
            {
                return string.Empty;
            }

            {
                var text = ProcessTemplateForTour(templateContent, tourNode);

                // Get meeting/time fields from template or tour data
                var effectiveTourStartTime = selectedMsg?.TourStartTime
                    ?? walkerData?.TourStartTime
                    ?? walkerData?.DisplayTime
                    ?? string.Empty;
                var effectiveMeetingTime = selectedMsg?.MeetingTime
                    ?? walkerData?.MeetingTime
                    ?? walkerData?.DisplayTime
                    ?? effectiveTourStartTime;
                var effectiveMeetingInstructions = selectedMsg?.MeetingInstructions
                    ?? walkerData?.MeetingInstructions
                    ?? string.Empty;
                var effectiveMeetingLocation = selectedMsg?.MeetingLocation
                    ?? selectedMsg?.MeetingPlace
                    ?? walkerData?.MeetingPlace
                    ?? walkerData?.TourLocation
                    ?? string.Empty;
                //todo may have to revert this //var catalogEntry =
                // Fallback to cached tours (loaded for Message Walkers) if available
                var tourRowCache = ResolveTourByLabelForWalkers(messageWalkersToursCache, tourName ?? tourLabel)
                    ?? ResolveTourForLinkLookup(tourName ?? tourLabel);
                if (tourRowCache != null)
                {
                    if (string.IsNullOrWhiteSpace(effectiveMeetingLocation))
                    {
                        effectiveMeetingLocation = tourRowCache.MeetingPlace ?? string.Empty;
                    }
                    if (string.IsNullOrWhiteSpace(effectiveMeetingTime) ||
                        string.Equals(effectiveMeetingTime, effectiveTourStartTime, StringComparison.OrdinalIgnoreCase))
                    {
                        effectiveMeetingTime = tourRowCache.MeetingTime ?? effectiveMeetingTime;
                    }
                    if (string.IsNullOrWhiteSpace(effectiveTourStartTime))
                    {
                        effectiveTourStartTime = tourRowCache.TourStartTime ?? tourRowCache.MeetingTime ?? string.Empty;
                    }
                }

                // Fallback to catalog meeting place/time if still empty
                var catalogEntry = FindCatalogEntry(tourName);
                if (catalogEntry != null)
                {
                    if (string.IsNullOrWhiteSpace(effectiveMeetingLocation))
                    {
                        effectiveMeetingLocation = catalogEntry.MeetingPlace ?? string.Empty;
                    }
                    var catalogMeeting = catalogEntry.MeetingTime
                        ?? catalogEntry.Times.FirstOrDefault()
                        ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(effectiveMeetingTime) ||
                        string.Equals(effectiveMeetingTime, effectiveTourStartTime, StringComparison.OrdinalIgnoreCase))
                    {
                        effectiveMeetingTime = catalogMeeting;
                    }
                    if (string.IsNullOrWhiteSpace(effectiveTourStartTime))
                    {
                        effectiveTourStartTime = catalogMeeting;
                    }
                }
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
                var stageHint = $"{selectedMsg?.Type} {selectedMsg?.Name}".Trim();
                var resolvedLinks = ResolveVendorLinksForStage(
                    tourRowCache?.Id,
                    tourName,
                    vendorName,
                    stageHint,
                    templateVendorLink,
                    tourRowCache?.VendorLink,
                    tourRowCache?.ReviewLink);
                var resolvedVendorLink = resolvedLinks.VendorLink;
                var resolvedVendorTourLink = resolvedLinks.VendorTourLink;
                var resolvedVendorReviewLink = resolvedLinks.VendorReviewLink;
                var allToursLink = resolvedLinks.AllToursLink;
                var resolvedGalleryLink = ResolveEffectiveGalleryLink(walkerData, tourNode);
                //to here
                var meetingTimeDisplay = FormatAsAmPm(effectiveMeetingTime);
                var tempMeetingTime = ComputeTempMeetingTime(tourNode, walkerData);
                var tempSignature = VendorLinkResolver.ResolveTempSignature(stageHint, vendorName, allToursLink);
                var tourStartDisplay = FormatAsAmPm(effectiveTourStartTime);

                text = text
                    .Replace("{walker}", name)
                    .Replace("{walkerFirst}", walkerFirstName)
                    .Replace("{walkerLast}", walkerLastName)
                    .Replace("{name}", name)
                    .Replace("{guest}", string.Empty)
                    .Replace("{phone}", phone)
                    .Replace("{tour}", tourName)
                    .Replace("{tourname}", tourName)
                    .Replace("{date}", formattedDate)
                    .Replace("{tourDayOfWeek}", walkerData?.TourDayOfWeek ?? string.Empty)
                    .Replace("{tourMonthAndDayOrdinal}", walkerData?.TourMonthAndDayOrdinal ?? string.Empty)
                    .Replace("{monthOfTour}", walkerData?.MonthOfTour ?? string.Empty)
                    .Replace("{displayDayOrdinal}", walkerData?.DisplayDayOrdinal ?? string.Empty)
                    .Replace("{displayDate}", walkerData?.DisplayDate ?? string.Empty)
                    .Replace("{displayTime}", FormatAsAmPm(walkerData?.DisplayTime))
                    .Replace("{tourStartTime}", tourStartDisplay)
                    .Replace("{meetingTime}", meetingTimeDisplay)
                    .Replace("{tempMeetingTime}", tempMeetingTime)
                    .Replace("{startMinus10}", tempMeetingTime)
                    .Replace("{tempSignature}", tempSignature)
                    .Replace("{meetingLocation}", effectiveMeetingLocation)
                    .Replace("{meetingInstructions}", effectiveMeetingInstructions)
                    .Replace("{vendorName}", vendorName)
                    .Replace("{signature}", templateSignature ?? string.Empty);
                text = ApplyVendorLinkTokenReplacements(text, resolvedVendorLink, resolvedVendorTourLink, resolvedVendorReviewLink, allToursLink);
                text = text.Replace("{galleryLink}", ResolveLinkTokenDisplayValue(resolvedGalleryLink, "galleryLink"));
                return text.Replace("&amp;", "&");
            }
        }

        // 2025-12-06 15:20 CST - WhatsApp message builder aligned with SMS template selection
        // 2025-12-10: Added meeting location/time/instructions token support
        private string BuildWhatsAppMessageForWalker(
            ShapedTreeNode walkerNode,
            ShapedTreeNode vendorNode,
            ShapedTreeNode tourNode,
            string formattedDate,
            string tourLabelNoTime,
            string? dayOfWeekOrDateLabel,
            string? contentOverride = null)
        {
            var tourLabel = tourNode?.Label ?? string.Empty;
            var selectedMsg = ResolveSelectedTemplateForTour(walkerNode, tourNode, dayOfWeekOrDateLabel);
            var templateContent = contentOverride ?? selectedMsg?.Content;
            var templateSignature = selectedMsg?.Signature;
            var templateVendorLink = selectedMsg?.VendorLink;

            var walkerData = walkerNode?.WalkerData;
            var name = walkerData?.DisplayName ?? walkerNode?.Label ?? "";
            var (walkerFirstName, walkerLastName) = SplitPersonName(name);
            var phone = walkerData?.DisplayPhone ?? "";
            var vendorName = walkerData?.VendorName ?? vendorNode?.Label ?? "";
            var tourName = NormalizeTourNameForMessage(walkerData?.TourName ?? tourLabelNoTime);

            if (string.IsNullOrWhiteSpace(templateContent))
            {
                return string.Empty;
            }

            {
                var text = ProcessTemplateForTour(templateContent, tourNode);

                // Get meeting/time fields from template or tour data
                var effectiveTourStartTime = selectedMsg?.TourStartTime
                    ?? walkerData?.TourStartTime
                    ?? walkerData?.DisplayTime
                    ?? string.Empty;
                var effectiveMeetingTime = selectedMsg?.MeetingTime
                    ?? walkerData?.MeetingTime
                    ?? walkerData?.DisplayTime
                    ?? effectiveTourStartTime;
                var effectiveMeetingInstructions = selectedMsg?.MeetingInstructions
                    ?? walkerData?.MeetingInstructions
                    ?? string.Empty;
                var effectiveMeetingLocation = selectedMsg?.MeetingPlace
                    ?? walkerData?.MeetingPlace
                    ?? walkerData?.TourLocation
                    ?? string.Empty;

                // Fallback to cached tours (loaded for Message Walkers) if available
                var tourRowCache = ResolveTourByLabelForWalkers(messageWalkersToursCache, tourLabelNoTime)
                    ?? ResolveTourForLinkLookup(tourLabelNoTime);
                if (tourRowCache != null)
                {
                    if (string.IsNullOrWhiteSpace(effectiveMeetingLocation))
                    {
                        effectiveMeetingLocation = tourRowCache.MeetingPlace ?? string.Empty;
                    }
                    if (string.IsNullOrWhiteSpace(effectiveMeetingTime) ||
                        string.Equals(effectiveMeetingTime, effectiveTourStartTime, StringComparison.OrdinalIgnoreCase))
                    {
                        effectiveMeetingTime = tourRowCache.MeetingTime ?? effectiveMeetingTime;
                    }
                    if (string.IsNullOrWhiteSpace(effectiveTourStartTime))
                    {
                        effectiveTourStartTime = tourRowCache.TourStartTime ?? tourRowCache.MeetingTime ?? string.Empty;
                    }
                }
                var stageHint = $"{selectedMsg?.Type} {selectedMsg?.Name}".Trim();
                var resolvedLinks = ResolveVendorLinksForStage(
                    tourRowCache?.Id,
                    tourName,
                    vendorName,
                    stageHint,
                    templateVendorLink,
                    tourRowCache?.VendorLink,
                    tourRowCache?.ReviewLink);
                var resolvedVendorLink = resolvedLinks.VendorLink;
                var resolvedVendorTourLink = resolvedLinks.VendorTourLink;
                var resolvedVendorReviewLink = resolvedLinks.VendorReviewLink;
                var allToursLink = resolvedLinks.AllToursLink;
                var resolvedGalleryLink = ResolveEffectiveGalleryLink(walkerData, tourNode);
                Console.WriteLine($"Time check {tourLabel}");
                // Fallback to catalog meeting place/time if still empty
                var catalogEntry = FindCatalogEntry(tourLabelNoTime);
                if (catalogEntry != null)
                {
                    if (string.IsNullOrWhiteSpace(effectiveMeetingLocation))
                    {
                        effectiveMeetingLocation = catalogEntry.MeetingPlace ?? string.Empty;
                    }
                    var catalogMeeting = catalogEntry.MeetingTime
                        ?? catalogEntry.Times.FirstOrDefault()
                        ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(effectiveMeetingTime) ||
                        string.Equals(effectiveMeetingTime, effectiveTourStartTime, StringComparison.OrdinalIgnoreCase))
                    {
                        effectiveMeetingTime = catalogMeeting;
                    }
                    if (string.IsNullOrWhiteSpace(effectiveTourStartTime))
                    {
                        effectiveTourStartTime = catalogMeeting;
                    }
                }
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

                var meetingTimeDisplay = FormatAsAmPm(effectiveMeetingTime);
                var tempMeetingTime = ComputeTempMeetingTime(tourNode, walkerData);
                var tempSignature = VendorLinkResolver.ResolveTempSignature(stageHint, vendorName, allToursLink);
                var tourStartDisplay = FormatAsAmPm(effectiveTourStartTime);

                text = text
                    .Replace("{walker}", name)
                    .Replace("{walkerFirst}", walkerFirstName)
                    .Replace("{walkerLast}", walkerLastName)
                    .Replace("{name}", name)
                    .Replace("{guest}", string.Empty)
                    .Replace("{phone}", phone)
                    .Replace("{tour}", tourName)
                    .Replace("{tourname}", tourName)
                    .Replace("{date}", formattedDate)
                    .Replace("{tourDayOfWeek}", walkerData?.TourDayOfWeek ?? string.Empty)
                    .Replace("{tourMonthAndDayOrdinal}", walkerData?.TourMonthAndDayOrdinal ?? string.Empty)
                    .Replace("{monthOfTour}", walkerData?.MonthOfTour ?? string.Empty)
                    .Replace("{displayDayOrdinal}", walkerData?.DisplayDayOrdinal ?? string.Empty)
                    .Replace("{displayDate}", walkerData?.DisplayDate ?? string.Empty)
                    .Replace("{displayTime}", FormatAsAmPm(walkerData?.DisplayTime))
                    .Replace("{tourStartTime}", tourStartDisplay)
                    .Replace("{meetingTime}", meetingTimeDisplay)
                    .Replace("{tempMeetingTime}", tempMeetingTime)
                    .Replace("{startMinus10}", tempMeetingTime)
                    .Replace("{tempSignature}", tempSignature)
                    .Replace("{meetingLocation}", effectiveMeetingLocation)
                    .Replace("{meetingInstructions}", effectiveMeetingInstructions)
                    .Replace("{vendorName}", vendorName)
                    .Replace("{signature}", templateSignature ?? string.Empty);
                text = ApplyVendorLinkTokenReplacements(text, resolvedVendorLink, resolvedVendorTourLink, resolvedVendorReviewLink, allToursLink);
                text = text.Replace("{galleryLink}", ResolveLinkTokenDisplayValue(resolvedGalleryLink, "galleryLink"));
                return text.Replace("&amp;", "&");
            }
        }

        private string ResolveEffectiveGalleryLink(ShapedWalkerData? walkerData, ShapedTreeNode? tourNode)
        {
            if (!string.IsNullOrWhiteSpace(currentGalleryUrl))
            {
                return currentGalleryUrl.Trim();
            }

            var publicId = !string.IsNullOrWhiteSpace(walkerData?.GuideReportPublicId)
                ? walkerData!.GuideReportPublicId.Trim()
                : GetGuideReportPublicIdForTour(tourNode).Trim();

            if (string.IsNullOrWhiteSpace(publicId))
            {
                return string.Empty;
            }

            var baseUrl = Configuration["PublicGallery:BaseUrl"] ?? "http://testcity.w41.wh-2.com";
            return $"{baseUrl}/tour-gallery/{publicId}.html";
        }

        // 2025-11-20 00:00 UTC - Helper (VERBATIM)
        private string BuildSmsAppUrl(string phoneNumber, string message)
        {
            try
            {
                var e164Phone = FormatPhoneToE164(phoneNumber);
                if (string.IsNullOrEmpty(e164Phone))
                {
                    return "";
                }

                // For SMS app, use the message exactly as written
                var encodedMessage = Uri.EscapeDataString(message ?? string.Empty);
                var smsUrl = $"sms:{e164Phone}&body={encodedMessage}";
                return smsUrl;
            }
            catch
            {
                return "";
            }
        }







        private async void OnSmsDialogMessageChanged(ChangeEventArgs e)
        {
            var newVal = e.Value?.ToString();
            var tourLabel = selectedSmsWalkerInfo?.TourName ?? selectedSmsTourNode?.Label;
            // 2025-02-05 - Pass date label
            var dateLabel = selectedSmsWalkerInfo?.TourDate; // This might be "Friday..." or "2025-..."
            // Best effort to get the original date label from tree if node available
             if (selectedSmsTourNode != null && TourData?.TreeNodes != null)
            {
                var parent = TourData.TreeNodes.FirstOrDefault(d => d.Children.Contains(selectedSmsTourNode));
                if (parent != null) dateLabel = parent.Label;
            }

            if (!string.IsNullOrWhiteSpace(tourLabel))
            {
                OnMessageSelected(dateLabel, tourLabel, newVal);
            }

            selectedSmsMessage = InMemoryMessages?.FirstOrDefault(m => m.Id == newVal);
            var smsVendorName = selectedSmsWalkerNode?.WalkerData?.VendorName
                ?? selectedSmsVendorNode?.Label
                ?? selectedSmsWalkerInfo?.VendorName;
            var smsTourTime = selectedSmsWalkerNode?.WalkerData?.TourTime
                ?? selectedSmsWalkerNode?.WalkerData?.DisplayTime
                ?? string.Empty;
            await RefreshGalleryUrlForThankYouTemplateAsync(
                selectedSmsMessage,
                dateLabel,
                tourLabel,
                smsTourTime,
                smsVendorName);

            if (selectedSmsWalkerNode != null && selectedSmsTourNode != null)
            {
                var w = selectedSmsWalkerNode?.WalkerData;
                var formattedDate = FormatDateForVcard(w?.TourDate ?? "");
                smsMessage = BuildSmsMessageForWalker(selectedSmsWalkerNode, selectedSmsVendorNode, selectedSmsTourNode, formattedDate);
                RefreshSmsUrl();
                StateHasChanged();
            }
        }

        private async void OnWhatsAppDialogMessageChanged(ChangeEventArgs e)
        {
            var newVal = e.Value?.ToString();
            var tourLabel = selectedWalkerInfo?.TourName ?? selectedWhatsAppTourNode?.Label;
             // 2025-02-05 - Pass date label
            var dateLabel = selectedWalkerInfo?.TourDate; 
             if (selectedWhatsAppTourNode != null && TourData?.TreeNodes != null)
            {
                var parent = TourData.TreeNodes.FirstOrDefault(d => d.Children.Contains(selectedWhatsAppTourNode));
                if (parent != null) dateLabel = parent.Label;
            }

            if (!string.IsNullOrWhiteSpace(tourLabel))
            {
                OnMessageSelected(dateLabel, tourLabel, newVal);
            }

            var selectedMessage = InMemoryMessages?.FirstOrDefault(m => m.Id == newVal);
            var waVendorName = selectedWhatsAppWalkerNode?.WalkerData?.VendorName
                ?? selectedWhatsAppVendorNode?.Label
                ?? selectedWalkerInfo?.VendorName;
            var waTourTime = selectedWhatsAppWalkerNode?.WalkerData?.TourTime
                ?? selectedWhatsAppWalkerNode?.WalkerData?.DisplayTime
                ?? string.Empty;
            await RefreshGalleryUrlForThankYouTemplateAsync(
                selectedMessage,
                dateLabel,
                tourLabel,
                waTourTime,
                waVendorName);

            if (selectedWhatsAppWalkerNode != null && selectedWhatsAppTourNode != null)
            {
                var w = selectedWhatsAppWalkerNode?.WalkerData;
                // Logic from OpenWhatsAppDialog
                var tourDateStr = w?.TourDate ?? selectedWalkerInfo?.TourDate ?? "";
                var dayOfWeek = DateTime.TryParse(tourDateStr, out var parsedDate) ? parsedDate.ToString("dddd") : "your tour date";
                var monthDay = DateTime.TryParse(tourDateStr, out var parsedDate2) ? parsedDate2.ToString("MMMM d") : "your tour date";
                var formattedDate = FormatDateForVcard(tourDateStr);
                var dayLabel = $"{dayOfWeek} {monthDay}".Trim();
                var tourLabelNoTime = GetTourNameWithoutTime(selectedWhatsAppTourNode?.Label);

                var waMessage = BuildWhatsAppMessageForWalker(selectedWhatsAppWalkerNode, selectedWhatsAppVendorNode, selectedWhatsAppTourNode, formattedDate, tourLabelNoTime, dayLabel);
                whatsAppMessage = waMessage;
                whatsAppAppMessage = waMessage;
                RefreshWhatsAppUrls();
                StateHasChanged();
            }
        }

        // 2025-02-05 - Restored and updated for Razor dialog bindings using composite key
        private string GetSelectedMessageId(string? dateLabel, string? tourName)
        {
            if (string.IsNullOrWhiteSpace(tourName)) return "";
            var key = GetTourKey(dateLabel, tourName);
            return selectedMessageIdByTour.TryGetValue(key, out var id) ? id ?? "" : "";
        }

        //private void InsertTemplateField(string targetField, string token)
        //{
        //    if (string.IsNullOrWhiteSpace(token)) return;

        //    if (string.Equals(targetField, "smsMessage", StringComparison.OrdinalIgnoreCase))
        //    {
        //        smsMessage = (smsMessage ?? "") + token;
        //    }
        //    else if (string.Equals(targetField, "whatsAppMessage", StringComparison.OrdinalIgnoreCase))
        //    {
        //        whatsAppMessage = (whatsAppMessage ?? "") + token;
        //    }
        //    else if (string.Equals(targetField, "whatsAppAppMessage", StringComparison.OrdinalIgnoreCase))
        //    {
        //        whatsAppAppMessage = (whatsAppAppMessage ?? "") + token;
        //    }
        //    StateHasChanged();
        //}

        // 2026-02-07: Append gallery link to message if toggle is enabled
        private string AppendGalleryLinkIfNeeded(string message)
        {
            if (includeGalleryLink && !string.IsNullOrEmpty(currentGalleryUrl))
            {
                if (!string.IsNullOrWhiteSpace(message) &&
                    message.Contains(currentGalleryUrl, StringComparison.OrdinalIgnoreCase))
                {
                    return message;
                }

                return $"{message}\n\nView your tour photos: {currentGalleryUrl}";
            }
            return message;
        }

        private static bool LooksLikeTemplateText(string? text)
        {
            return !string.IsNullOrWhiteSpace(text) &&
                   text!.Contains('{') &&
                   text.Contains('}');
        }

        private void ResolveSmsDialogTokensInEditor()
        {
            if (selectedSmsWalkerNode == null || selectedSmsVendorNode == null || selectedSmsTourNode == null)
            {
                return;
            }

            if (!LooksLikeTemplateText(smsMessage))
            {
                return;
            }

            var tourDate = selectedSmsWalkerNode.WalkerData?.TourDate
                ?? selectedSmsWalkerInfo?.TourDate
                ?? string.Empty;
            var formattedDate = FormatDateForVcard(tourDate);
            smsMessage = BuildSmsMessageForWalker(
                selectedSmsWalkerNode,
                selectedSmsVendorNode,
                selectedSmsTourNode,
                formattedDate,
                smsMessage);
        }

        private void ResolveWhatsAppTokensInEditor(bool useAppMessage)
        {
            if (selectedWhatsAppWalkerNode == null || selectedWhatsAppVendorNode == null || selectedWhatsAppTourNode == null)
            {
                return;
            }

            var input = useAppMessage ? whatsAppAppMessage : whatsAppMessage;
            if (!LooksLikeTemplateText(input))
            {
                return;
            }

            var w = selectedWhatsAppWalkerNode.WalkerData;
            var tourDateStr = w?.TourDate ?? selectedWalkerInfo?.TourDate ?? string.Empty;
            var formattedDate = FormatDateForVcard(tourDateStr);
            var dayOfWeek = DateTime.TryParse(tourDateStr, out var parsedDate) ? parsedDate.ToString("dddd") : "your tour date";
            var monthDay = DateTime.TryParse(tourDateStr, out var parsedDate2) ? parsedDate2.ToString("MMMM d") : "your tour date";
            var dayLabel = $"{dayOfWeek} {monthDay}".Trim();
            var tourLabelNoTime = GetTourNameWithoutTime(selectedWhatsAppTourNode.Label);

            var resolved = BuildWhatsAppMessageForWalker(
                selectedWhatsAppWalkerNode,
                selectedWhatsAppVendorNode,
                selectedWhatsAppTourNode,
                formattedDate,
                tourLabelNoTime,
                dayLabel,
                input);

            if (useAppMessage)
            {
                whatsAppAppMessage = resolved;
            }
            else
            {
                whatsAppMessage = resolved;
            }
        }

        private async Task RefreshGalleryUrlForThankYouTemplateAsync(
            InMemoryMessageTemplate? selectedMessage,
            string? dateLabel,
            string? tourLabel,
            string? tourTime,
            string? vendorName)
        {
            if (string.IsNullOrWhiteSpace(dateLabel) ||
                string.IsNullOrWhiteSpace(tourLabel))
            {
                currentGalleryUrl = null;
                includeGalleryLink = false;
                isLoadingGalleryUrl = false;
                StateHasChanged();
                return;
            }

            isLoadingGalleryUrl = true;
            StateHasChanged();

            var lookupTourTime = ResolveGalleryLookupTourTime(tourTime, tourLabel);
            currentGalleryUrl = await FetchGalleryUrlForTourAsync(
                dateLabel,
                tourLabel,
                lookupTourTime,
                vendorName);

            isLoadingGalleryUrl = false;
            if (string.IsNullOrWhiteSpace(currentGalleryUrl))
            {
                includeGalleryLink = false;
                if (selectedMessage?.Type?.Contains("thank", StringComparison.OrdinalIgnoreCase) == true)
                {
                    await ShowGalleryMissingErrorAsync(vendorName);
                }
            }

            StateHasChanged();
        }

        private string? ResolveGalleryLookupTourTime(string? explicitTourTime, string? tourLabel)
        {
            if (!string.IsNullOrWhiteSpace(explicitTourTime))
            {
                return explicitTourTime.Trim();
            }

            var fromProvidedLabel = ExtractTimeFromTourLabel(tourLabel);
            if (!string.IsNullOrWhiteSpace(fromProvidedLabel))
            {
                return fromProvidedLabel;
            }

            var fromSelectedSmsTour = ExtractTimeFromTourLabel(selectedSmsTourNode?.Label);
            if (!string.IsNullOrWhiteSpace(fromSelectedSmsTour))
            {
                return fromSelectedSmsTour;
            }

            var fromSelectedWaTour = ExtractTimeFromTourLabel(selectedWhatsAppTourNode?.Label);
            if (!string.IsNullOrWhiteSpace(fromSelectedWaTour))
            {
                return fromSelectedWaTour;
            }

            return null;
        }

        private async Task ShowGalleryMissingErrorAsync(string? vendorName)
        {
            var vendorLabel = string.IsNullOrWhiteSpace(vendorName) ? "this vendor" : vendorName.Trim();
            try
            {
                await ToastService.ShowErrorAsync(
                    "Gallery Not Found",
                    $"No gallery file exists for {vendorLabel}. Tried vendor-specific first, then general gallery.");
            }
            catch
            {
                // no-op
            }
        }

        private async Task OpenWhatsAppApp()
        {
            var phone = selectedWalkerInfo?.Phone ?? "";
            var cleanPhone = phone.Replace("+", "").Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "");
            
            // 2026-02-07: Include gallery link if checkbox is checked
            var finalMessage = AppendGalleryLinkIfNeeded(whatsAppAppMessage);
            
            var url = string.IsNullOrEmpty(finalMessage)
                ? $"whatsapp://send?phone={cleanPhone}"
                : $"whatsapp://send?phone={cleanPhone}&text={Uri.EscapeDataString(finalMessage)}";

            // 2025-12-16 - fixing buttons for iphone
            // Fire-and-forget the tracking to ensure window.open keeps user gesture context
            if (selectedWhatsAppWalkerNode != null && selectedWhatsAppTourNode != null)
            {
                var stage = DetermineStageForTour(selectedWhatsAppTourNode.Label);
                var templateId = ResolveTemplateIdForTour(selectedWhatsAppTourNode);
                _ = AutoMarkStageAsync(selectedWhatsAppWalkerNode, selectedWhatsAppTourNode, stage, "wa", templateId);
            }

            // 2025-12-16 - fixing buttons for iphone: Navigation is now via <a> tag href
            // try { await JSRuntime.InvokeVoidAsync("open", url, "_blank"); } catch { }

            CloseWhatsAppDialog();
        }

        // 2025-11-19 00:00 UTC - Send via WhatsApp Web
        private async Task SendWhatsAppMessage()
        {
            var phone = selectedWalkerInfo?.Phone ?? "";
            var cleanPhone = phone.Replace("+", "").Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "");
            
            // 2026-02-07: Include gallery link if checkbox is checked
            var finalMessage = AppendGalleryLinkIfNeeded(whatsAppMessage);
            
            var url = string.IsNullOrEmpty(finalMessage)
                ? $"https://wa.me/{cleanPhone}"
                : $"https://wa.me/{cleanPhone}?text={Uri.EscapeDataString(finalMessage)}";

            // 2025-12-16 - fixing buttons for iphone
            // Fire-and-forget the tracking to ensure window.open keeps user gesture context
            if (selectedWhatsAppWalkerNode != null && selectedWhatsAppTourNode != null)
            {
                var stage = DetermineStageForTour(selectedWhatsAppTourNode.Label);
                var templateId = ResolveTemplateIdForTour(selectedWhatsAppTourNode);
                _ = AutoMarkStageAsync(selectedWhatsAppWalkerNode, selectedWhatsAppTourNode, stage, "wa", templateId);
            }

            // 2025-12-16 - fixing buttons for iphone: Navigation is now via <a> tag href
            // try { await JSRuntime.InvokeVoidAsync("open", url, "_blank"); } catch { }

            CloseWhatsAppDialog();
        }



        // 2025-11-19 00:00 UTC - Open WhatsApp dialog for a walker (adapted to ShapedTreeNode)
        private async Task OpenWhatsAppDialog(ShapedTreeNode walkerNode, ShapedTreeNode vendorNode, ShapedTreeNode tourNode)
        {
            _ = JSRuntime.InvokeVoidAsync("console.log", $"OpenWhatsAppDialog: walker={walkerNode?.Label}, vendor={vendorNode?.Label}, tour={tourNode?.Label}");
            _ = MarkBookingMessageSentAsync(walkerNode);
            selectedWhatsAppWalkerNode = walkerNode;
            selectedWhatsAppTourNode = tourNode;
            selectedWhatsAppVendorNode = vendorNode;
            var w = walkerNode?.WalkerData;
            var tourLabelNoTime = NormalizeTourNameForMessage(GetTourNameWithoutTime(tourNode?.Label));
            selectedWalkerInfo = new WalkerInfo
            {
                Name = w?.DisplayName ?? walkerNode?.Label ?? "",
                Phone = w?.DisplayPhone ?? "",
                Attendees = w?.Attendees,
                TourName = NormalizeTourNameForMessage(w?.TourName ?? tourLabelNoTime ?? ""),
                TourDate = w?.TourDate ?? "",
                VendorName = w?.VendorName ?? vendorNode?.Label ?? ""
            };

            // Default message VERBATIM per Workstation (day-of-week + month/day), using shaped TourDate
            var tourDateStr = w?.TourDate ?? selectedWalkerInfo.TourDate ?? "";
            var dayOfWeek = DateTime.TryParse(tourDateStr, out var parsedDate) ? parsedDate.ToString("dddd") : "your tour date";
            var monthDay = DateTime.TryParse(tourDateStr, out var parsedDate2) ? parsedDate2.ToString("MMMM d") : "your tour date";
            var formattedDate = FormatDateForVcard(tourDateStr);
            var dayLabel = $"{dayOfWeek} {monthDay}".Trim();

            // 2025-12-06 15:20 CST - Align WhatsApp dialog message with template selection (like SMS)
            if (!HasTemplateForTour(walkerNode, tourNode, tourDateStr))
            {
                await ShowTemplateRequiredAsync(tourNode, "WhatsApp");
            }
            var waMessage = BuildWhatsAppMessageForWalker(walkerNode, vendorNode, tourNode, formattedDate, tourLabelNoTime, dayLabel);
            whatsAppMessage = waMessage;
            whatsAppAppMessage = waMessage;

            // Pre-load gallery availability for currently selected template.
            var dateLabel = selectedWalkerInfo?.TourDate;
            if (selectedWhatsAppTourNode != null && TourData?.TreeNodes != null)
            {
                var parent = TourData.TreeNodes.FirstOrDefault(d => d.Children.Contains(selectedWhatsAppTourNode));
                if (parent != null)
                {
                    dateLabel = parent.Label;
                }
            }
            var selectedMessageId = GetSelectedMessageId(dateLabel, selectedWalkerInfo?.TourName);
            var selectedMessage = InMemoryMessages?.FirstOrDefault(m => string.Equals(m.Id, selectedMessageId, StringComparison.Ordinal));
            var tourTime = w?.TourTime ?? w?.DisplayTime ?? string.Empty;
            await RefreshGalleryUrlForThankYouTemplateAsync(
                selectedMessage,
                dateLabel,
                selectedWalkerInfo?.TourName,
                tourTime,
                selectedWalkerInfo?.VendorName);

            waMessage = BuildWhatsAppMessageForWalker(walkerNode, vendorNode, tourNode, formattedDate, tourLabelNoTime, dayLabel);
            whatsAppMessage = waMessage;
            whatsAppAppMessage = waMessage;

            showWhatsAppDialog = true;
            RefreshWhatsAppUrls();
            _ = JSRuntime.InvokeVoidAsync("console.log", $"OpenWhatsAppDialog: showWhatsAppDialog set -> {showWhatsAppDialog}");
            StateHasChanged();
        }

        // 2025-11-19 00:00 UTC - Close WhatsApp dialog
        private void CloseWhatsAppDialog()
        {
            _ = JSRuntime.InvokeVoidAsync("console.log", "CloseWhatsAppDialog invoked");
            showWhatsAppDialog = false;
            activeWhatsAppTabIndex = 0;
            whatsAppAppMessage = "";
            whatsAppMessage = "";
            selectedWalkerInfo = new WalkerInfo();
            selectedWhatsAppWalkerNode = null;
            selectedWhatsAppTourNode = null;
            
            // 2026-02-07: Reset gallery link state
            currentGalleryUrl = null;
            includeGalleryLink = false;
            isLoadingGalleryUrl = false;
            
            StateHasChanged();
        }

        // 2025-11-19 00:00 UTC - Active tab setter
        private void SetActiveWhatsAppTab(int index)
        {
            activeWhatsAppTabIndex = index;
            StateHasChanged();
        }

        // 2025-11-19 00:00 UTC - Message input handlers
        private void OnWhatsAppAppMessageInput(Microsoft.AspNetCore.Components.ChangeEventArgs e)
        {
            whatsAppAppMessage = e.Value?.ToString() ?? "";
            ResolveWhatsAppTokensInEditor(useAppMessage: true);
            RefreshWhatsAppUrls();
            StateHasChanged();
        }

        private void OnWhatsAppMessageInput(Microsoft.AspNetCore.Components.ChangeEventArgs e)
        {
            whatsAppMessage = e.Value?.ToString() ?? "";
            ResolveWhatsAppTokensInEditor(useAppMessage: false);
            RefreshWhatsAppUrls();
            StateHasChanged();
        }

        private void RefreshWhatsAppUrls()
        {
            var phone = selectedWalkerInfo?.Phone ?? "";
            var cleanPhone = phone.Replace("+", "").Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "");
            
            whatsAppAppUrl = string.IsNullOrEmpty(whatsAppAppMessage)
                ? $"whatsapp://send?phone={cleanPhone}"
                : $"whatsapp://send?phone={cleanPhone}&text={Uri.EscapeDataString(whatsAppAppMessage)}";

            whatsAppWebUrl = string.IsNullOrEmpty(whatsAppMessage)
                ? $"https://wa.me/{cleanPhone}"
                : $"https://wa.me/{cleanPhone}?text={Uri.EscapeDataString(whatsAppMessage)}";
        }

        private void RefreshSmsUrl()
        {
            var phone = selectedSmsWalkerInfo?.Phone ?? "";
            // 2026-02-07: Include gallery link if checkbox is checked
            var finalMessage = AppendGalleryLinkIfNeeded(smsMessage);
            smsAppUrl = BuildSmsAppUrl(phone, finalMessage);
        }

        // 2025-12-04 00:00 UTC - Copy WhatsApp message to clipboard (align with Workstation; use app message)
        private async Task CopyWhatsAppMessage()
        {
            if (string.IsNullOrEmpty(whatsAppAppMessage)) return;
            try { await JSRuntime.InvokeVoidAsync("blazorCopyText", whatsAppAppMessage); } catch { }
        }

        private void OnBulkSmsAppMessageInput(Microsoft.AspNetCore.Components.ChangeEventArgs e)
        {
            bulkSmsAppMessage = e.Value?.ToString() ?? "";
            RefreshBulkWhatsAppUrls();
            StateHasChanged();
        }

        private void OnBulkSmsMessageInput(Microsoft.AspNetCore.Components.ChangeEventArgs e)
        {
            bulkSmsMessage = e.Value?.ToString() ?? "";
            RefreshBulkWhatsAppUrls();
            StateHasChanged();
        }

        private void RefreshBulkWhatsAppUrls()
        {
            var waPhone = GetWhatsappPhoneDigits(bulkSmsPhone);
            if (string.IsNullOrWhiteSpace(waPhone))
            {
                bulkSmsAppUrl = "";
                bulkSmsWebUrl = "";
                return;
            }

            var appBody = BuildBulkWhatsAppBody(useAppMessage: true);
            bulkSmsAppUrl = string.IsNullOrWhiteSpace(appBody)
                ? $"whatsapp://send?phone={waPhone}"
                : $"whatsapp://send?phone={waPhone}&text={Uri.EscapeDataString(appBody)}";

            var webBody = BuildBulkWhatsAppBody(useAppMessage: false);
            bulkSmsWebUrl = string.IsNullOrWhiteSpace(webBody)
                ? $"https://wa.me/{waPhone}"
                : $"https://wa.me/{waPhone}?text={Uri.EscapeDataString(webBody)}";
        }

        private async Task<string?> FetchGalleryUrlForTourAsync(string? tourDate, string? tourName, string? tourTime, string? vendorName)
        {
            if (string.IsNullOrWhiteSpace(tourDate) ||
                string.IsNullOrWhiteSpace(tourName) ||
                !DateTime.TryParse(tourDate, out var parsedDate))
            {
                return null;
            }

            var normalizedTourTime = string.IsNullOrWhiteSpace(tourTime) ? null : tourTime.Trim();
            var resolved = await GalleryLinkResolver.ResolveAsync(parsedDate.Date, tourName, normalizedTourTime, vendorName);
            return resolved.Found ? resolved.Url : null;
        }

    }


}
