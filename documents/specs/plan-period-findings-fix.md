# Plan — Period Hierarchy Review Findings Fix (Active-Period Determinism + UI Current-Period Gaps)

- **Status:** Approved (orchestrator) — implementation round started 2026-08-28, branch fix/period-findings-fix
- **Date:** 2026-08-28
- **Branch:** `fix/period-findings-fix`
- **Depends on:** `period-hierarchy-terms-semesters.md` (Implemented), `active-period-per-tenancy.md` (Implemented)

## 0. Context

A review of the per-tenant academic periods feature confirmed that the hierarchy
(`PeriodType` = AcademicYear | Term | Semester + self-referencing `ParentPeriodId`,
per `period-hierarchy-terms-semesters.md`) is implemented end-to-end, including
per-tenant partial-unique indexes for the "one active" invariants. The review
surfaced a small set of backend determinism findings and, more importantly, a set
of **UI gaps** in how the "current/active period" is resolved now that a tenant
can legitimately have **two active rows at once** (one AcademicYear + one
Term/Semester sub-period).

## 1. How the UI currently resolves the "current period" (per type)

Two competing mechanisms exist:

1. **Dedicated endpoints (correct, hierarchy-aware)** —
   `GET /students/periods/active-academic-year` and
   `GET /students/periods/active-sub-period` via
   `StudentsApiClient.GetActiveAcademicYearAsync/GetActiveSubPeriodAsync`. Used by:
   - `GradeLevels/Detail.razor:982` (active year)
   - `JoinGroupsDialog.razor:97–99` (active year + active sub-period; matches the
     group's `EnrollmentSpan` to the resolved `_activePeriodType`)
   - `TopicCreateDialog.razor:254`, `TopicAssignmentPeriodEditDialog.razor:49` (active year)

2. **Client-side derivation from the flat list (pre-hierarchy legacy)** —
   `periods.FirstOrDefault(p => p.Status == "Active")`:
   - `EnrollStudentDialog.razor:498` (comment still says "the single period with Status == Active")
   - `ActiveTermToolbar.razor:85` (renders "Active term: {name}")

## 2. Identified gaps / findings

| # | Area | Finding | Impact |
|---|------|---------|--------|
| G1 | UI | `EnrollStudentDialog` picks an arbitrary active period via `FirstOrDefault` from `ListPeriodsAsync`. Under the hierarchy two active rows can coexist; the list is ordered `StartDate DESC` so the sub-period usually wins. Grade enrollment attaches to the **AcademicYear** (FR-H9) and `EnrollStudent` rejects sub-period `PeriodId` — the dialog can target the wrong period or surface a confusing server rejection. | High |
| G2 | UI | `ActiveTermToolbar` shows one arbitrary active period as "Active term" — may show the year while a term is open (or vice versa). No notion of both hierarchy layers. | Medium (most visible surface in the app) |
| G3 | UI | `Periods.razor` optimistic activate flips only the clicked row to `Active` locally, but `ActivatePeriodHandler` also **completes** the prior active year/siblings server-side (cascade). The grid stays stale until manual refresh. Same applies to the optimistic `Complete` path. | Medium |

| G4 | UI | `PeriodForm` is framework-blind: offers Term/Semester unconditionally; a tenant on the wrong academic-year division only discovers it via a raw 422 (`PeriodFrameworkMismatchException`) after submit. | Low / UX |
| B1 | Core | `ActivePeriodProvider.GetActiveSubPeriodAsync` is ambiguous: no ordering, no parent scoping. The unique index permits one active Term **and** one active Semester simultaneously (different types), so the result is arbitrary. This feeds `JoinGroupsDialog`'s span matching. | High |
| B2 | Core | `PeriodRepository.GetCurrentPeriodAsync` has no `Status` filter and no deterministic ordering. | Low |
| B3 | Core | `Period.SetNextPeriod` doesn't validate the target (existence / is-AcademicYear / not-self) at the domain or handler level. | Low |

## 3. Fix plan

### Phase 1 — Core (backend)

1. **B1 — Deterministic active sub-period** (`Tenancy/ActivePeriodProvider.cs`):
   scope the query to the active academic year, filter
   `p.ParentPeriodId == activeYearId && p.Status == Active && p.PeriodType != AcademicYear`,
   then `OrderBy(p.PeriodType).ThenBy(p.StartDate).FirstOrDefault()`.
   Keep the existing cache key/tag so `RemoveByTagAsync("students")`
   invalidation continues to work unchanged.
2. **B2 — `GetCurrentPeriodAsync`** (repository + provider): add
   `Status == PeriodStatus.Active` and a deterministic ordering (document whether
   sub-period is preferred over year or vice versa, per display semantics).
3. **B3 — `SetNextPeriod` guard**: `Period.SetNextPeriod` already rejects
   sub-periods carrying `NextPeriodId` (the FR-H11 "self is AcademicYear" domain
   guard exists), so add only the missing guards: target != self (domain) plus a
   handler-level repo lookup validating that the target exists and is an
   `AcademicYear`; add a unit test.
4. Tests: two active sub-periods of different types →
   `GetActiveSubPeriodAsync` returns the deterministic one;
   `GetCurrentPeriodAsync` ignores Draft periods.

### Phase 2 — UI: single source of truth for the current period

5. **G1 — `EnrollStudentDialog`**: replace the `ListPeriods` + `FirstOrDefault`
   derivation with `await Api.GetActiveAcademicYearAsync()` (grade enrollment is
   year-scoped). Update the stale comment block (lines ~492–497).
6. **G2 — `ActiveTermToolbar`**: fetch **both**
   `GetActiveAcademicYearAsync()` and `GetActiveSubPeriodAsync()`; render e.g.
   `2025–2026 · Term 1` (year + optional sub-period) with sensible fallbacks
   when either is null. This becomes the only surface displaying both layers.
7. **G3 — `Periods.razor`**: after a successful `ActivatePeriodAsync` /
   `CompletePeriodAsync`, **re-fetch the list** (`ListPeriodsAsync`) instead of
   the optimistic single-row mutation, so server-side cascade completions are
   reflected without a manual refresh.
8. **G4 — `PeriodForm`**: fetch the tenant's academic-year division
   (`academic_year_division` feature-flag value) on init; when `Terms`/`Semesters`,
   hide the non-matching `FluentOption` (or disable it + show a hint), keeping
   client-side validation in sync with the server's 422 gates. The existing
   `ConfigFlagsApiClient.GetAcademicYearDivisionAsync()`
   (`src/SchoolCollab.Admin.Shared/Services/ConfigFlagsApiClient.cs`) already
   wraps `GET /api/config/flags/academic_year_division` — use it; no new client
   method is needed.
9. **Sweep**: grep for any remaining client-side `Status == "Active"` period
   derivation and migrate to the dedicated endpoints.

### Phase 3 — Validation

- `dotnet build` + run the Students test suite; `NoUncommittedModelChanges`
  must pass (no model changes expected — Phases 1–2 are query/UI only, no
  migration).
- Manual E2E: tenant with `Semesters` framework → create year + semester →
  activate both → verify:
  - toolbar shows both layers,
  - enroll dialog uses the active **year**,
  - join-groups matches a `Semester` span against the active semester,
  - periods grid reflects cascade closes without refresh.

## 4. Assumptions / out of scope

- The division flag is `academic_year_division` (FR-H6; the earlier
  "`school_framework`" naming in this plan was stale) and is already exposed:
  `GET /api/config/flags/academic_year_division` with a client wrapper
  `ConfigFlagsApiClient.GetAcademicYearDivisionAsync()` in
  `SchoolCollab.Admin.Shared`. G4 needs **no** new client method.
- No schema changes anywhere in this plan; no new endpoints required (existing
  active-academic-year / active-sub-period / sub-periods endpoints suffice).
- OpenEnded / DateRange activity-group enrollment spans are out of scope here
  (tracked by the activity-group enrollment backlog, Phase 8/10).

## 5. Affected files

| File | Change |
|---|---|
| `src/Students/SchoolCollab.Students.Core/Tenancy/ActivePeriodProvider.cs` | B1, B2 |
| `src/Students/SchoolCollab.Students.Core/Data/Repositories/PeriodRepository.cs` | B2 |
| `src/Students/SchoolCollab.Students.Core/Domain/Period.cs` (+ handler) | B3 |
| `src/Students/SchoolCollab.Students.Application/Components/Students/EnrollStudentDialog.razor` | G1 |
| `src/Students/SchoolCollab.Students.Application/Components/Toolbar/ActiveTermToolbar.razor` | G2 |
| `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/Periods.razor` | G3 |
| `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodForm.razor` | G4 |
| `tests/...` (Students Core/Application test projects) | Phase 1 + 2 tests |
