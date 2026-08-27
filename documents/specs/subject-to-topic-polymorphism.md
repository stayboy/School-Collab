# Spec: Subject-to-Topic Polymorphism (Merge Grade-Subjects & Activity-Lists)

> **Status:** Authoritative = **Design B** (§0). The rename of `Subject` → `Topic`
> and of `SubjectStrand`/`SubjectLesson` → `TopicStrand`/`TopicLesson` stands.
> The body (§2 onward) reflects Design B: `Topic` is a shared, global
> definition with **no owner columns**, and `GradeSubjectAssignment` is the
> **retained** M:N bridge (Topic → GradeLevel or ActivityGroup). The earlier
> Design A (Topic carries `OwnerType`/`OwnerId`/`PeriodId`; bridge eliminated)
> and the "lite variant" (keep `Subject`, add owner columns) are both
> **superseded**. See §0.
> **Author:** Cline (spec-driven-workflow)
> **Date:** 2026-07-31 (reconciled to Design B 2026-08-26)
> **Reviewers:** Students context owner, Assignments context owner, Architecture
> **Owner contexts:** `SchoolCollab.Students.Core`, `SchoolCollab.Assignments.Core`,
> `SchoolCollab.Students.Admin`, `SchoolCollab.Assignments.Admin`
> **Depends on:** `grade-level-setup.md`, `activity-group-enrollment.md`,
> `global-tenant-filter.md`, `ef-migrations.md`, `endpoint-organization-pattern.md`
> **Supersedes:** Activity List sections (§3.7, §7.4, §7.5, §8.4, §8.5, EC-15..18,
> AC-32..34) of `activity-group-enrollment.md`

---

## 0. Decisions locked in this revision

> **Revision note (2026-08-03, reconciled 2026-08-26):** The earlier polymorphic
> decision (Topic carries `OwnerType`/`OwnerId`/`PeriodId` **and** retains
> `GradeSubjectAssignment` as a bridge) was found to be **incoherent** — the
> bridge was redundant with `Topic.OwnerId`. It was reversed to **Design B**.
> The body of this spec has been reconciled to Design B; the decisions below are
> the **current**, authoritative model.

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

   > **Amended by `activity-group-enrollment.md` Rev. 6:** the bridge gains an
   > **optional** nullable `PeriodId` (FK → `periods`) to align a topic
   > assignment to a term/semester/academic-year period for enrollment-span-
   > aware delivery. `PeriodId = null` = this date-based, year-spanning
   > behavior (unchanged); non-null = the topic is delivered during that
   > specific period. The amendment is additive (existing rows stay NULL).

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
`GradeSubjectAssignment` as the **retained** M:N bridge that assigns a topic to
a grade or group and selects the strand/lesson that grade/group uses. This
eliminates the parallel `ActivityList` entity from
`activity-group-enrollment.md` and unifies strands, lessons, reporting, and
admin UI under one shared model.

> **Note:** The body (§2 onward) reflects Design B (§0): `Topic` is a shared,
> global definition with no owner columns; `GradeSubjectAssignment` is the
> retained M:N bridge.

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

### 2.3 Target state (Design B)

```
Topic (was Subject) — shared, global, tenant-scoped catalog entry (no owner columns)
├── Code: string?            (nullable — group-bridge topics may have no code)
├── CodedValueId: Guid?       (nullable — group-bridge topics are free-text)
├── Name, Description, DisplayOrder   (existing; Description is NEW)
├── TopicStrand (1:N — was SubjectStrand, renamed)
│   └── TopicLesson (1:N — was SubjectLesson, renamed)
└── StudentTopicAssignment (was StudentSubjectAssignment, renamed)

GradeSubjectAssignment — RETAINED as the M:N bridge (Topic → GradeLevel OR ActivityGroup)
├── GradeLevelId: Guid?      (set for grade-level assignments)
├── ActivityGroupId: Guid?  (set for activity-group assignments; NEW column)
├── TopicId: Guid            (FK → topics.id; was SubjectId, renamed)
├── StartDate: date           (effective window start)
├── EndDate: date?            (null = currently active — decision 2a)
├── TopicStrandId: Guid?      (selects the strand for this grade/group; was SubjectStrandId, renamed)
├── TopicLessonId: Guid?      (selects the lesson; was SubjectLessonId, renamed)
└── PeriodId: Guid?           (optional — Rev. 6 alignment; null = year-spanning)
   At most one of GradeLevelId / ActivityGroupId is set per row.

ELIMINATED (from activity-group-enrollment.md):
  - ActivityList (§3.7)
  - AssignmentActivityList (§7.5)
  - activity_lists table (§8.4)
  - assignment_activity_lists table (§8.5)
```

### 2.4 Why retain GradeSubjectAssignment as the M:N bridge

`GradeSubjectAssignment` is a many-to-many link: one global `Topic` can be
assigned to multiple grades across multiple periods, and to one or more
activity groups (one bridge row per grade+period or per group). The topic
itself stays a single shared, global definition.

Design B keeps the bridge rather than folding the owner into `Topic`:

- A topic is a **shared catalog entry** (e.g., "Mathematics"); the per-grade /
  per-group offering is the **bridge row**, carrying the period (optional, per
  Rev. 6), the selected strand/lesson, and the effective date window. This
  avoids duplicating "Mathematics" once per grade+period.
- `Assignment.TopicId` references the shared topic; the bridge resolves which
  grade/group offers it and when. Owner-matching validation (FR-14/15) reads
  the bridge, not the topic.
- Activity-group topics follow the same bridge pattern (one bridge row per
  group, `ActivityGroupId` set; `PeriodId` optional per Rev. 6 alignment).

The cost is that offering a subject to multiple grades creates multiple
bridge rows (one per grade+period), all pointing at the same `Topic`. The
admin UI can offer "create from existing coded value" to streamline this.

---

## 3. Functional Requirements

> RFC 2119 keywords (MUST / MUST NOT / SHOULD / MAY) are used precisely.
> Each requirement is atomic and testable. IDs: `FR-N`.

### 3.1 Topic lifecycle (rename Subject)

- **FR-1** — The entity currently named `Subject` MUST be renamed to `Topic`
  (see §12 Q1 for the naming decision). The `subjects` table MUST be renamed
  to `topics`. All references (`SubjectId`, `SubjectStrand`, `SubjectLesson`,
  `StudentSubjectAssignment`, etc.) MUST be renamed accordingly
  (`TopicId`, `TopicStrand`, `TopicLesson`, `StudentTopicAssignment`).
- **FR-2** — `Topic` MUST retain all existing properties: `Name`, `Code`,
  `DisplayOrder`, `CodedValueId`, `TenantId`, `RowVersion`, `CreatedAt`,
  `UpdatedAt`, domain events, and the `Create` / `Update` / `Delete`
  factory methods. The existing `SubjectReferencedException` MUST be renamed
  to `TopicReferencedException`. `Topic` carries **no owner columns** and no
  `DefaultStrandId`/`DefaultLessonId` (Design B — those live on the bridge).
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

### 3.2 GradeSubjectAssignment bridge (owner link — Design B)

- **FR-6** — `GradeSubjectAssignment` MUST be retained as the M:N bridge
  connecting a `Topic` to a `GradeLevel` **or** an `ActivityGroup`. It MUST
  gain a nullable `ActivityGroupId` (operational ref to `ActivityGroup`)
  alongside the existing `GradeLevelId`; at most one of `GradeLevelId` /
  `ActivityGroupId` MUST be set per row.
- **FR-7** — The bridge's `SubjectId` column MUST be renamed to `TopicId`
  (FK → `topics.id`). The optional `SubjectStrandId` / `SubjectLessonId`
  MUST be renamed to `TopicStrandId` / `TopicLessonId`.
- **FR-8** — The bridge MUST carry the effective window `[StartDate, EndDate?]`
  (date-based, not period-bound — decision 2a). A null `EndDate` means
  currently active; blocking/removing a topic-from-a-grade sets `EndDate`
  (no status enum, no hard delete — history preserved).
- **FR-9** — The bridge MUST carry the optional per-grade/group strand and
  lesson selection (`TopicStrandId?`, `TopicLessonId?`). These stay on the
  bridge — they are **not** moved to `Topic`.
- **FR-10** — The system MUST support creating a bridge row with
  `ActivityGroupId` set (`GradeLevelId` null) to offer a topic to an activity
  group. Such a row MUST NOT require a `PeriodId` (groups outlast periods —
  activity-group-enrollment FR-3); `PeriodId` is set only per the Rev. 6
  alignment (FR-55..58 of `activity-group-enrollment.md`).
- **FR-11** — The system MUST reject creating a bridge row if the referenced
  `GradeLevel` or `ActivityGroup` does not exist, is off (`IsActive = false`
  for a group — activity-group-enrollment FR-12), or belongs to a different
  tenant.
- **FR-12** — `GradeSubjectAssignment` MUST be retained (NOT removed). Its
  entity, table, CQRS handlers, API endpoints, and admin UI are **extended**
  (add `ActivityGroupId`, rename `Subject*` → `Topic*`, add optional
  `PeriodId` per Rev. 6). No data is absorbed into `Topic`; the bridge rows
  are preserved and extended in place.

### 3.3 Assignment topic validation

- **FR-13** — `Assignment.SubjectId` (renamed `Assignment.TopicId` — see
  §12 Q1) MUST remain required. The `Assignment.Create()` factory MUST
  continue to throw if `topicId == Guid.Empty`.
- **FR-14** — When `Assignment.TargetAudienceType = SelectedGrades`, the
  system MUST validate via the bridge that the topic is assigned
  (`GradeLevelId` set) to `Assignment.GradeLevelId` for a period covering the
  assignment's effective date (a null-`PeriodId` year-spanning bridge row
  active on that date, or a period-aligned row per Rev. 6). A mismatch MUST
  be rejected. (The topic itself has no owner columns under Design B — the
  bridge is the source of truth.)
- **FR-15** — When `Assignment.TargetAudienceType = SelectedGroups`, the
  system MUST validate via the bridge that the topic is assigned
  (`ActivityGroupId` set) to one of the assignment's linked `ActivityGroup`s
  (via `AssignmentActivityGroup`) for the relevant enrollment period. A
  mismatch MUST be rejected. This replaces the `AssignmentActivityList` link
  table from `activity-group-enrollment.md`.
- **FR-16** — When `Assignment.TargetAudienceType = AllStudents`, no
  bridge owner-matching validation is applied (the topic may be
  bridge-assigned to any grade or group).
- **FR-17** — `SelectedGrades` MUST continue to use the existing scalar
  `Assignment.GradeLevelId` unchanged. `SelectedGroups` MUST continue to use
  the `AssignmentActivityGroup` link table (from
  `activity-group-enrollment.md`). Neither code path is altered beyond the
  `SubjectId` → `TopicId` rename and the bridge-based validation.

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
- **FR-20** — Strands and lessons MUST work identically for topics
  bridge-assigned to a grade or an activity group. A topic bridge-assigned
  to an activity group MAY have strands (e.g., "Strategy", "Opening Theory")
  and lessons (e.g., "Sicilian Defense", "King-Pawn Endgames") exactly as a
  grade-level topic would. No additional validation or code branching is
  required.

### 3.5 Reads / queries

- **FR-21** — The existing query "list subjects by grade level + period"
  MUST be replaced with "list topics assigned to a grade via the bridge"
  (`GradeSubjectAssignment` rows for `GradeLevelId`, optionally filtered by
  `PeriodId` / effective date). The API endpoint MUST accept `gradeLevelId`
  and optional `periodId` / `effectiveDate` query parameters.
- **FR-22** — A new query MUST be supported: "list topics assigned to an
  activity group via the bridge" (`GradeSubjectAssignment` rows for
  `ActivityGroupId`). This replaces the `GET /api/activity-groups/{groupId}/lists`
  endpoint from `activity-group-enrollment.md` §7.4.
- **FR-23** — All topic/bridge queries MUST be tenant-filtered via the global
  tenant filter (`global-tenant-filter.md` §3.2 Strict).

### 3.6 Domain events

- **FR-24** — All existing domain events MUST be renamed: `SubjectCreatedEvent`
  → `TopicCreatedEvent`, `SubjectUpdatedEvent` → `TopicUpdatedEvent`,
  `SubjectDeletedEvent` → `TopicDeletedEvent`, `SubjectStrandCreatedEvent`
  → `TopicStrandCreatedEvent`, etc.
- **FR-25** — Creating a topic MUST emit a `TopicCreatedEvent` regardless of
  whether it is later bridge-assigned to a grade or a group — no owner-type
  branching in events. (Bridge assignment may emit its own event; out of scope
  here.)

---

## 4. Non-Functional Requirements

> All thresholds are measurable. IDs: `NFR-N`.

- **NFR-1 (Non-breaking migration sequence)** — The migration MUST be
  sequenced so that the Students context migration lands first (table rename
  `subjects` → `topics`; bridge extension: add `activity_group_id`, rename
  `subject_*` → `topic_*`), then the Assignments context migration (column
  rename `subject_id` → `topic_id`). The optional Rev. 6 `period_id` alignment
  (making the existing `period_id` nullable + adding the alignment semantics)
  is a separate additive migration owned by `activity-group-enrollment-impl.md`
  Phase 11, landing after this rename. Both migrations MUST be independently
  deployable behind the `FEATURE:EnableActivityGroups` flag.
- **NFR-2 (Migration guard)** — A `NoUncommittedModelChanges` unit test
  MUST be updated for `StudentsDbContext` and `AssignmentsDbContext` per
  `ef-migrations.md`. The test MUST pass after the migration lands.
- **NFR-3 (Data preservation)** — The migration MUST NOT lose any data.
  Existing `subjects` rows MUST become `topics` rows (rename only — **no owner
  columns** are added to `topics`). Existing `grade_subject_assignments` rows
  MUST be preserved in place and extended (`subject_id` → `topic_id`,
  `subject_strand_id` → `topic_strand_id`, `subject_lesson_id` →
  `topic_lesson_id`; `grade_level_id` retained; `activity_group_id` added
  nullable; `period_id` retained as-is — the Rev. 6 amendment (Phase 11) later
  makes it nullable).
- **NFR-4 (Indexing)** — The existing index `ix_subjects_tenant_code`
  MUST become a partial unique index `ix_topics_tenant_code` with
  `WHERE code IS NOT NULL`. Bridge indexes MUST lead with `tenant_id`:
  `(tenant_id, grade_level_id, topic_id)` and
  `(tenant_id, activity_group_id, topic_id)` for the hot-path owner queries.
- **NFR-5 (Tenancy)** — All topic/strand/lesson/bridge reads and writes MUST
  remain strict-tenant-filtered (`global-tenant-filter.md` §3.2 Strict).
  No cross-tenant access is permitted.
- **NFR-6 (Backward compatibility)** — The API MUST maintain backward
  compatibility for the `/api/subjects` endpoint by aliasing it to
  `/api/topics` during a deprecation window. Existing clients that call
  `/api/subjects` MUST continue to work, returning `TopicDto` data under
  the old route name.
- **NFR-7 (Auditability)** — `CreatedAt` / `UpdatedAt` MUST be present
  and populated on all renamed entities (unchanged from existing behavior).
- **NFR-8 (Feature flag)** — The activity-group bridge surface
  (`ActivityGroupId` on the bridge, group-bridge validation) MUST be gated
  behind `FEATURE:EnableActivityGroups` (same flag as activity groups).
  Grade-level bridge behavior MUST work regardless of the flag (it is the
  existing, unflagged behavior, just renamed).

---

## 5. Acceptance Criteria

> Given / When / Then. Every criterion references at least one `FR-*` or
> `NFR-*`. IDs: `AC-N`.

- **AC-1 (grade-level topic retained)** — **Given** the migration has
  run, **When** the admin queries topics assigned to Grade 8 in the current
  period, **Then** the topics returned are the same subjects that were
  previously assigned to Grade 8 (now bridge rows with `GradeLevelId = G8`,
  `TopicId` = the renamed subject). *(FR-6, FR-8, NFR-3)*
- **AC-2 (assignment topic retained)** — **Given** an existing assignment
  with `SubjectId = S1`, **When** the migration completes, **Then** the
  assignment now has `TopicId = S1` (same Guid, renamed column) and the
  topic is found in the `topics` table. *(FR-13, NFR-3)*
- **AC-3 (activity-group topic via bridge)** — **Given** an activity group
  G, **When** the admin creates a topic "Tournament Prep" (no code, no coded
  value) and a bridge row assigning it to G, **Then** the topic is persisted
  with `Code = null`, `CodedValueId = null`, and the bridge row has
  `ActivityGroupId = G`, `GradeLevelId = null`, `PeriodId = null`.
  *(FR-3, FR-4, FR-10)*
- **AC-4 (partial unique index on code)** — **Given** two topics in the
  same tenant both have `Code = null`, **When** they are persisted,
  **Then** no unique-constraint violation occurs (the partial index only
  applies to non-null codes). *(FR-3, NFR-4)*
- **AC-5 (group bridge rejects off group)** — **Given** activity group G is
  off (`IsActive = false`), **When** the admin creates a bridge row to G,
  **Then** the request is rejected. *(FR-11)*
- **AC-6 (group bridge rejects cross-tenant group)** — **Given** an
  activity group from tenant T2, **When** the admin in tenant T1 creates a
  bridge row referencing it, **Then** the request is rejected. *(FR-11, NFR-5)*
- **AC-7 (SelectedGrades bridge validation)** — **Given** an assignment with
  `TargetAudienceType = SelectedGrades` and `GradeLevelId = G8`, **When**
  the assignment's topic is not bridge-assigned to G8 for the effective date,
  **Then** the mismatch is rejected with a validation error. *(FR-14)*
- **AC-8 (SelectedGroups bridge validation)** — **Given** an assignment with
  `TargetAudienceType = SelectedGroups` linked to group G1, **When** the
  assignment's topic is not bridge-assigned to G1, **Then** the mismatch is
  rejected. *(FR-15)*
- **AC-9 (SelectedGroups topic matches linked group)** — **Given** an
  assignment with `TargetAudienceType = SelectedGroups` linked to groups
  G1 and G2, **When** the assignment's topic is bridge-assigned to G2,
  **Then** validation passes. *(FR-15)*

- **AC-10 (strands under group-bridged topic)** — **Given** a topic
  bridge-assigned to group G, **When** the admin creates a strand
  "Opening Theory" under that topic, **Then** the strand is persisted with
  `TopicId` referencing the topic and is queryable. *(FR-18, FR-20)*
- **AC-11 (lessons under group-bridged topic strand)** — **Given** a strand
  "Opening Theory" under a topic bridge-assigned to a group, **When** the
  admin creates a lesson "Sicilian Defense" linked to that strand, **Then**
  the lesson is persisted and queryable. *(FR-19, FR-20)*
- **AC-12 (GradeSubjectAssignment retained as bridge)** — **Given** the
  migration has run, **When** the admin queries the database, **Then** the
  `grade_subject_assignments` table still exists, extended with
  `activity_group_id` (nullable) and renamed `topic_*` columns (and optional
  `period_id` per Rev. 6); its rows are preserved. *(FR-6, FR-12, NFR-3)*
- **AC-13 (topic query by group)** — **Given** activity group G has bridge
  rows for topics T1 and T2, **When** the admin queries topics for group G
  (via the bridge), **Then** T1 and T2 are returned and no grade-only topics
  appear. *(FR-22, FR-23)*
- **AC-14 (topic query by grade)** — **Given** Grade 8 has a bridge row for
  topic T3 in period P, **When** the admin queries topics for Grade 8 with
  period P, **Then** T3 is returned. *(FR-21, FR-23)*
- **AC-15 (AllStudents no owner validation)** — **Given** an assignment
  with `TargetAudienceType = AllStudents`, **When** the assignment's topic is
  bridge-assigned to an activity group, **Then** no owner-matching validation
  error occurs. *(FR-16)*
- **AC-16 (backward-compatible API route)** — **Given** a client calls
  `/api/subjects` (old route), **When** the request is processed,
  **Then** the response returns `TopicDto` data (same shape, route is
  aliased). *(NFR-6)*

---

## 6. Edge Cases

> Numbered `EC-N`. At least one failure mode per external dependency
> (DB, user input, cross-context reference, migration).

- **EC-1 (owner mismatch on assignment)** — An assignment with
  `SelectedGrades` whose topic is bridge-assigned only to an `ActivityGroup`
  MUST be rejected. Conversely, `SelectedGroups` with a topic bridge-assigned
  only to a `GradeLevel` MUST be rejected. *(FR-14, FR-15)*
- **EC-2 (non-existent owner)** — Creating a bridge row whose
  `GradeLevelId` / `ActivityGroupId` references a `GradeLevel` or
  `ActivityGroup` that does not exist MUST be rejected with a 404/validation
  error. *(FR-11)*
- **EC-3 (multi-grade via bridge)** — When the same coded value is used
  for topics bridge-assigned to different grade levels, each bridge row is
  independent. Updating one (e.g., selected strand) MUST NOT affect the
  others. The `CodedValueId` on `Topic` is the conceptual link, not a
  uniqueness constraint.
- **EC-4 (null code on grade-level topic)** — A grade-level topic with
  `Code = null` MUST NOT be rejected (the partial unique index allows it),
  but the admin UI SHOULD warn that a code is recommended for grade-level
  topics for reporting consistency.
- **EC-5 (strand references different topic)** — A `TopicStrand` with
  `TopicId` referencing a topic that has been deleted MUST be cascade-deleted
  (existing behavior, unchanged — `OnDelete(Cascade)` on the FK).
- **EC-6 (orphaned subjects)** — If a `Subject` has no
  `GradeSubjectAssignment` (orphaned), the migration MUST still rename it to a
  `topics` row (no bridge row is created for it). The admin SHOULD be able to
  create bridge rows for it post-migration. This is a data-quality issue, not
  a migration failure.
- **EC-7 (assignment references deleted topic)** — Because topic deletes are
  referentially guarded (`TopicReferencedException` when referenced by a
  `Draft`/`Published` assignment), a deleted topic has no live assignment
  references — no orphan rows can occur in normal operation.
- **EC-8 (StudentTopicAssignment owner via bridge)** — A `StudentTopicAssignment`
  (was `StudentSubjectAssignment`) references a topic. If the topic is
  bridge-assigned to an `ActivityGroup`, the student MUST be an active member
  of that group. This validation is deferred to the activity-group-enrollment
  spec (membership checks).

---

## 7. API Contracts

> TypeScript-style interfaces. Success and error responses are both shown.
> Routes follow the existing `endpoint-organization-pattern.md`.
> Base path `/api` implied. Old route `/api/subjects` is aliased to
> `/api/topics` for backward compatibility (NFR-6).

### 7.1 Topic CRUD — Students API

> **Design B.** `Topic` is a shared, global definition with no owner columns.
> The owner/grading link lives on the `GradeSubjectAssignment` M:N bridge
> (§8.1b). There is no `ownerType`/`ownerId`/`periodId` query surface on
> topics; grade/group-filtered reads go through the bridge
> (`GET /topics/by-grade/{gradeLevelId}`, `GET /topics/by-group/{groupId}`).
> The `/subjects` prefix is a deprecated backward-compatible alias for
> `/topics` (NFR-6 / AC-16).

```ts
// POST   /api/topics                           -> 201 TopicDto | 409
// GET    /api/topics                           -> 200 TopicDto[]
// GET    /api/topics/{id}                       -> 200 TopicDto | 404
// GET    /api/topics/by-code/{code}             -> 200 TopicDto | 404
// GET    /api/topics/by-grade/{gradeLevelId}?effectiveDate=&periodId= -> 200 TopicDto[]
// GET    /api/topics/by-group/{groupId}         -> 200 TopicDto[]
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

### 7.2 GradeSubjectAssignment bridge — Students API

> The bridge carries the owner link (grade or group), the effective window,
> the selected strand/lesson, and the optional Rev. 6 `PeriodId`.

```ts
// POST   /api/grade-subject-assignments              -> 201 BridgeDto | 404 | 422
// GET    /api/grade-subject-assignments?gradeLevelId= -> 200 BridgeDto[]
// GET    /api/grade-subject-assignments?activityGroupId= -> 200 BridgeDto[]
// PUT    /api/grade-subject-assignments/{id}         -> 204 | 404 | 422
// DELETE /api/grade-subject-assignments/{id}        -> 204 | 404 | 409 (ends the row — sets EndDate)

interface BridgeDto {
  id: string;
  topicId: string;
  gradeLevelId: string | null;
  activityGroupId: string | null;
  startDate: string;
  endDate: string | null;
  topicStrandId: string | null;
  topicLessonId: string | null;
  periodId: string | null;   // Rev. 6
  createdAt: string;
  updatedAt: string;
}

interface CreateBridgeRequest {
  topicId: string;
  gradeLevelId?: string | null;
  activityGroupId?: string | null;  // exactly one of gradeLevelId/activityGroupId required
  startDate: string;
  endDate?: string | null;
  topicStrandId?: string | null;
  topicLessonId?: string | null;
  periodId?: string | null;         // Rev. 6
}
```

### 7.3 Strands and lessons — Students API

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

> Design B: `Topic` is a shared, global definition. It carries **no owner
> columns** and **no per-grade default strand/lesson** — those live on the
> bridge (§8.1b).

| field | type | constraints |
|---|---|---|
| `id` | uuid | PK |
| `tenant_id` | uuid | NOT NULL; strict-tenant filter |
| `code` | text | NULL, <= 50; was NOT NULL — now nullable (group-bridge topics may have no code) |
| `coded_value_id` | uuid | NULL; was NOT NULL — now nullable (group-bridge topics are free-text) |
| `name` | text | NOT NULL, <= 200 |
| `description` | text | NULL, <= 2000; NEW (replaces ActivityList.description) |
| `display_order` | integer | NOT NULL |
| `xmin` | xid | row version (PostgreSQL) |
| `created_at` | timestamptz | NOT NULL |
| `updated_at` | timestamptz | NOT NULL |

Indexes:
- **Partial unique** `(tenant_id, code)` WHERE `code IS NOT NULL` →
  `ix_topics_tenant_code` (was `ix_subjects_tenant_code`, now partial)
- `(tenant_id, coded_value_id)` WHERE `coded_value_id IS NOT NULL` →
  `ix_topics_tenant_coded_value` (partial)

### 8.1b `grade_subject_assignments` (Students context — RETAINED + extended bridge)

> Design B: the bridge is the M:N link from `Topic` to `GradeLevel` OR
> `ActivityGroup`. It is **not** dropped.

| field | type | constraints |
|---|---|---|
| `id` | uuid | PK |
| `tenant_id` | uuid | NOT NULL; strict-tenant filter |
| `grade_level_id` | uuid | NULL; set for grade-level assignments |
| `activity_group_id` | uuid | NULL; set for activity-group assignments (NEW) |
| `topic_id` | uuid | NOT NULL; FK → `topics.id` (was `subject_id`, renamed) |
| `start_date` | date | NOT NULL; effective window start |
| `end_date` | date | NULL; null = currently active (decision 2a) |
| `topic_strand_id` | uuid | NULL; FK → `topic_strands.id` (was `subject_strand_id`, renamed) |
| `topic_lesson_id` | uuid | NULL; FK → `topic_lessons.id` (was `subject_lesson_id`, renamed) |
| `period_id` | uuid | NULL; FK → `periods.id`; optional (Rev. 6 — aligns the assignment to a term/semester/academic-year period; null = year-spanning) |
| `xmin` | xid | row version |
| `created_at` | timestamptz | NOT NULL |
| `updated_at` | timestamptz | NOT NULL |

Constraints: at most one of `grade_level_id` / `activity_group_id` is set per
row. Indexes (NFR-4): `(tenant_id, grade_level_id, topic_id)`,
`(tenant_id, activity_group_id, topic_id)`, `(tenant_id, topic_id)`.

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

### 8.6 Dropped tables (activity-group-enrollment only)

| table | status |
|---|---|
| `activity_lists` | **DROPPED** — from `activity-group-enrollment.md` §8.4 (superseded by the bridge) |
| `assignment_activity_lists` | **DROPPED** — from `activity-group-enrollment.md` §8.5 |

> `grade_subject_assignments` is **retained** as the bridge (§8.1b) — it is
> NOT dropped under Design B.

### 8.7 Enums

`SubjectAssignmentSource` is unchanged (`GradeAssignment = 0, IndividualAssignment = 1`).

> Design B: there is no `TopicOwnerType` enum — `Topic` carries no owner
> columns. The bridge distinguishes grade vs. group by which of
> `grade_level_id` / `activity_group_id` is set.

---

## 9. Out of Scope

> Explicit exclusions with reasons. IDs: `OS-N`.

- **OS-1 (Owner columns on `Topic`)** — Putting
  `OwnerType`/`OwnerId`/`PeriodId` directly on `Topic` (the rejected Design A).
  **Reason:** it duplicates the topic once per grade+period, makes the
  bridge redundant, and makes `Assignment.TopicId` reference a per-grade
  offering instead of a shared definition. Design B keeps `Topic` global and
  the owner on the `GradeSubjectAssignment` bridge (§8.1b).
- **OS-2 (Coded-value parent rename)** — Renaming the coded-value parent
  from "Subjects" to "Topics" in the Settings context. **Reason:** coded
  values are display metadata; renaming the parent is cosmetic and can be
  done independently. The `CodedValueId` on `Topic` still links to the
  existing parent.
- **OS-3 (AssignmentRecipient changes)** — The publish pipeline
  (`AssignmentRecipient`, contact resolution) is unchanged. The bridge only
  affects which students are targeted (via group membership), not how
  recipients are resolved.
- **OS-4 (Teacher-subject link rename)** — `TeacherSubject` (which links
  teachers to subjects) should be renamed to `TeacherTopic`, but this is a
  mechanical rename that can follow in a separate PR. It is not blocking
  the merge.
- **OS-5 (Subject admin page full rewrite)** — The existing Subjects admin
  page (`Subjects.razor`, `SubjectCreateDialog.razor`, `SubjectEditDialog.razor`)
  will be renamed to Topics and extended with an ActivityGroup filter, but
  the full UX redesign (e.g., "create from existing coded value" flow) is
  deferred to a follow-up.

---

## 10. Affected files (indicative)

| Context | Path | Change |
|---|---|---|
| Students.Core | `Domain/Subject.cs` → `Domain/Topic.cs` | **rename + extend** (add `Description`; make `Code`/`CodedValueId` nullable; **no owner columns**) |
| Students.Core | `Domain/SubjectStrand.cs` → `Domain/TopicStrand.cs` | **rename** (SubjectId → TopicId) |
| Students.Core | `Domain/SubjectLesson.cs` → `Domain/TopicLesson.cs` | **rename** (SubjectId → TopicId, StrandId → TopicStrandId) |
| Students.Core | `Domain/GradeSubjectAssignment.cs` | **retained + extended** (add `ActivityGroupId`, rename `SubjectId`→`TopicId`, `SubjectStrandId`→`TopicStrandId`, `SubjectLessonId`→`TopicLessonId`, add optional `PeriodId` per Rev. 6) |
| Students.Core | `Domain/StudentSubjectAssignment.cs` → `Domain/StudentTopicAssignment.cs` | **rename** (SubjectId → TopicId) |
| Students.Core | `Domain/SubjectAssignmentSource.cs` | **unchanged** |
| Students.Core | `Domain/Events/*` | **rename** all Subject* events → Topic* events |
| Students.Core | `Domain/Exceptions/SubjectReferencedException.cs` → `TopicReferencedException.cs` | **rename** |
| Students.Core | `Data/Configurations/SubjectConfiguration.cs` → `TopicConfiguration.cs` | **rename + extend** (`Description`, nullable `Code`/`CodedValueId`, partial indexes) |
| Students.Core | `Data/Configurations/SubjectStrandConfiguration.cs` → `TopicStrandConfiguration.cs` | **rename** |
| Students.Core | `Data/Configurations/SubjectLessonConfiguration.cs` → `TopicLessonConfiguration.cs` | **rename** |
| Students.Core | `Data/Configurations/GradeSubjectAssignmentConfiguration.cs` | **retained + extended** (`ActivityGroupId`, `TopicId` rename, `TopicStrandId`/`TopicLessonId` rename, optional `PeriodId`, owner indexes, at-most-one-owner constraint) |
| Students.Core | `Data/Repositories/*` | **rename** all Subject* repos → Topic*; retain + extend `GradeSubjectAssignment` repo |
| Students.Core | `Data/StudentsDbContext.cs` | update DbSets (rename; **retain** `GradeSubjectAssignment` DbSet) |
| Students.Core | `CQRS/Subjects/*` → `CQRS/Topics/*` | **rename** all handlers, commands, queries |
| Students.Core | `CQRS/GradeSubjectAssignments/*` | **retained + extended** (bridge create/update/list by grade/group, `ActivityGroupId`, `PeriodId`) |
| Students.Core | `CQRS/StudentSubjectAssignments/*` → `CQRS/StudentTopicAssignments/*` | **rename** |
| Students.Core | `DTOs/SubjectDto.cs` → `DTOs/TopicDto.cs` | **rename + extend** (`Description`) |
| Students.Core | `DTOs/SubjectStrandDto.cs` → `DTOs/TopicStrandDto.cs` | **rename** |
| Students.Core | `DTOs/SubjectLessonDto.cs` → `DTOs/TopicLessonDto.cs` | **rename** |
| Students.Core | `DTOs/GradeSubjectAssignmentDto.cs` | **retained + extended** (bridge DTO: `ActivityGroupId`, `TopicId`, `PeriodId`) |
| Students.Core | `Migrations/<ts>_SubjectToTopic.cs` | **new** migration (rename + bridge extension) |
| Students.Api | `Endpoints/SubjectRoutes.cs` → `TopicRoutes.cs` | **rename** (+ backward-compat `/subjects` alias; bridge-aware by-grade/by-group queries) |
| Students.Api | `Endpoints/GradeSubjectAssignmentRoutes.cs` | **retained + extended** (bridge endpoints, group owner) |
| Students.Api | `Endpoints/StudentSubjectAssignmentRoutes.cs` → `StudentTopicAssignmentRoutes.cs` | **rename** |
| Students.Admin | `Components/Pages/Students/Subjects/Subjects.razor` → `Topics/Topics.razor` | **rename + extend** (ActivityGroup filter via bridge) |
| Students.Admin | `Components/Students/SubjectCreateDialog.razor` → `TopicCreateDialog.razor` | **rename + extend** |
| Students.Admin | `Components/Students/SubjectEditDialog.razor` → `TopicEditDialog.razor` | **rename + extend** |
| Students.Admin | `Components/Students/GradeLevelFormFields.razor` | **update** (subject picker → topic picker; bridge-aware) |
| Students.Contracts | `ContractTypes.cs` | **rename** Subject* → Topic* |
| Assignments.Core | `Domain/Assignment.cs` | **update** (SubjectId → TopicId; bridge-based owner validation) |
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

---

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
  - `GradeSubjectAssignment` retained as the M:N bridge, date-based, extended
    with `ActivityGroupId` + optional `PeriodId` (AC-12 reversed — the bridge
    is NOT dropped under Design B; FR-6/FR-12).
  - `NoUncommittedModelChanges` passes for `StudentsDbContext`.
- bUnit: Topics admin page; assignment create/edit topic picker.
- Integration: backward-compatible API — `GET /api/subjects` returns
  `TopicDto` data, identical to `GET /api/topics` (AC-16 / NFR-6).

---

## 12. Open questions (resolved)

1. **Naming: full rename (Subject → Topic) vs. lite variant (keep "Subject")**
   ✅ **Decision: full rename (Subject → Topic).** The rename stands (see
   header): `Subject` → `Topic`, `SubjectStrand` → `TopicStrand`,
   `SubjectLesson` → `TopicLesson`, `subjects` → `topics`,
   `Assignment.SubjectId` → `Assignment.TopicId`. `Topic` is a shared global
   definition with **no owner columns** (Design B); ownership lives on the
   retained `GradeSubjectAssignment` bridge (§8.1b). The earlier "lite
   variant / keep `Subject` / add owner columns" is **superseded**.
2. **GradeSubjectAssignment retained or eliminated?** ✅ **Decision: retain
   as the M:N bridge (Design B).** `GradeSubjectAssignment` is kept and
   extended (`activity_group_id` added, `subject_*` → `topic_*` renamed,
   optional `period_id` per Rev. 6). It connects a shared `Topic` to a
   `GradeLevel` or `ActivityGroup`; at most one owner per row. Eliminating it
   (Design A) is rejected — it would duplicate the topic per grade+period and
   make `Assignment.TopicId` reference a per-grade offering instead of a
   shared definition.
3. **StudentTopicAssignment derivation** ✅ **Decision: proceed.** Derivation
   reads the bridge: a `StudentTopicAssignment` is derived when the student is
   enrolled in a grade (or is an active member of a group) that has an active
   `GradeSubjectAssignment` bridge row for the topic (effective on the
   period). For group-bridged topics, derive from group membership.
   Implementation detail resolved during build.
4. **Assignment topic picker UX** ✅ **Decision: audience-type-aware
   picker.** When `SelectedGrades`, show only topics bridge-assigned to the
   selected grade + current period. When `SelectedGroups`, show only topics
   bridge-assigned to the linked groups. Better UX — the admin never sees
   topics that would fail validation.

> All questions resolved. Implementation may proceed. Per Q1, the full
> rename (Subject → Topic) is in scope; `GradeSubjectAssignment` is retained
> as the bridge (Q2) — not dropped.

---

## 13. Implementation phases (one PR per step, each shippable behind the flag)

### Phase 1 — Students domain: rename Subject → Topic + extend bridge (dark)
- Rename `Subject` → `Topic`, `subjects` → `topics`, `SubjectStrand` →
  `TopicStrand`, `SubjectLesson` → `TopicLesson` (entities, configs, repos,
  CQRS, DTOs, events, exceptions, routes).
- `Topic`: add `Description`; make `Code`/`CodedValueId` nullable; convert the
  unique index to partial. **No owner columns on `Topic`.**
- `GradeSubjectAssignment` (retained): add `ActivityGroupId` (nullable),
  rename `SubjectId` → `TopicId`, `SubjectStrandId` → `TopicStrandId`,
  `SubjectLessonId` → `TopicLessonId`, **retain the existing `period_id` column
  as-is** (the Rev. 6 amendment — `activity-group-enrollment-impl.md` Phase 11
  — later makes it nullable and adds the alignment semantics); enforce at-most-one
  of `GradeLevelId`/`ActivityGroupId`; add owner indexes.
- Migration: rename tables/columns in place; backfill is a pure rename (no
  owner-column absorption into `Topic`); bridge rows preserved and extended.
- Unit tests (AC-1..6, AC-10..14); `NoUncommittedModelChanges`.
- **Flag OFF** (grade-level bridge behavior works unflagged; group-bridge
  surface gated).

### Phase 2 — Assignments: rename + bridge-based validation (dark)
- Rename `Assignment.SubjectId` → `Assignment.TopicId` (+ migration).
- Add bridge-based owner validation in `Assignment.Create` / `Update`
  (FR-14, FR-15) — validate via `GradeSubjectAssignment`, not topic columns.
- Update assignment create/edit admin pages (topic picker filtered by
  audience type — Q4).
- Unit tests (AC-7..9, AC-15..16).
- **Flag OFF**

### Phase 3 — Activity groups (depends on Phase 1)
- `ActivityGroup` + `ActivityGroupMembership` entities, migration, CQRS, API.
- (From `activity-group-enrollment.md` Phase 1-2, referencing `Topic` via the
  bridge instead of `ActivityList`.)

### Phase 4 — Assignment↔group link + publish (depends on Phase 2 + 3)
- `AssignmentActivityGroup` link entity.
- Publish recipient resolution for `SelectedGroups`.
- Bridge-based topic validation for group assignments (FR-15).
- (From `activity-group-enrollment.md` Phase 3, updated.)

### Phase 5 — Admin UI + Student Detail + flag flip (dark→lit)
- Topics admin page with ActivityGroup filter (via bridge).
- ActivityGroups admin pages (from `activity-group-enrollment.md` Phase 4).
- Student Detail page Activity Groups section (Phase 5 of that spec).
- Backward-compatible API alias (`/api/subjects` → `/api/topics`).
- Playwright smoke test.
- `FEATURE:EnableActivityGroups` defaults ON for pilot tenant.

---

### Traceability summary

- **25 FRs** (FR-1..25), **8 NFRs** (NFR-1..8), **16 ACs** (AC-1..16),
  **8 ECs** (EC-1..8), **5 OS items** (OS-1..5).
- Every entity in §8 maps to a requirement: `topics` → FR-1..5;
  `grade_subject_assignments` (bridge) → FR-6..12;
  `topic_strands` → FR-18; `topic_lessons` → FR-19;
  `student_topic_assignments` → FR-13 (renamed);
  `assignments.topic_id` → FR-13..17.
- `GradeSubjectAssignment` is **retained** as the M:N bridge (FR-12) —
  extended, not dropped (Design B, NFR-3).
- `ActivityList` and `AssignmentActivityList` from
  `activity-group-enrollment.md` are superseded — a shared `Topic` bridge-
  assigned to an `ActivityGroup` replaces both.