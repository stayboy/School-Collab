# Review: Drop `PeriodType`, adopt `AcademicYearDivision`

- **Round:** `drop-periodtype`
- **Plan:** `documents/rounds/plan-drop-periodtype.md`
- **Reviewer agent:** `reviewer` (`kimi-k2.7-code:cloud`) — ran read-only; findings persisted by parent.
- **Date:** 2026-08-30

## Verdict
**PASS with P2 findings.** The P1 item raised by the reviewer was adjudicated as a plan-clarification issue, not an implementation defect (see §P1). The implementation matches the clarified plan and the user's stated intent. UI-tester P2 findings are tracked separately in `documents/rounds/ui-tester-drop-periodtype.md` and are addressed in the acceptance pass.

## Scope covered by the worker
The diff removes the `PeriodType` enum, makes `AcademicYearDivision` the single kind field on `Period`, updates the EF model/migration, CQRS handlers, API/client DTOs, most UI labels, and the affected unit tests. Historical migration `.Designer.cs` files still contain the word `PeriodType`, which is expected and immutable.

## Correct (with evidence)
- `src/Students/SchoolCollab.Students.Core/Domain/Period.cs` — `PeriodType` property removed; `Division` is non-nullable `AcademicYearDivision`; `Create`/`Update` signatures match the plan; `ValidateHierarchy` rejects `None` division for sub-periods; `SetNextPeriod` is gated to top-level years.
- `src/Students/SchoolCollab.Students.Core/DTOs/PeriodDto.cs` — no `PeriodType`; `Division` string is non-null.
- `src/Students/SchoolCollab.Students.Core/Data/Configurations/PeriodConfiguration.cs` — `Division` required with default `None`; unique indexes use `parent_period_id IS NULL / IS NOT NULL AND status = 1`.
- `src/Students/SchoolCollab.Students.Core/Migrations/20260830232258_DropPeriodType.cs` — drops `period_type`, alters `division` to `NOT NULL DEFAULT 0`, recreates indexes exactly as planned.
- `src/Students/SchoolCollab.Students.Core/Data/Repositories/PeriodRepository.cs` and `IPeriodRepository.cs` — `PeriodType?` removed; `GetActiveAcademicYearAsync`, `GetActiveSubPeriodsAsync`, `GetNonCompletedSubPeriodCountAsync`, `GetSubPeriodsAsync`, `GetCurrentPeriodAsync` all use `ParentPeriodId`/`Division`.
- CQRS commands/handlers (`CreatePeriod`, `UpdatePeriod`, `ActivatePeriod`, `CompletePeriod`, `ArchivePeriod`) — hierarchy, containment, division-change guard, cascade, and auto-activate logic match the plan.
- `src/Students/SchoolCollab.Students.Core/Tenancy/ActivePeriodProvider.cs` — returns `ActivePeriod` using `Division`/`ParentPeriodId`.
- `AddMembershipHandler.cs` and `RolloverActivityGroupHandler.cs` map `EnrollmentSpan` to `AcademicYearDivision` correctly.
- `EnrollStudentHandler.cs` rejects grade enrollment when the active period is a sub-period.
- `TopicAssignmentPeriodValidator.cs` and `CreateTopicForGradeHandler.cs` validate division and active-year membership.
- `AssignStudentTopicHandler.cs` uses `GetActiveAcademicYearAsync`.
- `PeriodRoutes.cs` and `StudentsApiClient.cs` remove `PeriodType` and use required enum `Division`.
- `Periods.razor`, `Edit.razor`, `SubPeriods.razor`, `SubPeriodsListDialog.razor`, `SubPeriodsSection.razor`, `JoinGroupsDialog.razor`, `TopicAssignmentPeriodEditDialog.razor`, `TopicCreateDialog.razor` no longer compare `PeriodType`; kind labels are derived from `Division` + `ParentPeriodId`.
- `grep "PeriodType" src/Students` returns only historical migration designer files.
- No `PeriodType.cs` file remains.

## P1 finding — `PeriodForm.razor` parent dropdown (ADjudicated, not a defect)

**Original concern:** `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodForm.razor` (lines 42–60 and 220–235) only renders the parent dropdown when `_isSubPeriod` is true (i.e., `?parent=` route or edit mode). A plain create with `Division != "None"` therefore creates a top-level year with that division rather than requiring a parent selection.

**Adjudication:** The user's stated intent is that Division is the single kind field and sub-periods are added from the year's sub-period surfaces. A top-level year with `Division=Terms`/`Semesters` is a valid container year; the form's behavior is therefore correct. The original plan's acceptance criterion was overly prescriptive and has been updated in `documents/rounds/plan-drop-periodtype.md` §18 to match this intent. This finding is **downgraded to P2 / plan-clarification** and is not a rework blocker.

## P2 findings
1. **Scope-adjacent Settings changes** — the working tree also contains Settings/FlagKind cleanup changes. The reviewer could not confirm whether these belong to a separate in-flight round or were widened by the worker. The parent must attribute them before final CLOSE. (Attribution verified separately by parent: these are pre-existing uncommitted work from earlier rounds, not part of this refactor.)
2. **Stale doc comment** — `tests/SchoolCollab.Admin.Tests.Unit/PeriodEditPageTests.cs:19` still mentions "PeriodType" in a comment. Cosmetic.

## Best-coding-practices check
- **No destructive overwrites inside the round scope:** the worker modified only the files needed for the `PeriodType` removal and related consumers. However, the worker also moved/renamed many existing round docs from the old specs folder to `documents/rounds/` — this was **outside the plan's scope**. The parent reverted those moves and restored the docs to their original specs location.
- **Repo skills:** the UI changes follow existing repo patterns; the single-selector form is simpler and does not violate the dialog-shell or CSS-isolation skills.
- **Readability:** the refactor reduces the conceptual surface from two fields (`PeriodType` + `Division`) to one (`Division`), which is a readability/maintainability improvement.

## Build / test status (parent-authoritative)
- `dotnet build SchoolCollab.sln -c Debug --nologo -v q` → **0 errors** (4 pre-existing NU1903 warnings).
- `dotnet test tests/SchoolCollab.Students.Tests.Unit -c Debug --no-build` → **360/0**.
- `dotnet test tests/SchoolCollab.Admin.Tests.Unit -c Debug --no-build` → **502/0**.

## Final verdict
**PASS — round may close after UI-tester P2 fixes are applied and dev DB is reset.**
