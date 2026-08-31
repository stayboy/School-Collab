# Review — Period Hierarchy Findings Fix Round

- **Reviewer:** reviewer agent (kimi-k2.7-code)
- **Date:** 2026-08-28
- **Branch:** `fix/period-findings-fix` (working-tree diff vs `main`)
- **Plan:** `plan-period-findings-fix.md` (Approved)
- **Verdict: ACCEPT ("OK with notes")**

## Verified correct (from source)

- **B1** — `ActivePeriodProvider.GetActiveSubPeriodAsync` resolves the active academic year first, scopes to `ParentPeriodId == activeYearId && Status == Active && PeriodType != AcademicYear`, orders by `PeriodType` then `StartDate`; `"students"` cache tag preserved (`ActivePeriodProvider.cs:88-115`).
- **B2** — `GetCurrentPeriodAsync` in both `ActivePeriodProvider` (`:117-146`) and `PeriodRepository` (`:98-115`): `Status == PeriodStatus.Active` + deterministic ordering (sub-period over year, Term before Semester, earliest start), documented in comments.
- **B3** — `Period.SetNextPeriod` rejects `nextPeriodId == Id` (`Period.cs:142-150`); test `SetNextPeriod_OnSelf_Throws` (`PeriodHierarchyActivationTests.cs:146-151`).
- **G1** — `EnrollStudentDialog` resolves the active academic year via `Api.GetActiveAcademicYearAsync`; stale comments replaced (`EnrollStudentDialog.razor:404-411`, `OnInitializedAsync:492-496`).
- **G2** — `ActiveTermToolbar` fetches both endpoints, renders `year · sub-period` with null fallbacks (`ActiveTermToolbar.razor:80-91`, `DisplayName:117-128`).
- **G3** — `Periods.razor` re-fetches via `ReloadAsync()` after activate/complete; optimistic single-row mutation removed (`Periods.razor:139-152`, `ReloadAsync:180-200`).
- **G4** — `PeriodForm` injects `ConfigFlagsApiClient`, loads `academic_year_division`, gates Term/Semester options, shows a division hint, resets disallowed type to `AcademicYear` (`PeriodForm.razor:11`, `215-249`, `193-195`).
- **Item-9 sweep** — remaining `Status == "Active"` hits are action-label/display or enrollment-status logic (correctly left).
- **Supporting change** — `StudentsApiClient.GetActiveAcademicYearAsync/GetActiveSubPeriodAsync` use the body-read error pattern, preserving tracing UX asserted by `LoadError_ShowsErrorMessageBar_WithTracingDetail` (`StudentsApiClient.cs:1265-1290`).

## Findings (all P2)

| # | Location | Finding | Smallest fix |
|---|----------|---------|--------------|
| P2-1 | `PeriodForm.razor:247-248` | Resetting `_periodTypeText` to `AcademicYear` leaves `_parentPeriodIdText` populated → save sends non-null `ParentPeriodId` for an AcademicYear → server rejects | In the same reset block add `_parentPeriodIdText = "";` |
| P2-2 | `EnrollStudentDialog.razor:435-440` | XML doc references non-existent `OnGradeCodedValueChanged`; actual handler is `OnGradePicked`, wire value is `CodedValueId` | Update `<see cref>` + reword comment |
| P2-3 | `EnrollStudentDialog.razor:481` | Dangling unterminated `<summary>` XML doc line before `OnInitializedAsync`; refers to removed "flag-OFF GradeLevel dropdown" | Remove the broken comment line |
| P2-4 | `PeriodForm.razor:197-203` | `DivisionHint` switch has unreachable `null`/`default` arm (hint only rendered when `_division is not null`) | Remove dead branch or add clarifying guard comment |

## Residual risks / deviations

- **B3 handler-level guard:** no `SetNextPeriod` command handler/endpoint exists in the codebase, so only the domain self-guard was added (documented deviation). Acceptable — method has no reachable call path today. Any future wiring of `NextPeriodId` must add handler-level validation (target exists + is AcademicYear).
- **Endpoint/provider drift (P2-severity, flagged by parent verification):** `GetActiveSubPeriodHandler` (the HTTP endpoint the UI calls) still queries `Status == Active && PeriodType != AcademicYear` with a bare `FirstOrDefaultAsync` — no parent-scoping, no deterministic ordering (`GetActiveSubPeriodHandler.cs`). It shares cache key `periods:active-sub-period:{tenantId}` with the provider. Under clean data the results coincide, but with one active Term + one active Semester the endpoint result is arbitrary. Recommend aligning the handler with the provider's deterministic logic.

## Reviewer environment note

Reviewer had no shell/execute tools; it verified from source. Build + tests were run independently by the parent (authoritative): build 0 errors; Students.Tests.Unit / Admin.Tests.Unit / Students.Api.Tests.Unit all Passed, 0 failed.

## Merge verdict

**ACCEPT** — OK with notes (P2 findings + endpoint alignment recommended before merge).