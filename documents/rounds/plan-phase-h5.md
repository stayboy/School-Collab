# Phase H5 Implementation Plan — Wire to activity-group enrollment

> **Spec:** `period-hierarchy-terms-semesters.md` (FR-H4/H5, FR-H9, AC-H9, NFR-H2, NFR-H4, EC-H5)
> **Tracker:** `period-hierarchy-impl.md` Phase H5 + cross-cutting items
> **Review basis:** `review-phases-completed.md` §3.1, §4.2, §5
> **Status:** Plan (this doc) — no code written yet. H5.3 (E2E/Playwright seeded flow) is **out of scope** (Phase 6.2 territory).

---

## 1. Goal

Verify and complete the H5 verification/coverage work: prove that activity-group
enrollment for period-aligned spans (`WholeAcademicYear`, `Termly`, `Semester`)
attaches memberships to the matching typed period of the active academic year
(H5.1/H5.2), and close the four cross-cutting gaps — `AcademicYearDivision = None`
back-compat regression (NFR-H4/EC-H5), strict-tenancy coverage for sub-periods and
the framework setting (NFR-H2), and cache-invalidation coverage for the new
active-academic-year / active-sub-period HybridCache keys (active-period-per-tenancy
§4.6/§10).

**No production-code changes are expected.** Backend Phase 10 already ships the
behavior (see §2); this round adds verification tests and ticks the tracker.

---

## 2. Current state (verified shipped — do not change)

| Item | Evidence |
|---|---|
| Span → typed-period resolution | `AddMembershipHandler.ResolveSpanAsync` default branch (`src/Students/SchoolCollab.Students.Core/CQRS/ActivityGroups/Commands/AddMembership/AddMembershipHandler.cs`): `WholeAcademicYear → PeriodType.AcademicYear`, `Termly → Term`, `Semester → Semester`; resolves the active AcademicYear via `periodRepository.GetActiveAcademicYearAsync()`, then the active sub-period of the required type via `GetActiveSubPeriodsAsync(activeYear.Id, requiredType)`; validates a caller-provided `PeriodId` (type match + parent = active year). |
| Framework compatibility gate on group creation | `CreateActivityGroupHandler` (FR-45): `Termly` requires division `Terms`, `Semester` requires `Semesters`; `WholeAcademicYear`/`OpenEnded`/`DateRange` framework-agnostic. Sub-period **create/update** gating is `PeriodFrameworkMismatchException` → 422 (already covered by `PeriodHierarchyDivisionGateTests`, 5 tests). |
| Grade enrollment stays year-level | `EnrollStudentHandler` guards `active.PeriodType != AcademicYear` (FR-H9/AC-H8) — covered by `ActiveSubPeriod_ThrowsYearLevelPeriodNotOpen_AndPersistsNothing`. |
| Cache keys + invalidation | `ActivePeriodProvider` keys `active-academic-year:{tenantId}`, `active-sub-period:{tenantId}`, `current-period:{tenantId}`, tag `"students"`, explicit `TenantId` predicate inside the factory (tenant context does not flow into HybridCache factories). `ActivatePeriodHandler` + `CompletePeriodHandler` call `cache.RemoveByTagAsync("students")` after every transition (cascade-completes included — same save + same invalidation). |
| Existing H5.2 coverage | `ActivityGroupPeriodAlignedSpanTests.cs` — 6 passing tests incl. `Add_WholeAcademicYear_AttachesToActiveYear`, `Add_Termly_AttachesToActiveTerm`, `Add_Termly_WithAcademicYearPeriod_Throws`, `Create_Termly_WhenDivisionNone_Throws`, `Create_Termly_WhenDivisionTerms_Succeeds`, `Rollover_Termly_ReenrollsIntoActiveTerm`. These run on the real handlers + real `HybridCache` + EF InMemory `StudentsTestScope`, which satisfies H5.2's "integration test" intent at the unit-hosted level (true API-level E2E is H5.3, out of scope). |
| Strict-tenancy pattern | `Tenancy/StudentsStrictTenancyTests.cs` — real scope + `TenantProvider.SetTenant` switching + `IgnoreQueryFilters(["Tenant"])` bypass + FR-4 default-tenant rejection. |

Baseline run (2026-08-27): `dotnet test tests/SchoolCollab.Students.Tests.Unit -- --filter "FullyQualifiedName~ActivityGroupPeriodAlignedSpan|FullyQualifiedName~ActivePeriodProviderTests|FullyQualifiedName~StudentsStrictTenancy|FullyQualifiedName~PeriodHierarchy"` → **40 passed, 0 failed**.

---

## 3. Gap analysis

| H5 item | Status | Gap |
|---|---|---|
| H5.1 (typed-period attachment) | ✅ Shipped (Phase 10) | None — verified by code read (§2). Test gap: `Semester` span has **no membership-level test** (only period-creation gate tests). |
| H5.2 (Termly → Active Term; WholeAcademicYear → AcademicYear) | 🟡 Partial | Both named scenarios covered; missing: Semester-span membership, positive provided-`PeriodId` happy path, no-active-term rejection. |
| Back-compat (NFR-H4, EC-H5) | ❌ None | No regression test pins the `AcademicYearDivision = None` tenant behavior. |
| Tenancy (NFR-H2) | 🟡 Partial | Period-overlap per-tenancy + FR-4 exist for AcademicYear periods only. No sub-period tenancy tests; framework setting has **zero tests** anywhere (Settings `GET/PUT academic_year_division` and its per-tenant resolution are untested). |
| Cache invalidation (active-period-per-tenancy §4.6/§10) | 🟡 Partial | `ActivePeriodProviderTests` never exercise `GetActiveSubPeriodAsync` and never prove tag invalidation clears the **new** keys after Activate/Complete. |

Note: `AddMembership` resolves periods via `IPeriodRepository` (direct query), so
membership attachment is not cache-sensitive; the HybridCache item concerns the
`IActivePeriodProvider` lookups (grade-eligibility, EnrollStudent's guard, API read
endpoints).

**Promotion caveat (important):** EC-H5's "year-to-year promotion" wording is
historical — the Worker `PromotionService` was **removed** (see
`docs/plans/2026-07-16-student-picker-transfer.md`; `SchoolCollab.Students.Worker`
now contains only `ActivityGroupRolloverService` + `CodedValueBackfillService`).
Grade movement is handled by Student Transfer. Back-compat therefore asserts the
period lifecycle + year-level grade enrollment, **not** a promotion service.

---

## 4. Change list (tests only + tracker)

All Students unit tests live in `tests/SchoolCollab.Students.Tests.Unit/`
(MSTest runner — use `dotnet test <proj> -- --filter "..."`, note the `--` separator;
MTP filter syntax).

### 4.1 H5.2 extension — `ActivityGroupPeriodAlignedSpanTests.cs` (+3 tests)

1. **`Add_Semester_AttachesToActiveSemester`** — seed with a `Semesters`-division
   create handler (the existing `SeedYearAndTermAsync` hardcodes `"Terms"`:
   either parameterize it or add a sibling helper): AcademicYear AY2026
   (create + activate), Semester S1 2026-09-01→2027-01-31 (`PeriodType.Semester`,
   parent = year; create + activate). `Semester`-span group + student →
   `AddMembership` → `membership.PeriodId == semesterId`.
2. **`Add_Termly_WithProvidedTermId_Succeeds`** — existing year+term seed;
   `AddMembership(group.Id, sid, PeriodId: termId)` → membership attaches to
   `termId` (positive coverage of the provided-PeriodId branch: type match +
   parent-in-active-year).
3. **`Add_Termly_NoActiveTerm_Throws`** — year created + activated, **no term**;
   Termly group (division `Terms`) → `AddMembership` throws
   `EnrollmentSpanMismatchException`.

### 4.2 Back-compat — new `AcademicYearDivisionNoneBackCompatTests.cs` (4 tests)

Use `StubAcademicYearDivisionProvider("None")` everywhere; scope per
`StudentsTestScope` (unique in-memory DB name per test, existing convention).

1. **`None_AcademicYearLifecycle_SingleActiveYear`** — with division `None`:
   create + activate AY2025, create + activate AY2026 → AY2025 `Completed`,
   AY2026 `Active` (the shipped single-active invariant is byte-identical under
   `None`; mirrors `PeriodHierarchyActivationTests` but under the None framework).
2. **`None_GradeEnrollment_AttachesToActiveAcademicYear`** — the full shipped
   enrollment path with division `None`: create + activate year, then
   `EnrollStudentHandler` wired with the **real** `ActivePeriodProvider(s.Db,
   s.Tenants, s.Cache)` (not a stub) + the construction pattern from
   `EnrollStudentHandlerTests` (`StudentEnrollmentRepository`, `InMemoryGradeLevelRepository`,
   `StubCodedValuesApiClient`, recording publisher, `StubFeatureFlagService(false)`,
   `CompositeEnrollmentSpecification`, seeded `GradeLevel` row so no coded-values
   API call happens). Assert: enrollment persisted with `PeriodId == yearId`,
   `StudentEnrolled` enqueued, and the FR-H9 year-level guard did not fire.
3. **`None_TermlyAndSemesterGroups_Rejected_WholeYearAllowed`** —
   `CreateActivityGroupHandler` with None stub: `Span: Termly` →
   `EnrollmentSpanIncompatibleException`; `Span: Semester` →
   `EnrollmentSpanIncompatibleException` (new coverage); `Span: WholeAcademicYear`
   → succeeds (framework-agnostic).
4. **`None_WholeAcademicYearMembership_AttachesToActiveYear`** — year only (no
   sub-periods can exist under None): create + activate year, WholeAcademicYear
   group + student → membership `PeriodId == yearId`. This pins the
   pre-hierarchy-compatible activity-group flow for None tenants.
   *(Sub-period creation under None is already covered by `PeriodHierarchyDivisionGateTests` — do not duplicate.)*

### 4.3 Tenancy (NFR-H2)

**Students unit — extend `Tenancy/StudentsStrictTenancyTests.cs` (+3 tests)**, reusing
its `AsTenant`/`AsDefault` helpers and `StubAcademicYearDivisionProvider`:

1. **`AC_H2_SubPeriod_IsTenantScoped_AndPerTenantCreatable`** — Tenant A creates
   year + term; Tenant B sees 0 periods (filter isolation); B creates its own
   year + term with the same names/dates (per-tenant creatable); bypass
   `IgnoreQueryFilters(["Tenant"])` shows 2 years + 2 terms.
2. **`AC_H2_SubPeriod_Activation_IsTenantScoped`** — Tenant B attempts
   `ActivatePeriod` on Tenant A's term id → `PeriodNotFoundException`
   (tenant-filtered `GetAsync`); A's term row remains `Active`.
3. **`FR4_CreateSubPeriod_UnderDefaultTenant_ThrowsBeforeAnyWrite`** —
   `AsDefault(s)` + `CreatePeriod(…, PeriodType.Term, parent)` →
   `TenantContextRequiredException`; `IgnoreQueryFilters(["Tenant"])` count == 0.

**Settings integration — new `tests/SchoolCollab.Settings.Tests.Integration/AcademicYearDivisionTenancyTests.cs` (+2 tests)**

The framework setting is owned by Settings; its strict-tenancy is proven at the
API boundary (the Students-side `AcademicYearDivisionProviderHttpClient` forwards
the tenant via the `"settings-api"` named client + `TenantForwardingDelegatingHandler`
— verified by code read of `Students.Api/Program.cs:76-82`; no Students-side test needed).

Test infrastructure requirements (mirror `ConfigApiTests`):
- `[TestClass] [DoNotParallelize]`, ClassInitialize/Cleanup with `ApiFactory`.
- **ApiFactory change** (`tests/SchoolCollab.Settings.Tests.Integration/ApiFactory.cs`):
  in `ConfigureWebHost`, replace the HTTP-backed count provider with the Core
  default so a division value change is testable without a running Students API:
  `services.RemoveAll<ISubPeriodCountProvider>(); services.AddSingleton<ISubPeriodCountProvider>(new DefaultSubPeriodCountProvider());`
  (same RemoveAll pattern as `StubEntityCodeGenerator` in the Students factory;
  no existing test depends on the HTTP count provider).
- **TestInitialize**: truncate `flag_audit_entries`, `tenant_flag_overrides`,
  `feature_flags CASCADE`, `outbox_messages` (as `ConfigApiTests` does), then **seed
  the global flag row** `FEATURE:AcademicYearDivision` (`FlagKind.String`,
  `Value = "None"`, idempotent) directly via `SettingsDbContext`, mirroring
  `SeedAcademicYearDivisionAsync` in `src/SchoolCollab.MigrationService/Program.cs:363`
  — the MigrationService does not run in the test factory, so without this seed
  GET returns 404 and PUT 404s.
- Per-request tenancy via the `x-tenant-id` header (shared Core `TestAuthHandler`
  prefers the header — same mechanism the Students integration factory documents).

1. **`AcademicYearDivision_IsTenantScoped`** — `PUT /api/config/flags/academic_year_division`
   with `x-tenant-id: {TenantA}` body `{ value: "Terms", reason: "test" }` → 204;
   `GET` with `x-tenant-id: {TenantA}` → `{ value: "Terms" }`; `GET` with a second
   tenant id → `{ value: "None" }` (global default) — the setting resolves and
   stores strictly per tenant.
2. **`AcademicYearDivision_PutRejectsInvalidValue`** — `PUT` value `"Quarterly"` →
   400 (request-shape guard; cheap sibling of the scoping test).

### 4.4 Cache invalidation — extend `ActivePeriodProviderTests.cs` (+5 tests)

Construct the provider directly (`new ActivePeriodProvider(s.Db, s.Tenants, s.Cache)`
— existing pattern; the scope's `Cache` is a real `HybridCache` over
`AddDistributedMemoryCache`, and tag removal is already proven in
`PeriodWizardOpenTermGateTests`).

1. **`GetActiveSubPeriod_ReturnsActiveSubPeriodForCurrentTenant`** — seed an
   activated Term (tenant-stamped via `((ITenantEntity)period).TenantId`, as the
   existing tests do) → provider returns it with `PeriodType == "Term"`.
2. **`GetActiveSubPeriod_ReturnsNullWhenNoneActive`** — empty scope → null.
3. **`GetActiveSubPeriod_IsolatedPerTenant`** — tenant A has an active Term;
   switch `TenantProvider` to TenantB → null (per-tenant key
   `active-sub-period:{tenantId}` never leaks).
4. **`Activate_SecondTerm_InvalidatesCachedActiveSubPeriod`** — the §4.6/§10 core:
   seed year + T1, activate T1, warm `GetActiveSubPeriodAsync` (→ T1), then
   `ActivatePeriodHandler.HandleAsync(ActivatePeriod(T2))` (auto-closes T1 and
   invalidates tag "students") → provider now returns T2, proving no stale
   sub-period lookup.
5. **`Activate_SecondYear_InvalidatesCachedActiveAcademicYear`** — warm
   `GetActiveAcademicYearAsync` on AY2025, activate AY2026 → provider returns
   AY2026 (AY2025's cached entry is gone).
6. **`Complete_AcademicYear_InvalidatesCachedYearLookups`** — warm the year
   lookup, `CompletePeriodHandler.HandleAsync(CompletePeriod(year))` →
   `GetActiveAcademicYearAsync()` returns null (also proves the Complete-path
   invalidation covers the new keys, incl. cascade-completed sub-periods).

*(Seed handlers via the existing `StudentsTestScope` + real
`CreatePeriodHandler`/`ActivatePeriodHandler`/`CompletePeriodHandler` as
`PeriodHierarchyActivationTests` does, or entity-direct like the current provider
tests — worker's choice, but tests 4–6 must go through the handlers so the real
`RemoveByTagAsync` path is exercised.)*

### 4.5 Tracker updates — `documents/specs/period-hierarchy-impl.md`

- Tick `[x]` **H5.1**, **H5.2**, and the cross-cutting **Back-compat**,
  **Tenancy**, **Cache invalidation** rows (leave **H5.3** and **Open questions**
  untouched; H5.3 is Phase 6.2 territory).
- Add a Notes/changelog line: "Phase H5 verification round (see
  `plan-phase-h5.md`): H5.1 verified shipped (Phase 10); H5.2 extended with
  Semester + provided-PeriodId + no-active-term tests; None back-compat,
  sub-period/framework-setting strict-tenancy, and active-year/sub-period cache
  invalidation covered by new tests. H5.3 E2E deferred to Phase 6.2."

Do **not** edit `review-phases-completed.md` (point-in-time review snapshot).

---

## 5. Acceptance criteria

1. **AC-1 (H5.1 verified)** — All three period-aligned spans resolve the matching
   typed period of the active academic year, demonstrated by membership tests for
   `WholeAcademicYear`, `Termly`, **and** `Semester` (new). No production-code
   change required.
2. **AC-2 (H5.2)** — `ActivityGroupPeriodAlignedSpanTests` has 9 passing tests:
   the 6 existing ones untouched plus the 3 new ones in §4.1.
3. **AC-3 (back-compat, NFR-H4/EC-H5)** — `AcademicYearDivisionNoneBackCompatTests`
   (4 tests) proves None tenants keep: single-active-year lifecycle, year-level
   grade enrollment via the real provider, Termly/Semester group rejection, and
   the WholeAcademicYear activity flow attaching to the active year.
4. **AC-4 (tenancy, NFR-H2)** — Sub-period create/activate is proven
   tenant-scoped + default-tenant-rejected (3 new tests in
   `StudentsStrictTenancyTests`); the `academic_year_division` setting is proven
   per-tenant at the Settings API (2 new integration tests).
5. **AC-5 (cache invalidation)** — The new active-academic-year /
   active-sub-period HybridCache keys are proven to invalidate on Activate and
   Complete (tag "students") — no stale sub-period lookups (5 new tests in
   `ActivePeriodProviderTests`).
6. **AC-6 (green)** — Full `SchoolCollab.Students.Tests.Unit` suite passes
   (baseline 299 + 15 new = 314 expected); `SchoolCollab.Settings.Tests.Integration`
   passes including the 2 new tests (Docker/Testcontainers available: 28.3.2).
   Pre-existing unrelated failures (OpenRouter live tests in Settings integration,
   if any) are out of scope and must be noted, not fixed.
7. **AC-7 (tracker)** — `period-hierarchy-impl.md` updated per §4.6.
8. **AC-8 (no production changes)** — `git status` shows changes only under
   `tests/` and `documents/specs/` (plus the Settings integration `ApiFactory.cs`
   test-host substitution in §4.3).

---

## 6. Test expectations (summary table)

| File | New tests | Asserts |
|---|---|---|
| `ActivityGroupPeriodAlignedSpanTests.cs` | +3 | Semester → active Semester; provided term PeriodId happy path; no active Term → `EnrollmentSpanMismatchException` |
| `AcademicYearDivisionNoneBackCompatTests.cs` (new) | +4 | None-tenant lifecycle/enrollment/activity-flow identical to shipped |
| `Tenancy/StudentsStrictTenancyTests.cs` | +3 | Sub-period isolation, cross-tenant activation rejected, FR-4 sub-period |
| `ActivePeriodProviderTests.cs` | +5 (or +6 with Complete) | Sub-period lookups + tag invalidation on Activate/Complete |
| `Settings.Tests.Integration/AcademicYearDivisionTenancyTests.cs` (new) | +2 | Division setting per-tenant GET/PUT; invalid value 400 |
| `period-hierarchy-impl.md` | doc | H5.1/H5.2 + cross-cutting ticked, changelog line |

Total: **+17 tests**, ~15–16 including the optional Complete-path variant.

---

## 7. Out of scope

- **H5.3** E2E/Playwright seeded flow (AppHost + seeded data — Phase 6.2).
- Any production-code change in `src/` (behavior already shipped; if a test
  uncovers a genuine defect, **stop and report back** instead of fixing inline).
- Editing `review-phases-completed.md`.
- The 3 pre-existing Settings-integration OpenRouter live-test failures.

---

## 8. Risks / notes for the worker

- **MTP filter syntax:** this solution's test projects use the MSTest runner
  (`EnableMSTestRunner`, `OutputType=Exe`). Filter args must follow `--`
  (`dotnet test <proj> -- --filter "FullyQualifiedName~X"`); without the `--`
  VSTest prints usage help and runs zero tests.
- **Settings integration tests need Docker** (Testcontainers Postgres + RabbitMQ);
  Docker 28.3.2 is available in this environment. They are `[DoNotParallelize]`.
- **Division PUT in the isolated Settings factory** hits the fail-closed count
  check (students-api unreachable → 422) — hence the `DefaultSubPeriodCountProvider`
  substitution in §4.3. Do not weaken the production fail-closed behavior.
- **InMemory provider limits:** relational-only features (partial unique indexes)
  are not enforced by the InMemory provider; tests must assert handler/domain
  behavior, not index enforcement (existing convention).
- **Seeding entities tenant-stamped:** mirror the `((ITenantEntity)period).TenantId = tenantId`
  pattern in `ActivePeriodProviderTests` for cross-tenant setups, and
  `WithTenant(s.Tenants)` for in-context creation.
---

## 9. Acceptance

> Performed by the parent (supervisor) after the worker child returned planning
> output instead of making edits — the parent took over the implementation and
> ran the verification. Review report persisted at
> `documents/specs/review-phase-h5.md`.

### Per-criterion verdict

| # | Criterion | Verdict | Evidence |
| --- | --- | --- | --- |
| AC-1 | H5.1 verified — all three period-aligned spans resolve the matching typed period | **PASS** | Membership tests for WholeAcademicYear, Termly, Semester; no production change |
| AC-2 | H5.2 — `ActivityGroupPeriodAlignedSpanTests` has 9 passing tests | **PASS** | 6 existing + 3 new (Semester, provided-TermId, no-active-term) |
| AC-3 | Back-compat — None tenants byte-identical (lifecycle, grade enrollment, group rejection, WholeAcademicYear flow) | **PASS** | `AcademicYearDivisionNoneBackCompatTests` (4 tests) |
| AC-4 | Tenancy — sub-period + framework setting strict-tenant | **PASS** | 3 `StudentsStrictTenancyTests` + 2 `AcademicYearDivisionTenancyTests` |
| AC-5 | Cache invalidation — active-year/sub-period keys invalidate on Activate/Complete | **PASS** | 5 new `ActivePeriodProviderTests` |
| AC-6 | Green — build + all affected suites | **PASS** | Build 0 errors; Students 332/332, Settings Unit 446/446, Settings integration 2/2 |
| AC-7 | Tracker updated | **PASS** | `period-hierarchy-impl.md` H5.1/H5.2 + cross-cutting ticked; changelog line |
| AC-8 | No production changes | **PASS** | Changes only under `tests/` + `documents/specs/` + Settings integration `ApiFactory.cs` test-host substitution |

### Overall verdict

**CLOSED.**

### Residual items
- **H5.3** (E2E/Playwright seeded flow) remains open — deferred to Phase 6.2
  (needs AppHost + seeded data). Not a defect.
- 3 pre-existing Settings-integration OpenRouter live-test failures are unrelated
  and out of scope.
