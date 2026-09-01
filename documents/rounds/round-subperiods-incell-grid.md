# Round: subperiods-incell-grid

Provider: pi (models: glm-5.3-flash, deepseek-v4-flash, kimi-k2.7-code, minimax-m3)
Tier: 3 (UI round — `.razor` + `.razor.css` changed)
Round base: `40f11205` (main, clean tree at start)

## Plan

**Goal.** Migrate `PeriodSubPeriodsEditor`'s **edit-mode** sub-period list from the
hand-rolled `role="table"` markup to a `FluentDataGrid` with `TemplateColumn`
**in-cell editing**. Create mode (in-memory definition rows, `YearId == null`) is
**out of scope** and stays as-is.

**Why.** The library has no built-in in-cell grid editing (verified against
`microsoft/fluentui-blazor` v4.14.2 source — no edit API, no edit demo). The
idiomatic FluentUI pattern is `FluentDataGrid` + `TemplateColumn` with a per-row
edit state. The current component already does inline-row editing via a bottom
form swap; this round moves the edit surface **into the grid row** (in-cell).

**Scope — expected files.**
- `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodSubPeriodsEditor.razor`
  — replace the edit-mode `role="table"` list with `FluentDataGrid`; in-cell
  editing via `TemplateColumn`; keep the always-visible add row below the grid.
- `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodSubPeriodsEditor.razor.css`
  — remove now-dead list styles (`.subperiods-list`, `.subperiods-row*`,
  `.subperiods-name`, `.subperiods-actions` if unused); keep add-row + field
  styles. No dead CSS left.
- `tests/SchoolCollab.Admin.Tests.Unit/PeriodEditPageTests.cs` — update only if
  an assertion breaks; the section-presence and `title="Add sub-period"` tests
  must keep passing.

**Design (edit mode, `YearId != null`).**
- Render `FluentDataGrid TGridItem="PeriodDto" Items="_subs"` with `TemplateColumn`
  columns: **Name, Type, Start, End, Status, Actions**.
- Per-row edit state: when `p.Id == _editingId`, the Name/Start/End cells render
  `FluentTextField`/`FluentDatePicker` bound to the existing editing fields
  (`_name`, `_start`, `_end`); the Type cell renders `FluentSelect` (Term/Semester)
  only when `Kind` is empty (i.e. `Division` is None/unknown); Status cell shows
  the badge; Actions cell shows **Save**/**Cancel**.
- Non-editing rows: Name text, Type badge, Start/End text, Status badge, Actions
  cell shows **Edit** (and **Delete** when `p.Status == "Draft"`).
- Keep the always-visible add row (`.subperiods-inline-add`) below the grid for
  **new** sub-periods, with `Title="@(_editingId is null ? "Add sub-period" : "Save sub-period")"`
  preserved (test `SubPeriodsSection_InlineAddButton_HasTitle` depends on it).
- Preserve unchanged: `StartEdit`/`CancelEdit`/`SubmitAsync`/`OnDeleteAsync`
  logic, `_saving` disable, validation messages, error `FluentMessageBar`,
  loading `FluentProgressRing`, empty-state info bar, `OnChanged` firing after
  CRUD, Auto-split button + `CanAutoSplit`/`AutoSplitTitle` gating + confirmation
  dialog, `PeriodDeletePrompts.SubPeriodMessage` delete confirmation.

**Acceptance criteria.**
- AC1: Edit-mode sub-periods render in a `FluentDataGrid` (not the old
  `role="table"`).
- AC2: Clicking Edit switches that row to in-cell inputs (Name/Start/End; Type
  select when `Kind` empty); Save persists via `UpdatePeriodAsync`, Cancel
  reverts; validation messages still surface.
- AC3: Non-editing rows show text/badges + Edit (and Draft-only Delete).
- AC4: Add row below the grid still creates via `CreatePeriodAsync` and keeps
  `title="Add sub-period"`.
- AC5: Auto-split button + gating + confirmation unchanged.
- AC6: `OnChanged` fires after CRUD; `_saving` disables during save; error bar,
  spinner, empty state preserved.
- AC7: No dead CSS; repo skills honored (`blazor-css-isolation`, `fluentui-*`).
- AC8: `dotnet build SchoolCollab.sln` 0 errors; `SchoolCollab.Admin.Tests.Unit`,
  `SchoolCollab.Students.Tests.Unit`, `SchoolCollab.ArchitectureTests.Unit` pass.

## Worker Report

- **Changed files:** `PeriodSubPeriodsEditor.razor`, `PeriodSubPeriodsEditor.razor.css`, `PeriodEditPageTests.cs`
- **Build:** 0 errors (`dotnet build SchoolCollab.sln -c Debug --nologo -v q`)
- **Tests:** Admin.Tests.Unit 518/0; Students.Tests.Unit 394/0; ArchitectureTests.Unit 20/0
- **Deviations from plan:** The add row is hidden while an in-cell edit is active (plan said "always-visible add row"). Rationale: the add row and the in-cell inputs bind to the same shared fields (`_name`/`_start`/`_end`), so keeping both visible would render two identical input sets. The `title="Add sub-period"` test still passes (initial render, not editing).

## Review

- **Reviewer model note:** the configured reviewer (`ollama/kimi-k2.7-code:cloud`) timed out twice (30 min each, no output) and was unavailable in this environment; the **parent performed the static review** (authoritative verifier) against the plan + diff + changed files.
- **Verdict: PASS** (add-row deviation accepted)
- **P1:** none
- **P2:** none
- **Best-practices:** no overwrites (diff touches only the 3 planned files, edit-mode list + CSS + one test; no unrelated changes) / skills honored (blazor-css-isolation: scoped `.razor.css`, no inline `<style>`; fluentui-*: `FluentDataGrid`/`TemplateColumn`/`FluentBadge`/`FluentButton` used correctly) / readable (focused diff, no dead CSS — removed `.subperiods-list`/`.subperiods-row*`/`.subperiods-row--head`, kept `.subperiods-row--editing`/`.subperiods-name`/`.subperiods-actions` which are still referenced, added `.subperiods-cell-*`).
- **Deviation assessment:** hiding the add row during in-cell edit is **accepted** — the add row and in-cell inputs share `_name`/`_start`/`_end`, so both visible would duplicate the input set; the add row still creates via `CreatePeriodAsync` and keeps `title="Add sub-period"` when not editing (AC4 holds). In-cell Save calls `SubmitAsync` → `UpdatePeriodAsync` when `_editingId != null`; add-row Add calls the same `SubmitAsync` → `CreatePeriodAsync` when null; only one is visible at a time, so no conflict.
- **API surface confirmed:** `FluentDataGrid` `RowClass`/`GridTemplateColumns`/`GenerateHeader` compile clean (0 errors).

## Acceptance

- **Verdict: CLOSED** (all ACs met, no P1s)
- **AC1 met** — diff replaces the `role="table"` list with `FluentDataGrid TGridItem="PeriodDto" Items="_subs.AsQueryable()"` + 6 `TemplateColumn`s.
- **AC2 met** — `p.Id == _editingId` cells render FluentTextField/FluentDatePicker (+FluentSelect when `Kind` empty); Save → `SubmitAsync` → `UpdatePeriodAsync`, Cancel → `CancelEdit`; new bUnit test `SubPeriodsSection_InCellEdit_SaveCallsUpdate` proves Edit→inputs→PUT persist.
- **AC3 met** — non-editing rows render name text, Type badge, Start/End text, Status badge, Edit; Delete only when `context.Status == "Draft"`.
- **AC4 met (deviation accepted)** — add row below grid retained with `title="Add sub-period"` and `SubmitAsync` → `CreatePeriodAsync`; hidden while in-cell edit active (accepted: shares `_name`/`_start`/`_end`, duplicate input set otherwise); title test passes.
- **AC5 met** — no diff hunks touch auto-split markup/logic; gating + confirmation preserved.
- **AC6 met** — `OnChanged`/`ReloadAsync` path untouched; `_saving` Disabled binding on both Save and Add buttons; error `FluentMessageBar`, spinner, empty-state branches preserved.
- **AC7 met** — removed classes (`.subperiods-list`, `.subperiods-row`, `:nth-child(even)`, `--head`) have zero remaining references; kept `.subperiods-row--editing`/`.subperiods-name`/`.subperiods-actions` still used; scoped `.razor.css`, no inline styles.
- **AC8 met** — parent-authoritative: `dotnet build SchoolCollab.sln` 0 errors; Admin.Tests.Unit 518/0; Students.Tests.Unit 394/0; ArchitectureTests.Unit 20/0.

**UI-tester scope handover (verbatim):**
- **Period edit page `/students/periods/{Id:guid}/edit` (Edit.razor)** — hosts the changed `PeriodSubPeriodsEditor` in edit mode (`YearId != null`): the round's target — grid rendering, in-cell Edit/Save/Cancel, add row, auto-split, Draft delete.
- **Period create page `/students/periods/create` (Create.razor)** — shares the same changed component + changed `.razor.css` in create mode (`YearId == null`, out of round scope): regression-check that create-mode definition rows and the add row still render/styles intact.
- **Periods list page `/students/periods` (Periods.razor)** — navigation entry point to both surfaces above; verify links to create/edit still land on the pages hosting the changed editor.
- **ApiClient surface: `ListSubPeriodsAsync`, `UpdatePeriodAsync`, `CreatePeriodAsync`, `DeletePeriodAsync`** — the diff rewires only the UI layer over these calls (in-cell Save→Update, add row→Create, auto-split→Create loop, Draft delete→Delete + `PeriodDeletePrompts.SubPeriodMessage` confirmation); verify each round-trips and errors surface in the `FluentMessageBar`.

## UI Tester

- **Verdict: P2-only** (no P1s)
- **Scope ack:** hunted the 4 handed-over surfaces — edit page in-cell grid (render, Edit/Save/Cancel, add row, auto-split, Draft delete), create-page create-mode regression, list-page nav links, and all four ApiClient round-trips + FluentMessageBar error surface.
- **P1:** none
- **P2 (both fixed by parent, trivial):**
  - `PeriodSubPeriodsEditor.razor:137` — Actions `<TemplateColumn Title="">` rendered an empty column header (a11y). **Fixed → `Title="Actions"`.**
  - `PeriodSubPeriodsEditor.razor:178-179` — dead ternary in the add-row button (wrapped in `@if (_editingId is null)`, so the `Save sub-period`/`Save` branches were unreachable). **Fixed → literal `Title="Add sub-period"` + `@(_saving ? "Saving…" : "Add")`.**
- **Out-of-round observations (pre-existing, not in this round's diff):** `OnDeleteAsync` does not special-case 404 like Edit.razor's does; Auto-split button stays clickable while `_saving=true`; CSS defines `.subperiods-actions` twice (naming-collision risk).
- **Post-fix verification:** `dotnet build SchoolCollab.sln` 0 errors; Admin.Tests.Unit 518/0 (incl. `SubPeriodsSection_InlineAddButton_HasTitle` and `SubPeriodsSection_InCellEdit_SaveCallsUpdate`).

## Round verdict

**CLOSED — PASS.** Worker implemented the FluentDataGrid + TemplateColumn in-cell migration; parent authoritative build/test green (build 0 errors; Admin 518/0, Students.Unit 394/0, ArchitectureTests 20/0); static review PASS (add-row deviation accepted); orchestrator-accept CLOSED (all AC1–AC8 met); UI tester P2-only with both P2s fixed. Reviewer model (`kimi-k2.7-code`) and UI-tester model (`minimax-m3`) both timed out once and were recovered (parent static review; UI-tester resumed to verdict).
