# Spec: Period Upsert — single page for create & edit

> **Status:** r3 — decisions resolved; ready for implementation planning.
> **Scope:** Merge the period create and edit pages into ONE page
> (`PeriodUpsert`); keep `PeriodFormFields` + `PeriodSubPeriodsEditor` as the
> single source of truth; sub-periods section always visible (disabled at
> `None`); auto-split count user-configurable (prefilled from division);
> "Add sub-period" affordance in both modes.

---

## 1. Problem

`PeriodFormFields` is ALREADY the shared field-rows component used by both flows,
and `PeriodSubPeriodsEditor` is ALREADY the shared sub-periods section (create +
edit modes). The duplication is NOT in the components — it is in the two PAGES:

- `Create.razor` (`/students/periods/create`) — owns the tenant gate, page shell,
  action row, error bar, model mapping, `?parent=` handling, Suggest/Backfill,
  blocked-parent panel, and create submit (`CreatePeriodRequest` + in-memory
  sub-period definitions).
- `Edit.razor` (`/students/periods/{Id}/edit`) — owns the same shell, action row,
  error bar, model mapping, load/404 handling, Deactivate/Danger zone, and edit
  submit (`UpdatePeriodRequest`, live sub-period CRUD).

Every field/section change must be applied to both pages by hand (e.g. the
tolerance field, the division default, the layout fixes).

Observed gaps:

1. **Sub-periods section is gated by division** — toggling the division makes the
   section jump in/out of view.
2. **Auto-split count is hard-coded from division** (`Semesters ? 2 : 3`) — the
   user cannot change it.
3. **Edit mode has no "Add sub-period" button** (create mode has one).
4. **Two pages drift** — same fields/sections, kept in sync manually.

## 2. Goals

- **G1 — ONE page** (`PeriodUpsert.razor`) for create and edit; consumes the
  existing `PeriodFormFields` + `PeriodSubPeriodsEditor`.
- **G2 — Sub-periods section always visible** (disabled + empty at `None`).
- **G3 — Auto-split count user-configurable** (prefilled from division).
- **G4 — "Add sub-period" affordance in both modes.**
- **G5 — Zero new parallel components.**

## 3. Requirements

### 3.1 Single page — `PeriodUpsert.razor` (decided)

- **FR-1** — Merge based on `Edit.razor` (the more complete structure: load/404,
  lifecycle sections) with the create route and create-mode logic added; rename to
  `PeriodUpsert.razor`. Both routes on the one component:

  ```
  @page "/students/periods/create"
  @page "/students/periods/{Id:guid}/edit"
  ```

  `Id == Guid.Empty` ⇒ create mode. `Create.razor` and `Edit.razor` are deleted
  (no shims — no extra layers).
- **FR-2** — The page owns: tenant gate, page shell, `PeriodFormFields`,
  `PeriodSubPeriodsEditor`, submit/cancel action row, error bar, and the
  mode-specific blocks (create: Suggest/Backfill + blocked-parent + `?parent=`
  handling; edit: load/404 + Deactivate + Danger zone).
- **FR-3** — Division editable in create, locked in edit (immutable — existing
  FR-E2).
- **FR-4** — Create submit builds sub-period definitions from in-memory rows
  (`TryBuildDefinitions` → atomic `CreatePeriodRequest`); edit submit sends
  `UpdatePeriodRequest` (sub-periods managed live). Gated by mode.

### 3.2 Sub-periods section — always visible; `None` disables it (decided)

- **FR-5** — The section renders for any top-level period (create or edit),
  regardless of division. A sub-period (parent set) still hides it — sub-periods
  do not host sub-periods.
- **FR-6** — Division drives the sub-period kind label (Term vs Semester), the
  split-count prefill, and the section's enabled state — NOT its visibility.
- **FR-7** — At division `None` (decided):
  - the section is **disabled** (Add / Auto-split / inline controls inactive),
  - the rows/grid are **empty** (in-memory create rows are cleared; an existing
    `None` year has no sub-periods by the backend invariant anyway),
  - the create submit **does not send sub-period definitions**.

  The backend invariant (FR-C1: a `None`-division year cannot host sub-periods)
  is unchanged — no API changes.

### 3.3 Auto-split count — prefilled, user-configurable (decided)

- **FR-8** — The split count is a `FluentNumberField` in the sub-periods header
  (next to Auto-split), prefilled from the division convention (Terms = 3,
  Semesters = 2) and following division changes until the user overrides it.
  Clamped to a sane range (1–12).
- **FR-9** — `AutoSplitAsync` uses the user-entered count instead of the
  hard-coded `SplitCount`.
- **FR-10** — Auto-split keeps working in BOTH modes (already verified: the
  edit-mode branch deletes Draft sub-periods and creates the split spans live via
  the API; create mode fills in-memory rows). No behavior change here beyond the
  count input.

### 3.4 "Add sub-period" in both modes

- **FR-11** — Edit mode renders an "Add sub-period" button in the section header
  (parity with create) that focuses/activates the inline add row. Create mode
  keeps its existing button. The inline add row stays the entry surface (no
  nested dialogs — repo convention).

### 3.5 Component ownership (unchanged, restated)

- **FR-12** — `PeriodFormFields` keeps owning ONLY the field rows (Division →
  Parent → Name → Dates → Tolerance). The consuming page owns the shell — now
  one page instead of two.
- **FR-13** — `PeriodSubPeriodsEditor` keeps owning the sub-periods section for
  BOTH modes; it gains the split-count input, the edit-mode add button, and the
  `None`-disabled state.

## 4. Design

### 4.1 `PeriodUpsert.razor` (from `Edit.razor`)

- Mode: `private bool IsCreate => Id == Guid.Empty;`
- Renders `PeriodFormFields` with `DivisionLocked="@(!IsCreate)"` and create-only
  `NameActions` (Suggest/Backfill); renders `PeriodSubPeriodsEditor` with
  `YearId = (IsCreate ? null : Id)`.
- Create-mode additions to the Edit base: `?parent=` handling, blocked-parent
  panel, Suggest/Backfill, create submit path.
- Edit-only blocks (load/404, Deactivate, Danger zone) render only when a period
  is loaded.
- On division change to `None` in create mode: clear the editor's in-memory rows
  (the section disables; submit sends no definitions).

### 4.2 `PeriodSubPeriodsEditor` changes (inside the existing component)

- Host passes division through; the editor derives `Enabled => Division is "Terms"
  or "Semesters"` and disables its controls (with a hint) when `None`.
- Adds the split-count number field to the header; `AutoSplitAsync` uses it.
- Adds the "Add sub-period" button to the edit-mode header, activating the inline
  add row.
- Exposes a way for the host to clear the create-mode rows (e.g. a public
  `ClearCreateRows()` alongside `TryBuildDefinitions`).

## 5. Acceptance criteria

- **AC-1** — Both routes render the same single `PeriodUpsert` page; identical
  field rows and sub-periods section.
- **AC-2** — The sub-periods section is visible on create AND edit regardless of
  division; at `None` it is disabled, empty, and the create submit sends no
  sub-period definitions.
- **AC-3** — The split count is prefilled from division, editable, and drives
  Auto-split in both modes.
- **AC-4** — Edit mode shows an "Add sub-period" button that activates the inline
  add row.
- **AC-5** — `?parent=` handling and blocked-parent panel still work on create;
  404, Deactivate, and Danger zone still work on edit.
- **AC-6** — `dotnet build` 0 errors; Students + Admin tests pass (existing
  `PeriodCreatePageTests` / `PeriodEditPageTests` migrate to the merged page).

## 6. Decisions (resolved in r3)

1. **`None` division** — do not save sub-periods; empty grid; disable the
   section (FR-7). Backend invariant unchanged.
2. **Page name/structure** — reuse `Edit.razor` as the base with the create route
   added; rename to `PeriodUpsert.razor` (FR-1).
3. **Auto-split count** — prefill Terms=3 / Semesters=2, following division until
   overridden (FR-8). Auto-split already works on edit (verified in
   `AutoSplitAsync`); only the count becomes configurable (FR-9/FR-10).

## 7. Out of scope

- Backend/API changes (create/update/sub-period endpoints unchanged; FR-C1
  invariant untouched).
- Tolerance inheritance (already delivered).
- Landing grid / kebab menu.