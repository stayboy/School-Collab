# Landing Page Wrapper (`<LandingPage<TItem>>`)

This document defines the standard pattern for wrapping every "landing page"
(the `Index.razor` list pages) into a single reusable shell, and the adoption
pattern for any future landing page.

It pairs with the layout contract documented in
[`SchoolCollabLayout.razor.css`](../../src/SchoolCollab.Admin.Shared/Components/Layout/SchoolCollabLayout.razor.css):
the layout owns the height chain and the three pinned section slots
(`page-toolbar`, `page-scroll` via `@Body`, `page-footer`). The wrapper owns
everything a landing page publishes into those slots.

## 1. Motivation — What the four landing pages share today

The four current landing pages are byte-for-byte duplicated in their
scaffolding, even though their data, columns, and row actions differ:

| Page | File |
|------|------|
| Assignments | `src/Assignments/.../Pages/Assignments/Index.razor` |
| Students | `src/Students/.../Pages/Students/Index.razor` |
| Coded Values | `src/CodedValues/.../Pages/CodedValues/Index.razor` |
| Config Flags | `src/Config/.../Pages/ConfigFlags/Index.razor` |

### 1.1 Shared scaffolding (identical across all four)

1. **Page shell**
   - `<PageTitle>…</PageTitle>`
   - `<ErrorBoundary>` wrapping `ChildContent` + an `ErrorContent` that
     renders `<FluentMessageBar Intent="MessageIntent.Error">Something went wrong: @ex.Message</FluentMessageBar>`
   - `@implements IDisposable`, a `volatile bool _disposed`, and a `Dispose()`
     that cancels/diposes the load + search `CancellationTokenSource`s.
   - A `string? _error` plus the trailing
     `@if (!string.IsNullOrEmpty(_error)) { <FluentMessageBar Intent="Error" class="mt-3">@_error</FluentMessageBar> }`

2. **Pinned toolbar** — `<SectionContent SectionName="page-toolbar">`:
   - `title-row` div containing `<h1 class="page-title">`
   - `action-bar` div containing, left-to-right:
     - a `+ New …` `<FluentButton Appearance="Accent">` that
       `Nav.NavigateTo("/<route>/create")`
     - a per-page **filter control** (see §1.2)
     - a `spacer` div
     - a `search-box` `<FluentTextField>` with `Immediate="true" ImmediateDelay="300"`

3. **List body states** (rendered in the layout's scroll region):
   - `_items is null` → `spinner-container` + `<FluentProgressRing />`
   - `_items.Length == 0` → `<FluentMessageBar Intent="Info">No … yet.</FluentMessageBar>`
   - otherwise → `grid-container` wrapping `<FluentDataGrid>` with
     `Items="@…AsQueryable()"`, `GenerateHeader="Sticky"`, `MultiLine="true"`,
     `TGridItem="<Dto>"`, and a `GridTemplateColumns` string.

4. **CSS** — four near-identical `Index.razor.css` files, each defining the
   same `.title-row`, `.page-title`, `.action-bar`, `.spacer`,
   `::deep .search-box`, `.spinner-container`, `.grid-container` rules
   (only `.search-box` width and an optional `.action-bar-right` differ).

### 1.2 Per-page variations (the parts that are genuinely different)

| Concern | Assignments | Students | Coded Values | Config Flags |
|---------|-----------|----------|--------------|--------------|
| `TGridItem` | `AssignmentSummaryDto` | `StudentDto` | `CodedValueDto` | `FeatureFlagDto` |
| `GridTemplateColumns` | 8-col | 7-col | 6-col | 5-col |
| Filter control | `FluentSelect` (status) | `FluentCheckbox` "Show deleted" | `FluentCheckbox` "Show deleted" | `FluentCheckbox` "Show archived" |
| Search strategy | **server** (`_searchResults` + `_searchCts` cancel) | **client** (`_filteredItems` computed from `_searchText`) | **server** (`_searchResults` + `_searchCts`) | **server** (`@bind` + reload) |
| Row actions | View / Edit / Publish / Unpublish / Close / Delete | View / Delete / Recover | Enable/Disable / Edit / Delete / Recover | (none — detail page only) |
| Deleted-items grid | — | yes | yes | — |
| Confirmation dialogs | no (delete direct) | yes (`DialogService`) | yes (`DialogService`) | — |
| Optimistic row mutations | publish/unpublish/close | (full reload on recover) | enable/disable toggle | — |
| Pinned footer / AI chat | — | — | yes (`page-footer` chat + side drawer) | — |

**Conclusion:** the shell, toolbar, state machine, and CSS are the *shared
kernel* of the landing pages; the data type, columns, row actions, filter
control, search strategy, and optional deleted-grid / footer are the
*per-page* surface. The wrapper must own the former and expose clean
extension points for the latter.

## 2. The Wrapper Component

Location:
```
src/SchoolCollab.Admin.Shared/Components/Landing/LandingPage.razor
src/SchoolCollab.Admin.Shared/Components/Landing/LandingPage.razor.css
```

A new `Landing/` group folder under the shared components root, matching the
repo convention of "one folder per concern, named after the namespace segment."
Every admin module already references `SchoolCollab.Admin.Shared`, so the
component is available everywhere with no new project references (same
approach as `CodedValueDropdown` — see
[`shared-coded-value-dropdown.md`](./shared-coded-value-dropdown.md)).

`LandingPage<TItem>` is a generic Blazor component. Its API is deliberately
small and declarative: the page describes *what* to show, the wrapper renders
the *how*.

### 2.1 Parameters

| Parameter | Type | Required | Purpose |
|-----------|------|----------|---------|
| `TItem` | type param | yes | The grid row DTO. |
| `Title` | `string` | yes | `<PageTitle>` + the `page-title` `<h1>`. |
| `CreateLabel` | `string` | yes | Text of the `+ New …` button (e.g. `"+ New Assignment"`). |
| `CreateRoute` | `string` | yes | `Nav.NavigateTo` target for the New button. |
| `Items` | `TItem[]?` | yes | The list to render. `null` ⇒ loading spinner. |
| `EmptyMessage` | `string` | yes | Message bar text when `Items` is empty. |
| `GridTemplateColumns` | `string` | yes | Passed to `<FluentDataGrid>`. |
| `Columns` | `RenderFragment` | yes | The grid's column definitions, forwarded as child content of `<FluentDataGrid>`. |
| `Error` | `string?` | no | Error text; rendered as a red message bar under the grid when non-empty. |
| `Loading` | `bool` | no | When true, show the spinner even if `Items` is non-null (used for in-flight server search). |
| `ToolbarFilters` | `RenderFragment?` | no | Slot rendered in the action-bar right after the New button (status select / show-deleted checkbox / show-archived checkbox). |
| `ToolbarActions` | `RenderFragment?` | no | Slot rendered at the far right of the action-bar, after the search box (e.g. the ✨ Chat button). |
| `SearchText` | `string` | no | Two-way bound search box value. |
| `SearchTextChanged` | `EventCallback<string>` | no | Fired (debounced) on the search box; the page owns the strategy (server fetch with cancellation, or client filter, or bind-reload). |
| `SearchPlaceholder` | `string?` | no | Search box placeholder. Defaults to `"Search…"`. |
| `SearchEnabled` | `bool` | no | Default `true`; set `false` to hide the search box entirely (none of the four pages need this today, but a future page might). |
| `CreateEnabled` | `bool` | no | Default `true`; set `false` to hide the New button. |
| `AboveGrid` | `RenderFragment?` | no | Slot rendered above the main grid (the deleted-items section for Students/CodedValues). |
| `Footer` | `RenderFragment?` | no | Published into `<SectionContent SectionName="page-footer">` (CodedValues chat launcher). |
| `ChildContent` | `RenderFragment?` | no | Escape hatch for anything not covered by the slots above; rendered last in the scroll region. Kept for forward-compatibility. |

### 2.2 Rendered shape

```razor
<PageTitle>@Title</PageTitle>

<ErrorBoundary>
    <ChildContent>
        <SectionContent SectionName="page-toolbar">
            <div class="title-row">
                <h1 class="page-title">@Title</h1>
            </div>
            <div class="action-bar">
                @if (CreateEnabled)
                {
                    <FluentButton Appearance="Appearance.Accent"
                                  OnClick="@(() => Nav.NavigateTo(CreateRoute))">
                        @CreateLabel
                    </FluentButton>
                }
                @ToolbarFilters
                <div class="spacer"></div>
                @if (SearchEnabled)
                {
                    <FluentTextField Placeholder="@SearchPlaceholder"
                                     class="search-box"
                                     Immediate="true" ImmediateDelay="300"
                                     Value="@SearchText"
                                     ValueChanged="@OnSearchChanged" />
                }
                @ToolbarActions
            </div>
        </SectionContent>

        @AboveGrid

        @if (Loading || Items is null)
        {
            <div class="spinner-container"><FluentProgressRing /></div>
        }
        else if (Items.Length == 0)
        {
            <FluentMessageBar Intent="MessageIntent.Info">@EmptyMessage</FluentMessageBar>
        }
        else
        {
            <div class="grid-container">
                <FluentDataGrid Items="@Items.AsQueryable()"
                                GridTemplateColumns="@GridTemplateColumns"
                                GenerateHeader="GenerateHeaderOption.Sticky"
                                MultiLine="true" TGridItem="TItem">
                    @Columns
                </FluentDataGrid>
            </div>
        }

        @if (!string.IsNullOrEmpty(Error))
        {
            <FluentMessageBar Intent="MessageIntent.Error" class="mt-3">@Error</FluentMessageBar>
        }

        @ChildContent
    </ChildContent>
    <ErrorContent Context="ex">
        <FluentMessageBar Intent="MessageIntent.Error">Something went wrong: @ex.Message</FluentMessageBar>
    </ErrorContent>
</ErrorBoundary>

@if (Footer is not null)
{
    <SectionContent SectionName="page-footer">@Footer</SectionContent>
}
```

### 2.3 CSS (`LandingPage.razor.css`)

Consolidates the four duplicate `Index.razor.css` files into one scoped file.
Because structural elements are authored directly in the wrapper, they receive
the wrapper's CSS-isolation scope; elements inside `RenderFragment` slots
(filter controls, search box, columns) are reached via `::deep`. This is the
exact same scope behaviour documented in `SchoolCollabLayout.razor.css` and
the existing `Index.razor.css` files.

```css
.title-row { flex-shrink: 0; display: flex; align-items: center; gap: 0.75rem; padding-top: 0.25rem; }
.page-title { margin: 0; }
.action-bar { flex-shrink: 0; width: 100%; display: flex; align-items: center; gap: 0.75rem; padding-bottom: 0.75rem; }
.spacer { flex-grow: 1; flex-basis: auto; }

::deep .search-box { flex: 1 1 auto; min-width: 180px; max-width: 320px; width: 100%; }
::deep .status-filter { width: 160px; }

.action-bar-right { display: flex; align-items: center; gap: 0.5rem; margin-left: auto; flex-shrink: 0; }

.spinner-container { flex: 1 1 0; min-height: 0; display: flex; align-items: center; justify-content: center; }
.grid-container { width: 100%; }
```

Pages no longer ship their own `Index.razor.css` for these scaffolding rules.
Page-specific styling (e.g. a deleted-section heading) stays in a page-scoped
CSS file scoped to that page.

## 3. Search Strategy — owned by the page, not the wrapper

The three search strategies are too different to collapse into the wrapper
without forcing a behavioural change (violates §7 of
[`shared-kernel-extraction-pattern.md`](./shared-kernel-extraction-pattern.md)
— "do not extract if the contract differs"). So the wrapper only provides the
search **box** and emits a debounced `SearchTextChanged`; the page decides
what to do:

| Page | Strategy | Page-side handler |
|------|----------|-------------------|
| Assignments | server | cancels prior `_searchCts`, calls `Api.ListAsync(null, ct)`, client-filters the result, sets `_searchResults`, sets `Items`/`Loading` on the wrapper |
| Students | client | sets `_searchText`, recomputes `_filteredItems`, binds that to the wrapper's `Items` |
| Coded Values | server | cancels `_searchCts`, calls `Api.SearchAsync(text, …)`, sets `_searchResults` |
| Config | server (bind+reload) | sets `_search`, calls `LoadAsync(_showArchived, _search, …)` |

> **Note (per the shared-kernel pattern §7):** a future `ISearchStrategy`
> abstraction was considered and rejected. The three strategies differ in
> cancellation lifecycle, whether they call the API at all, and whether the
> result replaces or filters `Items`. Forcing a single interface would either
> leak page concerns into the shared kernel or paper over real differences.
> Keep search in the page until a second page wants the *same* strategy
> (rule of three).

## 4. Adoption pattern for a new landing page

A new `<Domain>` landing page becomes ~40 lines of declarations + a column
block, instead of ~250 lines of duplicated scaffolding:

```razor
@page "/widgets"
@inject WidgetsApiClient Api
@inject NavigationManager Nav
@inject ILogger<Index> Logger
@implements IDisposable

<LandingPage TItem="WidgetDto"
             Title="Widgets"
             CreateLabel="+ New Widget"
             CreateRoute="/widgets/create"
             EmptyMessage="No widgets yet."
             GridTemplateColumns="minmax(180px,2fr) 1fr 1fr auto"
             Items="@_items"
             Error="@_error"
             SearchText="@_searchText"
             SearchTextChanged="@OnSearchChanged"
             SearchPlaceholder="Search widgets…">
    <ToolbarFilters>
        <FluentCheckbox @bind-Value="_showInactive" @bind-Value:after="ReloadAsync"
                        Label="Show inactive" />
    </ToolbarFilters>
    <Columns>
        <TemplateColumn Title="Name" SortBy="@_sortByName" Sortable="true">
            <FluentAnchor Appearance="Appearance.Hypertext"
                          Href="@($"/widgets/{context.Id}")">@context.Name</FluentAnchor>
        </TemplateColumn>
        <TemplateColumn Title="Status">
            <FluentBadge Appearance="@(context.IsActive ? Appearance.Accent : Appearance.Neutral)">
                @(context.IsActive ? "Active" : "Inactive")
            </FluentBadge>
        </TemplateColumn>
        <TemplateColumn Title="Actions">
            <FluentAnchor Href="@($"/widgets/{context.Id}/edit")">Edit</FluentAnchor>
            <FluentAnchor Href="#" OnClick="@(() => OnDeleteAsync(context.Id))" class="ms-2">Delete</FluentAnchor>
        </TemplateColumn>
    </Columns>
</LandingPage>

@code {
    private WidgetDto[]? _items;
    private string? _error;
    private string _searchText = string.Empty;
    private bool _showInactive;
    private CancellationTokenSource? _cts;
    private volatile bool _disposed;
    private readonly GridSort<WidgetDto> _sortByName = GridSort<WidgetDto>.ByAscending(x => x.Name);

    protected override async Task OnInitializedAsync() => await ReloadAsync();

    private async Task ReloadAsync()
    {
        _cts?.Cancel(); _cts?.Dispose();
        _cts = new();
        try { _items = await Api.ListAsync(_showInactive, _searchText, _cts.Token); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _error = ex.Message; }
    }

    private Task OnSearchChanged(string text)
    {
        _searchText = text;
        return ReloadAsync();
    }

    private async Task OnDeleteAsync(Guid id) { /* … */ }

    public void Dispose() { _disposed = true; _cts?.Cancel(); _cts?.Dispose(); }
}
```

### 4.1 Checklist for adopting the wrapper on a new page

- [ ] Page references `SchoolCollab.Admin.Shared` (already required for the layout).
- [ ] `_Imports.razor` includes `@using SchoolCollab.Admin.Shared.Components.Landing`
      (add to the shared `_Imports.razor` once, so every module gets it).
- [ ] Page declares `@implements IDisposable` and cancels its own CTS(s) — the
      wrapper does **not** own the page's `CancellationTokenSource`s.
- [ ] Page supplies a unique `EmptyMessage`, `GridTemplateColumns`, and `Columns`.
- [ ] If the page needs a deleted-items section, render it via `AboveGrid`.
- [ ] If the page needs a pinned footer (chat), render it via `Footer`.
- [ ] Page-specific CSS only — no re-declaration of `title-row` / `action-bar` /
      `spacer` / `search-box` / `spinner-container` / `grid-container`.
- [ ] `dotnet build SchoolCollab.sln` is 0 errors / 0 new warnings.

## 5. Migration of the four existing landing pages

The migration is mechanical and should land as one PR per page (so each diff is
reviewable), after the wrapper itself lands. Order: simplest → most complex.

### Phase 0 — Add the wrapper (foundation PR)
1. Create `src/SchoolCollab.Admin.Shared/Components/Landing/LandingPage.razor`
   (+ `.razor.css`).
2. Add `@using SchoolCollab.Admin.Shared.Components.Landing` to
   `src/SchoolCollab.Admin.Shared/Components/_Imports.razor` so every module
   sees it without per-module imports.
3. Add unit tests mirroring `DashboardCard`/`DashboardSection` test shape
   (`SchoolCollab.Admin.Tests.Unit`): renders title, New button navigates,
   `null` Items shows spinner, empty Items shows message bar, non-empty
   renders a `FluentDataGrid` with the supplied `GridTemplateColumns`,
   `Error` shows the red bar.
4. `dotnet build` + run the wrapper unit tests.

### Phase 1 — Config Flags (simplest: no row actions, no deleted grid, no footer)
- Move the grid columns into `<Columns>`.
- Move `Show archived` checkbox into `<ToolbarFilters>`.
- Bind `SearchText`/`SearchTextChanged` to the existing `LoadAsync` reload.
- Delete `Index.razor.css` (all its rules now live in the wrapper).
- Update `AssignmentIndexBunitTests`-equivalent for Config if any exist.

### Phase 2 — Students
- `StudentDto` rows + `GridSort` definitions stay; only the scaffolding moves.
- `Show deleted` checkbox → `ToolbarFilters`.
- The deleted-students grid → `<AboveGrid>` (conditionally rendered).
- Search is client-side: bind `SearchText`, recompute `_filteredItems`, bind to `Items`.
- `DialogService` confirmations and `RecoverAsync` stay in the page `@code`.
- Delete `Index.razor.css`.

### Phase 3 — Assignments
- `FluentSelect` status filter → `ToolbarFilters`.
- Search is server-side: keep the `_searchResults` + `_searchCts` logic; switch
  between `_items` and `_searchResults` by binding the right array to `Items`
  and setting `Loading` while searching. The dual-view (search grid vs default
  grid) collapses into a single `<Columns>` block, because both currently
  render the *same* columns — only the bound array differs.
- Optimistic publish/unpublish/close/delete handlers stay in `@code`.
- Delete `Index.razor.css`.
- Update `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentIndexBunitTests.cs`.

### Phase 4 — Coded Values (most complex: footer + side drawer + feature flag)
- `Show deleted` checkbox → `ToolbarFilters`.
- Deleted-values grid → `<AboveGrid>`.
- Server search → as Phase 3.
- The `✨ Chat` button → `<ToolbarActions>` (only rendered when
  `_aiChatEnabled == true`; the page still owns the feature-flag resolution and
  the tri-state null guard against first-paint flash).
- The inline `InputOnly` chat → `<Footer>` (published into `page-footer`).
- `CodedValuesChatPanel` (the side drawer) is page-owned and stays outside the
  wrapper; it is not a toolbar concern.
- Delete `Index.razor.css`.
- Update `tests/SchoolCollab.CodedValues.Tests.Unit/Components/*` (the
  `CodedValuesPageHost` test helper may need to pass `Items` through the wrapper).

### Phase 5 — Cleanup & docs
- Delete the now-empty `Index.razor.css` files (verify no page references them).
- Add a short "Landing pages" section to the solution docs index pointing here.
- Grep for stray `page-toolbar` `SectionContent` in pages that should now use the
  wrapper (`ConfigFlagDetail.razor` is a *detail* page, not a landing page — it
  keeps its own toolbar; do not force it onto the wrapper).

## 6. What the wrapper deliberately does NOT own

To keep the abstraction honest (shared-kernel pattern §7), these stay in the
page and are **not** parameters of the wrapper:

- **The data load.** `OnInitializedAsync`, the API client, the
  `CancellationTokenSource`, and the disposal are the page's. The wrapper takes
  `Items` (already loaded) and renders states from it.
- **The search strategy.** See §3.
- **Row actions and optimistic mutations.** View/Edit/Delete/Publish/etc. are
  page-specific command surfaces; the wrapper only forwards the `Columns`
  fragment that contains them.
- **Confirmation dialogs.** `DialogService.ShowConfirmationAsync` is injected and
  called by the page (Students, CodedValues).
- **The AI chat surface.** Feature-flag resolution, the chat hub, and the side
  drawer are CodedValues-specific and stay in that page; only the launcher
  button and the pinned footer slot are wired through the wrapper.
- **`_disposed` / `Dispose`.** The page owns its cancellation tokens, so the
  page disposes. (A `Loading`-only wrapper has no resources to free.)

If two pages later want the *same* optimistic-mutation pattern or the *same*
search strategy, extract those into a sibling component/service at that point
— not now, and not into this wrapper.

## 7. Verification

- `dotnet build SchoolCollab.sln` — 0 errors, 0 new warnings.
- Unit tests:
  - `SchoolCollab.Admin.Tests.Unit` — new `LandingPageTests` covering shell
    states, New-button navigation, and slot rendering.
  - `SchoolCollab.Assignments.Tests.Unit/AssignmentIndexBunitTests.cs` —
    updated to find elements via the wrapper; asserts the search/empty/grid
    behaviour still holds.
  - `SchoolCollab.CodedValues.Tests.Unit/Components/*` — updated through
    `CodedValuesPageHost`; chat drawer still opens from the footer slot.
- Playwright smoke (`SchoolCollab.CodedValues.Tests.Playwright`,
  `SchoolCollab.Config.Tests.Playwright`) — landing pages render and search.
- Visual: confirm the pinned toolbar, spinner, sticky grid header, and (for
  CodedValues) the pinned chat footer all behave exactly as before. The
  height-chain contract in `SchoolCollabLayout.razor.css` is unchanged.

## 8. Rollout order (summary)

1. **PR A** — foundation: wrapper + `_Imports` + wrapper unit tests.
2. **PR B** — Config Flags migration + its tests.
3. **PR C** — Students migration + its tests.
4. **PR D** — Assignments migration + its tests.
5. **PR E** — Coded Values migration (toolbar actions + footer) + its tests.
6. **PR F** — delete dead `Index.razor.css` files + doc index entry.

Each PR is independently shippable: after PR A the wrapper exists and is tested
but nothing uses it; PRs B–E each remove one page's duplicated scaffolding; PR F
is pure cleanup.