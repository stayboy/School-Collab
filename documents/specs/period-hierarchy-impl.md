# Period Hierarchy (Terms & Semesters) — Implementation Checklist

> Tracked checklist for `period-hierarchy-terms-semesters.md` (draft, 2026-08-26).
> Extends the shipped `active-period-per-tenancy.md` (FR-A1–A6).
> **Workflow:** stacked PRs — each phase branches from the previous phase's branch;
> merges deferred until the whole spec is complete, then merged bottom-up.
>
> **Spec is the source of truth; this is the granular backend phase tracker
> (Phases H1–H5) for `period-hierarchy-terms-semesters.md`.** Requirements live in
> the spec; this doc only tracks implementation steps. Forward backend work is
> sprint-ordered in `backend-implementation-backlog.md` (Sprint 1); the matching
> UI work is in `ui-implementation-backlog.md` (Sprint 1).
>
> **Status legend:** `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked
>
> **Hard dependency note:** This spec is the dependency referenced by
> `activity-group-enrollment.md` Rev. 3 (decision 14 / FR-43). Only the activity-group
> `WholeAcademicYear`/`Termly`/`Semester` spans need it; `OpenEnded` and `DateRange`
> spans do **not** and can ship before this spec lands.

---

## Phase H1 — Period type & hierarchy (additive, behavior-neutral)

- [x] **H1.1** Add `PeriodType` enum (`AcademicYear=0, Term=1, Semester=2`) to `Students.Core/Domain/`. Add `PeriodType` (NOT NULL, default `AcademicYear`) and nullable `ParentPeriodId` to `Period`. `Create`/`Update` accept type + parent; enforce: sub-period requires an `AcademicYear` parent; `AcademicYear` requires null parent. — *FR-H1, FR-H2*
- [x] **H1.2** Additive migration `<ts>_AddPeriodHierarchy` (`20260826164501_AddPeriodHierarchy`): add `period_type` (default 0) + `parent_period_id` (NULL, FK → `periods.id` `ON DELETE CASCADE`) to `periods`; back-fill `period_type = 0` for all existing rows. `NoUncommittedModelChanges` passes. — *NFR-H1, AC-H1*
- [x] **H1.3** Extend `IActivePeriodProvider` (Core) + `ActivePeriod` projection with `PeriodType`/`ParentPeriodId`; add `GetActiveAcademicYearAsync`/`GetActiveSubPeriodAsync`. Implement in `ActivePeriodProvider` (Students.Core). `GetActivePeriodAsync` returns the active `AcademicYear` (so `EnrollStudent`'s guard is unchanged). — *FR-H8*
- [x] **H1.4** Surface `periodType`/`parentPeriodId` on `PeriodDto` + API (read DTO + create/update request pass-through). — *FR-H12*
- [x] **H1.5** Unit tests: type/parent invariants + parent-aware no-overlap (6 new in `PeriodHierarchyTests.cs`); `NoUncommittedModelChanges` (`MigrationGuardTests`); provider resolves active year vs sub-period (`ActivePeriodProviderTests`). — *AC-H1, NFR-H4*

## Phase H2 — Relaxed active invariant + typed activation/completion

- [x] **H2.1** Partial unique indexes (`2026082617*_AddActivePeriodUniqueIndexes`): `(tenant_id) WHERE period_type = 0 AND status = 1` (one active AcademicYear) and `(tenant_id, parent_period_id, period_type) WHERE status = 1` (one active sub-period of each type per year). **Note:** the spec's §8.1 filter used `status = 0`, which is `Draft` — corrected to `status = 1` (`Active`). — *FR-H4, NFR-H3*
- [x] **H2.2** `ActivatePeriodHandler` hierarchy-aware auto-close: AcademicYear activate → auto-complete prior active AcademicYear (cascade sub-periods); Term/Semester activate → require parent AcademicYear Active (else `PeriodNotOpenException`), auto-close prior active sibling sub-period of same type. — *FR-H4, FR-H5, AC-H2..H5*
- [x] **H2.3** `CompletePeriod`: AcademicYear completion cascade-completes still-Active sub-periods; sub-period completion does NOT trigger promotion (no `PromotionService` exists yet — nothing to trigger). — *FR-H10, AC-H10*
- [x] **H2.4** Reject `NextPeriodId` on sub-periods (date-ordered within year); keep AcademicYear→AcademicYear chaining. — *FR-H11*
- [x] **H2.5** Unit tests (6 new in `PeriodHierarchyActivationTests.cs`): one active year + one active term; no two active years (second closes first + cascades); sibling term close; cascade completion; term activation without active year rejected; `SetNextPeriod` on a term throws. — *AC-H2..H5, EC-H4*

## Phase H3 — Academic-year division tenant setting (Settings context)

- [x] **H3.1** ~~Confirm tenant-setting storage~~ — RESOLVED: reuse the feature-flag machinery (extend `FlagKind` with `String` + nullable `Value` columns on `FeatureFlag`/`TenantFeatureFlagOverride`), not a new KV table. — *FR-H6, §8.2, §12.1*
- [x] **H3.2** Extend `FlagKind` (`String = 1`) + add nullable `Value` to `FeatureFlag` and `TenantFeatureFlagOverride`; additive migration `<ts>_AddFeatureFlagValue` (`2026082617*_AddFeatureFlagValue` — nullable columns). Seed the `academic_year_division` `FeatureFlag` (`Kind = String`, `Value = 'None'`, key `FEATURE:AcademicYearDivision`) via `SeedAcademicYearDivisionAsync`. `AcademicYearDivision` enum in `Settings.Core/Domain`. Threaded `Value` through commands/handlers/DTOs/route. — *FR-H6, NFR-H1*
- [x] **H3.3** Settings API: `GET`/`PUT` `/api/config/flags/academic_year_division`. Value ∈ None/Terms/Semesters. — *FR-H7, AC-H7, EC-H2*
- [x] **H3.4** Gate `Term`/`Semester` creation **and update** on the framework (Term requires `Terms`, Semester requires `Semesters`); `AcademicYear` always allowed. Implemented via cross-context `IAcademicYearDivisionProvider` (port in Students.Core; `AcademicYearDivisionProviderHttpClient` in Students.Api calling `GET /api/config/flags/academic_year_division`; `DefaultAcademicYearDivisionProvider` = "None" registered in `AddStudentsCore`, overridden by the HTTP client in Students.Api). Both `CreatePeriodHandler` and `UpdatePeriodHandler` throw `PeriodFrameworkMismatchException`; `PeriodRoutes` maps it to `422` on POST/PUT. — *FR-H7, AC-H7*
- [x] **H3.5** Unit tests: framework gates sub-period creation **done** (4 in `PeriodHierarchyDivisionGateTests`). The FR-H7 reverse switch-rejection (Settings PUT rejects changing the division while Students has non-completed sub-periods) is **done** via cross-context `ISubPeriodCountProvider` (Settings.Core port; `SubPeriodCountProviderHttpClient` in Settings.Api calling `GET /students/periods/sub-period-count` via `students-api` + `TenantForwardingDelegatingHandler`; `DefaultSubPeriodCountProvider` = 0 in `AddSettingsCore`). Settings PUT compares the new vs effective division and returns `422` when sub-periods exist or when the count is indeterminate (fail-closed). Students count query `GetSubPeriodCount` counts Draft/Active Term/Semester periods. — *AC-H7, EC-H2*

## Phase H4 — Containment, no-overlap, grade-enrollment guard, reads

- [x] **H4.1** Create/update validation: sub-period `[StartDate,EndDate]` contained in parent AcademicYear (`PeriodContainmentException`, maps to `422`); no sibling overlap within a year of the same type (existing `GetOverlappingPeriodsAsync` no-overlap invariant, now mapped to `422` on POST/PUT); cross-year-boundary sub-periods rejected (containment). — *FR-H3, AC-H6, EC-H3*
- [x] **H4.2** `EnrollStudentHandler` rejects a `Term`/`Semester` active period (grade enrollment is year-level — guards the active period type in addition to the existing PeriodId-match check). — *FR-H9, AC-H8*
- [x] **H4.3** Read endpoints: `GET /students/periods/active-academic-year`, `GET /students/periods/active-sub-period`, `GET /students/periods/{academicYearId}/sub-periods` (new `GetActiveAcademicYear`/`GetActiveSubPeriod`/`ListSubPeriods` queries + handlers). — *FR-H12*
- [x] **H4.4** Unit tests: containment (4 in `PeriodHierarchyContainmentTests`), grade-enrollment rejection (`ActiveSubPeriod_ThrowsYearLevelPeriodNotOpen…` in `EnrollStudentHandlerTests`), hierarchy reads (4 in `PeriodHierarchyReadTests`). — *AC-H6, AC-H8*

## Phase H5 — Wire to activity-group enrollment

- [x] **H5.1** After the activity-group Rev. 2/3 migration lands, enable `Termly`/`Semester`/`WholeAcademicYear` activity-group enrollment to attach memberships to the matching typed period of the active academic year. — *AC-H9, activity-group FR-43* (verified shipped in Phase 10; membership tests for all three spans)
- [x] **H5.2** Integration test: create `Termly` group → add membership → attaches to an Active `Term` of the active AcademicYear; `WholeAcademicYear` → AcademicYear period. — *AC-H9* (extended with Semester + provided-PeriodId + no-active-term tests)
- [ ] **H5.3** E2E/Playwright (seeded): open academic year → open term → create Termly activity group → enrol a student → verify membership `period_id` = the term. — *AC-H9* (deferred to Phase 6.2 — needs AppHost + seeded data)

---

## Cross-cutting / don't-forget

- [x] **Back-compat** — `AcademicYearDivision = None` tenants (no sub-periods) must be byte-identical in behavior to the shipped flow (one active academic-year period, year-level grade enrollment, year-to-year promotion). Regression test. — *NFR-H4, EC-H5* (`AcademicYearDivisionNoneBackCompatTests`, 4 tests)
- [x] **Tenancy** — Period + tenant-setting reads/writes strict-tenant; verify with `StudentsStrictTenancyTests`-style tests for sub-periods and the framework setting. — *NFR-H2* (3 sub-period tests in `StudentsStrictTenancyTests` + 2 Settings integration tests in `AcademicYearDivisionTenancyTests`)
- [x] **Cache invalidation** — `IActivePeriodProvider`'s `HybridCache` ("students" tag) keys cover the new active-academic-year / active-sub-period lookups; Activate/Complete handlers already invalidate by tag — confirm no stale sub-period lookups. — *active-period-per-tenancy §4.6/§10* (5 new tests in `ActivePeriodProviderTests`)
- [ ] **Open questions** — §12.1 RESOLVED (reuse feature-flag machinery — extend `FlagKind` + `Value` columns, no new table); §12.2 (cross-type auto-close) and §12.3 (sub-period `NextPeriodId`) are confirmed out of scope.

---

## Notes / change log

- _Checklist generated from `period-hierarchy-terms-semesters.md` (draft 2026-08-26). Source of truth: that spec._
- _Dependency direction: this spec's Phase H5 unblocks `activity-group-enrollment.md` Rev. 3 FR-43 (termly/semester/whole-year spans). The activity-group `OpenEnded`/`DateRange` spans do not depend on this spec._
- _Phase H5 verification round (see `plan-phase-h5.md`): H5.1 verified shipped (Phase 10); H5.2 extended with Semester + provided-PeriodId + no-active-term tests; None back-compat, sub-period/framework-setting strict-tenancy, and active-year/sub-period cache invalidation covered by new tests. H5.3 E2E deferred to Phase 6.2._