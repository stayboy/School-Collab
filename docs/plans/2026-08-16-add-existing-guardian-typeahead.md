# Plan — "Add Existing Guardian" via the relationship dropdown (student edit dialog)

> **Date:** 2026-08-16
> **Status:** Spec (implementation pending)
> **Related:** `docs/plans/2026-08-14-student-edit-all-inclusive.md` — PR #170 (guardian card redesign + contacts reorder) that this builds on.

---

## 1. Objective

Give the inline **add-guardian row** on the student edit dialog a way to **link an
existing guardian** through the **relationship dropdown**. Selecting a special
trigger line item in that dropdown swaps the free-text name fields for a
**typeahead** that searches existing guardians. The separate "Add existing"
button is removed; the relationship dropdown becomes the sole entry point.

## 2. Trigger — relationship dropdown

- **Component:** a **local `FluentSelect` wrapper** in `StudentFormFields.razor`
  (option B). The shared `CodedValueDropdown` is **not** modified.
- **Option list (top → bottom):**

  ```
  Add Existing Guardian        ← selectable trigger line item (sentinel value)
  ───────────────────────────  ← non-selectable horizontal divider
  Father
  Mother
  Guardian
  … (remaining relationship types)
  ```

- **Sentinel:** a dedicated
  `private static readonly Guid ExistingGuardianSentinel = new("e1a2b3c4-d5e6-4f7a-8b9c-0d1e2f3a4b5c");`
  — **not** `Guid.Empty` (avoids confusion with `new Guid()` defaults and `null`
  "no selection"). Selecting it → existing-guardian mode (`InExistingMode`).
- **Divider:** a **disabled, non-selectable** empty `FluentOption` styled as a
  1px horizontal rule (muted/hint colour, reduced padding). `Disabled` guarantees
  it can never be selected; it is excluded from value-mapping and the
  duplicate-link checks.
- Selecting a real relationship → free-text `FluentTextField` name fields,
  exactly as today.

### 2.1 Critical — the sentinel is UI-only transient; never reaches the save path

`_newRelId` is a `Guid?` that flows into
`GuardianAssignment.RelationshipCodedValueId`, which `ToGuardianDraft` →
`link.Update(role, relationship, …)` persists. If the sentinel leaks through,
the handler writes a bogus relationship Guid (constraint violation / dangling
reference).

**Guard (must implement):** `AddInlineGuardianAsync` resolves the sentinel
before constructing the `GuardianAssignment`:

```csharp
var relationshipForDraft =
    _newRelId == ExistingGuardianSentinel ? _pickedRelationshipId : _newRelId;
var assignment = new GuardianAssignment(
    ExistingGuardianId: _existingGuardianId,   // null when adding a NEW guardian
    FirstName: …, LastName: …,
    RelationshipCodedValueId: relationshipForDraft, …);
```

When the user picks a guardian, the dropdown swaps the sentinel for a real
relationship (representative when determinable — see §3.2 — else the user picks
one, or it stays `null` which is a valid "no relationship" link). The sentinel
## 3. Existing-guardian mode — the typeahead

When the sentinel is selected, hide the **First name / Last name** fields and
show a debounced typeahead:

```
[  Search existing guardian…                        ]  (•)Student (•)Contact
                                                     ^ two-option radio, far right
```

- **Radio (far right):** a `FluentRadioGroup` with **`Student`** and
  **`Contact`** — default **`Contact`**. Pushed right via `margin-left: auto`
  (mirrors the "ward count far-right" rhythm). Switching clears results and
  re-searches if there's a query.
- **Debounce:** ~300 ms `CancellationTokenSource` cancel-and-restart on each
  keystroke, min 2 chars (matches `EntityGrid`'s internal cadence). Call this
  out — the custom `FluentTextField` typeahead does **not** inherit
  `EntityGrid`'s built-in debounce.

### 3.1 Search modes

- **`Contact` mode (single-step, default):**
  `Api.ListGuardiansAsync(query, excludeStudentId: thisStudentId)` → guardian
  rows; click a guardian to select it.
> ⚠️ **Superseded by §11** — Student mode is now **single-step**: type a student name → guardians of matching students are listed directly (two-line rows). The two-step "pick a student" flow below is the original §3.1 design, kept for history.

- **`Student` mode (two-step, original — superseded by §11):**
  1. `Api.ListStudentsAsync(query)` → student rows; pick a student.
  2. `Api.ListGuardiansByStudentAsync(studentId)` → that student's guardians
     as the result list; pick one.

### 3.2 "(Relationship)" source — differs by mode

- **By-student mode:** the relationship **is** available —
  `ListGuardiansByStudentAsync` returns `StudentGuardianViewDto` carrying
  `RelationshipCodedValueId`. Resolve via `_relNames` → renders `(Father)`.
- **By-contact mode:** `GuardianDto.RelationshipName` is `null` for a
  tenant-wide search (a relationship is per student↔guardian link, not a
  guardian property). So `(Relationship)` is **omitted** — the user picks the
  relationship in the dropdown after selecting the guardian.

Do **not** fabricate a representative relationship for the contact path.

## 4. Result row format (guardian rows, both modes)

```
Mr. John Smith (Father)                          +2 wards
```

- Left: `FullName (Relationship)` — title + name via the existing
  `_salutations` / `_relNames` (or `TitleName` on the DTO), matching the card
  head-line format. Relationship shown only in by-student mode (§3.2).
- **Ward count:** `+N ward` / `+N wards` (singular/plural), **far-right** and
  **green**, from `GuardianDto.StudentCount` enriched via the existing bulk
  `Api.ListStudentCountsByGuardiansAsync(ids)` call. `—` when `StudentCount`
  is `null`.
- Max ~20–30 rows; empty + "no matches" states.

### 4.1 Row model

```csharp
private sealed record GuardianSearchRow(
    Guid Id,
    string FullName,
    string? RelationshipName,   // set in by-student mode; null in by-contact
    int? WardCount);
```

Replaces `GuardianPickerRow` (which is removed — see §7).

## 5. Selection → draft

Picking a guardian sets:

- `_existingGuardianId = selected.Id`
- `_newFirstName` / `_newLastName` prefilled (and `_newTitleId` when available)
- the relationship dropdown swaps its sentinel for a real relationship:
  - by-student → the guardian's relationship to that student (`StudentGuardianViewDto.RelationshipCodedValueId`)
  - by-contact → `null` (user picks)

The Add button then links the existing guardian through the existing
`AddDraftAsync` (`ExistingGuardianId != null`) path — already-linked guardians
are excluded server-side via `excludeStudentId` **and** client-side (see §6).

## 6. Edge cases / guards (acceptance criteria)

1. **Duplicate-link for drafts (client-side).** `excludeStudentId` only hides
   **server-linked** guardians. Guardians already drafted in
   `Model.GuardianLinks` (create mode, not yet saved) would still appear.
   Filter them client-side so they don't show as pickable. (`AddDraftAsync`
   already silently no-ops on dup, but the typeahead shouldn't offer them.)
2. **By-student mode: searching the current student.** If the edited student is
   itself searched in Student mode, their guardians include ones already
   linked here. Exclude the current student from the student-search results
   (simplest), or apply the same client-side duplicate filter.
3. **Add-button disabled state in existing mode.** When the sentinel is
   selected but no guardian is picked yet, **Add must be disabled**. Extend the
   `Disabled` predicate:
   `Disabled="@(InExistingMode && _existingGuardianId is null) || <existing first/last check>"`.

## 7. Removals

- The **"Add existing"** `<FluentButton>` (`guardian-add-existing`) in the add
  row.
- The obsolete `GuardianPanelMode.Existing` panel + its state:
  `StartAddExisting`, `ExistingTab`, `SwitchExistingTab`,
  `OnStudentSearchAsync` / `OnContactSearchAsync` (replaced by the typeahead
  handlers), `BackToStudentSearch`, `_pickerRows`, `_selectedRows`,
  `AddSelectedGuardiansAsync`, `GuardianPickerRow`.
- **Enum trim:** `GuardianPanelMode { List, Edit, Existing }` → `{ List, Edit }`.
- **`GuardianPickerRow` removal is contingent** on replacing ALL its usages
  (the `EntityGrid TItem="GuardianPickerRow"` markup, `OnContactSearchAsync`,
  `OnStudentSearchAsync`, `SelectStudentAsync`, `AddSelectedGuardiansAsync`).
  The by-student two-step uses the new `GuardianSearchRow` — confirm no
  dangling reference remains.

## 8. Files (implementation phase)

- `src/Students/SchoolCollab.Students.Application/Components/Students/StudentFormFields.razor`
  — local `FluentSelect` wrapper + divider, sentinel detection, typeahead +
  radio, `GuardianSearchRow`, mode handlers, draft construction + sentinel
  guard, removals.
- `.../StudentFormFields.razor.css` — typeahead list, right-aligned radio,
  green `+N wards`, divider styling.
- `src/Students/SchoolCollab.Students.Application/Services/StudentsApiClient.cs`
  — **only if** a representative-relationship or bulk by-student enrichment
  endpoint is needed; otherwise no change (existing `ListGuardiansAsync`,
  `ListStudentsAsync`, `ListGuardiansByStudentAsync`,
  `ListStudentCountsByGuardiansAsync` cover the data).
- Tests — extend `StudentFormFieldsRenderActionsBunitTests` / add a typeahead
  bUnit test (see §9.4 for the testing approach).

## 9. Acceptance criteria

1. No separate "Add existing" button remains in the add row.
2. The relationship dropdown renders `Add Existing Guardian` → a
   **non-selectable** horizontal divider → the relationship types.
3. Selecting `Add Existing Guardian` swaps the name fields for the typeahead,
   whose far-right **radio** toggles `Student`/`Contact`; `Student` is
   two-step (find student → pick their guardian), `Contact` is one-step.
4. Results render `FullName (Relationship)` + **green, right-aligned**
   `+N ward(s)`. `(Relationship)` appears in by-student mode only; omitted in
   by-contact mode.
5. Selecting a guardian prefills name/relationship; Add links the existing
   guardian. Already-linked guardians (server-side AND client-side drafts)
   are excluded from the typeahead.
6. Add is **disabled** in existing mode until a guardian is picked.
7. The sentinel never reaches `GuardianAssignment.RelationshipCodedValueId`
   (the §2.1 guard).
8. Build 0 errors; full Admin unit suite green.

### 9.1 Verification — typeahead debounce
Implement a `CancellationTokenSource` debounce (~300 ms, min 2 chars) for the
custom `FluentTextField` typeahead. `EntityGrid`'s internal debounce does NOT
apply here.

### 9.2 Verification — FluentSelect divider rendering
`FluentOption Disabled` is supported; whether the styled empty option renders
as a clean horizontal rule inside the `<fluent-select>` listbox needs a visual
check against the installed FluentUI version. If `OptionGroup` /
`FluentOptionGroup` is available, grouping the relationship types gives a
cleaner native break — verify and pick the cleaner path during implementation.

### 9.3 Verification — sentinel stability
Confirm no real relationship coded value has the sentinel Guid (real coded
values have distinct Guids — safe by construction, but assert in a unit test).

### 9.4 Verification — bUnit testing approach
`FluentSelect` renders as a `<fluent-select>` web component; bUnit hydration of
its internal options is fragile (the existing
`StudentFormFieldsRenderActionsBunitTests` only asserts `.form-actions`, never
FluentSelect internals). **Test the option MODEL** — the wrapper's
`RelationshipOption[]` contains sentinel + divider + relationships, with
`divider.IsDivider`/`Disabled` set — rather than the rendered listbox DOM.
More reliable and decoupled from FluentUI's web-component rendering.

## 10. Phased implementation

> **Branch:** `feature/add-existing-guardian-typeahead` (rooted on `main`; the
> all-inclusive edit (#167/#170) it builds on is already merged).
>
> Phasing rule: every phase compiles and leaves the dialog usable. Later phases
> only **add** capability or **remove** dead code — they never break an earlier
> phase's contract. Verify after each phase before starting the next.

### Phase 0 — Wiring: `StudentId` parameter on `StudentFormFields`

**Why first:** the typeahead's `excludeStudentId` filter (§3.1, §6.1) needs the
id of the student being edited. The component does **not** currently declare it.
`Edit.razor` already passes `StudentId="Id"` (a silent no-op today), and
`StudentEditDialog` holds the id internally (`StudentIdKey`). Wire the
parameter so every later phase can call `Api.ListGuardiansAsync(query,
excludeStudentId: StudentId)`.

**Files:**
- `src/Students/SchoolCollab.Students.Application/Components/Students/StudentFormFields.razor`
  — add `[Parameter] public Guid? StudentId { get; set; }` (optional; null in
  create / wizard). Document: null for create (no server-linked guardians to
  exclude — drafted ones are filtered client-side in Phase 4).
- `src/Students/SchoolCollab.Students.Application/Components/Students/StudentEditDialog.razor`
  — pass `StudentId="@StudentId"` to its `<StudentFormFields>` (Create.razor
  and the wizard inline form leave it unset).

**Verify:** `dotnet build` 0 errors; edit dialog still loads/saves unchanged;
`StudentId` is populated when editing (quick watch / log), null when creating.

---

### Phase 1 — Models, sentinel, and new state (compile-only, not wired)

**Goal:** land all the new C# state and the option model with zero markup
change. Nothing renders yet; the existing add row keeps working.

**Files:** `StudentFormFields.razor` (`@code` block).

**Add:**
- `private static readonly Guid ExistingGuardianSentinel =
    new("e1a2b3c4-d5e6-4f7a-8b9c-0d1e2f3a4b5c");` (the §2 sentinel).
- `private enum RelationshipOptionKind { Relationship, Sentinel, Divider }`
  and `private sealed record RelationshipOption(Guid? Id, string Label,
  RelationshipOptionKind Kind, bool IsDisabled);`
- `RelationshipOption[]? _relationshipOptions;` + a `BuildRelationshipOptions()`
  that loads `RELATSHIPS` via the already-injected `CodedValuesApi
  .GetChildrenByParentCodeAsync("RELATSHIPS", ct)` and prepends the sentinel +
  a disabled divider option. Load it in `OnInitializedAsync` (guard when
  `!ShowGuardians`).
- `private sealed record GuardianSearchRow(Guid Id, string FullName,
  string? RelationshipName, int? WardCount);` (§4.1) — alongside, not yet
  replacing, `GuardianPickerRow`.
- New existing-mode state: `bool _inExistingMode;`, `string? _searchQuery;`,
  `enum SearchMode { Contact, Student }` with `SearchMode _searchMode =
  SearchMode.Contact;`, `GuardianSearchRow[] _searchRows = [];`,
  `bool _searchLoading;`, `Guid? _existingGuardianId;`, `Guid?
  _pickedRelationshipId;`, `Guid? _pickedTitleId;`, `CancellationTokenSource?
  _searchCts;`. Plus `private bool InExistingMode => _inExistingMode;`.
- A `Dispose` for the new `_searchCts` (the component isn't `IDisposable` today —
  add `@implements IAsyncDisposable` or `IDisposable` and dispose the CTS).

**Verify:** `dotnet build` 0 errors; no behavior change (all new fields unused).

---

### Phase 2 — Local `FluentSelect` wrapper replaces the relationship dropdown

**Goal:** the add-row relationship selector is now a local `FluentSelect` that
renders `Add Existing Guardian` → a non-selectable divider → the relationship
types. Selecting a real relationship still binds `_newRelId` exactly as today;
selecting the sentinel sets `_inExistingMode = true` (the typeahead itself is
Phase 3, so the name fields still show for now — the only visible effect of the
sentinel this phase is the flag + a cleared `_newRelId`).

**Files:** `StudentFormFields.razor` (add-row markup), `.razor.css`.

**Changes:**
- In the `guardian-add-row`, replace the `<CodedValueDropdown
  Parent="CodedValueParent.Relationships" @bind-SelectedId="_newRelId" …/>`
  with a `<FluentSelect TOption="RelationshipOption"
    Items="@(_relationshipOptions ?? [])"
    SelectedOption="@_selectedRelOption"
    SelectedOptionChanged="@OnRelationshipOptionChanged"
    OptionText="@(o => o.Label)"
    OptionValue="@(o => o.Id?.ToString() ?? "sentinel")"
    Placeholder="Relationship"
    class="guardian-rel-select" />`.
- `OnRelationshipOptionChanged(RelationshipOption? opt)`: if `opt.Kind ==
  Sentinel` → `_inExistingMode = true; _newRelId = ExistingGuardianSentinel;
  _existingGuardianId = null; _searchRows = []; _searchQuery = null;` ; if
  `Divider` → ignore (should never fire — `Disabled`); else → `_inExistingMode
  = false; _newRelId = opt.Id;`. Keep a `_selectedRelOption` field synced.
- `.razor.css`: add `.guardian-rel-select { … }` matching the W4 width the old
  `CodedValueDropdown Width=FieldWidth.W4` produced (inline style or fixed
  width), and `.guardian-rel-divider` styling for the disabled divider option
  (1px rule, muted colour, reduced padding) — applied via the option's
  `class`/`Disabled`.
- **§9.2 check during this phase:** verify whether `FluentOptionGroup` /
  `FluentOptionGroup` gives a cleaner native break than a styled disabled
  `FluentOption`. Pick the cleaner path and record the choice in the PR
  description. The option MODEL (Phase 7 test) is stable either way.

**Verify:** `dotnet build`; dialog opens; dropdown shows sentinel + divider +
relationships; selecting `Father` etc. still adds with that relationship;
selecting `Add Existing Guardian` sets `_inExistingMode` (confirm via a temp
log or by the Phase 3 UI landing on top of it).

---

### Phase 3 — Typeahead UI + far-right radio (markup + CSS, no search yet)

**Goal:** when `_inExistingMode`, the First/Last name fields hide and the
typeahead input + `Student`/`Contact` radio render. Result list is empty this
phase (handlers land in Phase 4) but empty/loading states render.

**Files:** `StudentFormFields.razor` (add-row markup), `.razor.css`.

**Markup:** inside the `guardian-add-row` (or a sibling block that shows only
in existing mode), gated on `@if (InExistingMode)`:
- Hide the two `<FluentTextField @bind-Value="_newLastName"/>` /
  `_newFirstName` (wrap them in `@if (!InExistingMode)`).
- A debounced `<FluentTextField Value="_searchQuery"
  ValueChanged="@OnSearchChanged" Placeholder="Search existing guardian…"
  class="guardian-typeahead" />` + a `<FluentRadioGroup @bind-Value="_searchMode"`
  with `Student` / `Contact` options, `class="guardian-search-mode"` pushed
  right via `margin-left: auto` (mirrors the ward-count far-right rhythm).
- By-student step-2 sub-state: when a student is picked in Student mode, show a
  small "Back to student search" affordance (reuses the two-step flow).
- Result list: `<div class="guardian-search-results">` iterating
  `_searchRows` → row: `<span class="guardian-search-name">@row.FullName</span>`
  + `@if (row.RelationshipName is { } rel) { <span
  class="guardian-search-rel">(@rel)</span> }` + `<span
  class="guardian-search-wards">@WardLabel(row.WardCount)</span>`; click →
  `PickExistingGuardianAsync(row)` (Phase 5).
- `WardLabel(int? n)`: `n is null ? "—" : n == 1 ? "+1 ward" : $"+{n} wards"`.

**CSS (`.razor.css`):** `.guardian-typeahead`, `.guardian-search-mode {
  margin-left: auto; }`, `.guardian-search-results` (list-style none, column,
  gap), `.guardian-search-row` (flex, space-between, hover),
  `.guardian-search-rel { color: var(--neutral-foreground-hint); font-style:
  italic; }`, `.guardian-search-wards { color: var(--accent-fill-rest);
  margin-left: auto; }` (green, far-right), `.guardian-search-empty` for the
  no-matches state.

**Verify:** `dotnet build`; selecting the sentinel swaps name fields for the
input + radio; radio toggles; result list shows the empty state.

---

### Phase 4 — Search handlers (Contact + Student) with debounce + dup filter

**Goal:** typing produces live results in both modes, with the §4 row format
and the §6 duplicate exclusions.

**Files:** `StudentFormFields.razor` (`@code`).

**Add:**
- `async Task OnSearchChanged(string? value)`: set `_searchQuery`, cancel-and-
  restart `_searchCts` (~300 ms `Task.Delay`), min 2 chars (§9.1 — the custom
  `FluentTextField` does NOT inherit `EntityGrid`'s debounce). After the delay,
  dispatch to `SearchContactAsync` or `SearchStudentStepAsync` based on
  `_searchMode`. Switching the radio clears `_searchRows` and re-runs if
  `_searchQuery` ≥ 2 chars.
- `SearchContactAsync(string q, CancellationToken ct)`: `Api.ListGuardiansAsync(
  ct, search: q, excludeStudentId: StudentId)` → map to `GuardianSearchRow`
  (FullName from `TitleName`/`DisplayName`/First+Last via existing
  `_salutations`-style logic; **RelationshipName null** per §3.2). Ward counts:
  `Api.ListStudentCountsByGuardiansAsync(ids)` → `GuardianDto.StudentCount` is
  already enriched, so prefer that; fall back to the bulk call only if needed.
- `SearchStudentStepAsync(string q, CancellationToken ct)`: `Api.ListStudentsAsync(
  ct, search: q)`, **exclude the current student** (`StudentId`) per §6.2, map
  to a student-pick row (reuse `GuardianSearchRow` with `FullName =
  student.FullName`, `RelationshipName = null`, `WardCount = null`). On pick →
  `LoadGuardiansForStudentAsync(studentId)`.
- `LoadGuardiansForStudentAsync(Guid studentId, CancellationToken ct)`: `Api
  .ListGuardiansByStudentAsync(studentId, ct)` → `GuardianSearchRow` with
  **RelationshipName from `StudentGuardianViewDto.RelationshipCodedValueId`**
  resolved via `_relNames`/`EnsureRelNameAsync` (§3.2), ward counts via the bulk
  endpoint.
- **§6.1 client-side dup filter:** before rendering, drop any row whose `Id` is
  already in `Model.GuardianLinks` (drafted or server-linked). Apply in both
  modes so already-drafted guardians aren't pickable.

**Verify:** `dotnet build`; typing ≥2 chars in Contact mode lists guardians
(name + green `+N wards`, no relationship); Student mode lists students, picking
one lists that student's guardians with `(Relationship)`; already-linked /
already-drafted guardians don't appear.

---

### Phase 5 — Selection → draft, sentinel guard, Add-disabled predicate

**Goal:** picking a guardian prefills name/relationship and the Add button
links the existing guardian through `AddDraftAsync`; the §2.1 sentinel guard
holds; Add is disabled until a guardian is picked in existing mode.

**Files:** `StudentFormFields.razor` (`@code` + Add-button `Disabled`).

**Changes:**
- `PickExistingGuardianAsync(GuardianSearchRow row)`: set `_existingGuardianId
  = row.Id`; prefill `_newFirstName`/`_newLastName` (and `_pickedTitleId` when
  the DTO carries it); swap the sentinel for a real relationship: **by-student**
  → `_pickedRelationshipId = <the StudentGuardianViewDto relationship id>` and
  sync `_selectedRelOption` to that relationship; **by-contact** →
  `_pickedRelationshipId = null` and leave the dropdown on the sentinel line so
  the user picks a relationship (or stays null = valid "no relationship" link).
  `await InvokeAsync(StateHasChanged)`.
- **§2.1 guard** in `AddInlineGuardianAsync`: before constructing the
  `GuardianAssignment`, resolve `var relationshipForDraft = _newRelId ==
  ExistingGuardianSentinel ? _pickedRelationshipId : _newRelId;` and pass
  `RelationshipCodedValueId: relationshipForDraft`. Also pass
  `ExistingGuardianId: _existingGuardianId` (null when adding a NEW guardian).
- Add button `Disabled`: extend to
  `Disabled="@(_adding || InExistingMode && _existingGuardianId is null ||
  (!InExistingMode && (string.IsNullOrWhiteSpace(_newFirstName) ||
  string.IsNullOrWhiteSpace(_newLastName))))"` (§6.3).
- After a successful add in existing mode, reset `_inExistingMode = false;` and
  clear `_existingGuardianId`, `_searchRows`, `_searchQuery`,
  `_pickedRelationshipId`, `_newRelId` (same clear-on-success the free-text path
  already does). The post-add switch to the Edit panel (existing behaviour)
  stays.
- `CancelPanel` / panel nav: also reset the new existing-mode fields (the old
  `_pickerRows`/`_selectedRows` reset moves to Phase 6 when those fields are
  deleted).

**Verify:** `dotnet build`; pick a guardian → name prefills, by-student
prefills relationship, by-contact leaves it for the user; Add links the
existing guardian (card appears); Add is disabled until a pick; a debug assert
/ test (Phase 7) confirms `GuardianAssignment.RelationshipCodedValueId` is never
the sentinel.

---

### Phase 6 — Removals + enum trim

**Goal:** delete the old "Add existing" button, the `Existing` panel, and all
dead state. `GuardianPanelMode` trims to `{ List, Edit }`.

**Files:** `StudentFormFields.razor` (markup + `@code`), `.razor.css`.

**Remove (markup):**
- The `<FluentButton … OnClick="StartAddExisting" class="guardian-add-existing">Add
  existing</FluentButton>` in the add row.
- The entire `else if (_panelMode == GuardianPanelMode.Existing) { … }` block
  (the `EntityGrid TItem="GuardianPickerRow"` tabs + the by-student two-step +
  `AddSelectedGuardiansAsync` + Back button).

**Remove (`@code`):**
- `enum GuardianPanelMode { List, Edit, Existing }` → `{ List, Edit }`.
- `enum ExistingTab`, `StartAddExisting`, `SwitchExistingTab`,
  `OnSelectedRowsChanged`, `OnContactSearchAsync`, `OnStudentSearchAsync`,
  `SelectStudentAsync`, `BackToStudentSearch`, `AddSelectedGuardiansAsync`,
  `_existingTab`, `_pickerRows`, `_selectedRows`, `_studentResults`,
  `_selectedStudentId` (the by-student-mode picker field — **distinct** from the
  new `StudentId` parameter), `PickerGridSettings`, `StudentGridSettings`.
- `record GuardianPickerRow` (all usages now gone — §7 contingency satisfied).
- `.razor.css`: `.guardian-add-existing`, `.guardian-tabs`, and any
  existing-panel-only rules.
- `CancelPanel`: drop the `_pickerRows`/`_selectedRows`/`_studentResults`/
  `_selectedStudentId` clears (fields deleted); keep the new existing-mode
  reset from Phase 5.

**Verify:** `dotnet build` 0 errors; `grep -rn "GuardianPickerRow\|StartAddExisting\|GuardianPanelMode.Existing\|ExistingTab" src/` returns nothing; the dialog's List + Edit panels still work; the typeahead is the sole add-existing path.

---

### Phase 7 — Tests

**Goal:** lock the option model, the sentinel, and the disabled predicate
without coupling to `FluentSelect`'s web-component DOM (§9.4).

**Files:** `tests/SchoolCollab.Admin.Tests.Unit/` — extend
`StudentFormFieldsRenderActionsBunitTests.cs` or add
`StudentFormFieldsGuardianTypeaheadBunitTests.cs`.

**Tests:**
- **Option model:** `RelationshipOptions` contains exactly one sentinel (Kind ==
  `Sentinel`, `Id == ExistingGuardianSentinel`), exactly one divider (`Kind ==
  Divider`, `IsDisabled == true`), and the loaded relationship types (`Kind ==
  Relationship`, distinct non-sentinel Guids) in order sentinel → divider →
  relationships.
- **Sentinel stability (§9.3):** assert no loaded relationship coded value has
  `Id == ExistingGuardianSentinel` (safe by construction, but pinned).
- **Ward label:** `WardLabel(null) == "—"`, `WardLabel(1) == "+1 ward"`,
  `WardLabel(3) == "+3 wards"`.
- **Add-disabled predicate (unit, via a small extracted helper if needed):**
  existing-mode + no pick → disabled; existing-mode + pick → enabled; free-text
  mode + blank name → disabled; free-text + names → enabled.
- (Optional, if bUnit hydration allows) selecting the sentinel flips
  `InExistingMode` and the name fields are absent from the rendered add row.

**Run:** `dotnet test` for the Admin unit project; full Admin unit suite green.

---

### Phase 8 — Build, visual check, manual verification

- `dotnet build` 0 errors across the solution.
- `dotnet test` — Admin unit suite green.
- **§9.2 visual check:** confirm the divider renders as a clean horizontal rule
  inside `<fluent-select>` (or the chosen `FluentOptionGroup` path); adjust
  `.guardian-rel-divider` CSS if needed.
- Manual, create dialog: add a new guardian via free-text (unchanged); pick
  `Add Existing Guardian` → typeahead appears, Contact mode lists guardians,
  pick one → Add links it; switch to Student mode → two-step works.
- Manual, edit dialog: same, and already-linked guardians are excluded from the
  typeahead (server-side via `excludeStudentId` + client-side draft filter).
- Confirm §9 acceptance criteria 1–8 by inspection.

---

### Dependency order

Phase 0 → 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8. Phases 1 and 0 are independent and
could be done together; 2 depends on 1; 3 on 2; 4 on 3 (and 0 for
`excludeStudentId`); 5 on 4; 6 on 5; 7 on 6; 8 on 7. Do not start Phase 6
(removals) until Phase 5 is verified — the old panel must stay functional until
the typeahead fully replaces it.

## 10b. Open decisions (all resolved)

| # | Decision | Resolution |
|---|---|---|
| 1 | Trigger path | Local `FluentSelect` wrapper (option B) — no shared `CodedValueDropdown` change |
| 2 | "(Relationship)" source | Per-mode: by-student shows it (from `StudentGuardianViewDto`); by-contact omits it |
| 3 | Old "By student/contact" tabs | Replaced by the far-right radio on the typeahead input; Student mode ~~keeps the two-step flow~~ (§11 superseded: now single-step) |
| 4 | Cleanup of the old panel | Remove the `Existing` panel + dead state; trim the enum |

## 10c. Phase completion log

Snapshot of what has shipped against the §10 phase list. Each phase gets a row
when its implementation is complete and the build is green; update the row in
place rather than appending new versions. The "Notes" column captures
deviations from the phase spec — usually defensive pull-forwards (e.g. a guard
or predicate completed earlier than planned so the dialog stays usable across
phases) or small renames.

### Status

| Phase | Status | Build | Notes |
|---|---|---|---|
| 0 — Wire `StudentId` parameter | ✅ Done | 0 errors | — |
| 1 — Models, sentinel, state | ✅ Done | 0 errors | — |
| 2 — Local `FluentSelect` wrapper | ✅ Done | 0 errors | §2.1 sentinel guard pulled forward into `AddInlineGuardianAsync` (originally Phase 5); Add-disabled predicate left at the Phase 1 free-text form until Phase 3 |
| 3 — Typeahead UI + far-right radio | ✅ Done | 0 errors | §6.3 full Add-disabled predicate pulled forward (originally Phase 5) so the sentinel mode stays usable before search results land. **Revised to §11 single-FluentAutocomplete (see §10d) — Phases 3′/4′/5′ shipped as one rewrite; the FluentTextField + manual result-list markup and the debounce/CTS routing are deleted in favour of `ImmediateDelay="300"` + `OnOptionsSearch` + a union-free `GuardianSearchRow` (Contact mode: guardian-name search; Student mode: student-name → their guardians, single-step, N+1 bounded by `Take(10)`).** |
| 4 — Search handlers + debounce + dup filter | ✅ Done | 0 errors | Input binding switched to explicit `Value`/`ValueChanged` (not `@bind-Value` + `Task.Delay`) to own the debounce; radio wired with `@bind-Value:after` per the `CodedValueDropdown` pattern. **Superseded by §11** — debounce is component-level (`ImmediateDelay="300"`); handler-internal CTS keeps the cancel-on-supersede guarantee; `OnTypeaheadSearchAsync` is the single switch on `_searchMode` → `SearchContactRowsAsync` / `SearchGuardiansByStudentNameRowsAsync`. |
| 5 — Selection → draft, sentinel guard | ✅ Done | 0 errors | Rolled in the Phase-4 review's Finding 1 (typeahead state-reset on dropdown transitions) via a `ClearTypeaheadState()` helper; `GuardianSearchRow` extended with prefill source fields (FirstName/LastName/TitleCodedValueId/RelationshipCodedValueId) appended after the §4.1 display positions. **Revised to §11** — `OnTypeaheadSelectedAsync` is the single-branch pick (every row is a guardian); prefill contract preserved. |
| 6 — Removals + enum trim | ✅ Done | 0 errors | Deleted the `<FluentButton>Add existing</FluentButton>`, the entire `else if (_panelMode == GuardianPanelMode.Existing)` block (~8 KB), and dead state/methods (`StartAddExisting`, `SwitchExistingTab`, `OnSelectedRowsChanged`, `OnContactSearchAsync`, `OnStudentSearchAsync`, `SelectStudentAsync`, `BackToStudentSearch`, `AddSelectedGuardiansAsync`, `record GuardianPickerRow`, `enum ExistingTab`, `_existingTab`, `_pickerRows`, `_selectedRows`, `_studentResults`, `_selectedStudentId`, `_contactLoading`, `_studentLoading`, `PickerGridSettings`, `StudentGridSettings`). `enum GuardianPanelMode` trimmed to `{ List, Edit }`. `CancelPanel` updated to remove clears for deleted fields. **The §11 single-component rewrite made this phase larger** (≈14 KB net deletion). |
| 7 — Tests | ✅ Done | 11 new pass / 360 total | Added `StudentFormFieldsGuardianTypeaheadTests.cs` (11 tests, model-level per §9.4 + §11.7): WardLabel formatting (null/0/1/3), GuardianSearchRowComparer (same-Id-equal / different-Id / null-safe / GetHashCode), sentinel stability + exact-Guid pin, RelationshipOptionKind enum completeness, GuardianSearchRow record arity (11 fields). Test types exposed via `InternalsVisibleTo "SchoolCollab.Admin.Tests.Unit"` on the Application csproj. |
| 8 — Build + visual check | ✅ Done | 0 errors / 0 warnings | `dotnet build` (Application + Tests + API) → 0 errors, 0 warnings. **360 / 360 tests pass.** §9.2 divider render is structural-markup verified (the `<hr>` inside `<FluentOption>`); the runtime visual needs human sign-off in a browser per §11.8. The **§11 single-component rewrite supersedes the "two-step student re-open" runtime risk** (per §11.2): Student mode is now single-step — typing a student name lists their guardians directly, no popup re-open needed. **Remaining manual checks** (runtime): keyboard nav (↑/↓/Enter/Esc) on the FluentAutocomplete; the two-line row template rendering; the N+1 latency under realistic student-match counts. |

### Phase 4 — implementation notes

**Files touched:**
- `src/Students/SchoolCollab.Students.Application/Components/Students/StudentFormFields.razor` — only this file (markup + `@code`).

**Markup:**
- Typeahead input: `Value="_searchQuery" ValueChanged="@OnSearchChanged"` (no `@bind-Value`). The input stays controlled (`_searchQuery` updates synchronously), but the handler owns the debounce.
- Radio: `@bind-Value="_searchMode" @bind-Value:after="OnSearchModeChanged"` — Blazor's `:after` fires after the bind commits, matching the `CodedValueDropdown` `@bind-SelectedId:after` pattern. Re-runs the search immediately on toggle if the query is ≥ 2 chars.
- Results block expanded from the Phase 3 placeholder into three sub-states:
  - Student step-1: prompt (when query empty) → loading ring → "No matches." → student rows (`FirstName LastName` + `StudentNumber`).
  - Student step-2: Back button + picked-student context (Phase 3) → loading ring → "No matches." → guardian rows.
  - Contact mode: same as Student step-2 without the Back row.

**Code:**
- `private StudentDto[] _studentSearchResults = [];` — distinct from the old-panel's `_studentResults` (Phase 6 deletes that field).
- `OnSearchChanged(string? value)` — sets `_searchQuery`, cancels the prior CTS, debounces 300 ms (`Task.Delay(300, ct)`), then dispatches via `DispatchSearchAsync(q, ct)`. Min 2 chars (matches `EntityGrid`'s internal cadence per §9.1); below the threshold, clears results and cancels without firing. A subsequent keystroke supersedes the pending delay by throwing `OperationCanceledException`, which the catch swallows.
- `OnSearchModeChanged()` (parameterless void, `:after` target) → fire-and-forget `OnSearchModeChangedAsync()` per the `CodedValueDropdown` pattern. Clears BOTH lists so the previous mode's rows don't linger for a frame.
- `DispatchSearchAsync(q, ct)` — routes to `SearchContactAsync` or `SearchStudentStepAsync` based on `_searchMode`. Swallows `OperationCanceledException`.
- `SearchContactAsync(q, ct)` — `Api.ListGuardiansAsync(ct, search: q, excludeStudentId: StudentId)` → §6.1 client-side `DraftedGuardianIds()` dup filter → map to `GuardianSearchRow` with `RelationshipName: null` (§3.2) and `WardCount: g.StudentCount` (already client-enriched by `EnrichGuardiansAsync`). Capped at 30 rows.
- `SearchStudentStepAsync(q, ct)` — `Api.ListStudentsAsync(ct, search: q)` → exclude current `StudentId` (§6.2 — matches the user's mental model of "find one of MY other students' guardians" better than the §6.1 draft filter). Capped at 30.
- `LoadGuardiansForStudentAsync(studentId, ct)` — `Api.ListGuardiansByStudentAsync(studentId, ct)` → ensures any new `RelationshipCodedValueId` is loaded into `_relNames` via `EnsureRelNameAsync` → maps to `GuardianSearchRow` with `RelationshipName` resolved → bulk-loads ward counts via `Api.ListStudentCountsByGuardiansAsync` (failure is non-fatal; null renders "—" via `WardLabel`). Capped at 30.
- `OnStudentPickedAsync(StudentDto student)` — sets `_pickedStudent`, cancels any in-flight step-1 search, then loads that student's guardians.
- `DraftedGuardianIds()` — `HashSet<Guid>` of `Model.GuardianLinks.Where(g => g.ExistingGuardianId is {}).Select(...)`. The §6.1 client-side dup filter. Server-side `excludeStudentId` covers already-linked guardians; this catches drafted-but-not-yet-saved ones.
- `FormatGuardianName(titleName, firstName, lastName, displayName)` — title + first + last, trimmed; falls back to DisplayName; falls back to FirstLast. Mirrors the existing card head-line format.

**Deviations from the phase spec:**
- **Input binding:** spec said `OnSearchChanged` updates `_searchQuery` and then debounces — implemented with explicit `Value`/`ValueChanged` (not `@bind-Value`) so the component fully owns the debounce. `@bind-Value` would re-fire on every keystroke through Blazor's binder; the explicit form keeps the same semantics but makes the intent clearer.
- **Student search filter:** spec mentioned "reuse `GuardianSearchRow` with `FullName = student.FullName`" — implemented a separate `StudentDto[] _studentSearchResults` array because student rows render differently (no relationship label, show student number) and the spec's "reuse" path would have crowded the existing `GuardianSearchRow` model with student-specific fields. The §6.1 draft filter is also unnecessary on students (we're picking a student, not a guardian).
- **Loading + empty states:** added in Phase 4 (not Phase 3 as planned) so the live search experience is complete on its own. Phase 3 deliberately left them out so its scope stayed markup + CSS.

**Carry-forward into Phase 5:**
- `_pickedStudent` is set/cleared but `PickExistingGuardianAsync(GuardianSearchRow)` is still the `Task.CompletedTask` stub from Phase 3.
- `_existingGuardianId`, `_pickedRelationshipId`, `_pickedTitleId` are still unassigned (Phase 5 sets them).
- The §2.1 guard from Phase 2 is still resolving `relationshipForDraft` to null in existing mode (since `_pickedRelationshipId` is null). Phase 5 makes the resolution meaningful for both by-student (auto) and by-contact (user picks).

### Phase 5 — implementation notes

**Files touched:**
- `src/Students/SchoolCollab.Students.Application/Components/Students/StudentFormFields.razor` — only this file.

**`GuardianSearchRow` extension (deviation from §4.1):**
The §4.1 record shape `(Id, FullName, RelationshipName, WardCount)` carries only display fields, but §5's prefill needs the source first/last/title/relationship-id. Rather than refetch the guardian on pick (a second API round-trip), the record is extended with four trailing fields: `FirstName`, `LastName`, `TitleCodedValueId`, `RelationshipCodedValueId`. The four §4.1 positions stay stable (appended, not inserted) so the documented display order and any positional callers are unaffected; the markup only reads `FullName`/`RelationshipName`/`WardCount`. Phase 7 does not pin `GuardianSearchRow`'s shape (it pins `RelationshipOption` + `WardLabel` + the disabled predicate), so the extension is test-safe.
- `SearchContactAsync` now passes `FirstName: g.FirstName, LastName: g.LastName, TitleCodedValueId: g.TitleCodedValueId, RelationshipCodedValueId: null` (by-contact relationship is null per §3.2 — the unscoped list carries no link-level relationship).
- `LoadGuardiansForStudentAsync` now passes `FirstName: l.FirstName, LastName: l.LastName, TitleCodedValueId: l.TitleCodedValueId, RelationshipCodedValueId: l.RelationshipCodedValueId`.

**`PickExistingGuardianAsync(row)` — body filled:**
- Sets `_existingGuardianId = row.Id`; prefills `_newFirstName`/`_newLastName` from the row (so `AddInlineGuardianAsync`'s required-name guard passes even though the fields are hidden in existing mode); sets `_pickedTitleId = row.TitleCodedValueId`.
- **By-student with a relationship** (`_searchMode == Student && row.RelationshipCodedValueId is { } relId`): sets `_pickedRelationshipId = relId`, `_newRelId = relId` (no longer the sentinel — the §2.1 guard now resolves to it directly), and syncs `_selectedRelOption` to the matching real relationship option (dropdown shows e.g. "Father"). Existing mode STAYS true so the typeahead remains visible with the pick.
- **By-contact, or by-student with no link relationship** (else): leaves `_newRelId` on the sentinel and `_pickedRelationshipId = null` — the user picks a relationship in the dropdown after the guardian (§3.2), or stays null for a valid "no relationship" link.

**`OnRelationshipOptionChanged` — by-contact relationship-pick fix:**
The Phase 2 handler exited existing mode unconditionally on a real-relationship pick. That breaks the §3.2 by-contact flow: once a guardian is picked, the user must pick a relationship from the dropdown, but selecting one would discard the pick and hide the typeahead. Fixed by branching on `_existingGuardianId`:
- `_existingGuardianId is null` → free-text new-guardian flow: `_inExistingMode = false` + `ClearTypeaheadState()` (the Finding 1 reset).
- `_existingGuardianId is not null` → keep existing mode + the typeahead; just swap the sentinel for the real relationship.

**Finding 1 (rolled in) — `ClearTypeaheadState()` helper:**
Cancels `_searchCts` and clears `_searchRows`/`_studentSearchResults`/`_searchQuery`/`_searchLoading`/`_pickedStudent`. Deliberately does NOT touch the add-row selection (`_inExistingMode`/`_selectedRelOption`/`_newRelId`/`_existingGuardianId`/`_pickedRelationshipId`/`_pickedTitleId`/`_newFirstName`/`_newLastName`) — that's add-row state reset separately. Called from four sites:
1. `OnRelationshipOptionChanged` sentinel case (replaces the partial `_searchRows = []; _searchQuery = null;` — now also clears `_studentSearchResults`/`_pickedStudent` and cancels the CTS).
2. `OnRelationshipOptionChanged` relationship case (free-text transition only).
3. Post-add reset in `AddInlineGuardianAsync` (existing mode).
4. `CancelPanel` (ephemeral search session resets; add-row selection persists per the existing "add-row fields persist" comment).

**`AddInlineGuardianAsync` — §2.1 guard completed + `ExistingGuardianId`/title wired:**
- `GuardianAssignment` first arg changed from `null` to `ExistingGuardianId: _existingGuardianId` (null on the free-text path, the picked id on the existing path — `AddDraftAsync` no-ops on a duplicate `ExistingGuardianId`, §6.1).
- `TitleCodedValueId: null` → `TitleCodedValueId: _pickedTitleId` (null on the free-text path, which has no inline title picker).
- `relationshipForDraft` guard unchanged (sentinel → `_pickedRelationshipId`, else `_newRelId`).
- **Post-add reset (existing mode only):** after `AddDraftAsync`, when `_inExistingMode`, reset `_inExistingMode = false`, `_existingGuardianId = null`, `_pickedRelationshipId = null`, `_pickedTitleId = null`, `_selectedRelOption = null` (dropdown back to placeholder — the sentinel is a trigger, not a real relationship, so it does not persist like a free-text relationship selection), and `ClearTypeaheadState()`. The free-text path's existing `_newFirstName`/`_newLastName`/`_newRelId` clears remain.

**`CancelPanel`:** added `ClearTypeaheadState()` — cancels any in-flight search and clears the transient search session. The add-row selection persists (consistent with the existing "inline add-row fields are intentionally NOT cleared" comment).

**Deviations from the phase spec:**
- **`GuardianSearchRow` extension** — the spec's §4.1 record is display-only; §5's prefill needs source fields. Extended the record (appended, not inserted) rather than refetching on pick. Documented here and in the record's own doc comment.
- **By-contact relationship pick keeps existing mode** — the spec's §3.2 says "the user picks the relationship in the dropdown after selecting the guardian" but did not address that `OnRelationshipOptionChanged` would exit existing mode. The `_existingGuardianId is null` branch fixes this; it's a necessary consequence of §3.2 that the spec left implicit.
- **Post-add resets `_selectedRelOption = null`** — the spec listed the existing-mode field resets but not the dropdown option. Resetting it returns the dropdown to placeholder (the sentinel is a trigger and should not persist after a successful add); the free-text path keeps its existing persist-the-real-relationship behaviour.
- **`CancelPanel` scope** — interpreted "reset the new existing-mode fields" as the ephemeral search session only (not the add-row selection), to stay consistent with the existing "add-row fields persist" comment. The pick (`_existingGuardianId`) survives panel navigation; the search results do not.

**Build verification:** `dotnet build` (Application) → 0 errors, 16 warnings, **none in `StudentFormFields`** — every Phase 1–4 "field assigned but never used" warning is now resolved (`_pickedTitleId` is read by the assignment; `_searchLoading`/`_searchQuery`/`_existingGuardianId`/`_pickedRelationshipId`/`_pickedStudent`/`_studentSearchResults` all read). Test project → 0 errors.

**Carry-forward into Phase 6:**
- The old "Add existing" button + `GuardianPanelMode.Existing` panel + `StartAddExisting`/`AddSelectedGuardiansAsync`/`GuardianPickerRow`/`_pickerRows`/`_selectedRows`/`_studentResults`/`_selectedStudentId`/`ExistingTab` are all still live and unused-by-the-new-path. Phase 6 deletes them. `CancelPanel` still clears the old fields (the `_pickerRows`/`_selectedRows`/`_studentResults`/`_selectedStudentId` lines) — Phase 6 removes those clears when the fields are deleted.
- No `GuardianPickerRow` references remain outside the old panel (confirm with `grep` as Phase 6's first step).

## 11. Revision — switch the typeahead to `FluentAutocomplete` (Path B, single component)

> **Status:** Proposed revision. Supersedes the `FluentTextField`-based typeahead in §§3–5 and the §9.1/§9.4 verification notes. §§1–2 (trigger, sentinel, divider) are UNCHANGED — the local `FluentSelect` relationship dropdown + sentinel + divider stay exactly as implemented in Phases 0–2. Phase 6 (removals) and Phase 7 (tests) stand; their targets shift as noted below.
>
> **Why revise:** the Path-A `<FluentTextField>` + custom `<button>` result list has **no keyboard navigation and no ARIA combobox semantics** — a real accessibility gap (§9.5, new). `FluentAutocomplete` (4.14.2) gives debounce + popup + keyboard nav + ARIA + loading/empty/clear states for free.
>
> **Single component, no two-step:** `OnOptionsSearch` is the switch point. **Student mode is no longer a two-step pick-student-then-pick-guardian flow** (§3.1 revised) — instead the user types a **student name** and the results list **guardians of matching students directly**. One `FluentAutocomplete`, one `TOption` (all rows are guardians), no `_pickedStudent` step-2 state, no popup re-open hack.

### 11.1 What stays from Phases 0–2, 5

- The local `FluentSelect` relationship dropdown, the sentinel (`ExistingGuardianSentinel`), the divider, `RelationshipOption`/`RelationshipOptionKind`, `BuildRelationshipOptionsAsync`, `OnRelationshipOptionChanged` (sentinel → `_inExistingMode`), and the §2.1 sentinel guard — **all unchanged**.
- `AddInlineGuardianAsync` §2.1 guard + `ExistingGuardianId: _existingGuardianId` + `TitleCodedValueId: _pickedTitleId` wiring (Phase 5) — **unchanged**.
- The Add-button §6.3 disabled predicate (Phase 3) — **unchanged**.
- `CancelPanel` + post-add existing-mode resets (Phase 5) — **unchanged** (the fields they clear shift, see 11.5).

### 11.2 The component — single `FluentAutocomplete`, guardian rows in both modes

Replace the `<FluentTextField>` typeahead + the manual result-list `<button>`s + the manual `<FluentProgressRing>`/"No matches." markup with **one** `<FluentAutocomplete>`:

```razor
<FluentAutocomplete TOption="GuardianSearchRow"
                    @ref="_typeahead"
                    Multiple="false"
                    MaximumOptionsSearch="30"
                    ImmediateDelay="300"
                    ShowProgressIndicator="true"
                    AutoComplete="off"
                    Placeholder="Search by guardian or student name…"
                    OptionText="@(r => r.FullName)"
                    OptionComparer="@GuardianSearchRowComparer.Instance"
                    OptionTemplate="@RowTemplate"
                    OnOptionsSearch="@OnTypeaheadSearchAsync"
                    SelectedOptionChanged="@OnTypeaheadSelectedAsync"
                    Style="width: 200px"
                    class="guardian-typeahead" />
```

**Row type** (extends the Phase-5 `GuardianSearchRow` with a matched-student sub-line for Student mode; still one `TOption`, all rows are guardians):

```csharp
private sealed record GuardianSearchRow(
    Guid Id,
    string FullName,                  // "Mr. John Smith"
    string? RelationshipName,         // by-student only (§3.2); null by-contact
    int? WardCount,                   // green +N wards far-right
    // Phase 5 prefill source:
    string FirstName,
    string LastName,
    Guid? TitleCodedValueId,
    Guid? RelationshipCodedValueId,   // by-student: from the link; by-contact: null
    // §11 Student-mode match context (null in Contact mode):
    string? MatchedStudentName,       // "Alice Smith"
    string? MatchedStudentNumber);    // "STU-1234"

// Equality by Id so a re-search doesn't drop the selected guardian
// (each OnOptionsSearch returns fresh record instances). NOTE: a guardian
// linked to two matching students yields two rows (different relationship /
// match context) — that's intended; the user picks the specific link.
private sealed class GuardianSearchRowComparer : IEqualityComparer<GuardianSearchRow>
{
    public static readonly GuardianSearchRowComparer Instance = new();
    public bool Equals(GuardianSearchRow? x, GuardianSearchRow? y) =>
        x is null ? y is null : y is not null && x.Id == y.Id;
    public int GetHashCode(GuardianSearchRow obj) => obj.Id.GetHashCode();
}
```

**`OptionTemplate` — two-line row** (guardian above, matched student below):

```razor
<OptionTemplate>
    <div class="guardian-search-row">
        <div class="guardian-search-primary">
            <span class="guardian-search-name">@context.FullName</span>
            @if (context.RelationshipName is { } rel)
            {
                <span class="guardian-search-rel">(@rel)</span>   @* by-student only (§3.2) *@
            }
            <span class="guardian-search-wards">@WardLabel(context.WardCount)</span>  @* green, far-right *@
        </div>
        @if (context.MatchedStudentName is { } stu)
        {
            <div class="guardian-search-student">
                @stu
                @if (context.MatchedStudentNumber is { } num)
                {
                    <span class="muted"> #@num</span>
                }
            </div>   @* muted, the matching ward *@
        }
    </div>
</OptionTemplate>
```

### 11.3 `OnOptionsSearch` — the single switch point (replaces Phase 4 handlers)

One async handler switches on `_searchMode`:

```csharp
private async Task OnTypeaheadSearchAsync(OptionsSearchEventArgs<GuardianSearchRow> e)
{
    var q = (e.Text ?? string.Empty).Trim();
    if (q.Length < 2) { e.Items = []; return; }      // §9.1 min 2 chars

    // Cancellation: OptionsSearchEventArgs has no CancellationToken, so we
    // keep our own CTS and discard stale results before assigning e.Items.
    _searchCts?.Cancel(); _searchCts = new(); var ct = _searchCts.Token;
    try
    {
        e.Items = _searchMode == SearchMode.Contact
            ? await SearchContactRowsAsync(q, ct)
            : await SearchGuardiansByStudentNameRowsAsync(q, ct);
        if (ct.IsCancellationRequested) e.Items = [];
    }
    catch (OperationCanceledException) { e.Items = []; }
}
```

- **`SearchContactRowsAsync(q, ct)`** — `Api.ListGuardiansAsync(ct, search: q, excludeStudentId: StudentId)` → §6.1 `DraftedGuardianIds()` dup filter → `GuardianSearchRow(FullName, RelationshipName: null, RelationshipCodedValueId: null, MatchedStudentName: null, …)` (§3.2 by-contact omits relationship + student context). Ward counts from the client-enriched `GuardianDto.StudentCount`. Capped at 30.

- **`SearchGuardiansByStudentNameRowsAsync(q, ct)`** — the Student-mode path. **Searches students, lists their guardians directly** (no two-step):
  1. `Api.ListStudentsAsync(ct, search: q)` → exclude the current `StudentId` (§6.2) → `Take(10)` (bounded to cap the N+1).
  2. For each matching student `s`, `Api.ListGuardiansByStudentAsync(s.Id, ct)` — **parallelized via `Task.WhenAll`** to keep latency down.
  3. Flatten: for each `(student, guardian-link)`, build `GuardianSearchRow(FullName, RelationshipName: resolved from `l.RelationshipCodedValueId`, RelationshipCodedValueId: l.RelationshipCodedValueId, MatchedStudentName: $"{s.FirstName} {s.LastName}", MatchedStudentNumber: s.StudentNumber, …)`.
  4. §6.1 dup filter: drop rows whose `GuardianId` is in `DraftedGuardianIds()`.
  5. Bulk ward counts via `Api.ListStudentCountsByGuardiansAsync(distinct guardian ids, ct)`.
  6. Capped at 30 rows. A guardian linked to two matching students appears twice (different relationship / match context) — the user picks the specific link.

  **N+1 note:** no bulk "guardians by student-ids" endpoint exists today (`ListGuardiansByStudentAsync` is single-student; bulk variants exist only for enrollments and guardian-counts). The `Take(10)` + `Task.WhenAll` cap keeps this acceptable for a typeahead. A future server-side `GET /guardians?wardSearch={q}` endpoint (§8 allows StudentsApiClient changes when needed) could collapse it to one call — noted as a follow-up, not required for ship.

The debounce is `ImmediateDelay="300"` (component-level); the CTS is handler-level. The `_debounce`/`Task.Delay`/`DispatchSearchAsync`/`OnSearchModeChangedAsync` plumbing from Phase 4 is **deleted**. The radio `@bind-Value:after` becomes a simple `_selectedRow = null;` clear (mode switch just changes what the next search returns; no step state to reset).

### 11.4 Selection → draft (replaces Phase 5 `PickExistingGuardianAsync`)

`SelectedOptionChanged` — every row is a guardian, so there's one branch (no student-pick branch):

```csharp
private async Task OnTypeaheadSelectedAsync(GuardianSearchRow? row)
{
    if (row is null) return;
    _existingGuardianId = row.Id;
    _newFirstName = row.FirstName;
    _newLastName = row.LastName;
    _pickedTitleId = row.TitleCodedValueId;
    if (_searchMode == SearchMode.Student && row.RelationshipCodedValueId is { } relId)
    {
        // By-student: the link's relationship is known — prefill + sync dropdown.
        _pickedRelationshipId = relId;
        _newRelId = relId;
        _selectedRelOption = _relationshipOptions?.FirstOrDefault(o =>
            o.Kind == RelationshipOptionKind.Relationship && o.Id == relId);
    }
    else
    {
        // By-contact: relationship stays null — user picks in the relationship dropdown (§3.2).
        _pickedRelationshipId = null;
    }
}
```

The field shows the picked guardian's `FullName` (single-select display) + the built-in × clear (`OnClearAsync` → `SelectedOption = null`, which we handle to reset `_existingGuardianId`/`_pickedRelationshipId`). The relationship dropdown (separate `FluentSelect`) still shows the sentinel in by-contact mode; the user picks "Father" there → `OnRelationshipOptionChanged` keeps `_inExistingMode` (Phase 5 `_existingGuardianId is not null` branch) → Add links.

### 11.5 What gets deleted / changed vs the Path-A code

**Deleted** (Path A, Phases 3–4):
- `<FluentTextField Value=... ValueChanged=OnSearchChanged>` + the entire `.guardian-search-block` results markup (student rows, guardian rows, loading ring, "No matches." prompts, Back button).
- `OnSearchChanged`, `OnSearchModeChanged`/`OnSearchModeChangedAsync`, `DispatchSearchAsync` (debounce routing).
- `SearchContactAsync`, `SearchStudentStepAsync`, `LoadGuardiansForStudentAsync`, `OnStudentPickedAsync` (folded into `SearchContactRowsAsync` + `SearchGuardiansByStudentNameRowsAsync`).
- `_studentSearchResults`, **`_pickedStudent`** (no two-step → no step-2 state), `_searchRows` (results now live in the component's `Items`).
- `.guardian-typeahead`/`.guardian-search-block`/`.guardian-search-results`/`.guardian-search-row`/`.guardian-search-loading`/`.guardian-search-back`/`.guardian-search-picked` CSS (the popup is the component's own; only `.guardian-search-name`/`.guardian-search-rel`/`.guardian-search-wards`/`.guardian-search-primary`/`.guardian-search-student` stay for `OptionTemplate`).

**Kept / renamed:** `_searchCts` (moves inside `OnTypeaheadSearchAsync`), `_searchMode`/`SearchMode`, `DraftedGuardianIds`, `FormatGuardianName`, `WardLabel`, `EnsureRelNameAsync`/`_relNames`, `ClearTypeaheadState` (now clears `_selectedRow` + cancels `_searchCts`; no `_pickedStudent` to clear).

### 11.6 Revised acceptance criteria (§9 addendum)

- **9.5 (new) Accessibility:** the typeahead is a `role="combobox"` with keyboard navigation (↑/↓ to move, Enter to pick, Esc to close) and `aria-expanded`/`aria-controls` — provided by `FluentAutocomplete`. **This is the primary reason for the revision**; the Path-A `<button>` list had none of it.
- **3.1 (revised):** Student mode is **single-step** — type a student name → guardians of matching students are listed directly (two-line rows: guardian above, matched student below). The old two-step (search students → pick → load that student's guardians) is removed.
- 9.1 (debounce): now `ImmediateDelay="300"` + the handler-internal CTS (still min 2 chars).
- 9.2 (divider): unchanged (the relationship `FluentSelect` divider is unaffected).
- 9.3 (sentinel stability): unchanged.
- 9.4 (bUnit): still test the MODEL — `RelationshipOption[]` (sentinel + divider + relationships), `WardLabel`, the Add-disabled predicate, and now `GuardianSearchRowComparer` + the `OnTypeaheadSearchAsync` switch logic (mock `Api`, assert `e.Items` carry `MatchedStudentName` in Student mode and `RelationshipName` in Student mode only). Do NOT assert the `FluentAutocomplete` web-component popup DOM.
- 4 (row format): two-line — `FullName (Relationship) +N wards` above, matched student `Name #Number` below (Student mode only). `(Relationship)` still by-student-only (§3.2).
- 5 (selection → draft): `OnTypeaheadSelectedAsync` preserves the Phase-5 prefill contract (`_existingGuardianId` + names + title + by-student relationship auto-prefill + by-contact null).

### 11.7 Migration impact on the phase plan

- **Phases 0–2:** no rework (sentinel/divider/dropdown/guard stand).
- **Phase 5 pick flow:** `PickExistingGuardianAsync` body moves into `OnTypeaheadSelectedAsync` (one branch — no student-pick branch); logic preserved.
- **Phases 3–4:** rewrite. New Phase 3′ = `FluentAutocomplete` markup + `GuardianSearchRow` extension (match fields) + `OptionTemplate` (two-line) + `GuardianSearchRowComparer`. New Phase 4′ = `OnTypeaheadSearchAsync` + `SearchContactRowsAsync` + `SearchGuardiansByStudentNameRowsAsync` (N+1 with `Take(10)` + `Task.WhenAll`) + CTS-in-handler. **Net code: smaller** (no manual debounce/results/loading/empty markup, no `_pickedStudent`/Back-button state) and **no `FocusAsync()` re-open risk** (single-step — no popup re-open needed).
- **Phase 6 (removals):** unchanged.
- **Phase 7 (tests):** `RelationshipOption`/`WardLabel`/disabled-predicate tests unchanged; add `GuardianSearchRowComparer` + `OnTypeaheadSearchAsync` switch tests (Student-mode rows carry match context + relationship; Contact-mode rows don't).
- **Phase 8 (visual/acceptance):** keyboard-nav check (↑/↓/Enter/Esc); Student-mode two-line row render; Student-mode N+1 latency check under a realistic student-match count.

### 11.8 Open item — N+1 Student-mode search (no runtime risk, just latency)

`SearchGuardiansByStudentNameRowsAsync` does `1 + N` calls (`ListStudentsAsync` + `N × ListGuardiansByStudentAsync`) where N ≤ 10 (the `Take(10)` cap). `Task.WhenAll` parallelizes the N. This is the one cost of the single-component "search guardians by student name" approach — acceptable for a typeahead, and collapsible to a single server call later via a `GET /guardians?wardSearch={q}` endpoint (§8 permits StudentsApiClient changes when needed; not required for ship). Unlike the earlier `FocusAsync()` re-open risk, this is a **latency consideration, not a correctness risk** — there is no re-entrancy or popup-state hazard.

