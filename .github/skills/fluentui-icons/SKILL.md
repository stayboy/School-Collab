---
name: fluentui-icons
description: |
  Fluent UI Blazor icon usage patterns and Razor parser gotchas.
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

Both packages are required. The icons package provides the `Icons` static class.

## Critical: Two Ways to Reference Icons

### ✅ Pattern A — `Value` property with static `Icons` class (preferred)

Use this for **standalone `<FluentIcon>` components**:

```razor
@using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons

<FluentIcon Value="@(Icons.Regular.Size24.Save)" />
<FluentIcon Value="@(Icons.Filled.Size20.Delete)" Color="@Color.Error" />
<FluentIcon Value="@(Icons.Regular.Size16.Search)" />
```

**This is the only safe pattern for `<FluentIcon>`.**

### ✅ Pattern B — `Icon.FromType<T>()` on component parameters

Use this for **`Icon` parameters on other Fluent components** (`FluentButton`, `FluentNavLink`, etc.):

```razor
<FluentNavLink Icon="@(Icon.FromType<Icons.Regular.Size20.Home>())"
               Href="" Match="NavLinkMatch.All">
    Home
</FluentNavLink>

<FluentButton Icon="@(Icon.FromType<Icons.Regular.Size20.AddCircle>())"
              Appearance="Appearance.Accent">
    Add
</FluentButton>
```

This pattern works on component parameters because the generic syntax is inside a quoted attribute value, not a standalone component tag.

## ❌ Never Do This — Razor Parser Error

```razor
@* WRONG — causes CS0305/CS0019 compiler errors *@
@* The Razor parser sees <Icons.Regular.Size24.Star>() as HTML elements *@
<FluentIcon Icon="@(Icon.FromType<Icons.Regular.Size24.Star>())" />
```

The `>()/>` suffix after a generic type argument confuses the Razor parser into treating the closing `/>` as HTML tag closers, producing errors like:

- `CS0305: Using the generic type 'Icons' requires 1 type arguments`
- `CS0019: Operator '>' cannot be applied to operands of type...`

## Icon Naming Convention

Pattern: `Icons.[Variant].[Size].[Name]`

| Variant   | Use for                        |
|-----------|--------------------------------|
| `Regular` | Default/outline icons (most common) |
| `Filled`  | Filled/solid icons for emphasis |

Available sizes: `Size12`, `Size16`, `Size20`, `Size24`, `Size28`, `Size32`, `Size48`

Common icons: `Save`, `Delete`, `Search`, `Add`, `AddCircle`, `Home`, `Edit`, `Dismiss`, `Checkmark`, `Warning`, `Error`, `Info`, `ArrowDownload`, `ArrowExportUp`, `TextBulletList`, `Tag`, `Star`, `Settings`, `Mail`, `Calendar`, `People`, `Document`, `Folder`

## Common Patterns

### Standalone icon with color

```razor
<FluentIcon Value="@(Icons.Regular.Size20.Warning)" Color="@Color.Warning" />
<FluentIcon Value="@(Icons.Filled.Size24.Delete)" Color="@Color.Error" />
```

### Icon in a button

```razor
<FluentButton Appearance="Appearance.Accent"
              Icon="@(Icon.FromType<Icons.Regular.Size20.AddCircle>())">
    Create New
</FluentButton>
```

### Icon-only button (no text)

```razor
<FluentButton Icon="@(Icon.FromType<Icons.Regular.Size20.Delete>())"
              Appearance="Appearance.Outline"
              aria-label="Delete" />
```

### Custom image icon

```razor
<FluentIcon Value="@(Icon.FromImageUrl("/images/custom-icon.png"))" />
```

### Icon size override

```razor
<FluentIcon Value="@(Icons.Regular.Size24.Save)" Width="16" Height="16" />
```

## Troubleshooting

| Problem | Cause | Fix |
|---------|-------|-----|
| CS0305 / CS0019 on `<FluentIcon>` | `Icon.FromType<T>()` used on `<FluentIcon>` | Switch to `Value="@(Icons.Regular.Size20.Name)"` |
| Icon shows blank | Missing `Microsoft.FluentUI.AspNetCore.Components.Icons` package | Add the Icons NuGet package |
| Icon not found at design time | Missing `@using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons` | Add the alias to `_Imports.razor` |
| `@using` alias conflicts | Multiple `Icons` namespaces | Use the explicit `@using Icons = ...` alias form |

## Recommended `_Imports.razor`

```razor
@using Microsoft.FluentUI.AspNetCore.Components
@using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons
```

This makes all Fluent components available and the `Icons` alias shorthand for icon references.