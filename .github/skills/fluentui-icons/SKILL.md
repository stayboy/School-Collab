---
name: fluentui-icons
description: |
  Fluent UI Blazor icon usage patterns for Microsoft.FluentUI.AspNetCore.Components 4.x.
  Triggers: "FluentIcon", "icon in Blazor", "add icon", "icon parser error",
  "CS0305 icon", "Icons.Regular", "Icons.Filled", "FluentUI icon not rendering",
  "how to use icons in FluentUI Blazor".
---

# Fluent UI Blazor Icons

## Packages Required

```xml
<!-- In .csproj (version managed by Directory.Packages.props under CPM) -->
<PackageReference Include="Microsoft.FluentUI.AspNetCore.Components" />
<PackageReference Include="Microsoft.FluentUI.AspNetCore.Components.Icons" />
```

Both packages are required. The icons package provides the `Icons` static classes.

## Repository Rule

Use package-native icon patterns and keep reusable `Icon` instances in shared constants:

- Standalone `<FluentIcon>` is generic. Pass the icon type through the `Icon` parameter.
- Fluent components that expose `Icon`, `IconStart`, `IconEnd`, `IconPrevious`,
  `IconCurrent`, or `IconNext` expect an `Icon` instance. Prefer shared constants
  from `SchoolCollab.Admin.Shared.Constants.FluentIcons`.
- Do not use `Icon.FromType<T>()` in Razor markup. It is unnecessary for this
  package version and is not the repo pattern.
- Do not declare page-local `static readonly Icon` fields when a shared constant
  exists. Add missing reusable icons to
  `src/SchoolCollab.Admin.Shared/Constants/FluentIcons.cs`.

## Standalone `<FluentIcon>`

`<FluentIcon>` is generic in Microsoft.FluentUI.AspNetCore.Components 4.x, so pass
the icon type directly:

```razor
@using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons

<FluentIcon Icon="@(Icons.Regular.Size24.Document)" />
<FluentIcon Icon="@(Icons.Filled.Size20.Delete)" Color="@Color.Error" />
<FluentIcon Icon="@(Icons.Regular.Size24.CheckmarkCircle)" Width="16" Height="16" />
```

For `Icon` **instances** — shared constants and custom images alike — use the
non-generic `Value` property:

```razor
<FluentIcon Value="@(Icon.FromImageUrl("/images/custom-icon.png"))" />
```

## Icon Parameters on Fluent Components

Components such as `FluentButton`, `FluentWizardStep`, and `FluentNavLink` expect an
`Icon` instance for their icon parameters. Prefer shared constants:

```razor
<FluentButton Appearance="Appearance.Accent" IconStart="@FluentIcons.Add">
    Create New
</FluentButton>

<FluentWizardStep IconPrevious="@FluentIcons.CheckmarkCircleFilled" />
```

If a reusable icon is needed, add it to `FluentIcons`:

```csharp
using Microsoft.FluentUI.AspNetCore.Components;

namespace SchoolCollab.Admin.Shared.Constants;

public static class FluentIcons
{
    public static readonly Icon Add =
        new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.Add();
}
```

## Naming Convention

Pattern: `Icons.[Variant].[Size].[Name]`

| Variant   | Use for                        |
|-----------|--------------------------------|
| `Regular` | Default/outline icons (most common) |
| `Filled`  | Filled/solid icons for emphasis |

Available sizes: `Size12`, `Size16`, `Size20`, `Size24`, `Size28`, `Size32`, `Size48`.

Common icons: `Save`, `Delete`, `Search`, `Add`, `AddCircle`, `Home`, `Edit`,
`Dismiss`, `Checkmark`, `CheckmarkCircle`, `Warning`, `Error`, `Info`,
`ArrowDownload`, `ArrowExportUp`, `TextBulletList`, `Tag`, `Star`, `Settings`,
`Mail`, `Calendar`, `People`, `Document`, `Folder`.

## Troubleshooting

| Problem | Cause | Fix |
|---------|-------|-----|
| CS0305 / CS0019 on `<FluentIcon>` | Generic icon type used in a non-generic parameter | Use `<FluentIcon Icon="@(Icons.Regular.Size24.Name)" />` or an `Icon` instance for `Value` |
| Icon shows blank | Missing `Microsoft.FluentUI.AspNetCore.Components.Icons` package | Add the Icons NuGet package |
| Icon not found at design time | Missing `@using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons` | Add the alias to `_Imports.razor` |
| `@using` alias conflicts | Multiple `Icons` namespaces | Use the explicit `@using Icons = ...` alias form |
| Render-time throw `"Please use the constructor including parameters."` | An `Icon` **instance** passed to the generic `Icon=` parameter — it infers the abstract `Icon` base, whose parameterless ctor throws at render time | Instances go through `Value=`; types through `Icon=`. Full trap + diagnosis below |

## The instance trap — a mystery "timeout" in bUnit

`<FluentIcon>` is generic: `Icon=` wants the generated icon **type**
(`<FluentIcon Icon="@Icons.Regular.Size20.Save" />`). Passing an **instance** to
`Icon=` makes Blazor infer the abstract `Icon` base, whose parameterless
constructor throws `ArgumentNullException: "Please use the constructor including
parameters."` — at **render time**, inside the component tree:

- The page's `<ErrorBoundary>` (the repo rule) swallows it into the fallback UI,
  so the app shows "Something went wrong" while the markup still looks fine in
  review.
- In bUnit it reads as a **flaky timeout**: `WaitForFailedException` ("the
  assertion did not pass within the timeout period"). The signature: the
  affected tests **pass in isolation and fail together** under full-suite load
  (7 at once in the assignment-authoring round, 2026-10-08) — the crash kills
  the render pipeline mid-`WaitForAssertion`.

Rule: instances always go through `Value=` (`<FluentIcon Value="@FluentIcons.Add" />`
— the `DashboardCard` / `RowActionsMenu` / `HelpIcon` precedent); types through
`Icon=`.

Diagnosis recipe when a page suddenly renders its ErrorBoundary fallback:

1. Temporarily widen the page's `ErrorContent` from `@ex.Message` to `@ex.ToString()`.
2. Re-run the single failing test and read the INNER exception in the rendered fallback.
3. Revert the widening — never commit it. Workflow:
   `.github/copilot/rules/testing.md` §"bUnit pitfalls".

## Recommended `_Imports.razor`

```razor
@using Microsoft.FluentUI.AspNetCore.Components
@using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons
@using SchoolCollab.Admin.Shared.Constants
```

This makes Fluent components, the `Icons` alias, and shared `FluentIcons` constants
available to Razor components.
