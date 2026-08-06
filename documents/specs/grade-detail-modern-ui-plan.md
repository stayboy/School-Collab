# Spec: Grade-Detail Modern UI (section cards + grid actions + name polish)

> Status: **Complete — cg/7 (card-based redesign) + cg/8 (UI polish + shared FieldDisplay) shipped + tests green (Students 185, Admin 273, Arch 14, Assignments 88, Settings 402)**
> Owner: Students context (grade-level detail page + shared StudentsGrid)
> Depends on: `grade-detail-rich-grids-plan.md` (cg/2–cg/6), `landing-page-wrapper.md`
> Branches: `cg/7-grade-tabs-grid` (PR #133), `cg/8-grade-detail-polish` (PR #134)

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

| # | Scope | Detail | Status |
|---|-------|--------|--------|
| 1 | Section cards | Replace the tab control with three equally-sized FluentCards (Topics · Teachers · Students): header icon + title + accent count chip, top-15 preview list, "View all" footer. Topics/Teachers View-all are `FluentAnchor` (`Appearance.Hypertext`, `Href="#"`, `OnClick` opens `GradeTopicsDialog` / `GradeTeachersDialog`); Students View-all is a `FluentAnchor` (navigates via `Href` to the grade-filtered students landing) | ✅ cg/7 + cg/8 |
| 2 | Topics grid | Row actions via the repo's `RowActionsMenu` kebab (Strands, Lessons, Teachers, Remove) — rendered in `GradeTopicsDialog` | ✅ cg/7 |
| 3 | Topic name | Stacked `.topic-name` + muted `.topic-code` cell | ✅ cg/7 |
| 4 | Student name | `StudentsGrid` Name column → landing `student-full-name` pattern; drop Gender/Age columns; add `StudentsGrid.razor.css`; the Students card preview uses the same name + demographics stack | ✅ cg/7 |
| 5 | Add icons | Add `FluentButton` add icons in each card header (Topics/Teachers/Students: `FluentIcons.Add`); wire Topics/Teachers to existing dialogs, Students to new `OpenAddStudentsAsync` (period resolution + `StudentPickerDialog` + `EnrollStudentAsync`) | ✅ cg/8 |
| 6 | Rename Topics → Subjects/Curriculum | Visible labels only: card title "Subjects/Curriculum", empty state "No subjects assigned to this curriculum yet", View-all "View all subjects", dialog title "Subjects/Curriculum · {grade}" | ✅ cg/8 |
| 7 | Topic secondary text | Topic preview items restructured: `<div>` container + topic-name `FluentAnchor` (`Appearance.Hypertext`, `Href="#"`, `OnClick`) + Strands(N) and Lessons(N) `FluentAnchor` navigable counts, separated by a `|` divider and wider gap | ✅ cg/8 |
| 8 | View-all alignment | Fix text/arrow vertical alignment via `IconEnd="@FluentIcons.ArrowRight"` (`FluentAnchor` with `Appearance.Hypertext`, `Href="#"`, `OnClick` for Topics/Teachers; `FluentAnchor` with real `Href` for Students) | ✅ cg/8 |
| 9 | SectionCard component | Extract a shared `SectionCard.razor` component to unify the three grade-detail cards (header icon/title/count/add, preview item template, View-all footer). Wrap in `FluentCard` with explicit border so the card outline is visible. | ✅ cg/8 |
| 10 | Subject count gap | Subject card secondary text uses wider gap + `|` divider between Strands(N) and Lessons(N) counters. Topic name is a `FluentButton` (Lightweight, no card-style plain button). | ✅ cg/8 |
| 11 | Profile view | Grade Overview profile displays Name, Age range, Enrollment, Gender, Students; removed the redundant Level row. | ✅ cg/8 |
| 12 | FieldDisplay component | Extract a shared read-only `FieldDisplay` component in `SchoolCollab.Admin.Shared` for label/value pairs, with Vertical (detail-card default: muted uppercase label above value) and Horizontal (review-row default: bold inline label beside value) orientations. Migrated grade, student, guardian, and teacher detail profiles. | ✅ cg/8 |

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
  `ArgumentException` in `OnParametersSet`.
- For link-like actions that do **not** navigate (open dialogs, show panels, etc.),
  use `FluentAnchor` with `Appearance.Hypertext`, `Href="#"`, and the component's
  `OnClick` parameter. `Href="#"` is required so the anchor renders as a clickable
  link and the component internally prevents default navigation.
- Use `FluentAnchor` with `Appearance.Hypertext` and a real `Href` for actual
  navigation links (Students View-all; `GradeTeachersDialog` teacher-name link).
- Prefer the native component's parameter list first — `FluentAnchor` exposes
  `OnClick` as a `[Parameter]`. Do not fall back to HTML `@onclick` until a
  component parameter is confirmed absent.
- Keep the students card test asserting the `student-full-name`-style name + demographics.
- Add icons use `Appearance.Stealth` + `ButtonSize.Small` for compact icon-only buttons.

## 4. Verification

- Full solution builds; Students + Admin + Architecture + Assignments + Settings suites green.
- Admin suite: **273/273** tests pass (including 11 card-based `GradeLevelDetailPageTests`).
- SectionCard renders with visible card outline/border after wrapping in `FluentCard` and adding explicit `border`/`border-radius` CSS.
- Verified in a detached worktree (running dev-server file locks in the main tree).
- Stacked PR on top of `cg/6` in stack #126 (cg/7 PR #133, cg/8 PR #134).
