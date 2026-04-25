

# **MOBILE MESSAGE TEMPLATE PARITY IMPLEMENTATION PLAN**

**Date:** 2025-12-18  
**Target:** Exact parity between Desktop (`TourTreeDisplay_SMS_WhatsApp.cs`) and Mobile (`MobileTourManagementDashboard.razor` + `MobileTourCard.razor`)  
**Critical Issue:** Mobile templates are omitting key token replacements despite having data available
** ABSOULTE MUST: **Desktop and mobile render identical messages for same input** WE MUST HAVE THIS
---

## **EXECUTIVE SUMMARY**

The mobile implementation has **partial template support** but **FAILS to replicate the complete desktop token replacement logic**. The desktop's `BuildWhatsAppMessageForWalker()` method (lines 334-479 in `TourTreeDisplay_SMS_WhatsApp.cs`) is the **source of truth**. Mobile currently uses `MessageTemplateService.PopulateMessageTemplate()` which is **incomplete** and **missing critical fallback chains**.

**Root Cause:** Mobile is bypassing critical data population steps that desktop performs:
1. ✅ Basic guide tokens work (`{guide}`, `{guideFirstName}`)
2. ❌ **MISSING:** Fallback chain for meeting time/place (Template → WalkerData → TourCache → Catalog)
3. ❌ **MISSING:** Time shifting logic (the `-10 minute` rule for `{tempMeetingTime}`)
4. ❌ **MISSING:** Company phone replacement (`{phone}` = "+1 917-938-1170")
5. ❌ **INCOMPLETE:** Pre-computed date fields from `ShapedWalkerData` not being passed to mobile models

---

## **CRITICAL DATA FLOW ANALYSIS**

### **Desktop (Working) Flow:**
```
TourTreeDisplay (component)
  ├─ SendMessage() / GetQuickWhatsAppUrl()
  ├─ BuildWhatsAppMessageForWalker(walkerNode, vendorNode, tourNode, ...)
  │  ├─ Extract template from selectedMessageIdByTour
  │  ├─ Gather effectiveValues using fallback chain:
  │  │   ├─ Template value (if set) → highest priority
  │  │   ├─ WalkerData value (from ShapedWalkerData)
  │  │   ├─ TourRowCache value (messageWalkersToursCache lookup)
  │  │   └─ Catalog value (FindCatalogEntry lookup) → lowest priority
  │  ├─ Apply -10 minute shift if meeting time matches tour start time
  │  ├─ Replace ALL 40+ tokens (see complete list below)
  │  └─ Return formatted message
  └─ Open WhatsApp with pre-filled message
```

### **Mobile (Broken) Flow:**
```
MobileTourManagementDashboard.razor
  ├─ SendMessage() passes:
  │   └─ MobileWalkerInfoModel (walker) + DbGuide (guide)
  ├─ MobileTourCard.razor
  │   └─ GetQuickWhatsAppUrl()
  │       └─ Calls MessageTemplateService.PopulateMessageTemplate()
  │           ├─ Replaces basic tokens only
  │           ├─ MISSING: Meeting time/place fallback chain
  │           ├─ MISSING: -10 minute shift logic
  │           ├─ MISSING: Company phone
  │           └─ Returns incomplete message
  └─ Open WhatsApp with partial message
```

---

## **DETAILED IMPLEMENTATION GAPS**

### **Gap 1: MobileWalkerInfoModel Population (Transformation Tier)**
**File:** `Email/Pages/Mobile/MobileTourManagementDashboard.razor` (lines 551-574)

**Current State:**
```csharp
vendor.Walkers.Add(new MobileWalkerInfoModel
{
    DisplayName = name,
    Phone = phone,
    Attendees = attendees,
    VendorName = vendorNode.Label,
    TourName = tourNode.Label,
    DateLabel = dateNode.Label,
    MessageId = walkerData?.MessageId ?? string.Empty,
    BookingCode = walkerData?.BookingCode ?? string.Empty,
    MessageSent = walkerData?.MessageSent ?? false,
    MessageSentAtUtc = walkerData?.MessageSentAtUtc,
    // Pre-computed date/time fields from ShapedWalkerData
    TourDayOfWeek = walkerData?.TourDayOfWeek,
    TourMonthAndDayOrdinal = walkerData?.TourMonthAndDayOrdinal,
    MonthOfTour = walkerData?.MonthOfTour,
    DisplayDayOrdinal = walkerData?.DisplayDayOrdinal,
    DisplayDate = walkerData?.DisplayDate,
    DisplayTime = walkerData?.DisplayTime,
    TourStartTime = walkerData?.TourStartTime,
    MeetingTime = walkerData?.MeetingTime,
    MeetingPlace = walkerData?.MeetingPlace,
    MeetingInstructions = walkerData?.MeetingInstructions
});
```

**✅ CORRECT:** These fields ARE being populated from `ShapedWalkerData`. The problem is **downstream**.

### **Gap 2: MessageTemplateService Token Replacement (Service Tier)**
**File:** `Email/Services/MessageTemplateService.cs` (lines 17-111)

**Current Implementation Analysis:**

**✅ WORKING TOKENS:**
- `{name}`, `{walker}` → from `walker.DisplayName`
- `{guest}` → from `walker.Attendees`
- `{tour}`, `{tourname}` → from `walker.TourName`
- `{vendorName}` → from `walker.VendorName`
- `{guide}`, `{guideFirstName}`, `{guideLastName}`, `{guidePhone}`, `{guideEmail}` → from `DbGuide`
- `{signature}`, `{vendorLink}` → from template
- `{tourDayOfWeek}`, `{tourMonthAndDayOrdinal}`, `{monthOfTour}`, `{displayDayOrdinal}` → from pre-computed fields
- `{meetingLocation}`, `{meetingPlace}`, `{meetingInstructions}` → from fallback chain

**❌ BROKEN/INCOMPLETE:**

| Token | Desktop Implementation | Mobile Implementation | Issue |
|-------|----------------------|----------------------|-------|
| `{phone}` | `"+1 917-938-1170"` (company contact) | Not implemented | **MISSING** |
| `{tempMeetingTime}` | Time extracted from tour label, shifted -10 min | Implemented but incomplete logic | **INCOMPLETE extraction chain** |
| `{meetingLocation}` | Template → WalkerData → TourCache → Catalog → TourLocation | Template → WalkerData only | **MISSING fallback tiers** |
| `{meetingTime}` | Has special -10 min shift logic | Basic replacement only | **MISSING shift logic** |
| `{tempSignature}` | Vendor-specific URL lookup | Implemented correctly | ✅ |
| `{displayDate}` | Pre-computed OR fallback | Uses pre-computed | ✅ |

### **Gap 3: Missing Time Calculation Logic**

Desktop has **this critical logic** (lines 115-125 in `TourTreeDisplay_SMS_WhatsApp.cs`):

```csharp
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
```

Mobile has **partial version** but:
- Does NOT check if meeting time matches tour start time (line 51 only checks if empty)
- Does NOT apply secondary fallback to effective tour start time before shifting

### **Gap 4: Fallback Chain NOT Replicated**

Desktop `BuildWhatsAppMessageForWalker()` implements **three-tier fallback** (lines 369-425):

```
TIER 1 (Template):
  selectedMsg?.MeetingTime / selectedMsg?.MeetingPlace / selectedMsg?.TourStartTime

TIER 2 (WalkerData/Current Booking):
  walkerData?.MeetingTime / walkerData?.MeetingPlace / walkerData?.TourStartTime

TIER 3 (TourRowCache - messageWalkersToursCache):
  tourRowCache.MeetingTime / tourRowCache.MeetingPlace / tourRowCache.TourStartTime

TIER 4 (Catalog):
  catalogEntry.MeetingTime / catalogEntry.Times.FirstOrDefault()
```

**Mobile only checks:**
```
Tier 1: template?.MeetingPlace / template?.MeetingLocation
Tier 2: walker?.MeetingPlace
[MISSING: Tier 3 & 4]
```

---

## **COMPLETE TOKEN REPLACEMENT LIST (Desktop Reference)**

These 40+ tokens MUST work in mobile for parity:

```
WALKER/NAME TOKENS:
  {walker} → DisplayName
  {name} → DisplayName
  {guest} → Attendees (number as string)
  {phone} → "+1 917-938-1170" [COMPANY CONTACT - MISSING]

TOUR/VENDOR TOKENS:
  {tour} → TourName
  {tourname} → TourName
  {vendorName} → VendorName
  {date} → FormattedDate / DisplayDate

DATE TOKENS (pre-computed):
  {tourDayOfWeek} → "Monday", "Tuesday", etc.
  {tourMonthAndDayOrdinal} → "December 18th", etc.
  {monthOfTour} → "December", etc.
  {displayDayOrdinal} → "18th", etc.
  {displayDate} → Full date string
  {displayTime} → Tour time formatted

TIME TOKENS (with fallback chain):
  {tourStartTime} → From effective tour start time (formatted as AM/PM)
  {displayTime} → From walker display time
  {meetingTime} → From effective meeting time (formatted as AM/PM)
  {tempMeetingTime} → Meeting time shifted -10 minutes (computed)

LOCATION TOKENS (with fallback chain):
  {meetingLocation} → Template → WalkerData → TourCache → Catalog → TourLocation
  {meetingPlace} → Alias for {meetingLocation}
  {meetingInstructions} → Template → WalkerData

GUIDE TOKENS (from DbGuide):
  {guide} → "FirstName LastName"
  {guideFirstName} → FirstName
  {guideLastName} → LastName
  {guidePhone} → Phone
  {guideEmail} → Email

SIGNATURE TOKENS (from template or computed):
  {signature} → Template?.Signature
  {tempSignature} → Vendor-specific URL (GuruWalk/FreeTour)
  {vendorLink} → Template?.VendorLink
```

---

## **MIGRATION PLAN: 4-STEP IMPLEMENTATION**

### **STEP 1: Enhance MessageTemplateService (Service Layer)**
**File:** `Email/Services/MessageTemplateService.cs`

**What to change:**
1. Add `{phone}` company contact replacement (+1 917-938-1170)
2. Fix `{meetingTime}` -10 minute shift logic:
   - Must check if meeting time **matches** tour start time (case-insensitive)
   - Must apply shift only if missing OR matches tour start
3. **Verify** pre-computed date field replacements are working correctly

**Files involved:**
- `Email/Services/MessageTemplateService.cs` (lines 41-99)

**Note:** Desktop implementation does NOT use MessageTemplateService for desktop rendering. Desktop has inline logic in `TourTreeDisplay_SMS_WhatsApp.cs`. Mobile is attempting to centralize via service, which is fine, but service must be **complete**.

### **STEP 2: Add Catalog/Cache Fallback Support to MessageTemplateService**
**File:** `Email/Services/MessageTemplateService.cs`

**What to add:**
1. Extend `PopulateMessageTemplate()` signature to accept optional `ITourTreeService` dependency for catalog lookups
2. Implement fallback chain:
   ```
   effectiveMeetingLocation = 
       walker.MeetingPlace 
       ?? template?.MeetingPlace 
       ?? template?.MeetingLocation 
       ?? (await GetFromCatalogAsync(walker.TourName)) 
       ?? DefaultTourLocation
   ```
3. This allows mobile to use same service as desktop **eventually**

**Files involved:**
- `Email/Services/MessageTemplateService.cs`
- Modify mobile `SendMessage()` to pass necessary context

### **STEP 3: Verify MobileWalkerInfoModel Transformation**
**File:** `Email/Pages/Mobile/MobileTourManagementDashboard.razor` (lines 551-574)

**Verify:**
- All pre-computed fields from `ShapedWalkerData` are being mapped:
  - ✅ `TourDayOfWeek`
  - ✅ `TourMonthAndDayOrdinal`
  - ✅ `MonthOfTour`
  - ✅ `DisplayDayOrdinal`
  - ✅ `DisplayDate`
  - ✅ `DisplayTime`
  - ✅ `TourStartTime`
  - ✅ `MeetingTime`
  - ✅ `MeetingPlace`
  - ✅ `MeetingInstructions`

**Status:** Currently appears correct. Verify during integration testing.

### **STEP 4: Update Mobile Rendering Tier (Components)**
**Files:**
- `Email/Pages/Mobile/MobileTourManagementDashboard.razor` (SendMessage method, lines 1271-1361)
- `Email/Components/Mobile/MobileTourCard.razor` (GetQuickWhatsAppUrl method, lines 259-293)

**What to update:**
1. Both components call `MessageTemplateService.PopulateMessageTemplate()`
2. Ensure they pass:
   - `memoryTemplate` (mapped from DbTourMessage)
   - `walker` (MobileWalkerInfoModel with all pre-computed fields)
   - `guide` (DbGuide or null)
3. After service returns message, verify all 40+ tokens are replaced

---

## **KEY DIFFERENCES: Desktop vs. Mobile Data Access**

| Aspect | Desktop | Mobile |
|--------|---------|--------|
| **Data Source** | `ShapedTreeNode` (walkerNode, tourNode, vendorNode) | `MobileWalkerInfoModel` |
| **Template Lookup** | `selectedMessageIdByTour` dictionary | `SelectedMessageId` from UI dropdown |
| **Guide Lookup** | `selectedGuideIdByTour` dictionary (by tour label) | `selectedGuideIdByTour` dictionary (by tour name) |
| **Catalog Access** | Direct via `FindCatalogEntry()` | Loaded during init via `EnsureCatalogAsync()` |
| **Date Formatting** | Computed ad-hoc | Pre-computed in `ShapedWalkerData` |
| **Message Service** | Inline logic in `BuildWhatsAppMessageForWalker()` | `MessageTemplateService.PopulateMessageTemplate()` |

**Critical:** Desktop constructs effectiveValues with full fallback chain **AT MESSAGE BUILD TIME**. Mobile must do same via service.

---

## **IMPLEMENTATION CHECKLIST FOR AI**

**WHEN IMPLEMENTING, FOCUS ONLY ON:**

### Phase 1: MessageTemplateService Enhancement
- [ ] Add `{phone}` → "+1 917-938-1170" replacement
- [ ] Fix meeting time -10 minute shift condition:
  ```csharp
  // Current (WRONG):
  if (string.IsNullOrWhiteSpace(effectiveMeetingTime) && 
      !string.IsNullOrWhiteSpace(effectiveTourStartTime))
  
  // Correct (DESKTOP LOGIC):
  if (!string.IsNullOrWhiteSpace(effectiveTourStartTime) &&
      (string.IsNullOrWhiteSpace(effectiveMeetingTime) ||
       string.Equals(effectiveMeetingTime, effectiveTourStartTime, 
                     StringComparison.OrdinalIgnoreCase)))
  ```
- [ ] Verify all date token replacements work correctly
- [ ] Test with templates containing all 40+ tokens

### Phase 2: Verify Mobile Data Transformation
- [ ] Confirm `TransformToMobile()` (line 524) correctly populates all pre-computed fields
- [ ] Verify `MobileWalkerInfoModel` has ALL fields from `ShapedWalkerData` (lines 551-574)
- [ ] Run integration test with real message data

### Phase 3: Update Mobile Send Methods
- [ ] `MobileTourManagementDashboard.SendMessage()` (lines 1271-1361)
  - Verify `memoryTemplate` is correctly mapped from `DbTourMessage`
  - Verify `guide` is correctly resolved
  - Call `MessageTemplateService.PopulateMessageTemplate()`
  
- [ ] `MobileTourCard.GetQuickWhatsAppUrl()` (lines 259-293)
  - Extract message building logic (currently inline)
  - Delegate to `MessageTemplateService.PopulateMessageTemplate()`
  - Use same parameters as desktop

### Phase 4: Testing & Validation
- [ ] Test each token individually in message templates
- [ ] Test fallback chain (missing meeting time defaults to tour start time)
- [ ] Test -10 minute shift (meeting time becomes tour start - 10 min)
- [ ] Test with guide data (guide tokens must populate)
- [ ] Test without guide data (tokens must be empty, not error)
- [ ] Compare output with desktop for parity

---

## **DONT GET DISTRACTED BY:**

❌ Old message dialog code (removed 2025-12-18)  
❌ In-memory guide management (local storage logic)  
❌ Mobile layout/styling issues  
❌ Database queries (use existing APIs)  
❌ Message collection/processing  
❌ WhatsApp URL encoding (already correct)  

---

## **EXACT FILES TO MODIFY**

1. **`Email/Services/MessageTemplateService.cs`** (12-15 lines of changes)
   - Add `{phone}` token
   - Fix meeting time shift condition
   - Verify all replacements

2. **`Email/Pages/Mobile/MobileTourManagementDashboard.razor`** (0-5 lines, verify only)
   - Lines 551-574: Verify MobileWalkerInfoModel population
   - Lines 1271-1361: Verify SendMessage parameters

3. **`Email/Components/Mobile/MobileTourCard.razor`** (5-10 lines)
   - Lines 259-293: Update GetQuickWhatsAppUrl to use service consistently

---

## **SUCCESS CRITERIA**

✅ **All 40+ tokens replace correctly in mobile messages**  
✅ **Meeting time shows as tour_start_time - 10 minutes**  
✅ **Company phone (+1 917-938-1170) appears in templates using {phone} if the user inserted into db message- depends on database. 
✅ **Guide data populates when guide is assigned**  
✅ **Fallback chain works (template → walker → catalog)**  
✅ **Desktop and mobile render identical messages for same input**  
✅ **No regressions in existing functionality**  

---

**THIS IS A COMPREHENSIVE, LASER-FOCUSED PLAN. DO NOT DEVIATE.**