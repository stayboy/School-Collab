# Grade-Level Setup Feature

> Status: **Implemented** — see progress tracker at `documents/specs/grade-level-setup-progress.md`

## Overview

This feature establishes grade levels as the **starting point** for setting up students and the thing assignments get paired to. `GradeLevel` is the **reporting source of truth** — the stable operational entity that assignments reference. Display names are tenant-resolved from coded values, with a per-tenant override layer for tenants that need a different name.

## Key Concepts

### GradeLevel (Students bounded context)

- **Global entity** (no tenant-scoping) — one `GradeLevel` per grade coded value across all tenants
- **Unique index on `CodedValueId`** — ensures exactly one GradeLevel per grade blueprint
- **Find-or-create pattern** — wizard creates or reuses existing GradeLevel by CodedValueId
- **Stats**: `SubjectCount` (global) and `StudentCount` (tenant-scoped) for the **current period** (derived server-side)

### Subject (Students bounded context)

- **Global entity** — one `Subject` per subject coded value
- **Unique index on `CodedValueId`**
- **Not period-bound** — the `Subject` itself is global; only the `GradeSubjectAssignment` linking it to a grade is period-scoped
- **Assigned to grades** via `GradeSubjectAssignment` for a specific `Period` — querying subjects for a grade returns all assignments across periods when no period is specified
- **Create-for-grade** — `CreateSubjectForGrade` command creates (or reuses) a `Subject` **and** a `GradeSubjectAssignment` for the current period in one call
- **Get-or-create** — `GetOrCreateSubject` command finds-or-creates a `Subject` by `CodedValueId` (used by the wizard's auto-add flow on subject picker selection)

### Assignment → GradeLevel + Subject

- `Assignment.SubjectId` (required) → FK to `Subject.Id`
- `Assignment.GradeLevelId` (optional) → FK to `GradeLevel.Id`
- **No physical FK** (cross-database: Assignments DB vs Students DB)
- **Backfill** — `AssignmentBackfillService` migrates legacy coded-value IDs to FKs

### Current Period

- **Derived server-side** — the period whose `[StartDate, EndDate]` contains today
- **At most one active period** — invariant enforced on create/update/activate
- **No period picker on landing pages** — UI never chooses; server decides

### Tenant Context

- `TenantContext.TenantId` is a non-nullable `Guid`
- The **default tenant** is `Guid.Empty` with name "System" — used when no real tenant is in scope (dev tenant switcher's "(default tenant)" entry, background workers)
- `TenantContext.IsDefault` is the explicit helper for branching on "is there a real tenant?" (preferred over `Guid.Empty` direct comparison)
- `ITenantProvider` is a **singleton** backed by `AsyncLocal<TenantContext>`, set per-circuit by `TenantClaimsTransformation` from the auth claims

## API Endpoints

### Grade Levels

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/students/grade-levels` | List all grade levels |
| `GET` | `/students/grade-levels/landing` | Landing page with current-period stats |
| `GET` | `/students/grade-levels/{id}` | Get by ID |
| `GET` | `/students/grade-levels/by-coded-value/{id}` | Get by CodedValueId |
| `POST` | `/students/grade-levels` | Create |
| `POST` | `/students/grade-levels/get-or-create` | Find or create by CodedValueId |
| `PUT` | `/students/grade-levels/{id}` | Update |
| `DELETE` | `/students/grade-levels/{id}` | Delete (referential guard) |

### Subjects

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/students/subjects` | List all subjects |
| `GET` | `/students/subjects/by-grade/{gradeLevelId}` | Subjects for grade; if `periodId` is omitted, returns all assignments across periods (subjects are not period-bound) |
| `GET` | `/students/subjects/{id}` | Get by ID |
| `POST` | `/students/subjects` | Create |
| `POST` | `/students/subjects/get-or-create` | Find or create by CodedValueId (used by the wizard's auto-add flow) |
| `POST` | `/students/subjects/for-grade` | Create Subject + GradeSubjectAssignment for current period |
| `PUT` | `/students/subjects/{id}` | Update |
| `DELETE` | `/students/subjects/{id}` | Delete (referential guard) |

### Students

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/students` | List all students |
| `GET` | `/students/by-grade/{gradeLevelId}` | Students enrolled in grade for current period |
| `GET` | `/students/deleted` | List soft-deleted students |

### Coded Value Overrides

| Method | Endpoint | Description |
|--------|----------|-------------|
| `PUT` | `/api/coded-values/{id}/override` | Upsert tenant-specific display name. In default-tenant mode, rewrites the global `CodedValue` directly (no per-tenant row) |
| `DELETE` | `/api/coded-values/{id}/override` | Remove override (revert to global). No-op in default-tenant mode |

## Frontend Pages

### Grade Levels (`/students/grade-levels`)

- **LandingPage wrapper** with search, stats columns, Edit/Delete actions
- **Stats columns**: Subject count and Student count link to filtered Subjects/Students pages
- **Period column**: Shows current period name or "No current period"
- **Tenant-resolved names**: Client-side join with coded-values API
- **Create wizard** (`/students/grade-levels/create`): **3-step** `FluentWizard` (matches the assignment wizard UX pattern)
  - **Step 1 — "Grade & Subjects"**: grade + subject coded-value pickers on one screen, each with its own confirmation
    - Each section uses a **1/3 + 2/3 split** via `FluentGrid` (12-col system: `xs="12" md="4"` for the first column, `xs="12" md="8"` for the second). Columns stack on mobile (`xs="12"`)
    - The **first column** holds the section header (icon + title on one row) AND the picker stacked directly underneath, so the inputs sit right under the section title
    - The **second column** holds wider content per section:
      - **Grade section**: wide `FluentCard` showing the resolved name, code, "Overridden" badge (and "Reset to default" link when overridden). Uses native `FluentCard` / `FluentLabel` / `FluentBadge` / `FluentAnchor` / `FluentMessageBar` — no custom layout CSS. The `Label` on the `CodedValueDropdown` is removed because the section title already labels it
      - **Subject section**: the **assigned-subjects list** with Remove buttons, or `FluentMessageBar` empty-state when no subjects are assigned
    - A **sizeable 48px gap** separates the grade and subject sections for visual breathing room
    - **Grade picker**: coded-value dropdown + **Override name** / **New grade** buttons stacked below it
    - **Subject picker**: subject coded-value dropdown — picking a value **auto-adds the subject to the grade's assigned list** (no separate "Add to grade" button). The picker's `SelectedIdChanged` handler calls `GetOrCreateSubjectAsync` directly, appends the subject to `_assignedSubjects`, then resets the picker to `null` so the user can pick another subject immediately. A compact `FluentCard` chip under the picker confirms the most recent add ("✓ Mathematics added"). A **New subject** button opens the generic `CodedValueDialog` to create a new subject coded value (the dialog's success path also triggers auto-add)
    - **Override gating on real tenant**: the **Override name** button and **Reset to default** link on both the grade and subject sections are only shown when `IsRealTenant` is `true` (i.e., the current tenant is not the default/system tenant). The override handler updates the global `CodedValue` directly in default-tenant mode, so the per-tenant UI is meaningless there
    - `IsRealTenant` is a **field** (not a computed property) that gets refreshed in `OnInitializedAsync` and `OnAfterRender` — Blazor's diffing doesn't re-evaluate a property whose backing store (the singleton `TenantProvider`'s `AsyncLocal`) changes without a `[Parameter]` change. The `OnAfterRender` check catches tenant switches (e.g., via the dev tenant switcher's `forceLoad: true` page reload) and triggers a re-render
  - **Step 2 — "Students"**: student-enrollment grid (no current period → blocked with a warning)
  - **Step 3 — "Review"**: review card with `review-grid`/`review-row` pattern (name, tenant override badge, current period, subject count, student count) — `DeferredLoading="true"` so it doesn't render until visited
  - The bottom action bar uses `<ButtonTemplate Context="stepIndex">` with **Back** (arrow icon), **Cancel**, and **Continue** / **Save and Finish** (save icon) — matches the assignment wizard
  - `ErrorBoundary` wraps the wizard content; the outer `_error` and per-step `_step1Error`/`_step2Error` provide granular validation messages
  - `OnChange` handlers on the wizard steps gate navigation (cancel the change with `_step1Error`/`_step2Error` if validation fails)
  - `@implements IDisposable` with `CancellationTokenSource` for safe load cancellation
  - Save uses **find-or-create** (`GetOrCreateGradeLevelAsync`) by CodedValueId, with `Level` and `DisplayOrder` taken from the coded value (not from user input)
  - Subject/student assignment uses the **current period** (derived server-side); step 2 blocked if no current period

### Subjects (`/students/subjects`)

- **Mandatory GradeLevel filter** — must select a grade to see subjects
- **Subjects filtered** by selected grade via `ListSubjectsByGrade` (returns all assignments across periods when no `periodId` is specified — subjects are global)
- **Create**: Routes to `/students/subjects/create?gradeLevelId={id}` — creates `Subject` **and** `GradeSubjectAssignment` for the current period in one server call (`POST /students/subjects/for-grade`)
- **Edit/Delete** row actions

### Students (`/students`)

- **Deep-link filter**: `?gradeLevelId={guid}&periodId={guid?}` query params
- **Filter chip**: Shows active grade filter with dismiss button
- **Soft delete**: Show deleted toggle, recover action

### Assignments Create (`/assignments/create`)

- **GradeLevel dropdown** — selects from grade levels
- **Subject dropdown** — cascading filter by selected GradeLevel + current period
- Subject dropdown disabled until grade selected

## Tenant Display Names

The grade/subject display name is **sourced from the coded value, with a per-tenant override** layered on top:

1. `CodedValue` — global blueprint (e.g., "Grade 1", "Mathematics")
2. `TenantCodedValueOverride` — per-tenant override (e.g., "Standard 1", "Maths")
3. `CodedValueDto.IsOverridden` — boolean flag the resolver sets when an override contributed to the resolved name; the UI uses this to show an "Overridden" badge in the wizard and elsewhere
4. `CodedValueResolver.ResolveAsync` — applies the override on the server side; landing pages and dropdowns read the resolved name from this DTO
5. `GradeLevel.Name` is a **mirrored** field (not the source of truth) — the wizard writes the resolved name into it on save, but the user cannot edit it directly

### Default-tenant branch

When the current tenant is the sentinel "default" tenant (`Guid.Empty` / `IsDefault`), the override handler **rewrites the global `CodedValue` directly** instead of creating a `TenantCodedValueOverride` row. Rationale: per-tenant overrides are meaningless without a real tenant. In that mode:

- The wizard's **Override name** button and **Reset to default** link are hidden (`IsRealTenant` is `false`)
- "New" coded value still works (it goes through the same `CreateAsync` path)
- The UI is honest about what's happening — there's no per-tenant concept to manage

The grade-level wizard exposes the override model explicitly:

- The dropdown is the only way to pick a grade (no free-text Name input)
- The resolved name is shown read-only below the picker
- The Override button is the single, discoverable way to change the display name per tenant (only when a real tenant is in scope)
- The "Overridden" badge + "Reset to default" link make the override state visible

## Domain Invariants

### Period Overlap

```csharp
// No two periods may have overlapping date ranges
Period.Create(startDate, endDate) // throws PeriodOverlapException if overlaps exist
```

### GradeLevel/Subject Delete Guard

```csharp
// Cannot delete if referenced
GradeLevel.Delete() // throws GradeLevelReferencedException if StudentEnrollments or GradeSubjectAssignments exist
Subject.Delete()     // throws SubjectReferencedException if GradeSubjectAssignments or StudentSubjectAssignments exist
```

### Assignment Subject Required

```csharp
// Subject is always required
Assignment.Create(..., subjectId: Guid.Empty) // throws ArgumentException
```

### No Current Period

```csharp
// CreateSubjectForGrade requires a current period. If no Period covers today,
// the handler throws NoCurrentPeriodException.
new CreateSubjectForGrade(gradeLevelId, ...) // throws NoCurrentPeriodException when no period covers today
```

### Subjects are not period-bound

```csharp
// Subjects are global entities — the GradeSubjectAssignment is what is
// period-scoped. ListSubjectsByGrade without a periodId returns all
// assignments across periods, not an empty array.
ListSubjectsByGrade(gradeLevelId) // returns subjects assigned to this grade in any period
ListSubjectsByGrade(gradeLevelId, periodId) // returns subjects assigned to this grade in the specific period
```

## Client Methods (StudentsApiClient)

```csharp
// Grade levels
Task<GradeLevelDto[]?> ListGradeLevelsAsync(CancellationToken ct = default);
Task<GradeLevelLandingDto[]?> ListGradeLevelsForLandingAsync(CancellationToken ct = default);
Task<GradeLevelDto?> GetGradeLevelByIdAsync(Guid id, CancellationToken ct = default);
Task<Guid> CreateGradeLevelAsync(CreateGradeLevelRequest req, CancellationToken ct = default);
Task<GradeLevelDto> GetOrCreateGradeLevelAsync(GetOrCreateGradeLevelRequest req, CancellationToken ct = default);
Task UpdateGradeLevelAsync(Guid id, UpdateGradeLevelRequest req, CancellationToken ct = default);
Task DeleteGradeLevelAsync(Guid id, CancellationToken ct = default);

// Subjects
Task<SubjectDto[]?> ListSubjectsAsync(CancellationToken ct = default);
Task<SubjectDto[]?> ListSubjectsByGradeAsync(Guid gradeLevelId, Guid? periodId = null, CancellationToken ct = default);
Task<SubjectDto> GetOrCreateSubjectAsync(CreateSubjectRequest req, CancellationToken ct = default);
Task<SubjectDto> CreateSubjectForGradeAsync(CreateSubjectForGradeRequest req, CancellationToken ct = default);
Task DeleteSubjectAsync(Guid id, CancellationToken ct = default);

// Students by grade
Task<StudentDto[]?> ListStudentsByGradeAsync(Guid gradeLevelId, Guid? periodId = null, CancellationToken ct = default);
```

## Cross-Database Backfill

The `AssignmentBackfillService` runs in `MigrationService` after migrations:

1. Reads assignments with `subject_id IS NULL` from Assignments DB
2. Looks up coded values in Settings DB
3. Finds-or-creates Subject/GradeLevel in Students DB
4. Updates assignment FKs via raw SQL

**Note**: Old columns (`subject_coded_value_id`, `grade_coded_value_id`) are kept for the backfill and can be dropped in a future migration.

## Testing

### Unit Tests

- `ListSubjectsByGradeHandlerTests` — 4 tests covering grade filtering, period filtering, and "no current period returns all assignments"
- `ListGradeLevelsForLandingHandlerTests` — 4 tests covering current-period stats
- `CreateSubjectForGradeHandlerTests` — 5 tests (create, reuse-by-coded-value, idempotent assignment, no-current-period throws, grade-not-found throws)
- `GetOrCreateSubjectHandlerTests` — 2 tests (create + reuse-by-coded-value)
- `GetOrCreateGradeLevelHandlerTests` — 2 tests for find-or-create (create + reuse)
- `AssignmentTests` — 2 tests for empty SubjectId rejection
- `PeriodOverlapInvariantTests` — 6 tests for overlap enforcement
- `CodedValueOverrideHandlerTests` — 8 tests covering real-tenant and default-tenant branches (uses a `MutableTenantProvider` so each test opts into either scenario)

### Manual Testing Checklist

1. **Grade Levels landing**: Verify stats columns link to filtered Subjects/Students
2. **Subjects landing**: Verify mandatory GradeLevel filter; subjects show across all periods when no `periodId` is provided
3. **Subjects create**: `+ New Subject` carries `gradeLevelId`, creates Subject + GradeSubjectAssignment
4. **Students landing**: Verify deep-link from GradeLevels, filter chip
5. **Assignments Create**: Verify GradeLevel→Subject cascading dropdown
6. **Tenant switch (default → real)**: Override name button and Reset to default link appear in both grade and subject sections
7. **Tenant switch (real → default)**: Override name button and Reset to default link disappear; New grade / New subject still work (rewrite global)
8. **Delete guards**: Try deleting a GradeLevel with students enrolled
9. **Wizard Step 1 — Grade**: Coded-value dropdown + Override name / New grade buttons below; selected grade shows in 2nd column as a `FluentCard` with name + code + optional Overridden badge + Reset link (only when `IsRealTenant`)
10. **Wizard Step 1 — Subjects**: Subject coded-value picker auto-adds to the assigned list on selection; compact chip confirms the last add; New subject button opens the dialog (which also auto-adds on creation)
11. **Wizard save**: Uses find-or-create (reuses existing GradeLevel by CodedValueId) with the resolved name and the coded value's own `Level`/`DisplayOrder`; bottom action bar (Back / Cancel / Continue / Save and Finish) matches the assignment wizard
12. **No current period**: Wizard blocks step 2 (students); subject picker still works (assignments are created on save when a period exists)

## Related Specs

- `documents/specs/landing-page-wrapper.md` — LandingPage component
- `documents/specs/multi-tenant-coded-values.md` — Coded value tenancy
- `documents/specs/auth-tenancy-integration.md` — Tenant context propagation
