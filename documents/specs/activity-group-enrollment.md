# Spec: Activity-Group Enrollment & Assignment Targeting

> **Status:** Approved
> **Author:** Cline (spec-driven-workflow)
> **Date:** 2026-07-29
> **Reviewers:** Students context owner, Assignments context owner, Architecture
> **Owner contexts:** `SchoolCollab.Students.Core`, `SchoolCollab.Assignments.Core`,
> `SchoolCollab.Students.Admin`, `SchoolCollab.Assignments.Admin`
> **Depends on:** `grade-level-setup.md`, `global-tenant-filter.md`,
> `auth-tenancy-pattern.md`, `ef-migrations.md`, `endpoint-organization-pattern.md`

---

## 0. Decisions locked in this revision

1. **Create a NEW entity pair — do NOT adapt `StudentEnrollment`.** Activity
   groups get their own `ActivityGroup` (group definition) and
   `ActivityGroupMembership` (student↔group link) entities, both in the
   **Students** bounded context. `StudentEnrollment` stays untouched.
   Rationale in §2.3. This keeps the change **additive / non-breaking**
   (NFR-6) and fulfills the already-existing
   `TargetAudienceType.SelectedGroups = 2` placeholder rather than fighting it.
2. **Multi-membership is allowed.** A student MAY be an active member of one or
   more activity groups at the same time. This is the opposite of grade
   enrollment's single-active rule, which is exactly why the two must be
   separate entities (FR-6, FR-9).
3. **Group lifecycle is independent of `Period`.** A group MAY optionally be
   associated with a `Period` (`PeriodId` nullable) but is not required to be,
   and MAY outlast a period (FR-3, FR-4, FR-10). This directly satisfies the
   requirement that activity groups "may outlast grade enrollment."
4. **Audience type is mutually exclusive per assignment.** An assignment picks
   exactly one of `AllStudents | SelectedGrades | SelectedGroups` — matching
   the existing closed-set `TargetAudienceType` enum semantics. A *single*
   assignment simultaneously targeting a grade **and** a group requires a
   unified `AssignmentTarget` table, which is a **breaking change** to the
   existing scalar `Assignment.GradeLevelId` and is therefore **out of scope**
   for this spec (OS-1, OS-2). The system as a whole supports grades **and**
   groups (an assignment may target grades, or groups, or all students).
5. **Assignment↔group is many-to-many** via a new `AssignmentActivityGroup`
   link table in the **Assignments** context (FR-17). `SelectedGrades`
   continues to use the existing scalar `Assignment.GradeLevelId` — unchanged.

6. **Recipient resolution reuses the existing publish pipeline.** Group-targeted
   publish resolves group members → their subscribed contacts →
   `AssignmentRecipient` rows, exactly as grade-targeted publish does today
   (FR-20). No new notification channel.
7. **Hard delete is referentially guarded.** `ActivityGroup.Delete()` mirrors
   `GradeLevel.Delete()` — the repository rejects the delete when the group has
   any `ActivityGroupMembership` row (any status; the `activity_group_id` FK
   is `ON DELETE RESTRICT`, preserving history — NFR-8) OR when a
   `Draft`/`Published` assignment references the group (FR-6, EC-1), throwing
   an `ActivityGroupReferencedException`. The cross-context assignment check
   is obtained via the Assignments-API endpoint in §7.3 — the Students context
   cannot query the Assignments `DbContext` directly.
8. **Group membership validation does NOT apply grade age/gender specs.**
   `ActivityGroupMembership` only checks student existence, tenant match, and
   non-deleted status (FR-16). Age/gender restrictions are a grade-enrollment
   concern and do not apply to extracurricular groups.
9. **Activity Groups use the existing `Subject` entity (polymorphic).**
   Activity Groups do NOT have a separate `ActivityList` entity — instead,
   the existing `Subject` entity is made polymorphic via `OwnerType`
   (GradeLevel | ActivityGroup) and `OwnerId`. An assignment targeting a
   group MUST link to at least one group (FR-23) and MUST have a
   `SubjectId` whose `OwnerType = ActivityGroup` and `OwnerId` matches a
   linked group (per `subject-to-topic-polymorphism.md` FR-15). The
   `Subject` entity carries strands and lessons uniformly for both owner
   types. See `subject-to-topic-polymorphism.md` for the full design.

These decisions resolve the user's explicit question ("adapt grade enrollment
or create a new entity?") in favour of **a new entity**, with reasoning in §2.3.

## 1. Goal

Introduce **extracurricular activity groups** as a second grouping mechanism
alongside grade levels, let students belong to one or more such groups
(independent of their single grade enrollment), and let assignments be targeted
at the members of those groups — scoped to specific **subjects** (the
existing `Subject` entity, now polymorphic). Activity groups may outlast
grade enrollment periods. Unlike grade enrollment (single-active), a student
MAY belong to one or more activity groups simultaneously. Activity groups
use the polymorphic `Subject` entity for categorisation — see
`subject-to-topic-polymorphism.md`.

---

## 2. Context

### 2.1 Why this feature exists

School-Collab currently enrols students in **grade levels**, which act as the
grouping unit for assignments. An assignment's `TargetAudienceType` can be
`AllStudents` or `SelectedGrades` (backed by `Assignment.GradeLevelId`).
However, schools also run **extracurricular activity groups** (clubs, sports
teams, debate, music, etc.) that are *not* grade levels. Today there is no way
to assign work to those groups: the `TargetAudienceType.SelectedGroups = 2`
enum value exists in `TargetAudienceType.cs` but has **no backing entity, no
data model, and no UI**. It is an unfulfilled placeholder.

### 2.2 The two differences from grade enrollment

1. **Lifecycle.** Grade enrollment is bound to an academic `Period`
   (`StudentEnrollment.PeriodId` is `IsRequired()`, and
   `SingleActiveEnrollmentSpecification` enforces one active grade enrollment
   *cross-period*). Activity groups **outlast** a single period — a chess club
   runs across terms and academic years. A group therefore needs its own
   lifecycle, only *optionally* associated with a period.
2. **Cardinality.** A student holds **one** active grade enrollment at a time
   (enforced by the DB unique index `(tenant_id, student_id, period_id)` in
   `StudentEnrollmentConfiguration`). Activity groups require
   **multi-membership**: a student may be in the chess club, the choir, and the
   robotics team simultaneously. Reusing `StudentEnrollment` would require
   deleting its unique index and its single-active specification — destroying
   the grade invariant to accommodate the group invariant.

### 2.3 Decision: adapt vs. new entity

**Adapting `StudentEnrollment`** would mean: making `GradeLevelId` nullable,
adding a polymorphic target column, *removing* the
`(tenant_id, student_id, period_id)` unique index, *removing* the
`SingleActiveEnrollmentSpecification`, and type-branching every enrollment
query/spec on "is this a grade or a group?" It would also drag grade-only
guard clauses (`AgeRangeSpecification`, `GenderRestrictionSpecification`) into
a code path where they must be conditionally skipped. This is a **breaking
change** to an existing entity, its DB schema, its unique constraint, its
specification chain, and its existing tests — an explicit escalation trigger
under the bounded-autonomy rules, with no compensating benefit.

**Creating a new entity pair** (`ActivityGroup` + `ActivityGroupMembership`)
preserves every grade-enrollment invariant unchanged, gives groups their own
multi-membership + period-independent rules, and is purely additive (new
tables, one new optional link table, one new optional assignment field). It
also turns the dormant `SelectedGroups` enum value into a real feature.

**Decision: create new entities (Option B).** See §0 decision 1.

### 2.4 Success looks like

- An admin can create an activity group, add/remove students (multiple at once,
  multi-membership), and the group persists across periods.
- A teacher can create an assignment with `TargetAudienceType = SelectedGroups`,
  link one or more groups, optionally scope to specific subjects (via the
  polymorphic `Subject` entity), publish it, and only the active members
  of those groups (or specifically-scoped subjects) receive it via their
  subscribed contacts.
- Grade enrollment and its single-active invariant are completely unaffected.
- The student Detail page shows the student's activity groups and lets the
  admin join/leave groups from there.

---

## 3. Functional Requirements

> RFC 2119 keywords (MUST / MUST NOT / SHOULD / MAY) are used precisely.
> Each requirement is atomic and testable. IDs: `FR-N`.

### 3.1 Activity group lifecycle

- **FR-1** — The system MUST allow a tenant admin to create an `ActivityGroup`
  with a unique-within-tenant `Name`, an optional `Description`, an optional
  `Category` (free text, max 100 chars), an optional `PeriodId` (FK →
  `Period.Id`), and an optional integer `Capacity` (max members).
- **FR-2** — `ActivityGroup` MUST be a strict tenant entity
  (`ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion`), inheriting the
  tenant via `TenantEntityTypeConfigurationBase<T>` exactly as `GradeLevel`
  does.
- **FR-3** — `ActivityGroup` MUST have its own `ActivityGroupStatus`
  (`Active=0, Suspended=1, Archived=2`) lifecycle independent of `PeriodStatus`.
  A group MUST be creatable as `Active` without an active period existing.
- **FR-4** — `ActivityGroup` MAY be associated with a `Period`
  (`PeriodId` nullable); absence MUST NOT block creation or membership.
- **FR-5** — The system MUST support `Update` (name, description, category,
  capacity, period association) and a referentially-guarded `Delete`
  for `ActivityGroup`, mirroring `GradeLevel.Update`/`GradeLevel.Delete`.
- **FR-6** — `Delete` MUST be rejected (throwing
  `ActivityGroupReferencedException`) when EITHER of the following holds:
  (a) the group has **any** `ActivityGroupMembership` row (Active, Exited, or
  Removed) — the `activity_group_id` FK is `ON DELETE RESTRICT` so membership
  history is preserved (NFR-8); OR
  (b) an assignment whose `Status` is `Draft` or `Published` references the
  group. Because assignments live in the **Assignments** bounded context
  (`assignment_activity_groups` is an operational ref with no cross-context DB
  FK — §8.3), `DeleteActivityGroupHandler` (Students context) MUST obtain
  this check via the existing Assignments-API endpoint
  `GET /api/activity-groups/{id}/assignments` (§7.3) through a cross-context
  port (e.g. `IActivityGroupAssignmentQuery`, implemented in `Students.Api`
  as an HTTP client, mirroring the existing `StudentsContactResolver` pattern
  in `Assignments.Api`) and reject if any returned assignment is
  `Draft`/`Published`. The check is **fail-closed**: if the Assignments API
  is unreachable the delete is rejected. To retire a referenced group, use
  `Archive` (FR-3, Q3).

### 3.2 Activity group membership

- **FR-7** — The system MUST allow adding a student to an `ActivityGroup`,
  creating an `ActivityGroupMembership` (`StudentId`, `ActivityGroupId`,
  `JoinedOn`, optional `ExitedOn`, `MembershipStatus`).
- **FR-8** — `MembershipStatus` MUST be `Active=0, Exited=1, Removed=2`.
- **FR-9** — A student MUST be permitted to be an active member of **one or
  more** activity groups simultaneously (multi-membership). This MUST NOT be
  constrained by the student's grade enrollment or by any single-active rule.
- **FR-10** — The system MUST enforce at most one **active** membership per
  `(tenant_id, student_id, activity_group_id)` via a partial unique index on
  rows where `Status = Active`. Re-joining a previously exited group MUST set
  the old row to `Exited`/`Removed` (or reuse via a new active row) without
  violating the unique constraint.
- **FR-11** — The system MUST reject membership for a `Student` that is
  soft-deleted (`IsDeleted = true`), belongs to a different tenant, or does not
  exist.
- **FR-12** — The system MUST reject membership for an `ActivityGroup` whose
  `Status` is `Archived`.

- **FR-13** — If `ActivityGroup.Capacity` is set and the count of active
  members is >= `Capacity`, the system MUST reject a new active membership with
  a `GroupAtCapacityException`. If `Capacity` is null, no limit is enforced.
- **FR-14** — The system MUST support removing a member (`Remove`) and a member
  exiting (`Exit`), both setting `ExitedOn` and moving `Status` to `Removed` or
  `Exited` respectively, and emitting a domain event.
- **FR-15** — `ActivityGroupMembership` MUST be a strict tenant entity with
  `IHasRowVersion` (PostgreSQL `xmin`) and audit properties, consistent with
  `StudentEnrollment`.
- **FR-16** — Membership validation MUST NOT apply `AgeRangeSpecification` or
  `GenderRestrictionSpecification` (those are grade-enrollment-only).

### 3.3 Assignment targeting

- **FR-17** — The Assignments context MUST provide an `AssignmentActivityGroup`
  link entity (`AssignmentId`, `ActivityGroupId`, tenant, audit) implementing a
  many-to-many relationship between `Assignment` and `ActivityGroup`.
- **FR-18** — When `Assignment.TargetAudienceType = SelectedGroups`, the
  assignment MUST target the members of the group(s) linked via
  `AssignmentActivityGroup`. The audience type MUST remain mutually exclusive
  per assignment (one of `AllStudents | SelectedGrades | SelectedGroups`),
  matching the existing closed-set enum.
- **FR-19** — `SelectedGrades` MUST continue to use the existing scalar
  `Assignment.GradeLevelId` unchanged. This spec MUST NOT alter
  `Assignment.GradeLevelId`, its index, or the `SelectedGrades` code path.
- **FR-20** — At publish time, for a `SelectedGroups` assignment the system
  MUST resolve the active members of each linked group, then resolve each
  member's subscribed contacts, and create `AssignmentRecipient` rows — reusing
  the existing recipient-resolution pipeline used for grade-targeted publishes.
- **FR-21** — The system MUST reject linking a group from a different tenant
  than the assignment (tenant mismatch → `ArgumentException`/400).
- **FR-22** — The system MUST reject linking an `Archived` group to an
  assignment.
- **FR-23** — An assignment with `TargetAudienceType = SelectedGroups` MUST
  have at least one linked group before it can be published; publishing with
  zero linked groups MUST be rejected with a clear validation error (EC-7).

### 3.4 Reads / queries

- **FR-24** — The system MUST expose queries: list activity groups
  (tenant-scoped, filterable by status/period), list active members of a group,
  list the groups a given student is an active member of, and list the activity
  lists of a given group.
- **FR-25** — The system MUST expose a query listing assignments targeting a
  given activity group.
- **FR-26** — Group and membership list endpoints MUST be tenant-filtered via
  the global tenant filter (`global-tenant-filter.md` §3.2 Strict).

### 3.5 Domain events

- **FR-27** — Creating a group, adding/removing/exiting a member, and
  archiving/suspending a group MUST each emit a corresponding domain event
  (e.g. `ActivityGroupCreatedEvent`, `ActivityGroupMemberAddedEvent`) following
  the existing `IDomainEvent` + `_domainEvents` + `ClearDomainEvents()` pattern.

### 3.6 Student Detail page UI

- **FR-28** — The student Detail page (`Detail.razor`) MUST display an
  "Activity Groups" section below the existing Enrollments section, listing the
  groups the student is an active member of. Each row MUST show the group name,
  joined-on date, and status (`Active` badge or `Exited`/`Removed`).
- **FR-29** — The "Activity Groups" section header MUST contain a "Join Group"
  accent button that opens a group-membership dialog.
- **FR-30** — The group-membership dialog MUST present a searchable, pickable
  list of available activity groups in the same tenant (Active status, not at
  capacity, excluding groups the student is already an active member of). The
  user MUST be able to select one or more groups and submit to join them in a
  single operation. The dialog MUST handle partial failures: accepted groups
  create memberships, failed ones are reported per-group.
- **FR-31** — Each active membership row MUST include a "Leave" lightweight
  button that exits the student from that group.
- **FR-32** — The section MUST show an appropriate empty state message when
  the student has no active memberships.

### 3.7 Activity-group subjects (polymorphic Subject)

> Activity Groups do NOT have a separate `ActivityList` entity. The existing
> `Subject` entity is made polymorphic (`OwnerType` + `OwnerId`) to support
> both grade-level and activity-group ownership. See
> `subject-to-topic-polymorphism.md` (§0 decisions 1-7, FR-1..12) for the
> full design.

- **FR-33 (superseded)** — Replaced by `subject-to-topic-polymorphism.md`
  FR-1..4. The `Subject` entity gains `OwnerType` (GradeLevel | ActivityGroup),
  `OwnerId`, optional `PeriodId`, `Description`, `DefaultStrandId`, and
  `DefaultLessonId`. `Code` and `CodedValueId` become nullable.
- **FR-34 (superseded)** — Replaced by `subject-to-topic-polymorphism.md`
  FR-13..15. An assignment with `SelectedGroups` MUST have a `SubjectId`
  whose `OwnerType = ActivityGroup` and `OwnerId` matches a linked group.
  The `AssignmentActivityGroup` link table is **retained** (it is NOT
  eliminated — `subject-to-topic-polymorphism.md` FR-15/FR-17 keep it; only
  the separate `AssignmentActivityList` link table from the old §7.5 design
  is eliminated by that spec).
- **FR-35 (superseded)** — Replaced by `subject-to-topic-polymorphism.md`
  §7.1. The Subjects admin page gains an ActivityGroup filter. The existing
  Subjects admin page (`Subjects.razor`, `SubjectCreateDialog.razor`) is
  extended rather than introducing a separate activity-list UI (the
  standalone `ActivityList` concept is eliminated by
  `subject-to-topic-polymorphism.md`).

---

## 4. Non-Functional Requirements

> All thresholds are measurable. IDs: `NFR-N`.

- **NFR-1 (Performance — list members)** — Listing the active members of a
  group MUST return p95 < 300 ms for a group of 5,000 members, measured against
  a warmed PostgreSQL instance on the standard dev hardware.
- **NFR-2 (Performance — recipient resolution)** — Resolving recipients for a
  `SelectedGroups` publish MUST complete p95 < 2 s for 10,000 total active
  members across the linked groups.
- **NFR-3 (Indexing)** — All hot-path indexes MUST lead with `tenant_id`,
  matching the existing convention (`ix_student_enrollments_tenant_*`).
  Specifically: `(tenant_id, activity_group_id, status)` on memberships and
  `(tenant_id, activity_group_id)` on the assignment link.
- **NFR-4 (Concurrency)** — `ActivityGroup` and `ActivityGroupMembership` MUST
  use PostgreSQL `xmin` row versioning via `IHasRowVersion` +
  `ConfigurePostgresRowVersion()`. A conflicting write MUST throw the existing
  `ConcurrencyException`.
- **NFR-5 (Tenancy)** — All group/membership/link reads and writes MUST be
  strict-tenant-filtered (`global-tenant-filter.md` §3.2 Strict). No
  cross-tenant group or membership access is permitted.
- **NFR-6 (Non-breaking migration)** — The migration MUST be purely additive:
  new tables `activity_groups`, `activity_group_memberships`,
  `assignment_activity_groups`;
  no alteration, drop, or type change to `student_enrollments`, `assignments`,
  or any existing column/index. The existing `NoUncommittedModelChanges` guard
  MUST continue to pass for both contexts.
- **NFR-7 (Auditability)** — `CreatedAt` / `UpdatedAt` MUST be present and
  populated on all new entities.
- **NFR-8 (Deletion semantics)** — `ActivityGroup` uses a **referentially
  guarded hard delete** (like `GradeLevel`): delete is blocked when any
  membership row exists (the `activity_group_id` FK is `ON DELETE RESTRICT`,
  preserving history) or when a `Draft`/`Published` assignment references the
  group (FR-6). `ActivityGroupMembership` uses **status transitions**
  (`Active → Exited|Removed`) plus `ExitedOn`; membership rows are not
  hard-deleted in normal operation (preserving history). To retire a group
  with history or live assignment links, use `Archive` (FR-3, Q3).
- **NFR-9 (Migration guard)** — A `NoUncommittedModelChanges` unit test MUST be
  added/updated for `StudentsDbContext` and `AssignmentsDbContext` per
  `ef-migrations.md`.
- **NFR-10 (Accessibility)** — Group management UI MUST reuse existing FluentUI
  components, be fully keyboard navigable, and use ARIA labels consistent with
  the `GradeLevels` page. WCAG 2.1 AA target (inherited from the app baseline).
- **NFR-11 (Feature flag)** — The group-management surface MUST be gated behind
  a centralized feature flag `FEATURE:EnableActivityGroups` (fan-out via
  AppHost `Parameters`, per `centralized-feature-flags`), defaulting OFF, so
  the feature ships dark.

## 5. Acceptance Criteria

> Given / When / Then. Every criterion references at least one `FR-*` or
> `NFR-*`. IDs: `AC-N`.

- **AC-1 (create group)** — **Given** an admin in tenant T with
  `FEATURE:EnableActivityGroups` on, **When** they create an `ActivityGroup`
  named "Chess Club" with no period, **Then** a group row is persisted with
  `Status = Active`, `TenantId = T`, `PeriodId = null`, and a
  `ActivityGroupCreatedEvent` is raised. *(FR-1, FR-2, FR-3, FR-4, FR-27)*
- **AC-2 (create without active period)** — **Given** tenant T has **no**
  active `Period`, **When** the admin creates a group, **Then** creation
  succeeds (no period required). *(FR-3, FR-4)*
- **AC-3 (duplicate group name rejected)** — **Given** tenant T already has a
  group "Chess Club", **When** the admin creates another group "Chess Club" in
  T, **Then** the request is rejected with a 409/validation error. *(FR-1)*
- **AC-4 (multi-membership allowed)** — **Given** student S is an active member
  of group G1, **When** S is added to group G2, **Then** both memberships are
  `Active` and no exception is raised. *(FR-7, FR-9)*
- **AC-5 (duplicate active rejected)** — **Given** student S is an active
  member of group G, **When** S is added to G again while still active,
  **Then** the request is rejected by the partial unique constraint. *(FR-10)*
- **AC-6 (rejoin after exit)** — **Given** student S exited group G
  (`Status = Exited`), **When** S is re-added to G, **Then** a new active
  membership is created and the unique constraint is not violated. *(FR-8,
  FR-10, FR-14)*
- **AC-7 (deleted student rejected)** — **Given** student S is soft-deleted,
  **When** S is added to any group, **Then** the request is rejected. *(FR-11)*
- **AC-8 (archived group blocks membership)** — **Given** group G is
  `Archived`, **When** a student is added to G, **Then** the request is
  rejected. *(FR-12)*
- **AC-9 (capacity enforced)** — **Given** group G has `Capacity = 20` and 20
  active members, **When** a 21st student is added, **Then** the request is
  rejected with `GroupAtCapacityException`. *(FR-13)*
- **AC-10 (null capacity = unlimited)** — **Given** group G has
  `Capacity = null`, **When** more than any fixed number of students are added,
  **Then** all are accepted. *(FR-13)*
- **AC-11 (no age/gender check)** — **Given** a student whose age/gender would
  fail the grade `AgeRangeSpecification`/`GenderRestrictionSpecification`,
  **When** the student is added to an activity group, **Then** the membership is
  accepted (those specs are not applied). *(FR-16)*
- **AC-12 (assignment targets groups)** — **Given** an assignment A with
  `TargetAudienceType = SelectedGroups` linked to groups G1 and G2, **When** A
  is published, **Then** `AssignmentRecipient` rows are created only for the
  active members of G1 ∪ G2 who have subscribed contacts. *(FR-17, FR-18,
  FR-20)*
- **AC-13 (publish with zero groups rejected)** — **Given** assignment A has
  `TargetAudienceType = SelectedGroups` but no linked groups, **When** A is
  published, **Then** the publish is rejected with a validation error.
  *(FR-23, EC-7)*
- **AC-14 (cross-tenant link rejected)** — **Given** assignment A in tenant T1,
  **When** a group from tenant T2 is linked to A, **Then** the link is
  rejected. *(FR-21, NFR-5)*
- **AC-15 (archived group link rejected)** — **Given** group G is `Archived`,
  **When** G is linked to an assignment, **Then** the link is rejected.
  *(FR-22)*
- **AC-16 (grade path unchanged)** — **Given** an assignment with
  `TargetAudienceType = SelectedGrades`, **When** it is created/published,
  **Then** behaviour is identical to today (`GradeLevelId` used, no group
  involvement). *(FR-19, NFR-6)*

- **AC-17 (referential delete guard — assignments)** — **Given** group G is
  referenced by a `Draft` or `Published` assignment, **When** the admin
  deletes G, **Then** the delete is rejected with
  `ActivityGroupReferencedException` (the Students delete handler obtains the
  check via the Assignments-API endpoint in §7.3 — the Students context
  cannot query the Assignments `DbContext` directly). *(FR-6, FR-17)*
- **AC-18 (delete guard on membership history)** — **Given** group G has
  any membership row (Active, Exited, or Removed) and is not referenced by
  any assignment, **When** the admin deletes G, **Then** the delete is
  rejected with `ActivityGroupReferencedException` (the `activity_group_id`
  FK is `ON DELETE RESTRICT`, preserving history). *(FR-6, NFR-8)*
- **AC-19 (group outlasts period)** — **Given** group G is associated with
  period P and P transitions to `Completed`/`Archived`, **When** members are
  queried/added, **Then** G remains `Active` and membership operations succeed.
  *(FR-3, FR-4, FR-10, EC-8)*
- **AC-20 (list queries tenant-scoped)** — **Given** tenants T1 and T2 each
  have groups, **When** an admin in T1 lists groups, **Then** only T1's groups
  are returned. *(FR-24, FR-26, NFR-5)*
- **AC-21 (performance — list members)** — **Given** a group with 5,000 active
  members, **When** the members list endpoint is called, **Then** p95 response
  time is < 300 ms. *(NFR-1, NFR-3)*
- **AC-22 (performance — recipient resolution)** — **Given** a
  `SelectedGroups` assignment linked to groups totalling 10,000 active members,
  **When** it is published, **Then** recipient resolution completes p95 < 2 s.
  *(NFR-2)*
- **AC-23 (concurrency conflict)** — **Given** two concurrent edits to the same
  membership, **When** both are saved, **Then** the second throws
  `ConcurrencyException`. *(FR-15, NFR-4)*
- **AC-24 (feature flag off = dark)** — **Given** `FEATURE:EnableActivityGroups`
  is OFF, **When** the admin navigates to the groups route, **Then** the surface
  is hidden/403 and no group endpoints are reachable. *(NFR-11)*
- **AC-25 (group update)** — **Given** an existing `Active` group "Chess Club"
  in tenant T, **When** the admin updates its name to "Chess Club Advanced" and
  sets `Capacity = 30`, **Then** the persisted row reflects the new name and
  capacity with `UpdatedAt` bumped and an `ActivityGroupUpdatedEvent` raised.
  *(FR-5, FR-27)*
- **AC-26 (list assignments targeting a group)** — **Given** group G is linked
  to assignments A1 and A2 (both `SelectedGroups`), **When** the admin queries
  `/api/activity-groups/{G}/assignments`, **Then** A1 and A2 are returned and
  no assignments from other tenants appear. *(FR-25, FR-26, NFR-5)*
- **AC-27 (detail page shows activity groups)** — **Given** student S is an
  active member of groups G1 and G2, **When** the admin views S's Detail page,
  **Then** the page shows an "Activity Groups" section listing G1 and G2 with
  their status badges and no Enrollments-section interference. *(FR-28,
  NFR-10)*
- **AC-28 (join group button opens dialog)** — **Given** the admin is on the
  student Detail page, **When** they click "Join Group", **Then** a dialog
  opens listing available groups (Active, not-at-capacity, not already joined).
  *(FR-29, NFR-10)*
- **AC-29 (multi-join in single operation)** — **Given** the dialog shows groups
  G1, G2, G3, **When** the admin selects G1 and G3 and submits, **Then** both
  memberships are created, the student becomes an active member of both and
  the section refreshes. *(FR-30)*
- **AC-30 (leave group from row action)** — **Given** the student is an active
  member of group G on the Detail page, **When** the admin clicks "Leave" on
  G's row, **Then** the membership status transitions to `Exited` with
  `ExitedOn` set and the section refreshes. *(FR-31, FR-14)*
- **AC-31 (section hidden when flag OFF)** — **Given**
  `FEATURE:EnableActivityGroups` is OFF, **When** the admin navigates to the
  student Detail page, **Then** the "Activity Groups" section does not render
  and no activity-group API calls are made. *(FR-32, NFR-11)*

---

## 6. Edge Cases

> Numbered `EC-N`. At least one failure mode per external dependency
> (DB, user input, cross-context reference, publish pipeline).

- **EC-1 (delete group referenced by assignment)** — Deleting a group that a
  `Draft`/`Published` assignment links to MUST be rejected
  (`ActivityGroupReferencedException`), resolved via the cross-context
  Assignments-API check described in FR-6. *(FR-6, AC-17)*
- **EC-2 (student soft-deleted while active member)** — When a `Student` is
  soft-deleted while holding active memberships, the memberships MUST NOT be
  silently removed; they remain `Active` historically, but the student is
  excluded from recipient resolution and from "add" operations. Re-adding the
  student is blocked (FR-11). (Hard cleanup is out of scope — OS-8.)
- **EC-3 (duplicate active membership race)** — Two concurrent "add same
  student to same group" requests: the partial unique index MUST let exactly
  one succeed and reject the other with a unique-constraint violation
  surfaced as a 409. *(FR-10, AC-5)*
- **EC-4 (archive group with live assignments)** — Archiving a group that
  live (`Draft`/`Published`) assignments target is ALLOWED at the group level,
  but already-published assignments keep their snapshot of recipients; the
  archived group MUST NOT be linkable to NEW assignments (FR-22) and MUST be
  excluded from future re-publish recipient resolution.
- **EC-5 (capacity exceeded concurrently)** — Two concurrent joins that both
  pass the capacity check but together exceed `Capacity`: the unique index
  does not help here, so the handler MUST re-check the active count inside the
  transaction and reject the loser with `GroupAtCapacityException`. *(FR-13)*
- **EC-6 (cross-tenant group reference)** — An assignment link referencing a
  `groupId` from another tenant MUST be rejected before persistence. *(FR-21,
  AC-14)*
- **EC-7 (SelectedGroups publish with zero groups)** — Publishing a
  `SelectedGroups` assignment with no linked groups MUST be rejected (not
  silently produce zero recipients). *(FR-23, AC-13)*
- **EC-8 (group outlasts its period)** — A group associated with a period
  that later becomes `Completed`/`Archived` MUST remain operational. *(FR-3,
  AC-19)*

- **EC-9 (student in zero groups)** — A student in no groups MUST NOT receive
  any `SelectedGroups` assignment; this is not an error, just no recipient row.
- **EC-10 (concurrency on membership update)** — A stale `RowVersion` on a
  membership update MUST throw `ConcurrencyException`. *(NFR-4, AC-23)*
- **EC-11 (linking a non-existent group id)** — Linking an assignment to a
  `groupId` that does not exist MUST be rejected with 404/400.
- **EC-12 (orphaned link after group hard-delete)** — Deletes are
  referentially guarded against membership history (FK `ON DELETE RESTRICT`
  — a group with any membership row cannot be hard-deleted) and against
  `Draft`/`Published` assignment links (EC-1). A group linked **only** to
  `Closed` assignments MAY still be hard-deleted; those `Closed`-assignment
  link rows remain in the Assignments context as dangling operational refs
  (there is no cross-context DB FK — §8.3). This is **benign**: `Closed`
  assignments are never re-published and never re-resolve recipients (and
  `Assignment.Update` rejects edits to non-draft assignments, so the links
  cannot be silently re-pointed). For tidiness, unlink `Closed` assignments
  via `PUT /api/assignments/{id}/groups` (§7.3) before deleting the group.
  No membership orphans can occur because the FK blocks the delete whenever
  any membership row exists.
- **EC-13 (partial multi-join failure)** — When joining multiple groups in
  one operation and one group is at capacity, the capacity-checked groups
  MUST create memberships successfully; the full group MUST be reported as a
  per-group error without rolling back successful joins. *(FR-30)*
- **EC-14 (all groups already joined)** — When the student is already an
  active member of every available group, the dialog's group picker MUST
  show an empty state message (e.g. "You are already a member of all
  available groups") and the submit button MUST be disabled. *(FR-30)*

---

## 7. API Contracts

> TypeScript-style interfaces. Success and error responses are both shown.
> Routes follow the existing `endpoint-organization-pattern.md`
> (`Map...Endpoints(this WebApplication, IFeatureFlagService)`).
> Base path `/api` implied.

### 7.1 Activity group CRUD — Students API

```ts
// POST   /api/activity-groups                 -> 201 ActivityGroupDto | 409
// GET    /api/activity-groups                 -> 200 ActivityGroupDto[]
// GET    /api/activity-groups/{id}            -> 200 ActivityGroupDto | 404
// PUT    /api/activity-groups/{id}            -> 204 | 404 | 409
// DELETE /api/activity-groups/{id}            -> 204 | 404 | 409 (referenced)
// POST   /api/activity-groups/{id}/archive    -> 204 | 404
// POST   /api/activity-groups/{id}/suspend    -> 204 | 404

interface ActivityGroupDto {
  id: string;            // Guid
  tenantId: string;
  name: string;
  description: string | null;
  category: string | null;
  periodId: string | null;
  capacity: number | null;
  status: "Active" | "Suspended" | "Archived";
  activeMemberCount: number;
  createdAt: string;     // ISO 8601
  updatedAt: string;
}

interface CreateActivityGroupRequest {
  name: string;                 // required, <= 200
  description?: string | null;  // <= 2000
  category?: string | null;     // <= 100
  periodId?: string | null;     // Guid | null
  capacity?: number | null;     // >= 1
}

interface UpdateActivityGroupRequest extends Partial<CreateActivityGroupRequest> {}

// Errors
interface ProblemDetails {
  type: string; title: string; status: number;
  detail: string; errors?: Record<string, string[]>;
}
```

### 7.2 Membership — Students API

```ts
// POST   /api/activity-groups/{groupId}/members              -> 201 | 409 | 422
// DELETE /api/activity-groups/{groupId}/members/{studentId}  -> 204 | 404
// POST   /api/activity-groups/{groupId}/members/{studentId}/exit -> 204 | 404
// GET    /api/activity-groups/{groupId}/members              -> 200 MembershipDto[]
// GET    /api/students/{studentId}/activity-groups           -> 200 ActivityGroupDto[]

interface AddMemberRequest {
  studentId: string;     // Guid
  joinedOn?: string;     // DateOnly ISO, default today
}

interface MembershipDto {
  id: string;
  activityGroupId: string;
  studentId: string;
  studentName: string;   // denormalized for display
  joinedOn: string;      // DateOnly ISO
  exitedOn: string | null;
  status: "Active" | "Exited" | "Removed";
  createdAt: string;
  updatedAt: string;
}

// 409 GroupAtCapacityException | duplicate active membership
// 422 archived group | deleted student | tenant mismatch
```

### 7.3 Assignment↔group link — Assignments API

```ts
// PUT   /api/assignments/{assignmentId}/groups        -> 204 | 422 (replace set)
// GET   /api/assignments/{assignmentId}/groups        -> 200 ActivityGroupRefDto[]
// GET   /api/activity-groups/{groupId}/assignments    -> 200 AssignmentSummary[]

interface AssignmentGroupLinkRequest {
  activityGroupIds: string[];   // Guid[]; must be same tenant, non-archived
}

interface ActivityGroupRefDto {
  id: string; name: string; status: "Active" | "Suspended" | "Archived";
}
```

> Note: assignment create/update already accepts `targetAudienceType`; the
> `groups` set is managed via the dedicated link endpoints above so that
> `SelectedGrades` (scalar `gradeLevelId`) and `SelectedGroups` (link set)
> stay on independent code paths (FR-19).

## 8. Data Models

> Snake_case table names (matches `student_enrollments`, `assignments`).
> All entities are strict-tenant (`tenant_id`), audited, and use `xmin` row
> version where noted.

### 8.1 `activity_groups` (Students context)

| field | type | constraints |
|---|---|---|
| `id` | uuid | PK |
| `tenant_id` | uuid | NOT NULL; strict-tenant filter |
| `name` | text | NOT NULL, <= 200 |
| `description` | text | NULL, <= 2000 |
| `category` | text | NULL, <= 100 |
| `period_id` | uuid | NULL; FK → `periods.id` (optional) |
| `capacity` | integer | NULL; CHECK (capacity >= 1) |
| `status` | integer | NOT NULL; default 0 (Active); enum `ActivityGroupStatus` |
| `xmin` | xid | row version (PostgreSQL) |
| `created_at` | timestamptz | NOT NULL |
| `updated_at` | timestamptz | NOT NULL |

Indexes: unique `(tenant_id, lower(name))` → `ix_activity_groups_tenant_name`;
`(tenant_id, status)`; `(tenant_id, period_id)`.

### 8.2 `activity_group_memberships` (Students context)

| field | type | constraints |
|---|---|---|
| `id` | uuid | PK |
| `tenant_id` | uuid | NOT NULL |
| `activity_group_id` | uuid | NOT NULL; FK → `activity_groups.id` `ON DELETE RESTRICT` (preserves membership history — NFR-8; a group with any membership row cannot be hard-deleted, only Archived) |
| `student_id` | uuid | NOT NULL; FK → `students.id` |
| `joined_on` | date | NOT NULL |
| `exited_on` | date | NULL |
| `status` | integer | NOT NULL; default 0 (Active); enum `MembershipStatus` |
| `transfer_reason` | text | NULL |
| `xmin` | xid | row version |
| `created_at` | timestamptz | NOT NULL |
| `updated_at` | timestamptz | NOT NULL |

Indexes: **partial unique** `(tenant_id, student_id, activity_group_id) WHERE
status = 0` → `ix_agm_tenant_student_group_active` (FR-10);
`(tenant_id, activity_group_id, status)` → hot path (NFR-3);
`(tenant_id, student_id)`.

### 8.3 `assignment_activity_groups` (Assignments context)

| field | type | constraints |
|---|---|---|
| `id` | uuid | PK |
| `tenant_id` | uuid | NOT NULL |
| `assignment_id` | uuid | NOT NULL; FK → `assignments.id` |
| `activity_group_id` | uuid | NOT NULL; operational ref (no DB FK across contexts) |
| `created_at` | timestamptz | NOT NULL |
| `updated_at` | timestamptz | NOT NULL |

Indexes: unique `(tenant_id, assignment_id, activity_group_id)` → no duplicate
links; `(tenant_id, activity_group_id)` → reverse lookup (NFR-3).

> `activity_group_id` is an **operational reference** into the Students
> context, exactly like `Assignment.GradeLevelId` / `Assignment.SubjectId`
> today (no cross-context DB foreign key; integrity is enforced in code via
> FR-21/FR-22 and the referential delete guard FR-6).

### 8.6 Enums

```csharp
namespace SchoolCollab.Students.Core.Domain;
public enum ActivityGroupStatus { Active = 0, Suspended = 1, Archived = 2 }
public enum MembershipStatus   { Active = 0, Exited = 1, Removed = 2 }
```

`TargetAudienceType` is **unchanged** (`AllStudents=0, SelectedGrades=1,
SelectedGroups=2`) — this spec finally implements the existing `SelectedGroups`
value.

## 9. Out of Scope

> Explicit exclusions with reasons. IDs: `OS-N`.

- **OS-1 (Unified `AssignmentTarget` table)** — Replacing the scalar
  `Assignment.GradeLevelId` with a polymorphic `AssignmentTarget(TargetType,
  TargetId)` table that lets one assignment target grades **and** groups in a
  single config. **Reason:** breaking change to an existing column/index and
  the `SelectedGrades` code path; requires its own spec + migration + test
  rework. Tracked as a follow-up spec.
- **OS-2 (One assignment targeting both grades and groups simultaneously)** —
  Blocked on OS-1. The current closed-set `TargetAudienceType` keeps audience
  mutually exclusive per assignment. The *system* supports grades and groups;
  a *single* assignment does not mix them yet.
- **OS-3 (Group hierarchy / sub-groups)** — No parent/child groups. Reason: no
  current requirement; adds recursion complexity with no caller.
- **OS-4 (Group chat / messaging channel)** — Groups are a targeting audience,
  not a messaging surface. The notification pipeline is reused as-is (FR-20).
- **OS-5 (Attendance / roster scheduling for groups)** — Out of scope; groups
  here are membership sets for assignment targeting, not timetabled sessions.
- **OS-6 (Group-specific grading rubrics)** — Assignment grading
  (`GradingFormat`, `AssignmentReview`) is unchanged and not partitioned by
  group.
- **OS-7 (Multi-grade per assignment)** — Changing `SelectedGrades` from one
  `GradeLevelId` to many is a separate concern and a breaking change; not
  touched here (NFR-6).
- **OS-8 (Hard cleanup of memberships for soft-deleted students)** — EC-2 keeps
  history; an automated purge job is a separate data-retention concern.
- **OS-9 (Self-service student join/leave)** — Only admin/teacher manages
  membership in this spec. Student-initiated join requests are a future UX
  feature.
- **OS-10 (Teacher assignment / role on activity groups)** — Associating a
  teacher (leader, coach, advisor) with an `ActivityGroup`, granting that
  teacher group-management or publish rights, and displaying the leader in the
  UI are all deferred to a future spec. **Reason:** the teacher-role model
  (permissions, scoping, authorization for group-scoped publish) needs its own
  design and is not required for the core membership + assignment-targeting
  loop this spec delivers. The `leader_teacher_id` column was removed from the
  data model (§8.1), entity (FR-1), and API (§7.1) so it is not a dormant
  nullable column carrying an unimplemented feature.

## 10. Affected files (indicative)

| Context | Path | Change |
|---|---|---|
| Students.Core | `Domain/ActivityGroup.cs`, `Domain/ActivityGroupStatus.cs` | **new** entities |
| Students.Core | `Domain/ActivityGroupMembership.cs`, `Domain/MembershipStatus.cs` | **new** entities |
| Students.Core | `Data/Repositories/*` + `Data/StudentsDbContext.cs` | add DbSets + repos |
| Students.Core | `Migrations/<ts>_AddActivityGroups.cs` | **new** additive migration |
| Students.Core | `Services/IActivityGroupAssignmentQuery.cs` | **new** cross-context port for FR-6 delete guard |
| Students.Api | `Services/ActivityGroupAssignmentQueryHttpClient.cs` | **new** HTTP client impl (calls Assignments API `GET /api/activity-groups/{id}/assignments`) |
| Assignments.Core | `Data/AssignmentsDbContext.cs` | add DbSets |
| Assignments.Core | `Migrations/<ts>_AddAssignmentActivityLinks.cs` | **new** additive migration |
| Students.Admin | `Components/Pages/ActivityGroups/*` | **new** FluentUI pages (mirror `GradeLevels`) |
| Students.Admin | `Components/Pages/Students/Detail.razor` | **extend** — add Activity Groups section (FR-28..32) |
| Students.Admin | `Components/ActivityGroups/JoinGroupDialog.razor` | **new** dialog (FR-29, FR-30) |
| AppHost | `appsettings.json` + `Program.cs` | add `Parameters:FEATURE:EnableActivityGroups`, fan out |
| Docs | `documents/configuration.md` §2 | register the new feature flag |

## 11. Verification

- `dotnet build SchoolCollab.sln` — 0 errors, 0 new warnings.
- `dotnet ef migrations add DiagnosePendingChanges --project
  src/Students/SchoolCollab.Students.Core --context StudentsDbContext` → empty
  after the real migration lands (per `ef-migrations.md`). Same for
  `AssignmentsDbContext`.
- Unit tests (`SchoolCollab.Students.Tests.Unit`):
  - `ActivityGroup.Create/Update/Delete` invariants; delete rejected when
    referenced by assignment or when active members exist (AC-17, AC-18).
  - `ActivityGroupMembership`: multi-membership allowed (AC-4); duplicate
    active rejected (AC-5); rejoin after exit (AC-6); deleted-student/
    archived-group/capacity rejections (AC-7/8/9/10); age/gender specs NOT
    applied (AC-11).
  - `NoUncommittedModelChanges` passes for `StudentsDbContext`.
- Unit tests (`SchoolCollab.Assignments.Tests.Unit`):
  - `AssignmentActivityGroup` link set replace; cross-tenant + archived
    rejected (AC-14/15); `SelectedGrades` path unchanged (AC-16).
  - Publish with `SelectedGroups` resolves members → recipients (AC-12);
    publish rejected (AC-13).
  - `NoUncommittedModelChanges` passes for `AssignmentsDbContext`.
- bUnit: `ActivityGroups` landing + create/edit/delete + members tab; hidden
- bUnit: student Detail page "Activity Groups" section (AC-27); "Join Group"
  dialog opens (AC-28); multi-join operation (AC-29); "Leave" button exits
  member (AC-30); section hidden when flag OFF (AC-31).
- Performance: a benchmark harness seeds 5,000 members (AC-21) and 10,000
  across groups (AC-22) and asserts the NFR-1/NFR-2 thresholds.
- Playwright smoke (seeded): create "Chess Club" → add 3 students → create
  a group-scoped `Subject` (polymorphic, `OwnerType = ActivityGroup`) → create
  a `SelectedGroups` assignment linked to the club and that subject → publish
  → assert only club members' subscribed contacts received it.

## 12. Open questions (resolved)

1. **Group category taxonomy** — free-text `Category` (this spec) vs. a coded
   value under a new `CodedValueParent.ActivityCategory`? ✅ **Decision:
   free-text for v1** (FR-1). Promoted to coded values later if reporting
   needs it.
2. **Leader teacher / group ownership** — ~~should `LeaderTeacherId` grant
   the leader assignment-publish rights to that group?~~ **Decision: deferred.**
   Teacher assignment/role on an `ActivityGroup` is removed from this spec
   entirely and tracked as a future feature (see **OS-10**). The
   `leader_teacher_id` column, the `LeaderTeacherId` entity property, and the
   API field are all removed. Authorization stays as today (admin/teacher
   manages groups and membership); group-specific teacher roles will be
   planned in a separate spec.
3. **Archive vs. delete on active members** — when a group with active members
   is no longer needed, do we force `Archive` (keeps history) and forbid
   hard-delete (current spec, FR-6)? ✅ **Decision: yes** — archive is the
   soft-retire path; hard-delete only when truly unreferenced.
4. **Re-publish recipient semantics for archived groups** — EC-4 says archived
   groups are excluded from future re-publish. Confirm this is desired vs.
   keeping the last snapshot. ✅ **Decision: exclude from re-publish** (fresh
   resolution each publish).

> All questions are resolved. Implementation may proceed.

## 13. Implementation phases (one PR per step, each shippable behind the flag)

### Phase 1 — Students domain model (dark)
- `ActivityGroup` entity + `ActivityGroupStatus` enum
- `ActivityGroupMembership` entity + `MembershipStatus` enum
- Configurations with partial unique index on `(tenant_id, student_id, activity_group_id, status='Active')`
- `StudentsDbContext` migration
- Unit tests for entity invariants (AC-1..11)
- **Flag OFF**

### Phase 2 — Membership commands/queries + APIs (dark)
- CQRS: `CreateActivityGroup`, `UpdateActivityGroup`, `DeleteActivityGroup`, `ArchiveActivityGroup`, `SuspendActivityGroup` commands
- CQRS: `AddMembership`, `RemoveMembership`, `ExitMembership`, `GetGroupMembers`, `GetStudentGroups` queries
- Students API endpoints: `MapActivityGroupEndpoints`
- Cross-context delete-guard port `IActivityGroupAssignmentQuery` (HTTP client in `Students.Api` calling Assignments API `GET /api/activity-groups/{id}/assignments`) wired into `DeleteActivityGroupHandler` (FR-6)
- Feature flag gate on all endpoints
- Unit tests for all command/query handlers
- **Flag OFF**

### Phase 3 — Assignment↔group link + publish wiring (dark)
- `AssignmentActivityGroup` entity + configuration + migration
- `LinkAssignmentGroups` command handler
- Extend publish recipient resolution for `SelectedGroups` (resolve active
  members of linked groups → recipients)
- Unit tests for link set replace, cross-tenant rejection, archived group
  exclusion
- **Flag OFF**

### Phase 4 — Admin UI: ActivityGroups pages (dark)
- `ActivityGroups` list page (mirror `GradeLevels`)
- `ActivityGroupCreateEditDialog`
- `ActivityGroupDetails` page with members tab
- All UI gated by `FEATURE:EnableActivityGroups`
- bUnit tests for CRUD + members tab
- **Flag OFF**

### Phase 5 — Student Detail page UI (dark)
- "Activity Groups" section on student Detail page (FR-28)
- "Join Group" dialog with searchable multi-select (FR-29, FR-30)
- "Leave" action button per membership row (FR-31)
- Empty state message when no memberships (FR-32)
- Feature flag gate on entire section + all API calls
- bUnit tests (AC-27, AC-28, AC-29, AC-30, AC-31)
- **Flag OFF**

### Phase 6 — Flag flip + pilot rollout (dark→lit)
- `FEATURE:EnableActivityGroups` defaults ON in `appsettings.Pilot Tenant.json`
- Configuration documentation update
- Playwright smoke test: create group → add students → create `SelectedGroups` assignment → publish → verify recipients
- Monitor pilot tenant for 1 week before broader rollout

---

### Traceability summary

- **35 FRs (FR-1..35), 11 NFRs (NFR-1..11), 31 ACs (AC-1..31), 14 ECs (EC-1..14), 10 OS items (OS-1..10).
- Every entity in §8 maps to a requirement: `activity_groups` → FR-1..6;
  `activity_group_memberships` → FR-7..16; `assignment_activity_groups` →
  FR-17..23; FR-28..32 for student Detail page UI; FR-35 for admin UI.
- `TargetAudienceType.SelectedGroups` (pre-existing enum value) is now backed
  by data model §8.3 and behavior FR-17..23.
