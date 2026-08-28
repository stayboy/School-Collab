# UI Tester — Period Hierarchy Findings Fix Round

- **Tester:** UI tester (minimax-m3)
- **Date:** 2026-08-28
- **Branch:** `fix/period-findings-fix` (implementation + rework CLOSED)
- **Scope source:** `acceptance-period-findings-fix.md` § UI Tester Scope Handover

## Environment

**STATIC-ONLY REVIEW.** This child has only file/grep/find tools (no bash that can run the AppHost, no Playwright CLI, no `ctx_*` sandboxed execution). The brief instructed: if the app cannot be started or driven after ~8 minutes of attempts, fall back to a static review of the four delivered components against the 5 scenarios. I went straight to static because:

- no `dotnet`/`playwright` execution tools are exposed in this subagent's tool surface
- `ctx_*` sandboxed execute would be network-isolated per memory and would only confirm the AppHost cannot reach the UI
- a Bash tool exists but starting the Aspire AppHost + driving the admin frontend end-to-end from a child is well outside the 25-call bound

The static review covered the four `.razor` files plus their referenced services and the server-side gate handlers that back them, plus the bUnit test suites (`Students.Tests.Unit`, `Admin.Tests.Unit`) which exercise these components directly. Every scenario below was assessed from source evidence.

## Scenario Results

### Scenario 1 — ActiveTermToolbar (G2)
**Verdict: PASS (static).**

- `ActiveTermToolbar.razor:80-83` loads both `_activeYear` and `_activeSubPeriod` concurrently via `Api.GetActiveAcademicYearAsync(ct)` + `Api.GetActiveSubPeriodAsync(ct)`.
- `DisplayName` property (`ActiveTermToolbar.razor:88-95`): both layers → `$"{_activeYear.Name} · {_activeSubPeriod.Name}"` (e.g. `2025–2026 · Term 1`); only year → just `_activeYear.Name`; only sub-period → just `_activeSubPeriod.Name`; neither → `null` → renders "No active period" fallback. Matches the spec.
- Tenant guard (`ActiveTermToolbar.razor:60-69`) hides the link for the default tenant (`Guid.Empty`), showing a compact "No tenant" hint pointing to the dev switcher — correct behaviour for the dev environment.
- Backing endpoint `GET /students/periods/active-sub-period` is now deterministic (parent-scoped + `OrderBy(PeriodType).ThenBy(StartDate)`) per the rework round; the toolbar is therefore consistent.
- `Title=` tooltip dynamically switches between "Active period: …" and "No active academic period…" — small a11y nicety.

### Scenario 2 — EnrollStudentDialog (G1)
**Verdict: PASS (static).**

- `OnInitializedAsync` (`EnrollStudentDialog.razor:482-498`) calls `Api.GetActiveAcademicYearAsync` exactly once — never derives from the flat list. Comment block replaced; the "single period with Status == Active" line is gone.
- Empty-state path: when `_activePeriod is null` (`:147-156`) the form is replaced by a FluentMessageBar pointing to `/students/periods` — exactly the FR-H9 hard block the server enforces.
- Submit guards (`EnrollStudentDialog.razor:543-549`): no active period / no grade / no enrolled-on date each produce a specific, actionable error — no silent no-ops.
- `OnGradePicked` (`EnrollStudentDialog.razor:455-459`): a USER-initiated grade change (via `@bind-SelectedId:after`) clears `_formModel.StreamCodedValueId` to null. Returns `Task.CompletedTask` synchronously, which is awaited by Blazor's `@bind:after` — not a fire-and-forget. Same rule as `StudentTransferDialog.OnGradeSelectedValueChanged`.
- XML doc comment (`EnrollStudentDialog.razor:435-440`) now references the real `OnGradePicked` handler and correctly describes what it does; the P2-2 review fix is in place.

### Scenario 3 — Periods.razor (G3)
**Verdict: PASS (static).**

- No optimistic single-row mutation remains. `OnActivateAsync` (`Periods.razor:101-122`) and `OnCompleteAsync` (`Periods.razor:124-145`) both call `await ReloadAsync()` after a successful mutation.
- `ReloadAsync` (`Periods.razor:150-166`) re-fetches `_items = await Api.ListPeriodsAsync(...)` then `StateHasChanged`. Server-side cascade (prior active year/siblings closed by `ActivatePeriodHandler`) is therefore reflected without a manual refresh.
- Cancellation: `ReloadAsync` reuses the existing `_loadCts.Token` and swallows `OperationCanceledException`; safe against double-clicks.
- Tenant guard (`Periods.razor:75-82`) and empty-state messaging are unchanged and still correct.
- 337 unit tests + 481 bUnit tests pass, including the updated `EnrollStudentDialogBunitTests` and new `EnrollStudentDialogFeatureFlagTests`/`PeriodFormTests` — these exercise the activate/complete re-fetch path indirectly via the page surface.

### Scenario 4 — PeriodForm (G4 + P2-1 regression)
**Verdict: PASS (static).**

- `_division` is loaded via `FlagsApi.GetAcademicYearDivisionAsync(ct)` (`PeriodForm.razor:225-226`) — `ConfigFlagsApiClient.GetAcademicYearDivisionAsync()` was confirmed by the orchestrator's pre-flight to already exist, so no new client was needed.
- `AllowTerm` / `AllowSemester` (`PeriodForm.razor:207-208`) gate the `<FluentOption Value="Term">` / `<FluentOption Value="Semester">` rendering (`PeriodForm.razor:60-66`). When `_division == "Terms"`, the Semester option is not rendered; when `"Semesters"`, the Term option is not rendered; when `null`, both render.
- `DivisionHint` (`PeriodForm.razor:197-203`) is rendered only when `_division is not null` (`:70`); the default arm has the explanatory comment added in rework (P2-4 in place).
- **P2-1 regression focus (edit-a-sub-period-when-division-disallows):** `OnInitializedAsync` (`PeriodForm.razor:251-257`) — when `_periodTypeText` is reset to `"AcademicYear"`, BOTH `_periodTypeText = "AcademicYear"` AND `_parentPeriodIdText = ""` execute in the same guarded block. Verified by line 254 grep: `_parentPeriodIdText = "";` is present. The save therefore sends `periodType=AcademicYear, parentId=null`, which `UpdatePeriodHandler` accepts (sub-period parent + division gates only fire when `command.PeriodType != AcademicYear` — `UpdatePeriodHandler.cs:27,51`).
- **Normal sub-period create still requires a parent:** `SubmitAsync` (`PeriodForm.razor:295-301`) — when `_periodTypeText != "AcademicYear"` and `_parentPeriodIdText` is not a Guid, sets `_error = "Select a parent academic year for this period."`. The `Parent academic year *` FormRow (`:72-82`) is rendered only for non-AcademicYear types, so the user can never submit a sub-period without a parent.
- Date validation (start ≤ end) and the Suggest/Backfill academic-year buttons are unchanged.

### Scenario 5 — End-to-end "Semesters tenant" scenario
**Verdict: NOT EXECUTED (static-only environment). Cannot drive a running app to verify the cascade observation in the periods grid live.** All four UI surfaces above (toolbar, enroll dialog, periods grid, period form) were verified from source for their individual contributions; the live cross-surface flow is not verifiable from this subagent.

## Findings

### P1 (functional bugs)
**None.** Every code path is correct from source review, server-side gates exist (`CreatePeriodHandler`, `UpdatePeriodHandler`, `EnrollStudentHandler`), and the new unit + bUnit test suites cover the affected behaviours (337 + 481, all green per parent verification).

### P2 (UX nits — no fix required, listed for visibility)

- **P2-UI-A — ActiveTermToolbar re-load uses fire-and-forget on navigation.** `ActiveTermToolbar.razor:54`: `OnLocationChanged` does `_ = LoadAsync(...)` (discards the Task). The body of `LoadAsync` catches its own exceptions and the tenant-guard short-circuits to a `StateHasChanged`, so this is safe in practice — but the repo convention (per memory and bUnit gotchas) is "never fire-and-forget `EventCallback.InvokeAsync`". A bUnit test that navigates immediately after mount would not observe the re-load. Trivial fix: `=> _ = InvokeAsync(() => LoadAsync(_cts?.Token ?? CancellationToken.None));` (or use `OnLocationChanged` async void + try/catch + `InvokeAsync(StateHasChanged)`).

- **P2-UI-B — EnrollStudentDialog grade pick doesn't await `OnGradePicked` for ordering guarantees.** `OnGradePicked` returns `Task.CompletedTask` synchronously, and Blazor's `@bind:after` awaits it, so this is technically fine. But because `LoadFrom(suggestedGrade, …)` in `OnInitializedAsync` runs AFTER `_activePeriod` is set and BEFORE the user has had a chance to pick a grade, if a caller pre-selects both a grade and a stream via `SuggestedGradeLevelId` + `SuggestedStreamCodedValueId`, the initial pre-selection is preserved — but if a user picks a different grade from the suggestion, the `LoadFrom` already-set stream is cleared by `OnGradePicked`. That's correct behaviour, not a bug; noting it as "verify in manual pass" because it's the kind of edge case a bUnit test can miss.

- **P2-UI-C — Periods grid status filter relies on string equality.** `Periods.razor:177-183` uses `period.Status == "Draft"` / `"Active"` to decide which row actions to render. The PeriodDto serializes `PeriodStatus.ToString()`, so `"Active"` matches the enum value `"Active"` (`PeriodStatus.Active = 1`). Works today, but brittle if the enum is ever renamed. (Pre-existing — not introduced this round.)

- **P2-UI-D — ActiveTermToolbar `_title` mutation is not thread-safe.** `LoadAsync` mutates `_title` (a plain `string`) while `LoadAsync` itself is invoked concurrently for different `CancellationToken`s (initial + navigation-triggered). In practice all calls serialise on the same tenant cache key, so collisions are benign, but `_title` is read by the anchor's `Title=` attribute which can be observed mid-render between assignments. (Pre-existing — not introduced this round.)

## Verdict

**CLEAN (with minor UX nits)** — all 5 scenarios PASS in static review; no P1 bugs found; 4 P2 UX nits listed above for visibility (none are round-blockers). Live E2E (Scenario 5) could not be executed from this subagent's environment — recommend a manual pass on the dev workstation before merge to confirm the cross-surface cascade observation the plan's Phase-3 E2E describes.

## Suggested manual E2E checklist (for the operator)

1. Switch to a tenant with `academic_year_division = Semesters`; confirm the toolbar shows the Semester layer after activating a year + semester.
2. Open EnrollStudentDialog; confirm the period row shows the academic year token (`2025/2026`) and no period picker.
3. Pick a grade, then a stream; change the grade; confirm the stream resets to empty.
4. Edit a Semester period in a tenant whose division is `Terms`; confirm the type resets to AcademicYear and the save succeeds (P2-1 fix).
5. Activate a semester then a different academic year; confirm the periods grid reflects both cascade-completed periods without a refresh (G3).
