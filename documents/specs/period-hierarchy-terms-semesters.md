# Spec: Period Hierarchy — Terms & Semesters (draft)

> **Status:** Draft (depends on the shipped `active-period-per-tenancy.md`).
> **Author:** spec-driven refinement
> **Date:** 2026-08-26
> **Owner contexts:** `SchoolCollab.Students.Core` (Period entity, provider),
> `SchoolCollab.Settings.Core` (school-framework tenant setting), `SchoolCollab.Core` (`IActivePeriodProvider`).
> **Depends on:** `active-period-per-tenancy.md` (FR-A1–A6, `IActivePeriodProvider`,
> the "one Active period per tenant" invariant), `global-tenant-filter.md`,
> `grade-level-setup.md`, `activity-group-enrollment.md` (Rev. 3 — this spec is the
> hard dependency referenced by its decision 14 / FR-43).

---

## 0. Decisions locked in this revision

1. **`Period` gains a `PeriodType` and a `ParentPeriodId`.** A `Period` is one of
   `AcademicYear | Term | Semester`. A `Term`/`Semester` carries a non-null
   `ParentPeriodId` pointing at its `AcademicYear`; an `AcademicYear` has a null
   parent. This is **additive** (new nullable columns + a defaulted type) so the
   shipped single-period-per-tenant tenants keep working: every existing row is
   back-filled as `PeriodType = AcademicYear`, `ParentPeriodId = null`.
2. **The "one Active period per tenant" invariant (FR-A2) is relaxed, not removed.**
   It becomes: **at most one Active `AcademicYear` per tenant**, with **at most
   one Active sub-period** (`Term` or `Semester`) active *within* that academic
   year at a time. So a tenant may have an Active `AcademicYear` **and** an Active
   `Term` simultaneously (the term is a sub-window of the year); it may not have
   two Active academic years, nor two Active terms in the same year.
3. **Grade enrollment stays AcademicYear-level.** `StudentEnrollment.PeriodId`
   continues to reference an `AcademicYear` period; the `EnrollStudent`
   open-period guard (FR-A3) resolves the **active AcademicYear**, unchanged in
   behavior. Terms/semesters exist to give **activity-group** enrollment
   (`WholeAcademicYear`/`Termly`/`Semester` spans) a period to attach to — they
   are **not** used for grade enrollment. This keeps promotion
   (academic-year → academic-year) untouched.
4. **A tenant "academic-year division" selects the sub-division.** `AcademicYearDivision`
   (`Terms | Semesters | None`) is stored as a **value-valued `FeatureFlag`** in
   the Settings context, reusing the existing Global-blueprint → Tenant-override
   → Resolver machinery (`FeatureFlag` + `TenantFeatureFlagOverride`) rather
   than a new table. `None` = single academic-year periods only (pre-hierarchy);
   `Terms`/`Semesters` enables the matching sub-period type. Creating a `Term`
   requires `AcademicYearDivision = Terms`; a `Semester` requires `Semesters`. This is
   what the activity-group `EnrollmentSpan` compatibility check (activity-group
   FR-45) reads. (The boolean-only `FlagKind` is extended with a `String` kind +
   a `Value` column — see §8.2; `FlagKind` is explicitly forward-compatible per
   its own doc comment.)
5. **Completing an AcademicYear completes its sub-periods and promotes.**
   `AcademicYear.Complete()` cascades to complete any still-Active sub-period,
   then the existing `PromotionService` carries grade enrollments into
   `NextPeriodId` (the next AcademicYear). Sub-period completion does **not**
   trigger promotion (grade enrollment is year-level).
6. **`NextPeriodId` chains AcademicYear → AcademicYear only.** Sub-periods are
   ordered by date within their year; they do not carry `NextPeriodId`. The
   existing academic-year chaining and promotion flow are unchanged.

## 1. Goal

Introduce an academic-calendar hierarchy on the flat `Period` model so that:

- A tenant's academic year can be divided into **terms** or **semesters**
  (selected per-tenant via the academic-year division), giving activity-group
  enrollment spans (`Termly`, `Semester`, `WholeAcademicYear` —
  `activity-group-enrollment.md` Rev. 3) a typed period to attach to.
- The shipped "one Active period per tenant" invariant is relaxed to a
  hierarchy-aware form (one active year + one active sub-period) without
  breaking existing single-period tenants or the grade-enrollment/promotion
  flow.

`OpenEnded` and `DateRange` activity-group spans (activity-group Rev. 4) do
**not** depend on this spec — they carry no `PeriodId`. Only
`WholeAcademicYear`/`Termly`/`Semester` spans do.

## 2. Context (what already exists)

| Concern | Today | File |
| --- | --- | --- |
| Period lifecycle | `Draft → Active → Completed → Archived` | `Domain/Period.cs`, `Domain/PeriodStatus.cs` |
| At-most-one-active | `ActivatePeriodHandler` auto-closes the prior Active period (FR-A1/A2) | `CQRS/Periods/Commands/ActivatePeriod/` |
| Active lookup | `IActivePeriodProvider` (Core) → `ActivePeriodProvider` (Students.Core) | `SchoolCollab.Core/Tenancy/IActivePeriodProvider.cs` |
| Next-period link | `Period.NextPeriodId` (academic-year chaining) | `Domain/Period.cs` |
| Promotion | `PromotionService` carries enrollments into `NextPeriodId` (academic-year → academic-year) | `Students.Worker/Services/PromotionService.cs` |
| Enrollment guard | `EnrollStudentHandler` requires the active period (FR-A3) | `CQRS/Enrollments/Commands/EnrollStudent/` |
| Period table | flat `periods` (name/start/end/status/next_period_id), tenant-scoped | `Data/Configurations/PeriodConfiguration.cs` |
| Tenant settings | Settings context owns `AcademicYearDivision` as a value-valued `FeatureFlag` | `Settings/SchoolCollab.Settings.Core` |
| Division provider | Students.Core port `IAcademicYearDivisionProvider`; Students.Api HTTP impl calls `GET /api/config/flags/academic_year_division`; default provider returns `None` | `Students.Core/Services/`, `Students.Api/Services/` |

The flat model has no `PeriodType` and no parent-child link; the
"one Active period per tenant" invariant (FR-A2) forbids an active academic
year and an active term coexisting. This spec relaxes that to a hierarchy.

## 3. Functional Requirements

> RFC 2119 keywords. IDs: `FR-H-N`.

### 3.1 Period type & hierarchy

- **FR-H1** — `Period` MUST carry a `PeriodType` (`AcademicYear = 0, Term = 1,
  Semester = 2`, NOT NULL, default `AcademicYear`). Existing rows are
  back-filled to `AcademicYear` by the migration.
- **FR-H2** — `Period` MUST carry a nullable `ParentPeriodId` (FK → `periods.id`,
  `ON DELETE CASCADE`). A `Term`/`Semester` MUST have a non-null parent whose
  `PeriodType = AcademicYear`; an `AcademicYear` MUST have a null parent.
  Creating a sub-period with a missing/non-academic-year parent MUST be
  rejected.
- **FR-H3** — A sub-period's `[StartDate, EndDate]` MUST be contained within its
  parent `AcademicYear`'s `[StartDate, EndDate]`. Two sub-periods of the same
  `PeriodType` within one academic year MUST NOT overlap (sibling
  no-overlap). Crossing year boundaries into another academic year is
  rejected.

### 3.2 Active-period invariant (relaxed)

- **FR-H4** — The per-tenant invariant becomes: **at most one Active
  `AcademicYear`**, and **at most one Active sub-period** within the active
  academic year, at any time. `ActivatePeriodHandler` MUST:
  - on activating an `AcademicYear`: auto-close the tenant's current Active
    `AcademicYear` (which per FR-H11 completes its still-Active sub-periods),
    then activate the new one;
  - on activating a `Term`/`Semester`: require its parent `AcademicYear` to be
    Active (else reject with `PeriodNotOpenException`), and auto-close the
    current Active sibling sub-period of the same `PeriodType` within that
    year, then activate the new one.
- **FR-H5** — Activating a `Term`/`Semester` whose parent academic year is not
  the tenant's active academic year MUST be rejected. (Sub-periods live only
  inside the active year.)

### 3.3 Academic-year division (tenant setting)

- **FR-H6** — The Settings context MUST expose `AcademicYearDivision`
  (`None = 0, Terms = 1, Semesters = 2`, default `None`) as a value-valued
  `FeatureFlag` (`Key = 'academic_year_division'`, `Kind = String`) with a per-tenant
  `TenantFeatureFlagOverride` carrying the value, via `GET`/`PUT
  `/api/config/flags/academic_year_division` (reuses the value-valued feature-flag
  surface added in §3.3/H3.3). It is tenant-scoped and strict. The `FlagKind`
  enum MUST be extended with a non-boolean kind and `FeatureFlag`/
  `TenantFeatureFlagOverride` MUST gain a nullable `Value` column (additive —
  §8.2, NFR-H1).
- **FR-H7** — Creating a `Term` period MUST require `AcademicYearDivision = Terms`;
  creating a `Semester` MUST require `Semesters`; **updating** an existing period's
  type to `Term`/`Semester` is gated identically (no create-then-update bypass).
  `AcademicYear` creation is always allowed (framework-agnostic). The Students
  context resolves the division through the cross-context port
  `IAcademicYearDivisionProvider` (HTTP impl in Students.Api calling Settings
  `GET /api/config/flags/academic_year_division`, fail-open default `None` when
  Settings is unreachable). A mismatch throws `PeriodFrameworkMismatchException`,
  which the API maps to `422 Unprocessable Entity`. Changing the framework
  to `None` MUST be rejected while `Term`/`Semester` periods exist for the tenant;
  changing between `Terms` and `Semesters` MUST be rejected while sub-periods exist
  (the tenant must complete/remove its sub-periods first). This is the value
  the activity-group `EnrollmentSpan` compatibility check reads
  (activity-group FR-45).

### 3.4 Active-period provider & grade enrollment

- **FR-H8** — `IActivePeriodProvider` MUST be extended to resolve: the active
  `AcademicYear` (`GetActiveAcademicYearAsync`) and the active sub-period, if
  any (`GetActiveSubPeriodAsync`). The `ActivePeriod` projection MUST carry
  `PeriodType` and `ParentPeriodId`. `GetActivePeriodAsync` (existing) returns
  the active `AcademicYear` (so `EnrollStudent`'s guard is unchanged in
  behavior — it enrols into the active academic year).
- **FR-H9** — `StudentEnrollment.PeriodId` MUST reference an `AcademicYear`
  period. `EnrollStudent` MUST reject enrollment into a `Term`/`Semester`
  period (grade enrollment is year-level). Activity-group enrollment attaches
  to `Term`/`Semester`/`AcademicYear` periods per the activity-group span
  (activity-group FR-43) — this is the consumer of the hierarchy.

### 3.5 Completion & promotion

- **FR-H10** — `AcademicYear.Complete()` MUST first complete any still-Active
  sub-period of that year (cascade), then complete the year. Sub-period
  `Complete()` does NOT trigger promotion. Only `AcademicYear` completion
  triggers `PromotionService` carry-forward into `NextPeriodId` (unchanged).
- **FR-H11** — `NextPeriodId` chains `AcademicYear → AcademicYear` only.
  Sub-periods MUST NOT carry `NextPeriodId` (they are date-ordered within
  their year). Setting `NextPeriodId` on a sub-period MUST be rejected.

### 3.6 Reads

- **FR-H12** — The Period API MUST expose `periodType` and `parentPeriodId` on
  `PeriodDto`, and MUST support listing sub-periods of an academic year
  (`GET /students/periods/{academicYearId}/sub-periods`). All reads are
  tenant-filtered.

## 4. Non-Functional Requirements

- **NFR-H1 (Additive migration)** — The migration MUST be purely additive:
  add `period_type` (NOT NULL default 0) and `parent_period_id` (NULL) to
  `periods`; back-fill `period_type = 0` for all existing rows; extend the
  existing `feature_flags`/`tenant_feature_flag_overrides` with nullable `Value`
  columns + a `FlagKind.String` enum value (no new table). No drop/type-change
  to existing columns. `NoUncommittedModelChanges` MUST pass for both contexts.
- **NFR-H2 (Tenancy)** — All period and setting reads/writes are strict-tenant.
  The framework setting and sub-periods are never cross-tenant.
- **NFR-H3 (Indexing)** — Hot paths lead with `tenant_id`: `(tenant_id,
  period_type, status)` for the active-year/active-sub-period lookups and
  `(tenant_id, parent_period_id, status)` for "sub-periods of a year".
- **NFR-H4 (Back-compat)** — Tenants that never adopt the hierarchy
  (`AcademicYearDivision = None`, no sub-periods) behave identically to today:
  one active academic-year period, year-level grade enrollment, year-to-year
  promotion.

## 5. Acceptance Criteria

- **AC-H1 (back-fill)** — Given the shipped flat periods, when the migration
  lands, then every existing `periods` row has `period_type = 0 (AcademicYear)`
  and `parent_period_id = null`, and the existing single-active invariant still
  holds. *(FR-H1, NFR-H1, NFR-H4)*
- **AC-H2 (active year + active term)** — Given tenant T with an Active
  `AcademicYear` Y and a `Term` T1 within Y, when T1 is activated, then Y
  stays Active and T1 becomes Active (both Active). *(FR-H4)*
- **AC-H3 (no two active years)** — Given tenant T has an Active `AcademicYear`,
  when another `AcademicYear` is activated, then the prior one is auto-completed
  (and its sub-periods cascade-completed) before the new one activates. *(FR-H4, FR-H10)*
- **AC-H4 (no two active sub-periods)** — Given tenant T has an Active `Term`
  T1 within Y, when T2 (another `Term` in Y) is activated, then T1 is
  auto-completed before T2 activates. A `Semester` cannot be activated while a
  `Term` is active if the framework is `Terms` (framework mismatch). *(FR-H4, FR-H6)*
- **AC-H5 (sub-period requires active year)** — Given Y is not Active, when a
  `Term` in Y is activated, then the request is rejected. *(FR-H5)*
- **AC-H6 (containment & no overlap)** — Given Y spans Sep–Jun, when a `Term`
  with dates outside Y is created, it is rejected; when two overlapping `Term`s
  in Y are created, the second is rejected. *(FR-H3)*
- **AC-H7 (framework gates sub-period creation)** — Given `AcademicYearDivision =
  None`, creating a `Term` is rejected; setting it to `Terms` allows `Term`
  creation; switching back to `None` while `Term`s exist is rejected. *(FR-H6, FR-H7)*
- **AC-H8 (grade enrollment stays year-level)** — `EnrollStudent` enrols into
  the active `AcademicYear` and rejects a `Term`/`Semester` `PeriodId`. *(FR-H9)*
- **AC-H9 (activity-group attaches to typed period)** — Given a `Termly`
  activity group, when a membership is added, it attaches to an Active `Term`
  period of the active academic year (cross-ref activity-group FR-43). *(FR-H9)*
- **AC-H10 (completion cascades)** — Completing an `AcademicYear` completes its
  Active sub-periods and triggers promotion; completing a `Term` does not
  promote. *(FR-H10)*

## 6. Edge Cases

- **EC-H1 (orphan sub-period)** — Deleting an `AcademicYear` cascades its
  sub-periods (`ON DELETE CASCADE`); deleting a sub-period leaves the year
  intact. A sub-period cannot be hard-deleted while activity-group memberships
  reference its period (the membership FK `ON DELETE RESTRICT` blocks it) —
  archive instead.
- **EC-H2 (framework switch mid-year)** — Switching `Terms`→`Semesters` while
  `Term`s exist is rejected; the tenant completes the year first. *(FR-H7)*
- **EC-H3 (sub-period crossing year boundary)** — A `Term` whose dates cross
  into a different academic year is rejected at create; the admin splits it or
  uses two sub-periods in two years. *(FR-H3)*
- **EC-H4 (activate year closes sub-periods)** — Activating a new `AcademicYear`
  while a `Term` of the old year is still Active cascades-completes that `Term`
  before the old year completes. *(FR-H4, FR-H10)*
- **EC-H5 (promotion unaffected)** — Promotion runs only on `AcademicYear`
  completion; a tenant with `AcademicYearDivision = None` sees no behavioral change
  vs. the shipped flow. *(FR-H10, NFR-H4)*

## 7. API Contracts

> Base path `/api` implied. Tenant applied by the global filter.

```ts
// Existing period endpoints gain periodType + parentPeriodId:
interface PeriodDto {
  id: string; name: string;
  startDate: string; endDate: string; status: "Draft"|"Active"|"Completed"|"Archived";
  nextPeriodId: string | null;
  periodType: "AcademicYear"|"Term"|"Semester";   // new
  parentPeriodId: string | null;                  // new
  createdAt: string; updatedAt: string;
}

// POST   /students/periods                       -> 201 (body: { name, startDate, endDate, periodType, parentPeriodId? })
// GET    /students/periods                       -> 200 PeriodDto[]
// GET    /students/periods/{id}                  -> 200 PeriodDto | 404
// PUT    /students/periods/{id}                  -> 204 | 422 (containment/type rules)
// POST   /students/periods/{id}/activate         -> 204 | 409 (FR-H4 auto-close semantics)
// POST   /students/periods/{id}/complete         -> 204 (AcademicYear cascades sub-periods)
// GET    /students/periods/active-academic-year -> 200 PeriodDto | 404
// GET    /students/periods/active-sub-period     -> 200 PeriodDto | 404
// GET    /students/periods/{academicYearId}/sub-periods -> 200 PeriodDto[]

// Academic-year division (Settings context — value-valued FeatureFlag, reuses existing override surface):
// GET /api/config/flags/academic_year_division -> 200 { value: "None"|"Terms"|"Semesters" }
// PUT /api/config/flags/academic_year_division -> 204 | 422 (reject switch while sub-periods exist)
```

## 8. Data Models

### 8.1 `periods` (Students context — additive)

| field | type | constraints |
|---|---|---|
| `id` | uuid | PK |
| `tenant_id` | uuid | NOT NULL; strict-tenant filter |
| `name` | text | NOT NULL, <= 200 |
| `start_date` | date | NOT NULL |
| `end_date` | date | NOT NULL |
| `status` | integer | NOT NULL; default 0 (Draft); enum `PeriodStatus` |
| `next_period_id` | uuid | NULL; `AcademicYear → AcademicYear` only (FR-H11) |
| `period_type` | integer | NOT NULL; default 0 (AcademicYear); enum `PeriodType` (NEW) |
| `parent_period_id` | uuid | NULL; FK → `periods.id` `ON DELETE CASCADE`; null for `AcademicYear` (NEW) |
| `xmin` | xid | row version |
| `created_at` | timestamptz | NOT NULL |
| `updated_at` | timestamptz | NOT NULL |

Indexes (NFR-H3): `(tenant_id, period_type, status)` → active-year/active-sub
lookups; `(tenant_id, parent_period_id, status)` → sub-periods-of-a-year;
`(tenant_id, status)` (existing) retained. A partial unique index
`(tenant_id) WHERE period_type = 0 AND status = 1` enforces "one active
AcademicYear per tenant" (FR-H4); a partial unique
`(tenant_id, parent_period_id, period_type) WHERE status = 1` enforces "one
active sub-period of each type per year" (FR-H4). (`status = 1` = `Active`;
`PeriodStatus.Draft` is 0 — the earlier draft used `status = 0` in error.)
(Postgres NULL-distinct does
not bite here — `parent_period_id` is non-null for sub-periods, and the
academic-year index is scoped by `period_type = 0`.)

### 8.2 `feature_flags` / `tenant_feature_flag_overrides` (Settings context — extend, no new table)

`AcademicYearDivision` reuses the existing feature-flag machinery (Global-blueprint
`FeatureFlag` + per-tenant `TenantFeatureFlagOverride` + resolver), extended to
carry a non-boolean value:

| change | detail |
|---|---|
| `FlagKind` enum | add `String = 1` (`Boolean = 0` unchanged) |
| `feature_flags.value` | new nullable `text` column (NULL for boolean flags; the string for value flags) |
| `tenant_feature_flag_overrides.value` | new nullable `text` column (NULL = inherit global; the tenant's value when set) |

`AcademicYearDivision` is seeded as a `FeatureFlag` with `Key = 'academic_year_division'`,
`Kind = String`, `Value = 'None'` (global default). A tenant selects its
framework by creating/updating its `TenantFeatureFlagOverride` with `Value ∈
{'None','Terms','Semesters'}` (null = inherit the global `None`). The resolver
returns the effective value (override-or-global) the same way it resolves
`IsEnabled` today. This is **additive** (new nullable columns + one enum value;
no change to boolean-flag rows) and reuses the existing audit, effective
window, and tenant-override uniqueness `(tenant_id, feature_flag_id)`.

### 8.3 Enums

```csharp
namespace SchoolCollab.Students.Core.Domain;
public enum PeriodType { AcademicYear = 0, Term = 1, Semester = 2 }

namespace SchoolCollab.Settings.Core.Domain;
public enum AcademicYearDivision { None = 0, Terms = 1, Semesters = 2 }
// Extended (additive): the boolean-only FlagKind gains a string-valued kind.
public enum FlagKind { Boolean = 0, String = 1 }
```

## 9. Out of Scope

- **OS-H1 (Custom sub-period types)** — Only `Term` and `Semester`. Quarters,
  half-terms, etc. are a future extension of the enum.
- **OS-H2 (Overlapping sub-periods of different types)** — A `Term` and a
  `Semester` in the same year: out of scope (a tenant picks one framework).
- **OS-H3 (Sub-period promotion/rollover)** — Sub-period completion does not
  carry enrollments; only academic-year completion promotes. Activity-group
  window rollover (activity-group FR-50) is a separate concern.
- **OS-H4 (Period templates / auto-generation of terms)** — Auto-creating a
  year's terms from a template is a future admin convenience.

## 10. Affected files (indicative)

| Context | Path | Change |
|---|---|---|
| Students.Core | `Domain/Period.cs`, `Domain/PeriodType.cs` | add `PeriodType`, `ParentPeriodId`; create/complete cascade rules |
| Students.Core | `Data/Configurations/PeriodConfiguration.cs`, `Migrations/<ts>_AddPeriodHierarchy.cs` | additive columns + partial-unique indexes |
| Students.Core | `CQRS/Periods/Commands/ActivatePeriod/` | hierarchy-aware auto-close (FR-H4) |
| Students.Core | `CQRS/Periods/Commands/CompletePeriod/` | AcademicYear cascade-completes sub-periods (FR-H10) |
| Core | `Tenancy/IActivePeriodProvider.cs`, `ActivePeriod` record | add `PeriodType`/`ParentPeriodId`; `GetActiveAcademicYearAsync`/`GetActiveSubPeriodAsync` |
| Students.Core | `Tenancy/ActivePeriodProvider.cs` | implement the new lookups |
| Students.Api | `Endpoints/PeriodRoutes.cs` | typed CRUD, sub-period list, active-year/active-sub endpoints |
| Settings.Core | `Domain/FeatureFlag.cs`, `Domain/TenantFeatureFlagOverride.cs`, `Domain/FlagKind.cs`, `Migrations/<ts>_AddFeatureFlagValue.cs` | extend `FlagKind` + nullable `Value` columns; seed `academic_year_division` flag |
| Settings.Api | `Endpoints/ConfigAcademicYearDivisionRoutes.cs` | `GET`/`PUT /api/config/flags/academic_year_division` (value ∈ None/Terms/Semesters) |
| Students.Core | `Services/IAcademicYearDivisionProvider.cs`, `Services/DefaultAcademicYearDivisionProvider.cs` | cross-context port; default returns `None` |
| Students.Api | `Services/AcademicYearDivisionProviderHttpClient.cs` | HTTP impl calls Settings `GET /api/config/flags/academic_year_division`; overrides the default in DI |
| Students.Core | `CQRS/Enrollments/.../EnrollStudentHandler.cs` | reject `Term`/`Semester` `PeriodId` (FR-H9) |

## 11. Implementation phases (one PR per step, shippable)

1. **Phase H1 — Period type & hierarchy (additive, dark).** Add `PeriodType` +
   `ParentPeriodId` + migration (back-fill `AcademicYear`). Extend
   `IActivePeriodProvider`/`ActivePeriod` projection. `NoUncommittedModelChanges`
   passes. No behavior change yet (all rows are `AcademicYear`).
2. **Phase H2 — Relaxed active invariant + typed activation.** Update
   `ActivatePeriodHandler` (FR-H4) and `CompletePeriod` cascade (FR-H10). Add
   partial-unique indexes. Unit tests: one active year + one active sub-period;
   no two active years; no two active sub-periods; cascade completion.
3. **Phase H3 — Academic-year division via feature-flag machinery.** Extend
   `FlagKind` (`String`) + nullable `Value` on `FeatureFlag`/
   `TenantFeatureFlagOverride` + additive migration; seed `academic_year_division`
   flag; `GET`/`PUT /api/config/flags/academic_year_division`
   (FR-H6/FR-H7); framework gates `Term`/`Semester` creation.
4. **Phase H4 — Containment, no-overlap, grade-enrollment guard.** Create/update
   validation (FR-H3); `EnrollStudent` rejects sub-period `PeriodId` (FR-H9);
   sub-period list + active-sub-period endpoint (FR-H12). Unit + integration tests.
5. **Phase H5 — Wire to activity-group enrollment.** Activity-group `Termly`/
   `Semester`/`WholeAcademicYear` membership attaches to the matching typed
   period (unblocks activity-group Rev. 3 FR-43). This phase lands after the
   activity-group Rev. 2/3 migration phase.

## 12. Open questions

1. **~~Tenant-setting storage~~ (RESOLVED)** — Reuse the existing feature-flag
   machinery: extend `FlagKind` with a `String` kind + nullable `Value` columns
   on `FeatureFlag`/`TenantFeatureFlagOverride`, and model `AcademicYearDivision` as
   the `academic_year_division` flag with a per-tenant override value. No new
   `tenant_settings` table. (See §8.2.)
2. **Sub-period activation auto-close scope** — When activating a `Term` while a
   `Term` is already active in the *same* year, auto-close it (draft). Should
   activating a `Term` also close an active `Semester` of the same year? Draft
   says no (a tenant uses one framework), and FR-H7 prevents mixing — confirm.
3. **`NextPeriodId` for sub-periods** — Draft forbids it (date-ordering only).
   If a term→term chain is later needed for activity-group rollover sequencing,
   revisit. (Activity-group rollover uses the group's own window dates, not
   period chaining, so this should stay out of scope.)