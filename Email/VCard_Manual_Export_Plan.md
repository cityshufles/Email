# vCard Manual Export & Email Plan
**Created: 2025-12-19 00:00 UTC**

## Problem Statement
- Previous API endpoint downloads are unreliable across platforms
- Users need a simple, straightforward way to generate and export vCards
- Manual download + copy/paste approach is more reliable than file downloads

## Solution Overview
A dedicated Blazor page with:
1. **Manual vCard Generation** — User clicks button, vCards are generated locally
2. **View vCards** — Accordion display of generated vCards with copy-to-clipboard
3. **Manual Download** — User manually copies vCard content and saves it
4. **Email vCards** — Send vCards via SMTP using configured Gmail credentials

---

## Architecture

### New Classes to Create

#### 1. `Email/Services/VCardManualExportService.cs`
- **Purpose**: Generate vCards for manual export (no API call)
- **Methods**:
  - `GenerateVCardsForDay(date, filterType?, vendor?)` → List<VCardDisplayItem>
  - `GenerateVCardsForTour(date, tourLabel, filterType?, vendor?)` → List<VCardDisplayItem>
  - `GenerateVCardsForWalker(messageId)` → VCardDisplayItem
  - `GetFormattedVCardText(VCardDisplayItem)` → string (for display/copy)
- **Dependencies**: `ITourTreeService`, `VCardExportService`
- **Output**: `VCardDisplayItem` (displayable vCard data)

#### 2. `Email/Models/VCardDisplayItem.cs`
- **Purpose**: Simple vCard data model for UI display
- **Properties**:
  - `DisplayName` — Walker name
  - `Phone` — Phone number
  - `TourName` — Tour name
  - `DateLabel` — Formatted date
  - `VCardContent` — Full vCard text (for copy)
  - `MessageId` — Unique identifier

#### 3. `Email/Services/SmtpVCardEmailService.cs`
- **Purpose**: Send vCards via SMTP
- **Methods**:
  - `SendVCardAsync(toAddress, vCardTexts, subject?, ct)` → Task<bool>
  - `ValidateSmtpConfig()` → bool
- **Dependencies**: `IConfiguration` (to read Gmail settings)
- **Config Used**: `appsettings.json` → `Gmail` section
  - Email: `<SET_IN_LOCAL_SECRETS>`
  - Password: `<SET_IN_LOCAL_SECRETS>`
  - ImapServer: `imap.gmail.com`
  - ImapPort: `993`
  - EnableSsl: `true`

#### 4. `Email/Models/VCardEmailRequest.cs`
- **Purpose**: DTO for email requests
- **Properties**:
  - `ToAddress` — Recipient email
  - `VCardTexts` — List<string> of vCard content
  - `Subject` — Optional email subject

---

### New Page to Create

#### `Email/Pages/VCardManualExport.razor`
- **Route**: `/vcard-export`
- **Features**:
  1. **Top Button Bar**:
     - "Generate vCards" — Primary button (triggers accordion)
     - "Email vCards" — Secondary button (opens modal)
  
  2. **Generate vCards Modal**:
     - Three radio buttons: `Day`, `Tour`, `Single Walker`
     - Date picker (required)
     - Tour label dropdown (only if `Tour` selected)
     - Walker search/dropdown (only if `Single Walker` selected)
     - Submit button
  
  3. **Accordion Display** (after generation):
     - **Accordion Item 1: Day vCards**
       - Shows date, count of vCards
       - List of walkers with copy buttons
     - **Accordion Item 2: Tour vCards**
       - Shows tour name, count of vCards
       - List of walkers with copy buttons
     - **Accordion Item 3: Single Walker**
       - Shows walker name, phone
       - vCard content
       - Copy button
  
  4. **vCard Entry Display**:
     - Walker name, phone, tour, date
     - Formatted vCard text (gray code-like display)
     - Copy button (copies vCard to clipboard)
     - "Copied!" toast notification
  
  5. **Email Modal**:
     - To address field (pre-filled: `jon@cityshuffles.com`, stored in LocalStorage)
     - Subject field (pre-filled: "vCard Export - [Date/Tour]")
     - vCard preview (read-only)
     - Send button
     - Toast notification on success/failure

---

### Updated Files

#### `Email/Components/Layout/NavMenu.razor`
- Add new menu item: **vCard Export**
  - Icon: `bi bi-download`
  - Link: `/vcard-export`

#### `Email/Program.cs`
- Register `VCardManualExportService` as `Scoped`
- Register `SmtpVCardEmailService` as `Scoped`

---

## Configuration

### From `appsettings.json`

```json
"Gmail": {
  "Email": "<SET_IN_LOCAL_SECRETS>",
  "Password": "<SET_IN_LOCAL_SECRETS>",
  "ImapServer": "imap.gmail.com",
  "ImapPort": 993,
  "EnableSsl": true
}
```

**SMTP Server**: Will use Gmail SMTP (`smtp.gmail.com:587` with TLS)

---

## UI Design Notes

### Styling
- Use Bootstrap 5 for layout
- Accordion with `<Accordion>` from Blazor
- Buttons: Primary (Generate), Secondary (Email)
- Code display: `<pre><code>` with monospace font (gray background)
- Copy button: Small icon button with tooltip

### User Flow
1. User clicks "Generate vCards"
2. Modal opens with selection options (Day/Tour/Walker)
3. User selects type and fills required fields
4. Clicks "Generate"
5. Accordion expands with generated vCards
6. User clicks "Copy" on any vCard entry
7. vCard content copied to clipboard (toast confirms)
8. User can manually paste into contact app or email
9. **OR** Click "Email vCards" button
10. Email modal opens with pre-filled recipient
11. Click "Send"
12. Toast confirms email sent

---

## LocalStorage Integration

### Key: `vcard-export-email-address`
- Store last used email address
- Retrieve and populate "To" field on page load
- Update when user sends email

```javascript
// Get
localStorage.getItem('vcard-export-email-address')

// Set
localStorage.setItem('vcard-export-email-address', toAddress)
```

---

## Error Handling

1. **No vCards Found**: Show alert "No vCards found for selection"
2. **SMTP Connection Failure**: Show error "Unable to send email. Check SMTP configuration."
3. **Copy to Clipboard Failure**: Show error "Unable to copy to clipboard"
4. **Empty Selection**: Show validation error "Please select type and date"

---

## Testing Checklist

- [ ] Generate day vCards
- [ ] Generate tour vCards
- [ ] Generate single walker vCard
- [ ] Copy vCard to clipboard
- [ ] Verify vCard format (CRLF line endings, proper fields)
- [ ] Send email with vCards
- [ ] LocalStorage persists email address
- [ ] Modal validation works
- [ ] Toasts display correctly
- [ ] Responsive on mobile

---

## Implementation Order

1. Create `VCardDisplayItem.cs` (simple model)
2. Create `VCardManualExportService.cs` (generation logic)
3. Create `VCardEmailRequest.cs` (email DTO)
4. Create `SmtpVCardEmailService.cs` (SMTP sending)
5. Register services in `Program.cs`
6. Create `VCardManualExport.razor` page
7. Add menu item to `NavMenu.razor`
8. Test all flows

---

## Dependencies Required

- `System.Net.Mail` (SMTP)
- `System.Net.Security` (TLS)
- Existing: `VCardExportService`, `ITourTreeService`

---

## Notes

- **No API endpoints**: All logic runs in Blazor page context
- **No file downloads**: Manual copy/paste approach
- **No complex routing**: Single page handles all modes
- **Configuration-driven**: Uses existing Gmail settings from `appsettings.json`
- **User-controlled**: User decides when to copy/email vCards



