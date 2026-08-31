# Acceptance — Period Activation Guard & Atomic Period Create (r1)

Provider: pi (orchestrator ollama/glm-5.3-flash, worker ollama/deepseek-v4-flash:0731, reviewer ollama/kimi-k2.7-code, ui-tester ollama/minimax-m3)

> Spec: `documents/specs/period-activation-guard-atomic-create.md` · Plan: `plan-period-guard-atomic-create-r1.md` · Review: `review-period-guard-atomic-create-r1.md` · Branch: `feature/period-activation-guard-atomic-create`

## Round verdict

**ACCEPTED** (acceptance pass, r1).

The implementation matches the round plan and the spec: hard always-on activation guard (no feature flag), atomic single-`SaveChanges` create of year + sub-periods, 422 mapping, UI affordances in `Periods.razor` and `PeriodForm.razor`. The reviewer's full FR/AC matrix (FR-G1..G6, FR-C1..C6, NFR-G1/G2/C1/C2, AC-G1..G4, AC-C1..C4) is **PASS** with file:line evidence; no P1 blockers.

## Build / test results (re-verified independently by the parent)

| Check | Command | Result |
|---|---|---|
| Build | `dotnet build SchoolCollab.sln -c Debug` | **0 errors**, 6 warnings (pre-existing) |
| Students unit | `dotnet test tests/SchoolCollab.Students.Tests.Unit` | 382 total, **0 failures** |
| Admin unit | `dotnet test tests/SchoolCollab.Admin.Tests.Unit` | 505 total, **0 failures** |
| Students API unit | `dotnet test tests/SchoolCollab.Students.Api.Tests.Unit` | 1 total, **0 failures** |
| Architecture | `dotnet test tests/SchoolCollab.ArchitectureTests.Unit` | 20 total, **0 failures** |

Total: 908 tests, 0 failures. NFR-C2 satisfied.

## Reviewer findings disposition

3 × P2, all deferred as non-blocking polish (no rework this round):

| # | Severity | Location | Finding | Disposition |
|---|---|---|---|---|
| 1 | P2 | `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodForm.razor:~109` | `ShowSubPeriodsSection` has redundant literal OR comparisons (`_divisionSelect == nameof(...) == "Terms"`) — `nameof` already yields the literal | Deferred: readability nit, zero behavioral impact |
| 2 | P2 | `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodForm.razor:~262-280` | `AutoSplitSubPeriodsAsync` produces an invalid first half when the year range is exactly 1 day (unrealistic for academic years; helper should guard `dayCount >= 2`) | Deferred: edge-case robustness nit, unreachable with plausible inputs |
| 3 | P2 | `src/Students/SchoolCollab.Students.Core/CQRS/Periods/Commands/ActivatePeriod/ActivatePeriodHandler.cs:~28` | Guard message pluralizes division names ("at least one Terms first") — slightly awkward grammar | Deferred: spec allows exact wording flexibility; required elements (year name + required action) present |

No blockers. Candidates for a later polish round; not gating acceptance.

## Deviations

None reported by the worker. The reviewer confirmed no plan violations: every planned production file was changed as specified, `UpdatePeriod`/PUT endpoint, `SubPeriodsSection.razor`, `SubPeriodsListDialog.razor`, and `Edit.razor` are untouched (FR-C6), no new migrations (NFR-G1, `MigrationGuardTests` green), no feature flag, no commits. Test ripple changes (seed reordering / `None`-division switches / `CreatePeriodResult.YearId` migration) fall under plan task 7's "existing-test reconciliation, minimal diffs" mandate — a few affected files beyond the plan's non-exhaustive "verified list" table (e.g. `PeriodHierarchyContainmentTests`, `PeriodHierarchyDivisionGateTests`, `PeriodHierarchyTests`, `PeriodHierarchyTypeChangeTests`, `CreateSubjectForGradeHandlerTests`), but each is the same seed-reorder pattern the plan prescribes. No plan corrections needed.

## Changed files (summary)

Production: `PeriodGuardException.cs` (new), `ActivatePeriodHandler.cs`, `CreatePeriod.cs`, `CreatePeriodHandler.cs`, `IPeriodRepository.cs`, `PeriodRepository.cs`, `PeriodRoutes.cs`, `StudentsApiClient.cs`, `PeriodForm.razor` (+ `.razor.css`), `Periods.razor`. Tests: `PeriodGuardAndAtomicCreateTests.cs` (new), `PeriodFormSubPeriodsSectionTests.cs` (new), plus 15 existing-test reconciliations per plan task 7. Full inventory: `git status` on the branch — 26 modified + 4 new, all unstaged, nothing committed.

## UI-TESTER SCOPE HANDOVER (step 3)

The closed scope below is derived **only** from the worker's changed UI-relevant files. Out-of-scope areas are excluded explicitly.

**In scope (closed list — one surface per line):**

- `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodForm.razor` (+ `.razor.css`) — rendered at `/students/periods/create` via `Create.razor:27` and at `/students/periods/{Id}/edit` via `Edit.razor:62`; the new Sub-periods section (add/remove rows, Auto-split into 2, client validation mirroring FR-C2) appears **only in the Create flow** as originally intended (top-level Terms/Semesters create without `?parent=`), and must NOT appear in edit mode, `None`-division creates, or `?parent=` sub-period creates (FR-C5/FR-C6).
- `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/Periods.razor` (`/students/periods`) — disabled Activate action with "Add a draft Term/Semester to activate" tooltip on guarded Draft Terms/Semesters years with zero Draft sub-periods; enabled Activate on ≥1 Draft sub, `None` years, and sub-period rows; guard 422 message bar on activation failure (FR-G6).
- `src/Students/SchoolCollab.Students.Application/Services/StudentsApiClient.cs` (create-with-subperiods API path behind `PeriodForm` Save) — POST `/periods` body carries `subPeriods`; server 400/422 validation messages surface verbatim in the form's and the landing page's error bars (FR-C2/FR-C4 downstream display).
- Periods landing (`/students/periods`) navigation into Create and the form's Cancel/Save flows — entry points exercising the changed create path end-to-end (row "Add" action → Create page → Sub-periods section → Save success/422 → return to landing grid).

**Out of scope (explicitly excluded):**

- All Admin module pages other than the Periods landing page (`/students/periods`).
- All other modules (Subjects, Topics, Students, GradeLevels, Assignments, Tenancy, AI, etc.).
- Unchanged-by-design surfaces: `Edit.razor`'s Update/PUT flow, `SubPeriodsSection.razor`, `SubPeriodsListDialog.razor` (FR-C6 — regression-only via PeriodForm-in-edit hiddenness already covered above; do not test their internals).
- API endpoint semantics exercised only via the UI paths above (no direct API harness testing — covered by unit tests).

## Residual risks

- The two `PeriodForm.razor` P2s (redundant comparisons, 1-day auto-split edge) remain in code — cosmetic/robustness only.
- Guard message grammar (P2 #3) is user-visible on the 422 message bar; wording is acceptable per spec but may be refined later.