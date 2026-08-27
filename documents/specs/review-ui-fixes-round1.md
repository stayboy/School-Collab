# UI Fixes Review — Round 1

**Review scope:** verify the six UI fixes implemented after `review-ui-sprints-1-5.md` against the authoritative specs.

**Reviewer:** `ollama/kimi-k2.7-code:cloud` (file-based source inspection).  
**Parent verification:** `dotnet build` + `dotnet test` run from the parent session.

---

## Build / test status (parent-run)

| Command | Result |
|---------|--------|
| `dotnet build SchoolCollab.sln -c Debug --nologo -v q` | ✅ **0 errors** |
| `dotnet test tests/SchoolCollab.Assignments.Tests.Unit` | ✅ **102 passed, 0 failed** |
| `dotnet test tests/SchoolCollab.Students.Tests.Unit` | ✅ **301 passed, 0 failed** |
| `dotnet test tests/SchoolCollab.Admin.Tests.Unit` | ✅ **453 passed, 0 failed** |

**Total:** 856 passed, 0 failed.

---

## Fix-by-fix verification

| # | Fix | Status | Evidence |
|---|-----|--------|----------|
| 1 | Duplicate-assignment bug fixed in `TopicCreateDialog.razor` grade path | ✅ Verified | `TopicCreateDialog.razor` calls `Api.CreateTopicForGradeAsync(... periodId)` **only**; no second `AssignGradeTopicAsync` call in the grade branch. `CreateTopicForGradeHandler.cs` creates a single `GradeTopicAssignment` with `periodId: command.PeriodId` and validates it. |
| 2 | `Subjects.razor` now uses `TopicCreateDialog` | ✅ Verified | `Subjects.razor` opens `TopicCreateDialog` with the current owner context (`OwnerType`, `GradeLevelId` or `ActivityGroupId`). The legacy `SubjectCreateDialog` is no longer referenced. |
| 3 | Period filter in `Subjects.razor` works end-to-end | ✅ Verified | `Subjects.razor` passes `PeriodId` to `Api.ListSubjectsByGradeAsync`. `StudentsApiClient.cs` builds `?periodId=...`. `TopicRoutes.cs` passes `periodId` to `ListTopicsByGrade`. `ListTopicsByGradeHandler.cs` filters `a.PeriodId == query.PeriodId` when supplied. |
| 4 | `DateRange` next-window inputs present and wired to backend | ✅ Verified | `ActivityGroupCreateDialog.razor` and `ActivityGroupEditDialog.razor` render optional "Next window" date pickers for `DateRange` spans and validate `NextEnrollmentStartDate >= EnrollmentEndDate`. Both dialogs call `Api.SetActivityGroupNextWindowAsync`, mapped to `PUT /activity-groups/{id}/next-window`. |
| 5 | Assignment `Create.razor` subject picker is group-aware for `SelectedGroups` | ✅ Verified | `Create.razor` loads the union of topics from `ListSubjectsByGroupAsync` for each selected group and validates the chosen subject is present in that union before creating the assignment. |
| 6 | `TopicCreateDialog` period dropdown filtered by owner type | ✅ Verified | `TopicCreateDialog.razor` `_filteredPeriods` filters to active-year `AcademicYear`/`Term`/`Semester` for `GradeLevel` and to the group's `Span`-matching period type for `ActivityGroup`; `OpenEnded`/`DateRange` groups show no period options. |

---

## Findings (new issues / residual risks)

### P1 — `SelectedGroups` subject picker ignores effective date / due date (FR-58 partial gap)

`Create.razor` reloads grade subjects via `ListSubjectsByGradeEffectiveAsync(..., effectiveDate)` when the due date changes, but the `SelectedGroups` path always calls `ListSubjectsByGroupAsync(groupId)` with no effective-date parameter. The endpoint and handler support `effectiveDate`, but the client method `ListSubjectsByGroupAsync` does not expose it.

**Impact:** a group topic assignment that is not yet effective could be offered, or a future-effective one could be missed.

**Location:**
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Create.razor:613–637`
- `src/Students/SchoolCollab.Students.Application/Services/StudentsApiClient.cs:1079`

**Recommendation:** extend `ListSubjectsByGroupAsync` to accept an optional `DateOnly? effectiveDate` and pass the assignment due date from `Create.razor`, mirroring the grade path.

### P2 — `ExistingTopicCodedValueIds` not populated when opening `TopicCreateDialog` from `Subjects.razor`

`GradeLevels/Detail.razor` seeds `ExistingTopicCodedValueIds` so the dialog disables duplicate-coded-value picks, but `Subjects.razor` `OpenCreateDialogAsync` leaves it as an empty `HashSet`. The backend still rejects duplicates via find-or-create / `DuplicateTopicCodeException`, so no data corruption occurs, but the UX warning is silently disabled from the Topics landing.

**Location:** `src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Subjects/Subjects.razor:313–337`.

**Recommendation:** populate `ExistingTopicCodedValueIds` from the currently loaded `_items` in `Subjects.razor`.

### P2 — `CreateTopicForGradeHandler` silently ignores `PeriodId` when an active year-spanning assignment already exists

The idempotency guard in `CreateTopicForGradeHandler.cs` only skips creation when an *active* assignment for the same topic exists. If a grade already has topic X assigned with `PeriodId = null` (year-spanning), a request to create topic X scoped to Term 1 will reuse the topic and skip creating the scoped assignment, so the requested `PeriodId` is dropped without feedback.

**Impact:** acceptable for a create-only flow, but should be noted. Full `PeriodId` editing on existing assignments is already listed as out of scope.

### P2 — `TopicCreateDialog` activity-group path does not check for an existing assignment before assigning

Unlike the grade path, the group branch calls `CreateTopicAsync` + `AssignActivityGroupTopicAsync` without first checking whether that topic/group combination already has an active assignment. Repeated submissions could create overlapping group topic assignments. The backend `AssignActivityGroupTopic` handler may or may not guard this; not verified in this review.

**Recommendation:** add an idempotency check on the client side or verify the backend handler rejects duplicate active assignments.

---

## Recommendation

**Fix the P1 gap before Sprint 6.** The group-aware assignment subject picker is functionally correct for the simple path, but the missing effective-date parameter on `ListSubjectsByGroupAsync` leaves FR-58 half-implemented for `SelectedGroups`. Extend the client method to accept an optional `effectiveDate` and pass the assignment due date from `Create.razor`, mirroring the grade path.

The two P2 findings can be deferred to Sprint 6 polish or addressed alongside the P1 fix.

Once P1 is resolved (or explicitly accepted as a Sprint 6 task), proceeding to Sprint 6 bUnit/Playwright verification is appropriate. The core duplicate-assignment bug, topic-create wiring, period filter, next-window UI, and group subject loading are all in place and build/test green.

---

## Files changed in the fix round (git diff --stat highlights)

Notable UI/client files touched:
- `src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Subjects/Subjects.razor`
- `src/Students/SchoolCollab.Students.Application/Components/Students/TopicCreateDialog.razor`
- `src/Students/SchoolCollab.Students.Application/Components/Students/ActivityGroupCreateDialog.razor`
- `src/Students/SchoolCollab.Students.Application/Components/Students/ActivityGroupEditDialog.razor`
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Create.razor`
- `src/Students/SchoolCollab.Students.Application/Services/StudentsApiClient.cs`
- `src/Assignments/SchoolCollab.Assignments.Application/Services/AssignmentsApiClient.cs`
- `src/Students/SchoolCollab.Students.Api/Endpoints/TopicRoutes.cs`
- `src/Students/SchoolCollab.Students.Core/CQRS/Topics/Commands/CreateTopicForGrade/CreateTopicForGradeHandler.cs`
- `src/Students/SchoolCollab.Students.Core/CQRS/Topics/Queries/ListTopicsByGrade/ListTopicsByGradeHandler.cs`
- `src/Students/SchoolCollab.Students.Core/CQRS/ActivityGroups/Commands/SetActivityGroupNextWindow/*`

(The full diff covers 131 files and includes the prior backend + UI backlog work; see `git diff --stat` for the complete list.)
