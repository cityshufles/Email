# Context for AI Assistance
## Objective
The mobile application's message generation must have **exact parity** with the desktop logic. Discrepancies in data population (specifically `MeetingPlace`, [MeetingTime](file:///c:/Users/daves/source/repos/WhatsAppBusiness/Email/Services/MessageTemplateService.cs#184-195), and `VendorLink`) have caused regression issues in the mobile experience.
## Critical Logic to Replicate
The code below ([BuildWhatsAppMessageForWalker](file:///c:/Users/daves/source/repos/WhatsAppBusiness/Email/Components/Tours/TourTreeDisplay_SMS_WhatsApp.cs#332-480)) serves as the **source of truth**. Any AI working on this must ensure the Mobile [MessageTemplateService.cs](file:///c:/Users/daves/source/repos/WhatsAppBusiness/Email/Services/MessageTemplateService.cs) replicates:
1.  **The "Effective" Value Fallback Chain**:
    *   **Meeting Location**: Checks [Template](file:///c:/Users/daves/source/repos/WhatsAppBusiness/Email/Pages/TourManagementDashboard.razor.cs#404-482) -> [WalkerData](file:///c:/Users/daves/source/repos/WhatsAppBusiness/Email/TourTreeViewShapedData/Models/ShapedWalkerData.cs#10-55) -> `TourCache` -> [Catalog](file:///c:/Users/daves/source/repos/WhatsAppBusiness/Email/Components/Tours/TourTreeDisplay_Main.cs#425-433) -> Fallback to `TourLocation`.
    *   **Meeting Time**: Checks [Template](file:///c:/Users/daves/source/repos/WhatsAppBusiness/Email/Pages/TourManagementDashboard.razor.cs#404-482) -> [WalkerData](file:///c:/Users/daves/source/repos/WhatsAppBusiness/Email/TourTreeViewShapedData/Models/ShapedWalkerData.cs#10-55) -> `TourCache` -> [Catalog](file:///c:/Users/daves/source/repos/WhatsAppBusiness/Email/Components/Tours/TourTreeDisplay_Main.cs#425-433). *Crucially*, if the time matches the Tour Start time (or is missing), it applies a **-10 minute shift** rule.
    *   **Tour Start Time**: Checks [Template](file:///c:/Users/daves/source/repos/WhatsAppBusiness/Email/Pages/TourManagementDashboard.razor.cs#404-482) -> [WalkerData](file:///c:/Users/daves/source/repos/WhatsAppBusiness/Email/TourTreeViewShapedData/Models/ShapedWalkerData.cs#10-55) -> `TourCache` -> [Catalog](file:///c:/Users/daves/source/repos/WhatsAppBusiness/Email/Components/Tours/TourTreeDisplay_Main.cs#425-433).
2.  **Token Replacements**:
    *   Verify checking for `{tempMeetingTime}` (computed value) vs `{meetingTime}` (raw value).
    *   Verify `{meetingLocation}` is the primary location token (desktop does *not* use `{tempMeetingPlace}`).
## Implementation Notes
*   **Mobile Service**: [Email\Services\MessageTemplateService.cs](file:///c:/Users/daves/source/repos/WhatsAppBusiness/Email/Services/MessageTemplateService.cs)
*   **Desktop Reference**: [Email\Components\Tours\TourTreeDisplay_SMS_WhatsApp.cs](file:///c:/Users/daves/source/repos/WhatsAppBusiness/Email/Components/Tours/TourTreeDisplay_SMS_WhatsApp.cs) (Below)
---
# Desktop Template Logic Reference
**Source File:** [Email\Components\Tours\TourTreeDisplay_SMS_WhatsApp.cs](file:///c:/Users/daves/source/repos/WhatsAppBusiness/Email/Components/Tours/TourTreeDisplay_SMS_WhatsApp.cs)
**Lines:** 334 - 479
This method [BuildWhatsAppMessageForWalker](file:///c:/Users/daves/source/repos/WhatsAppBusiness/Email/Components/Tours/TourTreeDisplay_SMS_WhatsApp.cs#332-480) contains the authoritative logic for:
1.  Resolving effective meeting times and locations (including fallbacks to catalog/cache).
2.  Computing "temp" values like `{tempMeetingTime}`.
3.  Performing all token replacements.
## Code Snippet
```csharp
        // 2025-12-06 15:20 CST - WhatsApp message builder aligned with SMS template selection
        // 2025-12-10: Added meeting location/time/instructions token support
        private string BuildWhatsAppMessageForWalker(ShapedTreeNode walkerNode, ShapedTreeNode vendorNode, ShapedTreeNode tourNode, string formattedDate, string tourLabelNoTime, string? dayOfWeekOrDateLabel)
        {
            var tourLabel = tourNode?.Label ?? string.Empty;
            string? templateContent = null;
            string? templateSignature = null;
            string? templateVendorLink = null;
            InMemoryMessageTemplate? selectedMsg = null;
            if (!string.IsNullOrWhiteSpace(tourLabel) &&
                selectedMessageIdByTour.TryGetValue(tourLabel, out var selMsgId) &&
                !string.IsNullOrWhiteSpace(selMsgId) &&
                InMemoryMessages != null && InMemoryMessages.Count > 0)
            {
                var tmpl = InMemoryMessages.FirstOrDefault(m => string.Equals(m.Id, selMsgId, StringComparison.Ordinal));
                selectedMsg = tmpl;
                templateContent = tmpl?.Content;
                templateSignature = tmpl?.Signature;
                //if (walkerNode.Label.Contains("freetour") {
                //    tmpl.VendorLink = tmpl?.VendorLink.Replace(" freetour replacement");
                //}
                templateVendorLink = tmpl?.VendorLink;
            }
            var walkerData = walkerNode?.WalkerData;
            var name = walkerData?.DisplayName ?? walkerNode?.Label ?? "";
            var phone = walkerData?.DisplayPhone ?? "";
            var attendees = walkerData?.Attendees;
            var vendorName = walkerData?.VendorName ?? vendorNode?.Label ?? "";
            var tourName = walkerData?.TourName ?? tourLabelNoTime;
            var guestText = attendees.HasValue ? attendees.Value.ToString() : string.Empty;
            if (!string.IsNullOrWhiteSpace(templateContent))
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
                var tourRowCache = ResolveTourByLabelForWalkers(messageWalkersToursCache, tourLabelNoTime);
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
                var tempSignature = ComputeTempSignature(vendorName);
                var tourStartDisplay = FormatAsAmPm(effectiveTourStartTime);
                text = text
                    .Replace("{walker}", name)
                    .Replace("{name}", name)
                    .Replace("{guest}", guestText)
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
                    .Replace("{tempSignature}", tempSignature)
                    .Replace("{meetingLocation}", effectiveMeetingLocation)
                    .Replace("{meetingInstructions}", effectiveMeetingInstructions)
                    .Replace("{vendorName}", vendorName)
                    .Replace("{signature}", templateSignature ?? string.Empty)
                    .Replace("{vendorLink}", templateVendorLink ?? string.Empty);
                return text.Replace("&amp;", "&");
            }
            // Fallback to legacy default (keeps previous UX)
            var dayLabel = dayOfWeekOrDateLabel ?? "your tour date";
            return
                $"Hello {name}. Thank you for booking your \"{tourLabelNoTime}\" on {dayLabel} with CityShuffles.\n\n" +
                $"Your tour will meet at {formattedDate}\n\n" +
                $"We look forward to touring with you! You will be contacted with your guides name/number the day of/before your tour.\n" +
                $"If you need help with anything in NYC, please contact CityShuffles anytime at this number\n\n" +
                $"917-938-1170\n\n" +
                $"And be sure to check out our other food and history tours!\n\n" +
                $"https://www.guruwalk.com/gurus/5vfugt2iidv27yrsd6fm";
        }