# Review — Phase H5 verification round

> Reviewer report for `documents/specs/plan-phase-h5.md`.
> The 3-agent workflow's worker child returned planning output instead of making
> edits, so the parent (supervisor) took over the implementation and ran the
> verification. This review reflects the parent-run implementation + tests.

## Scope inspected
- Plan: `documents/specs/plan-phase-h5.md`
- `tests/SchoolCollab.Students.Tests.Unit/ActivityGroupPeriodAlignedSpanTests.cs` (+3)
- `tests/SchoolCollab.Students.Tests.Unit/AcademicYearDivisionNoneBackCompatTests.cs` (new, +4)
- `tests/SchoolCollab.Students.Tests.Unit/Tenancy/StudentsStrictTenancyTests.cs` (+3)
- `tests/SchoolCollab.Students.Tests.Unit/ActivePeriodProviderTests.cs` (+5)
- `tests/SchoolCollab.Settings.Tests.Integration/AcademicYearDivisionTenancyTests.cs` (new, +2)
- `tests/SchoolCollab.Settings.Tests.Integration/ApiFactory.cs` (test-host substitution)
- `documents/specs/period-hierarchy-impl.md` (tracker)

## Per-criterion verdict
| Criterion | Verdict | Notes |
| --- | --- | --- |
| AC-1 (H5.1 verified) | **Verified** | All three period-aligned spans resolve the matching typed period; membership tests for WholeAcademicYear, Termly, and Semester. No production-code change. |
| AC-2 (H5.2) | **Verified** | `ActivityGroupPeriodAlignedSpanTests` has 9 passing tests (6 existing + 3 new: Semester, provided-TermId, no-active-term). |
| AC-3 (back-compat, NFR-H4/EC-H5) | **Verified** | `AcademicYearDivisionNoneBackCompatTests` (4 tests): single-active-year lifecycle, year-level grade enrollment via real provider, Termly/Semester rejection, WholeAcademicYear activity flow. |
| AC-4 (tenancy, NFR-H2) | **Verified** | 3 sub-period tests in `StudentsStrictTenancyTests` (isolation, cross-tenant activation rejected, FR-4 sub-period); 2 Settings integration tests in `AcademicYearDivisionTenancyTests` (per-tenant GET/PUT, invalid value 400). |
| AC-5 (cache invalidation) | **Verified** | 5 new tests in `ActivePeriodProviderTests` (sub-period lookup, null, per-tenant isolation, Activate invalidates sub-period + year, Complete invalidates year). |
| AC-6 (green) | **Verified** | Full Students suite 332/332; Settings Unit 446/446; Settings integration 2/2 new tests pass. Build 0 errors. |
| AC-7 (tracker) | **Verified** | `period-hierarchy-impl.md` H5.1/H5.2 + Back-compat/Tenancy/Cache invalidation ticked; changelog line added. H5.3 left open (Phase 6.2). |
| AC-8 (no production changes) | **Verified** | Changes only under `tests/` + `documents/specs/` + the Settings integration `ApiFactory.cs` test-host substitution. No `src/` production change. |

## Findings
No code issues found. All implementation matches the plan and existing codebase
patterns. The `ApiFactory` substitution (`DefaultSubPeriodCountProvider`) is
test-host-only and does not weaken the production fail-closed behavior.

## Residual risks
- H5.3 (E2E/Playwright seeded flow) remains open — deferred to Phase 6.2 (needs
  AppHost + seeded data). Not a defect.
- The 3 pre-existing Settings-integration OpenRouter live-test failures are
  unrelated and out of scope.

## Final recommendation
**CLOSED.** All acceptance criteria verified; build + all affected suites green.
