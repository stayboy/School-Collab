# Acceptance — Period Hierarchy Review Findings Fix (Active-Period Determinism + UI Current-Period Gaps)

- **Status:** Implementation round completed 2026-08-28, branch `fix/period-findings-fix`; acceptance pass run; **REWORK REQUIRED (round 1)**
- **Authoritative spec:** `documents/specs/plan-period-findings-fix.md` (Approved — orchestrator)
- **Source specs (read-only):** `period-hierarchy-terms-semesters.md`, `active-period-per-tenancy.md`
- **Review doc:** `documents/specs/review-period-findings-fix.md` (ACCEPT — "OK with notes")

## Round Summary

- **Backend determinism (B1–B3, plan §3 Phase 1):** make `GetActiveSubPeriodAsync` deterministic (scoped to the active academic year's sub-periods, ordered `PeriodType` then `StartDate`), add `Status == PeriodStatus.Active` + deterministic ordering to `GetCurrentPeriodAsync` (provider + repository), and add the missing `SetNextPeriod` guards (target != self in the domain; handler validates target exists and is an AcademicYear).
- **UI current-period fixes (G1–G4, plan §3 Phase 2):** `EnrollStudentDialog` uses the dedicated `GetActiveAcademicYearAsync` endpoint instead of `ListPeriodsAsync` + `FirstOrDefault(Status == "Active")`; `ActiveTermToolbar` renders both hierarchy layers (active year + optional active sub-period) from the two endpoints; `Periods.razor` re-fetches the list after Activate/Complete so server-side cascade completions show without a manual refresh; `PeriodForm` filters Term/Semester options by the tenant's `academic_year_division` via the existing `ConfigFlagsApiClient.GetAcademicYearDivisionAsync()`.
- **Item 9 sweep:** grep for remaining client-side `Status == "Active"` period derivations and migrate any stragglers to the dedicated endpoints (verified pre-round: the only flat-list period derivations left are `EnrollStudentDialog.razor:499` and `ActiveTermToolbar.razor:85`, both in scope; other `Status == "Active"` hits are enrollment-status or display-only and out of scope).
- **Tests:** Phase 1 determinism tests (two active sub-periods of different types → deterministic result; `GetCurrentPeriodAsync` ignores Draft) + `SetNextPeriod` guard unit test; Phase-1/2 unit tests per plan §3 item 4 and the worker's judgment. No schema/migration changes anywhere in this round.

## Implementation Plan

Per-file change list (plan §3 + §5; the plan doc is authoritative):

| File | Change |
|---|---|
| `src/Students/SchoolCollab.Students.Core/Tenancy/ActivePeriodProvider.cs` | **B1** — `GetActiveSubPeriodAsync`: resolve the active academic year first, scope to `ParentPeriodId == activeYearId`, keep `Status == Active && PeriodType != AcademicYear`, order `OrderBy(PeriodType).ThenBy(StartDate).FirstOrDefault()`; keep the existing cache key/tag (`active-sub-period:{tenantId}`, tag `"students"`). **B2** — `GetCurrentPeriodAsync`: add `Status == PeriodStatus.Active` + deterministic ordering (document year-vs-sub-period preference). |
| `src/Students/SchoolCollab.Students.Core/Data/Repositories/PeriodRepository.cs` | **B2** — `GetCurrentPeriodAsync`: add `Status == PeriodStatus.Active` filter + deterministic ordering to match the provider. |
| `src/Students/SchoolCollab.Students.Core/Domain/Period.cs` (+ `SetNextPeriod` handler) | **B3** — domain guard `target != self` (the FR-H11 self-is-AcademicYear guard already exists); handler lookup validates target existence + `PeriodType == AcademicYear`. |
| `src/Students/SchoolCollab.Students.Application/Components/Students/EnrollStudentDialog.razor` | **G1** — replace `ListPeriodsAsync` + `FirstOrDefault(Status == "Active")` (~lines 490–499) with `await Api.GetActiveAcademicYearAsync()`; update the stale comment block. |
| `src/Students/SchoolCollab.Students.Application/Components/Toolbar/ActiveTermToolbar.razor` | **G2** — fetch both `GetActiveAcademicYearAsync()` and `GetActiveSubPeriodAsync()`; render year + optional sub-period (e.g. `2025–2026 · Term 1`) with null fallbacks. |
| `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/Periods.razor` | **G3** — after successful `ActivatePeriodAsync`/`CompletePeriodAsync`, re-fetch via `ListPeriodsAsync` instead of optimistic single-row mutation. |
| `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodForm.razor` | **G4** — fetch division via `ConfigFlagsApiClient.GetAcademicYearDivisionAsync()` on init; hide/disable the non-matching Term/Semester `FluentOption` + hint. |
| Students Core/Application test projects (`tests/...`) | Phase 1 (B1/B2/B3) + Phase-1/2 unit tests per plan §3 item 4. |

## Worker Report

Implemented B1–B3 + G1–G4 + tests on `fix/period-findings-fix`.

- **B1** — `ActivePeriodProvider.GetActiveSubPeriodAsync`: active-year scoping (`ParentPeriodId == activeYearId`) + deterministic ordering (`PeriodType`, `StartDate`); cache key/tag preserved. **B2** — `GetCurrentPeriodAsync` (`ActivePeriodProvider` + `PeriodRepository`): `Status == PeriodStatus.Active` + deterministic ordering (sub-period over year, Term before Semester, earliest start). **B3** — domain self-guard `Period.SetNextPeriod` + unit test.
- **G1** — `EnrollStudentDialog` now resolves the active academic year via `GetActiveAcademicYearAsync` (flat-list derivation removed). **G2** — `ActiveTermToolbar` renders year · sub-period from both endpoints with null fallbacks. **G3** — `Periods.razor` re-fetches after Activate/Complete. **G4** — `PeriodForm` gates Term/Semester options by `academic_year_division` with a hint and type-reset fallback.
- **Supporting change:** `StudentsApiClient.GetActiveAcademicYearAsync`/`GetActiveSubPeriodAsync` upgraded to the body-read error pattern (preserves tracing-detail error UX asserted by existing tests).
- **Deviations:** (1) B3 handler-level guard has no attachment point — no `SetNextPeriod` command handler/endpoint exists in the codebase, so only the domain guard + unit test landed (reviewer accepted; documented residual). (2) G4 needed no new client method — `ConfigFlagsApiClient` was already registered.
- **Worker self-verification:** build 0 errors; Students.Tests.Unit 337/337 passed; Admin.Tests.Unit 481/481 passed (2 new G4 tests + updated EnrollStudentDialog tests); Students.Api.Tests.Unit 1/1 passed; NoUncommittedModelChanges passed.

## Reviewer Findings

Full report: `documents/specs/review-period-findings-fix.md` (verdict **ACCEPT — "OK with notes"**). All findings are P2.

| # | Location | Finding | Smallest fix |
|---|----------|---------|--------------|
| P2-1 | `PeriodForm.razor:247-248` | G4 type-reset sets `_periodTypeText = "AcademicYear"` but leaves `_parentPeriodIdText` populated → save sends a non-null `ParentPeriodId` for an AcademicYear → server rejects | Clear `_parentPeriodIdText` in the same reset block |
| P2-2 | `EnrollStudentDialog.razor:435-440` | XML doc references non-existent `OnGradeCodedValueChanged`; actual handler is `OnGradePicked` (which clears the stream on grade change; the wire value is `CodedValueId`) | Update `<see cref>` + reword comment |
| P2-3 | `EnrollStudentDialog.razor:481` | Dangling unterminated `<summary>` XML doc line before `OnInitializedAsync` (refers to the removed flag-OFF GradeLevel dropdown) | Remove the broken comment line |
| P2-4 | `PeriodForm.razor:197-203` | `DivisionHint` switch default arm is unreachable (hint only renders when `_division is not null`) | Remove dead branch or add a clarifying guard comment |

**Residual risks / deviations (reviewer + parent):**

- **B3 handler-level guard:** no `SetNextPeriod` command handler/endpoint exists, so only the domain self-guard was added (accepted deviation — no reachable call path today). Any future wiring of `NextPeriodId` must add handler-level validation (target exists + is AcademicYear).
- **Endpoint/provider drift (parent-verified in code):** `GetActiveSubPeriodHandler` (`src/Students/SchoolCollab.Students.Core/CQRS/Periods/Queries/GetActiveSubPeriod/GetActiveSubPeriodHandler.cs`) still does `Status == Active && PeriodType != AcademicYear` with a bare `FirstOrDefaultAsync` — no parent-scoping, no deterministic ordering — while sharing cache key `periods:active-sub-period:{tenantId}` with the provider. The UI (`ActiveTermToolbar` G2, `JoinGroupsDialog`) calls this endpoint, so B1's determinism must also land in the handler.

## Parent Build/Test Numbers

Authoritative (parent-run, independent of worker/reviewer):

- `dotnet build`: **0 errors** (4 warnings, pre-existing).
- `Students.Tests.Unit`: **337/337 Passed**, 0 failed.
- `Admin.Tests.Unit`: **481/481 Passed**, 0 failed (includes 2 new G4 tests + updated EnrollStudentDialog tests).
- `Students.Api.Tests.Unit`: **1/1 Passed**, 0 failed.
- `NoUncommittedModelChanges`: **Passed**.

## Acceptance Verdict

**REWORK REQUIRED (round 1).** The B1/B2/B3 + G1–G4 implementation is accepted as sound (reviewer ACCEPT, build 0 errors, all unit tests green), but closure requires:

1. **`GetActiveSubPeriodHandler` alignment** — the HTTP endpoint the UI actually calls must match the provider's deterministic B1 logic (parent-verified drift; under one active Term + one active Semester the endpoint result is currently arbitrary).
2. **P2-1** — functional bug: stale `_parentPeriodIdText` after G4 type reset makes PeriodForm saves fail server-side.
3. **P2-2/P2-3/P2-4** — cheap comment/dead-code cleanups, included in the same round (all four are minutes of work).

## Rework Round 1

**Scope (5 fixes, smallest correct diffs):**

1. **Handler alignment (endpoint determinism)** — `src/Students/SchoolCollab.Students.Core/CQRS/Periods/Queries/GetActiveSubPeriod/GetActiveSubPeriodHandler.cs`: inside the existing cache factory, first resolve the active academic year exactly like `ActivePeriodProvider.GetActiveSubPeriodAsync` does (query `Status == PeriodStatus.Active && PeriodType == PeriodType.AcademicYear`, select `Id`, `FirstOrDefaultAsync`; return `null` if none), then scope the sub-period query with `p.ParentPeriodId == activeYearId` and order `OrderBy(p => p.PeriodType).ThenBy(p => p.StartDate)` before `FirstOrDefaultAsync`. **Keep the cache key `periods:active-sub-period:{tenantId}` and `tags: ["students"]` unchanged** so `RemoveByTagAsync` invalidation still works; both queries stay inside the single cached factory (one cache fill resolves year + sub-period). Mirror the provider's comments.
2. **P2-1** — `PeriodForm.razor:247-248` (the G4 reset block after the `if (_periodTypeText == "Term" && !AllowTerm) ... if (_periodTypeText == "Semester" && !AllowSemester) ...` lines): when the type is reset to `AcademicYear`, also clear `_parentPeriodIdText = "";` (an AcademicYear must not carry a `ParentPeriodId` or the server rejects the save). Collapse the two reset lines into one guarded block that clears both fields.
3. **P2-2** — `EnrollStudentDialog.razor:435-440`: in the `GradeCodedValueIdForFilter` XML doc, replace `<see cref="OnGradeCodedValueChanged"/>` with `<see cref="OnGradePicked"/>` and reword the sentence to describe what it actually does (a grade pick clears the stream selection via `OnGradePicked`; it does not resolve the grade for submission).
4. **P2-3** — `EnrollStudentDialog.razor:481`: delete the dangling unterminated `/// <summary>Selection state for the flag-OFF GradeLevel dropdown (the` line immediately above `OnInitializedAsync`.
5. **P2-4** — `PeriodForm.razor:197-203`: the `DivisionHint` switch's `_ =>` default arm is unreachable (the hint renders only when `_division is not null`, per the razor guard at line 69). A switch expression over a string needs exhaustiveness, so the minimal fix is a clarifying comment on the default arm (e.g. "// Unreachable in practice: the hint renders only when _division is not null; default kept for switch exhaustiveness."). A ternary rewrite is acceptable if preferred — do not change hint text.

**Constraints:** minimal diffs only; no doc edits; no schema/migration changes; keep cache key + `"students"` tag; `PeriodStatus.Active == 1` (enum numeric value) if touching test data; **do NOT run bare `dotnet test --nologo` (hangs in this environment) — use `dotnet build` then `dotnet test --no-build --filter ...`**; do NOT git commit.

**Required re-verification before reporting:** `dotnet build` (0 errors expected), then `dotnet test --no-build` on `Students.Tests.Unit` and `Admin.Tests.Unit` (all green expected; no new tests required for comment-only fixes, but existing suites must stay green). End the report with: changed files (paths + one-line what/why per file), build result, test counts.

### Rework Round 1 — Result (parent-verified)

All 5 fixes landed (3 files: `GetActiveSubPeriodHandler.cs`, `PeriodForm.razor`, `EnrollStudentDialog.razor`). Parent re-verification: build **0 errors**; `Students.Tests.Unit` **337/337**, `Admin.Tests.Unit` **481/481** (0 failed); handler now parent-scoped + deterministic with cache key/tag unchanged; P2-1 clear confirmed at `PeriodForm.razor:254`. **Round verdict: CLOSED (implementation + rework complete) — pending UI tester pass.**

## UI Tester Scope Handover

Manual E2E pass for the Students module periods UI. **Run the app via the Aspire AppHost** (`src/AppHost/SchoolCollab.AppHost` — it starts the API/gateway + Students/Admin frontends with service defaults; do not run projects individually).

Scope (UI delivered this round):

1. **ActiveTermToolbar year · sub-period rendering (G2):** with an active academic year and an active Term (or Semester), the toolbar shows both layers, e.g. `2025–2026 · Term 1`; with only an active year it shows just the year; with neither active it shows a sensible empty/fallback state.
2. **EnrollStudentDialog year-scoped enrollment (G1):** open the enroll dialog while a year + sub-period are active; the period context must resolve to the active **academic year** via the dedicated endpoint (not an arbitrary active row from the flat list), and the grade dropdown/stream invalidation on grade change (grade pick clears a previously picked stream) still behaves.
3. **Periods.razor cascade re-fetch (G3):** in the periods grid, Activate a sub-period then Complete/Activate a year or sibling — the grid reflects server-side cascade completions immediately after the action, without a manual browser refresh.
4. **PeriodForm division gating (G4 + P2-1 fix):** for a tenant with `academic_year_division = Semesters`, the Term option is hidden/disabled and the division hint shows; with `Terms`, Semester is gated; with the flag unset/None both are allowed. **Regression focus for P2-1:** create a sub-period (Term or Semester) under a year, then edit it in a form where the division disallows that type so the type resets to AcademicYear and save — the save must succeed (no stale parent id sent). Also verify a normal sub-period create still requires/keeps the parent year selection.
5. Suggested scenario: tenant with `Semesters` framework → create year + semester → activate both → verify toolbar shows both layers, enroll dialog uses the active year, and the periods grid reflects cascade closes without refresh.

## UI Tester Result

**Environment:** static-only (subagent had no shell/Playwright; ran the brief's fallback). Full report: `documents/specs/ui-tester-period-findings-fix.md`.

**Scenarios:** 1 (G2 toolbar) PASS · 2 (G1 enroll dialog) PASS · 3 (G3 cascade re-fetch) PASS · 4 (G4 + P2-1) PASS · 5 (live E2E) NOT EXECUTED — run on the dev workstation before merge.

**Findings + parent triage (no rework required):**

| # | Finding | Triage |
|---|---------|--------|
| P2-UI-A | `ActiveTermToolbar.razor:54` `_ = LoadAsync(...)` fire-and-forget | **False positive / correct pattern** — it is a `NavigationManager.LocationChanged` event subscription; the `void (object?, LocationChangedEventArgs)` signature cannot await. Repo's "await InvokeAsync" convention targets component EventCallback handlers, not event subscriptions. Body already guards with `_disposed` + try/catch. No change. |
| P2-UI-B | `OnGradePicked` returns `Task.CompletedTask` | Informational; awaited by `@bind:after`, correct. No change. |
| P2-UI-C | `Periods.razor:177-183` string compares on `Status` | Pre-existing display/action-label pattern outside round scope (reviewer verified same). Deferred. |
| P2-UI-D | `_title` mutated from concurrent `LoadAsync` calls | Pre-existing, benign (last-write-wins with `_disposed` guards). Deferred. |

**UI tester verdict: CLEAN.** Scenario 5 (live E2E with a Semesters tenant) remains as a manual pre-merge check on the dev workstation.

## Final Round Verdict

**CLOSED.** Orchestrator plan → worker implementation (B1–B3, G1–G4, sweep, tests) → reviewer ACCEPT + 4 P2s → rework round 1 (handler alignment + P2-1 functional bug + 3 cleanups) → parent re-verification (build 0 errors; Students.Tests.Unit 337/337, Admin.Tests.Unit 481/481, Students.Api.Tests.Unit green, NoUncommittedModelChanges green) → UI tester CLEAN (static). Deferred items recorded above.