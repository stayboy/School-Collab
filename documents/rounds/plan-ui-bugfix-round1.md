# Plan — UI Bug-Fix Round 1: ActivityGroups error surfacing + TopicCreateDialog enrollment-span display

**Status:** Planned (orchestrator-authored; implementation delegated to a worker, review delegated to a reviewer)
**Inputs:**
- Parent diagnosis: two confirmed UI bugs (swallowed errors on ActivityGroups; missing span display in TopicCreateDialog)
- `review-ui-fixes-round1.md` — prior UI-fix round (different defects; no overlap)
- `dialog-ui` skill + SubPeriods.razor error-state convention (`Error="@_error"` on LandingPage + red `FluentMessageBar`)
- `plan-ui-sprint6-r3.md` §4 — the proven bUnit driving rules

**Scope discipline:** two minimal bug fixes + focused bUnit coverage. No refactors, no new features beyond
the optional 2c span chip (explicitly capped), no API/client changes, no dialog framework changes.

---

## 1. Goal

1. **Bug 1 — ActivityGroups page (`/activity-groups`):** a failed `Api.ListActivityGroupsAsync()` call currently
   swallows the error: the catch sets `_error` but never sets `_items`, so `Loading="@(_items is null)"` stays
   true forever → an infinite `FluentProgressRing` with the failure never shown. `_error` is stored in three
   places (`ReloadAsync`, `OnToggleActiveAsync`, `OnDeleteAsync`) but is never rendered — the page does not bind
   `LandingPage`'s existing `Error` parameter (which renders a red `FluentMessageBar` under the grid).
   Fix: stop the spinner on failure, render the error, clear it on retry.
2. **Bug 2 — TopicCreateDialog:** for `OwnerType == "ActivityGroup"` the dialog shows the group picker and a
   period dropdown filtered by the group's span (FR-56), but never DISPLAYS the selected group's
   `EnrollmentSpan` (`ActivityGroupDto.Span`: `WholeAcademicYear | Termly | Semester | DateRange | OpenEnded`).
   The span is the critical context that determines topic delivery, yet users get no visible indication of it.
   Fix: show a read-only "Enrollment span" badge next to the group picker once a group is selected.

---

## 2. Confirmed current state (evidence)

- **`ActivityGroups.razor`** (`src/Students/SchoolCollab.Students.Application/Components/Pages/ActivityGroups/`):
  - `LandingPage` element does NOT pass `Error` (SubPeriods.razor passes `Error="@_error"` — the repo convention).
  - `ReloadAsync()` catch: `Logger.LogError(...); _error = ex.Message;` — `_items` untouched → stays null →
    `Loading="@(_items is null)"` true forever → infinite spinner. No retry affordance.
  - `OnToggleActiveAsync` catch sets `_error` (no `StateHasChanged`); `OnDeleteAsync` catch sets `_error` +
    `StateHasChanged()`. None of the three are visible because `Error` is unbound.
  - `CurrentEmptyMessage` shows "No activity groups yet. Create one to get started." whenever items are empty —
    misleading next to a failure.
- **`LandingPage.razor`** (`src/SchoolCollab.Admin.Shared/Components/Landing/`): already has
  `[Parameter] public string? Error` and renders `@if (!string.IsNullOrEmpty(Error)) <FluentMessageBar
  Intent="MessageIntent.Error" class="mt-3">@Error` OUTSIDE the loading/empty/grid branches — i.e. it renders
  in every state, including alongside the empty-state Info bar. Zero shared-component change needed.
- **`StudentsApiClient.ListActivityGroupsAsync`** uses `GetFromJsonAsync` → throws `HttpRequestException` on ANY
  non-success (404 included). In bUnit the `ScriptedHandler` returns 404 for unmapped URLs → the catch path is
  trivially exercisable. (`ListGradeLevelsForLandingAsync` also throws on 404 — it runs AFTER the items call, so
  a successful-load test should map `/students/grade-levels/landing` → `[]` to keep the error path quiet.)
- **`TopicCreateDialog.razor`** (`src/Students/SchoolCollab.Students.Application/Components/Students/`):
  - `_activityGroups` (full `ActivityGroupDto[]` incl. `Span`) is already loaded in `OnInitializedAsync`.
  - `FilterPeriodsForGroup()` already resolves the selected group via
    `_activityGroups.FirstOrDefault(g => g.Id == groupId)` — the span display needs the same lookup, no new data.
  - The `@if (Model.OwnerType == "ActivityGroup")` block renders the group `FormRow` + duplicate warning; the
    Period `FormRow` below it renders either the filtered dropdown or the `_periodHint` Info bar (OpenEnded /
    DateRange). No span surface anywhere.
  - `FormRow.For` is optional (`[Parameter] public string? For`) — a label-only row is safe.
- **Test harnesses (both green today):**
  - `tests/SchoolCollab.Admin.Tests.Unit/ActivityGroupsPageTests.cs` — renders `<ActivityGroups>` directly with
    `ScriptedHandler` (URL-prefix mapping, 404 default), `FakeAuth` (real-tenant claims), `StubFlagService`.
  - `tests/SchoolCollab.Admin.Tests.Unit/TopicCreateDialogTests.cs` — renders through `FluentDialogProvider` +
    `DialogService.ShowShellDialogAsync`; has `GroupJson(span)`, `PeriodsJson()`, and
    `DriveGroupSelectAsync(cut, groupId)` (drives `FluentSelect.ValueChanged` found by `Instance.Id ==
    "topic-create-group"` — the correct `@bind-Value:after` path).

---

## 3. Exact change list

### Bug 1 — `ActivityGroups.razor` (single file)

| # | Change | Detail |
|---|--------|--------|
| 1a | Bind the error surface | Add `Error="@_error"` to the `<LandingPage>` element (next to `Loading`/`EmptyMessage`). This is the SubPeriods.razor convention; LandingPage already renders it. |
| 1b | Stop the spinner on failure | In `ReloadAsync()`'s `catch (Exception ex)` block, after `_error = ex.Message;` add `_items = [];` (and keep the `if (_disposed) return;` guard first). The page then shows the empty-state bar + the red error message bar instead of an infinite ring. |
| 1c | Clear stale errors | Set `_error = null;` at the top of `ReloadAsync()` (before the try). A successful (re)load therefore renders with no error bar; a failed toggle/delete followed by a successful reload clears the old message. |
| 1d | Make failures re-render | In the `OnToggleActiveAsync` catch, after `_error = ex.Message;` add `StateHasChanged();` (mirrors the existing `OnDeleteAsync` catch). RowAction callbacks may not be Blazor event handlers, so the automatic post-handler re-render is not guaranteed. Also add `StateHasChanged();` in the `ReloadAsync` catch (guarded by the existing `_disposed` check) so every failure path repaints. |
| 1e | Don't lie in the empty state | Extend `CurrentEmptyMessage`: when `!string.IsNullOrWhiteSpace(_error)` (after the `!_isRealTenant` check), return `"Could not load activity groups."` so the Info bar next to the red error bar reads as a failure, not an empty list. |

**No retry button in this round.** SubPeriods has a Back button because it is a drill-down page with a parent
route; `/activity-groups` is a top-level landing where re-navigation/re-create-dialog already triggers
`ReloadAsync` (which now clears + re-renders errors). Adding a retry button is a state-machine change
(disabled-while-loading, cancellation interplay) — out of scope for a bug-fix round.

### Bug 2 — `TopicCreateDialog.razor` (single file)

| # | Change | Detail |
|---|--------|--------|
| 2a | Resolve the selected group | Add a private helper mirroring `FilterPeriodsForGroup`'s lookup: `private ActivityGroupDto? SelectedGroup => Guid.TryParse(_activityGroupIdText, out var id) ? _activityGroups.FirstOrDefault(g => g.Id == id) : null;` |
| 2b | Render the span | Inside the existing `@if (Model.OwnerType == "ActivityGroup")` block, immediately AFTER the group `FormRow` (before the duplicate-warning bar), add a read-only row shown only when a group is selected: `<FormRow Label="Enrollment span" For="topic-create-group-span"><FluentBadge Appearance="Appearance.Neutral" Id="topic-create-group-span">@selectedGroup.Span</FluentBadge></FormRow>` wrapped in `@if (SelectedGroup is { } selectedGroup) { ... }`. Badge style matches the ActivityGroups landing Span column (`FluentBadge Appearance.Neutral`). If `Id` does not compile on `FluentBadge`, drop the `Id`/`For` attributes — bUnit asserts on text, not ids. |
| 2c | (small, include unless it fights) Subjects.razor span chip | When the ActivityGroup owner filter is active AND `_selectedActivityGroup` is set, render the selected group's span as a `FluentBadge` (e.g. `Span: Termly`, `Appearance.Neutral`) in the `ToolbarFilters` else-branch right after the group `FluentSelect`. `_activityGroups` is already loaded; pure render, ~8 lines. If it complicates the toolbar layout or tests, SKIP it and record the skip in the worker report — it is explicitly optional. |

---

## 4. Acceptance criteria

**Bug 1:**
- AC-1a: When `ListActivityGroupsAsync` fails, the page shows a red error `FluentMessageBar` with the failure
  message (via `LandingPage Error`) — the error is VISIBLE.
- AC-1b: On failure the loading spinner stops (no `fluent-progress-ring` in the rendered markup after settle)
  and the empty-state bar reads "Could not load activity groups." — no infinite spinner.
- AC-1c: A subsequent successful `ReloadAsync` renders with no error bar (stale errors cleared).
- AC-1d: Toggle-active and delete failures surface through the same bound `Error` bar (no code-path change
  needed beyond 1c/1d; verified by the shared binding).
- AC-1e: No change to LandingPage, StudentsApiClient, or any shared component.

**Bug 2:**
- AC-2a: With `OwnerType == "ActivityGroup"` and NO group selected, no span display renders.
- AC-2b: Once a group is selected, a read-only "Enrollment span" row shows the selected group's span value
  (e.g. `Termly`), styled as a `FluentBadge` like the ActivityGroups landing Span column.
- AC-2c: The span display coexists with the existing period behavior: Termly → Term options; OpenEnded →
  hidden dropdown + existing hint (badge complements, does not replace).
- AC-2d: Grade-owner path is unchanged (no span row for `OwnerType == "GradeLevel"`).

**Both:** `dotnet build` green; full `tests/SchoolCollab.Admin.Tests.Unit` suite green (existing 464+
tests must not regress); new tests added per §5.

---

## 5. Test expectations (bUnit — repo rules are MANDATORY)

**Repo lessons (from `plan-ui-sprint6-r3.md` §4 and prior rounds — non-negotiable):**
1. NEVER click a FluentButton that is an `EditForm` `type=submit` — it does not fire `EditForm` in bUnit.
   Drive submits via `editForm.Instance.OnValidSubmit.InvokeAsync(editForm.Instance.EditContext)`. (Not needed
   for the tests below — no submit is driven — but stated for any follow-up guard tests.)
2. Assert no-POST guards via `handler.Calls` (method + URL), never via UI-only assertions.
3. The `DialogShellFooter` error surface is UNRELIABLE in bUnit — never assert on it. Assert on inline
   message bars in the form body, or (bug 1) on the LandingPage error bar which renders in the page markup.
4. Drive selects via the component instance callback (`FluentSelect<T>.ValueChanged.InvokeAsync(...)` found by
   `Instance.Id`), never by clicking `fluent-option`. For the dialog's group picker, reuse
   `DriveGroupSelectAsync` (it drives the `@bind-Value:after` path).
5. Fire-and-forget loads: always `WaitForAssertion`/`WaitForState`; script every GET the component issues.

**New test A — `ActivityGroupsPageTests.ListPage_ApiFailure_ShowsErrorAndStopsSpinner`:**
- `RegisterWith()` with NO mapped URL → `GET /activity-groups` returns 404 → `GetFromJsonAsync` throws →
  `ReloadAsync` catch path. (Do NOT map `/students/grade-levels/landing` — it is never reached.)
- `Render<ActivityGroups>()`; `cut.WaitForState(() => cut.Markup.Contains("Could not load activity groups"), 2s)`.
- Assert: `Markup.Should().Contain("Could not load activity groups.")` (state, from 1e);
  `Markup.Should().Contain("404")` (the rendered error text — `HttpRequestException.Message` includes the
  status code); `Markup.Should().NotContain("fluent-progress-ring")` (AC-1b — spinner stopped).

**New test B (recommended, small) — extend `ListPage_RendersGroupsFromApi`:**
- Add mapping `("/students/grade-levels/landing", HttpStatusCode.OK, "[]")` so the success path completes
  without a secondary failure, and add `cut.Markup.Should().NotContain("fluent-message-bar")` is too brittle —
  instead assert `cut.Markup.Should().NotContain("Could not load activity groups")` after the existing
  `WaitForState` (locks AC-1c's clean-success state).

**New test C — `TopicCreateDialogTests.CreateDialog_GroupSelected_ShowsEnrollmentSpanBadge`:**
- `handler.Map GET /activity-groups → GroupJson("Termly")`, `GET /students/periods → PeriodsJson()`,
  `GET /students/subjects/by-group/{GroupId} → "[]"` (plus `Register(handler)`'s base maps).
- Render `FluentDialogProvider`; `ShowShellDialogAsync` with `GroupModel("ActivityGroup")`.
- `WaitForAssertion(form exists)`; assert `Markup.Should().NotContain("Enrollment span")` (AC-2a — nothing selected yet).
- `await DriveGroupSelectAsync(cut, GroupId.ToString());`
- `WaitForAssertion(Markup.Contains("Enrollment span"))`; assert `Markup.Should().Contain("Termly")` (AC-2b)
  and `Markup.Should().Contain("Term 1")` (coexistence with the filtered period options, AC-2c).
- Cleanup (house pattern): `cut.Find("fluent-button[aria-label='Close']").Click();` then
  `await task.WaitAsync(5s)` → result null.

No new test files; both tests live in the existing harnesses. Existing tests must stay green.

---

## 6. Out of scope (explicit non-goals)

- Retry button / Back navigation on ActivityGroups (see §3 note).
- Any change to `LandingPage.razor`, `FormRow.razor`, `StudentsApiClient`, or dialog framework.
- Error-surface changes to OTHER landing pages (Subjects.razor error handling is fine already — it sets
  `_items = []` in its catches; only the optional 2c span chip touches that file).
- Playwright/E2E coverage; localization; theming.

## 7. Verification (worker + reviewer must both run)

```
dotnet build SchoolCollab.sln -c Debug --nologo -v q
dotnet test tests/SchoolCollab.Admin.Tests.Unit
```

---

## 8. Acceptance (orchestrator acceptance pass)

**Status: CLOSED.**

Per-criterion verdicts:

| Criterion | Verdict | Evidence |
|-----------|---------|----------|
| AC-1a: Failed `ListActivityGroupsAsync` shows red error `FluentMessageBar` | ✅ satisfied | `Error="@_error"` bound at `ActivityGroups.razor:27`; `_error = ex.Message` in catch. Test `ListPage_ApiFailure_ShowsErrorAndStopsSpinner` asserts "404" in markup — **passes**. |
| AC-1b: Spinner stops on failure; empty state reads failure | ✅ satisfied | `ActivityGroups.razor:141` sets `_items = []`; `CurrentEmptyMessage` returns "Could not load activity groups." Test asserts `NotContain("fluent-progress-ring")` — **passes**. |
| AC-1c: Successful reload clears stale error | ✅ satisfied | `ActivityGroups.razor:117` sets `_error = null;` at top of `ReloadAsync`. Extended `ListPage_RendersGroupsFromApi` asserts no "Could not load activity groups" on success — **passes**. |
| AC-1d: Toggle/delete failures surface via same `Error` bar | ✅ satisfied | `OnToggleActiveAsync` catch sets `_error` + `StateHasChanged()`; `OnDeleteAsync` already did; both render through the shared `Error` binding. |
| AC-1e: No change to shared components | ✅ satisfied | Only `ActivityGroups.razor` changed; `LandingPage.razor`, `FormRow.razor`, `StudentsApiClient` untouched. |
| AC-2a: No span display when no group selected | ✅ satisfied | Row wrapped in `@if (SelectedGroup is { } selectedGroup)` (`TopicCreateDialog.razor:90`). Test asserts `NotContain("Enrollment span")` pre-selection — **passes**. |
| AC-2b: Span badge shows selected group's span | ✅ satisfied | `FluentBadge Appearance.Neutral` renders `@selectedGroup.Span`; test asserts "Termly" — **passes**. |
| AC-2c: Span coexists with existing period behavior | ✅ satisfied | Span row independent of period row; test asserts "Term 1" alongside "Termly" — **passes**. `_periodHint`/`_filteredPeriods` logic unchanged. |
| AC-2d: Grade-owner path unchanged | ✅ satisfied | Span row lives inside the `OwnerType == "ActivityGroup"` block only. |
| Both: build green, Admin suite green, new tests per §5 | ✅ satisfied | See verification results below. |

Commands run (orchestrator, this round):

| Command | Result |
|---------|--------|
| `dotnet build SchoolCollab.sln -c Debug --nologo -v q` | ✅ 0 errors (14 pre-existing warnings, none new) |
| `dotnet test tests/SchoolCollab.Admin.Tests.Unit --no-build` | ✅ 479 passed, 0 failed |
| `dotnet test tests/SchoolCollab.Students.Tests.Unit --no-build` | ✅ 332 passed, 0 failed |
| Filtered re-run of the 3 new/extended tests | ✅ 3 passed, 0 failed |
| `git diff --cached --name-only` | ✅ 0 staged files |

Worker vs orchestrator counts reconcile: Admin 479/479, Students 332/332 — matches the worker report exactly.

Residual items:
- **Optional 2c (Subjects.razor span chip) — SKIPPED by design.** Explicitly permitted by §3; recorded in the worker report. No action needed; may be picked up in a future cosmetic round.
- **Pre-existing NU1903 warnings** (`SQLitePCLRaw.lib.e_sqlite3`, `SSH.NET` vulnerable packages) and pre-existing analyzer warnings in test projects — unrelated to this round.
- **No retry button** on ActivityGroups — deliberate non-goal per §3/§6.

Artifacts:
- Reviewer report: `documents/specs/review-ui-bugfix-round1.md` (created by the orchestrator from the reviewer's inline report, which the reviewer could not persist; verification tables updated with orchestrator-run results).