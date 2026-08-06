# Spec: Grade-Detail Modern UI (section cards + grid actions + name polish)

> Status: **Complete — all work items shipped + tests green (Students 185, Admin 271, Arch 14, Assignments 88, Settings 402)**
> Owner: Students context (grade-level detail page + shared StudentsGrid)
> Depends on: `grade-detail-rich-grids-plan.md` (cg/2–cg/6), `landing-page-wrapper.md`
> Branch: `cg/7-grade-tabs-grid`

## 0. Decisions locked in this revision

1. **Replace `FluentTabs` on the grade-level `Detail.razor` with three equally-sized
   FluentCards** (one each for Topics, Teachers, Students) in a responsive auto-fit
   grid. Each card has a header (icon + title + accent **count chip**), a **top-15
   preview list** (clickable rows into the relevant detail), and a **"View all"
   FluentAnchor** footer:
   - **Topics** card → opens new `GradeTopicsDialog` (full assigned-topic list with
     assign/remove and per-topic Strands/Lessons/Teachers dialogs).
   - **Teachers** card → opens new `GradeTeachersDialog` (full linked-teacher list with
     link/role/unlink/remove + resolved names).
   - **Students** card → navigates to the grade-filtered students landing
     (`/students?gradeLevelId={id}`).
   The full management grids moved out of the page into the two dialogs. The custom
   segmented pill tab bar (superseded) is removed entirely.
2. **Topics grid gets the repo's `RowActionsMenu` kebab pattern** for its row actions.
   The inline `Strands / Lessons / Teachers / Remove` buttons move into a `RowAction`
   list built from `RowAction.Callback(...)` (the shared `RowActionsMenu` component,
   `UseMenuService="false"` like `Subjects.razor`, so items render inline + are
   assertable). The clickable Strand/Lesson **count** cells stay. (Rendered in
   `GradeTopicsDialog` for the full list; the card preview lists topic names.)
3. **Clean up the topic name cell** to match the landing page's "primary name + muted
   secondary" stack: name on its own line, code as a muted sub-line — no crammed inline
   `Name Code`.
4. **StudentsGrid student name follows the student-landing pattern.** Render the Name
   column with the same `student-full-name` treatment as `Students/Index.razor`:
   `student-full-name__name` (FirstName LastName) over a muted
   `student-full-name__demographics` "(Gender, Age)" suffix, inside a lightweight anchor
   to `/students/{id}`. The now-redundant Gender and Age columns are removed from
   `StudentsGrid` (the suffix carries them), matching the landing grid's column set.
   On the grade-detail Students card, the preview uses the same name + demographics
   stack.
5. No backend changes — purely presentation + the shared `RowActionsMenu` already exists.

## 1. Work items

| # | Scope | Detail |
|---|-------|--------|
| 1 | Section cards | Replace the tab control with three equally-sized FluentCards (Topics · Teachers · Students): header icon + title + accent count chip, top-15 preview list, "View all" footer. Topics/Teachers View-all are `FluentButton` (`OnClick` opens `GradeTopicsDialog` / `GradeTeachersDialog`); Students View-all is a `FluentAnchor` (navigates via `Href` to the grade-filtered students landing) |
| 2 | Topics grid | Row actions via the repo's `RowActionsMenu` kebab (Strands, Lessons, Teachers, Remove) — rendered in `GradeTopicsDialog` |
| 3 | Topic name | Stacked `.topic-name` + muted `.topic-code` cell |
| 4 | Student name | `StudentsGrid` Name column → landing `student-full-name` pattern; drop Gender/Age columns; add `StudentsGrid.razor.css`; the Students card preview uses the same name + demographics stack |

## 2. Tests

- Rewrite `GradeLevelDetailPageTests` for the card layout: overview + three cards
  (titles, counts, top-15 previews, empty states), students landing navigation, and a
  source-wiring test asserting the View-all anchors open `GradeTopicsDialog` /
  `GradeTeachersDialog` (and the segmented tab bar is gone).
- New `GradeDialogsBunitTests` renders both dialogs directly: topic list/counts, empty
  states, remove/assign/unlink callbacks, teacher name resolution, and the TCHROLES
  role parent lookup.

## 3. Appearance rules (verified against 4.14.2)

- `FluentButton` does **NOT** support `Appearance.Hypertext` — it throws
  `ArgumentException` in `OnParametersSet`. Use `Appearance.Lightweight` for
  link-like action buttons (View-all Topics/Teachers, dialog strand/lesson counts).
- `FluentAnchor` supports `Hypertext` — use it for anchors that navigate via `Href`
  (Students View-all; `GradeTeachersDialog` teacher-name link).
- Rule: `FluentAnchor` → `FluentButton` when `OnClick` is used (no `Href`); keep
  `FluentAnchor` when it navigates via `Href`.
- Keep the students card test asserting the `student-full-name`-style name + demographics.

## 3. Verification

- Full solution builds; Students + Admin + Architecture + Assignments + Settings suites green.
- Verified in a detached worktree (running dev-server file locks in the main tree).
- Stacked PR on top of `cg/6` in stack #126.
