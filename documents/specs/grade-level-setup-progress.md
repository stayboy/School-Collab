# Grade-Level Setup — Implementation Progress / Handoff

Companion to `documents/specs/grade-level-setup.md` (the locked spec).
This file tracks PR-by-PR execution state so a future session can resume.

**Spec:** `documents/specs/grade-level-setup.md` — 10-PR plan, all §11 questions resolved.
**Last updated:** All 10 PRs complete. Build green, tests passing.

---

## Current build / test state

- **Solution build: GREEN** (0 errors)
- **Tests: GREEN**
  - Assignments: 43 tests
  - Students: 34 tests (incl. 5 new `CreateSubjectForGradeHandlerTests`)
  - Core: 38, Settings: 334, Admin: 20, Architecture: 8

---

## PRs landed (build green, tests pass)

### PR 1 — Tenant registry + sample seeding + dev tenant switcher ✅
- `Settings.Core/Domain/Tenant.cs` — global `Tenant` entity, unique on `Name`
- `MigrationService/Seeding/TenantSeeder.cs` — seeds 'Hydeson School' + 'Little Legends'
- Dev tenant switcher in admin header (Testing env only)

### PR 2 — Coded-value override endpoints + client methods ✅
- `PUT/DELETE /api/coded-values/{id}/override`
- `CodedValuesApiClient.UpsertOverrideAsync` / `RemoveOverrideAsync`

### PR 3 — GradeLevel+Subject unique indexes, find-or-create, current-period landing query ✅
- Unique indexes on `CodedValueId` for both entities
- `PeriodOverlapException` + invariant enforcement
- `ListGradeLevelsForLanding` query/handler with current-period stats

### PR 4 — Assignment → GradeLevel + Subject migration ✅
- Domain: `SubjectCodedValueId`→`SubjectId`, `GradeCodedValueId`→`GradeLevelId`
- Contracts: renamed fields in DTOs
- UI: Create.razor cascading GradeLevel→Subject dropdowns; Edit.razor field renames
- Students: `ListSubjectsByGrade` endpoint + client
- Backfill: `AssignmentBackfillService` for cross-DB migration

### PR 5 — Grade-coded-value dialog ✅
- `GradeCodedValueDialog.razor` in `Admin.Shared.Components`
- Create mode: creates new coded value under GRADE parent
- Override mode: upserts tenant-specific display name override
- Reset: removes override, reverts to global name
- Dialog data/result types: `GradeCodedValueDialogData`, `GradeCodedValueDialogResult`
- **Wired into `GradeLevelWizard.razor` Step 1**: "Override name" + "Create new" buttons open the dialog via `IDialogService.ShowDialogAsync<GradeCodedValueDialog>`
- **Wizard save uses `GetOrCreateGradeLevelAsync`** (find-or-create by CodedValueId) + **current period** (derived client-side, same rule as server)
- Steps 2–4 blocked with warning when no current period

### PR 6 — GradeLevel + Subject edit/delete ✅
- Domain: `GradeLevel.Delete()` + `Subject.Delete()` methods
- Exceptions: `GradeLevelReferencedException` + `SubjectReferencedException`
- Commands: `DeleteGradeLevel` + `DeleteSubject` with referential integrity checks
- Endpoints: `DELETE /students/grade-levels/{id}` + `DELETE /students/subjects/{id}`
- Client: `DeleteGradeLevelAsync()` + `DeleteSubjectAsync()`
- UI: `GradeLevels/Edit.razor` + `Subjects/Edit.razor` pages

### PR 7 — Grade-Level landing page onto LandingPage ✅
- `GradeLevels.razor` rewritten onto `<LandingPage<GradeLevelLandingDto>>`
- Stats columns: Subjects/Students link to filtered pages with `gradeLevelId`/`periodId`
- Current-period column shows period name or "No current period"
- Tenant-resolved name overlay via `CodedValuesApi.GetChildrenByParentCodeAsync("GRADE")`
- Edit/Delete row actions with confirmation dialogs

### PR 8 — Subjects landing page onto LandingPage ✅
- `Subjects.razor` rewritten onto `<LandingPage<SubjectDto>>`
- **Mandatory GradeLevel filter** in `<ToolbarFilters>` (FluentSelect)
- Subjects loaded via `ListSubjectsByGradeAsync(gradeLevelId, periodId?)`
- Query param support: `?gradeLevelId={guid}&periodId={guid?}`
- Edit/Delete row actions with confirmation dialogs
- Empty state messages for no-grade-level and no-subjects conditions
- **`POST /students/subjects/for-grade` endpoint** + `CreateSubjectForGrade` command/handler (creates Subject + GradeSubjectAssignment for current period)
- **`CreateSubjectForGradeAsync` client method** + `Subjects/Create.razor` page (`+ New Subject` always carries `gradeLevelId`, creates Subject + GradeSubjectAssignment in one call)

### PR 9 — Deep-link filter contract (Students page) ✅
- `/students?gradeLevelId={guid}&periodId={guid?}` query params
- `ListStudentsByGrade` query/handler: returns students enrolled in grade for period
- `GET /students/by-grade/{gradeLevelId}?periodId={guid?}` endpoint
- `StudentsApiClient.ListStudentsByGradeAsync()` client method
- Students page: filter chip showing active grade filter with dismiss button
- GradeLevels landing: Subjects/Students counts deep-link to filtered pages

### PR 10 — Docs ✅
- `documents/solution/grade-level-setup.md` — feature documentation (this file's companion)
- `documents/specs/grade-level-setup-progress.md` — progress tracker

---

## Remaining PRs

_All 10 PRs are complete._

---

## Key files added/modified

### Backend
- `Students.Core/CQRS/Students/Queries/ListStudentsByGrade/` — query + handler
- `Students.Core/CQRS/Subjects/Queries/ListSubjectsByGrade/` — query + handler
- `Students.Core/CQRS/Subjects/Commands/CreateSubjectForGrade/` — command + handler (creates Subject + GradeSubjectAssignment for current period)
- `Students.Core/Domain/GradeLevel.cs` — `Delete()` method
- `Students.Core/Domain/Subject.cs` — `Delete()` method
- `Students.Core/Domain/Events/GradeLevelDeletedEvent.cs`, `SubjectDeletedEvent.cs`
- `Students.Core/Domain/Exceptions/GradeLevelReferencedException.cs`, `SubjectReferencedException.cs`
- `Students.Core/Domain/Exceptions/DomainExceptions.cs` — added `NoCurrentPeriodException`
- `Students.Core/CQRS/GradeLevels/Commands/DeleteGradeLevel/`
- `Students.Core/CQRS/Subjects/Commands/DeleteSubject/`
- `Students.Core/Data/Repositories/ISubjectRepository.cs` + `SubjectRepository.cs` — `GetByCodedValueIdAsync`
- `Students.Core/Data/Repositories/IPeriodRepository.cs` + `PeriodRepository.cs` — `GetCurrentPeriodAsync`
- `Students.Api/Endpoints/StudentRoutes.cs` — `/by-grade/{gradeLevelId}` endpoint
- `Students.Api/Endpoints/SubjectRoutes.cs` — `/by-grade/{gradeLevelId}` + `/for-grade` endpoints
- `Students.Api/Endpoints/GradeLevelRoutes.cs` — DELETE + `/get-or-create` + `/landing` endpoints
- `Students.Admin/Services/StudentsApiClient.cs` — new client methods (`GetOrCreateGradeLevelAsync`, `CreateSubjectForGradeAsync`, `ListStudentsByGradeAsync`, etc.)

### Frontend
- `Admin.Shared/Components/GradeCodedValueDialog.razor` — create/override dialog
- `Admin.Shared/Components/GradeCodedValueDialogData.cs` — dialog types
- `Students.Admin/Components/Pages/Students/GradeLevels/GradeLevels.razor` — LandingPage rewrite
- `Students.Admin/Components/Pages/Students/GradeLevels/Edit.razor` — grade level edit page
- `Students.Admin/Components/Pages/Students/GradeLevels/GradeLevelWizard.razor` — dialog wiring + find-or-create + current period
- `Students.Admin/Components/Pages/Students/Subjects/Subjects.razor` — LandingPage rewrite with GradeLevel filter
- `Students.Admin/Components/Pages/Students/Subjects/Create.razor` — subject create page (creates Subject + GradeSubjectAssignment)
- `Students.Admin/Components/Pages/Students/Subjects/Edit.razor` — subject edit page
- `Students.Admin/Components/Pages/Students/Index.razor` — added grade/period filter support

### Tests
- `Students.Tests.Unit/CreateSubjectForGradeHandlerTests.cs` — 5 tests (create, reuse, idempotent, no-period, no-grade)
- `Students.Tests.Unit/StudentsTestScope.cs` — added `Subjects` + `GradeSubjectAssignments` repository properties

### Docs
- `documents/solution/grade-level-setup.md` — feature documentation

---

## Key decisions / gotchas

- **Cross-DB**: GradeLevel/Subject live in Students DB; Assignment in Assignments DB → **no physical FK**.
- **Delete is hard delete** with referential guard — throws if enrollments/assignments exist.
- **Subjects page requires GradeLevel filter** — no "all subjects" view; must select a grade first.
- **Create button on Subjects page** routes to `/students/subjects/create?gradeLevelId={id}`.
- **GradeLevels landing shows tenant-resolved names** via client-side join with coded-values API.
- **Students by grade** filters by active enrollments in the current (or specified) period.
- **CreateSubjectForGrade** derives the current period server-side via `IPeriodRepository.GetCurrentPeriodAsync()`; throws `NoCurrentPeriodException` if no period covers today.
- **Wizard period derivation** is done client-side from `ListPeriodsAsync()` (same rule: `StartDate <= today <= EndDate`); steps 2–4 are blocked when no current period exists.