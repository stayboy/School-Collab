# Spec — Periods Landing Grid Beautify (type width, name-as-edit-link, sub-period count dialog)

- **Status:** Approved (orchestrator) — implementation round started 2026-08-28, branch feat/periods-landing-grid-beautify
- **Date:** 2026-08-28
- **Scope:** UI only — `Periods.razor` landing page + one new read-only dialog component. No API, domain, or schema changes.
- **Pattern sources:** `LandingPage`/`EntityGrid` landing pattern (`src/SchoolCollab.Admin.Shared/Components/Landing/`), repo landing-page grid settings convention (`LandingGridSettings.GridTemplateColumns`), FluentUI badges/links, `dialog-ui` skill conventions (read-only dialogs do NOT need the `DialogShellBase` Save footer).

## 1. Goal

Bring the Periods landing page (`/students/periods`) up to the standard landing-page
grid polish and make the two most common navigation paths one click shorter:

1. the **Type** column currently truncates/compresses the `AcademicYear` badge;
2. editing a period requires opening the row-action menu → Edit;
3. viewing a year's sub-periods requires a full page navigation via the
   "Sub-periods" row action — a heavyweight round trip for what is usually a glance.

## 2. Current state (Periods.razor)

- Columns: Name (`PropertyColumn`, sortable) · Type (`TemplateColumn`, `FluentBadge`) · Start Date · End Date · Status (`FluentBadge`).
- `GridSettings.GridTemplateColumns = "minmax(160px,2fr) 100px 110px 110px 120px auto"` — the 100px Type track is too narrow for the "Academic Year" text at default font size.
- Row actions (menu): Draft → Activate; Active → Complete; AcademicYear → "Sub-periods" (navigates to `/students/periods/{id}/sub-periods`); always Edit.
- Data: `Api.ListPeriodsAsync()` returns the **flat** period list (years AND sub-periods), each `PeriodDto` carrying `PeriodType` + `ParentPeriodId` — sub-period counts are computable client-side with no API change.

## 3. Functional Requirements

- **FR-1 (Type column width)** — Widen the Type column track so the widest badge
  text ("Academic Year") renders on one line without truncation or wrapping.
  Update `GridTemplateColumns` to something like
  `"minmax(160px,2fr) 140px 110px 110px 120px 110px auto"` (140px type track +
  new 110px sub-period track, §FR-3); tune ±10px at implementation time and
  verify with the longest label.
- **FR-2 (Name = edit navigation)** — The Period **Name** cell becomes a link
  (anchor styled per repo link conventions) navigating to
  `/students/periods/{Id}/edit`. The column stays **sortable** (keep the
  `PropertyColumn` sort expression — restyle the cell content, not the sort key).
  The Edit entry in the row-action menu is **removed** as redundant.
- **FR-3 (Sub-periods count column)** — New "Sub-periods" template column:
  - For `PeriodType == "AcademicYear"` rows: show the count of periods whose
    `ParentPeriodId == row.Id`, computed client-side from the already-loaded
    flat list (memoize into a `Dictionary<Guid, int>` on load — no per-row fetch).
  - Render as a link (`N` or `N sub-periods`; `0` renders as plain muted text,
    not a link). Clicking opens the sub-periods list **in a dialog** (§FR-4).
  - For sub-period rows (Term/Semester): render an em dash (`—`), non-interactive.
- **FR-4 (Sub-periods dialog)** — New read-only dialog component
  `SubPeriodsListDialog` (students Application project, sibling of the Periods pages):
  - Content: title `Sub-periods — {year name}`, then the same list shape as the
    SubPeriods landing page (Name, Type badge, Start Date, End Date, Status badge).
  - Data: `Api.ListSubPeriodsAsync(yearId)` fetched when the dialog opens
    (cancellation + disposed guards per repo convention); loading spinner while
    in-flight; error `FluentMessageBar` inside the dialog on failure; empty-state
    message "No sub-periods for this academic year yet." with an inline
    **"+ New sub-period"** link that closes the dialog and navigates to
    `/students/periods/create?parent={yearId}`.
  - Actions per row (inline, not a menu): **Activate** (Draft rows), **Complete**
    (Active rows, confirm first) — same client calls as the landing page, then
    re-fetch the dialog list AND the parent grid so both stay in sync.
  - Footer: single **Close** button only (read-only dialog — do NOT use the
    `DialogShellBase` Cancel/Save footer; mirror `TopicStrandsDialog.razor`:
    a plain `IDialogService` dialog with a `.dialog-footer` + `FluentButton`
    Close, styled with `border-top` — never a `FluentDivider`).
  - Width ~640px — the `DialogSize.Medium` default of
    `ShowReadonlyDialogAsync` (opened via `Content.TryGet<T>(key)` inputs,
    per `TopicStrandsDialog.razor`).
- **FR-5 (Row actions cleanup)** — The "Sub-periods" navigate row action is
  removed for AcademicYear rows (superseded by the count link); keep
  Activate (Draft) / Complete (Active). Edit row action removed (FR-2). The
  row-action list collapses to at most one item; when only one action remains,
  render it as an inline icon button instead of a menu (landing pattern).
- **FR-6 (Full-page SubPeriods route preserved)** — The dedicated
  `/students/periods/{id}/sub-periods` page is unchanged and remains reachable
  (deep links, bookmarking); the dialog adds a lighter affordance on top.

## 4. Non-Functional Requirements

- **NFR-1 (No API changes)** — Count is derived from the existing
  `ListPeriodsAsync` response; dialog data from the existing
  `ListSubPeriodsAsync`. No new endpoints, DTO fields, or migrations.
- **NFR-2 (bUnit tests)** — Grid tests for `Periods.razor`: name renders as edit
  link; count column shows the correct per-year counts; `0` renders
  non-interactive; sub-period rows show `—`; clicking the count opens the dialog.
  Dialog tests: list rendering, activate/complete sync back to the grid, empty
  state, error state.
- **NFR-3 (Tenancy/visibility)** — Existing `VisibleTenantService` gating
  (`_isRealTenant`) and TenantGate behavior unchanged; dialog mutations go through
  the same tenant-scoped API client.

## 5. Acceptance Criteria

1. With the seed data, no Type badge wraps or truncates at ≥1280px viewport width.
2. Clicking a period name navigates to its Edit page; the grid remains sortable by name.
3. A year with 2 sub-periods shows "2"; clicking opens the dialog listing exactly those 2 rows.
4. Activating a Draft sub-period inside the dialog updates the dialog row AND the
   landing grid status badge without a manual refresh.
5. Sub-period rows show `—` in the count column; a year with none shows muted "0".
6. Row action menu contains only Activate (Draft) or Complete (Active); no Edit / no Sub-periods entries.
7. `dotnet build` 0 errors; unit tests green.

## 6. Edge cases

- **Draft/Completed year counts** — count is structural (all sub-period rows regardless of status), displayed as-is.
- **Orphaned sub-period (parent deleted)** — cascade delete makes this impossible; if encountered, the row simply doesn't contribute to any count.
- **Dialog open during tenant switch** — dialog closes on navigation; `_disposed` guards apply as everywhere else.
- **Long year names** — dialog title truncates with ellipsis (`text-overflow`), full name in `title` attribute.

## 7. Affected files

| File | Change |
|---|---|
| `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/Periods.razor` | FR-1/2/3/5: column widths, name link, count column, row-action cleanup, dialog open |
| `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/SubPeriodsListDialog.razor` (+ `.razor.css`) | FR-4: new read-only dialog component |
| `tests/SchoolCollab.Admin.Tests.Unit/` (new `PeriodsLandingGridTests.cs`) | NFR-2 |

## 8. Out of scope

- SubPeriods landing page restyle (only the dialog reuses its list shape).
- Inline create of sub-periods from the landing grid (dialog links out to the create route).
- Any backend change.

