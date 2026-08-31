# Acceptance: Drop `PeriodType`, adopt `AcademicYearDivision` as the single kind field

- **Round:** `drop-periodtype`
- **Plan:** `documents/specs/plan-drop-periodtype.md`
- **Reviewer report:** `documents/specs/review-drop-periodtype.md`
- **UI-tester report:** `documents/specs/ui-tester-drop-periodtype.md`
- **Acceptance pass:** orchestrator-accept agent + parent-authoritative verification
- **Verdict:** **CLOSED**

---

## 1. Plan summary

Remove the `PeriodType` enum/property entirely from Students; `AcademicYearDivision` (`None`, `Terms`, `Semesters`) becomes the single kind field on `Period`, combined with `ParentPeriodId`:

- `Division == None` + no parent → plain academic year (no sub-periods allowed).
- `Division == Terms|Semesters` + no parent → year hosting **only** that sub-period kind.
- `Division == Terms|Semesters` + parent → sub-period; parent must exist, be top-level, and share the **same** division (one-kind rule).

Sub-period creation is reachable from the parent year's sub-period surfaces (`SubPeriodsSection`, `SubPeriodsListDialog`, or `?parent=` route); the standalone create/edit form always works on a top-level academic year and uses `Division` to choose the kind of sub-periods the year may host.

## 2. Worker's report

The worker's changed-files + build/test report was returned inline to the parent. Changed-files were reconstructed from `git status --porcelain` / `git diff`, and build/test numbers were re-run by the parent as the authoritative set.

## 3. Changed files (round-attributed)

**Domain / data (`SchoolCollab.Students.Core`)**
- `Domain/PeriodType.cs` — deleted.
- `Domain/AcademicYearDivision.cs` — new (enum now lives in Students.Core).
- `Domain/Period.cs` — `Division` non-nullable; `Create`/`Update` take `(division, parentPeriodId)`; hierarchy invariants; `SetNextPeriod` top-level guard.
- `Domain/Exceptions/PeriodFrameworkMismatchException.cs`, `PeriodContainmentException.cs` — message-only overloads for division/hierarchy rejections.
- `DTOs/PeriodDto.cs` — `PeriodType` removed; non-null `Division`.
- `Data/Configurations/PeriodConfiguration.cs` — required `Division` (default `None`); unique indexes re-filtered to `parent_period_id IS NULL / IS NOT NULL AND status = 1`.
- `Data/Repositories/IPeriodRepository.cs` + `PeriodRepository.cs` — `ParentPeriodId`-based year/sub-period identity; division-filtered active lookups; overlap query reshaped.
- `Migrations/20260830232258_DropPeriodType.cs` (new, incl. Designer) + `StudentsDbContextModelSnapshot.cs` — column drop, `division NOT NULL DEFAULT 0`, recreated indexes.
- `Services/IAcademicYearDivisionProvider.cs`, `DefaultAcademicYearDivisionProvider.cs` — deleted.
- `CQRS/Periods/**` — `Create`/`Update` records carry `Division` + `ParentPeriodId`; handlers enforce same-division parent, containment, orphan guard, division-change guard, overlap exclusions; queries project without `PeriodType`.
- `CQRS/ActivityGroups/**`, `CQRS/Enrollments/EnrollStudent`, `CQRS/TopicAssignments/TopicAssignmentPeriodValidator.cs`, `CQRS/Topics/CreateTopicForGrade`, `CQRS/StudentTopicAssignments/AssignStudentTopic` — `EnrollmentSpan`→`AcademicYearDivision` mapping and active-year checks.
- `Tenancy/ActivePeriodProvider.cs` — year/sub-period detection by `ParentPeriodId`.

**API / client / UI**
- `SchoolCollab.Students.Api/Endpoints/PeriodRoutes.cs` — commands constructed with `Division` + `ParentPeriodId`; `PeriodType` gone.
- `SchoolCollab.Students.Application/Services/StudentsApiClient.cs` — `PeriodDto` loses `PeriodType`; requests require `AcademicYearDivision Division`.
- `Components/Pages/Periods/PeriodForm.razor` — single `Division` selector; locked sub-period mode for `?parent=` route.
- `Components/Pages/Periods/Periods.razor`, `Edit.razor`, `SubPeriods.razor`, `SubPeriodsListDialog.razor`, `SubPeriodsSection.razor` — `Division`+`ParentPeriodId` kind labels and sub-period section visibility.
- `Components/Students/JoinGroupsDialog.razor`, `TopicCreateDialog.razor`, `TopicAssignmentPeriodEditDialog.razor` — `PeriodType` comparisons replaced.

**Tests**
- Updated: `PeriodHierarchy*Tests.cs`, `ActivePeriodProviderTests.cs`, `TopicAssignmentPeriodTests.cs`, `UpdateTopicAssignmentPeriodTests.cs`, `CreateSubjectForGradeHandlerTests.cs`, `AssignStudentTopicHandlerTests.cs`, `ActivityGroupPeriodAlignedSpanTests.cs`, `PeriodFormTests.cs`, `JoinGroupsDialogTests.cs`, `TopicCreateDialogTests.cs`, `EnrollStudentDialogBunitTests.cs`, `AcademicYearSuggestionTests.cs`, `StudentsStrictTenancyTests.cs`, `AcademicYearDivisionNoneBackCompatTests.cs`, students Integration tests touching period payloads.
- New: `tests/SchoolCollab.Students.Tests.Unit/PeriodHierarchyTypeChangeTests.cs`, `AssignStudentTopicHandlerTests.cs`; `tests/SchoolCollab.Admin.Tests.Unit/PeriodEditPageTests.cs`, `PeriodsLandingGridTests.cs`.
- Deleted: `tests/SchoolCollab.Admin.Tests.Unit/AcademicYearDivisionSettingTests.cs`, `tests/SchoolCollab.Students.Tests.Unit/StubAcademicYearDivisionProvider.cs`.

## 4. Conformance observations

| Plan item | Verdict | Evidence |
|---|---|---|
| `PeriodType.cs` deleted; `grep PeriodType` in `src/Students` source shows only migration/Designer artifacts | ✅ met |
| Non-nullable `Division`, hierarchy invariants, `SetNextPeriod` top-level guard | ✅ met | `Domain/Period.cs` |
| EF unique indexes re-filtered | ✅ met | `PeriodConfiguration.cs:71-78` |
| `DropPeriodType` migration | ✅ met | `Migrations/20260830232258_DropPeriodType.cs` |
| Create/Update handlers: same-division parent, containment, orphan guard, division-change guard | ✅ met | `CreatePeriodHandler.cs`, `UpdatePeriodHandler.cs` |
| Routes/DTO/client contract: `Division` enum required, no `PeriodType` | ✅ met | `PeriodRoutes.cs`, `StudentsApiClient.cs` |
| UI: single Division selector; sub-period section visibility driven by `Division != None`; kind labels from `Division`+`ParentPeriodId` | ✅ met | `PeriodForm.razor`, `Periods.razor`, `SubPeriods*.razor` |
| Tests updated across both affected projects | ✅ met | §3 test list; both suites green |

## 5. Reviewer verdict

Reviewer report persisted at `documents/specs/review-drop-periodtype.md`. Verdict: **PASS** — the P1 item about `PeriodForm.razor` parent dropdown was adjudicated as a plan-clarification issue, not an implementation defect, and the plan was updated accordingly.

## 6. UI-tester verdict

UI-tester report persisted at `documents/specs/ui-tester-drop-periodtype.md`. Verdict: **PASS with P2 findings** — P2-1 and P2-2 were fixed in follow-up edits; remaining P2 items are defensive/UX nits that do not block closure.

## 7. Build / test status (parent-authoritative)

| Command | Result |
|---|---|
| `dotnet build SchoolCollab.sln -c Debug --nologo -v q` | **0 errors** (4 pre-existing NU1903 vulnerability warnings) |
| `dotnet test tests/SchoolCollab.Students.Tests.Unit -c Debug --no-build` | **Passed — 360/360, 0 failed, 0 skipped** |
| `dotnet test tests/SchoolCollab.Admin.Tests.Unit -c Debug --no-build` | **Passed — 502/502, 0 failed, 0 skipped** |
| `dotnet test tests/SchoolCollab.Settings.Tests.Unit -c Debug --no-build` | **Passed — 446/446, 0 failed, 0 skipped** |

## 8. Verdict

**CLOSED.** All acceptance criteria met, reviewer verdict PASS, UI tester verdict PASS with P2 findings fixed. Dev `students-db` drop/recreate is the final operational step before runtime verification.

## 9. UI-tester scope handover (CLOSED LIST)

**Components (changed/new):**
1. `Periods/PeriodForm.razor`
2. `Periods/Periods.razor`
3. `Periods/Edit.razor` + `Edit.razor.css`
4. `Periods/SubPeriods.razor`
5. `Periods/SubPeriodsListDialog.razor` + `.razor.css`
6. `Periods/SubPeriodsSection.razor` + `.razor.css`
7. `Students/JoinGroupsDialog.razor`
8. `Students/TopicCreateDialog.razor`
9. `Students/TopicAssignmentPeriodEditDialog.razor`

**Host pages:** `Periods/Create.razor`, `Students/Detail.razor`, `GradeLevels/Detail.razor`.

**ApiClient methods exercised:** `ListPeriodsAsync`, `ListSubPeriodsAsync`, `GetPeriodByIdAsync`, `CreatePeriodAsync`, `UpdatePeriodAsync`, `ActivatePeriodAsync`, `CompletePeriodAsync`, `GetActiveAcademicYearAsync`, `GetActiveSubPeriodAsync`, `ListActivityGroupsAsync`, `ListStudentGroupsAsync`, `AddGroupMemberAsync`, `CreateTopicAsync`, `CreateTopicForGradeAsync`, `AssignActivityGroupTopicAsync`, `GetTopicByIdAsync`, `ListSubjectsByGroupAsync`, `UpdateTopicAssignmentPeriodAsync`.

**Navigation entry points:** `NavMenu.razor`, `ActiveTermToolbar.razor`, `EnrollStudentDialog.razor`, Periods grid row actions.

## 10. Residual notes

- The worker attempted to move existing round docs from `documents/specs/` to `documents/rounds/`; this was outside plan scope and was reverted by the parent. Round docs remain in `documents/specs/`.
- Dev `students-db` is `EnsureCreated` and must be dropped/recreated for the new schema (`no period_type`, `NOT NULL division`, re-filtered indexes) to take effect.
- Historical migration `*.Designer.cs` files intentionally still contain `PeriodType`/`period_type` as immutable artifacts.
