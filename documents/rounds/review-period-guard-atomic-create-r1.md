# Review — Period Activation Guard & Atomic Period Create (r1)

> Agent: pi reviewer (ollama/kimi-k2.7-code:cloud). The reviewer had no shell/write tools, so build/test commands and diff inventory were run by the parent and attested to the reviewer; the reviewer verified the implementation via file reads. This file is the reviewer's final report persisted verbatim by the orchestrator-parent.

## Summary
Reviewed the worker's branch `feature/period-activation-guard-atomic-create` against `documents/rounds/plan-period-guard-atomic-create-r1.md` and `documents/specs/period-activation-guard-atomic-create.md`. All planned production files were inspected via read/grep; the parent supervisor attested the build/test commands the reviewer could not run (no shell/write tools available). The implementation is focused, matches the plan, follows the locked decisions (hard always-on guard, no feature flag, no migration, atomic single-SaveChanges create), and the test matrix covers every AC. No P1 blockers found.

## FR/AC coverage

| ID | Status | Evidence |
|---|---|---|
| FR-G1 | PASS | `PeriodGuardException.cs` is a typed sealed domain exception; message names the year and required action (`Cannot activate {Division} academic year '{Name}'… Create and activate at least one {Division} first.`). |
| FR-G2 | PASS | `ActivatePeriodHandler.cs:19-32` inserts the guard immediately after `repository.GetAsync` and before `GetActiveAcademicYearAsync`, `period.Activate()`, or any `Complete()`/`UpdateAsync`. |
| FR-G3 | PASS | Guard condition explicitly skips `AcademicYearDivision.None`; `Activate_NoneDivisionYear_NoSubPeriods_Activates` test verifies 204-equivalent behavior. |
| FR-G4 | PASS | Sub-period activation path is untouched; `Activate_Term_UnderDraftYear_StillThrowsPeriodNotOpen` confirms parent-must-be-Active rule; FR-H4a auto-activation block remains after the guard. |
| FR-G5 | PASS | `PeriodRoutes.cs:137-140` catches `PeriodGuardException` and returns `Results.Json(new { ex.Message }, statusCode: 422)`. |
| FR-G6 | PASS | `Periods.razor:226-241` disables Activate for top-level Terms/Semesters Draft years with zero Draft subs and sets an explanatory tooltip title; `OnActivateAsync` already surfaces the 422 message in `_error`. |
| FR-C1 | PASS | `CreatePeriod.cs:23` adds optional `SubPeriods` parameter; `CreatePeriodHandler.cs:21-27` rejects non-empty list for `ParentPeriodId != null` or `Division == None` with `ArgumentException` (→ 400). |
| FR-C2 | PASS | `CreatePeriodHandler.cs:83-118` validates each definition for end≥start, containment in year range, and pairwise sibling overlap before any persistence. |
| FR-C3 | PASS | `CreatePeriodHandler.cs:121-139` creates year + sub-periods with `Period.Create(...).WithTenant(...)` and calls `repository.AddRangeAsync(all)` once; `PeriodRepository.cs:18-22` performs a single `AddRangeAsync` + `SaveChangesAsync`. |
| FR-C4 | PASS | `CreatePeriodHandler.cs:146` returns `CreatePeriodResult(year.Id, subPeriodIds)`; `PeriodRoutes.cs:99` returns `Results.Created(..., new { id, subPeriodIds })`. |
| FR-C5 | PASS | `PeriodForm.razor:82-122` renders Sub-periods section only for top-level Terms/Semesters create; supports add/remove rows and an `Auto-split into 2` helper; client-side `TryBuildSubPeriods` mirrors FR-C2. |
| FR-C6 | PASS | `UpdatePeriodRequest`/`UpdatePeriodHandler` not touched; `SubPeriodsSection.razor`, `SubPeriodsListDialog.razor`, `Edit.razor` unchanged per git-status verification. |
| NFR-G1 | PASS | No new migration; `MigrationGuardTests` passes (parent-attested). |
| NFR-G2 | PASS | Guard and persistence remain inside single command context; no new locks/transactions added. |
| NFR-C1 | PASS | `PeriodGuardAndAtomicCreateTests.cs` covers AC-G1..G4, AC-C1..C3 plus end<start and Semesters variant. |
| NFR-C2 | PASS | Parent-attested build 0 errors, 4 test projects 0 failures (see below). |
| AC-G1 | PASS | `Activate_TermsYear_WithDraftTerm_ActivatesAndAutoActivatesTerm` asserts 204-equivalent + term auto-activated. |
| AC-G2 | PASS | `Activate_TermsYear_ZeroSubPeriods_ThrowsGuard_AndLeavesPriorYearActive` asserts `PeriodGuardException`, year Draft, prior year still Active. |
| AC-G3 | PASS | `Activate_TermsYear_OnlyCompletedSubPeriods_ThrowsGuard` asserts no Draft candidate → guard. |
| AC-G4 | PASS | `Activate_NoneDivisionYear_NoSubPeriods_Activates` asserts None year activates unchanged. |
| AC-C1 | PASS | `Create_TermsYear_WithTwoSubPeriods_PersistsAllDraftAtomically` asserts 3 Draft rows, result ids match. |
| AC-C2 | PASS | `Create_OverlappingSiblingDefinitions_ThrowsOverlap_ZeroRows` and `Create_SubPeriodOutsideYearRange_ThrowsContainment_ZeroRows` assert zero rows on rejection. |
| AC-C3 | PASS | `Create_NoneDivisionYear_WithSubPeriods_ThrowsArgumentException` and `Create_SubPeriodWithList_ThrowsArgumentException` assert 400-equivalent rejection. |
| AC-C4 | PASS | `PeriodFormSubPeriodsSectionTests.cs` verifies section shows for Terms create, hidden for None / edit / sub-period create. |

## Best-practices / diff-quality check

- **`.github/copilot/rules/dotnet-best-practices.md`**: PASS. Typed `PeriodGuardException`; records for `SubPeriodDefinition`, `CreatePeriodResult`, and API DTOs; primary-constructor handlers; factory-created entities (`Period.Create`); XML `<summary>` on new public members; structured logging; no Mediator / no `Console.WriteLine` / no raw SQL / no edited migrations.
- **CSS isolation**: PASS. New styles live in `PeriodForm.razor.css`; no new inline `<style>` blocks.
- **Minimal focused diff**: PASS. No production behavior changes outside the listed files; `UpdatePeriod` and standalone sub-periods UI untouched.
- **Test migration / CreatePeriodResult ripple**: PASS. Existing handler tests migrated from `Guid` result to `.YearId`; key seed reorder verified in `PeriodHierarchyActivationTests`, `PeriodOverlapInvariantTests`, `StudentsStrictTenancyTests`, `ActivePeriodProviderTests`, etc.
- **No dead/duplicated code**: PASS. New repository method `AddRangeAsync` is used; `CreatePeriodIdResponse` is consumed.

## Build / test attestation (parent-attested)

The reviewer could not execute shell commands in its subagent; the parent supervisor ran them and reports:

| Project | Result | Counts |
|---|---|---|
| `dotnet build SchoolCollab.sln -c Debug --nologo -v q` | passed | 0 Error(s), 6 Warning(s) (pre-existing) |
| `dotnet test tests/SchoolCollab.Students.Tests.Unit` | passed | 382 total, 0 failed |
| `dotnet test tests/SchoolCollab.Admin.Tests.Unit` | passed | 505 total, 0 failed |
| `dotnet test tests/SchoolCollab.Students.Api.Tests.Unit` | passed | 1 total, 0 failed |
| `dotnet test tests/SchoolCollab.ArchitectureTests.Unit` | passed | 20 total, 0 failed |

Git status (parent-attested): exactly the worker's changed files are modified plus the two new test files and `PeriodGuardException.cs`, all unstaged, nothing committed.

## Findings

- [P2] `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodForm.razor:109` — `ShowSubPeriodsSection` includes redundant string/literal OR checks (`_divisionSelect == nameof(AcademicYearDivision.Terms) || _divisionSelect == "Terms"`) because `nameof` already yields `"Terms"`. Same for Semesters. Cleanest fix: remove the redundant literal comparisons.
- [P2] `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodForm.razor:262-280` — `AutoSplitSubPeriodsAsync` produces an invalid first half that starts before the year when the year range is exactly 1 day (academic years are never 1 day, but the helper should guard against `_hasYearRange && dayCount >= 2`). Minor edge-case robustness nit.
- [P2] `src/Students/SchoolCollab.Students.Core/CQRS/Periods/Commands/ActivatePeriod/ActivatePeriodHandler.cs:28` — guard message uses plural division names (`"at least one Terms first"` / `"Semesters first"`), which is slightly awkward grammatically. Spec allows exact wording flexibility and the required elements (year name + required action) are present.

No P1 issues found.

## Merge verdict
OK with notes — implementation matches the spec/plan, tests cover the AC matrix, build is clean, and the only findings are minor readability/edge-case nits.

ACCEPTANCE: PASS
P1_COUNT: 0