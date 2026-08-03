# Spec: Subject-to-Topic Polymorphism (Merge Grade-Subjects & Activity-Lists)

> **Status:** Superseded for ownership model — see §0 (revised). The rename of
> `Subject` → `Topic` and of `SubjectStrand`/`SubjectLesson` → `TopicStrand`/
> `TopicLesson` stands. The **polymorphic ownership model** described in the
> body of this document (§2 onward) was **reversed** in favour of a shared,
> global `Topic` + `GradeSubjectAssignment` M:N bridge (Design B). See §0.
> **Author:** Cline (spec-driven-workflow)
> **Date:** 2026-07-31
> **Reviewers:** Students context owner, Assignments context owner, Architecture
> **Owner contexts:** `SchoolCollab.Students.Core`, `SchoolCollab.Assignments.Core`,
> `SchoolCollab.Students.Admin`, `SchoolCollab.Assignments.Admin`
> **Depends on:** `grade-level-setup.md`, `activity-group-enrollment.md`,
> `global-tenant-filter.md`, `ef-migrations.md`, `endpoint-organization-pattern.md`
> **Supersedes:** Activity List sections (§3.7, §7.4, §7.5, §8.4, §8.5, EC-15..18,
> AC-32..34) of `activity-group-enrollment.md`

---

## 0. Decisions locked in this revision

> **Revision note (2026-08-03):** The earlier polymorphic decision (§0 items
> 1–3 and 5–7 below) was found to be **incoherent**: `Topic` was both polymorphic
> (`OwnerType`/`OwnerId`/`PeriodId`) **and** retained `GradeSubjectAssignment` as a
> bridge — the bridge was redundant with `Topic.OwnerId`. This was reversed to
> **Design B**. The decisions below reflect the **current**, authoritative model.

1. **`Topic` is a shared, global definition.** A `Topic` (renamed from
   `Subject`) is a tenant-scoped catalog entry. It carries no owner columns
   (`OwnerType`/`OwnerId`/`PeriodId` were removed) and no per-grade default
   tags (`DefaultStrandId`/`DefaultLessonId` were removed). A topic is **not**
   scoped to a grade/period on the topic itself.

2. **`GradeSubjectAssignment` is the M:N bridge.** It connects a shared
   `Topic` to a `GradeLevel` **or** an `ActivityGroup`, and carries the
   per-grade/group **strand and lesson selection** via
   `TopicStrandId`/`TopicLessonId`. Columns: `GradeLevelId?`,
   `ActivityGroupId?`, `TopicId`, `StartDate`, `EndDate?`,
   `TopicStrandId?`, `TopicLessonId?`.
   At most one of `GradeLevelId`/`ActivityGroupId` is set per row.

2a. **The bridge is date-based, not period-bound.** A grade↔topic assignment
   spans multiple years, so it is not tied to a `PeriodId`. Its effective
   window is `[StartDate, EndDate]` where a null `EndDate` means open-ended
   (currently active). Blocking or archiving an assignment sets `EndDate`
   (typically today), which ends its effective period — no status enum is
   needed. Removing a subject from a grade therefore "ends" the bridge row
   rather than hard-deleting it, preserving history.

3. **Per-grade/group variance is selection, not duplication.** Strands and
   lessons stay **global under the `Topic`** (`TopicStrand.TopicId`,
   `TopicLesson.TopicId`). The bridge's `TopicStrandId`/`TopicLessonId`
   select **which** strand/lesson a given grade/group uses for that topic —
   no per-grade copies of strands or lessons.

4. **Strands and lessons work uniformly.** `SubjectStrand` (→ `TopicStrand`)
   and `SubjectLesson` (→ `TopicLesson`) already link to `SubjectId` (→
   `TopicId`). They require no structural change beyond the rename — they
   are shared by all grades/groups that bridge to the topic.

5. **`Assignment.TopicId` stays required** (the column was renamed
   `subject_id` → `topic_id`). The `ActivityGroup` side of the bridge
   supports `SelectedGroups` targeting; validation against the bridge is
   extended when the full rename lands in Assignments.

6. **`Code` and `CodedValueId` remain nullable/optional.** Grade-level topics
   use the coded-value system (existing behavior). Activity-group topics may be
   free-text. The existing unique index on `(tenant_id, code)` remains a
   partial `WHERE code IS NOT NULL`.

7. **Migration.** The table rename + bridge extension (`activity_group_id`
   added, `grade_level_id` made nullable, strand/lesson columns renamed
   `subject_*` → `topic_*`) is captured in one migration. No data loss.

---

## 1. Goal

Merge the grade-level subject system and the activity-group activity-list
system into a **single shared `Topic` entity** (formerly `Subject`), with
`GradeSubjectAssignment` as the M:N bridge that assigns a topic to a grade or
group and selects the strand/lesson that grade/group uses. This eliminates the
parallel `ActivityList` entity from `activity-group-enrollment.md` and
unifies strands, lessons, reporting, and admin UI under one shared model.

> **Note:** The polymorphic ownership sections in the body of this document
> (§2 onward) are **historical** and were superseded by §0 above. They are
> retained only as a record of the rejected approach; the implemented model is
> Design B per §0.

---

## 2. Context

### 2.1 Why this merge is needed

School-Collab currently has two parallel categorisation systems:

1. **Grade-level subjects:** `Subject` + `GradeSubjectAssignment` + optional
   `SubjectStrand` / `SubjectLesson`. An `Assignment` references
   `SubjectId` (required). Subjects are backed by the coded-value system
   (`CodedValueId`) and are scoped per grade level per period via
   `GradeSubjectAssignment`.

2. **Activity-group activity lists** (proposed in `activity-group-enrollment.md`):
   `ActivityList` (title/description, child of `ActivityGroup`) +
   `AssignmentActivityList` (optional link table for assignment scoping).

Both serve the same purpose: **categorising what an assignment is about**
(Mathematics, Science vs. Tournament Prep, General Practice). Maintaining
two entities with near-identical shape, two sets of CRUD APIs, two admin
UIs, and two sets of tests is unnecessary duplication. Worse, strands and
lessons — which already exist for subjects — would need to be re-implemented
or polymorphically shared for activity lists.

### 2.2 Current state (from code)

| Entity | Table | Key fields |
|---|---|---|
| `Subject` | `subjects` | `CodedValueId` (required), `Code` (required, unique per tenant), `Name`, `DisplayOrder` |
| `SubjectStrand` | `subject_strands` | `SubjectId` (FK), `Name`, `Description`, `DisplayOrder` |
| `SubjectLesson` | `subject_lessons` | `SubjectId` (FK), `StrandId` (optional FK), `Name`, `Description`, dates |
| `GradeSubjectAssignment` | `grade_subject_assignments` | `GradeLevelId`, `SubjectId`, `PeriodId`, `SubjectStrandId?`, `SubjectLessonId?` |
| `StudentSubjectAssignment` | `student_subject_assignments` | `StudentId`, `SubjectId`, `PeriodId`, `IsOverride`, `SourceType` |
| `Assignment` | `assignments` | `SubjectId` (required), `GradeLevelId` (nullable), `TargetAudienceType` |

`Assignment.Create()` throws `ArgumentException` if `subjectId == Guid.Empty`
(line 69-70 of `Assignment.cs`). Every assignment MUST have a subject.

### 2.3 Target state

```
Topic (was Subject)
├── OwnerType: TopicOwnerType (GradeLevel=0, ActivityGroup=1)  [NEW]
├── OwnerId: Guid  (GradeLevelId or ActivityGroupId)           [NEW]
├── PeriodId: Guid?  (grade-level topics; null for group topics) [NEW]
├── DefaultStrandId: Guid?  (moved from GradeSubjectAssignment)       [NEW]
├── DefaultLessonId: Guid?  (moved from GradeSubjectAssignment)       [NEW]
├── Code: string?  (nullable — group topics may not have a code)
├── Name, Description, DisplayOrder  (existing)
├── CodedValueId: Guid?  (nullable — group topics are free-text)
├── TopicStrand (1:N — was SubjectStrand, renamed)
│   └── TopicLesson (1:N — was SubjectLesson, renamed)
└── StudentTopicAssignment (was StudentSubjectAssignment, renamed)

ELIMINATED:
  - GradeSubjectAssignment (absorbed into Topic)
  - ActivityList (from activity-group-enrollment.md §3.7)
  - AssignmentActivityList (from activity-group-enrollment.md §7.5)
  - activity_lists table (from activity-group-enrollment.md §8.4)
  - assignment_activity_lists table (from activity-group-enrollment.md §8.5)
```

### 2.4 Why eliminate GradeSubjectAssignment

Currently `GradeSubjectAssignment` is a many-to-many link: one `Subject` can
be assigned to multiple grades across multiple periods (one row per
grade+period). The subject itself is a global definition shared across
grades.

In the new model, a `Topic` carries `OwnerId` (= GradeLevelId) and
`PeriodId` directly. This means each topic is a per-grade, per-period
offering. "Mathematics for Grade 8 in Period 2026-Spring" and "Mathematics
for Grade 9 in Period 2026-Spring" are two separate topic rows, linked
conceptually by the same `CodedValueId`.

This is **better** because:
- Each grade's topic is independently configurable (display order, default
  strand/lesson).
- There is no separate link table to maintain.
- The `Assignment.TopicId` directly references the offering, not a global
  definition that then needs to be resolved through a link table.
- Activity-group topics follow the same pattern (one topic per group, no
  period scoping needed since groups outlast periods).

The cost is that creating a subject for multiple grades now creates multiple
topic rows. The admin UI can mitigate this by offering "create from existing
coded value" — selecting a coded value and a grade level, which creates a
new topic row with that coded value and that grade as owner.

## 3. Functional Requirements

> RFC 2119 keywords (MUST / MUST NOT / SHOULD / MAY) are used precisely.
> Each requirement is atomic and testable. IDs: `FR-N`.

### 3.1 Topic lifecycle (rename + extend Subject)

- **FR-1** — The entity currently named `Subject` MUST be renamed to `Topic`
  (see §12.1 for the naming decision). The `subjects` table MUST be renamed
  to `topics`. All references (`SubjectId`, `SubjectStrand`, `SubjectLesson`,
  `StudentSubjectAssignment`, etc.) MUST be renamed accordingly
  (`TopicId`, `TopicStrand`, `TopicLesson`, `StudentTopicAssignment`).
- **FR-2** — `Topic` MUST retain all existing properties: `Name`, `Code`,
  `DisplayOrder`, `CodedValueId`, `TenantId`, `RowVersion`, `CreatedAt`,
  `UpdatedAt`, domain events, and the `Create` / `Update` / `Delete`
  factory methods. The existing `SubjectReferencedException` MUST be renamed
  to `TopicReferencedException`.
- **FR-3** — `Topic.Code` MUST become nullable. Grade-level topics MUST
  continue to use a code (existing behavior). Activity-group topics MAY
  have no code. The existing unique index on `(tenant_id, code)` MUST become
  a partial unique index `WHERE code IS NOT NULL`.
- **FR-4** — `Topic.CodedValueId` MUST become nullable. Grade-level topics
  MUST continue to use the coded-value system (existing behavior).
  Activity-group topics MUST NOT use coded values (they are free-text).
- **FR-5** — `Topic.Description` MUST be added as an optional property
  (max 2000 chars). This replaces the `ActivityList.description` from the
  activity-group-enrollment spec. Grade-level subjects currently have no
  description; this adds one without breaking existing behavior.

### 3.2 Topic polymorphic ownership

- **FR-6** — `Topic` MUST have an `OwnerType` property of type
  `TopicOwnerType` (`GradeLevel = 0, ActivityGroup = 1`). This is a
  non-nullable discriminator.
- **FR-7** — `Topic` MUST have an `OwnerId` property (Guid, non-nullable)
  that references either `GradeLevel.Id` or `ActivityGroup.Id` depending on
  `OwnerType`. This is an operational reference (no DB FK across contexts for
  `ActivityGroup`; FK to `grade_levels.id` for `GradeLevel` within the
  Students context).
- **FR-8** — `Topic` MUST have a nullable `PeriodId` (Guid). When
  `OwnerType = GradeLevel`, `PeriodId` MUST be set (the period this topic is
  offered in). When `OwnerType = ActivityGroup`, `PeriodId` MUST be null
  (groups outlast periods — see activity-group-enrollment.md FR-3).
- **FR-9** — `Topic` MUST have nullable `DefaultStrandId` and
  `DefaultLessonId` (Guid). These carry the optional strand/lesson tags
  previously on `GradeSubjectAssignment`. When set, they reference
  `TopicStrand.Id` and `TopicLesson.Id` respectively.
- **FR-10** — The system MUST support creating a topic with
  `OwnerType = ActivityGroup` and `OwnerId = ActivityGroupId`. Such a topic
  MUST NOT require a `Code` or `CodedValueId`. It MUST NOT require a
  `PeriodId`. It MUST support strands and lessons identically to grade-level
  topics.
- **FR-11** — The system MUST reject creating a topic with `OwnerType =
  ActivityGroup` if the referenced `ActivityGroup` does not exist, is
  `Archived`, or belongs to a different tenant.
- **FR-12** — The existing `GradeSubjectAssignment` entity, table, CQRS
  handlers, API endpoints, and admin UI MUST be removed. The data (grade
  level link, period link, strand/lesson tags) is absorbed into the `Topic`
  entity via the migration backfill.

### 3.3 Assignment topic validation

- **FR-13** — `Assignment.SubjectId` (renamed `Assignment.TopicId` — see
  §12.1) MUST remain required. The `Assignment.Create()` factory MUST
  continue to throw if `topicId == Guid.Empty`.
- **FR-14** — When `Assignment.TargetAudienceType = SelectedGrades`, the
  assignment's topic MUST have `OwnerType = GradeLevel` and `OwnerId` MUST
  match `Assignment.GradeLevelId`. The system MUST reject a mismatch with a
  validation error.
- **FR-15** — When `Assignment.TargetAudienceType = SelectedGroups`, the
  assignment's topic MUST have `OwnerType = ActivityGroup` and `OwnerId`
  MUST match one of the assignment's linked `ActivityGroup`s (via
  `AssignmentActivityGroup`). The system MUST reject a mismatch with a
  validation error. This replaces the `AssignmentActivityList` link table
  from `activity-group-enrollment.md`.
- **FR-16** — When `Assignment.TargetAudienceType = AllStudents`, the
  topic's `OwnerType` MAY be either `GradeLevel` or `ActivityGroup` (no
  owner-matching validation).
- **FR-17** — `SelectedGrades` MUST continue to use the existing scalar
  `Assignment.GradeLevelId` unchanged. `SelectedGroups` MUST continue to use
  the `AssignmentActivityGroup` link table (from
  `activity-group-enrollment.md`). Neither code path is altered beyond the
  `SubjectId` → `TopicId` rename and the owner-type validation.

### 3.4 Strands and lessons (rename only)

- **FR-18** — `SubjectStrand` MUST be renamed to `TopicStrand`. The
  `subject_strands` table MUST be renamed to `topic_strands`. The
  `SubjectId` column MUST be renamed to `TopicId`. All properties
  (`Name`, `Description`, `DisplayOrder`, FK to `Topic`) and behavior
  (`Create`, `Update`, domain events) MUST remain structurally identical.
- **FR-19** — `SubjectLesson` MUST be renamed to `TopicLesson`. The
  `subject_lessons` table MUST be renamed to `topic_lessons`. The
  `SubjectId` column MUST be renamed to `TopicId`. The optional `StrandId`
  MUST be renamed to `TopicStrandId`. All properties and behavior MUST
  remain structurally identical.
- **FR-20** — Strands and lessons MUST work identically for grade-level
  and activity-group topics. A topic with `OwnerType = ActivityGroup` MAY
  have strands (e.g., "Strategy", "Opening Theory") and lessons (e.g.,
  "Sicilian Defense", "King-Pawn Endgames") exactly as a grade-level topic
  would. No additional validation or code branching is required.

### 3.5 Reads / queries

- **FR-21** — The existing query "list subjects by grade level + period"
  MUST be replaced with "list topics by `OwnerId` (= GradeLevelId) +
  `PeriodId`". The API endpoint MUST accept `ownerType`, `ownerId`, and
  optional `periodId` query parameters.
- **FR-22** — A new query MUST be supported: "list topics by
  `OwnerId` (= ActivityGroupId)" with `OwnerType = ActivityGroup`. This
  replaces the `GET /api/activity-groups/{groupId}/lists` endpoint from
  `activity-group-enrollment.md` §7.4.
- **FR-23** — All topic queries MUST be tenant-filtered via the global
  tenant filter (`global-tenant-filter.md` §3.2 Strict).

### 3.6 Domain events

- **FR-24** — All existing domain events MUST be renamed: `SubjectCreatedEvent`
  → `TopicCreatedEvent`, `SubjectUpdatedEvent` → `TopicUpdatedEvent`,
  `SubjectDeletedEvent` → `TopicDeletedEvent`, `SubjectStrandCreatedEvent`
  → `TopicStrandCreatedEvent`, etc.
- **FR-25** — Creating an activity-group topic MUST emit a
  `TopicCreatedEvent` (same event as grade-level topics — no owner-type
  branching in events).

---

## 4. Non-Functional Requirements

> All thresholds are measurable. IDs: `NFR-N`.

- **NFR-1 (Non-breaking migration sequence)** — The migration MUST be
  sequenced so that the Students context migration lands first (table
  rename + column additions + backfill + drop `GradeSubjectAssignment`),
  then the Assignments context migration (column rename `subject_id` →
  `topic_id`). Both migrations MUST be independently deployable behind the
  `FEATURE:EnableActivityGroups` flag.
- **NFR-2 (Migration guard)** — A `NoUncommittedModelChanges` unit test
  MUST be updated for `StudentsDbContext` and `AssignmentsDbContext` per
  `ef-migrations.md`. The test MUST pass after the migration lands.
- **NFR-3 (Data preservation)** — The migration MUST NOT lose any data.
  All existing subject rows MUST become topic rows with `OwnerType = 0
  (GradeLevel)`, `OwnerId = GradeLevelId` (from `GradeSubjectAssignment`),
  `PeriodId` (from `GradeSubjectAssignment`), and optional
  `DefaultStrandId` / `DefaultLessonId` (from `GradeSubjectAssignment`).
- **NFR-4 (Indexing)** — The existing index `ix_subjects_tenant_code`
  MUST become a partial unique index `ix_topics_tenant_code` with
  `WHERE code IS NOT NULL`. A new index `ix_topics_tenant_owner`
  on `(tenant_id, owner_type, owner_id)` MUST be created for the hot-path
  query "list topics by owner."
- **NFR-5 (Tenancy)** — All topic/strand/lesson reads and writes MUST
  remain strict-tenant-filtered (`global-tenant-filter.md` §3.2 Strict).
  No cross-tenant topic access is permitted.
- **NFR-6 (Backward compatibility)** — The API MUST maintain backward
  compatibility for the `/api/subjects` endpoint by aliasing it to
  `/api/topics` during a deprecation window. Existing clients that call
  `/api/subjects` MUST continue to work, returning `TopicDto` data under
  the old route name.
- **NFR-7 (Auditability)** — `CreatedAt` / `UpdatedAt` MUST be present
  and populated on all renamed entities (unchanged from existing behavior).
- **NFR-8 (Feature flag)** — The polymorphic topic surface (activity-group
  topics, owner-type validation) MUST be gated behind
  `FEATURE:EnableActivityGroups` (same flag as activity groups). Grade-level
  topics MUST work regardless of the flag (they are the existing, unflagged
  behavior, just renamed).

---

## 5. Acceptance Criteria

> Given / When / Then. Every criterion references at least one `FR-*` or
> `NFR-*`. IDs: `AC-N`.

- **AC-1 (grade-level topic retained)** — **Given** the migration has
  run, **When** the admin queries topics for Grade 8 in the current period,
  **Then** the topics returned are the same subjects that were previously
  assigned to Grade 8 (now with `OwnerType = GradeLevel`, `OwnerId =
  Grade8`, `PeriodId` set). *(FR-2, FR-7, FR-8, NFR-3)*
- **AC-2 (assignment topic retained)** — **Given** an existing assignment
  with `SubjectId = S1`, **When** the migration completes, **Then** the
  assignment now has `TopicId = S1` (same Guid, renamed column) and the
  topic is found in the `topics` table. *(FR-13, NFR-3)*
- **AC-3 (nullable code for group topics)** — **Given** an activity group
  G, **When** the admin creates a topic "Tournament Prep" under G with no
  code and no coded value, **Then** the topic is persisted with
  `OwnerType = ActivityGroup`, `OwnerId = G`, `Code = null`,
  `CodedValueId = null`, `PeriodId = null`. *(FR-3, FR-4, FR-10)*
- **AC-4 (partial unique index on code)** — **Given** two topics in the
  same tenant both have `Code = null`, **When** they are persisted,
  **Then** no unique-constraint violation occurs (the partial index only
  applies to non-null codes). *(FR-3, NFR-4)*
- **AC-5 (group topic rejects archived group)** — **Given** activity group
  G is `Archived`, **When** the admin creates a topic under G, **Then**
  the request is rejected. *(FR-11)*
- **AC-6 (group topic rejects cross-tenant group)** — **Given** an
  activity group from tenant T2, **When** the admin in tenant T1 creates a
  topic referencing it, **Then** the request is rejected. *(FR-11, NFR-5)*
- **AC-7 (SelectedGrades topic validation)** — **Given** an assignment with
  `TargetAudienceType = SelectedGrades` and `GradeLevelId = G8`, **When**
  the assignment's topic has `OwnerType = GradeLevel` and `OwnerId = G9`,
  **Then** the mismatch is rejected with a validation error. *(FR-14)*
- **AC-8 (SelectedGroups topic validation)** — **Given** an assignment with
  `TargetAudienceType = SelectedGroups` linked to group G1, **When** the
  assignment's topic has `OwnerType = ActivityGroup` and `OwnerId = G2`
  (not linked), **Then** the mismatch is rejected. *(FR-15)*
- **AC-9 (SelectedGroups topic matches linked group)** — **Given** an
  assignment with `TargetAudienceType = SelectedGroups` linked to groups
  G1 and G2, **When** the assignment's topic has `OwnerType = ActivityGroup`
  and `OwnerId = G2`, **Then** validation passes. *(FR-15)*

- **AC-10 (strands under group topic)** — **Given** an activity-group topic
  "Chess Strategy" under group G, **When** the admin creates a strand
  "Opening Theory" under that topic, **Then** the strand is persisted with
  `TopicId` referencing the group topic and is queryable. *(FR-18, FR-20)*
- **AC-11 (lessons under group topic strand)** — **Given** a strand
  "Opening Theory" under an activity-group topic, **When** the admin creates
  a lesson "Sicilian Defense" linked to that strand, **Then** the lesson is
  persisted and queryable. *(FR-19, FR-20)*
- **AC-12 (GradeSubjectAssignment removed)** — **Given** the migration has
  run, **When** the admin queries the database, **Then** the
  `grade_subject_assignments` table does not exist and its data has been
  absorbed into the `topics` table. *(FR-12, NFR-3)*
- **AC-13 (topic query by group)** — **Given** activity group G has topics
  T1 and T2, **When** the admin queries `/api/topics?ownerType=1&ownerId=G`,
  **Then** T1 and T2 are returned and no grade-level topics appear.
  *(FR-22, FR-23)*
- **AC-14 (topic query by grade)** — **Given** Grade 8 has topic T3 in
  period P, **When** the admin queries
  `/api/topics?ownerType=0&ownerId=G8&periodId=P`, **Then** T3 is returned.
  *(FR-21, FR-23)*
- **AC-15 (AllStudents no owner validation)** — **Given** an assignment
  with `TargetAudienceType = AllStudents` and a topic with
  `OwnerType = ActivityGroup`, **When** the assignment is created,
  **Then** no owner-matching validation error occurs. *(FR-16)*
- **AC-16 (backward-compatible API route)** — **Given** a client calls
  `/api/subjects` (old route), **When** the request is processed,
  **Then** the response returns `TopicDto` data (same shape, route is
  aliased). *(NFR-6)*

---

## 6. Edge Cases

> Numbered `EC-N`. At least one failure mode per external dependency
> (DB, user input, cross-context reference, migration).

- **EC-1 (owner type mismatch on assignment)** — An assignment with
  `SelectedGrades` whose topic has `OwnerType = ActivityGroup` MUST be
  rejected. Conversely, `SelectedGroups` with a `GradeLevel` topic MUST be
  rejected. *(FR-14, FR-15)*
- **EC-2 (non-existent owner)** — Creating a topic with `OwnerId` referencing
  a `GradeLevel` or `ActivityGroup` that does not exist MUST be rejected
  with a 404/validation error. *(FR-7, FR-11)*
- **EC-3 (multi-grade duplication)** — When the same coded value is used
  for topics under different grade levels, each topic row is independent.
  Updating one (e.g., display order) MUST NOT affect the others. The
  `CodedValueId` is the conceptual link, not a uniqueness constraint.
- **EC-4 (null code on grade-level topic)** — A grade-level topic with
  `Code = null` MUST NOT be rejected (the partial unique index allows it),
  but the admin UI SHOULD warn that a code is recommended for grade-level
  topics for reporting consistency.
- **EC-5 (strand references different topic)** — A `TopicStrand` with
  `TopicId` referencing a topic that has been deleted MUST be cascade-deleted
  (existing behavior, unchanged — `OnDelete(Cascade)` on the FK).
- **EC-6 (migration backfill for orphaned subjects)** — If a `Subject` has
  no `GradeSubjectAssignment` (orphaned), the migration MUST set
  `OwnerType = GradeLevel`, `OwnerId = Guid.Empty`, `PeriodId = null`. The
  admin SHOULD be able to reassign these topics to the correct grade level
  post-migration. This is a data-quality issue, not a migration failure.
- **EC-7 (assignment references deleted topic)** — Because topic deletes are
  referentially guarded (`TopicReferencedException` when referenced by a
  `Draft`/`Published` assignment), a deleted topic has no live assignment
  references — no orphan rows can occur in normal operation.
- **EC-8 (StudentTopicAssignment owner type)** — A `StudentTopicAssignment`
  (was `StudentSubjectAssignment`) references a topic. If the topic's
  `OwnerType` is `ActivityGroup`, the student MUST be an active member of
  that group. This validation is deferred to the activity-group-enrollment
  spec (FR-11 membership checks).

---

## 7. API Contracts

> TypeScript-style interfaces. Success and error responses are both shown.
> Routes follow the existing `endpoint-organization-pattern.md`.
> Base path `/api` implied. Old route `/api/subjects` is aliased to
> `/api/topics` for backward compatibility (NFR-6).

### 7.1 Topic CRUD — Students API

> **Design B (revised §0).** `Topic` is a shared, global definition with no
> owner columns. The owner/grading link lives on the `GradeSubjectAssignment`
> M:N bridge (§2). So there is no `ownerType`/`ownerId`/`periodId` query
> surface on topics; grade-filtered reads go through the bridge (see
> `GET /topics/by-grade/{gradeLevelId}` below). The `/subjects` prefix is a
> deprecated backward-compatible alias for `/topics` (NFR-6 / AC-16).

```ts
// POST   /api/topics                           -> 201 TopicDto | 409
// GET    /api/topics                           -> 200 TopicDto[]
// GET    /api/topics/{id}                       -> 200 TopicDto | 404
// GET    /api/topics/by-code/{code}             -> 200 TopicDto | 404
// GET    /api/topics/by-grade/{gradeLevelId}?effectiveDate= -> 200 TopicDto[]
// PUT    /api/topics/{id}                       -> 204 | 404 | 409
// DELETE /api/topics/{id}                       -> 204 | 404 | 409 (referenced)
// (GET    /api/subjects/* — deprecated alias for /api/topics/*)

interface TopicDto {
  id: string;             // Guid
  codedValueId: string | null;  // optional; coded-value system link
  code: string | null;    // optional (grade-level topics typically set it)
  name: string;
  description: string | null;
  displayOrder: number;
  createdAt: string;      // ISO 8601
  updatedAt: string;
}

interface CreateTopicRequest {
  codedValueId?: string | null;
  code?: string | null;
  name: string;             // required, <= 200
  description?: string | null;  // <= 2000
  displayOrder?: number;    // default 0
}

interface UpdateTopicRequest extends Partial<CreateTopicRequest> {}

// Errors
interface ProblemDetails {
  type: string; title: string; status: number;
  detail: string; errors?: Record<string, string[]>;
}
// 409 DuplicateTopicCodeException / TopicReferencedException
// 404 topic or grade level not found
```

### 7.2 Strands and lessons — Students API

```ts
// POST   /api/topics/{topicId}/strands        -> 201 TopicStrandDto | 404 | 422
// GET    /api/topics/{topicId}/strands        -> 200 TopicStrandDto[]
// PUT    /api/topic-strands/{id}              -> 204 | 404 | 422
// DELETE /api/topic-strands/{id}              -> 204 | 404 | 409

// POST   /api/topics/{topicId}/lessons        -> 201 TopicLessonDto | 404 | 422
// GET    /api/topics/{topicId}/lessons        -> 200 TopicLessonDto[]
// PUT    /api/topic-lessons/{id}             -> 204 | 404 | 422
// DELETE /api/topic-lessons/{id}             -> 204 | 404 | 409

interface TopicStrandDto {
  id: string; topicId: string; name: string;
  description: string | null; displayOrder: number;
  createdAt: string; updatedAt: string;
}

interface TopicLessonDto {
  id: string; topicId: string; topicStrandId: string | null;
  name: string; description: string | null;
  startDate: string | null; endDate: string | null;
  displayOrder: number; createdAt: string; updatedAt: string;
}
```

> These endpoints are structurally identical to the existing
> `/api/subjects/{id}/strands` and `/api/subjects/{id}/lessons` — only the
> route prefix and DTO names change.

---

## 8. Data Models

> Snake_case table names. All entities are strict-tenant, audited, and use
> `xmin` row version.

### 8.1 `topics` (Students context — renamed from `subjects`)

| field | type | constraints |
|---|---|---|
| `id` | uuid | PK |
| `tenant_id` | uuid | NOT NULL; strict-tenant filter |
| `owner_type` | integer | NOT NULL; default 0; enum `TopicOwnerType` (GradeLevel=0, ActivityGroup=1) |
| `owner_id` | uuid | NOT NULL; GradeLevelId or ActivityGroupId (operational ref for group) |
| `period_id` | uuid | NULL; set for grade-level topics, null for group topics |
| `code` | text | NULL, <= 50; was NOT NULL — now nullable for group topics |
| `coded_value_id` | uuid | NULL; was NOT NULL — now nullable for group topics |
| `name` | text | NOT NULL, <= 200 |
| `description` | text | NULL, <= 2000; NEW (replaces ActivityList.description) |
| `display_order` | integer | NOT NULL |
| `default_strand_id` | uuid | NULL; FK → `topic_strands.id` (moved from GradeSubjectAssignment) |
| `default_lesson_id` | uuid | NULL; FK → `topic_lessons.id` (moved from GradeSubjectAssignment) |
| `xmin` | xid | row version (PostgreSQL) |
| `created_at` | timestamptz | NOT NULL |
| `updated_at` | timestamptz | NOT NULL |

Indexes:
- **Partial unique** `(tenant_id, code)` WHERE `code IS NOT NULL` →
  `ix_topics_tenant_code` (was `ix_subjects_tenant_code`, now partial)
- `(tenant_id, coded_value_id)` WHERE `coded_value_id IS NOT NULL` →
  `ix_topics_tenant_coded_value` (partial, for group-topic rows that have no coded value)
- **NEW** `(tenant_id, owner_type, owner_id)` → `ix_topics_tenant_owner`
  (hot path: list topics by owner)
- `(tenant_id, owner_id, period_id)` → `ix_topics_tenant_owner_period`
  (hot path: list grade-level topics by grade + period)

### 8.2 `topic_strands` (Students context — renamed from `subject_strands`)

| field | type | constraints |
|---|---|---|
| `id` | uuid | PK |
| `tenant_id` | uuid | NOT NULL |
| `topic_id` | uuid | NOT NULL; FK → `topics.id` (was `subject_id`) |
| `name` | text | NOT NULL, <= 200 |
| `description` | text | NULL, <= 1000 |
| `display_order` | integer | NOT NULL |
| `xmin` | xid | row version |
| `created_at` | timestamptz | NOT NULL |
| `updated_at` | timestamptz | NOT NULL |

Indexes: `(tenant_id, topic_id)` → `ix_topic_strands_tenant_topic` (was
`ix_subject_strands_tenant_subject`).

### 8.3 `topic_lessons` (Students context — renamed from `subject_lessons`)

| field | type | constraints |
|---|---|---|
| `id` | uuid | PK |
| `tenant_id` | uuid | NOT NULL |
| `topic_id` | uuid | NOT NULL; FK → `topics.id` (was `subject_id`) |
| `topic_strand_id` | uuid | NULL; FK → `topic_strands.id` (was `strand_id`) |
| `name` | text | NOT NULL, <= 200 |
| `description` | text | NULL, <= 1000 |
| `start_date` | date | NULL |
| `end_date` | date | NULL |
| `display_order` | integer | NOT NULL |
| `xmin` | xid | row version |
| `created_at` | timestamptz | NOT NULL |
| `updated_at` | timestamptz | NOT NULL |

Indexes: `(tenant_id, topic_id)` → `ix_topic_lessons_tenant_topic`.

### 8.4 `student_topic_assignments` (Students context — renamed from `student_subject_assignments`)

| field | type | constraints |
|---|---|---|
| `id` | uuid | PK |
| `tenant_id` | uuid | NOT NULL |
| `student_id` | uuid | NOT NULL; FK → `students.id` |
| `topic_id` | uuid | NOT NULL; operational ref to `topics.id` (was `subject_id`) |
| `period_id` | uuid | NOT NULL |
| `is_override` | boolean | NOT NULL |
| `source_type` | integer | NOT NULL; enum `SubjectAssignmentSource` (unchanged) |
| `xmin` | xid | row version |
| `created_at` | timestamptz | NOT NULL |
| `updated_at` | timestamptz | NOT NULL |

### 8.5 `assignments` column rename (Assignments context)

| column | was | now |
|---|---|---|
| `topic_id` | `subject_id` | `topic_id` (uuid, NOT NULL, FK → `topics.id` operational ref) |

All other `assignments` columns are unchanged.

### 8.6 Dropped table

| table | status |
|---|---|
| `grade_subject_assignments` | **DROPPED** — data absorbed into `topics` via migration backfill |

### 8.7 Enums

```csharp
namespace SchoolCollab.Students.Core.Domain;
public enum TopicOwnerType { GradeLevel = 0, ActivityGroup = 1 }
```

`SubjectAssignmentSource` is unchanged (`GradeAssignment = 0, IndividualAssignment = 1`).

---

## 9. Out of Scope

> Explicit exclusions with reasons. IDs: `OS-N`.

- **OS-1 (Multi-owner topics)** — A topic belonging to multiple grade levels
  simultaneously (the old `Subject` shared-across-grades model). **Reason:**
  the per-grade topic model is cleaner and more flexible; the coded-value
  system provides the conceptual link. Re-adding multi-owner would reintroduce
  the link table complexity we are eliminating.
- **OS-2 (Coded-value parent rename)** — Renaming the coded-value parent
  from "Subjects" to "Topics" in the Settings context. **Reason:** coded
  values are display metadata; renaming the parent is cosmetic and can be
  done independently. The `CodedValueId` on `Topic` still links to the
  existing parent.
- **OS-3 (AssignmentRecipient changes)** — The publish pipeline
  (`AssignmentRecipient`, contact resolution) is unchanged. Topic
  polymorphism only affects which students are targeted (via group
  membership), not how recipients are resolved.
- **OS-4 (Teacher-subject link rename)** — `TeacherSubject` (which links
  teachers to subjects) should be renamed to `TeacherTopic`, but this is a
  mechanical rename that can follow in a separate PR. It is not blocking
  the polymorphism.
- **OS-5 (Subject admin page full rewrite)** — The existing Subjects admin
  page (`Subjects.razor`, `SubjectCreateDialog.razor`, `SubjectEditDialog.razor`)
  will be renamed to Topics and extended with an ActivityGroup filter, but
  the full UX redesign (e.g., "create from existing coded value" flow) is
  deferred to a follow-up.

## 10. Affected files (indicative)

| Context | Path | Change |
|---|---|---|
| Students.Core | `Domain/Subject.cs` → `Domain/Topic.cs` | **rename + extend** (add OwnerType, OwnerId, PeriodId, Description, DefaultStrandId, DefaultLessonId; make Code/CodedValueId nullable) |
| Students.Core | `Domain/SubjectStrand.cs` → `Domain/TopicStrand.cs` | **rename** (SubjectId → TopicId) |
| Students.Core | `Domain/SubjectLesson.cs` → `Domain/TopicLesson.cs` | **rename** (SubjectId → TopicId, StrandId → TopicStrandId) |
| Students.Core | `Domain/GradeSubjectAssignment.cs` | **removed** (data absorbed into Topic) |
| Students.Core | `Domain/StudentSubjectAssignment.cs` → `Domain/StudentTopicAssignment.cs` | **rename** (SubjectId → TopicId) |
| Students.Core | `Domain/SubjectAssignmentSource.cs` | **unchanged** |
| Students.Core | `Domain/TopicOwnerType.cs` | **new** enum |
| Students.Core | `Domain/Events/*` | **rename** all Subject* events → Topic* events |
| Students.Core | `Domain/Exceptions/SubjectReferencedException.cs` → `TopicReferencedException.cs` | **rename** |
| Students.Core | `Data/Configurations/SubjectConfiguration.cs` → `TopicConfiguration.cs` | **rename + extend** (owner columns, partial indexes) |
| Students.Core | `Data/Configurations/SubjectStrandConfiguration.cs` → `TopicStrandConfiguration.cs` | **rename** |
| Students.Core | `Data/Configurations/SubjectLessonConfiguration.cs` → `TopicLessonConfiguration.cs` | **rename** |
| Students.Core | `Data/Configurations/GradeSubjectAssignmentConfiguration.cs` | **removed** |
| Students.Core | `Data/Repositories/*` | **rename** all Subject* repos → Topic* |
| Students.Core | `Data/StudentsDbContext.cs` | update DbSets (rename + remove GradeSubjectAssignment) |
| Students.Core | `CQRS/Subjects/*` → `CQRS/Topics/*` | **rename** all handlers, commands, queries |
| Students.Core | `CQRS/GradeSubjectAssignments/*` | **removed** (or repurposed as Topic.Create with owner) |
| Students.Core | `CQRS/StudentSubjectAssignments/*` → `CQRS/StudentTopicAssignments/*` | **rename** |
| Students.Core | `DTOs/SubjectDto.cs` → `DTOs/TopicDto.cs` | **rename + extend** |
| Students.Core | `DTOs/SubjectStrandDto.cs` → `DTOs/TopicStrandDto.cs` | **rename** |
| Students.Core | `DTOs/SubjectLessonDto.cs` → `DTOs/TopicLessonDto.cs` | **rename** |
| Students.Core | `DTOs/GradeSubjectAssignmentDto.cs` | **removed** |
| Students.Core | `Migrations/<ts>_SubjectToTopic.cs` | **new** migration |
| Students.Api | `Endpoints/SubjectRoutes.cs` → `TopicRoutes.cs` | **rename + extend** (owner params) |
| Students.Api | `Endpoints/GradeSubjectAssignmentRoutes.cs` | **removed** |
| Students.Api | `Endpoints/StudentSubjectAssignmentRoutes.cs` → `StudentTopicAssignmentRoutes.cs` | **rename** |
| Students.Admin | `Components/Pages/Students/Subjects/Subjects.razor` → `Topics/Topics.razor` | **rename + extend** (ActivityGroup filter) |
| Students.Admin | `Components/Students/SubjectCreateDialog.razor` → `TopicCreateDialog.razor` | **rename + extend** |
| Students.Admin | `Components/Students/SubjectEditDialog.razor` → `TopicEditDialog.razor` | **rename + extend** |
| Students.Admin | `Components/Students/GradeLevelFormFields.razor` | **update** (subject picker → topic picker) |
| Students.Contracts | `ContractTypes.cs` | **rename** Subject* → Topic* |
| Assignments.Core | `Domain/Assignment.cs` | **update** (SubjectId → TopicId, add owner-type validation) |
| Assignments.Core | `Data/Configurations/AssignmentConfiguration.cs` | **update** (column rename) |
| Assignments.Core | `CQRS/Assignments/Commands/*` | **update** (SubjectId → TopicId) |
| Assignments.Core | `CQRS/Assignments/Queries/*` | **update** |
| Assignments.Core | `DTOs/AssignmentSummary.cs` | **update** |
| Assignments.Core | `Migrations/<ts>_RenameSubjectToTopic.cs` | **new** migration |
| Assignments.Admin | `Components/Pages/Assignments/Create.razor` | **update** (subject picker → topic picker, filter by audience type) |
| Assignments.Admin | `Components/Pages/Assignments/Edit.razor` | **update** |
| Assignments.Admin | `Components/Pages/Assignments/Detail.razor` | **update** |
| Assignments.Contracts | `ContractTypes.cs` | **update** |
| Settings | coded-value parent "Subjects" | **unchanged** (OS-2) |
| Tests | `*Subject*` test files | **rename** → `*Topic*` |

**Total: ~209 files** (Students ~145, Assignments ~30, Tests ~25, Other ~9)

## 11. Verification

- `dotnet build SchoolCollab.sln` — 0 errors, 0 new warnings.
- `dotnet ef migrations add DiagnosePendingChanges` for both
  `StudentsDbContext` and `AssignmentsDbContext` → empty after the real
  migrations land.
- Unit tests (`SchoolCollab.Students.Tests.Unit`):
  - `StudentTopicAssignment.Create` (was `StudentSubjectAssignment.Create`) —
    entity + table renamed (FR-1/FR-13); the `NoUncommittedModelChanges` guard
    passes for `StudentsDbContext`.
  - `Topic.Create` — shared global definition (no owner columns) (AC-1, AC-3).
  - `GradeSubjectAssignment` retained as the M:N bridge, date-based (AC-12
    reversed — the bridge is NOT dropped under Design B).
  - `NoUncommittedModelChanges` passes for `StudentsDbContext`.
- bUnit: Topics admin page; assignment create/edit topic picker.
- Integration: backward-compatible API — `GET /api/subjects` returns
  `TopicDto` data, identical to `GET /api/topics` (AC-16 / NFR-6).

## 12. Open questions (resolved)

1. **Naming: full rename (Subject → Topic) vs. lite variant (keep "Subject")**
   ✅ **Decision: B (lite variant).** Keep the name `Subject`, add
   polymorphism via `OwnerType` + `OwnerId`. The word "Subject" already
   means "what an assignment is about" — it works for both grades and
   groups. The full rename (209 files) is deferred to a cosmetic PR if
   desired later. All references to "Topic" in this spec should be read
   as "Subject" during implementation — the entity stays `Subject`, the
   table stays `subjects`, and `Assignment.SubjectId` stays unchanged.
   Only the new columns (`owner_type`, `owner_id`, `period_id`,
   `description`, `default_strand_id`, `default_lesson_id`) and the
   nullable `code`/`coded_value_id` are added.

2. **GradeSubjectAssignment elimination confirmed?** ✅ **Decision: A
   (eliminate).** Each subject belongs to exactly one grade level
   (per-grade, per-period). "Mathematics for Grade 8" and "Mathematics for
   Grade 9" are two separate subject rows linked by the same
   `CodedValueId`. The coded-value system provides the conceptual link.

3. **StudentSubjectAssignment derivation** ✅ **Decision: proceed.**
   Derivation changes from `GradeSubjectAssignment` to `Subject` itself
   (if student enrolled in Grade 8 and subject has `OwnerType = GradeLevel,
   OwnerId = Grade8`). For group subjects, derive from group membership.
   Implementation detail resolved during build.

4. **Assignment topic picker UX** ✅ **Decision: audience-type-aware
   picker.** When `SelectedGrades`, show only grade-level subjects filtered
   by the selected grade + current period. When `SelectedGroups`, show
   only group subjects filtered by the linked groups. Better UX — the
   admin never sees subjects that would fail validation.

> All questions resolved. Implementation may proceed. Per Q1 (lite
> variant), the entity name stays `Subject` — no mechanical rename of
> 209 files is required. Only new columns, nullable constraints, and
> validation logic are added.

## 13. Implementation phases (one PR per step, each shippable behind the flag)

### Phase 1 — Students domain: extend Subject with polymorphism (dark)
- **No rename** (lite variant — entity stays `Subject`, table stays `subjects`)
- Add `OwnerType`, `OwnerId`, `PeriodId`, `Description`,
  `DefaultStrandId`, `DefaultLessonId` to `Subject`
- Make `Code` and `CodedValueId` nullable; convert unique index to partial
- New `SubjectOwnerType` enum
- Migration: add columns, backfill from `GradeSubjectAssignment`,
  drop `GradeSubjectAssignment`
- Remove `GradeSubjectAssignment` entity, CQRS, API, DTO
- Update `SubjectConfiguration` (owner columns, partial indexes)
- Add owner-type validation to `Subject.Create`
- Unit tests (AC-1..6, AC-10..14)
- **Flag OFF** (grade-level subjects work unflagged; group subjects gated)

### Phase 2 — Assignments: validation + subject picker (dark)
- **No rename** — `Assignment.SubjectId` stays as-is
- Add owner-type validation in `Assignment.Create` / `Update` (FR-14, FR-15)
- Update assignment create/edit admin pages (subject picker filtered by
  audience type — Q4 decision)
- Unit tests (AC-7..9, AC-15..16)
- **Flag OFF**

### Phase 3 — Activity groups (depends on Phase 1)
- `ActivityGroup` + `ActivityGroupMembership` entities, migration, CQRS, API
- (From `activity-group-enrollment.md` Phase 1-2, updated to reference
  `Topic` instead of `ActivityList`)

### Phase 4 — Assignment↔group link + publish (depends on Phase 2 + 3)
- `AssignmentActivityGroup` link entity
- Publish recipient resolution for `SelectedGroups`
- Topic owner-type validation for group assignments (FR-15)
- (From `activity-group-enrollment.md` Phase 3, updated)

### Phase 5 — Admin UI + Student Detail + flag flip (dark→lit)
- Topics admin page with ActivityGroup filter
- ActivityGroups admin pages (from `activity-group-enrollment.md` Phase 4)
- Student Detail page Activity Groups section (Phase 5 of that spec)
- Backward-compatible API alias (`/api/subjects` → `/api/topics`)
- Playwright smoke test
- `FEATURE:EnableActivityGroups` defaults ON for pilot tenant

---

### Traceability summary

- **25 FRs** (FR-1..25), **8 NFRs** (NFR-1..8), **16 ACs** (AC-1..16),
  **8 ECs** (EC-1..8), **5 OS items** (OS-1..5).
- Every entity in §8 maps to a requirement: `topics` → FR-1..12;
  `topic_strands` → FR-18; `topic_lessons` → FR-19;
  `student_topic_assignments` → FR-13 (renamed);
  `assignments.topic_id` → FR-13..17.
- `GradeSubjectAssignment` is eliminated (FR-12) — data absorbed into
  `topics` via migration backfill (NFR-3).
- `ActivityList` and `AssignmentActivityList` from
  `activity-group-enrollment.md` are superseded — a polymorphic `Topic`
  with `OwnerType = ActivityGroup` replaces both.
- Strands and lessons (FR-18..20) work uniformly for grade-level and
  activity-group topics — no code branching by owner type.
