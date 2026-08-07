# Strand ↔ Lesson Unification (lesson = strand with a parent)

## Status
Proposed. This is an architectural consolidation of the curriculum model: the
separate `TopicLesson` entity is redundant with `TopicStrand`, so we redefine a
**lesson as a strand that has a parent strand**. Split into shippable PRs below
(spec-driven; one PR per rollout step, each branch tip compiles + tests green).

## Problem / Motivation
Today a topic has two overlapping, parallel entities:

- **`TopicStrand`** (`subject_strands`) — root-level "strands" of a subject/topic
  (TopicId, Name, Description, DisplayOrder).
- **`TopicLesson`** (`subject_lessons`) — "lessons" that can optionally point at a
  strand via `StrandId` (TopicId, Name, Description, StartDate, EndDate,
  `IsOpenEnded`, DisplayOrder, StrandId).

They share the same shape (name + description + display order under a topic) and
a lesson is essentially "a finer strand under a strand". Keeping two entities
forces duplicated commands/queries/DTOs/routes/UI and a second FK (`TopicLessonId`)
on the assignment bridge. The user wants to collapse this: **a lesson is just a
strand that has a parent strand**, so we use one entity.

## Proposed design (user-confirmed direction)
- `TopicStrand` gains a nullable **`ParentStrandId`** self-referencing FK.
- **Root strand** = `ParentStrandId IS NULL`.
- **Lesson** = a strand with `ParentStrandId` set (points at a root strand).
- Strands may be created with a parent (never themselves).
- `TopicLesson` (entity, table `subject_lessons`, DTO, commands, routes, UI) is
  removed; lessons live as parented `TopicStrand` rows.

### Key design decisions (confirmed)
1. **Lesson date fields (StartDate / EndDate / IsOpenEnded).** Add optional
   `StartDate`/`EndDate` to `TopicStrand`, set for lessons too. `IsOpenEnded` is a
   derived property (`!StartDate.HasValue || !EndDate.HasValue`).
2. **Nesting depth.** Parents must themselves be **root** strands (a lesson cannot
   be a parent) — a clean 2-level tree (strand → its lessons). Enforce: parent != self;
   parent.TopicId == strand.TopicId; parent is a root strand.
3. **Assignment bridge.** Drop `TopicLessonId`; unify on `TopicStrandId` (which now
   references a root strand OR a lesson). Data migration backfills any assignment
   with `TopicLessonId` set → set `TopicStrandId` to that lesson's new (parented)
   strand id, then drops the column.

4. **Data migration strategy.** One EF migration that:
   - adds `ParentStrandId` + `StartDate`/`EndDate` to `subject_strands`;
   - copies each `subject_lessons` row into `subject_strands` as a parented strand
     (`ParentStrandId = old StrandId`); lessons with `StrandId IS NULL` become root
     strands (flagged in the plan — see Risks);
   - drops `subject_lessons`;
   - drops `TopicLessonId` on the assignment tables after backfill.

   **Confirmed: proceed with the migration.**

### CQRS / API surface after merge
- **Remove**: `CreateTopicLesson`, `UpdateTopicLesson`, `RemoveTopicLesson`,
  `AssignLessonStrand`, `ListTopicLessons` (as a separate table query), and the
  `/students/topics/{topicId}/lessons*` routes; `TopicLessonDto`.
- **Extend** `CreateTopicStrand`/`UpdateTopicStrand` with optional `ParentStrandId`
  (validation: not self, same topic, root parent).
- **`ListTopicStrands`** gains optional filtering (parented-only / by parent) so the
  "lessons of a strand" contract survives.
- **`TopicStrandDto`** gains `ParentStrandId`, `StartDate`, `EndDate`, derived
  `IsLesson`.
- **Curriculum counts** (`ListGradeTopicCurriculumByGrade`): count strands where
  `ParentStrandId IS NULL` and lessons where `ParentStrandId IS NOT NULL`, both
  from `subject_strands`.

### UI after merge
- **`StrandsEditor`** becomes the unified editor:
  - root-strand list with a **"New Strand"** affordance;
  - each root strand may expand/inline to show its **lessons** (child strands),
    created via a **"New Lesson"** affordance with a parent select;
  - edit/delete for both.
- **`LessonsEditor`** is removed (folded into StrandsEditor).
- **`TopicStrandsDialog` / `TopicLessonsDialog`** and the Detail row kebab update to
  the unified model (readonly dialogs list parented strands as lessons).

## Current-state summary (what exists today)
- `TopicStrand` domain + `TopicStrandConfiguration` (table `subject_strands`).
- `TopicLesson` domain + `TopicLessonConfiguration` (table `subject_lessons`),
  optional `StrandId` FK, date fields, `IsOpenEnded`.
- CQRS: Create/Update/Remove + ListTopicStrands; Create/Update/Remove +
  ListTopicLessons + AssignLessonStrand.
- Routes: `/students/topics/{topicId}/strands*` and `.../lessons*`.
- DTOs: `TopicStrandDto`, `TopicLessonDto`.
- Assignment bridge base has `TopicStrandId` + `TopicLessonId`.
- UI: `StrandsEditor` (inline CRUD), `LessonsEditor` (inline CRUD + strand select),
  `TopicStrandsDialog`/`TopicLessonsDialog` (readonly), Detail row kebab
  (Strands/Lessons), TopicEditDialog embeds `StrandsEditor`.

## Rollout steps (proposed)
- **sl/1 — Data model + migration.** Add `ParentStrandId` (+ dates) to `TopicStrand`;
  self-ref FK config; remove `TopicLesson` entity/config/table + `TopicLessonId` on
  the bridge; EF migration incl. data backfill.
- **sl/2 — CQRS + API.** Extend strand commands with `ParentStrandId` + validation;
  filter `ListTopicStrands`; `ListTopicLessons` via parented filter; delete lesson
  commands/routes/DTO; update `TopicStrandDto`; curriculum counts from one table.
- **sl/3 — UI.** Unify `StrandsEditor` (parents + lessons with parent select), remove
  `LessonsEditor`, update dialogs + Detail kebab.
- **sl/4 — Tests.** Update all strand/lesson CQRS, route, DTO, and bUnit tests;
  migration-guard test updated for the new migration.

## Risks / open questions
- Lessons with `StrandId IS NULL` today (no parent) — after migration they become
  root strands, which re-categorizes them as strands. Flag any rows; ideally the
  data has none, but the migration should log/flag them.
- The migration guard test (`MigrationGuardTests`) asserts a single head — the new
  migration must be the sole pending one.
- `dotnet ef migrations` can't reach the DB locally (localhost:5432) — migration is
  authored + verified via the model snapshot and build, per the repo's existing
  pattern.
