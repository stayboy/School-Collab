# Plan — Edit-page refactor: inline grid + override dialog + tenant gate

> **Goal:** Refactor `src/Settings/SchoolCollab.Settings.Admin/Components/Pages/EntityCodeRules/Edit.razor` and its sibling components to (1) fix the icon-only button rendering bug, (2) collapse the dual `SegmentEditor` + `SegmentsList` into a single editing grid, (3) move per-tenant overrides into a `DialogShellBase`-derived modal, and (4) gate the override UI behind a real tenant (`VisibleTenantService.IsRealTenant`).
>
> **Branch target:** `feature/entity-code-auto-generation`.
> **Spec:** follow-up to `docs/plans/2026-07-28-entity-code-auto-generation.md` §4.11 (admin UI) and §4.12 (tenant overrides).
> **Convention references:** `dialog-ui` skill (DialogShellBase / DialogShellFooter / ShowShellDialogAsync), `fluentui-icons-in-school-collab` skill (FluentIcons.X shorthand), `featureflags-tenant-gates` skill (VisibleTenantService.IsRealTenant gate).

---

## 1. Goals & non-goals

### Goals (verbatim from the user)

1. **Fix icon rendering** — the `FluentButton` icons for *Remove segment*, *Move up*, and *Move down* in the segment toolbar are not showing.
2. **Single editing surface** — show only the preview grid + add buttons for editing/deleting from the grid. Do **not** show the inline `SegmentEditor` (the full edit form) and the read-only `SegmentsList` at the same time. The grid itself must support inline edit, delete, and add.
3. **Override add/edit in a dialog** — move the per-tenant override add/edit from the inline `OverrideEditor` section to a `DialogShellBase`-derived modal.
4. **Tenant gate** — the override feature only shows when a real tenant is set on the page (`VisibleTenantService.IsRealTenant`). Default/sentinel tenants (`Guid.Empty` claim) do not see the override UI.

### Non-goals (v1)

- No drag-and-drop reordering (FluentUI Blazor 4.x has no built-in DnD for arbitrary form rows). ↑/↓ buttons remain. (Spec §4.11 documented divergence.)
- No changes to the server-side contract (`EntityCodeRule`, `TenantEntityCodeRuleOverride`, `ReplaceEntityCodeRuleOverrides`, the two API endpoints). The refactor is UI-only.
- No changes to `Create.razor` (it still uses the full `SegmentEditor` — the Edit-page collapse is scoped to Edit). The Create page is short-lived and a wizard; the Edit page is the long-lived "manage this rule" surface.
- No new unit tests for the Razor components (the repo does not have a Razor component test harness — testing is handled by the handler-level unit tests on the server).

---

## 2. Design decisions

### 2.1 Icon rendering — switch to `<FluentIcon>` child content

The three icon-only toolbar buttons (`FluentIcons.Delete`, `FluentIcons.ChevronUp`, `FluentIcons.ChevronDown`) use the `Icon="@…"` parameter with no child text. FluentUI Blazor's `FluentButton.Icon` parameter is unreliable when the button is icon-only (no text content); the icon often renders with zero bounding box. The repo's established fix is to place `<FluentIcon Icon="@…" />` **as child content** inside the button — this gives the icon its own bounding box. The precedent is `src/Students/SchoolCollab.Students.Admin/Components/Students/GuardiansTab.razor:55` which uses the fully-qualified `Icons.Regular.Size20.Delete` inside `<FluentButton>`. We will reuse the **shorthand** `FluentIcons.X` (the verified-shorthand list per the `fluentui-icons-in-school-collab` skill includes `Delete`, `ChevronUp`, `ChevronDown`, `Add`, `Edit`, `Save`, `Settings`, `Person`, `Tag`).

**Applied to:** `SegmentEditor.razor` (3 toolbar buttons), `OverrideEditor.razor` (the row Remove button — same bug, fix preemptively), and `EntityCodeRulesApiClient.cs` callers are unaffected.

### 2.2 Single editing surface — `SegmentsGrid` replaces `SegmentEditor` + `SegmentsList`

Today the Edit page renders:
```
<SegmentEditor @bind-Segments="_model.Segments" />
<SegmentsList Segments="_model.Segments" />
```

The `SegmentEditor` is a vertical stack of full edit forms (all fields per row), the `SegmentsList` is a read-only table with a "Preview next 5 codes" button. The user requirement is to show **only one** surface that supports inline add / edit / delete.

**New component:** `SegmentsGrid.razor` (under the same folder) that renders a single table where each row is **inline-editable**:

| Column | Behavior |
|---|---|
| `#` | index badge |
| `Role` | dropdown |
| `Type` | dropdown (changing type resets the row's field-specific inputs) |
| `FixedText` / `Prefix` / `Suffix` / `ResetPeriod` / `MinWidth` / `UpperLimit` | inline inputs (visibility adapts to Type, same logic as today) |
| Actions | ↑ / ↓ / 🗑 (icon buttons using the §2.1 fix) |

Each row starts in **view** mode (compact summary: type, role, static text or sequence shape, reset, upperlimit). Clicking the row's `Edit` icon (or double-clicking the row) enters **edit** mode (inputs appear). Save (✓) commits the row; Cancel (✕) reverts. Delete (🗑) removes the row (with confirmation if the row is non-empty). A row-level `FluentMessageBar` shows validation errors in edit mode (reusing the same checks as today: duplicate index, missing FixedText on Fixed, missing Prefix on AlphanumericSequence, MinWidth ≤ 0, UpperLimit format).

The `SegmentsGrid` replaces **both** `SegmentEditor` and `SegmentsList` on the Edit page. The Create page continues to use the full `SegmentEditor` (it is a short-lived wizard; inline editing in a grid would be noisy for an empty starting point).

The `SegmentsList`'s "Preview next 5 codes" button moves to the `SegmentsGrid` toolbar as a separate "Preview" button — it operates on the current grid state (read-only mode summary).

**File disposition:**
- Keep `SegmentsList.razor` for now (Create page no longer references it, but the file is harmless and can be removed in a follow-up if no other consumer appears). Actually — the Create page DOES reference it. Let me reconsider. Create page uses `SegmentsList Segments="_model.Segments"`. If we remove it, Create breaks. We will keep `SegmentsList.razor` as-is for Create, and add the new `SegmentsGrid.razor` for Edit only. The Edit page drops both `SegmentEditor` and `SegmentsList`.

### 2.3 Override editor → dialog

The current `OverrideEditor.razor` is an inline section with add/remove/save buttons. Per the user requirement, move add/edit into a dialog.

**New flow:**
- The Edit page shows a "Tenant overrides" **summary card** (when a real tenant is set — see §2.4): a compact table of the current overrides (Segment #, Field, Value, 🗑), plus an **"Add override"** button.
- Clicking "Add override" opens a `ShowShellDialogAsync<OverrideDialog, OverrideFormModel, OverrideResult>` dialog (the `DialogShellBase` pattern) with: Segment dropdown, Field dropdown, Value input, Cancel/Save footer.
- Clicking a row's `Edit` icon opens the same dialog pre-filled with that row's values (for the `Field` and `Value`; segment is read-only on edit — you cannot change which segment an existing override targets; you delete + re-add).
- Delete remains inline (🗑 on the row triggers a confirmation dialog, then a `PUT /{id}/overrides` with the row removed).
- After dialog close (success), the page reloads the override list via `Api.GetOverridesAsync(ruleId)`.
- The bulk **"Save overrides"** button is removed — every change (add / edit / delete) is its own atomic `PUT /{id}/overrides` call. This matches the API's replace-all semantics per row group.

**New files:**
- `OverrideDialog.razor` — `@inherits DialogShellBase<OverrideFormModel, OverrideResult>` with Segment / Field / Value inputs. The Submit side effect calls `Api.ReplaceOverridesAsync(ruleId, [newOverride])` after merging with the existing list (load → replace the matched `Id` row OR append → save). Wait — the API is replace-all on the rule. So the dialog's submit must: load current list, apply the change, PUT the whole list. We will do the load/merge in the parent (Edit page), pass the resulting full list to the dialog via the form model, and the dialog's Submit just calls `Api.ReplaceOverridesAsync`. Actually cleaner: the dialog's `SubmitAsync` receives the `OverrideFormModel` (the edited row), the dialog does NOT touch the API — the parent page handles the merge + PUT via an `EventCallback<OverrideFormModel>` on the dialog wrapper. Let me reconsider.

**Cleaner design:** The Edit page owns the override list state. The dialog is a pure form:
1. Edit page: `async Task OpenAddDialogAsync() { var result = await DialogService.ShowShellDialogAsync<OverrideDialog, OverrideFormModel, OverrideResult>(new OverrideFormModel { ...empty... }, "Add override", DialogSize.Medium); if (result is not null) { merge into list; PUT; reload } }`
2. Edit page: `async Task OpenEditDialogAsync(OverrideFormModel existing) { var result = await DialogService.ShowShellDialogAsync<OverrideDialog, OverrideFormModel, OverrideResult>(existing, "Edit override", DialogSize.Medium); if (result is not null) { merge into list; PUT; reload } }`
3. Edit page: `async Task DeleteOverrideAsync(OverrideFormModel row) { confirm; remove from list; PUT; reload }`

The dialog's `SubmitAsync` returns the modified `OverrideFormModel` (a copy) — the parent does the merge. This keeps the dialog pure (no API knowledge) and centralizes the API call + reload in the page.

**Override dialog layout** (`OverrideDialog.razor`):
- Segment: `FluentSelect` (read-only on Edit — labeled "Segment"; disabled if `Model.IsEdit`)
- Field: `FluentSelect` of `OverrideFieldDto`
- Value: `FluentTextField` with type-specific placeholder
- `DialogShellFooter` with `SubmitText="@(Model.IsEdit ? "Save" : "Add")"`

**Override form model** (`OverrideFormModel` in `EntityCodeRuleFormModels.cs` — extended):
```csharp
public sealed class OverrideFormModel
{
    public Guid Id { get; set; }            // Guid.Empty for new
    public bool IsEdit { get; set; }        // controls read-only Segment + button label
    public Guid EntityCodeSegmentId { get; set; }
    public int SegmentIndex { get; set; }   // for the dropdown label
    public OverrideFieldDto Field { get; set; }
    public string Value { get; set; } = "";
}
```

**New result type** (`OverrideResult`):
```csharp
public sealed record OverrideResult(OverrideFormModel FormModel);
```

### 2.4 Tenant gate

The override UI (summary card + Add button) only renders when `VisibleTenantService.IsRealTenant` is `true`. The user clarified: "only shows when global tenant is set on page" — interpreted as the `tenant_id` claim is a non-empty Guid (the `VisibleTenantService` definition of "real tenant").

**On the Edit page:**
```csharp
@inject VisibleTenantService VisibleTenant
...
private bool _isRealTenant;
protected override async Task OnInitializedAsync()
{
    _isRealTenant = (await VisibleTenant.GetScopeAsync()).IsRealTenant;
    await LoadAsync(...);
}
```

The `Tenant overrides` summary card is wrapped in `@if (_isRealTenant && _rule?.TenantId is null)`. (Both conditions: real tenant context AND the rule is the shared blueprint — tenant-owned rules never take overrides per spec §4.12.)

The Edit page **header** also shows a small badge when no real tenant is set: *"Viewing as default tenant — per-tenant overrides are disabled."* (Plain informational text, not an error.)

### 2.5 Scope: Edit page only

The Create page keeps the full `SegmentEditor` + `SegmentsList` (Create is a wizard with an empty initial state; the new grid would be empty + awkward). The Edit page gets the `SegmentsGrid` + override dialog.

---

## 3. Detailed design

### 3.1 File changes

| File | Action |
|---|---|
| `src/Settings/SchoolCollab.Settings.Admin/Components/Pages/EntityCodeRules/Edit.razor` | **Modify** — replace `<SegmentEditor>` + `<SegmentsList>` with `<SegmentsGrid>`; replace `<OverrideEditor>` section with a tenant-gated summary card + "Add override" button; add `VisibleTenantService` injection; add dialog open handlers; remove `_reloadOverrides` plumbing (overrides are reloaded explicitly after each mutation). |
| `src/Settings/SchoolCollab.Settings.Admin/Components/Pages/EntityCodeRules/SegmentEditor.razor` | **Modify** — fix the 3 icon-only buttons to use `<FluentIcon>` child content (§2.1). No markup restructure. |
| `src/Settings/SchoolCollab.Settings.Admin/Components/Pages/EntityCodeRules/OverrideEditor.razor` | **Modify** — fix the row Remove icon button to use `<FluentIcon>` child content (§2.1). No markup restructure. |
| `src/Settings/SchoolCollab.Settings.Admin/Components/Pages/EntityCodeRules/SegmentsGrid.razor` | **Create** — the new single-surface grid (view/edit modes per row, ↑/↓/🗑 actions, Preview button, inline validation messages). |
| `src/Settings/SchoolCollab.Settings.Admin/Components/Pages/EntityCodeRules/SegmentsGrid.razor.css` | **Create** — scoped CSS for the grid layout. |
| `src/Settings/SchoolCollab.Settings.Admin/Components/Pages/EntityCodeRules/OverrideDialog.razor` | **Create** — `@inherits DialogShellBase<OverrideFormModel, OverrideResult>` (the modal). |
| `src/Settings/SchoolCollab.Settings.Admin/Components/Pages/EntityCodeRules/EntityCodeRuleFormModels.cs` | **Modify** — extend `OverrideFormModel` with `IsEdit`, `SegmentIndex`; add `OverrideResult` record. |

`Create.razor`, `Index.razor`, `SegmentsList.razor`, `EntityCodeRulesApiClient.cs`, `EntityCodePreview` helper, and all server-side files are **untouched**.

### 3.2 `SegmentsGrid` component contract

```razor
@* Inline-editable segments grid (replaces SegmentEditor + SegmentsList on the Edit page).
   - One table; each row toggles between view mode (compact summary) and edit mode (full inputs).
   - Add button appends a new Fixed segment in view mode.
   - ↑/↓ buttons reorder; Delete removes the row.
   - "Preview next 5 codes" button computes from the current state (reuses the SimSegment pattern from SegmentsList).
   - Two-way bound via EventCallback<List<SegmentFormModel>>. *@

@code {
    [Parameter, EditorRequired] public List<SegmentFormModel> Segments { get; set; } = new();
    [Parameter] public EventCallback<List<SegmentFormModel>> SegmentsChanged { get; set; }

    private int? _editingIndex; // null = no row in edit mode
    private int _validationErrorIndex = -1;
    private string? _validationError;
    private List<string> _preview = [];

    private void BeginEdit(int idx) { _editingIndex = idx; _validationErrorIndex = -1; _validationError = null; }
    private void CancelEdit() { _editingIndex = null; _validationErrorIndex = -1; _validationError = null; }
    private async Task CommitEditAsync(int idx) { /* validate, push changes, exit edit mode */ }
    private void AddRow() { /* append Fixed segment, begin edit on new row */ }
    private void MoveUp(int idx) { /* swap with idx-1, renumber */ }
    private void MoveDown(int idx) { /* swap with idx+1, renumber */ }
    private void DeleteRow(int idx) { /* confirm if non-empty, remove, renumber */ }
    private void ComputePreview() { /* reuse SimSegment from SegmentsList (extract to static helper?) */ }
}
```

Per-row view mode renders: `<span class="seg-index">@idx</span>` + Role + Type + StaticText or Sequence shape + Reset + UpperLimit. Edit mode renders all the inputs (reuse the input markup from the current `SegmentEditor`, scoped to the row).

**Edit-mode markup reuse:** Rather than duplicating the per-Type conditional fields from `SegmentEditor`, extract a small `<SegmentRowEditor>` sub-component? That is over-engineering for v1 — keep it inline in `SegmentsGrid`. The per-row edit markup will mirror the current `SegmentEditor` row markup (same `showFixedFields`, `showNumericFields`, `showAlphaFields` flags, same UpperLimit validation). Net duplication is ~80 lines, accepted.

### 3.3 `OverrideDialog` component

```razor
@* Add / Edit per-tenant override dialog (spec §4.12).
   Inherits DialogShellBase so saving/error/Cancel/Close plumbing is shared.
   The dialog is pure-form: it does NOT touch the API. The parent (Edit page)
   handles load-existing → merge → PUT /{id}/overrides after a successful submit. *@

@inherits DialogShellBase<OverrideFormModel, OverrideResult>

<EditForm Model="Model" OnValidSubmit="HandleSubmitAsync">
    <DataAnnotationsValidator />

    <FluentSelect TOption="SegmentOption"
                  Items="@_segmentOptions"
                  @bind-SelectedOption="Model.SelectedSegment"
                  Label="Segment"
                  Disabled="@Model.IsEdit"
                  OptionText="@(o => o.Label)"
                  OptionValue="@(o => o.SegmentId.ToString())" />
    <FluentSelect TOption="OverrideFieldDto"
                  Items="@_fieldOptions"
                  @bind-SelectedOption="Model.Field"
                  Label="Field"
                  OptionText="@(f => f.ToString())"
                  OptionValue="@(f => ((int)f).ToString())" />
    <FluentTextField @bind-Value="Model.Value"
                     Label="Value"
                     Placeholder="e.g. ABC (FixedText), X (Prefix), 4 (MinWidth)" />

    <DialogShellFooter Saving="@Saving"
                       Error="@Error"
                       SubmitText="@(Model.IsEdit ? "Save" : "Add")"
                       SavingText="@(Model.IsEdit ? "Saving…" : "Adding…")"
                       OnCancel="HandleCancelAsync" />
</EditForm>

@code {
    [Parameter] public IReadOnlyList<SegmentOption> SegmentOptions { get; set; } = [];

    private List<SegmentOption> _segmentOptions = [];
    private static readonly OverrideFieldDto[] _fieldOptions = Enum.GetValues<OverrideFieldDto>();

    protected override void OnModelInitialized(OverrideFormModel model)
    {
        _segmentOptions = SegmentOptions.ToList();
        // Pre-select first segment on Add; on Edit, match by EntityCodeSegmentId.
        if (!model.IsEdit && _segmentOptions.Count > 0)
            model.SelectedSegment = _segmentOptions[0];
        else if (model.IsEdit)
            model.SelectedSegment = _segmentOptions.FirstOrDefault(o => o.SegmentId == model.EntityCodeSegmentId);
    }

    protected override Task<OverrideResult?> SubmitAsync(OverrideFormModel model)
    {
        if (model.SelectedSegment is null)
        {
            Error = "Pick a segment.";
            return Task.FromResult<OverrideResult?>(null);
        }
        if (string.IsNullOrWhiteSpace(model.Value))
        {
            Error = "Value is required.";
            return Task.FromResult<OverrideResult?>(null);
        }
        model.EntityCodeSegmentId = model.SelectedSegment.SegmentId;
        model.SegmentIndex = model.SelectedSegment.Index;
        model.Value = model.Value.Trim();
        return Task.FromResult<OverrideResult?>(new OverrideResult(model));
    }

    public sealed record SegmentOption(Guid SegmentId, int Index, string Label);
}
```

### 3.4 Edit page restructure

```
Edit.razor (top to bottom):
  <PageTitle>Edit @(_rule?.Name ?? "Generation Rule")</PageTitle>
  <h2>Edit: @(_rule?.Code ?? "…")</h2>

  @if (!_isRealTenant) { <FluentMessageBar>Viewing as default tenant — per-tenant overrides are disabled.</FluentMessageBar> }

  <ErrorBoundary>
    <ChildContent>
      @if (_loading) { <FluentProgressRing /> }
      else if (_rule is not null)
      {
        <EditForm Model="_model" OnValidSubmit="SubmitAsync">
          <DataAnnotationsValidator />
          <div class="form-fields">
            <FluentTextField Value="@_rule.Code" Disabled="true" Label="Code" />
            <FluentTextField @bind-Value="_model.Name" Label="Name *" Required />
            <FluentTextArea @bind-Value="_model.Description" Label="Description" Rows="2" />
            <FluentCheckbox @bind-Value="_model.IsActive" Label="Active" />
          </div>

          <hr class="my-4" />
          <SegmentsGrid @bind-Segments="_model.Segments" />

          @if (_isRealTenant && _rule.TenantId is null)
          {
            <hr class="my-4" />
            <TenantOverridesCard Rule="_rule" OnChanged="ReloadOverridesAsync" />
          }

          @if (!string.IsNullOrEmpty(_error)) { <FluentMessageBar>@_error</FluentMessageBar> }

          <div class="mt-3">
            <FluentButton Type="Submit">Save</FluentButton>
            <FluentButton OnClick="Cancel">Cancel</FluentButton>
          </div>
        </EditForm>
      }
    </ChildContent>
    <ErrorContent>...</ErrorContent>
  </ErrorBoundary>
```

`TenantOverridesCard` is a small new component (inline in Edit.razor as a `@code` partial? or a separate file). For clarity, **inline it in Edit.razor** as a private RenderFragment — it's ~50 lines and only used here.

### 3.5 Validation parity

`SegmentsGrid` reuses the same per-segment validation logic as the current `SegmentEditor.CreateRowValidation`:
- Duplicate indices → row-level message
- Missing `FixedText` on Fixed → row-level message
- Missing `Prefix` on AlphanumericSequence → row-level message
- `MinWidth` < 1 on Numeric/Alphanumeric → row-level message
- `UpperLimit` format (numeric → positive int; alphabetic → single A-Z letter) → row-level message

The Edit page's `SubmitAsync` still runs the **cross-row** checks (count > 0, no duplicates across rows, UpperLimit per row) before sending the `PUT`.

### 3.6 Preview reuse

The `SegmentsList.SimSegment` simulator is currently a private nested class. The new `SegmentsGrid` needs the same simulator for its "Preview next 5 codes" button. **Extract `EntityCodePreview` (already exists for Index) — extend it with a `RenderNext5(IReadOnlyList<SegmentFormModel>)` method that returns a `List<string>`.** The simulator class becomes a nested type of `EntityCodePreview`. This also lets `SegmentsList.razor` reuse it (optional cleanup; not required for this refactor).

### 3.7 CSS

- `SegmentsGrid.razor.css` — new. Mirrors `SegmentsList.razor.css` table styling + adds per-row edit-mode inputs styling. Uses the same scoped-CSS conventions (no overwriting rules; merge).
- `SegmentEditor.razor.css` — unchanged (only the markup icon-button pattern changed, no CSS delta needed).
- `Edit.razor.css` — unchanged.

---

## 4. Test plan

No new automated tests (no Razor component test harness in the repo). Manual verification checklist:

### 4.1 Icon rendering (§2.1)
- [ ] `Edit.razor` with multiple segments — the 🗑 / ↑ / ↓ icons render visibly in the segment toolbar of the `SegmentsGrid`.
- [ ] `Edit.razor` overrides summary card — the 🗑 icons on each override row render visibly.
- [ ] `Create.razor` — the existing `SegmentEditor` Add / Remove / ↑ / ↓ icons all render (regression check; the fix is in the shared component).

### 4.2 Single editing surface (§2.2)
- [ ] `Edit.razor` shows exactly one segments surface (the grid), not the dual panel.
- [ ] Add a segment via the grid's "Add segment" button → new row appears in view mode.
- [ ] Click the row's Edit icon → row enters edit mode with inputs.
- [ ] Change a field, click ✓ (commit) → row returns to view mode with the change reflected.
- [ ] Click ✕ (cancel) → row returns to view mode with original values.
- [ ] ↑ / ↓ reorder the rows; indices re-number automatically.
- [ ] 🗑 removes the row; if the row is non-empty, a confirmation dialog appears.
- [ ] "Preview next 5 codes" button shows 5 representative codes from the current state.
- [ ] Validation errors (duplicate index, missing FixedText, etc.) appear inline on the row in edit mode and block commit.

### 4.3 Override dialog (§2.3)
- [ ] Edit page with a real tenant + shared blueprint rule → "Tenant overrides" summary card shows.
- [ ] "Add override" button → modal opens with Segment / Field / Value inputs.
- [ ] Fill the form, click Add → modal closes, row appears in the summary, the API was called (`PUT /{id}/overrides` with the new row appended).
- [ ] Click Edit on an existing row → modal opens pre-filled with that row's values; Segment dropdown is disabled.
- [ ] Change the value, click Save → row updates in the summary.
- [ ] Click 🗑 on an existing row → confirmation, row removed, API called.
- [ ] Cancel the dialog (X) → no API call, summary unchanged.

### 4.4 Tenant gate (§2.4)
- [ ] Sign in as a real tenant user → overrides card visible on the Edit page for shared-blueprint rules.
- [ ] Sign in as the default/system tenant (`Guid.Empty` claim) → overrides card hidden; the informational banner appears.
- [ ] Tenant-owned rules (TenantId != null) never show the overrides card (existing behavior).

### 4.5 Regression
- [ ] `Create.razor` renders the full `SegmentEditor` + `SegmentsList` unchanged (Create is untouched).
- [ ] `Index.razor` renders the landing page unchanged (template preview column + search still work).
- [ ] `dotnet build SchoolCollab.sln` clean.
- [ ] All 564 unit tests still green (`SchoolCollab.Settings.Tests.Unit`, `Students.Tests.Unit`, `Assignments.Tests.Unit`, `ArchitectureTests.Unit`).

---

## 5. Implementation order

1. **Fix icon rendering** in `SegmentEditor.razor` + `OverrideEditor.razor` (3 lines + 1 line). Build + test.
2. **Extend `EntityCodeRuleFormModels.cs`** with the `OverrideFormModel.IsEdit` + `SegmentIndex` fields and the `OverrideResult` record. Build.
3. **Create `OverrideDialog.razor`** (the `DialogShellBase` modal). Build.
4. **Create `SegmentsGrid.razor`** (+ `.razor.css`) — the single-surface grid with view/edit modes. Build.
5. **Refactor `Edit.razor`** — replace `SegmentEditor` + `SegmentsList` with `SegmentsGrid`; replace inline `OverrideEditor` with tenant-gated summary card + dialog open handlers; inject `VisibleTenantService`. Build + test.
6. **Spec update** — amend `docs/plans/2026-07-28-entity-code-auto-generation.md` §4.11 and §6 progress marker with the Edit-page refactor notes.
7. **Final build + full test sweep.**

---

## 6. Risks & mitigations

| Risk | Mitigation |
|---|---|
| The `SegmentEditor` icon fix could regress `Create.razor` | `Create.razor` uses the same `SegmentEditor` component — the fix is shared. Manual regression check in §4.1. |
| `SegmentsGrid` introduces a bug in the bulk-save path (the Edit page's `PUT /api/entity-code-rules/{id}` carries the full segments list — if the grid's two-way binding breaks, segments could be lost on save) | The grid is a pure projection over `List<SegmentFormModel>` — `SegmentsChanged` fires only on Add / Remove / Reorder / Commit. Edit-mode local state does NOT bubble until commit. The bulk `PUT` payload matches the current Create-page behaviour. |
| `VisibleTenantService` inject fails at runtime (DI misconfiguration) | `VisibleTenantService` is already registered in `SchoolCollab.Admin.Shared` (used by Students Admin); the Settings Admin module inherits it via `ModuleServices`. Verified by `grep VisibleTenantService src/Settings/SchoolCollab.Settings.Admin/ModuleServices.cs`. |
| Dialog cancel returns null but the parent treats it as success | Parent checks `if (result is not null)` before applying the change; null → no-op. |
| The `SimSegment` extract changes the public surface of `EntityCodePreview` | Keep the existing `RenderFirst` overload (used by Index), add the new `RenderNext5` overload. Backward-compatible. |
| Scope creep into `Create.razor` | Explicitly out of scope (§1); Create keeps the dual-panel layout. If the grid is good, a follow-up PR can migrate Create. |

---

## 7. Open questions

None — the user requirements are unambiguous. Proceeding with implementation after this plan is filed.