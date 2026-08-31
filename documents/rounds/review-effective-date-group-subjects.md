# Review — Effective-date filtering for the SelectedGroups subject picker (FR-58 completion)

**Reviewer:** reviewer subagent (round: effective-date group-subjects fix)
**Plan:** `documents/specs/plan-effective-date-group-subjects.md`
**Date:** 2026-08-27

## Correct (verified against source)

- **AC1 — Client signature parity.** `StudentsApiClient.cs:1084` now declares
  `ListSubjectsByGroupAsync(Guid activityGroupId, DateOnly? effectiveDate, CancellationToken ct = default)`, matching the parameter name, type, ordering and defaulting of `ListSubjectsByGradeEffectiveAsync`.

- **AC2 — Query string parity.** `StudentsApiClient.cs:1086-1088` appends `?effectiveDate={effectiveDate:yyyy-MM-dd}` only when `effectiveDate.HasValue`; otherwise it uses the base `/students/subjects/by-group/{activityGroupId}` path, mirroring the grade method exactly.

- **AC3 — Backend endpoint/handler unchanged.** Inspected:
  - `TopicRoutes.cs:71-93` already binds `DateOnly? effectiveDate` and forwards it.
  - `ListTopicsByGroup.cs` record already has `DateOnly? EffectiveDate = null`.
  - `ListTopicsByGroupHandler.cs:20` already defaults to `DateTime.UtcNow` and filters `[StartDate, EndDate]`.
  No structural edits are present; the backend contract was already complete.

- **AC4 — Load site passes due date.** `Create.razor:624-627` computes `effectiveDate` from `_model.DueDate ?? DateTime.UtcNow` **once** outside the `foreach` over `_selectedGroupIds` and passes it to `ListSubjectsByGroupAsync(groupId, effectiveDate, ct)` at line 636.

- **AC5 — Due-date re-filter covers groups.** `Create.razor:592-600` adds a group branch in `OnDueDateChangedAsync` that resets `_selectedSubject` and `_groupSubjectOptions`, then awaits `LoadGroupSubjectsAsync()` when `_selectedTargetAudience == TargetAudienceTypeDto.SelectedGroups && _selectedGroupIds.Count > 0`. The existing grade branch at lines 563-590 is preserved unchanged.

- **Chosen-subject validation is effective-date-aware.** `Create.razor:692-699` validates the selected subject against `_groupSubjects`, which is populated by the effective-date-filtered `LoadGroupSubjectsAsync`. When the due date changes, `_groupSubjects` is reloaded and validation follows the new set.

- **Call-site compatibility.** A second caller exists at `Subjects.razor:249` (`Api.ListSubjectsByGroupAsync(groupId, null, token)`). The new signature is source-compatible because the `null` argument maps to `DateOnly? effectiveDate` before the `CancellationToken`. No caller requires updating.

- **Documentation updated.** `documents/specs/ui-implementation-backlog.md` Sprint 5.3 now records the effective-date gap as closed, consistent with the implementation.

## Not verified (runtime/tooling limitation — resolved by orchestrator acceptance pass)

- **AC7 — Build green.** Orchestrator ran `dotnet build SchoolCollab.sln -c Debug --nologo -v q` → 0 errors.
- **AC8 — Tests green.** Orchestrator ran the three unit-test projects' built EXEs directly → Assignments 102/0, Students 301/0, Admin 453/0 (all pass).
  - Note: `dotnet test ... --nologo` exits with code 5 (prints help, 0 tests run) on this machine because the installed Microsoft.Testing.Platform build rejects the `--nologo` flag forwarded by `dotnet test`. Running each test project's built `.exe` directly (no flags) executes the tests correctly. This is an environment/tooling mismatch, not a code defect.

## Finding: P2 / report-only

- `plan-effective-date-group-subjects.md §3.4` claims `ListSubjectsByGroupAsync` is referenced only in `Create.razor`. In fact `Subjects.razor:249` also calls it, passing `null` for `effectiveDate`. This is **not a defect**: the call compiles and behaves correctly (returns currently-effective topics). It is only a minor inaccuracy in the plan's grep claim.

## New issues

- **No new issues found.** The implementation matches the plan's intent and FR-58's effective-date requirement for `SelectedGroups`. Period-aligned (`PeriodId`) validation remains explicitly out of scope per the plan.

## Recommendation

**OK to close the round, subject to a parent-run build + unit-test confirmation.** All code changes are present, correct, and minimal. The only remaining gate was the runtime verification (build + tests), now confirmed green by the orchestrator.