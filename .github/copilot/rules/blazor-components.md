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

`@key` is **required** on a `@foreach`/`@for` whose collection can reorder, insert in the
middle, or filter, and on any loop rendering **components that own state** (an editor row, a
bound input, anything held by `@ref`) — there Blazor must match nodes to items, and without
a key it diffs by position, tearing down and re-creating DOM and losing that state:

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

**Exempt: append-only, purely presentational rows** — a list that is only appended to, whose
rows are plain markup (a label, an action button) and hold no state of their own. Positional
diffing is correct there, and a key can be *worse* than none: a value key that can legitimately
repeat (the user adds the same entity twice) throws `InvalidOperationException` at render.
When you rely on this exemption, **say so in a comment** on the loop, so the missing `@key`
does not read as an oversight.

*(Owner, 2026-10-17: narrowed from an unconditional "must" after a review measured 66 of 80
loops in `src/**/*.razor` unkeyed — a rule most of the codebase ignores weakens every rule
beside it.)*

## Component parameters

- Use `[Parameter, EditorRequired]` for required parameters — IDE warns if omitted.
- Use `EventCallback<T>` (not `Action<T>`) for child-to-parent events — async-safe and
  automatically calls `StateHasChanged`.
- A method invoked **directly** — a bUnit test seam, or a parent calling through `@ref` — does **not**
  re-render the component: only event handlers do. End every public mutating method with an explicit
  `StateHasChanged()` so the same method behaves identically from an event, a test, and a parent call.
  (Learned the hard way: a failure line that never appeared in bUnit because the seam set the field and
  nothing re-rendered.)
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

## Removed FluentUI *parameters* are silent — sweep attributes, not just components

A v4 → v5 component **rename or removal** at least announces itself: an unknown *tag* renders as
an inert HTML element and Razor emits `RZ10012`. An unknown **attribute** on a component that has
`AdditionalAttributes` is captured **silently** — no warning, no error, and the thing it configured
simply does nothing. The 4.14.2 → 5.0.1 upgrade shipped 26 such regressions
(`FluentTextArea.Rows` ~16, `FluentMenu.Anchor`/`UseMenuService`, `FluentBadge.DismissTitle`/
`OnDismissClick`, `FluentCard.AreaRestricted` 5, `FluentDivider.Role` 2,
`FluentInputFile.OnFileCountExceeded`) that a green build **and** a green test suite could not see.

When the library version moves, sweep **attributes** as well as tags:

1. Enumerate the `[Parameter]` properties per component from the **installed** package's XML
   (`~/.nuget/packages/microsoft.fluentui.aspnetcore.components/<version>/lib/net10.0/*.xml`,
   members are `P:Microsoft.FluentUI.AspNetCore.Components.<Component>.<Property>`), resolving the
   inheritance chain — generic bases such as `FluentTextArea : FluentInputImmediateBase<string>` and
   the external `InputBase<TValue>` properties (`Value`, `ValueChanged`, `EditContext`, …) are
   legitimate and must not be reported as unknown.
2. Grep the markup for every attribute passed to those components and diff it against that list.
   Match **case-insensitively** (Blazor binds case-insensitively, so `Autofocus` is not a mismatch
   for `AutoFocus`). Ignore `class`, `style`, `id`, `aria-*`, `data-*`, `slot`, `title`, `@on*`,
   `@bind*`, `ChildContent`.
3. For each mismatch, check the **previous** version's XML for the same property. Present there →
   **this upgrade broke it**; absent there → long-standing and out of scope. That one fact is what
   separates a regression from a pre-existing bug, and it belongs in the report either way.
4. Record every confirmed case in the round doc (`file:line`, element, attribute, and the v5 name
   where one exists) **before** fixing anything.

Where v5 has **no** equivalent (badge dismissal, for example), author the affordance or report it as
a named finding — never silently drop a working control.

## `FluentProviders` is a sibling, never a wrapper — and a green suite does not prove the app boots

`FluentProviders` (v5) renders **only** its own provider set — toast, dialog, tooltip and key-code —
inside a `div`. It declares no `ChildContent` parameter, so any markup placed **inside** it is **discarded
without a warning or an error**.

- **Place it self-closing at the layout root**, immediately above the layout markup
  (`<FluentLayout>` / the host's own layout root). Never around a `Router`, a `RouteView`, or any
  other markup you expect to see rendered.
- `FluentMessageBarProvider` is **not** part of `FluentProviders` — declare it separately
  (it is the one provider the host still lists by name).
- `<Router>` must stay the **root element** of every host's `Routes.razor`. Wrapping it in
  `FluentProviders` — or in any v5-only component whose child contract you have not verified —
  blanks **every page of the app** while the build, the component tests and the circuit all report
  success. That happened on 2026-10-16 (FluentUI 4.14.2 → 5.0.1): all three hosts shipped blank and
  nothing failed.

**General check — a green component-test suite does not prove the app boots.** Every page test
renders its component directly, so the whole suite stays green while `Routes.razor` renders nothing.
When adding or moving a shell-level element, render the real `Routes` component and assert the
**layout shell** appears (Admin guard:
`tests/SchoolCollab.Admin.Tests.Unit/RoutesShellRenderTests.cs`, asserting `article.content`). Write
acceptance criteria that test **placement**, not mere presence: “`<FluentProviders />` exists in
`MainLayout.razor`” passes in the broken state, “the routed layout renders under `Routes`” does not.

Before adopting one of these components, check its `P:` members in the installed XML (see the sweep
above) — a parameter you pass that it does not declare is either swallowed or, with child markup,
silently deleted.

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

7. **Budget a grid track for the Actions column.** `LandingPage` appends an Actions
   column whenever `RowActions` is supplied (`EffectiveColumns`), so
   `LandingGridSettings.GridTemplateColumns` must carry **one track per rendered
   column, including that one** — six data columns plus a kebab is seven tracks,
   the last `auto`. Omit it and the kebab falls into an implicit track, which is
   what the notification-template landing shipped on 2026-10-21
   (`documents/solution/adr-authoring-surface-pattern.md`).

   Rule 3 is **not landing-specific**: every page or authoring component that
   loads data in `OnInitializedAsync` owns a `CancellationTokenSource`, and every
   mutation it dispatches passes that token (see the section below).

## CancellationTokenSource: loads vs. mutations

A component that only loads data once can use a single `_loadCts` for both the
initial load and any subsequent mutations, disposing it only in `Dispose()`.

When a component **reloads data** (e.g. `OwnerId` or `Code` changes) and also has
user-triggered mutations, use **two separate CTSes**:

- `_loadCts` — recreated per load, cancels stale loads, disposed after each load
  or in `Dispose()`.
- `_mutationCts` — created once for the component lifetime, disposed only in
  `Dispose()`. Mutations pass `MutationToken` (`_mutationCts.Token`).

**Mutations take a token too.** A single-load component may reuse `_loadCts`, but a
delete/adopt/toggle must still pass the token — otherwise the request outlives the
circuit (2026-10-21: the notification-template landing's reset dispatched
`DeleteAsync` with no token while its read was cancellable).

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

**Consumer-side overrides of a shared component's chrome.** When a shared child
component's own scoped padding/spacing doesn't match your custom template
content, override it **from the consumer** — a `:deep` rule under your container
class — instead of changing the shared component for one caller:

```css
/* Page-scoped: the SectionCard header's 1rem indent vs readout rows that carry
   none — the title sat 16px right of the rows beneath it (authoring round,
   2026-10-09). Every other SectionCard consumer keeps the shared chrome. */
.my-rules-card :deep(.section-card__header) {
    padding-left: 0;
    padding-right: 0;
}
```

Related facts from the same round: `FluentCard` pads its content by
`calc(var(--design-unit) * 5px)` (≈20px), so anything inside a card inherits that
padding **on top of** the child component's own — alignment math (and any `:deep`
fix) must account for it. And the same consumer-scoped pattern constrains content
that would otherwise overflow a shared pill/chip in a grid cell —
`.my-cell ::deep .chip { max-width: 100%; }` (long strand names claimed whole
rows until that was added).

### FluentUI v5 tokens: only `--color*` exists — a dead `var()` is dropped silently

v5 defines **no** `--neutral-*` / `--accent-*` custom property at all (its 481 design tokens are `--color*`), so a `var()`
naming one of the v4 names makes its **whole declaration** invalid at computed-value time and the browser drops it: no
build error, no console warning, and no bUnit test can see it. `border: 1px solid var(--neutral-stroke-rest)` renders as
a `0px` border, and the rule still looks “applied” because its other properties took effect. Two post-merge UI defects
hid this way (2026-10-17): an invisible toolbar period pill and vanished folder-tab borders.

- Use the v5 name and keep the old value as a **literal fallback**: `var(--colorNeutralStroke1, #d1d1d1)`. The fallback is
  what stops a typo or a future rename from failing silently again.
- Read the real name off the DOM rather than guessing:
  `getComputedStyle(document.documentElement).getPropertyValue('--colorNeutralForegroundOnBrand')`.
- **Never write `*/` inside a CSS comment.** It closes the comment early; the leftover text is parsed as CSS and can
  swallow the rule that follows *whole*, silently. This cost a full debugging cycle on `ActiveTermToolbar.razor.css`
  (2026-10-17). Guarded by `NoStrayCommentTerminator_InCss`.
- `FluentLink`'s shadow root is only a `<slot>`: its text is slotted light-DOM content, so colour **the slotted element**
  (plus `::deep svg` for its icon) — do not try to push tokens into the link.
- `FluentIcon` renders a bare `<svg style="fill: currentColor">`; target `svg` from your own wrapper via `::deep`.

A value fix is **not** proven by a green build or a passing test. Verify the **computed** value in a live browser with the
HTTP cache disabled, or a stale scoped-CSS bundle will look exactly like a fix that did not work.

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

> **FluentUI v5:** `FluentAnchor` is **removed** from 5.0.1 — the replacements are `FluentLink`
> (anchors/links) and `FluentAnchorButton` (button-styled anchors), with a changed `Appearance`
> mapping (see `MigrationFluentAnchor.md`). The repo's ~50 existing `<FluentAnchor>` usages render as
> unknown HTML elements until the rename round lands (the build only emits an `RZ10012` warning, so
> this fails **silently**). When you touch one of these grids, convert it to `FluentLink`.

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
**modal** dialog with a dark overlay that is **dismissible by clicking the overlay** or pressing ESC.
In v5 that posture is expressed by the modal *type* rather than a parameter: `Modal = false` renders
`type="modal"`, which keeps the backdrop **and** allows overlay/ESC dismissal (v4 spelled this
`Modal = true` + `PreventDismissOnOverlayClick = false`; `PreventDismissOnOverlayClick` no longer
exists). Do NOT use FluentUI's `ShowConfirmationAsync`/`ShowMessageBoxAsync` for destructive
confirmations: v5's version always sets `Modal = true` (`type="alert"`), so a backdrop click is
swallowed and ESC-only dismissal differs from this repo's own prompt — one confirmation mechanism,
plus overlay/ESC parity with v4, is why this helper exists. (Round A, 2026-10-15: the ~25 call sites
that still used the library API were routed through this helper.)

Do NOT add a second, page-level confirmation dialog for an action already marked
`destructive` — that would double-prompt the user. If an action is destructive, mark it
`destructive: true` and remove any hand-rolled confirmation in the handler.

### Show a success toast after successful mutations

Every successful mutation (delete/remove, create, save, unlink, etc.) SHOULD surface a
success notification via FluentUI's `INotificationService`. The `FluentToastProvider` is still in
`MainLayout.razor`, and `AddFluentUIComponents()` registers the service (scoped) — so just inject it
and await the call after the API succeeds:

```razor
@inject INotificationService NotificationService
// ...
await Api.DeleteSubjectAsync(id);
_items = await LoadSubjectsAsync();          // follow-up work first …
await NotificationService.ShowSuccessToastAsync($"Subject '{name}' deleted.");   // … toast LAST
```

**FluentUI v5 rename (Round A, 2026-10-15):** `IToastService`/`ToastService` and
`Toast.ShowSuccess(...)` no longer exist — v5 unifies toasts, message bars and progress into
`INotificationService`. The toast methods are **async** (`await` them): `ShowSuccessToastAsync`,
`ShowErrorToastAsync`, `ShowInfoToastAsync`, `ShowProgressToastAsync`, `CloseAllToastsAsync`; the
message-bar siblings are `ShowSuccessBarAsync`/`ShowErrorBarAsync`/`ShowInfoBarAsync`.

Guidelines:
- Inject `INotificationService` (the interface), never a concrete type — `AddFluentUIComponents()`
  registers it under the interface.
- Fire the toast ONLY after the API call succeeds (inside the `try`, after the await). Do
  not show success in the `catch` path.
- **Await it LAST on the success path.** `ShowSuccessToastAsync` "waits for the close result"
  (the library's own doc): the awaited task completes when the toast *closes* (7 s default), so a
  toast awaited *before* the list reload / route change gates that work behind it for up to 7 s
  (invisible in bUnit, whose loose JS returns in ~26 ms). Order the success path as
  mutate → reload/navigate → toast; if a mutation has no follow-up work, the existing
  "await the toast last" call site is already correct. Owner pin Q29(a), 2026-10-16.
- Include the affected item's name in the message (e.g. `Subject '{name}' deleted.`).
- For display text of a Topic entity, use the term "subject" (per the product's display
  convention) — never "topic" in user-facing strings.

### Side-panel drawers (`FluentDialog` + a drawer alignment)

FluentUI Blazor has no standalone `Drawer` component: a drawer is a `FluentDialog` with the drawer
alignment. There is a project precedent in `CodedValuesChatPanel.razor` (the AI assistant drawer on
the coded-values landing page).

> **FluentUI v5 (Round A, 2026-10-15):** the v4 API below no longer compiles. Verified changes:
> `DialogType.Panel` is gone — the drawer mode is `DialogOptions.Alignment`
> (`DialogAlignment.Start` / `.End`, rendered as the `<fluent-drawer>` element);
> `DialogParameters` is `DialogOptions`; `HorizontalAlignment` is `DialogAlignment`; the two-generic
> `ShowDialogAsync<TDialog, TData>` overload is gone
> (`ShowDialogAsync(typeof(TDialog), options)` + `options.Parameters`); and
> `IDialogContentComponent<TData>` is gone — a dialog body now derives from `FluentDialogInstance`
> (v5 spreads `DialogOptions.Parameters` onto the body's typed `[Parameter]`s by name).
> The header close (×) affordance lives on `DialogOptionsHeader` — read `MigrationFluentDialog.md`
> before changing it. The **file itself** (`CodedValuesChatPanel.razor`) is migrated in a later round.

v4 API (do not copy — superseded above):

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

- **Rows ride the shared `FormRow`** (`src/SchoolCollab.Admin.Shared/Components/FormRow.razor`)
  — the canonical row primitive: a 180px label gutter + input cell, so every
  label on a form shares ONE left edge. One `FormRow` per single field; one
  `AlignTop` `FormRow` for a peer pair/group (sub-inputs carry their own visible
  `Label=` or an `aria-label`); `LabelPosition="RowLabelPosition.Below"` puts the
  row's label under its input (side-by-side textarea cells: equalise heights with
  the same `Rows=` on each, e.g. `Rows="4"`); `Help="…"` renders a small (i) icon
  in the label whose native tooltip carries the row's hint (see "Compact forms"
  below). Dialogs use `FormRow` too —
  `.github/skills/dialog-ui/SKILL.md`. `FluentStack` remains for ad-hoc grouping
  that is not a labelled row.
- Put form controls in a `<FluentStack Orientation="Orientation.Vertical" Gap="1rem">`
  when fields should stack vertically. This gives consistent spacing between fields
  without custom flex containers.
- **Dropdowns: use the repo wrappers, not raw `FluentSelect`.** Enum options →
  `DropdownForEnum`; coded values → `CodedValueDropdown`; parent-owned DTO lists →
  `DropdownComponent` (bind the primitive key, side-effects on
  `@bind-SelectedValue:after` — never `SelectedValueChanged=`). Raw `FluentSelect`
  only when none covers the case (e.g. `Multiple`) — comment why. Full decision
  table + pitfalls: `.github/skills/dropdown-ui/SKILL.md`.
- Keep each FluentUI control's label/input relationship intact. Do not add custom
  `margin-bottom` to labels; let the control render its normal label spacing.
- Use `full-width` only for controls that should span the available form width (text
  fields, text areas, selects). Short controls such as dates, numbers, and
  display-order fields should use a narrower width via the **W1–W9 input width
  ladder** (the repo's consistent sizing scale) rather than ad-hoc `Style`
  strings or `narrow-field`:
  - **Repo dropdown wrappers** (`CodedValueDropdown`, `DropdownForEnum`,
    `DropdownComponent`): set the strongly-typed `Width` parameter —
    `Width="FieldWidth.W3"`. The wrapper's inline `style` lands on the
    `fluent-field` **host** v5 renders around the select, and the control is
    sized from it by `fluent-field > fluent-dropdown { width: inherit; }` in
    `src/SchoolCollab.Admin.Shared/wwwroot/app.css` (owner pin Q28(a),
    2026-10-16). Both halves are needed — the pin's slotted-control rule is what
    makes the width reach the visible control.
  - **Third-party inputs** (`FluentTextInput`, `FluentSelect`,
    `FluentDatePicker`, `FluentNumberInput`, `FluentTextArea`): apply the
    matching CSS class — `Class="w-3"`. The class lands on the same
    `fluent-field` host, so it rides the same slotted-control rule.
  - The ladder lives in `src/SchoolCollab.Admin.Shared/Components/FieldWidth.cs`
    (enum + `ToCssStyle()`), the `w-1`…`w-9` classes in
    `src/SchoolCollab.Admin/wwwroot/css/app.css` and the slotted-control rule in
    `src/SchoolCollab.Admin.Shared/wwwroot/app.css` — all share one pixel ladder,
    keep them in sync.
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

### Compact forms: hints, disabled states and empty notes

Forms in this repo (the assignment-authoring round was the forcing example) stay
compact by a standing set of rules — each one replaced a real block of wasted
space:

- **No inline narrative paragraphs on a form.** Prose that explains a row
  ("Feedback mode…", "Set the pass threshold…") rides a **(i) help icon on the
  row label** — `FormRow Help=` → the shared `HelpIcon` component
  (`SchoolCollab.Admin.Shared/Components/HelpIcon.razor`, native `title`
  tooltip, optional `Icon=` glyph swap). A null `Help` renders the previous
  markup byte-for-byte.
- **Disabled-with-reason, never hidden.** A control that cannot be used renders
  disabled with the reason on its native `title` — and, where the row is
  affected, on the row's (i) icon. Never a disappearing act, never a paragraph
  — the disabled-with-reason rule of the compartments spec.
- **Empty-state sentences are not paragraphs either.** "This subject has no
  strands yet." rides the disabled button's `title` plus the row label's (i)
  hint; the transient "you haven't picked X yet" state says **nothing** — it is
  the normal state, not a condition to flag. Gate the hint on the AUTHORITATIVE
  empty (a list that loaded and is empty): a fetch that never happened is not
  evidence of emptiness.
- **One static helper owns state → text.** `public static string? EmptyStateHint(...)`
  on the component (`ContextPicksSection`, the `ScoringFieldsSection.IsScoringInapplicable`
  precedent) feeds both the page's `FormRow Help=` and the button titles, so the
  icon and the tooltip can never drift; the page must not duplicate the
  condition.
- **Shared chrome wins unless the owner asks.** Don't restyle a shared component
  for one caller — override from the consumer with `:deep` (see "Use `::deep`…"
  above) or add an optional parameter whose null-render is the old markup.
- **Hints explain influence, not just meaning.** When a field drives downstream
  UI (assignment type/grading format → the scoring fields, the AI-generation
  gate), the driving row's (i) icon must name the consequences — compacting or
  merging the downstream explanations is only safe while the causal chain stays
  visible on the row that owns the choice (`Authoring.TypeGradingPurposeHint` is
  the shipped example, composed as `\n`-labelled lines that a native `title`
  renders as a multi-line tooltip). Ground every claim in the **documented**
  behaviour (`documents/solution/…`), not assumed semantics — and let the
  enum's own `[Description]` values be the user's vocabulary; never invent
  glosses (the "Manual" value is described as *Offline*, not "in class").

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
6. **Child-write re-render trap:** when the shared component's `@bind-Value`
   writes into a property of the parent's model *object* (not a parameter of
   the parent), Blazor re-renders only the shared component — the owning form
   does NOT re-render. Any owning-form logic that depends on a bound value
   (e.g. a create-only section shown/hidden by a division dropdown selection)
   will silently go stale. Fix: expose the change with
   `@bind-Value:after="@( () => SomeChanged.InvokeAsync(...) )"` on the shared
   component, plus a matching `[Parameter] EventCallback<...>` that the owning
   form handles (an EventCallback invoke re-renders the receiver).

### Example (this repo)

The components below are **illustrative, not an exhaustive registry** — do not
treat the list as the set of adopters. To find all current `*FormFields`
components (and their model interfaces), search the solution for
`*FormFields.razor`.

- `src/Students/SchoolCollab.Students.Application/Components/Students/TopicFormFields.razor`
  — shared rows + `ITopicFormModel`.
- `TopicCreateDialog.razor` / `TopicEditDialog.razor` — both models implement
  `TopicFormFields.ITopicFormModel` and render `<TopicFormFields Model="Model" />`.
- `GradeLevelFormFields.razor` — same idea for the routable
  `GradeLevels/Create.razor` / `GradeLevels/Edit.razor` pages (owns the
  `<EditForm>` + validator + action row).
- `Components/Pages/Periods/PeriodFormFields.razor` — shared rows +
  `IPeriodFormModel` for the routable `Periods/Create.razor` / `Periods/Edit.razor`
  pages; the create-only sub-periods section and Suggest/Backfill buttons stay in
  the owning `PeriodForm` (extras below shared fields), and the division
  `@bind-Value:after` → `DivisionChanged` callback is a worked example of the
  step-6 re-render trap.

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
