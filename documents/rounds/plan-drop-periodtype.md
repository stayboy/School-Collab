# Plan: Drop `PeriodType`, adopt `AcademicYearDivision` as the single kind field

## Goal
Remove the `PeriodType` enum and property entirely. Use `AcademicYearDivision` (`None`, `Terms`, `Semesters`) as the single field that describes a period:

- `Division == None` with `ParentPeriodId == null` → a plain academic year (no sub-periods allowed).
- `Division == Terms` with `ParentPeriodId == null` → an academic year that may contain **only** term sub-periods.
- `Division == Semesters` with `ParentPeriodId == null` → an academic year that may contain **only** semester sub-periods.
- `Division == Terms`/`Semesters` with `ParentPeriodId != null` → a term/semester sub-period, respectively.

A parent academic year and its sub-periods must share the **same** division (one-kind rule).

Sub-periods are created from the parent year's sub-period surfaces (`SubPeriodsSection`, `SubPeriodsListDialog`, or the `?parent=` route); the standalone create form always creates a top-level academic year whose Division selects its sub-period kind.

## Scope

### Domain / data layer (`src/Students/SchoolCollab.Students.Core`)
1. Delete `Domain/PeriodType.cs`.
2. `Domain/Period.cs`
   - Remove `PeriodType` property.
   - Make `Division` non-nullable (`AcademicYearDivision`).
   - `Create`/`Update` signatures: `(..., AcademicYearDivision division, Guid? parentPeriodId = null)`.
   - Validation rules:
     - `ParentPeriodId == null` → top-level academic year; `Division` may be `None`, `Terms`, or `Semesters`.
     - `ParentPeriodId != null` → sub-period; `Division` must **not** be `None`; parent must exist, be top-level (`ParentPeriodId == null`), and have the **same** `Division`.
   - `SetNextPeriod` allowed only when `ParentPeriodId == null`.
3. `DTOs/PeriodDto.cs`
   - Remove `PeriodType` string property.
   - Keep `Division` string property (now non-null).
4. `Data/Configurations/PeriodConfiguration.cs`
   - Remove `PeriodType` mapping/indexes.
   - Make `Division` required with default `None`.
   - Update unique indexes:
     - At most one active **top-level** year per tenant: filter `parent_period_id IS NULL AND status = 1`.
     - At most one active sub-period per **parent academic year**: filter `parent_period_id IS NOT NULL AND status = 1`.
5. `Data/Repositories/IPeriodRepository.cs` + `PeriodRepository.cs`
   - Replace `PeriodType?` filters with `AcademicYearDivision?` where needed.
   - `GetActiveAcademicYearAsync` → active top-level year (`ParentPeriodId == null`).
   - `GetActiveSubPeriodsAsync(Guid parentPeriodId, AcademicYearDivision? division, ...)` → active children of that year, optionally filtered by division.
   - `GetSubPeriodsAsync` / `GetNonCompletedSubPeriodCountAsync` → use `ParentPeriodId` only.
   - `GetCurrentPeriodAsync` ordering: prefer sub-periods over top-level years, then earliest start date.
   - `ListAsync` projects DTO without `PeriodType`.
6. Add migration `DropPeriodType` that:
   - Drops the `period_type` column.
   - Alters `division` to `NOT NULL` with default `0`.
   - Re-creates the active-year and active-sub-period unique indexes with the new filters above.
   - Updates `StudentsDbContextModelSnapshot.cs` accordingly.
   - Note: the dev `students-db` is `EnsureCreated`. The worker must **not** assume EF migrations run automatically in dev. We will drop/recreate the dev `students-db` from the parent after the code changes.

### CQRS (`src/Students/SchoolCollab.Students.Core/CQRS`)
7. `CreatePeriod.cs` / `CreatePeriodHandler.cs` and `UpdatePeriod.cs` / `UpdatePeriodHandler.cs`
   - Remove `PeriodType` from command records.
   - Apply the hierarchy rules from §2.
   - Update overlap query arguments (top-level vs sub-period detection now based on `ParentPeriodId`).
   - Update division-change guard: a top-level year may not change `Division` while it has non-completed sub-periods.
8. `ActivatePeriodHandler.cs`
   - Detect year by `ParentPeriodId == null`.
   - Sibling active-sub-period check uses parent id only (one active sub-period per year).
   - Auto-activate earliest sub-period on year activation (FR-H4a).
9. `CompletePeriodHandler.cs` / `ArchivePeriodHandler.cs`
   - Cascade to sub-periods based on `ParentPeriodId`.
10. All period queries (`ListPeriods`, `ListSubPeriods`, `GetPeriodById`, `GetActiveAcademicYear`, `GetActiveSubPeriod`)
    - Project DTO without `PeriodType`; use `Division` and `ParentPeriodId` for filtering/sorting.

### Downstream domain logic
11. `Tenancy/ActivePeriodProvider.cs` → return active period info using `Division` and `ParentPeriodId`.
12. `CQRS/ActivityGroups/Commands/AddMembership/AddMembershipHandler.cs` and `RolloverActivityGroup/RolloverActivityGroupHandler.cs`
    - Map `EnrollmentSpan` to `AcademicYearDivision`: `WholeAcademicYear` → top-level year, `Termly` → `Terms`, `Semester` → `Semesters`.
    - Query active period / sub-period by division instead of `PeriodType`.
13. `CQRS/Enrollments/Commands/EnrollStudent/EnrollStudentHandler.cs` → grade enrollment requires the active period to be a top-level year (`ParentPeriodId == null`).
14. `CQRS/TopicAssignments/TopicAssignmentPeriodValidator.cs` and `CQRS/Topics/Commands/CreateTopicForGrade/CreateTopicForGradeHandler.cs`
    - Map span to `AcademicYearDivision`; validate period division and parent/active-year membership.
15. `CQRS/StudentTopicAssignments/Commands/AssignStudentTopic/AssignStudentTopicHandler.cs` → adjust active-year check.

### API + client contracts
16. `src/Students/SchoolCollab.Students.Api/Endpoints/PeriodRoutes.cs`
    - Remove `PeriodType` from request records and from `CreatePeriod`/`UpdatePeriod` command construction.
    - Construct commands with the provided `Division` and `ParentPeriodId`.
17. `src/Students/SchoolCollab.Students.Application/Services/StudentsApiClient.cs`
    - Remove `PeriodType` from `PeriodDto` and from `CreatePeriodRequest`/`UpdatePeriodRequest`.
    - `Division` becomes required (`AcademicYearDivision`, not nullable).

### UI (`src/Students/SchoolCollab.Students.Application/Components/Pages/Periods`)
18. `PeriodForm.razor`
    - Replace the two selectors (PeriodType + Division) with a single **Division** selector: `None` / `Terms` / `Semesters`.
    - The standalone create/edit form always edits a top-level academic year; `Division` selects the kind of sub-periods the year may host.
    - When entered via `?parent=…` (sub-period intent), lock `Division` to the parent year's division and show the parent dropdown read-only.
    - Pass `Division` (as enum) to `CreatePeriodRequest`/`UpdatePeriodRequest`.
19. `Periods.razor`, `Edit.razor`, `SubPeriods.razor`, `SubPeriodsListDialog.razor`, `SubPeriodsSection.razor`
    - Remove all `PeriodType` string comparisons.
    - Render kind labels from `Division` + `ParentPeriodId`:
      - top-level → "Academic Year"
      - `Division.Terms` child → "Term"
      - `Division.Semesters` child → "Semester"
    - Sub-period section visibility for a year is driven by `Division != None`.
20. `JoinGroupsDialog.razor`, `TopicAssignmentPeriodEditDialog.razor`, `TopicCreateDialog.razor`
    - Replace `PeriodType` comparisons with `Division`/`ParentPeriodId` checks.

### Tests
21. Update all unit tests that reference `PeriodType`:
    - `PeriodHierarchy*Tests.cs`, `ActivePeriodProviderTests.cs`, `TopicAssignmentPeriodTests.cs`, `UpdateTopicAssignmentPeriodTests.cs`, `CreateSubjectForGradeHandlerTests.cs`, `AssignStudentTopicHandlerTests.cs`, `ActivityGroupPeriodAlignedSpanTests.cs`, `PeriodFormTests.cs`, `PeriodEditPageTests.cs`, `StudentsStrictTenancyTests.cs`.
    - Mechanical mapping: `PeriodType.AcademicYear` → `AcademicYearDivision.None` (with no parent), `PeriodType.Term` → `AcademicYearDivision.Terms` (with parent), `PeriodType.Semester` → `AcademicYearDivision.Semesters` (with parent), type comparisons → division/parent comparisons.
22. Re-run affected test projects: `SchoolCollab.Students.Tests.Unit`, `SchoolCollab.Admin.Tests.Unit`.

### Bug-fix follow-ups identified by UI tester
23. `TopicCreateDialog.razor` — `FilterPeriodsForGroup` must restrict Termly/Semester picks to sub-periods of the active year (`ParentPeriodId == _activeYearId`).
24. `SubPeriods.razor` — `Activate` row action must be disabled unless `row.Status == "Draft"` (mirror `SubPeriodsListDialog.razor`).
25. `PeriodForm.razor` — when `?parent=` points at a `None`-division year, surface a clear inline message with a Cancel-to-periods affordance instead of a stuck disabled form.

## Out of scope
- Settings module (`FlagKind` cleanup already done).
- Non-students modules (Assignments, Admin API, etc.) unless they reference `Students.Core.PeriodType`.
- UX polish follow-ups (FU-7) and seeded E2E (FU-6) remain backlog.

## Acceptance criteria
- `dotnet build SchoolCollab.sln -c Debug --nologo -v q` reports **0 errors**.
- `dotnet test tests/SchoolCollab.Students.Tests.Unit` passes (no failures).
- `dotnet test tests/SchoolCollab.Admin.Tests.Unit` passes (no failures).
- `PeriodType.cs` no longer exists; `grep -r "PeriodType" src/Students` returns only matches inside generated migration `*.Designer.cs` and `Migrations/StudentsDbContextModelSnapshot.cs` from pre-refactor migrations (those files are immutable historical artifacts).
- The period create/edit form shows exactly one selector (`Division`); selecting `Terms`/`Semesters` configures a top-level year that may host that sub-period kind. Sub-period creation is reachable from the year's sub-period surfaces.
- UI-tester P1 findings: none. P2 findings addressed (or deferred with user consent).

## Owned documents
- Plan: `documents/specs/plan-drop-periodtype.md` (this doc).
- Acceptance/review doc: `documents/specs/review-drop-periodtype.md`.
- UI-tester report: `documents/specs/ui-tester-drop-periodtype.md`.
