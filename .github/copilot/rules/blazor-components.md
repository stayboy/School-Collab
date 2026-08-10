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

### FluentTextField has no OnClear — the attribute is silently swallowed

`FluentTextField` (and its base `FluentInputBase<T>`) has never exposed an
`OnClear` parameter, and the Microsoft Fluent UI Blazor library has never
shipped a built-in clear (×) button on plain `FluentTextField`. The attribute
is captured by `[Parameter(CaptureUnmatchedValues = true)] AdditionalAttributes`,
so the user-supplied callback **never fires** and the page compiles without
warning. The search-clear side effect must be implemented through
`ValueChanged` (fires with an empty value as soon as the user empties the
field) or by handling the `oninput` event manually.

```razor
@* ✅ correct — ValueChanged already runs when the user empties the textbox *@
<FluentTextField @bind-Value="_searchText"
                 ValueChanged="OnSearchValueChanged"
                 Immediate="true"
                 ImmediateDelay="300" />

@* ❌ wrong — OnClear is a captured-but-inert AdditionalAttribute; the
   supplied callback will never fire. *@
<FluentTextField @bind-Value="_searchText"
                 OnClear="() => OnSearchClear()" />
```

There is a bUnit regression guard at
`tests/SchoolCollab.CodedValues.Tests.Unit/Components/FluentTextFieldOnClearRegressionTests.cs`
that asserts (a) no built-in clear button is rendered and (b) a supplied
`OnClear` does not fire when the field is cleared. If that test fails after a
FluentUI upgrade, audit every search box in the codebase for the now-unnecessary
clear side-effect paths. The same principle applies to **any** parameter not
declared on the FluentUI control you're using: if `intellisense` doesn't show
it, it's a swallowed attribute, not a real callback.

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

6. **Reference implementation:** `src/Settings/SchoolCollab.Settings.Application/Components/Pages/CodedValues/Index.razor`.
   When you create a new read-only list page, copy that file and change only the
   route, the title, the API call, and the columns.

## CancellationTokenSource: loads vs. mutations

A component that only loads data once can use a single `_loadCts` for both the
initial load and any subsequent mutations, disposing it only in `Dispose()`.

When a component **reloads data** (e.g. `OwnerId` or `Code` changes) and also has
user-triggered mutations, use **two separate CTSes**:

- `_loadCts` — recreated per load, cancels stale loads, disposed after each load
  or in `Dispose()`.
- `_mutationCts` — created once for the component lifetime, disposed only in
  `Dispose()`. Mutations pass `MutationToken` (`_mutationCts.Token`).

```csharp
private CancellationTokenSource? _loadCts;
private CancellationTokenSource? _mutationCts = new();
private CancellationToken MutationToken => _mutationCts?.Token ?? CancellationToken.None;

public void Dispose()
{
    _loadCts?.Cancel();
    _loadCts?.Dispose();
    _loadCts = null;
    _mutationCts?.Cancel();
    _mutationCts?.Dispose();
    _mutationCts = null;
}
```

**Why:** `LoadAsync` disposes its CTS when it finishes. If `AddAsync` later
reuses `_loadCts.Token`, it throws `ObjectDisposedException` ("CancellationTokenSource
disposed"). Mutations must never depend on a CTS whose lifetime is shorter than the
mutation itself.

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

### Never put `<style>` blocks in `.razor` markup

Do **not** add `<style>…</style>` blocks inside a `.razor` file. Inline `<style>`
blocks are **unscoped** (global), so they bypass Blazor's CSS isolation and can leak
onto every page — the same problem the `.razor.css` rule exists to prevent.

```razor
@* ❌ wrong — global, unscoped *@
<style>.foo { color: red; }</style>
<div class="foo">…</div>

@* ✅ correct — scoped to this component *@
<div class="foo">…</div>
```

```css
/* In Component.razor.css */
.foo { color: red; }
```

Keep the markup clean: every rule that an inline `<style>` block would hold belongs in
the component's `.razor.css` file. If a component has no `.razor.css` yet, create one
and move the styles there.

### Minimise repeated styles across `.razor` files

Avoid duplicating the same CSS rule across multiple `.razor.css` files. When several
components share an identical style, prefer one of:

- a **shared component/class** that owns the style once (e.g. a shared `FormRow`,
  `SectionCard`, or dialog-shell class), or
- a **shared utility class** in `wwwroot/app.css` (e.g. a layout/flex utility) when
  the style is genuinely generic, or
- keep duplication to a **bare minimum** if the styles are only cosmetically similar
  and may diverge.

Prefer the shared-class option when the shared element is already a component;
prefer `app.css` when it is a layout/utility concern. Repeated styles should be the
exception, not the rule.

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

Only add styles to `wwwroot/app.css` when they are truly application-wide. Keep
this file **small** — it is the one global stylesheet and grows page-load cost.
Put only small, genuine utility classes here (e.g. `.muted`, spacing helpers);
never add page-layout or component-specific styles to it. Those belong in the
owning component's isolated `.razor.css` (CSS isolation is the preferred
mechanism — prefer component-scoped CSS over growing the global sheet):

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

### Destructive actions require a confirmation prompt

Every destructive action (Remove, Delete, Unlink, Deactivate, etc.) MUST be gated by
a user confirmation dialog before it runs. This is enforced at the component level in
`RowActionsMenu` (the kebab used by `SectionCard` `ItemActions` and grid action menus):

- Mark the action destructive with `RowAction.Callback("Remove", ..., destructive: true)`
  (or pass `destructive: true` to the `Action` overload).
- `RowActionsMenu` then shows a modal confirmation prompt via
  `DialogServiceExtensions.ShowConfirmDialogAsync` before invoking the callback, so the
  confirmation is enforced everywhere the kebab is used — no page-specific confirmation
  code needed.
- Optionally pass a custom `confirmMessage` for a specific prompt; otherwise it falls
  back to `"Are you sure you want to {label}?"`.

```razor
RowAction.Callback("Remove", () => RemoveTopicAsync(topic.TopicId), FluentIcons.Delete, destructive: true),
```

The prompt is the reusable `ConfirmDialog` (`src/SchoolCollab.Admin.Shared/Components/Dialogs/`),
shown via `ShowConfirmDialogAsync(message, primaryText, secondaryText, title)`. It renders a
**modal** dialog (dark overlay, `Modal = true`) that is **dismissible by clicking the overlay**
(`PreventDismissOnOverlayClick = false`) or pressing ESC. Do NOT use FluentUI's
`ShowConfirmationAsync`/`ShowMessageBoxAsync` for destructive confirmations — those hide the
dark overlay whenever a secondary (Cancel) button is present.

Do NOT add a second, page-level confirmation dialog for an action already marked
`destructive` — that would double-prompt the user. If an action is destructive, mark it
`destructive: true` and remove any hand-rolled confirmation in the handler.

### Show a success toast after successful mutations

Every successful mutation (delete/remove, create, save, unlink, etc.) SHOULD surface a
success toast via the FluentUI `IToastService`. The `FluentToastProvider` is already in
`MainLayout.razor`, and `AddFluentUIComponents()` registers `IToastService` (scoped) — so
just inject it and call `ShowSuccess` after the API call succeeds:

```razor
@inject IToastService Toast
// ...
await Api.DeleteSubjectAsync(id);
Toast.ShowSuccess($"Subject '{name}' deleted.");
```

Guidelines:
- Inject `IToastService`, NOT the concrete `ToastService` — `AddFluentUIComponents()`
  registers it under the `IToastService` interface, so injecting the concrete type fails.
- Fire the toast ONLY after the API call succeeds (inside the `try`, after the await). Do
  not show success in the `catch` path.
- Include the affected item's name in the message (e.g. `Subject '{name}' deleted.`).
- For display text of a Topic entity, use the term "subject" (per the product's display
  convention) — never "topic" in user-facing strings.

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

### Never open a dialog from another dialog instance

Do **not** open a dialog (`ShowReadonlyDialogAsync` / `ShowShellDialogAsync` /
`ShowDialogAsync`) from inside another dialog component (`IDialogContentComponent`).
When a dialog needs to expose a sub-editor (e.g. a per-row manager inside a list
dialog), use an **in-page section (`<div>`) toggle** that expands/collapses the
sub-editor inline within the same dialog — not a nested dialog. This keeps the
interaction in one dialog surface and avoids stacked overlays.

### Edit form layout with FluentUI

Use FluentUI's own layout components for edit/create forms:

- Put form controls in a `<FluentStack Orientation="Orientation.Vertical" Gap="1rem">`
  when fields should stack vertically. This gives consistent spacing between fields
  without custom flex containers.
- Keep each FluentUI control's label/input relationship intact. Do not add custom
  `margin-bottom` to labels; let the control render its normal label spacing.
- Use `full-width` only for controls that should span the available form width (text
  fields, text areas, selects). Short controls such as dates, numbers, and
  display-order fields should use a narrower width via the **W1–W9 input width
  ladder** (the repo's consistent sizing scale) rather than ad-hoc `Style`
  strings or `narrow-field`:
  - **Repo dropdown wrappers** (`CodedValueDropdown`, `DropdownForEnum`,
    `DropdownComponent`): set the strongly-typed `Width` parameter —
    `Width="FieldWidth.W3"`. The wrapper emits an inline `style` on the
    underlying `<FluentSelect>` that beats its scoped `width:100%` default.
  - **Third-party inputs** (`FluentTextField`, `FluentSelect`,
    `FluentDatePicker`, `FluentNumberField`, `FluentTextArea`): apply the
    matching CSS class — `Class="w-3"`.
  - The ladder lives in `src/SchoolCollab.Admin.Shared/Components/FieldWidth.cs`
    (enum + `ToCssStyle()`) and the `w-1`…`w-9` classes in
    `src/SchoolCollab.Admin/wwwroot/css/app.css`. Both share one pixel
    ladder — keep them in sync. See the `input-width-scale` skill for the
    full step→width→use map and the scoped-CSS specificity gotcha.
  - `narrow-field` and `full-width` remain for legacy call sites; prefer the
    W1–W9 ladder for new code.
- Required labels must include the asterisk in the label text, for example
  `Label="Title *"`, and required fields should use a helper class such as
  `required-field` so the label can be styled bold.

```razor
<FluentEditForm Model="_model" class="wizard-form details-form">
    <FluentStack Orientation="Orientation.Vertical" Gap="1rem" class="details-form-fields">
        <FluentTextField @bind-Value="_model.Title" Label="Title *" class="full-width required-field" Required />
        <FluentTextArea @bind-Value="_model.Description" Label="Description" Rows="4" class="full-width" />
        <FluentDatePicker @bind-Value="_model.DueDate" Label="Due Date" Class="w-3" />
        <FluentNumberField @bind-Value="_model.MaxScore" Label="Max Score" Class="w-4" />
    </FluentStack>
</FluentEditForm>
```

## Share form fields between create & edit forms

When a create form and an edit form render the **same field set** (e.g. a
`TopicCreateDialog` / `TopicEditDialog` pair, or routable `Create.razor` /
`Edit.razor` pages), extract the fields into ONE shared `XxxFormFields.razor`
component and have both forms use it — do NOT copy-paste the rows. This is a
general Blazor pattern, not just a dialog concern.

### The pattern

1. **Shared component renders only the field rows** — no `<EditForm>`, no
   `<DataAnnotationsValidator>`, no submit/cancel buttons. The owning form
   supplies those (it already owns the submit plumbing).
2. **Define a small interface** (e.g. `ITopicFormModel`) with the shared
   properties (`Name`, `Code`, `Description`, `DisplayOrder`). Both the create
   and edit models implement it, so the shared component can `@bind-Value` to
   either without a common base class.
3. **The shared component takes `[Parameter] IXxxFormModel Model`** and binds
   the rows to it. Add optional display parameters (e.g. `CodePlaceholder`) for
   create-vs-edit wording differences.
4. **Both forms** replace their inline rows with
   `<XxxFormFields Model="Model" />` inside their `<EditForm>`.
5. **Form-specific extras stay in the owning form**, below the shared fields
   (e.g. an edit-only strands editor, or a create-only grade picker).

### Example (this repo)

- `src/Students/SchoolCollab.Students.Application/Components/Students/TopicFormFields.razor`
  — shared rows + `ITopicFormModel`.
- `TopicCreateDialog.razor` / `TopicEditDialog.razor` — both models implement
  `TopicFormFields.ITopicFormModel` and render `<TopicFormFields Model="Model" />`.
- `GradeLevelFormFields.razor` — same idea for the routable
  `GradeLevels/Create.razor` / `GradeLevels/Edit.razor` pages (owns the
  `<EditForm>` + validator + action row).

### Checklist

- [ ] Shared fields live in ONE `XxxFormFields.razor`; no duplicated rows.
- [ ] Create & edit models implement a shared `IXxxFormModel` interface.
- [ ] The shared component binds to the interface, not a concrete model.
- [ ] Form-specific extras stay in the owning form, below the shared fields.
- [ ] Build passes; both forms render the same fields.

## SectionCard (grade-detail section cards)

The rules for the shared `SectionCard` component (kebab actions, page message
alerts for error state, reload-after-mutation `StateHasChanged()`, shared-form-fields
dialogs, no-nested-dialogs / in-page section toggle, topic+role assignment dates)
live in **`.github/copilot/rules/section-card.md`** — see that file. (Split out to
keep this file from overflowing; follow the same split-file pattern for other
component-specific rule sets.)
