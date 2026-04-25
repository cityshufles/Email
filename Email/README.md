# Email Project Architecture Guide

This document is the quick-start architecture guide for building new features in the `Email` project.

## Tech stack

- ASP.NET Core `.NET 8` web app
- Blazor Server UI (Razor components)
- Syncfusion + Bootstrap styling
- Services + SQL-backed data access in `Services/`

## Top-level structure

```text
Email/
  Program.cs
  App.razor
  Components/
  Controllers/
  Models/
  Services/
  Data/
  Calendar/
  Hubs/
  wwwroot/
    app.css
    css/
      tour-dashboard.css
      sf-grid-overrides.css
      emailModHighlighter.css
    js/
```

## UI architecture (how to build features)

- Pages live in `Components/Pages/` (and subfolders like `Admin/`, `Staff/`, `Public/`).
- Reusable feature UI lives in domain folders under `Components/` (example: `Components/Messages/`, `Components/Tours/`).
- Layout and navigation live in:
  - `Components/Layout/MainLayout.razor`
  - `Components/Layout/NavMenu.razor`

### Required pattern for new features

1. Create a page (`@page`) in `Components/Pages/...`.
2. Create one or more reusable components in `Components/<FeatureName>/...`.
3. Use **code-behind for C# logic**:
   - Keep markup in `Component.razor`
   - Keep logic in `Component.razor.cs` (partial class)
4. Put feature-specific style in `Component.razor.css` when possible.
5. Use shared/global classes from existing CSS before adding new styles.

## Code-behind standard

For new work, default to this shape:

```text
Components/
  Pages/
    FeaturePage.razor
  FeatureX/
    FeatureXPanel.razor
    FeatureXPanel.razor.cs
    FeatureXPanel.razor.css
```

Guideline:

- `.razor`: HTML/UI composition only.
- `.razor.cs`: data loading, event handlers, service calls, state, validation.
- Keep page files thin; move complex logic into feature components/services.

## Styling standard (important)

Use existing project style classes/patterns first, especially from:

- `wwwroot/app.css`
- `wwwroot/css/tour-dashboard.css`
- `wwwroot/css/sf-grid-overrides.css`
- `wwwroot/css/emailModHighlighter.css`

Practical rules:

1. Reuse existing classes and Bootstrap utility classes where possible.
2. Avoid inline styles unless there is no reasonable alternative.
3. If new class names are needed, keep them feature-scoped and readable.
4. Put global/shared style in `wwwroot/app.css` or `wwwroot/css/*.css`.
5. Put component-only style in `Component.razor.css`.

## Routing and nav updates

When adding a new page:

1. Add `@page "/your-route"` to the page component.
2. Add a `NavLink` entry in `Components/Layout/NavMenu.razor` if it should be visible in sidebar nav.
3. Keep route names short and consistent with existing pages.

## Service and model placement

- API/data orchestration: `Services/`
- DTO/domain models: `Models/`
- Controller endpoints (if needed): `Controllers/`
- Keep business logic out of UI files whenever possible.

## Feature delivery checklist

1. Page created under `Components/Pages/...`
2. Reusable component(s) created under `Components/<Feature>/...`
3. C# logic moved to `.razor.cs` code-behind
4. Existing CSS classes reused first, then minimal new styles added
5. Nav route added (if needed)
6. Build passes

```powershell
dotnet build .\Email\Email.csproj
```
