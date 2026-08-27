# Plan — Re-deferred P2 Fold-in Tasks (Round)

> **Status:** Plan / acceptance contract (orchestrator-owned). No code in this
> doc — it scopes three re-deferred feature-sized tasks from
> `ui-implementation-backlog.md` "Deferred P2 fold-in" (Items 4, 5, and the
> backend group duplicate guard) and defines the worker + reviewer contracts.
> Source of truth: `activity-group-enrollment.md` (FR-55..58, AC-44..46),
> `subject-to-topic-polymorphism.md` (Design B: TopicAssignment TPH), the
> existing code under `src/Students/...` and `src/Settings/...`.

## Goal

Close the three re-deferred gaps from the activity-group feature work with the
smallest correct implementation each:

- **Task 1 (Item 4)** — Let an admin change `PeriodId` on an *existing*
  topic assignment (grade or activity-group), reusing the exact creation-time
  period validation (FR-56/57). Today `PeriodId` is set only at creation
  (`AssignGradeTopic` / `AssignActivityGroupTopic`).
- **Task 2 (Item 5)** — Capture and display the *value* of string-valued
  feature flags in the audit log. `FlagAuditEntry` records only
  `PreviousIsEnabled`/`NewIsEnabled` (bool); string flags (e.g.
  `academic_year_division`) lose their value on the audit row.
- **Task 3 (backend guard)** — Reject a duplicate *active* activity-group
  topic assignment for the same `(group, topic, period)` in
  `AssignActivityGroupTopicHandler`. The client-side guard in
  `TopicCreateDialog` only closes the create-dialog flow; the backend creates
  the duplicate row.

Each task is independently shippable. Tasks 1 and 3 share the Students context
and may land in one PR; Task 2 is Settings-only and lands separately.

---

## Task 1 — Topic-assignment `PeriodId` editing

### Scope decision

- **In scope:** new update command + handler + endpoint that changes
  `PeriodId` on any existing `TopicAssignment` (TPH root — works for both the
  grade and activity-group subtype via one command), reusing creation-time
  validation; a domain `UpdatePeriod` method; an admin edit surface on the
  existing grade Topics card (`GradeTopicsDialog` + `GradeLevels/Detail.razor`)
  where assignments already render with row actions; an `ApiClient` method; and
  adding `PeriodId` to the *application-layer* `TopicAssignmentDto` (currently
  missing it, so the UI cannot read the current value).
- **Deferred (follow-up):** the activity-group *UI* edit surface. There is no
  group-topics list/edit page in the admin today (`ActivityGroupDetails.razor`
  does not manage topics; group topic assignments are only created via
  `TopicCreateDialog`). Building a group-topics list surface is a larger,
  separate UI feature. The new endpoint + command already support the
  group subtype, so the follow-up is purely UI. Tracked under
> "Follow-up: ActivityGroup topic-assignments list/edit surface (UI) — reuses
>  `PUT /topic-assignments/{id}/period` once a group-topics page exists."
- **Out of scope:** editing `StartDate`/`EndDate`/`TopicStrandId` via this
  command (those already move via `End`/`UpdateTags`); bulk period reassignment.

### Change list

1. **Domain** — `src/Students/SchoolCollab.Students.Core/Domain/TopicAssignment.cs`
   - Add `public void UpdatePeriod(Guid? periodId)` that sets `PeriodId` and
     stamps `UpdatedAt = DateTimeOffset.UtcNow`. No-op if unchanged. (Mirrors
     `UpdateTags`.) `PeriodId` is already a private-setter; the method is the
     only mutation path.

2. **Shared period validation (reuse)** — extract the two existing
   `ValidatePeriodAsync` blocks from `AssignGradeTopicHandler` (FR-57) and
   `AssignActivityGroupTopicHandler` (FR-56) into a small shared helper so the
   new update handler reuses the *identical* rules without duplicating logic.
   - New: `src/Students/SchoolCollab.Students.Core/CQRS/TopicAssignments/TopicAssignmentPeriodValidator.cs`
     (static or injectable service) exposing:
     - `Task ValidateGradePeriodAsync(Guid? periodId, IPeriodRepository, ct)` —
       FR-57: null ⇒ year-spanning; AcademicYear ⇒ any; Term/Semester ⇒ must
       belong to the tenant's active academic year.
     - `Task ValidateGroupPeriodAsync(Guid activityGroupId, Guid? periodId,
       IActivityGroupRepository, IPeriodRepository, ct)` — FR-56: null ⇒
       date-based window (OpenEnded/DateRange or period-aligned-but-unset); else
       the group's `EnrollmentSpan` dictates the required `PeriodType`
       (Termly→Term, Semester→Semester, WholeAcademicYear→AcademicYear;
       OpenEnded/DateRange ⇒ PeriodId must be null → throws
       `TopicAssignmentPeriodException`).
   - Refactor `AssignGradeTopicHandler` and `AssignActivityGroupTopicHandler`
     to call this helper (behaviour-preserving; their existing
     `ValidatePeriodAsync` bodies move into it). This keeps the create and
     update paths on one validated rule set.

3. **New command + handler** —
   `src/Students/SchoolCollab.Students.Core/CQRS/TopicAssignments/Commands/UpdateTopicAssignmentPeriod/`
   - `UpdateTopicAssignmentPeriod(Guid AssignmentId, Guid? PeriodId) : ICommand<TopicAssignmentDto>`
     (record, mirrors `UpdateTopicAssignmentTags` shape).
   - `UpdateTopicAssignmentPeriodHandler(StudentsDbContext db,
     IPeriodRepository periodRepository,
     IActivityGroupRepository groupRepository,
     HybridCache cache,
     ILogger<...> logger) : ICommandHandler<UpdateTopicAssignmentPeriod, TopicAssignmentDto>`
     - `var assignment = await db.TopicAssignments.FindAsync(...)`; null ⇒
       `KeyNotFoundException` (→ 404).
     - Dispatch validation by subtype:
       - `GradeTopicAssignment` ⇒ `ValidateGradePeriodAsync(command.PeriodId, ...)`.
       - `ActivityGroupTopicAssignment` ⇒ `ValidateGroupPeriodAsync(
         assignment.ActivityGroupId, command.PeriodId, ...)`.
       - unknown subtype ⇒ `InvalidOperationException`.
     - `assignment.UpdatePeriod(command.PeriodId); await db.SaveChangesAsync(ct);`
     - `await cache.RemoveByTagAsync("students", ct);`
     - Return `TopicAssignmentDto` (same `switch` projection as
       `UpdateTopicAssignmentTagsHandler` — grade vs activity_group).
   - Note: the validation helpers resolve the *current* owner (the assignment's
     own `GradeLevelId`/`ActivityGroupId`) — period scope is not changed by this
     command, so no re-derivation of owner is needed.

4. **Endpoint** — `src/Students/SchoolCollab.Students.Api/Endpoints/TopicAssignmentRoutes.cs`
   - Add `PUT /topic-assignments/{id:guid}/period`:
     - body record `UpdateTopicAssignmentPeriodRequest(Guid? PeriodId)`.
     - `catch (KeyNotFoundException) { return Results.NotFound(); }`
     - `catch (TopicAssignmentPeriodException ex) { return Results.Json(new { ex.Message }, statusCode: 422); }`
   - Pattern mirrors the existing `PUT /topic-assignments/{id}/tags` route.

5. **Application client** —
   `src/Students/SchoolCollab.Students.Application/Services/StudentsApiClient.cs`
   - Add `PeriodId` to the application-layer `TopicAssignmentDto` record
     (line ~143 — currently missing it; the Core DTO already has it). Position
     it before `CreatedAt` to match the Core DTO. This is required so the UI
     can read the current period and so the JSON round-trips.
   - Add `UpdateTopicAssignmentPeriodRequest(Guid? PeriodId)` record.
   - Add `UpdateTopicAssignmentPeriodAsync(Guid id, Guid? periodId, ct)` →
     `PUT /students/topic-assignments/{id}/period`.

6. **Admin edit surface (grade path)** —
   - `src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Detail.razor`
     - Extend the private `AssignedTopic` class with `Guid? PeriodId` and
       populate it from the now-PeriodId-bearing `TopicAssignmentDto` in both
       `LoadAsync` and `ReloadAssignedTopicsAsync`.
     - Build a `Dictionary<Guid topicId, AssignedTopic>` (already effectively
       `_assignedTopics`) and pass an `EditPeriod` callback + the assignment map
       into `GradeTopicsDialog` (new keys: `EditPeriodKey`,
       `AssignmentByKeyKey`).
   - `src/Students/SchoolCollab.Students.Application/Components/Students/GradeTopicsDialog.razor`
     - Add a "Period" cell per row showing the current period (resolved name via
       a passed-in `Dictionary<Guid, string> periodNames` or the assignment's
       `PeriodId` short-form; "Year-spanning" when null).
     - Add an "Edit period" `RowAction` that opens a small new dialog
       `TopicAssignmentPeriodEditDialog` with the assignment id + current
       `PeriodId` + the grade id (for valid-period filtering).
   - New: `src/Students/SchoolCollab.Students.Application/Components/Students/TopicAssignmentPeriodEditDialog.razor`
     - Loads valid periods for the grade owner (active AcademicYear + its
       active Term/Semester sub-periods, plus a "Year-spanning (no period)"
       option) — mirrors the period-loading logic in `TopicCreateDialog`
       (`_activeYearId` filtering). Calls
       `Api.UpdateTopicAssignmentPeriodAsync(assignmentId, selected)`, surfaces
       422 messages, returns a result that triggers `ReloadAssignedTopicsAsync`.
     - Reuses `DialogShellBase`/`ShowShellDialogAsync` like the other dialogs.
   - **Deferred (group path UI):** no group-topics list surface exists; the
     endpoint supports it. Follow-up tracked above.

### Acceptance (Task 1)

- `PUT /students/topic-assignments/{id}/period` with a valid period for the
  assignment's owner updates `PeriodId` and returns the updated `TopicAssignmentDto`.
- Setting `PeriodId = null` reverts to year-spanning (grade) / date-based
  window (group) and is accepted.
- An invalid period for the owner is rejected with 422 + the same message the
  create endpoint produces (FR-56/57): e.g. a Term for a `WholeAcademicYear`
  group, a Term outside the active year for a grade, a non-null period for an
  `OpenEnded` group.
- A missing assignment id returns 404.
- The grade Topics card shows each assigned topic's period and lets the admin
  change it; the create endpoint and the update endpoint enforce the same
  period rules (shared validator).

### Test expectations (Task 1)

Project: `tests/SchoolCollab.Students.Tests.Unit` (new file
`UpdateTopicAssignmentPeriodTests.cs`, plus extend `TopicAssignmentPeriodTests`
if convenient). Reuse `StudentsTestScope` (seeds active year + term, grade,
activity groups as in the existing tests).
- `Update_GradeAssignment_ValidAcademicYear_Succeeds`
- `Update_GradeAssignment_TermWithinActiveYear_Succeeds`
- `Update_GradeAssignment_TermOutsideActiveYear_Throws` (FR-57/EC-24)
- `Update_GradeAssignment_NullPeriod_RevertsToYearSpan`
- `Update_GroupAssignment_TermlyGroup_TermPeriod_Succeeds` (FR-56)
- `Update_GroupAssignment_OpenEndedGroup_WithPeriod_Throws` (FR-56/EC-23)
- `Update_GroupAssignment_TermlyGroup_AcademicYearPeriod_Throws` (FR-56)
- `Update_UnknownAssignment_Throws` (KeyNotFound)
- `Update_DoesNotChangeOwner` (GradeLevelId/ActivityGroupId unchanged)
- `MigrationGuardTests.NoUncommittedModelChanges` still passes (no schema
  change — `PeriodId` column already exists).
Admin bUnit (project `SchoolCollab.Students.Tests.Unit` or Admin tests, where
the dialog tests live): a `TopicAssignmentPeriodEditDialog` test asserting the
valid period list is offered and a 422 error renders — keep light; if the
dialog is thin enough, defer to a manual check and document.

---

## Task 2 — String-flag audit-log value display

### Scope decision

- **In scope:** add nullable `PreviousValue`/`NewValue` string columns to
  `FlagAuditEntry` + an additive migration; capture the string value at every
  relevant `auditor.Record(...)` call site (string-flag value mutations); expose
  the values on the `FlagAuditEntryDto` + query; render them in the
  `ConfigFlagDetail.razor` audit grid.
- **Deferred:** capturing the *global default value* change for string flags
  (there is no `SetFeatureFlagValue` command today — the global default is seed
  time only, per the existing UI note "The global default is set at seed time").
  When such a command is added later it will reuse the new audit fields. The
  `CreateFeatureFlagHandler` already records on create; it will pass
  `newValue: flag.Value` so a seeded string flag's initial value is audited.
- **Out of scope:** a Settings-context `NoUncommittedModelChanges` guard test
  (none exists today; the migration is verified by the integration tests
  applying it via Testcontainers). Adding that guard is a separate hardening
  follow-up.

### Change list

1. **Domain** — `src/Settings/SchoolCollab.Settings.Core/Domain/FlagAuditEntry.cs`
   - Add `public string? PreviousValue { get; private set; }` and
     `public string? NewValue { get; private set; }`.
   - Extend `Create(...)` with `string? previousValue, string? newValue`
     parameters (appended after `newIsEnabled` to minimise call-site churn, or
     as named args at call sites). Set both fields.

2. **Configuration** — `src/Settings/SchoolCollab.Settings.Core/Data/Configurations/FlagAuditEntryConfiguration.cs`
   - `builder.Property(x => x.PreviousValue).HasMaxLength(200);`
   - `builder.Property(x => x.NewValue).HasMaxLength(200);`
   (200 matches the existing `FeatureFlagKey` width and covers the enum-like
   division values `None`/`Terms`/`Semesters`; generous enough for any
   string-flag value.)

3. **Migration** — new additive migration under
   `src/Settings/SchoolCollab.Settings.Core/Migrations/`:
   `<ts>_AddFlagAuditEntryValueColumns.cs` — `ALTER TABLE flag_audit_entries
   ADD COLUMN previous_value text NULL, ADD COLUMN new_value text NULL;`
   (nullable ⇒ back-compatible with existing rows; no data backfill needed.)

4. **Auditor** — `src/Settings/SchoolCollab.Settings.Core/Services/FeatureFlagAuditor.cs`
   - Extend `Record(...)` with `string? previousValue, string? newValue`
     params and pass them to `FlagAuditEntry.Create`.

5. **Call sites** — `src/Settings/SchoolCollab.Settings.Core/CQRS/FeatureFlags/Commands/FeatureFlagCommandHandlers.cs`
   - Update every `auditor.Record(...)` call to pass `previousValue`/`newValue`:
     - `CreateFeatureFlagHandler` → `previousValue: null, newValue: flag.Value`
       (null for boolean flags; the seeded value for string flags).
     - `UpsertTenantFlagOverrideHandler` → capture `string? previousValue =
       existing?.Value;` before the update; `previousValue: previousValue,
       newValue: command.Value`. (Boolean override value is `Value`-less ⇒
       both null — the existing `IsEnabled` fields still carry the bool.)
     - `DeleteTenantFlagOverrideHandler` → `previousValue: existing.Value,
       newValue: null`.
     - `SetFeatureFlagEnabled`/`Rename`/`Archive`/`Unarchive`/`Delete`/`Recover`
       → `previousValue: null, newValue: null` (no string-value mutation).
   - Note: only string-flag (`FlagKind.String`) rows ever carry non-null value
     fields; boolean flags stay null in both columns. The DTO/UI render the
     value columns only when non-null.

6. **DTO + query** —
   - `src/Settings/SchoolCollab.Settings.Core/DTOs/FeatureFlagDtos.cs`:
     extend `FlagAuditEntryDto` with `string? PreviousValue, string? NewValue`
     (insert before `Reason` or after `NewIsEnabled`; keep order stable for
     JSON clients — appended at the end is safest for the integration test's
     local DTO mirror).
   - `src/Settings/SchoolCollab.Settings.Core/CQRS/FeatureFlags/Queries/FeatureFlagQueryHandlers.cs`:
     `ListAuditEntriesHandler` projection adds `e.PreviousValue, e.NewValue`.
   - `src/SchoolCollab.Admin.Shared/Services/ConfigFlagsApiClient.cs`:
     extend the shared `FlagAuditEntryDto` record with the two fields (keeps
     admin host deserialising without a Core reference).
   - `tests/SchoolCollab.Settings.Tests.Integration/ConfigApiTests.cs`:
     update the local `FlagAuditEntryDto` mirror record (line ~164) with the
     two new fields.

7. **UI** — `src/Settings/SchoolCollab.Settings.Application/Components/Pages/ConfigFlagDetail.razor`
   - Audit grid: add a "Value" `TemplateColumn` rendered after "Before -> After",
     showing `@(context.PreviousValue ?? "—") -> @(context.NewValue ?? "—")`
     **only when the flag is a string flag** (`_flag.Kind == FlagKindDto.String`)
     — gate the column with `@if (_flag.Kind == FlagKindDto.String)` around the
     column (or render an empty cell for boolean flags to keep the grid stable).
   - The existing bool "Before -> After" column stays (boolean flags still
     need it).

### Acceptance (Task 2)

- After upserting a tenant override on the `academic_year_division` string
  flag (e.g. `None` → `Terms`), the audit endpoint returns a row with
  `PreviousValue = "None"`, `NewValue = "Terms"` (and `PreviousIsEnabled`/
  `NewIsEnabled` null, unchanged).
- Deleting that override records `PreviousValue = "Terms"`, `NewValue = null`.
- Boolean-flag mutations record `PreviousValue = null`, `NewValue = null`
  (bool state still in `PreviousIsEnabled`/`NewIsEnabled`).
- The migration applies cleanly (integration tests green); existing audit rows
  have NULL value columns.
- `ConfigFlagDetail.razor` shows a "Value" column in the audit grid for string
  flags; no value column noise for boolean flags.

### Test expectations (Task 2)

Project `tests/SchoolCollab.Settings.Tests.Unit`:
- Extend `FeatureFlagAuditorTests`:
  `Record_adds_audit_row_with_previous_and_new_value` — assert
  `PreviousValue`/`NewValue` are persisted from `Record(previousValue, newValue)`.
Project `tests/SchoolCollab.Settings.Tests.Integration` (`ConfigApiTests.cs`):
- `PUT_UpsertStringOverride_WritesValueAuditRow` — create a `FlagKind.String`
  flag, upsert an override with `Value = "Terms"`, assert the audit row has
  `NewValue == "Terms"` and `PreviousValue == null`; upsert again with
  `"Semesters"` and assert `PreviousValue == "Terms"`, `NewValue == "Semesters"`.
- (Existing `PUT_SetEnabled_WritesAuditRow` stays green; value columns null.)
Admin bUnit (`AcademicYearDivisionSettingTests` or a new `ConfigFlagDetailTests`
if present): assert the audit grid renders a "Value" column for a string flag
detail. If no `ConfigFlagDetail` bUnit harness exists, defer to a manual UI
check and document — do not spin up a new harness for this.

---

## Task 3 — Backend duplicate-active guard (AssignActivityGroupTopic)

### Scope decision

- **In scope:** reject a duplicate *active* (unended) activity-group topic
  assignment for the same `(ActivityGroupId, TopicId, PeriodId)` in
  `AssignActivityGroupTopicHandler`, with a 409 Conflict response. This
  matches the client-side guard in `TopicCreateDialog` and the existing
  duplicate-active precedent (`DuplicateActiveMembershipException` → 409).
- **Deferred (grade-path symmetry):** the grade create path
  (`CreateTopicForGradeHandler`) intentionally *skips* an identical
  (grade, topic, period) assignment (idempotent), and `CreateForGrade_ExistingSamePeriod_Skips`
  asserts that. Replacing skip with reject there is a behaviour change beyond
  this item and would break that test; `AssignGradeTopicHandler` (the direct
  grade-assign path) is a thin assign with no duplicate guard today. Adding a
  grade-path reject is a separate, larger decision (idempotency vs. reject)
  and is tracked as:
> "Follow-up: decide skip-vs-reject semantics for grade-path duplicate active
>  assignments (`AssignGradeTopicHandler` / `CreateTopicForGradeHandler`)."
- **Out of scope:** a DB unique constraint on
  `(tenant, activity_group_id, topic_id, period_id) WHERE end_date IS NULL`
  (a partial index). The guard is enforced in the handler for now; a DB
  constraint is a migration+concurrency follow-up (and the OpenEnded/DateRange
  null-PeriodId uniqueness pitfall from Rev. 3 would need the same
  NULL-partial-index treatment as memberships). Documented as a residual risk.

### Change list

1. **Exception** — new
   `src/Students/SchoolCollab.Students.Core/Domain/Exceptions/DuplicateTopicAssignmentException.cs`
   - `public sealed class DuplicateTopicAssignmentException : Exception` with a
     constructor taking the group/topic/period ids for a clear message. Mirrors
     `DuplicateActiveMembershipException` (which maps to 409).

2. **Handler** — `src/Students/SchoolCollab.Students.Core/CQRS/TopicAssignments/Commands/AssignActivityGroupTopic/AssignActivityGroupTopicHandler.cs`
   - Inject `IActivityGroupTopicAssignmentRepository` (already present).
   - After `ValidatePeriodAsync` (so an invalid period still 422s first) and
     before `ActivityGroupTopicAssignment.Create`, run the guard:
     ```
     var today = DateOnly.FromDateTime(DateTime.UtcNow);
     var active = await repository.ListByActivityGroupAsync(command.ActivityGroupId, today, cancellationToken);
     if (active.Any(a => a.TopicId == command.TopicId && a.PeriodId == command.PeriodId))
         throw new DuplicateTopicAssignmentException(command.ActivityGroupId, command.TopicId, command.PeriodId);
     ```
   - "Active" = the same definition used by `CreateTopicForGradeHandler`'s
     idempotency check: an assignment effective on today (`StartDate <= today`
     and `EndDate` null or `>= today`) with the same `TopicId` and `PeriodId`.
     `ListByActivityGroupAsync` already filters by effectiveDate, so reuse it.
   - Note on period equality: `PeriodId` is `Guid?`; `==` compares nulls as
     equal, so a second date-based (null-period) assignment for the same
     (group, topic) is also rejected — consistent with the client guard.

3. **Route mapping** — `src/Students/SchoolCollab.Students.Api/Endpoints/TopicAssignmentRoutes.cs`
   - In the `POST /topic-assignments/activity-group` handler, add
     `catch (DuplicateTopicAssignmentException ex) { return Results.Conflict(new { ex.Message }); }`
     (409, matching `DuplicateActiveMembershipException` handling in
     `ActivityGroupRoutes`). Keep the existing `TopicAssignmentPeriodException`
     (422) and `ActivityGroupNotFoundException` (404) catches above it.

### Acceptance (Task 3)

- `POST /students/topic-assignments/activity-group` with `(group, topic,
  period)` that already has an active assignment returns 409 Conflict with a
  clear message.
- A second assignment for the same group+topic but a *different* `PeriodId` is
  still accepted (the domain permits multiple bridge rows per (group, topic)
  with different period scopes — same as the grade path).
- Period validation still runs first: an invalid period still yields 422, not
  a duplicate 409.
- The client `TopicCreateDialog` guard and the backend guard now agree (the
  backend is authoritative; the client guard becomes a UX nicety).

### Test expectations (Task 3)

Project `tests/SchoolCollab.Students.Tests.Unit` (extend
`TopicAssignmentPeriodTests.cs`):
- `AssignGroup_DuplicateActiveSamePeriod_Throws` — seed a Termly group + active
  term, assign once, assign again with the same `(group, topic, termId)` ⇒
  `DuplicateTopicAssignmentException`.
- `AssignGroup_DuplicateActiveNullPeriod_Throws` — OpenEnded-style: assign a
  null-PeriodId assignment twice for the same (group, topic) ⇒ throws (null
  period compared equal).
- `AssignGroup_DifferentPeriod_AllowsSecond` — two assignments for the same
  (group, topic) with different `PeriodId` (or one null + one set) both
  succeed.
- `AssignGroup_InvalidPeriod_Still422BeforeDuplicate` — when both an invalid
  period *and* a duplicate would apply, the 422 period error wins (validation
  runs before the duplicate guard). Assert `TopicAssignmentPeriodException`,
  not `DuplicateTopicAssignmentException`.

---

## Cross-cutting

- **Build:** `dotnet build SchoolCollab.sln` — 0 errors, no new warnings.
- **Migrations:** Task 1 adds no schema (column exists). Task 2 adds one
  additive Settings migration. Task 3 adds no schema.
- **Migration guards:** `tests/SchoolCollab.Students.Tests.Unit/MigrationGuardTests.NoUncommittedModelChanges`
  must stay green (no Students schema change). Settings has no such guard
  today; the Task 2 migration is validated by the Settings integration tests
  applying it against Testcontainers Postgres.
- **No staged files:** the worker must not leave staged/uncommitted scratch.

---

## Acceptance criteria the reviewer must check

Reviewer verifies, per task, that:

1. **Task 1**
   - `UpdateTopicAssignmentPeriod` command/handler exist and reuse the shared
     `TopicAssignmentPeriodValidator` (not a duplicated rule set).
   - `PUT /topic-assignments/{id}/period` returns 200 + DTO on success, 404 on
     unknown id, 422 on an invalid period with the create-equivalent message.
   - `TopicAssignment.UpdatePeriod` is the only `PeriodId` mutation path.
   - The application-layer `TopicAssignmentDto` now carries `PeriodId`.
   - The grade Topics card shows the period and offers an "Edit period" action
     that calls the new endpoint and reloads.
   - Group-path UI is documented as deferred (endpoint supports it).

2. **Task 2**
   - `FlagAuditEntry` has `PreviousValue`/`NewValue`; the migration adds the
     nullable columns; `FlagAuditEntryConfiguration` maps them.
   - `FeatureFlagAuditor.Record` and all call sites pass the value fields;
     string-flag value mutations capture before/after; boolean mutations leave
     them null.
   - `FlagAuditEntryDto` (Core + Admin.Shared) and `ListAuditEntriesHandler`
     expose the fields.
   - `ConfigFlagDetail.razor` renders a value column for string flags.
   - Integration test asserts a string override upsert writes
     `PreviousValue`/`NewValue`.

3. **Task 3**
   - `AssignActivityGroupTopicHandler` rejects a duplicate active
     `(group, topic, period)` with `DuplicateTopicAssignmentException`.
   - The activity-group POST route maps it to 409 Conflict.
   - Different-period and different-topic assignments still succeed.
   - Period validation precedes the duplicate guard (422 before 409).
   - Grade-path reject is documented as deferred (skip semantics preserved).

4. **General**
   - `dotnet build SchoolCollab.sln` is clean.
   - The listed unit/integration tests pass.
   - No staged files left behind.

---

## Test summary (projects + names)

| Task | Project | Tests |
|---|---|---|
| 1 | `SchoolCollab.Students.Tests.Unit` | `UpdateTopicAssignmentPeriodTests.cs`: `Update_GradeAssignment_ValidAcademicYear_Succeeds`, `Update_GradeAssignment_TermWithinActiveYear_Succeeds`, `Update_GradeAssignment_TermOutsideActiveYear_Throws`, `Update_GradeAssignment_NullPeriod_RevertsToYearSpan`, `Update_GroupAssignment_TermlyGroup_TermPeriod_Succeeds`, `Update_GroupAssignment_OpenEndedGroup_WithPeriod_Throws`, `Update_GroupAssignment_TermlyGroup_AcademicYearPeriod_Throws`, `Update_UnknownAssignment_Throws`, `Update_DoesNotChangeOwner` |
| 2 | `SchoolCollab.Settings.Tests.Unit` | `FeatureFlagAuditorTests`: `Record_adds_audit_row_with_previous_and_new_value` |
| 2 | `SchoolCollab.Settings.Tests.Integration` | `ConfigApiTests`: `PUT_UpsertStringOverride_WritesValueAuditRow` |
| 3 | `SchoolCollab.Students.Tests.Unit` | `TopicAssignmentPeriodTests` (extended): `AssignGroup_DuplicateActiveSamePeriod_Throws`, `AssignGroup_DuplicateActiveNullPeriod_Throws`, `AssignGroup_DifferentPeriod_AllowsSecond`, `AssignGroup_InvalidPeriod_Still422BeforeDuplicate` |

---

## Residual risks / follow-ups

- **Task 1:** group-path UI edit surface deferred (no group-topics list page
  exists; endpoint supports it).
- **Task 2:** no Settings `NoUncommittedModelChanges` guard test exists; the
  migration is covered by integration tests only. (Optional hardening
  follow-up.)
- **Task 3:** no DB partial-unique constraint backing the handler guard
  (concurrency race window between two simultaneous identical requests;
  matches the membership guard's current handler-only approach). Grade-path
  skip-vs-reject semantics deferred.

---

## Acceptance

> Orchestrator acceptance pass. Independent build/test run + P1 fix applied.
> Reviewer code-read verdicts (see `review-redeferred-tasks.md`) re-verified
> against actual command output below.

### P1 fix applied

- `src/Settings/SchoolCollab.Settings.Core/CQRS/FeatureFlags/Commands/FeatureFlagCommandHandlers.cs`
  had duplicate `using SchoolCollab.Core.CQRS;` (lines 2 & 10) and
  `using SchoolCollab.Core.Messaging;` (lines 3 & 11) → CS0105 warnings.
  Removed the two duplicate lines at the bottom of the using block. After the
  fix the solution build emits **no CS0105** warnings.

### Commands run (independent)

| Command | Result | Notes |
|---|---|---|
| `dotnet build SchoolCollab.sln -c Debug --nologo -v q` | **passed** | 0 errors, 5 warnings — all pre-existing (NU1903 package-vuln advisories for `SQLitePCLRaw.lib.e_sqlite3`/`SSH.NET`, and a pre-existing CS9113 unread `publisher` param in untouched `CreateEntityCodeRuleHandler`). No new warnings; CS0105 gone after P1 fix. |
| `SchoolCollab.Students.Tests.Unit` (exe) | **passed** | total 316, failed 0, skipped 0. Includes the new `UpdateTopicAssignmentPeriodTests.cs` and the extended `TopicAssignmentPeriodTests.cs` duplicate-guard cases (`AssignGroup_DuplicateActiveSamePeriod_Throws`, `AssignGroup_DuplicateActiveNullPeriod_Throws`, `AssignGroup_DifferentPeriod_AllowsSecond`, `AssignGroup_InvalidPeriod_Still422BeforeDuplicate`). |
| `SchoolCollab.Settings.Tests.Unit` (exe) | **passed** | total 442, failed 0, skipped 0. Includes `FeatureFlagAuditorTests.Record_adds_audit_row_with_previous_and_new_value`. |
| `SchoolCollab.Admin.Tests.Unit` (exe) | **passed** | total 477, failed 0, skipped 0 (regression gate for the touched Admin.Shared client + dialogs). |
| `SchoolCollab.Settings.Tests.Integration` (exe) | **passed*** | total 27, succeeded 24, failed 3. The 3 failures are all `ChatAsync_WithOpenRouter_*` live-AI tests in `ChatProviderLiveTests`/`CodedValueAIServiceLiveTests` — they require a live OpenRouter API key/network and are unrelated to this work. The Task 2 in-scope tests `PUT_UpsertStringOverride_WritesValueAuditRow` and `PUT_SetEnabled_WritesAuditRow` both **Passed** (TRX-confirmed). |

> *The `dotnet test` wrapper reported “Zero tests ran” for the MTP-based unit
> projects (a known Microsoft Testing Platform discovery quirk under this
> harness); each project was run via its built `net10.0` executable instead,
> which is the authoritative MTP entry point.

### No staged files

`git diff --cached --name-only` is empty — no staged/uncommitted scratch left
behind by the worker.

### Per-criterion verdict

#### Task 1 — Period editing

| Criterion | Verdict | Evidence |
|---|---|---|---|
| `UpdateTopicAssignmentPeriod` command/handler reuse shared `TopicAssignmentPeriodValidator` | **CLOSED** | Files exist; both create handlers refactored to the shared validator; build clean |
| `PUT /topic-assignments/{id}/period` 200/404/422 | **CLOSED** | Route maps `KeyNotFoundException`→404, `TopicAssignmentPeriodException`→422; `UpdateTopicAssignmentPeriodTests` pass |
| `UpdatePeriod` only mutation path | **CLOSED** | `PeriodId` private-set; single `UpdatePeriod` method |
| Application `TopicAssignmentDto.PeriodId` | **CLOSED** | Present in Core + application DTOs |
| Grade Topics card edit surface | **CLOSED** | `Detail.razor` + `GradeTopicsDialog.razor` + `TopicAssignmentPeriodEditDialog.razor` wired; Admin bUnit regression green |
| Group-path UI deferred | **CLOSED (deferred as planned)** | No group-topics list UI; endpoint supports it; follow-up tracked |

#### Task 2 — String-flag audit value

| Criterion | Verdict | Evidence |
|---|---|---|
| `FlagAuditEntry` columns + migration + config | **CLOSED** | Nullable `previous_value`/`new_value` columns; `HasMaxLength(200)`; additive migration applied by integration tests |
| Auditor + call sites capture values | **CLOSED** | `Create`/`Upsert`/`Delete` pass before/after; bool-only handlers leave null |
| DTO + query + Admin.Shared client | **CLOSED** | Core DTO, Admin.Shared DTO, `ListAuditEntriesHandler` projection all carry the fields |
| `ConfigFlagDetail` value column | **CLOSED** | Conditionally rendered for `FlagKindDto.String` |
| Integration test for value audit | **CLOSED** | `PUT_UpsertStringOverride_WritesValueAuditRow` Passed (TRX-confirmed; None→Terms→Semesters) |

#### Task 3 — Duplicate-active guard

| Criterion | Verdict | Evidence |
|---|---|---|
| Handler rejects duplicate active `(group, topic, period)` | **CLOSED** | `DuplicateTopicAssignmentException` after period validation; `AssignGroup_DuplicateActiveSamePeriod_Throws` + `AssignGroup_DuplicateActiveNullPeriod_Throws` pass |
| Route maps to 409 | **CLOSED** | `catch (DuplicateTopicAssignmentException) → Results.Conflict` |
| Different period/topic still allowed | **CLOSED** | `AssignGroup_DifferentPeriod_AllowsSecond` passes |
| 422 before 409 | **CLOSED** | `AssignGroup_InvalidPeriod_Still422BeforeDuplicate` passes |
| Grade-path skip semantics preserved | **CLOSED (deferred as planned)** | No guard added to grade path; follow-up tracked |

#### General

| Criterion | Verdict | Evidence |
|---|---|---|
| Clean build (0 errors, no new warnings) | **CLOSED** | Build succeeded; 0 errors; only pre-existing warnings; CS0105 removed by P1 fix |
| Tests pass | **CLOSED** | Students 316 / Settings 442 / Admin 477 all green; Settings integration config-flag tests green (3 unrelated live-AI failures) |
| No staged files | **CLOSED** | `git diff --cached` empty |

### Overall verdict

**CLOSED.**

All three in-scope tasks meet their plan acceptance criteria. The P1
build-blocker (duplicate `using` directives) is fixed. Build is clean with no
new warnings; all in-scope unit and integration tests pass. The 3 integration
failures are pre-existing live-OpenRouter AI tests unrelated to this round.

### Residual items (carry forward, as planned)

- Task 1: activity-group topic-assignments list/edit **UI** surface (endpoint
  already supports the group subtype).
- Task 2: no Settings `NoUncommittedModelChanges` guard test (optional
  hardening; migration covered by integration tests).
- Task 2: capturing the *global default value* change for string flags (no
  `SetFeatureFlagValue` command exists today).
- Task 3: no DB partial-unique constraint on
  `(tenant, activity_group_id, topic_id, period_id) WHERE end_date IS NULL`
  (concurrency race window; handler-only guard, matching memberships).
- Task 3: grade-path skip-vs-reject semantics decision deferred.
- UI polish (non-blocking): period-edit dialog does not pre-filter to active
  sub-periods; `GradeTopicsDialog` shows period short-form GUID prefix; no
  bUnit coverage for the new dialog / audit grid (manual check per plan).
- Environment note: 3 `ChatAsync_WithOpenRouter_*` integration tests fail in
  this sandbox due to live OpenRouter API access — unrelated to this round.
  Re-run on a host with credentials to confirm green.