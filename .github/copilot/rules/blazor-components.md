# Blazor Components and Styling

This file contains topic-specific guidance for Blazor pages, Fluent UI usage, and
component-scoped styling. Read it before changing Razor components, admin pages,
layouts, dialogs, or Fluent UI markup.

## Render mode and pre-rendering

The render mode is set **once** for the entire app on `<Routes>` and `<HeadOutlet>` in
`Components/App.razor`. Pages **must not** declare `@rendermode` themselves — they
inherit the global default.

```razor
@* ✅ correct — declared once in App.razor, inherited by every page *@
<HeadOutlet @rendermode="new InteractiveServerRenderMode(prerender: false)" />
<Routes @rendermode="new InteractiveServerRenderMode(prerender: false)" />

@* ❌ wrong — per-page rendermode drifts and re-enables prerendering *@
@rendermode InteractiveServer
```

The default for this project is **Interactive Server with pre-rendering disabled**:

- **Pre-rendering is off at the app level** so `OnInitializedAsync` runs exactly once
  (no duplicate API calls, no duplicate component-tree construction).
- The list-bearing landing page relies on `OnInitializedAsync` to populate the first
  render directly, with `<FluentProgressRing />` as the loading state.

If a single page genuinely needs a different mode, declare `@rendermode` on **that page
or instance only** and add a comment explaining why.

## Parallel data loading

Never `await` independent API calls sequentially. Use `Task.WhenAll` to fire them in
parallel:

```csharp
// ❌ wrong — serial, pays full latency twice
_parent   = await Api.GetByIdAsync(ParentId);
_children = await Api.GetChildrenAsync(ParentId);

// ✅ correct — parallel, pays full latency once
var parentTask   = Api.GetByIdAsync(ParentId);
var childrenTask = Api.GetChildrenAsync(ParentId);
await Task.WhenAll(parentTask, childrenTask);
_parent   = parentTask.Result;
_children = childrenTask.Result;
```

## Loading states

Use `<FluentProgressRing />` consistently across **all** pages — never use bare
`<p><em>Loading…</em></p>`. Every page must show a spinner while `OnInitializedAsync`
is pending:

```razor
@if (_loading)
{
    <FluentProgressRing />
}
else if (_items is { Length: 0 })
{
    <FluentMessageBar Intent="MessageIntent.Info">No items yet.</FluentMessageBar>
}
```

## Error boundaries

Wrap the primary content of every page in `<ErrorBoundary>` so unhandled exceptions
show a recovery UI instead of crashing the whole layout:

```razor
<ErrorBoundary>
    <ChildContent>
        @* main page content *@
    </ChildContent>
    <ErrorContent Context="ex">
        <FluentMessageBar Intent="MessageIntent.Error">
            Something went wrong: @ex.Message
        </FluentMessageBar>
    </ErrorContent>
</ErrorBoundary>
```

## `@key` on dynamic lists

Any `@foreach` that renders child components or elements **must** include `@key` so
Blazor's diffing algorithm can reuse existing DOM nodes instead of re-creating them:

```razor
@* ✅ correct *@
@foreach (var attr in context.Attributes)
{
    <FluentBadge @key="attr.Key">@attr.Key=@attr.Value</FluentBadge>
}

@* ❌ wrong — no @key, causes unnecessary DOM teardown *@
@foreach (var attr in context.Attributes)
{
    <FluentBadge>@attr.Key=@attr.Value</FluentBadge>
}
```

## Component parameters

- Use `[Parameter, EditorRequired]` for required parameters — IDE warns if omitted.
- Use `EventCallback<T>` (not `Action<T>`) for child-to-parent events — async-safe and
  automatically calls `StateHasChanged`.
- Implement `IAsyncDisposable` to clean up timers, subscriptions, and JS module
  references.

## Fluent UI component usage

Do **not** mix Bootstrap HTML elements with FluentUI. Replace every Bootstrap element:

| Bootstrap | FluentUI replacement |
|---|---|
| `<input class="form-control">` | `<FluentTextField>` |
| `<textarea class="form-control">` | `<FluentTextArea>` |
| `<input type="number">` | `<FluentNumberField>` |
| `<button class="btn btn-primary">` | `<FluentButton Appearance="Appearance.Accent">` |
| `<button class="btn btn-secondary">` | `<FluentButton Appearance="Appearance.Outline">` |
| `<div class="alert alert-danger">` | `<FluentMessageBar Intent="MessageIntent.Error">` |

For icon usage, follow `.github/skills/fluentui-icons/SKILL.md`.

For FluentUI component property enum validation, follow
`.github/skills/fluentui-component-props/SKILL.md`.

## ShouldRender optimisation

Override `ShouldRender()` on components that receive frequent external events but only
need to update their DOM when their own data changes:

```csharp
private bool _dataChanged;

protected override bool ShouldRender()
{
    if (!_dataChanged) return false;
    _dataChanged = false;
    return true;
}
```

## Landing-page performance pattern

The coded-values landing page (`Components/Pages/CodedValues/Index.razor`) is the
**canonical example** for read-only public pages. Every new read-only list/detail page
**must** follow the same pattern:

1. **Interactive Server with pre-render disabled — the app's default.** The list page
   has event handlers (`OnClick` for navigation, `OnToggleAsync` for Enable/Disable)
   that require an interactive circuit. It inherits the global render mode from
   `App.razor` and does **not** declare its own `@rendermode`. Do not add
   `[StreamRendering(true)]` or a per-page `@rendermode` to list pages.

2. **Optimistic UI mutations, not full re-fetch.** When a row action changes a single
   field (Enable/Disable, Toggle, Increment), mutate the in-memory DTO with `with`,
   call `StateHasChanged()`, dispatch the API call in the background, and roll back on
   failure. **Never call `LoadAsync()` after a single-row mutation** — that re-serialises
   the entire list and resets the user's sort/scroll state.

   ```csharp
   private async Task OnToggleAsync(Guid id, bool disable)
   {
       if (_items is null) return;
       var idx = Array.FindIndex(_items, x => x.Id == id);
       if (idx < 0) return;
       var previous = _items[idx];
       _items[idx] = previous with { IsDisabled = disable };
       StateHasChanged();
       try { await (disable ? Api.DisableAsync(id) : Api.EnableAsync(id)); }
       catch (Exception ex)
       {
           Logger.LogError(ex, "Failed to toggle coded value {Id}", id);
           var i = Array.FindIndex(_items, x => x.Id == id);
           if (i >= 0) _items[i] = previous;   // rollback
           _error = ex.Message;
           StateHasChanged();
       }
   }
   ```

3. **Always implement `IDisposable` and pass a `CancellationToken`.** Every page that
   loads data in `OnInitializedAsync` must own a `CancellationTokenSource`, pass its
   token into every `Api.*Async(ct)` call, and dispose the CTS in `Dispose()`. This
   aborts the in-flight HTTP request when the user navigates away and prevents setting
   state on an unmounted component.

   ```csharp
   @implements IDisposable

   @code {
       private CancellationTokenSource? _loadCts;
       private volatile bool _disposed;

       protected override async Task OnInitializedAsync()
       {
           _loadCts = new CancellationTokenSource();
           try
           {
               _items = await Api.GetRootValuesAsync(_loadCts.Token);
               if (_disposed) return;   // <-- ALWAYS guard post-await state writes
               Logger.LogInformation("Loaded {Count}", _items?.Length ?? 0);
           }
           catch (OperationCanceledException) { /* user navigated away */ }
           catch (Exception ex)
           {
               if (_disposed) return;   // <-- and again on the error path
               _error = ex.Message;
           }
       }

       public void Dispose()
       {
           _disposed = true;            // <-- set this FIRST so the continuation
                                        //     sees it even if Cancel() races
           _loadCts?.Cancel();
           _loadCts?.Dispose();
           _loadCts = null;
       }
   }
   ```

4. **Use the "items null" pattern for loading state**, not a `_loading` bool. The
   streaming-rendering placeholder is shown automatically while `_items is null`.
   The `else if (_items.Length == 0)` branch handles the empty case.

   ```razor
   @if (_items is null)         { <FluentProgressRing /> }
   else if (_items.Length == 0) { <FluentMessageBar>No items yet.</FluentMessageBar> }
   else                         { <FluentDataGrid ... /> }
   ```

5. **Keep the slim payload on the landing endpoint.** The landing page never renders
   `Attributes` — don't transfer them. If the current DTO includes
   `IReadOnlyCollection<CodedValueAttributeDto>`, add a `CodedValueSummaryDto`
   projection to the API and a matching `GetRootSummariesAsync()` on the client.
   (Pending implementation — tracked in `lp-slim-dto` todo.)

6. **Reference implementation:** `src/CodedValues/SchoolCollab.CodedValues.Admin/Components/Pages/CodedValues/Index.razor`.
   When you create a new read-only list page, copy that file and change only the
   route, the title, the API call, and the columns.

## Blazor CSS isolation and styling

### Prefer Blazor CSS isolation over global CSS

Every Razor component (pages, layouts, dialogs) **must** have a companion `.razor.css`
file for component-scoped styles. Never add page-specific styles to `wwwroot/app.css`.

```text
Components/Pages/CodedValues/Index.razor      ← markup
Components/Pages/CodedValues/Index.razor.css  ← scoped styles
```

**Why?** Blazor CSS isolation generates unique scope identifiers (e.g. `b-xyz`) so
styles in `Index.razor.css` only apply to `Index.razor` — no class-name collisions,
no specificity wars, and no dead CSS when a page is removed.

### Prefer CSS classes over inline `style`

Never use `style="…"` on HTML elements in `.razor` files. Extract the styles into a
named class in the component's `.razor.css` file:

```razor
@* ❌ wrong *@
<div style="flex:1; min-height:0; overflow:auto;">

@* ✅ correct *@
<div class="grid-container">
```

```css
/* In Component.razor.css */
.grid-container {
    flex: 1;
    min-height: 0;
    overflow: auto;
}
```

The only exceptions are truly one-off dynamic values computed in C# (e.g. a `width` or
`color` derived from data) — even then, prefer a CSS custom property set via
`style="--my-value: @value"` and reference it in the isolated CSS.

### Use `::deep` for child-component and web-component selectors

Blazor CSS isolation scopes selectors to the *component's own* elements. To target
elements rendered by child components (including FluentUI web components like
`<fluent-data-grid>`), use `::deep`:

```css
/* In Index.razor.css — targets cells inside FluentDataGrid */
::deep .grid-sticky-cols td[col-index="1"] {
    position: sticky;
    left: 0;
}
```

Without `::deep`, the scope attribute would be placed on the `td` itself, which does
not exist in the component's direct markup.

### Global styles in `wwwroot/app.css`

Only add styles to `wwwroot/app.css` when they are truly application-wide:

| Keep in `app.css` (global) | Move to `.razor.css` (isolated) |
|---|---|
| `html, body` resets | Page layout (flex containers, grid wrappers) |
| Theme / colour variables | Toolbar actions, button bars |
| `fluent-data-grid` global parts | Spinner / loading containers |
| `.grid-sticky-cols` (shared grid pattern) | Form field layouts |
| `.brand*` | Dialog / chat bubble styles |
| Responsive breakpoints for `.brand*` | Any style used by exactly one component |

The global `app.css` already styles bare `<a>` and `fluent-anchor` as accent-coloured
inline links with hover underlines (see the `a, .btn-link, fluent-anchor` rule
below). Don't reintroduce `.link-action` or similar wrappers — use `class="ms-2"` for
spacing only.

If a style is used by two or more components, consider whether it should be a shared
CSS class in `app.css` (e.g. utility classes) or duplicated in each isolated file
(preferred if the styles may diverge).

### FluentUI components — use component parameters, not CSS

Prefer FluentUI's built-in parameters over custom CSS for spacing, alignment, and
layout on `<Fluent*>` components:

| Instead of | Use |
|---|---|
| `style="width:100%"` on `<FluentTextField>` | `Style="width:100%"` parameter (still inline) or a class in isolated CSS |
| `style="margin:0"` on `<h1>` | A class like `.page-title { margin: 0; }` in isolated CSS |

When FluentUI provides a parameter (e.g. `Appearance`, `Gap`, `Orientation`), use it
instead of replicating the same effect in CSS.

### FluentAnchor patterns in grids

`<FluentAnchor>` inside a `<FluentDataGrid>` falls into two roles, each with its own
convention:

| Role | Example columns | Markup |
|---|---|---|
| Identity / navigation link (the row's primary identifier — Name, Title, Code) | `<TemplateColumn Title="Name">`, `<TemplateColumn Title="Title">` | `<FluentAnchor Appearance="Appearance.Hypertext" Href="...">...</FluentAnchor>` |
| Action verb (Enable, Disable, Edit, Delete, Recover) | `<TemplateColumn Title="Actions">` | `<FluentAnchor Href="..." OnClick="...">Enable</FluentAnchor>` |

**Identity / navigation link**: use `Appearance="Appearance.Hypertext"` so the link is
visually unambiguous as the row's primary navigable target (accent colour, underlined).

**Action verb**: do **not** set `Appearance` — leave it at FluentUI's default so it
inherits the global `fluent-anchor` styling from `app.css` (accent-coloured inline
text, underline on hover). Do not add custom CSS classes such as `.link-action`,
`.delete-link`, or `.cancel-link`; the browser's default `cursor: pointer` on `<a>`
is enough.

For destructive actions, do not colour the link red — the confirmation dialog already
gates the action, and the label text (`Delete`) conveys the semantics.

### Side-panel drawers (`FluentDialog` + `DialogType.Panel`)

FluentUI Blazor has no standalone `Drawer` component. Use `FluentDialog` with
`DialogType.Panel` instead — that's its purpose-built side-drawer primitive.
There is a project precedent in `CodedValuesChatPanel.razor` (the AI assistant
drawer on the coded-values landing page).

API:

```razor
@inject IDialogService DialogService

<FluentButton OnClick="OpenDrawerAsync">Open panel</FluentButton>

@code {
    private async Task OpenDrawerAsync()
    {
        await DialogService.ShowDialogAsync<MyDrawer, MyDrawerData>(
            new MyDrawerData(...),
            new DialogParameters
            {
                Title = "My Panel",
                DialogType = DialogType.Panel,                 // <-- drawer mode
                Alignment = HorizontalAlignment.Right,         // Left | Right | Center
                Width = "420px",                               // panel width
                ShowDismiss = true,                            // show × button
                Modal = false,                                 // let user still click page behind
            });
    }
}
```

For the dialog body to be accepted by `ShowDialogAsync<TDialog, TData>`, the
component must implement `IDialogContentComponent<TData>`:

```razor
@implements IDialogContentComponent<MyDrawerData>
@code {
    [Parameter] public MyDrawerData Content { get; set; } = default!;
}
```

For the common pattern of a chat-style panel with **scrollable content above +
pinned input bar below**, compose two children in a flex column. Don't use
`position: absolute` for the input — make it a flex item with `flex: 0 0 auto`
so it participates in layout and never overlaps content.

### Mirroring a page-side component into a drawer

When the drawer needs to show the **same live state** as a component on the
hosting page (e.g. an inline chat → side-drawer chat), `@ref` does not work —
the drawer content lives in a separate render tree behind
`DialogService.ShowDialogAsync`. Use a **scoped service** as a pub/sub bridge.
Project precedent: `CodedValuesChatHub` (scoped), injected by the inline
`<CodedValuesChat>` on the landing page and by `CodedValuesChatPanel` inside
the drawer. The inline chat emits `OnMessageAdded` events; the page handler
appends to the hub; the drawer's display chat reads from the hub via its
`ExternalMessages` parameter and re-renders when the hub's `Changed` event
fires.

### Edit form layout with FluentUI

Use FluentUI's own layout components for edit/create forms:

- Put form controls in a `<FluentStack Orientation="Orientation.Vertical" Gap="1rem">`
  when fields should stack vertically. This gives consistent spacing between fields
  without custom flex containers.
- Keep each FluentUI control's label/input relationship intact. Do not add custom
  `margin-bottom` to labels; let the control render its normal label spacing.
- Use `full-width` only for controls that should span the available form width (text
  fields, text areas, selects). Short controls such as dates, numbers, and
  display-order fields should use a narrower width, either with the component `Style`
  parameter or a small helper class such as `narrow-field`.
- Required labels must include the asterisk in the label text, for example
  `Label="Title *"`, and required fields should use a helper class such as
  `required-field` so the label can be styled bold.

```razor
<FluentEditForm Model="_model" class="wizard-form details-form">
    <FluentStack Orientation="Orientation.Vertical" Gap="1rem" class="details-form-fields">
        <FluentTextField @bind-Value="_model.Title" Label="Title *" class="full-width required-field" Required />
        <FluentTextArea @bind-Value="_model.Description" Label="Description" Rows="4" class="full-width" />
        <FluentDatePicker @bind-Value="_model.DueDate" Label="Due Date" Style="width: 12rem;" />
        <FluentNumberField @bind-Value="_model.MaxScore" Label="Max Score" Style="width: 12rem;" />
    </FluentStack>
</FluentEditForm>
```
