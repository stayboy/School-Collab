# UI Bug-Fix Round 1 Review — ActivityGroups error surfacing + TopicCreateDialog enrollment span

**Review scope:** verify the two bug fixes from `plan-ui-bugfix-round1.md` against its acceptance criteria.

**Reviewer:** delegated read-only review (source inspection); acceptance pass + command verification run by the orchestrator.

**Changed source files:**
- `src/Students/SchoolCollab.Students.Application/Components/Pages/ActivityGroups/ActivityGroups.razor`
- `src/Students/SchoolCollab.Students.Application/Components/Students/TopicCreateDialog.razor`

**Changed tests:**
- `tests/SchoolCollab.Admin.Tests.Unit/ActivityGroupsPageTests.cs`
- `tests/SchoolCollab.Admin.Tests.Unit/TopicCreateDialogTests.cs`

**Note:** The path given in the original task for `TopicCreateDialog.razor` (`src/SchoolCollab.Students.Application/...`) was missing the `Students` segment. The actual file is at `src/Students/SchoolCollab.Students.Application/Components/Students/TopicCreateDialog.razor`.

---

## Build / test status (orchestrator-run)

| Command | Result |
|---------|--------|
| `dotnet build SchoolCollab.sln -c Debug --nologo -v q` | ✅ **0 errors** (14 pre-existing warnings, none introduced by this round) |
| `dotnet test tests/SchoolCollab.Admin.Tests.Unit --no-build` | ✅ **479 passed, 0 failed** |
| `dotnet test tests/SchoolCollab.Students.Tests.Unit --no-build` | ✅ **332 passed, 0 failed** |
| Filtered re-run of the 3 new/extended tests | ✅ **3 passed, 0 failed** |

`git diff --cached --name-only` → **0 staged files**.

---

## Correct (evidence)

- **Bug 1 — ActivityGroups error surfacing and spinner stop**
  - `LandingPage` now binds `Error="@_error"` at `ActivityGroups.razor:27`, matching the `SubPeriods.razor` convention.
  - `ReloadAsync()` clears stale errors with `_error = null;` at the top (`ActivityGroups.razor:117`).
  - On failure, `_items = [];` is set and `StateHasChanged();` is invoked in the `catch` block (`ActivityGroups.razor:138-142`), stopping the spinner and surfacing the error.
  - `OnToggleActiveAsync` catch now calls `StateHasChanged();` (`ActivityGroups.razor:238`).
  - `CurrentEmptyMessage` returns `"Could not load activity groups."` when `_error` is set (`ActivityGroups.razor:101-108`).

- **Bug 2 — TopicCreateDialog enrollment-span badge**
  - `SelectedGroup` helper is implemented exactly as specified (`TopicCreateDialog.razor:178-184`), mirroring `FilterPeriodsForGroup()`'s lookup.
  - A read-only "Enrollment span" row is rendered only when a group is selected, using `Appearance.Neutral` `FluentBadge` consistent with the ActivityGroups landing Span column (`TopicCreateDialog.razor:90-96`).
  - The span row lives inside the `OwnerType == "ActivityGroup"` block and does not appear for `GradeLevel` owners.
  - The existing `_periodHint` and `_filteredPeriods` logic for `OpenEnded`/`DateRange` is unchanged, so the period hint still displays correctly.

- **Tests**
  - New test `ListPage_ApiFailure_ShowsErrorAndStopsSpinner` added to `ActivityGroupsPageTests.cs`.
  - Existing `ListPage_RendersGroupsFromApi` extended with the AC-1c clean-success assertion (plus the `/students/grade-levels/landing → []` mapping).
  - New test `CreateDialog_GroupSelected_ShowsEnrollmentSpanBadge` added to `TopicCreateDialogTests.cs`.
  - All three follow the mandatory bUnit driving rules from `plan-ui-sprint6-r3.md` §4 (instance-callback select driving, scripted-handler URL mapping, no DialogShellFooter assertions).

### Fixed
No fixes applied by the reviewer. This is a read-only review.

### Finding
No P0/P1/P2 issues found in the changed code.

One minor, expected deviation from the plan: the optional 2c span chip in `Subjects.razor` was **not implemented**. This is explicitly permitted by §3 of the plan: "If it complicates the toolbar layout or tests, SKIP it and record the skip in the worker report — it is explicitly optional." No action needed.

---

## Per-criterion verdicts

| Criterion | Verdict | Evidence |
|-----------|---------|----------|
| AC-1a: Failed `ListActivityGroupsAsync` shows red error bar | **verified** | `ActivityGroups.razor:27` binds `Error="@_error"`; `_error = ex.Message` set in catch. Test `ListPage_ApiFailure_ShowsErrorAndStopsSpinner` asserts "404" in markup — passes. |
| AC-1b: Spinner stops on failure; empty state reads failure | **verified** | `ActivityGroups.razor:141` sets `_items = []`; `CurrentEmptyMessage` returns `"Could not load activity groups."`. Test asserts `NotContain("fluent-progress-ring")` — passes. |
| AC-1c: Successful reload clears stale error | **verified** | `ActivityGroups.razor:117` sets `_error = null;` at top of `ReloadAsync`. Extended `ListPage_RendersGroupsFromApi` asserts no "Could not load activity groups" on success — passes. |
| AC-1d: Toggle/delete failures surface via same `Error` | **verified** | `OnToggleActiveAsync` catch sets `_error` and calls `StateHasChanged()`; `OnDeleteAsync` already does. Both surface through the shared `Error="@_error"` binding. |
| AC-1e: No change to shared components | **verified** | Only `ActivityGroups.razor` changed; `LandingPage.razor`, `StudentsApiClient`, etc. untouched. |
| AC-2a: No span display when no group selected | **verified** | Wrapped in `@if (SelectedGroup is { } selectedGroup)` (`TopicCreateDialog.razor:90`). Test asserts `NotContain("Enrollment span")` before selection — passes. |
| AC-2b: Span badge shows selected group's span | **verified** | `FluentBadge` renders `@selectedGroup.Span` with `Appearance.Neutral`. Test asserts "Termly" — passes. |
| AC-2c: Span coexists with existing period behavior | **verified** | Span row is independent of period row; filtered periods still render. Test asserts "Term 1" alongside "Termly" — passes. |
| AC-2d: Grade owner path unchanged | **verified** | Span row is inside `OwnerType == "ActivityGroup"` block only. |
| Both: build green, suites green | **verified** | Build 0 errors; Admin 479/479 (+2 net new), Students 332/332 — no regressions. |

---

## Merge verdict

**OK with notes → CLOSED.** All acceptance-criteria code changes are present and coherent; the orchestrator ran the verification commands (build + both suites) and all are green, closing the reviewer's only caveat.