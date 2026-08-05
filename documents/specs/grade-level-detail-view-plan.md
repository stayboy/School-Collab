# Spec: Grade-Level Detail/View Page + Teacher Role Tags + Subject->Topic Rename Finish

> Status: **Complete - all phases implemented + tests green; submitted as a linked gh-stack (stack #118, PRs #113-#117) awaiting merge**
> Owner: Students + Settings + Admin contexts
> Depends on: `grade-level-landing-topics-strands-lessons.md`,
> `grade-level-simplified-management.md`, `subject-to-topic-polymorphism.md`,
> `coded-values-architecture.md`, `multi-tenant-coded-values.md`,
> `landing-page-wrapper.md`, `ui-visible-tenancy-guard.md`
> Branch: `feat/grade-level-detail-view`

## 0. Decisions locked in this revision

1. **Add a routable Grade-Level detail/view page.**
   `@page "/students/grade-levels/{Id:guid}"` ->
   `src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Detail.razor`.
   The landing `Name` column becomes a `FluentAnchor` to it; the row kebab gains a
   **View** action (`FluentIcons.Open`) before Edit. Edit/Delete unchanged.
2. **Tabs, not accordion.** The page uses `FluentTabs` (already the established
   pattern in `Student Detail.razor`) with three peer-level concerns:
   **Topics & Curriculum** . **Teachers** . *(deferred)* **Notification & Delivery**.
3. **Teacher role is a coded value, not an enum.** A new `CodedValueParent.TeacherRoles`
   (`TCHROLES`) parent is seeded with sample children
   (*Head of Grade, Class Teacher, Assistant Teacher, Subject Lead*). The role lives on
   the `TeacherGradeLevel` link as a nullable `TeacherRoleCodedValueId` - mirrors the
   `StudentGuardian.RelationshipCodedValueId` pattern. Tenant-definable, picker via
   `CodedValueDropdown Parent="CodedValueParent.TeacherRoles"`.
4. **Finish the Subject->Topic rename** in the same pass (mechanical, kept in its own
   commit/PR step for reviewability). All new code uses canonical `Topic` naming; legacy
   `Subject`-named teacher-link commands/routes/client methods are renamed; the redundant
   `SubjectRoutes.cs` / `CQRS.Subjects.*` are audited and folded into `TopicRoutes` or
   deleted; UI labels read "Topics".
5. **Fix the dead Create/Edit grade routes** in the same pass. The landing already
   navigates to `/students/grade-levels/create` and `/{id}/edit`, but those routable
   pages don't exist (only `GradeLevelFormFields.razor` + dialogs do). Create the two
   pages per `grade-level-landing-topics-strands-lessons.md` 0.5.
6. **Notification & Delivery is deferred but shaped.** Channel preference is a
   **global tenant default with per-grade exceptions**. Not built in this pass -
   documented in 9 so the Phase-2/3 build is unambiguous.

## 1. Goal

A tenant admin opens a grade level and, on one screen, manages:

- **Topics & Curriculum:** assign/remove topics to the grade; per-topic inline
  strands + lessons CRUD (absorbs the stub `StrandsDialog`/`LessonsDialog`); create a
  topic for the grade.
- **Teachers:** see teachers linked to this grade with a **role tag**; add/remove
  teachers; change a teacher's role on this grade; link/unlink that teacher's topics.
- **Overview:** name, level, age range, gender, enrollment-blocked toggle, counts,
  dates.

Plus: the missing Create/Edit grade pages, the `Subject->Topic` rename finish, and the
`TCHROLES` coded-value parent + seed.

## 2. Current-state analysis (what exists vs. new)

### Already backend-backed (UI wiring only)
| Concern | Existing backend |
|---|---|
| Read grade | `GET /grade-levels/{id}` (`GetGradeLevelById`) + `/grade-levels/landing` aggregates |
| Topics <-> grade | `GET /topics/by-grade/{gradeLevelId}` (`ListTopicsByGrade`), `AssignGradeTopicAsync`, `RemoveTopicAssignmentAsync` - already used by `GradeLevelEditDialog` |
| Strands CRUD | `GET /topics/{topicId}/strands`, `POST /topics/strands`, `PUT /topics/strands/{id}`, `DELETE /topics/strands/{id}` |
| Lessons CRUD | `GET /topics/{topicId}/lessons`, `POST /topics/lessons`, `PUT /topics/lessons/{id}`, `POST /topics/lessons/{id}/strand`, `DELETE /topics/lessons/{id}` |
| Teacher <-> grade / topic links | `POST/DELETE /teachers/{id}/grade-levels`, `POST/DELETE /teachers/{id}/subjects` (-> renamed `/topics`), `GET /teachers/{id}/grade-levels`, `GET /teachers/{id}/subjects` (-> `/topics`) |
| Enrollment block toggle | `PATCH /grade-levels/{id}/enrollment-blocked` (already on landing) |

### Gaps this plan closes
1. **No "list teachers *for* a grade"** -> new `ListTeachersForGradeLevel` query +
   `GET /grade-levels/{gradeLevelId}/teachers`.
2. **No role on `TeacherGradeLevel`** -> new nullable `TeacherRoleCodedValueId` column +
   migration + `SetTeacherGradeLevelRole` command + `PATCH` route.
3. **No `TCHROLES` coded-value parent** -> enum value + seed CSV rows.
4. **No Detail page** -> new `Detail.razor`.
5. **Dead Create/Edit grade routes** -> new `Create.razor` / `Edit.razor`.
6. **Subject->Topic rename incomplete** -> finish (see 6).

### Deferred (9)
- Channel-preference policy (global tenant default + per-grade exceptions).
- Reminder/sendout worker, link-validity enforcement - the 18
  "email/SMS/WhatsApp delivery" deferred feature.

## 3. Backend - Students.Core

### 3.1 Teacher role on the grade link
- **`Domain/TeacherGradeLevel.cs`:** add `public Guid? TeacherRoleCodedValueId { get; private set; }`.
  Extend `Create(teacherId, gradeLevelId, roleCodedValueId = null)`. Add
  `SetRole(Guid? roleCodedValueId)` (idempotent, stamps `UpdatedAt`). Keep `xmin` row version.
- **`Domain/Teacher.cs`:** `LinkGradeLevel(gradeLevelId, roleCodedValueId = null)`.
  (Cosmetic: rename `LinkTopic(Guid subjectId)` -> `LinkTopic(Guid topicId)`, backing
  field `_subjects` -> `_topics`.)
- **Migration:** `AddTeacherRoleToTeacherGradeLevel.cs` - nullable column, no backfill
  (existing links -> null). Add index on `(GradeLevelId)` to support the new inverse query.
- **CQRS (new/extended), under `CQRS.Teachers.*`:**
  - Extend `LinkTeacherGradeLevel` command + handler to accept `TeacherRoleCodedValueId`.
  - New `SetTeacherGradeLevelRole(teacherId, gradeLevelId, roleCodedValueId)` command + handler.
  - New `ListTeachersForGradeLevel(gradeLevelId)` query -> returns
    `TeacherWithRoleDto[]` (`TeacherDto` + `TeacherRoleCodedValueId` + `AssignedTopics: TopicDto[]`).
- **`Endpoints/GradeLevelRoutes.cs`:** add
  `GET /grade-levels/{gradeLevelId:guid}/teachers` -> `ListTeachersForGradeLevel`.
- **`Endpoints/TeacherRoutes.cs`:** add
  `PATCH /teachers/{id:guid}/grade-levels/{gradeLevelId:guid}/role` -> `SetTeacherGradeLevelRole`
  (body `SetTeacherRoleRequest(Guid? RoleCodedValueId)`); extend the existing
  `POST /teachers/{id}/grade-levels` body with optional `RoleCodedValueId`.

### 3.2 DTOs
- `TeacherWithRoleDto` (Students.Core.DTOs): `TeacherDto` fields + `Guid? TeacherRoleCodedValueId` + `TopicDto[] AssignedTopics`.
- All new DTOs use `TopicDto` (canonical), never `SubjectDto`.

### 3.3 EF configuration
- `TeacherGradeLevelConfiguration`: map the new nullable column + the `GradeLevelId` index.

## 4. Backend - Settings.Core (coded-value parent + seed)

### 4.1 `CodedValueConstants.cs` (Admin.Shared)
```csharp
    GradeStrands = 11,
    /// <summary>Teacher roles on a grade link (children of <c>TCHROLES</c>).
    /// Nullable FK on <c>TeacherGradeLevel.TeacherRoleCodedValueId</c>.</summary>
    TeacherRoles = 12
```
and in `CodedValueParentExtensions.ToCode`:
```csharp
    CodedValueParent.TeacherRoles => "TCHROLES",
```

### 4.2 Seed (sample roles)
Add rows to `src/SchoolCollab.MigrationService/Seeding/seed.csv` (read by the idempotent
`CodedValueSeeder`; mirror the `GRSTRNDS` seeding pattern). Parent + four children:

| Code | Name | ParentCode | DisplayOrder |
|---|---|---|---|
| `TCHROLES` | Teacher Roles | *(root)* | - |
| `TCHROLE_HOG` | Head of Grade | `TCHROLES` | 1 |
| `TCHROLE_CT` | Class Teacher | `TCHROLES` | 2 |
| `TCHROLE_AT` | Assistant Teacher | `TCHROLES` | 3 |
| `TCHROLE_SL` | Subject Lead | `TCHROLES` | 4 |

(Exact CSV column order to match `CodedValueSeedRow`; tenant-definable afterward via the
Coded Values admin page.)

## 5. Frontend - `GradeLevels/Detail.razor`

**Route:** `@page "/students/grade-levels/{Id:guid}"` at
`src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Detail.razor`.

**Shell** (mirrors `Student Detail.razor`): `PageTitle` -> `ErrorBoundary` -> `TenantGate`
-> title row (resolved grade name . Level . age range . gender . `[Blocked]` badge .
`[Edit]` button -> `/students/grade-levels/{id}/edit`) -> Overview `FluentCard` -> `FluentTabs`.

**Overview card:** resolved coded-value name, level, display order, age range, gender,
enrollment-blocked status + `FluentSwitch` toggle (`PATCH .../enrollment-blocked`), and
student/topic/strand/lesson counts. Resolves coded-value names via `CodedValuesApi`
(same pattern as the landing page). Uses `@using TopicDto = SchoolCollab.Students.Core.DTOs.TopicDto`
to avoid the documented `SubjectDto`/`TopicDto` CS0104 ambiguity.

### Tab 1 - Topics & Curriculum (existing APIs)
- Assigned-topics `FluentDataGrid` from `ListTopicsByGrade(gradeLevelId)`, with
  Add (topic picker -> `AssignGradeTopicAsync`) / Remove (`RemoveTopicAssignmentAsync`) -
  reuses the diff logic already in `GradeLevelEditDialog.ApplyTopicDiffAsync`.
- Per-topic expander (`FluentAccordion` per row) -> **Strands** list (CRUD via
  `Create/Update/DeleteTopicStrand`) + **Lessons** list (CRUD via
  `Create/Update/DeleteTopicLesson` + `AssignLessonStrand`). **Absorbs and replaces**
  the stub `StrandsDialog.razor` / `LessonsDialog.razor`.
- "Create topic for grade" button -> existing `CreateTopicForGrade` route.

### Tab 2 - Teachers (3 backend + existing link APIs)
- `FluentDataGrid` of teachers linked to this grade from
  `ListTeachersForGradeLevelAsync`, columns: Name, Email, **Role**
  (`CodedValueDropdown Parent="CodedValueParent.TeacherRoles"` per row; change ->
  `SetTeacherGradeLevelRoleAsync`), **Assigned Topics** (chips; link/unlink via
  `LinkTeacherTopic` / `UnlinkTeacherTopic`).
- "Add teacher" -> picker of unlinked teachers ->
  `LinkTeacherGradeLevelAsync(teacherId, gradeId, roleCodedValueId)`.
- Remove teacher from grade -> `UnlinkTeacherGradeLevelAsync`.
- "View teacher" chip -> `/students/teachers/{id}` (existing `TeacherDetail`).
## 6. Subject -> Topic rename finish

Canonical naming rule for **all new code**: entities, commands, queries, DTOs, routes,
UI labels use **`Topic`**. UI labels read "Topics & Curriculum" / "Assigned Topics".

### 6.1 Domain (Students.Core)
- `Domain/TeacherSubject.cs` -> rename file + class to `TeacherTopic` (field is already
  `TopicId`, nav `Topic`). Update `Teacher.cs` backing field `_subjects` -> `_topics`
  and the `Topics` property (already named correctly).
- `Teacher.LinkTopic/UnlinkTopic` param `subjectId` -> `topicId` (cosmetic).

### 6.2 CQRS + routes (Students.Core + Students.Api)
- `LinkTeacherSubject` -> `LinkTeacherTopic`; `UnlinkTeacherSubject` -> `UnlinkTeacherTopic`
  (command + handler + file/namespace renames). `ListTopicsForTeacher` already canonical.
- `TeacherRoutes.cs`: `/teachers/{id}/subjects` -> `/teachers/{id}/topics` (GET/POST/DELETE).
  Keep a `/subjects` alias only if external consumers need it; default is to drop the alias
  for teacher links (the topic-route alias is the documented deprecation, not teacher links).
  Request record `LinkTeacherSubjectRequest(SubjectId)` -> `LinkTeacherTopicRequest(TopicId)`.

### 6.3 Audit + fold `SubjectRoutes.cs` / `CQRS.Subjects.*`
- `SubjectRoutes.cs` is a **separate legacy file** alongside canonical `TopicRoutes.cs`
  (which already registers `/subjects` as a deprecated alias with shared `CQRS.Topics.*`
  handlers). `CQRS.Subjects.Commands.CreateSubjectForGrade` / `GetOrCreateSubject` overlap
  with `CQRS.Topics.Commands.CreateTopicForGrade` / `GetOrCreateTopic`.
- **Action:** verify `SubjectRoutes.cs` is wired (grep the endpoint-group extension in
  `Program.cs`/`Extensions`). If wired -> fold its unique handlers into `CQRS.Topics.*`,
  re-point the routes to `TopicRoutes` alias, and **delete** `SubjectRoutes.cs` +
  `CQRS.Subjects.*`. If dead -> delete outright. Either way, no `Subject`-named command
  remains in `src/`.

### 6.4 Admin client (Students.Application.Services / StudentsApiClient)
- `ListSubjectsForTeacherAsync` -> `ListTopicsForTeacherAsync` (returns `TopicDto[]`).
- `LinkTeacherSubjectAsync` -> `LinkTeacherTopicAsync`; `UnlinkTeacherSubjectAsync` ->
  `UnlinkTeacherTopicAsync`.
- `CreateSubjectRequest` / `CreateSubjectForGradeRequest` / `CreateSubjectAsync` /
  `GetOrCreateSubjectAsync` -> `CreateTopic*` / `GetOrCreateTopic*` (or remove if
  superseded by canonical topic methods).
- New methods for 3: `ListTeachersForGradeLevelAsync`, `SetTeacherGradeLevelRoleAsync`,
  extended `LinkTeacherGradeLevelAsync(..., roleCodedValueId)`.
- Legacy Admin DTOs `GradeSubjectAssignmentDto` / `StudentSubjectAssignmentDto` reference
  the eliminated `GradeSubjectAssignment` bridge - flag for removal (follow-up, not
  blocking; do not use in new code).

### 6.5 Docs
- Update `documents/configuration.md` coded-value mapping for `TCHROLES`.
- Update `.github/skills/dotnet-best-practices/SKILL.md` examples
  (`ListSubjectsByGrade` -> `ListTopicsByGrade`,
  `CreateSubjectForGradeHandler` -> `CreateTopicForGradeHandler`,
  `SubjectRoutes.cs` -> `TopicRoutes.cs`).

## 7. Frontend - fix dead Create/Edit grade pages

Per `grade-level-landing-topics-strands-lessons.md` 0.5:
- `GradeLevels/Create.razor` -> `@page "/students/grade-levels/create"` -> uses
  `GradeLevelFormFields` + a topics section -> `GetOrCreateGradeLevelAsync` +
  `AssignGradeTopicAsync` per topic -> navigate to detail (or landing) on success.
- `GradeLevels/Edit.razor` -> `@page "/students/grade-levels/{id:guid}/edit"` -> loads
  grade + assignments, pre-checks topics -> `UpdateGradeLevelAsync` + topic diff ->
  navigate back to detail.
- Extend `GradeLevelFormFields.razor` with the topics multi-select (absorb
  `GradeLevelEditDialog` topic-diff logic). Optionally delete
  `GradeLevelCreateDialog.razor` / `GradeLevelEditDialog.razor` per the spec.

## 8. Landing wiring (`GradeLevels/Index.razor`)
- `Name` column -> `FluentAnchor Href="@($\"/students/grade-levels/{context.Id}\")"`.
- `BuildRowActions`: add **View**
  (`RowAction.Callback("View", () => Nav.NavigateTo($"/students/grade-levels/{row.Id}"), FluentIcons.Open)`)
  before Edit. Edit/Delete unchanged.

## 9. Deferred - Notification & Delivery (shaped, not built this pass)

When built (Phase 2/3), per the "global tenant default + per-grade exceptions" directive:
- **`TenantNotificationPolicy`** (Settings.Core) - one row per tenant; the **global
  default**: `PreferredChannelOrder` (ordered Email/SMS/WhatsApp), `BlockedChannels`,
  `MaxNotifications`, `MaxReminders`, `ReminderIntervalHours`, `LinkValidityDays`,
  `SendoutTimeOfDay`, `SendoutIntervalMinutes`.
- **`GradeNotificationPolicy`** (Students.Core) - optional 1:1 per grade; same fields;
  any non-null field **overrides** the tenant default; null fields inherit.
- `PublishAssignmentCommandHandler` resolves the effective policy (grade exception ->
  tenant default), filters recipients by blocked channels, applies preferred order.
- New `AssignmentReminderWorker` (`BackgroundService`, Assignments.Core) reads
  `SendoutTimeOfDay`/`SendoutIntervalMinutes`/`MaxReminders`/`ReminderIntervalHours`;
  enforces `LinkValidityDays` expiry. This is the 18 "email/SMS/WhatsApp delivery"
  deferred feature.
- UI: a third **Notification & Delivery** tab showing the effective (merged) policy with
  "uses global default" indicators per field and per-grade overrides.

## 10. Test plan

### Handler unit tests (Students.Tests.Unit)
- `ListTeachersForGradeLevel`: returns only teachers linked to the grade; each carries
  its role + assigned topics; tenant isolation.
- `SetTeacherGradeLevelRole`: updates role, stamps `UpdatedAt`, idempotent,
  `ConcurrencyException` on stale `xmin`.
- Extended `LinkTeacherGradeLevel`: accepts + persists optional role.
- Rename regressions: `LinkTeacherTopic`/`UnlinkTeacherTopic` still resolve the same
  `TeacherTopic` rows after the rename.

### Integration tests (Students.Tests.Integration)
- `GET /grade-levels/{id}/teachers` 200 + payload shape.
- `PATCH /teachers/{id}/grade-levels/{gradeId}/role` 204 (and 409 on concurrency).
- `POST /teachers/{id}/grade-levels` with role persists.
- `GET /teachers/{id}/topics` (renamed) returns the same data as the old `/subjects`.

### bUnit (Admin.Tests.Unit)
- `Detail.razor`: renders all tabs; Overview shows resolved name + counts; Topics tab
  add/remove topic calls correct APIs; strand/lesson inline CRUD; Teachers tab lists
  teachers with role dropdown bound to `TCHROLES`; role change calls
  `SetTeacherGradeLevelRoleAsync`; add/remove teacher; link/unlink topic.
- `Create.razor` / `Edit.razor`: save + navigate.
- Landing: `Name` is an anchor to detail; kebab has View before Edit.

### Seed verification
- `CodedValueSeeder` run is idempotent; `TCHROLES` parent + 4 children present;
  `CodedValueParent.TeacherRoles.ToCode() == "TCHROLES"`.

## 11. Open questions / follow-ups
1. **Teacher-role uniqueness.** Is a role unique per (grade, teacher), or can a teacher
   hold two roles on one grade? Default: unique (one `TeacherGradeLevel` row per pair;
   role is an attribute of that row) - confirm.
2. **`/teachers/{id}/subjects` alias.** Drop the alias for teacher links (default) or
   keep it for a deprecation window like `/topics`? Default: drop.
3. **Legacy Admin DTOs** (`GradeSubjectAssignmentDto`, `StudentSubjectAssignmentDto`) -
   remove in this pass or a follow-up? Default: follow-up (don't use in new code).
4. **StrandsDialog/LessonsDialog deletion.** Delete once the Topics tab absorbs them,
   or keep as fallback? Default: delete per spec 0.5.

## 12. Rollout order (one PR per step, each shippable)

1. **Settings: `TCHROLES` coded-value parent + seed.** `CodedValueConstants` enum +
   `ToCode`; `seed.csv` rows; seed idempotency test.
2. **Students.Core: teacher role + inverse query.** `TeacherGradeLevel` role field +
   migration + EF config; `SetTeacherGradeLevelRole` + `ListTeachersForGradeLevel`
   CQRS; routes (`GET /grade-levels/{id}/teachers`, `PATCH .../role`); unit + integration tests.
3. **Subject->Topic rename finish.** `TeacherSubject`->`TeacherTopic`; teacher-link
   command/route/client renames; audit + fold/delete `SubjectRoutes.cs` +
   `CQRS.Subjects.*`; doc updates. Build green.
4. **Admin client methods.** Add `ListTeachersForGradeLevelAsync`,
   `SetTeacherGradeLevelRoleAsync`, extended `LinkTeacherGradeLevelAsync`; rename
   `ListSubjectsForTeacherAsync`->`ListTopicsForTeacherAsync`,
   `LinkTeacherSubjectAsync`->`LinkTeacherTopicAsync`, `UnlinkTeacherSubjectAsync`->`UnlinkTeacherTopicAsync`.
5. **Detail.razor + landing wiring.** New page (Overview + Topics & Curriculum tab +
   Teachers tab); landing `Name` anchor + View kebab action; absorb/replace
   Strands/Lessons dialogs. bUnit tests.
6. **Create.razor / Edit.razor.** Two routable pages; extend `GradeLevelFormFields`
   with topics; delete old dialogs. bUnit tests.
7. **Full build + all test suites green.**

## 13. What this spec supersedes / relates to
- Extends `grade-level-landing-topics-strands-lessons.md` (adds the routable **View**
  page and absorbs the strand/lesson dialogs into it).
- Implements the Create/Edit routable pages called for by that spec 0.5.
- Advances the `Subject->Topic` rename from `subject-to-topic-polymorphism.md` 12.1
  (finishes the teacher-link surface).
- Does **not** supersede the deferred Notification & Delivery feature
  (`student-guardian-plan.md` 18) - it shapes it (9) for a later phase.

## 14. Implementation status (live log - updated 2026-08-05)

> This section is a rolling status tracker for the active implementation pass. The
> checklist items map 1:1 to the rollout steps in 12. Items are marked
> **[done]** / **[partial]** / **[pending]** and are updated as work lands.

### Step 1 - Settings: `TCHROLES` coded-value parent + seed — **[done]**
- [x] `CodedValueParent.TeacherRoles = 12` + `ToCode() == "TCHROLES"`
  (`src/SchoolCollab.Admin.Shared/Constants/CodedValueConstants.cs`).
- [x] `seed.csv` rows added (parent `TCHROLES` + 4 children:
  `TCHROLE_HOG` Head of Grade, `TCHROLE_CT` Class Teacher, `TCHROLE_AT`
  Assistant Teacher, `TCHROLE_SL` Subject Lead) at
  `src/SchoolCollab.MigrationService/SeedData/seed.csv`.
- [x] Seed idempotency test touched (`SeedCsvArchitectureTests`).

### Step 2 - Students.Core: teacher role + inverse query — **[done]**
- [x] `TeacherGradeLevel.TeacherRoleCodedValueId` (nullable) + `SetRole(...)`
  (`src/Students/SchoolCollab.Students.Core/Domain/TeacherGradeLevel.cs`).
- [x] Migration `20260805104356_AddTeacherGradeLevelRole` + EF config
  (`TeacherGradeLevelConfiguration`) + `GradeLevelId` index.
- [x] `SetTeacherGradeLevelRole` command + handler.
- [x] `ListTeachersForGradeLevel` query + `TeacherWithRoleDto` DTO.
- [x] `GET /grade-levels/{id:guid}/teachers` route (`GradeLevelRoutes.cs`).
- [x] `PATCH /teachers/{id:guid}/grade-levels/{gradeLevelId:guid}/role`
  route + `SetTeacherGradeLevelRoleRequest` (`TeacherRoutes.cs`).
- [x] Extended `LinkTeacherGradeLevel` command + `POST /teachers/{id}/grade-levels`
  body to accept optional `RoleCodedValueId`.

### Step 3 - Subject->Topic rename finish — **[done]**
- [x] `TeacherSubject` -> `TeacherTopic` (file + class + config
  `TeacherTopicConfiguration`, table `teacher_topics`, index rename).
- [x] `LinkTeacherSubject` / `UnlinkTeacherSubject` CQRS -> `LinkTeacherTopic` /
  `UnlinkTeacherTopic` (files + namespaces + handlers).
- [x] `Teacher.cs`: `_subjects` -> `_topics`, `LinkTopic(topicId)`/`UnlinkTopic`.
- [x] `TeacherRoutes.cs`: `/subjects` -> `/topics`; `LinkTeacherSubjectRequest` ->
  `LinkTeacherTopicRequest`; `ListTopicsForTeacher` query.
- [x] Migration `20260805111523_RenameTeacherSubjectsToTeacherTopics`.
- [x] Audited `SubjectRoutes.cs` + `CQRS.Subjects.*` -> **deleted** (dead / no
  `Subject`-named command remains in `src/`).
- [x] Core + Api build green (0 errors) after rename.

### Step 4 - Admin client methods — **[done]**
- [x] `ListTopicsAsync` (canonical catalog), `ListTopicsForTeacherAsync`.
- [x] `LinkTeacherTopicAsync` / `UnlinkTeacherTopicAsync`.
- [x] `ListTeachersForGradeLevelAsync`, `SetTeacherGradeLevelRoleAsync`,
  extended `LinkTeacherGradeLevelAsync(..., roleCodedValueId = null)`.
- [x] Admin UI (`TeacherSetupWizard.razor`, `TeacherDetail.razor`) rewired to the
  topic/role client methods. **UI display labels remain "Subjects"** by explicit
  instruction (only internal identifiers/DTO method names renamed to `Topic`).

### Step 5 - Detail.razor + landing wiring — **[done]**
- [x] `GradeLevels/Detail.razor` (`@page "/students/grade-levels/{Id:guid}"`) with
  Overview + Topics & Curriculum + Teachers tabs.
- [x] Landing `Name` -> `FluentAnchor` to detail; kebab **View** action before Edit.
- [x] Absorb/replace `StrandsDialog.razor` / `LessonsDialog.razor` (deleted stubs;
  `StrandsEditor.razor` + `LessonsEditor.razor` inline components).
- [x] bUnit tests (`GradeLevelDetailPageTests.cs`, 7 tests; landing wiring in
  `GradeLevelsLandingDetailWiringTests.cs`).

### Step 6 - Create.razor / Edit.razor — **[done]**
- [x] `GradeLevels/Create.razor`, `GradeLevels/Edit.razor` routable pages (topics
  `FluentListbox` multi-select; topic diff on submit; navigate to Detail).
- [x] Extend `GradeLevelFormFields.razor` with `TopicsSection` RenderFragment + `OnCodedValuePicked`.
- [x] bUnit tests (`GradeLevelCreateEditPageTests.cs`, 3 tests: Create render, Edit
  load, Edit not-found).

### Step 7 - Full build + all test suites green — **[done]**
- [x] Students.Core + Students.Api + Students.Application build green.
- [x] Full solution build green (0 errors, 2 warnings).
- [x] `SchoolCollab.Students.Tests.Unit` — 172/172 passed.
- [x] `SchoolCollab.ArchitectureTests.Unit` — 13/13 passed (seed CSV + tenant-filter audit).
- [x] `SchoolCollab.Students.Api.Tests.Unit` — 1/1 passed.
- [x] `SchoolCollab.Admin.Tests.Unit` — 245/245 passed (11 new: 7 Detail page,
  3 Create/Edit page, 1 landing Name-anchor wiring).
- [x] Integration suite: 27/28 pass; `TopicsByGradeEndpointErrorMappingTests.
  WithExplicitEffectiveDate_FiltersToThatDate` fails **on clean base HEAD too**
  (confirmed via isolated git worktree) — pre-existing, unrelated to this pass.
- [ ] Playwright suite not run for this pass.

### Stacked PR rollout (gh-stack) — 2026-08-05
All rollout steps are complete and shipped as a single linked **gh-stack (stack #118)**, one
branch/PR per layer, each PR diff scoped to its phase and based on the PR below it. Merge
bottom-up with `gh stack merge <n> --yes` (all-or-nothing).

| PR | Branch | Rollout steps | Status |
|----|--------|---------------|--------|
| #113 | `stack/1-tchroles-seed` | Step 1 (TCHROLES seed) | **open** |
| #114 | `stack/2-backend-teacher-role-rename` | Steps 2-3 (role + rename) | **open** |
| #115 | `stack/3-admin-client` | Step 4 (client methods) | **open** |
| #116 | `stack/4-detail-landing` | Step 5 (Detail + landing) | **open** |
| #117 | `stack/5-create-edit` | Step 6 (Create/Edit) | **open** |

- Setup: `gh extension install github/gh-stack`; `git config rerere.enabled true` +
  `remote.pushDefault origin`. Skill at `~/.agents/skills/gh-stack`.
- Each branch tip builds green (0 errors) and the 11 new bUnit tests pass at the stack tip.
- Commit identity: `pi-agent` / `bot@school-collab.local` (configured repo default).
- To update a layer: `gh stack checkout <layer>` -> edit/commit -> `gh stack rebase --upstack`
  -> `gh stack push`; then `gh stack sync` to refresh PRs.

### Notes / decisions captured during the pass
- Per the "keep UI labels as-is" instruction, the admin client retains the visible
  **"Subjects"** wording while the backing entity/API surface is fully `Topic`-named.
- `GradeLevelRoutes.cs` `GET /grade-levels/{id}/teachers` returns
  `TeacherWithRoleDto[]` (`TeacherDto` fields + `TeacherRoleCodedValueId` +
  `AssignedTopics: TopicDto[]`).
- Known pre-existing integration failure (NOT this pass): `TopicsByGradeEndpointErrorMappingTests.
  WithExplicitEffectiveDate_FiltersToThatDate` ("body to be empty, but found items") reproduces on
  clean base HEAD via isolated git worktree. Integration tests share a persistent Postgres DB,
  so OutboxDispatcher background noise (`outbox_messages does not exist`, `PeriodOverlapException`)
  also appears intermittently.

