# Subject → Topic Polymorphism — Implementation Checklist

> Tracked checklist for `subject-to-topic-polymorphism.md` (Design B, reconciled
> 2026-08-26). The rename of `Subject` → `Topic` and of `SubjectStrand`/
> `SubjectLesson` → `TopicStrand`/`TopicLesson`, plus the retained
> `GradeSubjectAssignment` bridge extension.
> **Workflow (adopted repo-wide for spec-driven effort): stacked PRs.** Each
> phase branches from the previous phase's branch (not main) and its PR's base
> is the previous PR's head. Merges are deferred until the whole spec is
> complete, then merged bottom-up. Update each box to `[x]` when the phase is
> built; the PR link/SHA is tracked in the Notes / change log below.

**Status legend:** `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked

---

> **Spec is the source of truth.** This is the granular, per-spec phase tracker
> (checkbox-level) for the `Subject` → `Topic` rename + `GradeSubjectAssignment`
> bridge extension (Design B). Requirements live in the spec; this doc only
> tracks implementation steps and cites the FRs/§s it builds.
>
> **Backend / UI split.** This tracker covers the rename + bridge work
> (backend-heavy). Forward backend work is sprint-ordered in
> `backend-implementation-backlog.md` (Sprint 6 owns the Rev. 6 bridge `PeriodId`
> validation); forward UI work is in `ui-implementation-backlog.md` (Sprint 4).
> The activity-group entity / link / publish / admin-UI phases live in
> `activity-group-enrollment-impl.md` (Phases 1–5) and are **not duplicated**
> here — this tracker cross-references them.
>
> **Hard dependency.** This rename is a prerequisite for
> `activity-group-enrollment-impl.md` (Steps 16 and 27) and for
> `backend-implementation-backlog.md` Sprint 6 (Rev. 6 bridge `PeriodId`).

---

## Phase 1 — Students domain: rename Subject → Topic + extend bridge (dark, flag OFF)

- [x] **1.1** Rename `Subject` → `Topic` entity (`Domain/Subject.cs` →
  `Domain/Topic.cs`), `subjects` → `topics` table. Retain `Name`, `Code`,
  `DisplayOrder`, `CodedValueId`, `TenantId`, `RowVersion`, audit, and the
  `Create`/`Update`/`Delete` factory methods. **No owner columns** on `Topic`
  (Design B). — *FR-1, FR-2*
- [x] **1.2** `Topic`: add `Description` (≤ 2000); make `Code` and `CodedValueId`
  nullable; convert the `(tenant_id, code)` unique index to a partial
  `WHERE code IS NOT NULL` (`ix_topics_tenant_code`); add partial
  `(tenant_id, coded_value_id)` index. — *FR-3, FR-4, FR-5, NFR-4*
- [x] **1.3** Rename `SubjectStrand` → `TopicStrand`, `subject_strands` →
  `topic_strands`, `SubjectId` → `TopicId`. — *FR-18*
- [x] **1.4** Rename `SubjectLesson` → `TopicLesson`, `subject_lessons` →
  `topic_lessons`, `SubjectId` → `TopicId`, `StrandId` → `TopicStrandId`. — *FR-19*
- [x] **1.5** Rename `StudentSubjectAssignment` → `StudentTopicAssignment`,
  `student_subject_assignments` → `student_topic_assignments`, `SubjectId` →
  `TopicId`. — *FR-1, §8.4*
- [x] **1.6** Rename `SubjectReferencedException` → `TopicReferencedException`;
  rename all `Subject*` domain events → `Topic*` (`SubjectCreatedEvent` →
  `TopicCreatedEvent`, etc.). — *FR-2, FR-24*
- [x] **1.7** `GradeSubjectAssignment` bridge (**retained**, Design B): add
  nullable `ActivityGroupId`; rename `SubjectId` → `TopicId`,
  `SubjectStrandId` → `TopicStrandId`, `SubjectLessonId` → `TopicLessonId`;
  enforce at-most-one of `GradeLevelId`/`ActivityGroupId`; keep
  `[StartDate, EndDate?]` (date-based, decision 2a). **Do NOT add `period_id`
  here** — that is the Rev. 6 amendment, owned by
  `activity-group-enrollment-impl.md` Phase 11. — *FR-6, FR-7, FR-8, FR-9, FR-12*
- [x] **1.8** Bridge indexes (NFR-4): `(tenant_id, grade_level_id, topic_id)`,
  `(tenant_id, activity_group_id, topic_id)`, `(tenant_id, topic_id)`; retain
  existing indexes. — *NFR-4*
- [x] **1.9** Rename `SubjectConfiguration` → `TopicConfiguration`,
  `SubjectStrandConfiguration` → `TopicStrandConfiguration`,
  `SubjectLessonConfiguration` → `TopicLessonConfiguration`; extend
  `GradeSubjectAssignmentConfiguration` (`ActivityGroupId`, renames, owner
  indexes, at-most-one-owner constraint). — *§10*
- [x] **1.10** Repos: rename `Subject*` repos → `Topic*`; **retain + extend**
  the `GradeSubjectAssignment` repo (bridge create/update/list-by-grade/
  list-by-group). — *§10*
- [x] **1.11** CQRS: rename `CQRS/Subjects/*` → `CQRS/Topics/*`; **retain +
  extend** `CQRS/GradeSubjectAssignments/*` (bridge commands/queries:
  by-grade, by-group). — *FR-21, FR-22, §10*
- [x] **1.12** DTOs: rename `SubjectDto` → `TopicDto` (+`Description`),
  `SubjectStrandDto` → `TopicStrandDto`, `SubjectLessonDto` → `TopicLessonDto`;
  **retain + extend** `GradeSubjectAssignmentDto` (`BridgeDto`:
  `ActivityGroupId`, `TopicId`). — *§10, §7.2*
- [x] **1.13** `StudentsDbContext`: rename DbSets; **retain** the
  `GradeSubjectAssignment` DbSet. — *§10*
- [x] **1.14** Migration `<ts>_SubjectToTopic`: rename tables/columns in place
  (`subjects`→`topics`, `subject_strands`→`topic_strands`,
  `subject_lessons`→`topic_lessons`,
  `student_subject_assignments`→`student_topic_assignments`; bridge
  `subject_id`→`topic_id`, `subject_strand_id`→`topic_strand_id`,
  `subject_lesson_id`→`topic_lesson_id`; add `activity_group_id` nullable).
  Pure rename + add nullable column; **no owner-column absorption into
  `topics`**. Preserve all rows. — *NFR-1, NFR-3*
- [x] **1.15** Students API: rename `SubjectRoutes` → `TopicRoutes`; add the
  backward-compat `/api/subjects` alias to `/api/topics`; add bridge endpoints
  (`POST`/`GET`/`PUT`/`DELETE /api/grade-subject-assignments`,
  `GET /topics/by-grade/{id}`, `GET /topics/by-group/{id}`); gate the
  group-bridge surface behind `FEATURE:EnableActivityGroups`. — *FR-21, FR-22,
  FR-23, NFR-6, NFR-8, §7.1, §7.2*
- [x] **1.16** Unit tests: rename `*Subject*` test files → `*Topic*`;
  `Topic.Create` (no owner columns — AC-1/AC-3); bridge retained + extended
  (AC-12); partial code index (AC-4); strand/lesson under a group-bridged topic
  (AC-10/AC-11); `NoUncommittedModelChanges` for `StudentsDbContext`. — *AC-1..6,
  AC-10..14, NFR-2*
- [x] **1.17** **Flag OFF** (grade-level bridge behavior works unflagged;
  group-bridge surface gated by `FEATURE:EnableActivityGroups`).

---

## Phase 2 — Assignments: rename + bridge-based validation (dark, flag OFF)

- [x] **2.1** Rename `Assignment.SubjectId` → `Assignment.TopicId` (domain +
  configuration + migration `<ts>_RenameSubjectToTopic`). — *FR-13, §8.5*
- [x] **2.2** `Assignment.Create`/`Update`: keep `TopicId` required (throw on
  `Guid.Empty`); add **bridge-based** owner validation — `SelectedGrades`: topic
  bridge-assigned to `Assignment.GradeLevelId` for the effective date;
  `SelectedGroups`: topic bridge-assigned to a linked group; `AllStudents`: no
  validation. — *FR-13, FR-14, FR-15, FR-16, FR-17*
- [x] **2.3** Assignments CQRS/DTOs: rename `SubjectId` → `TopicId` across
  commands/queries/`AssignmentSummary`. — *§10*
- [ ] **2.4** Assignments admin pages: update the topic picker (renamed); filter
  by audience type (§12 Q4) — grade-bridged topics for `SelectedGrades`,
  group-bridged topics for `SelectedGroups`. — *§10, §12 Q4*
- [x] **2.5** Cross-context: `Assignment.TopicId` is an operational ref to
  `topics.id` (no DB FK across contexts). — *§8.5*
- [x] **2.6** Unit tests: `SelectedGrades`/`SelectedGroups` bridge validation
  (AC-7/AC-8/AC-9); `AllStudents` no validation (AC-15); backward-compat route
  (AC-16); `NoUncommittedModelChanges` for `AssignmentsDbContext`. — *AC-7..9,
  AC-15..16, NFR-2*
- [x] **2.7** **Flag OFF**.

---

## Phase 3 — Activity groups (depends on Phase 1) — see `activity-group-enrollment-impl.md`

> Owned by `activity-group-enrollment-impl.md` Phases 1–2 (`ActivityGroup` +
> `ActivityGroupMembership` entities, migration, CQRS, API). The activity-group
> side of the bridge (`ActivityGroupId`) lands in step 1.7 above; group-bridge
> validation is gated by `FEATURE:EnableActivityGroups`. **Not duplicated here.**

---

## Phase 4 — Assignment↔group link + publish (depends on Phase 2 + 3) — see `activity-group-enrollment-impl.md`

> Owned by `activity-group-enrollment-impl.md` Phase 3 (`AssignmentActivityGroup`
> link, publish recipient resolution for `SelectedGroups`, bridge-based topic
> validation FR-15). **Not duplicated here.**

---

## Phase 5 — Admin UI rename + backward-compat alias (dark→lit)

> UI rename portions only. The activity-group admin UI + Student Detail section
> are owned by `activity-group-enrollment-impl.md` Phases 4–5; forward UI work
> (ActivityGroup filter, topic create/edit owner + period alignment, strand/
> lesson dialogs for group-bridged topics, "Subject"→"Topic" label rename) is in
> `ui-implementation-backlog.md` Sprint 4. This phase covers only the
> Subjects→Topics admin rename + the backward-compat alias.

- [ ] **5.1** Rename the Subjects admin page (`Subjects.razor` → `Topics.razor`,
  `SubjectCreateDialog` → `TopicCreateDialog`, `SubjectEditDialog` →
  `TopicEditDialog`); extend with an ActivityGroup filter via the bridge. —
  *FR-1, OS-5, §10*
- [ ] **5.2** Update `GradeLevelFormFields.razor`: subject picker → topic picker
  (bridge-aware). — *§10*
- [ ] **5.3** Confirm the backward-compatible API alias `/api/subjects` →
  `/api/topics` end-to-end (built in 1.15; verify in the UI/API integration). —
  *NFR-6, AC-16*
- [ ] **5.4** bUnit: Topics admin page; assignment create/edit topic picker. —
  *§11*
- [ ] **5.5** Playwright smoke (seeded, coordinated with
  `activity-group-enrollment-impl.md` Phase 6): create topic → bridge-assign to
  group → `SelectedGroups` assignment → publish → recipients. — *§11*
- [ ] **5.6** `FEATURE:EnableActivityGroups` defaults ON for pilot tenant
  (coordinated with `activity-group-enrollment-impl.md` Phase 6). — *NFR-8*

---

## Cross-cutting / don't-forget

- [x] **Rev. 6 bridge `PeriodId`** (FR-55..58) — add the nullable `period_id`
  column + the grade-owned/group-owned `PeriodId` validation + assignment/
  subject consistency, owned by `activity-group-enrollment-impl.md` Phase 11;
  lands **after** this rename (additive). — *Rev. 6, backend-implementation-backlog.md Sprint 6*
- [x] **Strict-tenant** on all topic/bridge reads/writes. — *NFR-5*
- [x] **Feature flag** — group-bridge surface gated by
  `FEATURE:EnableActivityGroups`; grade-level bridge unflagged. — *NFR-8*
- [x] **Orphaned subjects** (no `GradeSubjectAssignment`) → still renamed to
  `topics`; create bridge rows post-migration. — *EC-6*
- [ ] **`TeacherSubject` → `TeacherTopic`** rename is a separate mechanical PR
  (OS-4), not blocking. — *OS-4*
- [ ] **Coded-value parent "Subjects"** rename is out of scope (OS-2). — *OS-2*
- [x] **`NoUncommittedModelChanges`** passes for `StudentsDbContext` and
  `AssignmentsDbContext` after the migrations land. — *NFR-2*

---

## Notes / change log

- _Checklist generated from `subject-to-topic-polymorphism.md` (Design B,
  reconciled 2026-08-26). Source of truth: that spec._
- _Dependency direction: this rename is a **prerequisite** for
  `activity-group-enrollment-impl.md` (Steps 16/27) and for
  `backend-implementation-backlog.md` Sprint 6 (Rev. 6 bridge `PeriodId`).
  Phases 3–5 cross-reference `activity-group-enrollment-impl.md` rather than
  duplicating its activity-group entity / link / publish / admin-UI work._