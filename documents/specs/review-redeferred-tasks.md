# Review — Re-deferred P2 Fold-in Tasks (Round)

> Reviewer report for the three re-deferred tasks scoped in
> `plan-redeferred-tasks.md` (Item 4 PeriodId editing, Item 5 string-flag audit
> value, backend duplicate-active guard). Code-read verification; shell/build
> deferred to the orchestrator acceptance pass.

## Correct (with evidence)

- **Task 1 — Period editing**
  - `UpdateTopicAssignmentPeriod` command + handler exist:
    - `src/Students/SchoolCollab.Students.Core/CQRS/TopicAssignments/Commands/UpdateTopicAssignmentPeriod/UpdateTopicAssignmentPeriod.cs`
    - `.../UpdateTopicAssignmentPeriodHandler.cs`
  - Period validation is factored into the shared `TopicAssignmentPeriodValidator`:
    - `src/Students/SchoolCollab.Students.Core/CQRS/TopicAssignments/TopicAssignmentPeriodValidator.cs`
  - Both create handlers now call the shared validator instead of duplicating rules:
    - `AssignGradeTopicHandler.cs` calls `ValidateGradePeriodAsync`
    - `AssignActivityGroupTopicHandler.cs` calls `ValidateGroupPeriodAsync`
  - Domain mutation path is the single `TopicAssignment.UpdatePeriod(Guid?)` method:
    - `src/Students/SchoolCollab.Students.Core/Domain/TopicAssignment.cs:132-140`
  - `PUT /students/topic-assignments/{id}/period` route exists and maps `KeyNotFoundException` → 404 and `TopicAssignmentPeriodException` → 422:
    - `src/Students/SchoolCollab.Students.Api/Endpoints/TopicAssignmentRoutes.cs:98-119`
  - Application-layer `TopicAssignmentDto` now carries `PeriodId`:
    - `src/Students/SchoolCollab.Students.Application/Services/StudentsApiClient.cs:160`
    - Core DTO: `src/Students/SchoolCollab.Students.Core/DTOs/TopicAssignmentDto.cs:16`
  - Grade detail page wires period display and an “Edit period” action:
    - `Detail.razor` builds `AssignmentByKeyKey` + `EditPeriodKey`
    - `GradeTopicsDialog.razor` renders `PeriodLabel` and an “Edit period” row action
    - `TopicAssignmentPeriodEditDialog.razor` calls `Api.UpdateTopicAssignmentPeriodAsync`
  - Unit tests cover all required cases:
    - `tests/SchoolCollab.Students.Tests.Unit/UpdateTopicAssignmentPeriodTests.cs`

- **Task 2 — String-flag audit values**
  - `FlagAuditEntry` has `PreviousValue`/`NewValue`:
    - `src/Settings/SchoolCollab.Settings.Core/Domain/FlagAuditEntry.cs:28-32`
  - EF configuration maps them with `HasMaxLength(200)`:
    - `src/Settings/SchoolCollab.Settings.Core/Data/Configurations/FlagAuditEntryConfiguration.cs:30-31`
  - Migration adds nullable columns:
    - `src/Settings/SchoolCollab.Settings.Core/Migrations/20260827190728_AddFlagAuditEntryValueColumns.cs`
  - `FeatureFlagAuditor.Record` accepts and passes the value fields:
    - `src/Settings/SchoolCollab.Settings.Core/Services/FeatureFlagAuditor.cs:13-28`
  - All call sites pass the values correctly:
    - `CreateFeatureFlagHandler` passes `newValue: flag.Value`
    - `UpsertTenantFlagOverrideHandler` captures `previousValue = existing?.Value` and passes `newValue: command.Value`
    - `DeleteTenantFlagOverrideHandler` passes `previousValue: existing.Value, newValue: null`
    - Bool-only handlers (`SetEnabled`, `Rename`, `Archive`, `Unarchive`, `Delete`, `Recover`) leave values null
  - DTOs and query expose the new fields:
    - `src/Settings/SchoolCollab.Settings.Core/DTOs/FeatureFlagDtos.cs:26-27`
    - `src/SchoolCollab.Admin.Shared/Services/ConfigFlagsApiClient.cs:25-26`
    - `ListAuditEntriesHandler` projects `e.PreviousValue, e.NewValue`
  - `ConfigFlagDetail.razor` renders a “Value” column only for string flags:
    - `src/Settings/SchoolCollab.Settings.Application/Components/Pages/ConfigFlagDetail.razor:135-139`
  - Tests updated/added:
    - `tests/SchoolCollab.Settings.Tests.Unit/FeatureFlagAuditorTests.cs:48-65`
    - `tests/SchoolCollab.Settings.Tests.Integration/ConfigApiTests.cs:90-130`

- **Task 3 — Duplicate-active guard**
  - New exception exists:
    - `src/Students/SchoolCollab.Students.Core/Domain/Exceptions/DuplicateTopicAssignmentException.cs`
  - `AssignActivityGroupTopicHandler` checks active `(group, topic, period)` duplicates **after** period validation:
    - `src/Students/SchoolCollab.Students.Core/CQRS/TopicAssignments/Commands/AssignActivityGroupTopic/AssignActivityGroupTopicHandler.cs:25-29`
  - Activity-group POST route maps the exception to 409:
    - `TopicAssignmentRoutes.cs:73-83`
  - Unit tests cover same-period, null-period, different-period, and 422-before-409 ordering:
    - `tests/SchoolCollab.Students.Tests.Unit/TopicAssignmentPeriodTests.cs:142-224`

## Finding: P1 — duplicate `using` directives will break the “clean build” acceptance

- **Location:** `src/Settings/SchoolCollab.Settings.Core/CQRS/FeatureFlags/Commands/FeatureFlagCommandHandlers.cs`
- **Evidence:** lines 2 and 10 both contained `using SchoolCollab.Core.CQRS;`; lines 3 and 11 both contained `using SchoolCollab.Core.Messaging;`.
- **Impact:** CS0105 warnings violate the plan’s “`dotnet build SchoolCollab.sln` — 0 errors, **no new warnings**” acceptance.
- **Smallest fix:** Remove the duplicate `using SchoolCollab.Core.CQRS;` and `using SchoolCollab.Core.Messaging;` lines at the bottom of the using block.
- **Status:** Fixed during the orchestrator acceptance pass (see `plan-redeferred-tasks.md` Acceptance).

## Finding: P2 — UI polish / deferred items (non-blocking)

1. `TopicAssignmentPeriodEditDialog.razor` does **not** filter the period list to active Term/Semester sub-periods; it shows every period whose parent is the active academic year. The server validation still enforces FR-57, so the worst case is a slightly confusing picker and a 422.
2. `GradeTopicsDialog.razor` renders the current period as the first 8 characters of the GUID (`pid.ToString()[..8]`). This satisfies the spec’s “short-form” allowance but is not user-friendly.
3. No bUnit coverage for the new dialog / ConfigFlagDetail audit grid; the plan explicitly allows deferring to manual checks.

## Per-criterion verdicts

| Task | Criterion | Verdict | Notes |
|---|---|---|---|
| 1 | Command/handler + shared validator | **Verified** | Files exist; create handlers refactored to shared validator |
| 1 | Endpoint 200/404/422 | **Verified** | Route implemented with correct catch blocks |
| 1 | `UpdatePeriod` only mutation path | **Verified** | `PeriodId` is private-set; only `UpdatePeriod` mutates it |
| 1 | Application `TopicAssignmentDto.PeriodId` | **Verified** | Present in both Core and application DTOs |
| 1 | Grade Topics card edit surface | **Verified** | `Detail.razor`, `GradeTopicsDialog.razor`, `TopicAssignmentPeriodEditDialog.razor` wired |
| 1 | Group-path UI deferred | **Verified** | No group-topics list UI; endpoint supports it |
| 2 | `FlagAuditEntry` columns + migration + config | **Verified** | Nullable columns added, configured with max length 200 |
| 2 | Auditor + call sites capture values | **Verified** | String-flag upsert/delete capture before/after; bool handlers leave null |
| 2 | DTO + query + Admin.Shared client | **Verified** | Fields present and projected |
| 2 | `ConfigFlagDetail` value column | **Verified** | Conditionally rendered for `FlagKindDto.String` |
| 2 | Integration test for value audit | **Verified** | `PUT_UpsertStringOverride_WritesValueAuditRow` covers None→Terms→Semesters |
| 3 | Duplicate guard in handler | **Verified** | Throws `DuplicateTopicAssignmentException` after period validation |
| 3 | Route maps to 409 | **Verified** | `catch (DuplicateTopicAssignmentException) → Results.Conflict` |
| 3 | Different period/topic still allowed | **Verified by code + test** | `AssignGroup_DifferentPeriod_AllowsSecond` passes |
| 3 | 422 before 409 | **Verified by test** | `AssignGroup_InvalidPeriod_Still422BeforeDuplicate` |
| 3 | Grade-path skip semantics preserved | **Verified** | No guard added to grade path |
| General | Clean build | **Verified by orchestrator** | 0 errors; P1 fixed; no new warnings |
| General | Tests pass | **Verified by orchestrator** | See Acceptance section |
| General | No staged files | **Verified by orchestrator** | `git diff --cached` empty |

## Recommendation

**OK with notes — approve once the parent runs the requested commands and the duplicate-`using` warning is removed.**
The orchestrator acceptance pass (below) ran the build/tests, fixed the P1 duplicate-`using` issue, and confirmed all in-scope tests pass. The residual risks documented in the plan (group-path UI, no Settings migration guard, no DB partial unique constraint, grade-path skip-vs-reject) remain as expected.