# Grade-Level Setup Feature

> Status: **Implemented** — see progress tracker at `documents/specs/grade-level-setup-progress.md`

## Overview

This feature establishes grade levels as the **starting point** for setting up students and the thing assignments get paired to. `GradeLevel` is the **reporting source of truth** — the stable operational entity that assignments reference. Display names are tenant-resolved from coded values.

## Key Concepts

### GradeLevel (Students bounded context)

- **Global entity** (no tenant-scoping) — one `GradeLevel` per grade coded value across all tenants
- **Unique index on `CodedValueId`** — ensures exactly one GradeLevel per grade blueprint
- **Find-or-create pattern** — wizard creates or reuses existing GradeLevel by CodedValueId
- **Stats**: `SubjectCount` (global) and `StudentCount` (tenant-scoped) for the **current period** (derived server-side)

### Subject (Students bounded context)

- **Global entity** — one `Subject` per subject coded value
- **Unique index on `CodedValueId`**
- **Assigned to grades** via `GradeSubjectAssignment` for a specific `Period`
- **Create-for-grade** — `CreateSubjectForGrade` command creates (or reuses) a `Subject` **and** a `GradeSubjectAssignment` for the current period in one call

### Assignment → GradeLevel + Subject

- `Assignment.SubjectId` (required) → FK to `Subject.Id`
- `Assignment.GradeLevelId` (optional) → FK to `GradeLevel.Id`
- **No physical FK** (cross-database: Assignments DB vs Students DB)
- **Backfill** — `AssignmentBackfillService` migrates legacy coded-value IDs to FKs

### Current Period

- **Derived server-side** — the period whose `[StartDate, EndDate]` contains today
- **At most one active period** — invariant enforced on create/update/activate
- **No period picker on landing pages** — UI never chooses; server decides

## Tenant Display Names

Grade levels and subjects show **tenant-resolved names**:

1. `CodedValue` — global blueprint (e.g., "Grade 1", "Mathematics")
2. `TenantCodedValueOverride` — per-tenant override (e.g., "Standard 1", "Maths")
3. Client-side overlay — landing pages fetch resolved names via `CodedValuesApi.GetChildrenByParentCodeAsync("GRADE")`

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
| `GET` | `/students/subjects/by-grade/{gradeLevelId}` | Subjects for grade + current period |
| `GET` | `/students/subjects/{id}` | Get by ID |
| `POST` | `/students/subjects` | Create |
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
| `PUT` | `/api/coded-values/{id}/override` | Upsert tenant-specific display name |
| `DELETE` | `/api/coded-values/{id}/override` | Remove override (revert to global) |

## Frontend Pages

### Grade Levels (`/students/grade-levels`)

- **LandingPage wrapper** with search, stats columns, Edit/Delete actions
- **Stats columns**: Subject count and Student count link to filtered Subjects/Students pages
- **Period column**: Shows current period name or "No current period"
- **Tenant-resolved names**: Client-side join with coded-values API
- **Create wizard** (`/students/grade-levels/create`): 4-step wizard
  - Step 1: Coded-value dropdown with **"Override name"** and **"Create new"** buttons that open `GradeCodedValueDialog`
  - Save uses **find-or-create** (`GetOrCreateGradeLevelAsync`) by CodedValueId
  - Subject/student assignment uses the **current period** (derived server-side); steps 2–4 blocked if no current period

### Subjects (`/students/subjects`)

- **Mandatory GradeLevel filter** — must select a grade to see subjects
- **Subjects filtered** by selected grade + current period via `ListSubjectsByGrade`
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
// CreateSubjectForGrade and wizard subject/student assignment require a current period.
// If no Period covers today, the handler throws NoCurrentPeriodException.
new CreateSubjectForGrade(gradeLevelId, ...) // throws NoCurrentPeriodException when no period covers today
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

- `ListSubjectsByGradeHandlerTests` — 5 tests covering grade/period filtering
- `ListGradeLevelsForLandingHandlerTests` — 4 tests covering current-period stats
- `CreateSubjectForGradeHandlerTests` — 5 tests (create, reuse-by-coded-value, idempotent assignment, no-current-period throws, grade-not-found throws)
- `AssignmentTests` — 2 tests for empty SubjectId rejection
- `PeriodOverlapInvariantTests` — 6 tests for overlap enforcement
- `GetOrCreateGradeLevelHandlerTests` — 2 tests for find-or-create (create + reuse)

### Manual Testing Checklist

1. **Grade Levels landing**: Verify stats columns link to filtered Subjects/Students
2. **Subjects landing**: Verify mandatory GradeLevel filter, period derivation
3. **Subjects create**: `+ New Subject` carries `gradeLevelId`, creates Subject + GradeSubjectAssignment
4. **Students landing**: Verify deep-link from GradeLevels, filter chip
5. **Assignments Create**: Verify GradeLevel→Subject cascading dropdown
6. **Tenant switch**: Verify grade names change based on tenant override
7. **Delete guards**: Try deleting a GradeLevel with students enrolled
8. **Wizard Step 1**: "Override name" and "Create new" buttons open `GradeCodedValueDialog`
9. **Wizard save**: Uses find-or-create (reuses existing GradeLevel by CodedValueId)
10. **No current period**: Wizard blocks steps 2–4; CreateSubjectForGrade returns 409

## Related Specs

- `documents/specs/landing-page-wrapper.md` — LandingPage component
- `documents/specs/multi-tenant-coded-values.md` — Coded value tenancy
- `documents/specs/auth-tenancy-integration.md` — Tenant context propagation