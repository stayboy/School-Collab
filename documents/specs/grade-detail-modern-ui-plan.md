# Spec: Grade-Detail Modern UI (custom tabs + grid actions + name polish)

> Status: **In progress**
> Owner: Students context (grade-level detail page + shared StudentsGrid)
> Depends on: `grade-detail-rich-grids-plan.md` (cg/2–cg/6), `landing-page-wrapper.md`
> Branch: `cg/7-grade-tabs-grid`

## 0. Decisions locked in this revision

1. **Replace `FluentTabs` on the grade-level `Detail.razor` with a custom segmented
   "pill" tab bar.** The default FluentTabs header is JS-composed by the web component
   (labels are not text in markup, styling is shadow-DOM-limited) and reads as dated.
   A small custom `role="tablist"` bar gives full design + a11y control:
   icon + label + live count badge per tab, a filled accent pill for the active tab,
   and `role="tab"/aria-selected` + arrow-key navigation. Panels stay in the DOM with
   `display:none` on inactive panes so existing bUnit content assertions keep working.
2. **Topics grid gets the repo's `RowActionsMenu` kebab pattern** for its row actions.
   The inline `Strands / Lessons / Teachers / Remove` buttons move into a `RowAction`
   list built from `RowAction.Callback(...)` (the shared `RowActionsMenu` component,
   `UseMenuService="false"` like `Subjects.razor`, so items render inline + are
   assertable). The clickable Strand/Lesson **count** cells stay.
3. **Clean up the topic name cell** to match the landing page's "primary name + muted
   secondary" stack: name on its own line, code as a muted sub-line — no crammed inline
   `Name Code`.
4. **StudentsGrid student name follows the student-landing pattern.** Render the Name
   column with the same `student-full-name` treatment as `Students/Index.razor`:
   `student-full-name__name` (FirstName LastName) over a muted
   `student-full-name__demographics` "(Gender, Age)" suffix, inside a lightweight anchor
   to `/students/{id}`. The now-redundant Gender and Age columns are removed from
   `StudentsGrid` (the suffix carries them), matching the landing grid's column set.
5. No backend changes — purely presentation + the shared `RowActionsMenu` already exists.

## 1. Work items

| # | Scope | Detail |
|---|-------|--------|
| 1 | Tabs | Custom segmented tab bar on `Detail.razor` (Topics & Curriculum · Teachers · Students) with icon + label + count badge, accent active pill, arrow-key nav; panels rendered server-side, inactive hidden via CSS |
| 2 | Topics grid | `Actions` column → `<RowActionsMenu Actions="BuildTopicRowActions(context)" .../>` (Strands, Lessons, Teachers, Remove) |
| 3 | Topic name | Stacked `.topic-name` + muted `.topic-code` cell |
| 4 | Student name | `StudentsGrid` Name column → landing `student-full-name` pattern; drop Gender/Age columns; add `StudentsGrid.razor.css` |

## 2. Tests

- Update `GradeLevelDetailPageTests` topics-row "Remove" interaction to drive the kebab
  menu item (or assert the menu action list) instead of the inline `fluent-button`.
- Add a bUnit test asserting the modern tab bar renders all three labels + counts and
  that switching tabs toggles the active panel.
- Keep the Students-tab tests green with the new name pattern (assert `student-full-name`).

## 3. Verification

- Full solution builds; Students + Admin + Architecture + Assignments + Settings suites green.
- Verified in a detached worktree (running dev-server file locks in the main tree).
- Stacked PR on top of `cg/6` in stack #126.
