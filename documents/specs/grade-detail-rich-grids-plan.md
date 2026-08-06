# Grade-Level Detail — Rich Curriculum & Teacher Grids (plan)

Status: **complete — cg grids delivered** (dm phase complete; cg/2–cg/4 shipped).
Parent: `grade-level-detail-view-plan.md` (Phase A) — enhances the grade-level Detail page.

## 0. Active architecture (dm — demographics phase)
The user redirected the phase: **unify Teacher / Guardian / Student onto a shared `PersonDemographic` base** (name, date of birth, gender, title) **and give all three multiple contacts** (SMS/WhatsApp/Email via `Contact`). This is a dedicated `dm` stack; the cg curriculum grids are **paused until it lands**.

- **dm/1 — person base + gaps**: new abstract `PersonDemographic` (Title, FirstName, LastName, DateOfBirth, GenderCodedValueId) flattened onto each entity's own table (verified: migration adds `students.title_coded_value_id`, `guardians.date_of_birth` + `guardians.gender_coded_value_id`, `teachers.date_of_birth`/`gender`/`level`; no discriminator/base table). Guardian gains **gender + DOB**; Student gains **Title** (DOB stays **required/enforced** per user).
- **dm/2 — teacher contacts**: migrate Teacher off single `Email`/`ContactPhone` onto `Contact` rows (reverses the v1 "teachers not notification recipients" carve-out).

### Grade-level Detail UI (in dm/1, user-directed)
- **Overview card** trimmed to student-card style showing only: **name of grade, age range, enrollment, valid gender, total students as a `FluentAnchor`** navigating to `/students?gradeLevelId={id}`.
- **New "Students" tab** on the grade Detail page listing active students via a **shared `StudentsGrid.razor`** component (Name→detail link, Student #, Gender, Age, Status).

## 1. Why / Goal

The grade-level Detail page's two curriculum tabs currently:
- **Topics & Curriculum** — renders topics as an expandable card list. No at-a-glance strand/lesson counts; to add a strand or lesson you must expand a card first.
- **Teachers** — a bare grid (Name, Email, Role, Assigned Topics chips). No demographic columns, no topic-count aggregate.

Goal: show both tabs as **rich data grids padded with aggregates**, and make strand/lesson authoring reachable directly from the topic grid.

## 2. Scope decisions (needs confirmation)

**DECISION A — Teacher profile fields are NEW.** `Teacher` currently has `TitleCodedValueId` but **no `Gender`, `DateOfBirth`, level of education, or qualifications/specialties**. To show them as columns we must add them (mirroring the `Student` pattern for gender/DOB):
- `DateOfBirth` (`DateOnly?`) + `GenderCodedValueId` (`Guid?`, `CodedValueParent.Genders` → "GENDER").
- `LevelOfEducationCodedValueId` (`Guid?`, single, new `EducLevel` parent).
- **Qualifications / specialties — multiple** — via a join table `TeacherQualification` (`TeacherId` + `CodedValueId`, new `Qualification` parent).
- Title is already present as `TitleCodedValueId` (resolved via `CodedValueParent.Salutations`); we just need to display its name.

**DECISION B — Grids replace (not augment) the current card/expand UI.**
- Topics tab → a `FluentDataGrid` (Name+Code, Strands count, Lessons count, expand row, "Add Strand" / "Add Lesson" actions). The existing `StrandsEditor` / `LessonsEditor` CRUD stays, reached via an expanded row or dialogs from the grid.
- Teachers tab → add Title, Gender, Date of Birth, Level of education, Qualifications, Topics count columns (Topics count is derivable from `AssignedTopics`).

## 3. Domain / data model changes

### Shared demographic base (new architecture directive)
Student, Guardian and Teacher should share a base demographic class — **name, date of birth, gender, title** — and all three should have **multiple contacts (SMS, WhatsApp, Email)** via the existing `Contact` table (`ContactChannel` + polymorphic `ContactOwnerType`).

Current gaps to close:
- **Title**: on Guardian + Teacher; **missing on Student** (add `TitleCodedValueId`).
- **DOB**: on Student + (new) Teacher; **missing on Guardian** (add `DateOfBirth`).
- **Gender**: on Student + (new) Teacher; **missing on Guardian** (add `GenderCodedValueId`).
- **Multiple contacts**: Student + Guardian already use `Contact`; **Teacher still uses single `Email`/`ContactPhone`** — must migrate to `Contact` rows (Email/SMS/WhatsApp), reversing the v1 "teachers not notification recipients" carve-out.

**Implementation options (needs confirmation):**
- A C# abstract base `PersonDemographic` (Title/FirstName/LastName/DisplayName/DateOfBirth/GenderCodedValueId) that Student/Guardian/Teacher inherit, with EF mapping **flattened onto each entity's own table** (add `students.title_coded_value_id`, `guardians.date_of_birth` + `guardians.gender_coded_value_id`). Lowest-risk: no TPT/TPH table hierarchy.
- Full EF Table-per-Type hierarchy (new base table) — more invasive.

This is a **distinct architectural phase** (touches Students/Guardians/Teachers, the Contact/notification domain, many queries/forms/tests). Recommended as its own stack (`dm`) rather than inside the curriculum-grids `cg` stack.

### Coded value parents + seeds (MigrationService + CodedValueConstants)
Two new `CodedValueParent` entries + `ToCode` mappings + `seed.csv` rows:
- `EducLevel` → `"EDUCLEVEL"` with sample children: Certificate, Diploma, Bachelor's, Honours, Master's, Doctorate.
- `Qualification` → `"QUALIF"` with sample children: Mathematics Teaching, Physical Sciences Teaching, Life Sciences Teaching, Languages Teaching, Humanities Teaching, Arts Teaching, Special Education.

### Teacher (Students.Core)
- Add `DateOnly? DateOfBirth`, `Guid? GenderCodedValueId`, `Guid? LevelOfEducationCodedValueId` to `Teacher`, `Teacher.Create(...)`, `Teacher.Update(...)`.
- Add `TeacherQualification` entity (FK→Teacher, FK→CodedValue) + `Teacher.Qualifications` collection; `LinkQualification`/`UnlinkQualification`.
- Migration `AddTeacherProfileFields`.
- Update `TeacherDto` + `TeacherWithRoleDto` (add `DateOfBirth`, `GenderCodedValueId`, `LevelOfEducationCodedValueId`, `QualificationCodedValueIds[]`).
- Update teacher create/update request records + handlers + teacher create/update UI.

### Aggregates (backend)
- **Per-topic curriculum counts for a grade**: extend the existing grade-topic assignment query (or add `ListGradeTopicCurriculumByGrade`) to return, per assigned topic, `StrandCount` + `LessonCount`.
- **Teacher display names**: resolve `TitleCodedValueId` → salutation name and `GenderCodedValueId` → gender name in the `ListTeachersForGradeLevel` handler (join coded values), so the grid can render names without per-cell lookups.

## 4. CQRS / API surface

- `ListGradeTopicCurriculumByGrade` (Students.Core) → `GradeTopicCurriculumDto[]`:
  `{ TopicId, Name, Code, StrandCount, LessonCount }` (or extend `TopicAssignmentDto`).
- `ListTeachersForGradeLevel` (Students.Core) → extend `TeacherWithRoleDto` with `TitleName`, `GenderName`, `LevelOfEducationName`, `QualificationNames[]`, `DateOfBirth`, and an explicit `TopicsCount` (currently derivable via `AssignedTopics.Length`).
- Teacher create/update endpoints gain `DateOfBirth` / `GenderCodedValueId` fields.

## 5. UI

## 5. UI

### Dialog-based authoring (strands, lessons, topics)
- Strands, lessons, and topics are added/edited via **dialogs** (IDialogService), not inline editors. Reuse/extend the `StrandsEditor`/`LessonsEditor` logic inside dialog components, or new `StrandDialog`/`LessonDialog`/`TopicDialog`.

### Topics & Curriculum tab
- Replace the card list with a `FluentDataGrid`:
  - Columns: **Topic** (name+code, clickable to expand), **Strands**, **Lessons**, **Actions** (`Add Strand`, `Add Lesson`, `Edit`, `Remove`).
  - **Clickable count cells**: clicking a topic's **Strands** count or **Lessons** count pops a dialog listing that topic's strands / lessons.
  - The **strands list dialog** also supports **add / edit of lessons** (e.g. a lessons section per strand, or add/edit lesson from the strand context).
- **Topic create/edit dialog** assigns teachers to the topic **and their roles** (`TeacherTopic.RoleCodedValueId`), in addition to name/code/description.
- Aggregate header chips (total strands / total lessons for the grade).

### Teachers tab
- Add columns to the existing grid: **Title**, **Gender**, **Date of Birth**, **Level of education**, **Qualifications** (chips), **Topics** (count + chips), keeping Name/Email/Role/Actions.
- **Teacher create/edit dialog** captures the profile fields (gender, DOB, level of education, qualifications) **and** the subjects/topics the teacher teaches **with their roles** (the same `TeacherTopic` role link).

### Tests (bUnit)
- Topics grid renders counts; add-strand/add-lesson/add-topic dialog round-trips; topic dialog assigns teachers+roles; teacher grid renders title/gender/DOB/level/qualifications/topic-count; teacher dialog edits topics+roles.

### Teacher ↔ Topic assignment with roles
- Add `RoleCodedValueId` (`Guid?`) to `TeacherTopic` (mirrors `TeacherGradeLevel.SetRole`), reusing the existing `TeacherRoles`/`TCHROLES` parent — a teacher's role on a topic (e.g. Head of Department, Subject Lead).

## 6. Stacked PR train

| # | Branch | Concern |
|---|--------|---------|
| 1 | `cg/1-teacher-profile` | ✅ **superseded by dm/1 + dm/2** (teacher gender/DOB/level/qualifications + contacts already landed in the demographics phase) |
| 2 | `cg/2-curriculum-aggregates` | ✅ Per-topic strand/lesson counts (`ListGradeTopicCurriculumByGrade`) + `TeacherTopic.RoleCodedValueId` (link command/endpoint/API client) + migration + tests |
| 3 | `cg/3-topics-grid` | ✅ Topics tab → rich data-grid with Strand/Lesson count columns + `TopicStrandsDialog`/`TopicLessonsDialog` hosting the shared editors (UI + bUnit) |
| 4 | `cg/4-teachers-grid` | ✅ Teachers tab → Title/Gender/Level/Qualifications/Topics columns resolved client-side (UI + bUnit) |

Base for layer 1 = the branch containing the tabbed `Detail.razor` (Phase A `stack/5-create-edit`). The cg stack was rooted on the `dm/3` tip. Note: cg/1 was replaced by the dedicated `dm` phase, so the shipped cg stack is cg/2 → cg/3 → cg/4 on top of dm/1–dm/3.

## 7. Delivery / out of scope
- No changes to strands/lessons CRUD semantics — only surfacing counts + authoring entry points.
- No notification / delivery coupling.

## 8. Open items for confirmation
1. **Add `Gender` + `DateOfBirth` to Teacher** — ✅ resolved in dm/1 (gender/DOB/level/qualifications captured).
2. Grids **replace** the card/expand UI — ✅ resolved: cg/3 replaced the card list with the data grid; cg/4 padded the teacher grid.
3. Stack base — ✅ resolved: the cg stack is rooted on the `dm/3` tip (after the demographics phase).
