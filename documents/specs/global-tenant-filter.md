# Spec: Global Tenant Query Filter (Hybrid Reference + Strict Operational)

> **Status:** Approved v3 (2026-07-08) — owner approved §3 (hybrid CodedValue +
> strict operational) and §11.2 defaults (Q-1 System tenant sink, Q-2 FeatureFlag
> global, Q-3 override-not-duplicate). Corrects v2's over-generalization: the
> owner's rule *"no creation with null/empty TenantId"* applies to the
> **operational** entities (`GradeLevel`, `Subject`, `Period`, `Student`);
> `CodedValue` is **reusable across tenancy** and therefore **hybrid** (nullable
> `tenant_id`; `NULL` = shared blueprint, real = tenant-owned), with the landed
> override pattern **retained**. Implementation proceeding per §12 sequencing.
> **Author:** spec-draft pass · **Date:** 2026-07-08
> **Reviewers:** Students/Settings/Assignments/Core owners
> **Supersedes (narrowed):** `documents/specs/grade-level-setup.md`
> §3.1 tenancy for `GradeLevel`/`Subject` (global → strict), §3.5 tenancy
> model, the `coded_value_id` unique index (→ composite with `tenant_id`),
> and the §5.6 Period invariant (→ per-tenant) — see §11.1.
> **Does NOT supersede:** `grade-level-setup.md` §3.3 (CodedValue global
> blueprint + override) and §5.4 (client-side name overlay) — these
> **stand**. `gradelevel-wizard-subject-override-per-row.md` **stands**
> (with a noted branch for tenant-owned CodedValues — §11.1).
> **Builds on:** `documents/solution/auth-tenancy-pattern.md`.
> **Related plan + review:** `C:\Users\skwar\.pi\agent\tmp\plans\global-tenant-filter.md`,
> `...global-tenant-filter.review.md`.

---

## 1. Goal

Make cross-tenant data access **impossible by construction** across every
bounded context, while **preserving `CodedValue` reusability across
tenancy** and fixing the Grade-Level wizard's "create new" leak.

Two outcomes:

1. **A global query filter** that scopes every tenant-scoped entity to the
   current `TenantContext` automatically, so a developer cannot forget
   `Where(... => ...TenantId == tenantId)` and leak data. Enforcement is
   layered: EF Core 10 named query filter (`"Tenant"`) + `ModuleDbContext`
   save-guard + a Roslyn analyzer + a build-time model audit.
2. **A split tenancy model by bounded-context role:**
   - **Strict operational** — `GradeLevel`, `Subject`, `Period`, `Student`
     (+ owned children): every row owned by exactly one real tenant; NOT
     NULL `tenant_id`; filter `TenantId == CurrentTenantId`; **creation with
     null/empty `TenantId` rejected** (the owner's rule).
   - **Hybrid** — `CodedValue` (+ owned children): nullable `tenant_id`;
     `NULL` = shared blueprint (CSV-seeded, reusable across all tenants,
     names overlaid per tenant via the retained `TenantCodedValueOverride`
     pattern); real `tenant_id` = tenant-owned row (wizard "create new"
     under a real tenant, for codes the blueprint lacks, isolated to that
     tenant). Filter `TenantId == CurrentTenantId OR TenantId IS NULL`.
   - **Global** — `Tenant`, `FeatureFlag`, `FlagAuditEntry`, `OutboxMessage`:
     no tenant filter (allow-list).

The hybrid model is what fixes the wizard leak **without** destroying
reusability: a real tenant's "create new" writes a tenant-owned row (no
leak), while the shared blueprint stays `NULL` and overlay-able for every
tenant.

---

## 2. Context

### 2.1 What exists today (verified)

- **`ITenantEntity` / `BaseTenantEntity` / `BaseTenantEntityWithAudit`** in
  `src/SchoolCollab.Core/Tenancy/BaseTenantEntity.cs`.
- **`ITenantProvider` / `TenantProvider`** (`AsyncLocal<TenantContext>`,
  singleton via `AddTenancy()`).
- **`ModuleDbContext`** (`src/SchoolCollab.Core/Data/ModuleDbContext.cs`)
  injects `ITenantProvider`, exposes `CurrentTenantId`, stamps `TenantId` on
  `Added` entities **only if empty** — it does **not** validate or refuse
  mismatches.
- **`EntityTypeConfigurationBase.ConfigureTenantQueryFilter(() =>
  CurrentTenantId)`** installs a **named** EF Core 10 filter `"Tenant"`
  (`src/SchoolCollab.Core/Data/EntityTypeConfigurationBase.cs:180`). A
  regression test
  (`tests/SchoolCollab.Core.Tests.Unit/Data/GlobalQueryFilterTests.cs`)
  proves `IgnoreQueryFilters(["SoftDelete"])` keeps tenant isolation.
- **`Tenant` entity + `DbSet<Tenant>` on `SettingsDbContext`** exist
  (`src/Settings/SchoolCollab.Settings.Core/Domain/Tenant.cs`, migration
  `20260707082801_AddTenantRegistry`). `Tenant` is **global by design**
  (it is the registry). PR 1 of `grade-level-setup.md` has landed.
- **Only `Student` and `Assignment` implement `ITenantEntity` today.**
  `GradeLevel`, `Subject`, `Period`, `StudentEnrollment`,
  `GradeSubjectAssignment`, `StudentSubjectAssignment`, `SubjectStrand`,
  `SubjectLesson`, `CodedValue` do **not**.
- **`CodedValue`** (`src/Settings/.../Domain/CodedValue.cs`) is global today;
  `CodedValue.Create(...)` takes no tenant. `CodedValueSeeder` seeds the
  GRADE/SUBJECT tree globally from `seed.csv`. `CodedValueResolver` does
  `overrideValue?.OverriddenName ?? cv.Name`. `TenantCodedValueOverride`
  (`GlobalCodedValueId`, `TenantId`, `OverriddenName`) +
  `TenantCodedValueAttributeOverride` + `Upsert/RemoveCodedValueOverride`
  CQRS all exist and are used.
- **`TenantSeeder`** seeds 'Hydeson School' + 'Little Legends' and writes
  `TenantCodedValueOverride` rows renaming `GRADE_1`→"Standard 1" (Hydeson)
  / "Year 1" (Little Legends). There is **no System tenant** today.
- **`GradeLevel`** (`src/Students/.../Domain/GradeLevel.cs`) carries a
  denormalized `Name` mirror (comment: *"source of truth … should be the
  CodedValue system + Tenant Overrides"*). `ix_grade_levels_coded_value_id`
  is **globally unique** today (migration
  `20260707085922_AddUniqueIndexesGradeLevelSubjectCodedValueId`). Same for
  `Subject`. `AssignmentBackfillService` find-or-creates global
  `GradeLevel`/`Subject` from assignments' coded-value ids (assignments
  *do* carry a `TenantId`).

### 2.2 The leak the wizard exposes (the trigger)

`GradeLevelWizard.razor` Step 1 offers **"New grade"** / **"New subject"**
→ `CodedValuesApiClient.CreateAsync` → `CreateCodedValueHandler`
(`src/Settings/.../CreateCodedValueHandler.cs`). **That handler stamps no
tenant** (`CodedValue.Create(...)` has no tenant parameter). So a real
tenant creating "Foundation Phase" writes a **global** row visible to
**every** tenant's `<CodedValueDropdown>` — the leak. The override buttons
are gated on `IsRealTenant`, so the UX is per-tenant but the persistence is
global.

### 2.3 Why `CodedValue` cannot be strict (the owner's correction)

The owner's directive: *"CodedValue tenancy cannot be strict, as it's
reusable across tenancy."* `CodedValue` is the **shared reference
vocabulary** every tenant picks from and overlays names on. Making it
strict (one owner per row) would destroy the landed
global-blueprint → `TenantCodedValueOverride` → `CodedValueResolver` pattern
that `grade-level-setup.md` §3.3 locks in and `TenantSeeder` actively
demonstrates. Therefore:

- **`CodedValue` is hybrid.** `tenant_id IS NULL` = the reusable shared
  blueprint (CSV-seeded, all tenants read it, overlay names via
  `TenantCodedValueOverride`). `tenant_id = <real>` = a tenant-owned row
  the wizard's "create new" writes **under a real tenant** when the tenant
  needs a code the blueprint lacks — isolated to that tenant (this is what
  fixes the leak). `Guid.Empty` is **never** a valid `CodedValue.tenant_id`.
- **The override pattern is retained, unchanged, for shared (`NULL`) rows.**
  `CodedValueResolver`, `TenantCodedValueOverride`,
  `TenantCodedValueAttributeOverride`, `Upsert/RemoveCodedValueOverride`,
  and `GetCodedValuesByParentHandler` keep working as-is. The resolver
  transparently returns a tenant-owned row's own `Name` (no override row
  exists for owned rows) and a shared row's `OverriddenName ?? cv.Name` —
  **no resolver code change required**.
- **No clone-on-provisioning, no System-tenant blueprint, no deprecation.**
  v2's `ITenantProvisioningService`, `CreateTenantHandler` clone step, and
  override freeze/drop are all **dropped**. New tenants simply read the
  `NULL` blueprint immediately and overlay names — simpler onboarding.
- The owner's *"no creation with null/empty TenantId"* rule applies to the
  **strict operational** entities (`GradeLevel`, `Subject`, `Period`,
  `Student`). For `CodedValue`, the rule is: **never `Guid.Empty`**; `NULL`
  is the blueprint sentinel (written only by the seeder / admin blueprint
  path), a real `Guid` is a tenant-owned row (written by the tenant-facing
  wizard path).

### 2.4 Other gaps the filter must close (from the plan review)

- `ModuleDbContext` does not refuse to save an `ITenantEntity` with an
  empty/mismatched `TenantId`.
- `PromotionService` (Students worker) has no `ITenantProvider` in scope;
  adding the filter to `Period`/`StudentEnrollment`/`GradeSubjectAssignment`
  makes it read **zero rows** — a silent regression. It must enumerate
  tenants and run per tenant.
- `OutboxMessage` has **no `TenantId`** (`src/SchoolCollab.Core/Messaging/OutboxMessage.cs`)
  — the dispatcher cannot reconstruct the publisher's tenant.
- Existing `students.tenant_id` rows are all `Guid.Empty` (migration
  `20260620100737_AddTenantToStudents`) — no real tenant to backfill from;
  a legacy-ownership policy is required (§9.3).
- `ix_students_student_number` is **globally unique** today; once
  tenant-scoped it must become `(tenant_id, student_number)` or inserts hit
  `UNIQUE VIOLATION`. Same for `ix_grade_levels_coded_value_id` /
  `ix_subjects_coded_value_id` (→ `(tenant_id, coded_value_id)`).
- `ListDeletedStudentsHandler` uses unnamed `IgnoreQueryFilters()`.
- The wizard reads tenant from `AuthenticationStateProvider`, **not**
  `ITenantProvider`, because `AsyncLocal` does not flow into Blazor Server
  circuits (`gradelevel-wizard-subject-override-per-row.md` §11.1). Any
  filter that reads `ITenantProvider` must be fed via the API pipeline for
  Blazor-mediated writes (FR-19).

---

## 3. Decision: Hybrid Reference + Strict Operational (the heart of this spec)

> **This section is the gate. Nothing in §4–§12 is implementable until §3
> and the §11.2 confirmations are approved.**

### 3.1 The split

- **Strict operational:** implements `ITenantEntity`; **non-null**
  `tenant_id`; configured via `TenantEntityTypeConfigurationBase<T>` with
  filter `TenantId == CurrentTenantId`; creation with `null`/`Guid.Empty`
  rejected (FR-4).
- **Hybrid:** implements `IHybridTenantEntity` (nullable `Guid? TenantId`);
  configured via `TenantOrGlobalEntityTypeConfigurationBase<T>` with filter
  `TenantId == CurrentTenantId OR TenantId == null`; `Guid.Empty` never
  stored (FR-5).
- **Global:** no filter; on the per-context allow-list (FR-2/FR-14).

### 3.2 Entity classification

| Context | Entity | Class | Filter | Notes |
|---|---|---|---|---|
| Settings | `CodedValue` | **Hybrid** | `== current OR IS NULL` | `NULL` = shared blueprint (reusable across tenancy); real = tenant-owned (wizard "create new" under a real tenant). Owned children `CodedValueAttribute`, `CodedValueAttributeDefinition` inherit tenant via the parent (owned types — no separate DbSet/filter). |
| Settings | `TenantCodedValueOverride` | **Strict** | `== current` | **Retained.** Targets `NULL`-blueprint `CodedValue` rows only (overlay name/description per tenant). |
| Settings | `TenantCodedValueAttributeOverride` | **Strict** | `== current` | **Retained.** Same. |
| Settings | `TenantFeatureFlagOverride` | **Strict** | `== current` | Retained (feature-flag override pattern). |
| Settings | `Tenant` | **Global** | none | The registry. Exempt (allow-list). |
| Settings | `FeatureFlag` | **Global** | none | Infrastructure flags. Exempt. |
| Settings | `FlagAuditEntry` | **Global** | none | Audit log; `tenant_id` is payload, not a filter. Exempt. |
| Students | `GradeLevel` | **Strict** | `== current` | Tenant-owned; references a `CodedValue` (shared or tenant-owned). `Name` is the denormalized mirror of the resolved CodedValue name (§5.4 overlay **stands**). |
| Students | `Subject` | **Strict** | `== current` | Same. Owned children `SubjectStrand`, `SubjectLesson` inherit via parent. |
| Students | `Period` | **Strict** | `== current` | Tenant-owned academic calendar. "At most one current period" becomes **per-tenant**. |
| Students | `Student` | **Strict** | `== current` (already `ITenantEntity`) | Already strict; this spec adds the no-empty-creation guard + composite unique index. |
| Students | `StudentEnrollment` | **Strict** | `== current` | Tenant via `Student`. |
| Students | `GradeSubjectAssignment` | **Strict** | `== current` | Tenant-owned. |
| Students | `StudentSubjectAssignment` | **Strict** | `== current` | Tenant via `Student`. |
| Assignments | `Assignment` | **Strict** | `== current` (already) | Owned children inherit via parent. |
| Core | `OutboxMessage` | **Global** (+ `TenantId?` payload) | none | Queue table. Exempt; `TenantId` is dispatch-routing payload (FR-16). |

### 3.3 Hybrid CodedValue — blueprint vs tenant-owned

- **Shared blueprint rows (`tenant_id IS NULL`)** are written **only** by
  `CodedValueSeeder` (CSV bootstrap, under `SuppressTenantGuard()`) and by
  an **admin/blueprint-edit path** in default-tenant (dev) mode (the per-row
  override spec's "default-tenant mode edits the global coded value"
  behavior — §11.1). They are visible to all tenants via the hybrid filter
  and overlaid per tenant via `TenantCodedValueOverride`.
- **Tenant-owned rows (`tenant_id = <real>`)** are written **only** by the
  tenant-facing `CreateCodedValueHandler` when `CurrentTenantId != Guid.Empty`
  (the wizard "create new" under a real tenant). They are isolated to that
  tenant. The tenant edits `Name`/`IsDisabled` directly (no override row).
- **`CodedValueResolver` is unchanged** — `overrideValue?.OverriddenName ??
  cv.Name` returns the owned row's own name (no override row targets owned
  rows) and the shared row's overridden name. The override pattern is
  retained for shared rows.

### 3.4 Duplicate-code guard (resolves the hybrid-filter double-surface)

Because the hybrid filter surfaces **both** `NULL`-blueprint and
tenant-owned rows to a tenant, a tenant could otherwise create a
tenant-owned `GRADE_1` while a shared `GRADE_1` already exists → a
duplicate in its dropdown. Guard (FR-6):

- **`CreateCodedValueHandler` (real-tenant path)** MUST reject creation if
  a `CodedValue` with the same `(parent, code)` already exists in
  **(shared blueprint ∪ this tenant's owned rows)**. If a shared row with
  that code exists, the tenant is directed to **override its name** via the
  existing `UpsertCodedValueOverride` (the per-row "Override Name" UX),
  not create a duplicate.
- **`CreateCodedValueHandler` (default-tenant / blueprint path)** MUST
  reject creation if a shared row with that `(parent, code)` already exists
  (route to update instead).
- **Partial unique indexes** backstop this (FR-7): one shared row per
  `(COALESCE(parent_id, sentinel), code)` where `tenant_id IS NULL`, plus
  one tenant-owned row per `(tenant_id, COALESCE(parent_id, sentinel),
  code)` where `tenant_id IS NOT NULL`.

### 3.5 What this revises in `grade-level-setup.md` (narrowed — see §11.1)

`GradeLevel` and `Subject` change from **global** to **strict operational**
(tenant-owned); the `coded_value_id` unique index becomes
`(tenant_id, coded_value_id)`; "exactly one `GradeLevel` per grade coded
value" becomes "one `GradeLevel` per **(tenant, coded value)**." The
`Assignment.GradeLevelId`/`SubjectId` reporting key is per-tenant (a
tenant's assignment references the tenant's own `GradeLevel`). §5.6 Period
invariant becomes per-tenant. **§3.3 (CodedValue blueprint + override) and
§5.4 (name overlay) STAND** — this spec does not supersede them.

---

## 4. Functional Requirements (RFC 2119)

### Tenancy classification

| FR | Requirement |
|---|---|
| **FR-1** | Every entity in §3.2 marked **Strict** MUST implement `ITenantEntity` and be configured via `TenantEntityTypeConfigurationBase<T>` with filter `TenantId == CurrentTenantId`. |
| **FR-2** | Every entity marked **Global** (`Tenant`, `FeatureFlag`, `FlagAuditEntry`, `OutboxMessage`) MUST NOT have a tenant filter; its configuration MUST carry an XML doc comment stating the justification. These are the only entries on the per-context allow-list (FR-14). |
| **FR-3** | `CodedValue` MUST implement `IHybridTenantEntity` and be configured via `TenantOrGlobalEntityTypeConfigurationBase<T>` with filter `TenantId == CurrentTenantId OR TenantId == null`. The override pattern (`TenantCodedValueOverride`, `TenantCodedValueAttributeOverride`, `CodedValueResolver`, `Upsert/RemoveCodedValueOverride`) MUST be **retained unchanged** for shared (`NULL`) rows. |

### No null/empty tenant on creation (the owner's rule — strict entities)

| FR | Requirement |
|---|---|
| **FR-4** | Every **strict** tenant-facing create handler (`GetOrCreateGradeLevel`, `GetOrCreateSubject`, `CreateStudent`, `CreatePeriod`, `CreateAssignment`, `EnrollStudent`, `AssignGradeSubject`, etc.) MUST obtain `CurrentTenantId` from `ITenantProvider` and stamp it via `.WithTenant(provider)`. If `CurrentTenantId == Guid.Empty`, the handler MUST throw `TenantContextRequiredException` before any write. **No strict entity may be created with `TenantId == null` or `Guid.Empty`.** |
| **FR-5** | For `CodedValue` (hybrid), `Guid.Empty` MUST NEVER be stored as `TenantId`. `CreateCodedValueHandler` MUST: when `CurrentTenantId != Guid.Empty`, stamp the real tenant (tenant-owned row); when `CurrentTenantId == Guid.Empty` (default/dev tenant), write `TenantId = null` (shared blueprint) under `SuppressTenantGuard()`. The `NULL`-blueprint path is the dev/admin vocabulary-edit affordance the per-row override spec defines for default-tenant mode. In production the API pipeline guarantees a real `tenant_id` claim (FR-19), so `Guid.Empty` only occurs in dev/test. |
| **FR-6** | (Duplicate-code guard — see §3.4.) `CreateCodedValueHandler` MUST reject creation when a `CodedValue` with the same `(parent, code)` already exists in (shared blueprint ∪ current tenant's owned rows) [real-tenant path] / in (shared blueprint) [default-tenant path], throwing `CodedValueCodeConflictException` that directs the caller to override the existing shared row's name instead. |
| **FR-7** | The `coded_values` unique index MUST be replaced by **two partial unique indexes**: one on `(COALESCE(parent_id, sentinel), code)` `WHERE tenant_id IS NULL` (shared blueprint), and one on `(tenant_id, COALESCE(parent_id, sentinel), code)` `WHERE tenant_id IS NOT NULL` (tenant-owned). `grade_levels` / `subjects` unique on `coded_value_id` MUST become `(tenant_id, coded_value_id)`. `students.student_number` unique MUST become `(tenant_id, student_number)`. Use the existing raw-SQL `COALESCE`-sentinel precedent for NULL parent ids. |
| **FR-8** | `ModuleDbContext.SaveChangesAsync` MUST throw `TenantContextRequiredException` if any **strict** `ITenantEntity` in `Added` state has `TenantId == Guid.Empty` (or null) and no tenant-guard suppression is active. For **hybrid** `IHybridTenantEntity`, it MUST throw if `TenantId == Guid.Empty` (null is allowed). It MUST throw `TenantMismatchException` if any strict entity being saved has a `TenantId` differing from `CurrentTenantId`, unless suppression is active. (Hybrid entities may legitimately be `NULL`; the mismatch guard applies only to non-null hybrid rows.) |
| **FR-9** | Read isolation MUST be the union of: (a) the EF Core `"Tenant"` filter (strict or hybrid) returning zero wrong-tenant rows, (b) the SaveChanges guard (FR-8), (c) the Roslyn analyzer (FR-17), (d) the build-time model audit (FR-18). |

### Sanctioned bypass

| FR | Requirement |
|---|---|
| **FR-10** | The ONLY sanctioned way to bypass the `"Tenant"` filter MUST be `ITenantContextAccessor` (§8.3): `RunWithExplicitTenantAsync(Guid?, …)` for admin/cross-tenant work and `SuppressTenantGuard()` for the outbox dispatcher, design-time factories, migration/seed services, and the CodedValue blueprint-edit path. Every call site MUST carry a comment naming the justifying spec section. |

### Workers / outbox

| FR | Requirement |
|---|---|
| **FR-15** | `OutboxMessage` MUST gain a nullable `TenantId` payload column + migration. On enqueue, the publisher MUST stamp `OutboxMessage.TenantId = CurrentTenantId` (NULL for global events). The dispatcher MUST set the tenant context via `RunWithExplicitTenantAsync(msg.TenantId)` before invoking any handler that touches tenant-scoped data, and MUST suppress the guard when `msg.TenantId == null`. |
| **FR-16** | `PromotionService` MUST enumerate tenants via a cross-context `ITenantDirectory` (§8.4) (NOT `db.Tenants` — that DbSet is on `SettingsDbContext`, unavailable to the Students worker) and run the promotion body inside `RunWithExplicitTenantAsync(tenantId, …)` per tenant. The "at most one current period" invariant is **per-tenant**. |
| **FR-17roslyn** | (See Static guarantees — Roslyn `SC0001`.) |
| **FR-18audit** | (See Static guarantees — model audit.) |

### Blazor / tenant propagation

| FR | Requirement |
|---|---|
| **FR-19** | Because `ITenantProvider` (AsyncLocal) does not flow into Blazor Server circuits, every Blazor-mediated write MUST go through an API endpoint whose request pipeline sets `ITenantProvider` from the `tenant_id` claim (`TenantClaimsTransformation`). In production, a valid real-tenant `tenant_id` claim MUST be required (so `CurrentTenantId == Guid.Empty` only occurs in dev/test, closing the `Guid.Empty`→blueprint-creation door in prod). The wizard's existing `IsRealTenant` read from `AuthenticationStateProvider` is UI-gating only. **In default-tenant mode (`Guid.Empty`), the wizard's strict-entity create actions (New grade-level/subject/period/student) MUST be disabled** (FR-4 would throw); the CodedValue "create new" remains available and writes a `NULL`-blueprint row (FR-5, the per-row override spec's default-tenant behavior). |

### Static guarantees

| FR | Requirement |
|---|---|
| **FR-17** | A Roslyn analyzer `SC0001` MUST report an **error** on any `IgnoreQueryFilters()` call **with no named-filter arguments** that targets a `DbSet<T>` whose entity type implements `ITenantEntity` **or** `IHybridTenantEntity`. Calls on global-allow-list entities (`Tenant`, `FeatureFlag`, `FlagAuditEntry`, `OutboxMessage`) MUST be exempt. Unnamed `IgnoreQueryFilters()` on a soft-delete-only entity MUST still be flagged (use `["SoftDelete"]`). |
| **FR-18** | A build-time model audit MUST, during `ModuleDbContext.OnModelCreating`, enumerate every `IEntityType` and throw `TenantFilterMissingException` if a non-allow-listed entity lacks a configured `"Tenant"` filter (strict or hybrid — the audit checks for filter *presence*). The per-context allow-list MUST be an explicit, reviewed constant (e.g. `SettingsDbContext.GlobalEntityAllowList = { typeof(Tenant), typeof(FeatureFlag), typeof(FlagAuditEntry), typeof(OutboxMessage) }`). |

---

## 5. Non-Functional Requirements

| ID | Requirement |
|---|---|
| **NFR-1** | The EF Core model MUST be cached once per `DbContext` type per process; the tenant predicate MUST be a SQL parameter evaluated per query, never a compiled constant. The existing `ConfigureTenantQueryFilter(() => CurrentTenantId)` expression-splice approach MUST be preserved (and the hybrid `ConfigureTenantOrGlobalQueryFilter` MUST splice `() => CurrentTenantId` identically). |
| **NFR-2** | No regression to the existing test suite. `dotnet test` green. `GlobalQueryFilterTests` unchanged. |
| **NFR-3** | Every strict tenant-scoped table MUST have a composite index with `tenant_id` as the leading column on its hot paths (`(tenant_id, is_deleted)`, `(tenant_id, coded_value_id)`, `(tenant_id, student_number)`, etc.). `coded_values` MUST have `(tenant_id, parent_id)` where `tenant_id IS NOT NULL` and `(parent_id)` where `tenant_id IS NULL`. A migration adding `tenant_id` without the index MUST fail review. |
| **NFR-4** | `ITenantContextAccessor` MUST save/restore the prior `AsyncLocal<TenantContext>` value (and a new `AsyncLocal<bool>` guard flag) in `try/finally`, MUST support correct nesting/unwinding, and MUST be safe under the singleton `TenantProvider`. No set without a paired restore (cross-tenant leak prevention). |
| **NFR-5** | Tenanted cache keys MUST remain prefixed `tenant:{tenantId}:…`. Invalidation is per-tenant for tenant-owned CodedValue edits; the global `coded-values` tag covers shared-blueprint edits (seeder/admin). |
| **NFR-6** | `dotnet build` 0 errors, 0 new warnings across the solution, including the new analyzer project. |

---

## 6. Acceptance Criteria (Given/When/Then)

### 6.1 Strict operational filter

- **AC-1** (FR-1): Given tenants A and B each with one `Student`, when
  `ITenantProvider` is A and `db.Students.ToListAsync()` runs, then exactly
  A's student is returned. *(Regression guard — passes today.)*
- **AC-2** (FR-4, FR-8): Given `CurrentTenantId == Guid.Empty` and no
  suppression, when `CreateStudent`/`CreatePeriod`/`GetOrCreateGradeLevel`
  is invoked, then `TenantContextRequiredException` is thrown before any
  write.
- **AC-3** (FR-8): Given `CurrentTenantId == A` and a strict ChangeTracker
  entity with `TenantId == B`, when `SaveChanges()` runs, then
  `TenantMismatchException` is thrown.
- **AC-4** (FR-10): Given `RunWithExplicitTenantAsync(B, …)` wraps a strict
  create that calls `.WithTenant(provider)`, when `SaveChanges()` runs
  inside the scope, then the row persists with `TenantId == B` and no
  exception; on scope exit `CurrentTenantId` is restored to its prior value.

### 6.2 Hybrid CodedValue + override retained

- **AC-5** (FR-3, FR-5): Given the CSV-seeded shared `GRADE_1` (`tenant_id
  IS NULL`) exists, when tenant A's `db.CodedValues` is enumerated, then
  `GRADE_1` is returned (the hybrid filter surfaces `NULL` rows to all
  tenants). Given tenant B enumerates, `GRADE_1` is also returned.
- **AC-6** (FR-5): Given tenant A is authenticated and the wizard's "New
  grade" submits a new code, when `CreateCodedValueHandler` runs, then the
  persisted `CodedValue.TenantId == A` (tenant-owned) and the row is **not**
  visible to tenant B (B's `db.CodedValues` omits it; B still sees the
  shared `NULL` rows).
- **AC-7** (FR-5): Given `CurrentTenantId == Guid.Empty` (default/dev) and
  "New grade" submits, when `CreateCodedValueHandler` runs, then the
  persisted `CodedValue.TenantId IS NULL` (shared blueprint) — the dev/admin
  vocabulary-edit path. No `Guid.Empty` is ever stored.
- **AC-8** (FR-3, override retained): Given the shared `GRADE_1` and
  Hydeson's `TenantCodedValueOverride(OverriddenName = "Standard 1")`, when
  Hydeson resolves `GRADE_1` via `CodedValueResolver`, then the result name
  is "Standard 1" (unchanged from today). `CodedValueResolver` code is
  unchanged.
- **AC-9** (FR-6, duplicate guard): Given the shared `GRADE_1` exists and
  tenant A submits "New grade" with code `GRADE_1`, when
  `CreateCodedValueHandler` runs, then `CodedValueCodeConflictException` is
  thrown directing A to override the shared row's name instead. No duplicate
  is created.
- **AC-10** (FR-7): Given two tenants each create a tenant-owned coded
  value with code `MATH` under parent `SUBJECT`, when both saves complete,
  then both succeed (the partial unique index allows one owned per tenant).
  Given tenant A already owns `MATH`, when A creates another `MATH`, then it
  fails with a unique violation.

### 6.3 GradeLevel / Subject / Period strict tenancy

- **AC-11** (FR-1, FR-4): Given tenant A creates a grade coded value then
  saves the wizard, when `GetOrCreateGradeLevel` runs, then
  `GradeLevel.TenantId == A` and tenant B's landing page does not list it.
  Find-or-create is scoped by `(A, codedValueId)`; a concurrent B
  find-or-create for the same `codedValueId` creates a separate B-owned
  `GradeLevel` (the `(tenant_id, coded_value_id)` index permits both).
- **AC-12** (FR-16, §3.2): Given tenants A and B each have an expired
  `Period`, when `PromotionService.RunPromotionCycleAsync` runs with no
  ambient tenant, then both tenants' periods are completed (the service
  enumerates via `ITenantDirectory` and runs the body per tenant).
- **AC-13** (FR-15): Given a tenant-A command enqueues an `OutboxMessage`,
  when the dispatcher reads it, then `OutboxMessage.TenantId == A` and the
  handler runs with `CurrentTenantId == A`.

### 6.4 Static guarantees

- **AC-14** (FR-17): Given source `db.Students.IgnoreQueryFilters()` (unnamed),
  when the analyzer runs, then `SC0001` is reported as an error.
- **AC-15** (FR-17): Given source
  `db.CodedValues.IgnoreQueryFilters()` (unnamed, hybrid DbSet), when the
  analyzer runs, then `SC0001` is reported as an error.
- **AC-16** (FR-17): Given source
  `db.Students.IgnoreQueryFilters(["SoftDelete"])`, when the analyzer runs,
  then no diagnostic is reported.
- **AC-17** (FR-18): Given `SettingsDbContext` is built and a new entity
  `Foo` is mapped that is not on the allow-list and has no `"Tenant"` filter,
  when `OnModelCreating` completes, then `TenantFilterMissingException` is
  thrown naming `Foo`.

---

## 7. Edge Cases

| ID | Case | Handling |
|---|---|---|
| **EC-1** | Admin tenant-switcher impersonation. | The admin shell switches the `tenant_id` claim (`IDevTenantSelection` + `TestAuthHandler` in Testing; Keycloak re-auth in prod). Reads/writes run under that tenant's `ITenantProvider`. A true cross-tenant aggregate view wraps reads in `RunWithExplicitTenantAsync(null, …)` + `IgnoreQueryFilters(["Tenant"])` on a reviewed admin-only endpoint. |
| **EC-2** | Outbox event with `TenantId == null` (global event). | Dispatcher suppresses the guard and invokes the handler with `CurrentTenantId == Guid.Empty`; the handler MUST touch only global allow-list data. |
| **EC-3** | Design-time `IDesignTimeDbContextFactory`. | `DesignTimeTenantProvider` returns `Guid.Empty`; the factory wraps model building in `SuppressTenantGuard()` so migrations generate. |
| **EC-4** | `FlagAuditEntry.tenant_id` is payload data. | Confirmed global (FR-2). Audit reads are admin-only; `tenant_id` is provenance. |
| **EC-5** | A tenant wants a coded value another tenant owns. | The tenant creates its own tenant-owned row (find-or-create by `(tenant, code)`), unless a shared blueprint row with that code already exists (then override its name). "Promote tenant value to shared blueprint" is out of scope (§10). |
| **EC-6** | `ListDeletedStudentsHandler` unnamed `IgnoreQueryFilters()`. | Refactored to `IgnoreQueryFilters(["SoftDelete"])` (keeps the tenant filter; lifts soft-delete only). SC0001 would otherwise block the build. |
| **EC-7** | Backfill of existing strict `Guid.Empty` rows. | Legacy-ownership policy (§9.3): **attributable** strict rows (e.g. `GradeLevel`/`Subject` created by `AssignmentBackfillService`, where the source assignment has a real tenant) → stamp that tenant; **unattributable** strict rows (students with `Guid.Empty`, GradeLevel/Subject whose source assignment is also `Guid.Empty`, Periods with no owner) → **System tenant sink** (a backfill-only tenant seeded idempotently by `TenantSeeder`; no end-users). No `Guid.Empty` remains in any strict table. |
| **EC-8** | Concurrent requests reuse the singleton `TenantProvider`'s async flow. | `AsyncLocal<TenantContext>` flows per logical async context; `TenantClaimsTransformation` sets it per request; `RunWithExplicitTenantAsync` saves/restores in `try/finally`. NFR-4 guards against a missing restore leaking A into B. |
| **EC-9** | Default-tenant (dev) mode opens the wizard. | Strict-entity create actions (New grade-level/subject/period/student) are disabled when `IsRealTenant == false` (FR-19); the dev selects a real tenant via the dev switcher to use them. The CodedValue "create new" remains available and writes a `NULL`-blueprint row (the per-row override spec's default-tenant vocabulary-edit behavior). |
| **EC-10** | Blazor Server circuit: `ITenantProvider` not available. | Writes go through the API (FR-19). The admin shell's interactive circuit MUST NOT call `DbContext` directly; it goes through `*ApiClient` → endpoints whose pipeline sets the provider. |
| **EC-11** | Admin seeds a shared-blueprint code that a tenant already owns. | The seeder/blueprint-edit path MUST check for tenant-owned collisions before inserting a shared row; if any tenant owns `(parent, code)`, the admin is warned (the shared insert would create a duplicate in that tenant's dropdown). Seeder runs at bootstrap before tenants create owned rows, so this is an admin-edit-time check. |
| **EC-12** | A `GradeLevel` references a tenant-owned `CodedValue` that the tenant later deletes. | `CodedValue` uses soft-delete (`ISoftDeletableEntity`); the `GradeLevel` keep its `CodedValueId` and the resolved name from the (soft-deleted) row. Hard-delete of a referenced CodedValue is blocked by existing referential guards. |

---

## 8. API / Data Contracts

### 8.1 New exceptions (`SchoolCollab.Core.Tenancy` / `SchoolCollab.Settings.Core.Domain`)

```csharp
public sealed class TenantContextRequiredException : InvalidOperationException
{
    public string Caller { get; }
    public Type? EntityType { get; }
    public TenantContextRequiredException(string caller, Type? entityType) : base(
        $"A real tenant context is required to {caller}" +
        (entityType is null ? "" : $" for {entityType.Name}") +
        ". No strict entity may be created with an empty/null TenantId. " +
        "Select a real tenant (dev switcher) or wrap in ITenantContextAccessor.") { }
}

public sealed class TenantMismatchException : InvalidOperationException
{
    public Guid ExpectedTenantId { get; }
    public Guid ActualTenantId { get; }
    public Type EntityType { get; }
    // ...
}

public sealed class TenantFilterMissingException : InvalidOperationException
{
    public Type EntityType { get; }
    public string DbContextName { get; }
    // ...
}

// SchoolCollab.Settings.Core.Domain (CodedValue-specific)
public sealed class CodedValueCodeConflictException : DomainException
{
    public string Code { get; }
    public Guid? ExistingCodedValueId { get; }
    public bool ExistingIsSharedBlueprint { get; }
    // Message directs the caller to override the shared row's name instead of creating a duplicate.
}
```

### 8.2 `IHybridTenantEntity` + hybrid configuration base

```csharp
// SchoolCollab.Core.Tenancy
public interface IHybridTenantEntity
{
    Guid? TenantId { get; set; }   // null = shared blueprint; real Guid = tenant-owned; never Guid.Empty
}

// SchoolCollab.Core.Data
public abstract class TenantOrGlobalEntityTypeConfigurationBase<T> : EntityTypeConfigurationBase<T>
    where T : class, IHybridTenantEntity
{
    protected override void ConfigureEntity(EntityTypeBuilder<T> builder)
    {
        base.ConfigureEntity(builder);
        // Named "Tenant" filter, hybrid predicate. Splices () => CurrentTenantId (NFR-1).
        builder.ConfigureTenantOrGlobalQueryFilter(() => CurrentTenantId);
    }
}
```

`ConfigureTenantOrGlobalQueryFilter` installs the **same named `"Tenant"`
filter** with predicate `e => e.TenantId == CurrentTenantId || e.TenantId
== null`, so `IgnoreQueryFilters(["SoftDelete"])` keeps it and
`IgnoreQueryFilters(["Tenant"])` lifts it (sanctioned via accessor). The
existing strict `TenantEntityTypeConfigurationBase<T>` +
`ConfigureTenantQueryFilter(() => CurrentTenantId)` is unchanged and used
by all strict entities.

### 8.3 `ITenantContextAccessor` (the only sanctioned bypass)

```csharp
public interface ITenantContextAccessor
{
    /// <summary>Run <paramref name="callback"/> with <paramref name="tenantId"/> as the
    /// current tenant, restoring the prior context on exit. null = suppress (global/blueprint).</summary>
    Task<T> RunWithExplicitTenantAsync<T>(Guid? tenantId, Func<CancellationToken, Task<T>> callback, CancellationToken ct);

    /// <summary>Suppress the ModuleDbContext save-guard for the current async flow.
    /// Outbox dispatcher / design-time / migration-seed / CodedValue blueprint-edit only.</summary>
    IDisposable SuppressTenantGuard();
}
```

**Implementation contract (NFR-4):** backed by the singleton `TenantProvider`
(save/restore its `AsyncLocal<TenantContext>`) **plus** a new
`AsyncLocal<bool>` guard flag (save/restore in `try/finally`). `ModuleDbContext`
reads the guard via the accessor. Nesting is correct because each scope
captures and restores its prior value.

### 8.4 `ITenantDirectory` (cross-context tenant enumeration for workers)

```csharp
// SchoolCollab.Core.Tenancy — both Settings & Students contexts depend on Core
public interface ITenantDirectory
{
    Task<IReadOnlyList<Guid>> GetAllTenantIdsAsync(CancellationToken ct);
}
```

**Implementation:** `TenantDirectory` queries `SettingsDbContext.Tenants`
under `SuppressTenantGuard()` (`Tenant` is global/allow-listed) and returns
the ids. Registered in DI for the Students worker. Resolves review BLOCKER
B1 (`db.Tenants` is not on `StudentsDbContext`).

### 8.5 `OutboxMessage` additions

```csharp
public sealed class OutboxMessage : IEntity
{
    // ... existing ...
    public Guid? TenantId { get; set; }   // NEW — publisher's tenant (null = global event)
}
```

`OutboxDispatcher` per message: if `msg.TenantId is Guid tid`, call
`RunWithExplicitTenantAsync(tid, …)`; else `SuppressTenantGuard()` scope.

> **Note:** v2's `ITenantProvisioningService` / `CreateTenantHandler` clone
> step are **dropped** — hybrid CodedValue needs no clone-on-provisioning;
> new tenants read the `NULL` blueprint immediately.

---

## 9. Data Models

### 9.1 Per-entity tenancy classification (the Step-0 gate table — final)

See §3.2. **Strict** entities: NOT NULL `tenant_id`, filter `== current`.
**Hybrid** (`CodedValue` only): nullable `tenant_id` (`NULL` blueprint / real
owned), filter `== current OR IS NULL`. **Global** allow-list: `Tenant`,
`FeatureFlag`, `FlagAuditEntry`, `OutboxMessage`.

### 9.2 Migrations

1. **`AddTenantIdToCodedValues`** (Settings): add `tenant_id uuid NULL` to
   `coded_values` (and ensure owned children `coded_value_attributes`,
   `coded_value_attribute_definitions` inherit via parent — owned types
   follow the root's tenant in queries). **Backfill existing global rows →
   `NULL`** (they ARE the shared blueprint). Existing
   `TenantCodedValueOverride` rows are **unchanged** (they already target
   these now-`NULL` rows via `GlobalCodedValueId`). Drop the old global
   unique index; create the two **partial unique indexes** per FR-7; add
   `(tenant_id, parent_id)` where `tenant_id IS NOT NULL` and `(parent_id)`
   where `tenant_id IS NULL` indexes.
2. **`AddTenantIdToGradeLevelsAndSubjects`** (Students): add `NOT NULL
   tenant_id DEFAULT sentinel` to `grade_levels`, `subjects`,
   `subject_strands`, `subject_lessons`; **backfill** per §9.3
   (attributable → assignment's tenant; unattributable → System tenant);
   drop default; rebuild unique indexes `→ (tenant_id, coded_value_id)`.
3. **`AddTenantIdToRemainingStudentsOperationalEntities`** (Students): add
   `NOT NULL tenant_id DEFAULT sentinel` to `student_enrollments`,
   `grade_subject_assignments`, `student_subject_assignments`; backfill from
   the parent `Student.TenantId` join; convert residual `Guid.Empty` →
   System tenant (EC-7); drop default; rebuild `student_number` unique →
   `(tenant_id, student_number)`; add hot-path composite indexes
   (`(tenant_id, is_deleted)`, etc.).
4. **`AddTenantIdToPeriods`** (Students): add `NOT NULL tenant_id DEFAULT
   sentinel` to `periods`; backfill existing periods → System tenant (or per
   §9.3 if attributable); drop default; the "at most one current period"
   invariant becomes per-tenant (widen the `grade-level-setup.md` §5.6
   domain check to be per-tenant).
5. **`AddSystemTenant`** (Settings): seed the System tenant
   (`Name = "System"`, `TenantType.Organization`) idempotently via
   `TenantSeeder` — a backfill sink for unattributable strict rows (EC-7).
   No end-users authenticate as System. (Optional: skip if Q-3 resolves to
   "attribute to a school tenant" instead.)
6. **`AddTenantIdToOutboxMessage`** (per context owning an outbox): add
   `tenant_id uuid NULL`; backfill `NULL` (legacy events are global).

### 9.3 Seed / backfill policy

- **`CodedValue` existing rows** → `tenant_id = NULL` (shared blueprint).
  Existing `TenantCodedValueOverride` rows keep working unchanged. **No
  override absorption, no clone, no System-tenant blueprint.**
- **Strict rows — attributable:** `GradeLevel`/`Subject` created by
  `AssignmentBackfillService` → stamp the **owning assignment's `TenantId`**
  (the backfill is updated to set tenant on find-or-create). The backfill
  runs under `SuppressTenantGuard()` and sets `TenantId` explicitly.
- **Strict rows — unattributable:** students with `Guid.Empty`, and any
  GradeLevel/Subject/Period whose source is also `Guid.Empty` or has no
  owner → **System tenant sink** (Q-3). No `Guid.Empty` remains in any
  strict table after migration, so `Guid.Empty` reliably means "no context."
- **`TenantSeeder`** is extended to seed the System tenant (idempotent by
  `Name`) when Q-3 = System sink. The existing school-tenant override
  seeding (Hydeson "Standard 1", Little Legends "Year 1") is **unchanged**
  — those overrides target the now-`NULL` blueprint rows and still resolve
  correctly (AC-8).

### 9.4 (removed — no override absorption needed under hybrid)

v2's override-absorption migration is dropped: the override pattern is
retained, so existing `TenantCodedValueOverride` rows need no migration
beyond their target rows becoming `NULL`-blueprint (which they already
were, implicitly).

---

## 10. Out of Scope

| Item | Reason |
|---|---|
| "Promote a tenant-owned coded value to shared blueprint" admin action. | Future feature (would need collision check — EC-11). |
| Rebuilding the Periods landing page onto `<LandingPage>`. | `grade-level-setup.md` follow-up. |
| Keycloak claim-mapper configuration. | `auth-tenancy-integration.md` future work. |
| Per-tenant rate limiting / logging. | Future cross-cutting concern. |
| Migrating the (empty) `src/CodedValues/*` module. | Verified empty — no source files. |
| Soft-delete for `GradeLevel`/`Subject`. | `grade-level-setup.md`: hard delete + referential guards. |
| Making `FeatureFlag` strict-tenant. | §11.2 Q-2 — flags are admin-created infra; override retained. |
| **Clone-on-provisioning / System-tenant CodedValue blueprint / dropping the override tables.** | **Dropped from v2** — hybrid CodedValue is reusable across tenancy by design (§2.3); the override pattern is retained. |
| Changing the per-row override spec's default-tenant "edit global coded value" behavior. | Stands (§11.1); this spec only adds the hybrid tenant column + duplicate guard beneath it. |

---

## 11. Reconciliation & Open Questions

### 11.1 Supersession of `grade-level-setup.md` locked decisions (narrowed)

This spec **revises** only:

- **§3.1 / §3.5 tenancy** for `GradeLevel`/`Subject` ("global, no TenantId")
  → **strict operational, NOT NULL `tenant_id`**, filter `== current`.
- **§3.1** the `coded_value_id` unique index → `(tenant_id, coded_value_id)`;
  "exactly one `GradeLevel` per grade coded value" → "one `GradeLevel` per
  **(tenant, coded value)**." `Assignment.GradeLevelId` references the
  tenant's own `GradeLevel`. By symmetry `Subject` is strict operational.
- **§5.6** Period invariant ("at most one current period") → unchanged in
  spirit but **per-tenant**.

This spec **does NOT supersede** (these **stand**):

- **§3.3** (CodedValue global blueprint + `TenantCodedValueOverride` +
  `CodedValueResolver`). CodedValue becomes **hybrid** (nullable `tenant_id`)
  but the blueprint-and-override model is retained unchanged for `NULL` rows.
- **§5.4** (client-side name overlay). `GradeLevel.Name` remains the
  denormalized mirror of the resolved CodedValue name. The wizard's per-row
  "Override Name" UX (`gradelevel-wizard-subject-override-per-row.md`)
  **stands**, with one noted branch: when the referenced `CodedValue` is
  **tenant-owned** (`TenantId == current`), the row action is **"Rename"**
  (edits the owned `CodedValue.Name` directly + mirrors into
  `GradeLevel.Name`); when the referenced `CodedValue` is **shared**
  (`TenantId IS NULL`), the action is **"Override Name"** (upserts
  `TenantCodedValueOverride` + mirrors) — exactly the per-row spec's current
  behavior.

Everything else in `grade-level-setup.md` (the `Tenant` table, current-period
derivation, the `Assignment.GradeLevelId`/`SubjectId` migration, the
find-or-create wizard, the landing-page wrapper) **stands**. On approval,
this §11.1 is appended to `grade-level-setup.md` and
`gradelevel-wizard-subject-override-per-row.md` as a supersession/notice.

> **The override-pattern skill (`.skills/tenancy-override-pattern/SKILL.md`)
> is extended, not superseded:** it documents that reference data is hybrid
> — a `NULL`-tenant shared blueprint overlaid per tenant via
> `TenantCodedValueOverride` (retained), plus tenant-owned rows created by
> the wizard under a real tenant; the override layering is retained for
> shared rows and for feature flags.

### 11.2 Open questions (need your call before build)

| # | Question | Default if not answered |
|---|---|---|
| **Q-1** | Backfill sink for unattributable strict rows (students with `Guid.Empty`, etc.): a **System tenant** (backfill-only, no end-users) or attribute to the **first school tenant** (Hydeson)? | **System tenant** sink (keeps real school tenants clean; `Guid.Empty` never used as a value). |
| **Q-2** | Should `FeatureFlag` also become strict-tenant (removing `TenantFeatureFlagOverride`), for consistency with the strict operational entities? | **No — keep FeatureFlag global + override.** Flags are admin-created infrastructure; tenants toggle, not create. The wizard-creation trigger does not apply to flags. |
| **Q-3** | Confirm the duplicate-code guard behavior (FR-6): when a shared blueprint row with a code already exists, the tenant **overrides its name** rather than creating a tenant-owned duplicate. (Approved in principle; confirming the override-not-duplicate direction.) | **Yes — override, not duplicate.** |

> v2's Q-1 (override deprecation) and Q-4 (clone-on-provisioning trigger)
> are **dropped** — hybrid CodedValue retains the override pattern and needs
> no clone step.

---

## 12. Implementation sequencing

> Each step ends green (`dotnet build` + `dotnet test` for touched projects)
> and committed. No step touches >1 module.

- **Step 0 — Approve §3 + §11.2.** Nothing else starts until the
  hybrid/strict split and Q-1..Q-3 are approved. (Spec-driven-workflow gate.)
- **Step 1 — Core safety net + hybrid/strict bases + accessor + directory.**
  Exceptions (§8.1); `IHybridTenantEntity` +
  `TenantOrGlobalEntityTypeConfigurationBase<T>` +
  `ConfigureTenantOrGlobalQueryFilter` (hybrid); `ITenantContextAccessor`
  with save/restore + nesting tests (AC-4, EC-8); `ModuleDbContext`
  save-guards (AC-2, AC-3) incl. hybrid `Guid.Empty`-never rule (FR-8);
  `ITenantDirectory`; `OnModelCreating` entity audit (AC-17).
- **Step 2 — Settings: CodedValue hybrid + duplicate guard + override
  retained.** `CodedValue` → `IHybridTenantEntity` + hybrid config;
  `CreateCodedValueHandler`: real-tenant stamps owned row, default-tenant
  writes `NULL` blueprint, duplicate-code guard (AC-5..AC-10);
  `CodedValueResolver`/override CQRS **unchanged** (AC-8); migration
  `AddTenantIdToCodedValues` (existing rows → `NULL`); partial unique
  indexes (FR-7).
- **Step 3 — Students: GradeLevel + Subject + Period + operational entities
  strict.** Migrate `GradeLevel`/`Subject`/`Period`/`StudentEnrollment`/
  `GradeSubjectAssignment`/`StudentSubjectAssignment` (+strands/lessons) to
  strict config; `GetOrCreateGradeLevel`/`GetOrCreateSubject`/`CreatePeriod`
  no-empty guard + tenant stamp (AC-11); migrations §9.2 #2/#3/#4;
  `student_number` + `coded_value_id` composite indexes; backfill per §9.3
  (EC-7); update `AssignmentBackfillService` to stamp the assignment's
  tenant on find-or-create; `ListDeletedStudentsHandler` → `["SoftDelete"]`
  (EC-6); per-tenant Period invariant; `AddSystemTenant` (Q-1).
- **Step 4 — Assignments.** Verify `Assignment` (already strict) + owned
  types inherit via parent; add inheritance test (owned types have no DbSet
  — test via `Include`).
- **Step 5 — Outbox + design-time + workers.** `OutboxMessage.TenantId`
  (FR-15, AC-13); dispatcher tenant-context per message; design-time
  `SuppressTenantGuard`; `PromotionService` tenant loop via
  `ITenantDirectory` (AC-12); MigrationService seeders allow-list
  (`CodedValueSeeder` under `SuppressTenantGuard` writes `NULL` rows).
- **Step 6 — Roslyn analyzer (deferred to future task).** `SC0001` targeting `ITenantEntity` **and**
  `IHybridTenantEntity` DbSets + global-allow-list exemption (AC-14, AC-15,
  AC-16). New project `src/Analyzers/SchoolCollab.Tenancy.Analyzers`. **Status:** reserved for a future iteration — implementation deferred. The behavioral guarantees in FR-17/AC-14..16 will be delivered by the runtime `ValidateTenantFilters` model audit (AC-17) and `ModuleDbContext` save-guard (AC-2/AC-3) in the interim.
- **Step 7 — End-to-end + docs.** Integration test (two tenants: hybrid
  CodedValue shared-row visibility + tenant-owned isolation + override
  resolution + duplicate guard; strict GradeLevel isolation); performance
  check (NFR-1); update `auth-tenancy-pattern.md` §4.6; strike the
  future-work item in `auth-tenancy-integration.md`; append the §11.1
  supersession/notice to `grade-level-setup.md` +
  `gradelevel-wizard-subject-override-per-row.md`; update
  `.skills/tenancy-override-pattern/SKILL.md` with the hybrid
  (NULL-blueprint + tenant-owned) model.

**Effort:** ~12–16 days (less than v2: no clone-on-provisioning, no override
absorption; the CodedValue change is a column + filter + guard, and the
override pattern is untouched).

---

## 13. Self-Review Checklist (run after each step)

- [ ] `dotnet build` clean; `dotnet test` green for touched projects.
- [ ] Every strict entity passes `() => CurrentTenantId`; no `OR IS NULL`.
- [ ] `CodedValue` (the only hybrid entity) passes the hybrid predicate
      `() => CurrentTenantId OR IS NULL`; no other entity is hybrid.
- [ ] No strict create handler allows `CurrentTenantId == Guid.Empty`
      (FR-4) — it throws `TenantContextRequiredException`.
- [ ] `CodedValue.tenant_id` is never `Guid.Empty` (NULL or real Guid only).
- [ ] Duplicate-code guard rejects tenant-owned creation when a shared row
      with that `(parent, code)` exists (FR-6, AC-9).
- [ ] `CodedValueResolver` and override CQRS are unchanged (AC-8).
- [ ] No unnamed `IgnoreQueryFilters()` on strict or hybrid DbSets (SC0001).
- [ ] No `FindAsync`/`SingleOrDefaultAsync` on a strict/hybrid entity
      without the filter.
- [ ] Every strict tenant-scoped table has `tenant_id` NOT NULL + composite
      indexes (NFR-3); `coded_values` has the two partial unique indexes.
- [ ] No `Guid.Empty` in any strict table after backfill (EC-7).
- [ ] New tests trace to an AC-*; every AC-* has a passing test.
- [ ] `auth-tenancy-pattern.md` §4.6 + `grade-level-setup.md` + per-row
      override spec notices updated; override-pattern skill extended.
