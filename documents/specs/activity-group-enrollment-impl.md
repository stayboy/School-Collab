# Activity Group Enrollment — Implementation Checklist

> Tracked checklist for `activity-group-enrollment.md` (post-review, dated 2026-07-29).
> One PR per step, each shippable behind `FEATURE:EnableActivityGroups` (flag OFF until Phase 6).
> Update each box to `[x]` when the PR merges; add a PR link/SHA in the **PR** column or a note line.

**Status legend:** `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked

---

## Prerequisite dependency

- [ ] **Subject polymorphism** (`subject-to-topic-polymorphism.md`) — `Subject` gains `OwnerType`/`OwnerId`; `GradeSubjectAssignment` eliminated. Steps 16 and 27 assume this is done. **Coordinate phasing with that spec's implementation.**

---

## Phase 1 — Students domain model (dark, flag OFF)

- [x] **1.1** Add feature flag constant `EnableActivityGroups` in `src/SchoolCollab.Core/Features/FeatureFlagKeys.cs`. Seeded via `MigrationService/Program.cs` `SeedEnableActivityGroupsAsync` (default OFF), registered in `SchoolCollab.Admin/appsettings.json` FeatureFlags.FEATURE. (AppHost `Parameters` fan-out was deprecated in favour of the central Settings FeatureFlag aggregate — see `settings-context-merge-spec.md`.) — *NFR-11*
- [x] **1.2** Create `ActivityGroup` entity + `ActivityGroupStatus` enum (`Active`, `Suspended`, `Archived`) in `Students.Core/Domain/`. Extends `TenantEntityTypeConfigurationBase<ActivityGroup>`. Mirrors `GradeLevel.cs` (Create/Update/Delete + Suspend/Archive/Reactivate lifecycle). **No teacher-leader field** — `LeaderTeacherId`/`leader_teacher_id` removed per OS-10 (future feature) and taken out of the entity, config, DTO, API, and migration. — *FR-1..3, AC-1..6*
- [x] **1.3** Create `ActivityGroupMembership` entity + `MembershipStatus` enum (`Active`, `Exited`, `Removed`) + `ExitedOn`. Partial unique index on `(tenant_id, student_id, activity_group_id)` filtered to `status = 0` via `.HasFilter("status = 0")` (matches repo's unquoted snake_case convention). `transfer_reason` column omitted (spec review finding #9: undocumented carryover from `StudentEnrollment`, no FR uses it). — *FR-7..11, AC-7..11*
- [x] **1.4** Set `activity_group_id` FK `ON DELETE RESTRICT` in membership configuration (`OnDelete(DeleteBehavior.Restrict)`). `student_id` FK also RESTRICT (preserve membership history on soft-delete — EC-2). — *FR-6, NFR-8, AC-18*
- [x] **1.5** Add DbSets + repositories to `Students.Core/Data/StudentsDbContext.cs` + `Data/Repositories/*` (IActivityGroupRepository, ActivityGroupRepository, IActivityGroupMembershipRepository, ActivityGroupMembershipRepository). DTOs: ActivityGroupDto, MembershipDto. — *§10*
- [x] **1.6** Generate additive migration `20260801084654_AddActivityGroups.cs`. Case-insensitive unique name index `(tenant_id, lower(name))` via raw SQL (EF Core can't express `lower()` — mirrors CodedValue COALESCE pattern). `CHECK (capacity IS NULL OR capacity >= 1)` via raw SQL. `NoUncommittedModelChanges` test passes. — *NFR-9*
- [x] **1.7** Unit-test entity invariants (group create/archive/suspend/reactivate, membership status transitions, multi-membership allowed, rejoin-after-exit, domain events). 27 new tests, all passing. DB/handler-dependent tests (duplicate-name, delete-guard, capacity count, concurrency) deferred to Phase 2 with clear comments. — *AC-1..11*

---

## Phase 2 — Membership commands/queries + APIs (dark, flag OFF)

- [x] **2.1** Group CQRS commands: `CreateActivityGroup`, `UpdateActivityGroup`, `ArchiveActivityGroup`, `SuspendActivityGroup`, `DeleteActivityGroup` (`Students.Core/CQRS/ActivityGroups/Commands/`). — *FR-1..6*
- [x] **2.2** Wire cross-context delete-guard port into `DeleteActivityGroupHandler`:
  - create `Students.Core/Services/IActivityGroupAssignmentQuery.cs`
  - implement `Students.Api/Services/ActivityGroupAssignmentQueryHttpClient.cs` as HTTP client calling Assignments API `GET /api/activity-groups/{id}/assignments` (mirror existing `Assignments.Api/Services/StudentsContactResolver.cs`, reversed direction)
  - handler rejects delete on (a) any membership row, or (b) any returned `Draft`/`Published` assignment → `ActivityGroupReferencedException`
  - **fail-closed** if Assignments API unreachable
  - **confirm `AssignmentSummary` DTO carries `Status`** before wiring — *FR-6, AC-17, AC-18, EC-1*
- [x] **2.3** Membership CQRS: `AddMembership`, `RemoveMembership`, `ExitMembership` commands; `GetGroupMembers`, `GetStudentGroups` queries (`Students.Core/CQRS/ActivityGroups/`). — *FR-7..16*
- [x] **2.4** Students API endpoints: `Students.Api/Endpoints/MapActivityGroupEndpoints` (mirror `GradeLevelRoutes.cs`). Gate every endpoint behind `FEATURE:EnableActivityGroups`. — *§7.1, §7.2, NFR-11*
- [x] **2.5** Unit-test all handlers (AC-12..19, AC-17, AC-18, EC-1..6). — *AC-12..19, EC-1..6*

---

## Phase 3 — Assignment↔group link + publish wiring (dark, flag OFF)

- [ ] **3.1** Create `AssignmentActivityGroup` link entity + config + `AssignmentsDbContext` DbSet + additive migration `<ts>_AddAssignmentActivityLinks.cs`. Update `NoUncommittedModelChanges` test for `AssignmentsDbContext`. — *FR-17, §8.3, NFR-9*
- [ ] **3.2** `LinkAssignmentGroups` command handler — replace-set semantics; enforce same-tenant, non-archived; reject archived-group links. — *FR-18, FR-19, AC-20, AC-21*
- [ ] **3.3** Add Assignments API link endpoints: `PUT`/`GET /api/assignments/{assignmentId}/groups` and `GET /api/activity-groups/{groupId}/assignments` (§7.3). The last is what step 2.2's port consumes. — *§7.3, AC-20*
- [ ] **3.4** Extend publish recipient resolution for `SelectedGroups`: in `PublishAssignmentCommandHandler`, when `TargetAudienceType = SelectedGroups` (enum `2`), call `IContactResolver.ResolveSubscribersRequest` with the **`StudentIds` roster of active members of linked groups** (the `StudentIds` param already exists in `IContactResolver`). Stop hardwiring `assignment.GradeLevelId` for this audience. Generate `AssignmentRecipient` rows. — *FR-20..23, AC-22..26*
- [ ] **3.5** Unit-test: link-set replace, cross-tenant rejection, archived-group exclusion, publish-resolves-only-active-members. — *AC-22..26, EC-7..14*

---

## Phase 4 — Admin UI: ActivityGroups pages (dark, flag OFF)

- [ ] **4.1** `ActivityGroups` list page in `Students.Admin/Components/Pages/ActivityGroups/*`, mirror `GradeLevels` (FluentUI, keyboard nav, ARIA — NFR-10). — *FR-24..27*
- [ ] **4.2** `ActivityGroupCreateEditDialog` + `ActivityGroupDetails` page with members tab. Follow repo `fluentui-dialog-shell` / `dialog-ui` skill conventions for modal forms. — *FR-24..27*
- [ ] **4.3** Gate all UI by `FEATURE:EnableActivityGroups` (repo tenant-gate / feature-flag pattern per `featureflags-tenant-gates` skill). — *NFR-11*
- [ ] **4.4** bUnit tests: CRUD + members tab. — *AC-12..19*

---

## Phase 5 — Student Detail page UI (dark, flag OFF)

- [ ] **5.1** Add "Activity Groups" section to `Students.Admin/Components/Pages/Students/Detail.razor` (FR-28). `StudentDetailSectionsTests.cs` already has red-phase tests asserting Join Group/Leave buttons gated on `EnableActivityGroups` — make them pass. — *FR-28, AC-27*
- [ ] **5.2** `JoinGroupDialog` (`Students.Admin/Components/ActivityGroups/JoinGroupDialog.razor`), searchable multi-select, using `dialog-ui` skill conventions. — *FR-29, FR-30, AC-28, AC-29*
- [ ] **5.3** "Leave" action button per membership row + empty-state message when no memberships. — *FR-31, FR-32, AC-30, AC-31*
- [ ] **5.4** bUnit tests (AC-27..31). — *AC-27..31*

---

## Phase 6 — Flag flip + pilot rollout (dark→lit)

- [ ] **6.1** Default `FEATURE:EnableActivityGroups` ON in `appsettings.PilotTenant.json`; update `documents/configuration.md` §2. — *NFR-11*
- [ ] **6.2** Playwright smoke: create "Chess Club" → add 3 students → create group-scoped polymorphic `Subject` (`OwnerType = ActivityGroup`) → create `SelectedGroups` assignment linked to club + that subject → publish → assert only club members' subscribed contacts received it. — *§11*
- [ ] **6.3** Monitor pilot tenant 1 week before broader rollout.

---

## Cross-cutting / don't-forget

- [ ] **Closed-assignment link cleanup** — no automatic unlink needed (EC-12: benign dangling refs); `PUT /api/assignments/{id}/groups` (step 3.3) is the manual escape hatch.
- [ ] **Missing referenced docs** (not implementation blockers, but cited by the spec): `centralized-feature-flags.md`, `ef-migrations.md`, `auth-tenancy-pattern.md`, `endpoint-organization-pattern.md` — either draft them or repoint the spec to the real mechanism.

---

## Notes / change log

- _Checklist generated from spec review (post-edit of items 1–7 + FR-35 cleanup). Spec source of truth: `activity-group-enrollment.md`._
