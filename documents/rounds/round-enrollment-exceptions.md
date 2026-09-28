tier 3 full; provider ollama; orchestrator ollama-cloud/glm-5.3-flash; worker ollama-cloud/deepseek-v4.1-flash; plan-reviewer ollama/glm-5.3:cloud; code/diff-reviewer openrouter/stealth/space-bunny-alpha (thinking high, per-role owner override, post-worker diff review only); ui-tester ollama/minimax-m3:cloud; round base 3e408bf; UI round EXPECTED.

**Tree state at round open (recorded 2026-09-26).** `git rev-parse --short HEAD` = `3e408bf`. The tree is **dirty**: the entire v1 round (`feat/subject-period-blocks`) is **uncommitted**. `git status --porcelain`, verbatim:

```
 M AGENTS.md
 M documents/solution/subject-topic-delivery-periods.md
 M documents/specs/subject-period-exception-model.md
 M src/Students/SchoolCollab.Students.Api/Endpoints/TopicAssignmentRoutes.cs
 M src/Students/SchoolCollab.Students.Api/StudentEndpoints.cs
 M src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Detail.razor
 M src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Subjects/Subjects.razor
 M src/Students/SchoolCollab.Students.Application/Components/Students/GradeTopicsDialog.razor
 M src/Students/SchoolCollab.Students.Application/Components/Students/TopicCreateDialog.razor
 M src/Students/SchoolCollab.Students.Application/Components/Students/TopicEditDialog.razor
 M src/Students/SchoolCollab.Students.Application/Components/Students/TopicEditDialog.razor.css
 D src/Students/SchoolCollab.Students.Application/Components/Students/TopicPeriodsEditDialog.razor
 M src/Students/SchoolCollab.Students.Application/Services/StudentsApiClient.cs
 M src/Students/SchoolCollab.Students.Core/CQRS/Periods/Commands/DeletePeriod/DeletePeriodHandler.cs
 M src/Students/SchoolCollab.Students.Core/CQRS/TopicAssignments/Commands/AssignActivityGroupTopic/AssignActivityGroupTopic.cs
 M src/Students/SchoolCollab.Students.Core/CQRS/TopicAssignments/Commands/AssignActivityGroupTopic/AssignActivityGroupTopicHandler.cs
 M src/Students/SchoolCollab.Students.Core/CQRS/TopicAssignments/Commands/AssignGradeTopic/AssignGradeTopic.cs
 M src/Students/SchoolCollab.Students.Core/CQRS/TopicAssignments/Commands/AssignGradeTopic/AssignGradeTopicHandler.cs
 M src/Students/SchoolCollab.Students.Core/CQRS/TopicAssignments/Commands/UpdateTopicAssignmentPeriod/UpdateTopicAssignmentPeriodHandler.cs
 M src/Students/SchoolCollab.Students.Core/CQRS/TopicAssignments/Queries/ListActivityGroupTopicAssignments/ListActivityGroupTopicAssignmentsHandler.cs
 M src/Students/SchoolCollab.Students.Core/CQRS/TopicAssignments/Queries/ListGradeTopicAssignments/ListGradeTopicAssignmentsHandler.cs
 M src/Students/SchoolCollab.Students.Core/CQRS/TopicAssignments/Queries/ListGradeTopicCurriculumByGrade/ListGradeTopicCurriculumByGradeHandler.cs
 M src/Students/SchoolCollab.Students.Core/CQRS/TopicAssignments/TopicAssignmentPeriodValidator.cs
 M src/Students/SchoolCollab.Students.Core/CQRS/Topics/Commands/CreateTopicForGrade/CreateTopicForGrade.cs
 M src/Students/SchoolCollab.Students.Core/CQRS/Topics/Commands/CreateTopicForGrade/CreateTopicForGradeHandler.cs
 M src/Students/SchoolCollab.Students.Core/Data/StudentsDbContext.cs
 M src/Students/SchoolCollab.Students.Core/Extensions.cs
 M src/Students/SchoolCollab.Students.Core/Migrations/StudentsDbContextModelSnapshot.cs
 M tests/SchoolCollab.Admin.Tests.Unit/GradeLevelDetailPageTests.cs
 M tests/SchoolCollab.Admin.Tests.Unit/SubjectsLandingPageTests.cs
 M tests/SchoolCollab.Admin.Tests.Unit/TopicCreateDialogTests.cs
 M tests/SchoolCollab.Admin.Tests.Unit/TopicEditDialogTests.cs
 D tests/SchoolCollab.Admin.Tests.Unit/TopicPeriodsEditDialogTests.cs
 M tests/SchoolCollab.Students.Tests.Unit/CreateSubjectForGradeHandlerTests.cs
 M tests/SchoolCollab.Students.Tests.Unit/ListGradeTopicCurriculumByGradeHandlerTests.cs
 M tests/SchoolCollab.Students.Tests.Unit/PeriodDeleteHandlerTests.cs
 M tests/SchoolCollab.Students.Tests.Unit/RemoveTopicAssignmentHandlerTests.cs
 M tests/SchoolCollab.Students.Tests.Unit/TopicAssignmentPeriodTests.cs
 M tests/SchoolCollab.Students.Tests.Unit/UpdateTopicAssignmentPeriodTests.cs
?? documents/rounds/diffs-subject-period-blocks.patch
?? documents/rounds/round-subject-period-blocks.md
?? src/Students/SchoolCollab.Students.Api/Endpoints/SubjectBlockRoutes.cs
?? src/Students/SchoolCollab.Students.Application/Components/Students/SubjectBlockLabels.cs
?? src/Students/SchoolCollab.Students.Application/Components/Students/SubjectBlocksDialog.razor
?? src/Students/SchoolCollab.Students.Application/Components/Students/SubjectBlocksDialog.razor.css
?? src/Students/SchoolCollab.Students.Core/CQRS/SubjectEnrollmentBlocks/
?? src/Students/SchoolCollab.Students.Core/CQRS/TopicAssignments/SubjectAvailability.cs
?? src/Students/SchoolCollab.Students.Core/DTOs/SubjectEnrollmentBlockDto.cs
?? src/Students/SchoolCollab.Students.Core/Data/Configurations/SubjectEnrollmentBlockConfiguration.cs
?? src/Students/SchoolCollab.Students.Core/Data/Repositories/ISubjectEnrollmentBlockRepository.cs
?? src/Students/SchoolCollab.Students.Core/Data/Repositories/SubjectEnrollmentBlockRepository.cs
?? src/Students/SchoolCollab.Students.Core/Domain/Exceptions/DuplicateSubjectBlockException.cs
?? src/Students/SchoolCollab.Students.Core/Domain/SubjectEnrollmentBlock.cs
?? src/Students/SchoolCollab.Students.Core/Migrations/20260926170504_AddSubjectEnrollmentBlocks.Designer.cs
?? src/Students/SchoolCollab.Students.Core/Migrations/20260926170504_AddSubjectEnrollmentBlocks.cs
?? tests/SchoolCollab.Admin.Tests.Unit/SubjectBlocksDialogTests.cs
?? tests/SchoolCollab.ArchitectureTests.Unit/DeprecatedPeriodWritePathArchitectureTests.cs
?? tests/SchoolCollab.Students.Tests.Integration/SubjectBlockAvailabilityEndpointTests.cs
?? tests/SchoolCollab.Students.Tests.Unit/BlockSubjectHandlerTests.cs
?? tests/SchoolCollab.Students.Tests.Unit/DeletePeriodBlockGuardTests.cs
?? tests/SchoolCollab.Students.Tests.Unit/Domain/SubjectEnrollmentBlockTests.cs
?? tests/SchoolCollab.Students.Tests.Unit/ListGradeTopicAssignmentsCacheKeyTests.cs
?? tests/SchoolCollab.Students.Tests.Unit/StubActivePeriodProvider.cs
?? tests/SchoolCollab.Students.Tests.Unit/SubjectAvailabilityTests.cs
```

**Isolation rule (consequence of the dirty tree):** this round's diff is computed **against the recorded base `3e408bf`**, not against HEAD, and the frozen patch
(`diffs-enrollment-exceptions.patch`) is produced with the §F idiom (`git add -N -- src tests` → `git diff 3e408bf -- src tests` → `git reset -q -- src tests`). The v1 work is part of the diff; nothing is lost and nothing outside `src/`+`tests/` (except this round doc) belongs in it.

# Round enrollment-exceptions — Subject Enrollment Exceptions v3 (period part + date span, no PeriodId anywhere)

Implements `documents/specs/subject-period-exception-model.md` **v3 only** (redesigned
2026-09-26; §0 decisions 1–10, §2.1–2.4, §4.2–4.3, §5, §6, §7, §8, §9 are authoritative;
§8 Q5 was **ANSWERED by the owner during planning — REMOVE**, recorded in (e)/(b), not open).
v3 **removes the `PeriodId` dependency entirely**: an exception is a **period part**
(`AcademicYearDivision`) plus a **nullable date span**; availability is **one date test**.
The whole v1 surface is **renamed** (`SubjectEnrollmentBlock` → `SubjectEnrollmentException`,
`subject_enrollment_blocks` → `subject_enrollment_exceptions`, `/students/subject-blocks` →
`/students/enrollment-exceptions`) — free now because v1's migration is uncommitted —
exception editing moves to a **dedicated `EnrollmentExceptions` page**, every other surface
shrinks to a **count badge + a kebab that navigates**, and v1's `SubjectBlocksDialog` is
**retired**.

---

## Context

- **Lead spec (implement THIS only):** `documents/specs/subject-period-exception-model.md`
  **v3** — the authority. Everything v1 shipped that v3 dissolves must be **deleted, not
  ported** (the dissolve list is in (b)).
- **Previous round (read, do not edit):** `documents/rounds/round-subject-period-blocks.md` —
  CLOSED. Its `## Review` sections, *Owner decisions — settled*, and *Next round — fixes and
  misses* record what v1 built. Key carry-over facts used by this plan:
  - **RISK-2 (ratified in v1):** availability resolves an active-period **two-id set**
    `{activeYear, activeSubPeriod}` used as an exact-id `Contains` set, in
    `SubjectAvailability.ResolveActivePeriodIdsAsync` + `CacheKeySegment`
    (`src/Students/SchoolCollab.Students.Core/CQRS/TopicAssignments/SubjectAvailability.cs:20-46`)
    and in the cache keys of the three availability readers. **v3 dissolves this** — with no
    period reference in the predicate there is no active-period lookup at all.
  - v1's `DeletePeriodHandler` pre-mutation guard (P2-10) loads
    `ISubjectEnrollmentBlockRepository` rows referencing a to-be-deleted period → 422. v3
    dissolves it (nothing references a period).
  - v1 measured **Step-0 Outcome B** (one pinned dev bridge row); the additive-migration +
    deferred-conversion rule stands unchanged (§(d)).
  - v1's AC-5 guard `tests/SchoolCollab.ArchitectureTests.Unit/DeprecatedPeriodWritePathArchitectureTests.cs`
    pins the deprecated `PUT /topic-assignments/{id}/period` and
    `StudentsApiClient.UpdateTopicAssignmentPeriodAsync` — **must survive untouched**.
- **Pitfall list (read before touching the UI):**
  `documents/solution/subject-topic-delivery-periods.md` §8 pitfalls 1–12 still apply
  (`Error="@Error"`, `FluentSelect`/`FluentMenu` children not materialised while closed,
  `cut.InvokeAsync` for row actions, never key subject collections by `TopicId`).
- **Sibling spec (independent — do not conflate):**
  `subject-enrollment-requirement-flag.md` (`RequiresEnrollment`) — NOT this round.
- **Branch:** `feat/subject-period-blocks` (same branch; the round continues the uncommitted
  stack), round base `3e408bf`.

### Q5 reader-chain resolution (grepped, stated per parent instruction)

The bridge whitelist reader named in spec §1 **Defect 1** is `ListTopicsByGrade` under an
explicit `?periodId=` (`TopicRoutes.cs:96-105` → `ListTopicsByGrade.PeriodId` →
`ListTopicsByGradeHandler.cs:30-36`, exact match). The subjects listing the owner decided to
un-plumb (`Subjects.razor:95` `[SupplyParameterFromQuery] PeriodId` → `:386`
`ListSubjectsByGradeAsync(gradeLevelId, PeriodId, token)`) calls `/students/subjects/by-grade/{id}`.
**These are the SAME handler chain:** `TopicRoutes.MapTopicRoutes` registers the topic surface
under `/topics` **and** the legacy `/subjects` prefix as a deprecated alias (NFR-6,
`TopicRoutes.cs:30-34`, "every handler is shared") — so `/students/subjects/by-grade/{id}` is
`ListTopicsByGrade`. **Consequence:** removing the Q5 plumbing retires the **last period-aware
read path** entirely; §4.1's "keep `PeriodId` on the bridge, ignored" needs **no further reader
work** — after Q5, zero production readers consult bridge `PeriodId`. The **write** path
`PUT /topic-assignments/{id}/period` stays a deprecated, guarded no-op
(`DeprecatedPeriodWritePathArchitectureTests` untouched).

### Enumeration audit trail (grep commands this plan was derived from)

Per the previous round's recurring lesson (enumerations came up short three separate times),
this plan's file/test lists were derived by grep, not memory. Commands run (PowerShell `bash`
tool, repo root):

```
git rev-parse --short HEAD ; git status --porcelain
grep -rln "SubjectEnrollmentBlock" src tests          # minus bin/obj hits
grep -rln "subject-blocks" src tests                  # minus bin/obj hits
grep -rn -l "SubjectBlockLabels|BlocksForTopic|BlocksByTopicKey|BlockedTopicIds|IsRetiredPeriod|SubjectBlocksDialog|SubjectBlockRoutes" src tests   # minus bin/obj
grep -rn "Describe(|IsRetiredPeriod" src tests        # minus bin/obj
grep -rn "BlockedTopicIds|BlocksForTopic|BlocksByTopicKey|_assignmentsByTopic|AssignmentDto" src tests   # minus bin/obj
grep -rn "NotContain|Contain(\"SubjectBlockLabels|Enrollment exceptions" tests/SchoolCollab.Admin.Tests.Unit/{GradeLevelDetailPageTests,SubjectsLandingPageTests,SubjectBlocksDialogTests,TopicEditDialogTests}.cs
grep -rn "AcademicYearDivision" src/SchoolCollab.Core src/Students --include=*.cs   # minus bin/obj
grep -rn "ListSubjectsByGradeAsync" src --include=*.cs --include=*.razor            # minus bin/obj
grep -rn "by-grade" src/Students/SchoolCollab.Students.Api/Endpoints/*.cs
grep -rn "ListTopicsByGrade(" src tests             # minus bin/obj
grep -rn "StubActivePeriodProvider|new ListGradeTopicAssignmentsHandler|new ListActivityGroupTopicAssignmentsHandler|new ListGradeTopicCurriculumByGradeHandler" tests
grep -rn "SubjectEnrollmentBlock|BlockedTopicIds" tests/SchoolCollab.Students.Tests.Unit/*.cs
grep -n "Division" .../PeriodConfiguration.cs .../StudentsDbContextModelSnapshot.cs   # division stored as int enum, default None
```

Key facts established by that audit and relied on below:

- `SubjectBlockLabels.Describe` has exactly **3 consumers**: `Detail.razor:300`,
  `Subjects.razor:440`, `GradeTopicsDialog.razor:147`; `IsRetiredPeriod` has 2 more in
  `SubjectBlocksDialog.razor` (:55, :179). All five call sites die in v3 → the class is deleted.
- `BlockedTopicIdsForGradeAsync`/`ForGroupAsync`/`BlockedTopicIdsAsync` are consumed by exactly
  the three availability handlers; `ResolveActivePeriodIdsAsync`/`CacheKeySegment` have no other
  callers → RISK-2 deletion is self-contained.
- `StubActivePeriodProvider` (test helper) is consumed by `EnrollStudentHandlerTests`,
  `ListGradeTopicAssignmentsCacheKeyTests`, `ListGradeTopicCurriculumByGradeHandlerTests`,
  `RemoveTopicAssignmentHandlerTests`, `SubjectAvailabilityTests` — the cache-key/availability
  consumers die with RISK-2; the EnrollStudent/RemoveTopicAssignment consumers are pre-existing
  and **keep** the stub (so the file is **edited**, not deleted — see file T10).
- `PeriodDeleteHandlerTests.cs:25` constructs `DeletePeriodHandler` with
  `new SubjectEnrollmentBlockRepository(...)` — the v1 block-guard test wiring that must revert.
- No test constructs `ListTopicsByGrade` with a `PeriodId` (only 0/1/2-arg forms exist), and
  `ListSubjectsByGradeHandlerErrorTests.cs:31-32` carries a **stale doc comment** naming a
  `PeriodIdSpecified_NoMatchingAssignment_ReturnsEmpty` test that no longer exists.
- `AcademicYearDivision` is an **int-backed enum** (`PeriodConfiguration.cs:40-42`,
  snapshot `b.Property<int>("Division")`, default `None`) — the new entity column follows.
- The v1 card design test `GradeLevelDetailPageTests.cs:1030-1036` contains the two assertions
  v3 legitimately invalidates (see AC-9 / trap replacements).

---

## Plan

### (a) GOAL

v1 shipped a period-only `SubjectEnrollmentBlock` (`PeriodId` **non-nullable**), grade-only UI,
a `SubjectBlocksDialog` on the Topics landing, and an availability predicate of the form
`bridge EXISTS AND NOT block(owner, topic, {activeYear, activeSubPeriod})` (the RISK-2 two-id
set). v3 **dissolves the period dependency entirely**:

1. An exception is a **period part** — `AcademicYearDivision` (`Terms` | `Semesters` | `None`) —
   plus a **nullable date span** (`StartDate`/`EndDate`, at least one bound, `End >= Start` when
   both set). It never references a period *instance*, so year rollover is a structural no-op and
   "Term 3" survives the calendar (§0 decisions 7–8).
2. Availability becomes **one date test** —
   `bridge(owner, topic) effective on the date AND NOT exceptionContains(owner, topic, date)` —
   with **no active-period lookup, no id set, no cache-key period segment**. `Division` is
   descriptive only and does not participate in matching (§0 decision 9). v1's RISK-2 two-id
   set is **deleted, not carried forward**.
3. The vocabulary and the surface rename: `SubjectEnrollmentException`, table
   `subject_enrollment_exceptions`, routes `/students/enrollment-exceptions` — free now because
   v1's migration is uncommitted (§0 decision 10). Management moves to a **dedicated
   `EnrollmentExceptions` page**; every other surface shrinks to a **count badge + a kebab that
   navigates**; `SubjectBlocksDialog` is retired (its body becomes the page's list section).

The refinement stays entirely Students-side: **`Assignments.Core`/`Assignments.Api` need ZERO
production changes** (the `ITopicAssignmentLookup` contract signature is unchanged; the client
already reduces with `dtos?.Any(d => d.TopicId == topicId)`).

### (b) SCOPE GATES

**IN:**
- Entity rename + reshape (`SubjectEnrollmentException`), migration edit-in-place, CQRS/API
  rename + reshape, availability rewrite to the date test.
- **Dissolutions (delete, do not port** — v1 built these and the worker must remove them):
  - **RISK-2 / the two-id "active period set"** — `SubjectAvailability.ResolveActivePeriodIdsAsync`
    and `CacheKeySegment`, the `activePeriodIds.Contains(b.PeriodId)` predicate, the
    active-period segment in the three cache keys, the `IActivePeriodProvider` injection in the
    three availability handlers, and the RISK-2-specific tests
    (`SubjectAvailabilityTests.GradeTopic_BlockedOnActiveAcademicYear_*` /
    `..._BlockedOnActiveSubPeriod_*` / `..._BlockedOnANonActivePeriod_*`,
    `ListGradeTopicAssignmentsCacheKeyTests.*_DifferentActivePeriod_*` /
    `..._DifferentActiveSubPeriod_*`). Replacements are named in (j)/AC-5 — **replace, never
    delete silently**.
  - **All retired-period rules**: "Archived/Deactivated cannot be excepted"
    (`TopicAssignmentPeriodValidator` block rules + `BlockSubjectHandlerTests`
    `Block_GradeArchivedPeriod_Throws422` / `Block_GradeDeactivatedPeriod_Throws422`), the
    archived-marker rendering (`SubjectBlockLabels.RetiredMarker`/`IsRetiredPeriod`,
    `SubjectBlocksDialog` badge), and `SubjectBlocksDialogTests.Picker_ExcludesRetiredAndAlreadyBlockedPeriods`'
    retired half. Nothing references a period, so nothing can be retired.
  - **FR-57's "within the active academic year" rejection**
    (`Block_GradeTermOutsideActiveAcademicYear_Throws422`,
    `Block_GradeAcademicYearPeriod_IsAllowedEvenWhenNotTheActiveYear`) — replaced by AC-7's
    any-Division/any-span acceptance test.
  - **v1's `ON DELETE RESTRICT` FK + orphan argument and the "period must be submittable"
    rule** — the migration's `periods` FK, `ValidateGradeBlockPeriodAsync`'s status
    admissibility, and `Block_UnknownPeriod_Throws422` all go; **`DeletePeriodHandler`'s v1
    block guard (P2-10) reverts** (file P22) — nothing can reference a period.
  - **`SubjectBlocksDialog`** (+ `.razor.css`) and its test file — the page replaces it.
  - **`SubjectBlockLabels`** (`Describe`/`DescribePeriods`/`IsRetiredPeriod`/`NoExceptions`/
    `RetiredMarker`) — the label concept dissolves into count badges and the page's span
    rendering; `"Offered in every period"` disappears from every card (spec §5.2: the absence
    of a badge is the normal state).
  - **The `?periodId=` listing filter** (Q5 — **ANSWERED: REMOVE**, see (e)/(b)):
    `Subjects.razor:95` param + `:386` argument, `StudentsApiClient.ListSubjectsByGradeAsync`'s
    `periodId` parameter and query-string plumbing, `TopicRoutes.cs` by-grade route's
    `Guid? periodId` parameter, `ListTopicsByGrade.PeriodId`, and
    `ListTopicsByGradeHandler.cs:30-36`'s period predicate. This retires the **last period-aware
    read path** (see the reader-chain resolution above).
- The new `EnrollmentExceptions` page (owner-aware, immediate write) + thin entry points.
- Cache invalidation on exception writes (carried from v1) with the key reverting to date-only.

**OUT — explicitly, do not touch:**
- `RequiresEnrollment` (sibling spec — separate round).
- Strands and lessons — `<StrandsEditor>` untouched; acceptance asserts the diff is EMPTY (AC-15).
- **`DeprecatedPeriodWritePathArchitectureTests.cs`, the deprecated `PUT
  /topic-assignments/{id}/period` endpoint, and `PeriodId` on `AssignGradeTopicRequest`** —
  stay no-ops, deprecated not removed; the architecture guard must pass **untouched**.
- **ANY change to `src/Assignments/**` production code** — zero (verified: the lookup client
  reduces by `TopicId` only).
- The `Assignments.Core → Students.Core` project reference (deferred,
  `documents/solution/cross-context-rule-followups.md` §1).
- `Program.cs` — never touched (endpoint-grouping rule; v1 P1-3 stands).
- `SubjectEditDialog` internals; the coded-value sections of `TopicEditDialog`.
- The v1 migration's **Step-0 conversion** — stays deferred to a separately reviewed script
  under owner sign-off (RISK-1 of the previous round; §(d) keeps the rule).
- `documents/**` — the spec is this round's **input**, not an output; `documents/solution/`
  updates are parent-owned.

### (c) DATA MODEL — `SubjectEnrollmentException`

`src/Students/SchoolCollab.Students.Core/Domain/SubjectEnrollmentException.cs` (renamed from
`SubjectEnrollmentBlock.cs`), exactly per spec §2.1:

```csharp
public sealed class SubjectEnrollmentException : BaseTenantEntityWithAudit, IHasRowVersion
{
    public Guid Id { get; private set; }
    public Guid? GradeLevelId { get; private set; }      // XOR ActivityGroupId
    public Guid? ActivityGroupId { get; private set; }   // XOR GradeLevelId
    public Guid TopicId { get; private set; }
    public AcademicYearDivision Division { get; private set; }   // a KIND, never a period instance
    public DateOnly? StartDate { get; private set; }     // null = open start
    public DateOnly? EndDate { get; private set; }       // null = open end
    public string? Reason { get; private set; }          // optional free text, never load-bearing
}
```

Invariants (entity `Create` factory + validator, private setters, no public setters — mirror
`TopicAssignment`/`GradeAssignmentPolicy`):

1. **Exactly one owner:** `GradeLevelId` XOR `ActivityGroupId`.
2. **At least one bound:** `StartDate` or `EndDate` set; both null → 422 ("never offered" is a
   different concept, §0 decision 8).
3. **Ordering:** both set → `EndDate >= StartDate` (422 otherwise).
4. **No period reference.** No `PeriodId`, no FK to `periods`, no period-status rule, no
   submittability rule. The v1 `ON DELETE RESTRICT` argument and the retired-period rules are
   **dissolved** with it.
5. **No status/lifecycle column.** Removal = soft delete (`BaseTenantEntityWithAudit` +
   `IHasRowVersion`/`xmin`); the `is_deleted = false` filter in the unique index is **required**
   (remove-then-re-add must not collide).
6. **Uniqueness — the COALESCE expression index.** Both bounds are nullable and Postgres treats
   NULLs as distinct, so the unique key is an **expression index** in raw SQL (§(d)), keyed on
   `(tenant_id, owner_id, topic_id, division, COALESCE(start_date,'-infinity'),
   COALESCE(end_date,'infinity'))`, filtered `owner_id IS NOT NULL AND is_deleted = false`, once
   per owner column. A plain `HasIndex` on the nullable columns would let the same open-ended
   exception be inserted twice. Duplicates are a **409**, not a silent second row.
7. **`Division` is descriptive only** — it never participates in availability matching and is
   not validated against dates (a `Terms` exception crossing a semester boundary is accepted,
   §2.4). It is validated only against the owner's FR-56 rule (§(f)).
8. **Storage of `Division`:** int-backed enum mirroring `Periods.Division`
   (`PeriodConfiguration.cs:40-42`, default `None`) — verified by grep, snapshot
   `b.Property<int>("Division")`.

**Availability semantics (single source of truth, spec §2.3):**

```
SubjectAvailable(owner, topicId, effectiveDate) =
      EXISTS bridge(owner, topicId) effective on effectiveDate
  AND NOT EXISTS exception(owner, topicId) where
        (start_date IS NULL OR start_date <= effectiveDate)
    AND (end_date   IS NULL OR end_date   >= effectiveDate)
```

One date test. `Division` does not match. All of it lives in the one shared helper
(`SubjectAvailability`, renamed methods) so the predicate and the cache key cannot drift; the
exception lookup stays tenant-scoped inside the cache factory (ambient tenant unavailable
there), and exception writes invalidate the `students` cache tag — both carried from v1.

### (d) MIGRATION — edit the existing uncommitted migration, do NOT add a second one

v1's migration `src/Students/SchoolCollab.Students.Core/Migrations/20260926170504_AddSubjectEnrollmentBlocks.cs`
(+ `.Designer.cs` + the model snapshot) is **unreleased and uncommitted** — v3 edits it in place;
there is exactly one migration and one Designer/snapshot regeneration.

**Step 0 — measure first (unchanged from v1; run and report verbatim):**

```sql
SELECT topic_assignment_type, grade_level_id, activity_group_id, count(*)
FROM topic_assignments
WHERE period_id IS NOT NULL
GROUP BY 1, 2, 3;
```

- **Zero rows** → purely additive: create `subject_enrollment_exceptions` (no `period_id`
  column at all) + the two raw-SQL expression unique indexes below.
- **Non-zero** → v1's rule stands: ship the **additive table only**; the whitelist→exception
  **conversion** is NOT written into the migration and stays deferred to a separately reviewed
  script under owner sign-off. (v1 measured **non-zero** — one pinned dev row. Converting would
  encode "offered only in X" semantics that §0 decision 2 rules out.) **The worker STOPS and
  reports if a conversion is ever thought necessary; this round never writes one.**

**The edit (all in `Up()`/`Down()` of the renamed `AddSubjectEnrollmentExceptions` partial):**
- Rename the class and the table: `subject_enrollment_blocks` → `subject_enrollment_exceptions`
  (pk/index/FK names follow the new table name).
- **Drop `period_id`** — the column, the `fk_subject_enrollment_blocks_periods_period_id`
  FOREIGN KEY (there is **no** period FK in v3; the v1 `ReferentialAction.Restrict` argument is
  dissolved), and `ix_subject_enrollment_blocks_period_id`.
- **Add** `division` (`int`, not null, default `AcademicYearDivision.None` — mirroring
  `Periods.Division`), `start_date` and `end_date` (`date`, nullable).
- **The two unique indexes are raw SQL the EF model cannot express** (COALESCE over nullable
  columns + `WHERE owner_id IS NOT NULL AND is_deleted = false`), created in `Up()` and dropped
  in `Down()` via `migrationBuilder.Sql(...)`:

```sql
CREATE UNIQUE INDEX ix_enrollment_exceptions_tenant_grade_topic_span
  ON subject_enrollment_exceptions
     (tenant_id, grade_level_id, topic_id, division,
      COALESCE(start_date, '-infinity'), COALESCE(end_date, 'infinity'))
  WHERE grade_level_id IS NOT NULL AND is_deleted = false;
-- plus the activity_group_id twin:
CREATE UNIQUE INDEX ix_enrollment_exceptions_tenant_group_topic_span
  ON subject_enrollment_exceptions
     (tenant_id, activity_group_id, topic_id, division,
      COALESCE(start_date, '-infinity'), COALESCE(end_date, 'infinity'))
  WHERE activity_group_id IS NOT NULL AND is_deleted = false;
```

`Down()` drops both by name **before** `DropTable`. The EF model
(`SubjectEnrollmentExceptionConfiguration`) keeps only the **plain** non-unique lookup indexes
(tenant+owner+topic+division, plus start/end as ordinary indexed columns if the existing pattern
indexes them) — the raw expression indexes must NOT appear in the model or the snapshot, so
`MigrationGuardTests.NoUncommittedModelChanges` stays green (a SQL-only index is invisible to the
model — v1 established this for `HasFilter`; same principle, stronger form).

### (e) API surface + deprecations

Routes stay grouped per the endpoint-organization rule — the renamed
`MapEnrollmentExceptionRoutes(this RouteGroupBuilder group)` appended to the students group chain
in `StudentEndpoints.cs` (never `Program.cs`; v1's P1-3 unauthenticated-group finding stands).

| Method | Route (v3, renamed) | Purpose |
|---|---|---|
| `GET` | `/students/enrollment-exceptions?gradeLevelId={id}` | list a grade's exceptions (or `?activityGroupId={id}`), optional `&topicId={id}` |
| `GET` | `/students/enrollment-exceptions/check?gradeLevelId={id}&topicId={id}&onDate={date}` | is this topic excepted on this date (pickers) — served by the list query narrowed to the topic, containment evaluated on `onDate` |
| `POST` | `/students/enrollment-exceptions` | create — `division` + `startDate`/`endDate` (**at least one bound**, else **422**); `endDate >= startDate` when both set |
| `DELETE` | `/students/enrollment-exceptions/{id}` | remove (idempotent soft delete) |

- **The body carries no period id — that is the point** (§0 decision 7). The v1
  `BlockSubject.PeriodId` field, the `?periodId=` route parameter, and the
  `SubjectEnrollmentBlockDto` `PeriodId`/`PeriodName`/`PeriodStatus` projections are all
  **removed**, not deprecated — the migration is uncommitted and the routes are unreleased, so
  there is no wire-compat surface to preserve (this is the rename's whole justification).
- **No feature flag** — enrollment correctness; two divergent availability rules are
  unacceptable (spec §6). `documents/configuration.md` unchanged.
- **Deprecated, not removed (carried from v1, untouched):** `PUT /topic-assignments/{id}/period`,
  `PeriodId` on `AssignGradeTopicRequest`, `AssignActivityGroupTopic.PeriodId`,
  `CreateTopicForGrade.PeriodId` — all stay accepted-and-ignored no-ops under
  `DeprecatedPeriodWritePathArchitectureTests`. **No deprecation work in this round**; only the
  incidental comment-token renames in four handler files (files P24–P26 and P44).
- **Q5 — `?periodId=` listing filter: ANSWERED — REMOVE (owner decision, 2026-09-26).**
  Resolved reader chain: `Subjects.razor:95` → `ListSubjectsByGradeAsync` →
  `/students/subjects/by-grade/{id}` → the deprecated `/subjects` alias of
  `TopicRoutes.MapTopicSubgroup` → `ListTopicsByGrade` → `ListTopicsByGradeHandler` — **one
  chain**. Removal touches: `Subjects.razor` (`[SupplyParameterFromQuery] Guid? PeriodId` and
  the `:386` call argument), `StudentsApiClient.ListSubjectsByGradeAsync` (drop the parameter +
  the `?periodId=` query branch; also correct the stale "Rev. 6 `PeriodId`" doc comments on the
  effective readers at `:1143-1147`/`:1156+`), `TopicRoutes.cs` by-grade route (drop the
  `Guid? periodId` parameter and its pass-through), `ListTopicsByGrade` (drop the member), and
  `ListTopicsByGradeHandler.cs:30-36` (drop the predicate). Coverage rule: **replace, never
  delete silently** — no test currently constructs the query with a `PeriodId`, so the
  replacement is a **new handler test pinning that a bridge row carrying a non-null (ignored)
  `PeriodId` no longer filters the listing** (file T7), plus correcting the stale
  `PeriodIdSpecified_...` doc comment in `ListSubjectsByGradeHandlerErrorTests.cs:31-32` (T8).

### (f) FR-56/FR-58 semantics

**FR-56 (group-owned) — REWRITTEN per spec §4.2.** The group's exception `Division` must match
the group's `EnrollmentSpan`:

| `EnrollmentSpan` | Required `Division` | Extra rule |
|---|---|---|
| `WholeAcademicYear` | `None` | — |
| `Termly` | `Terms` | — |
| `Semester` | `Semesters` | — |
| `DateRange` | `None` | span must fall **inside** `[EnrollmentStartDate, EnrollmentEndDate]`; a null group bound = unbounded on that side |
| `OpenEnded` | `None` | any span, open or bounded |

v1 declared `OpenEnded`/`DateRange` groups unexceptable — that clause is **gone**; the v1 tests
`Block_OpenEndedGroup_Throws422` / `Block_DateRangeGroup_Throws422` are replaced by acceptance
tests (AC-6). Implementation lives where v1's block validators lived
(`TopicAssignmentPeriodValidator`, renamed methods) so the rules stay beside the FR-56/57
bridge rules the deprecated write paths still use. **Deleted from the same file:** the
retired-period exclusion and the active-year rule.

**FR-57 (grade-owned):** no shape constraint at all — a grade has no window and no period
reference. Any `Division`, any span, including spans in years that do not exist yet. The v1
"within the active academic year" rejection and its test are **replaced, not silently deleted**
(AC-7 names the replacement).

**FR-58 — refined, purely date-based (spec §4.3).** Contract signature unchanged;
`Assignments.Core` does not change at all. The Students-side implementation gets **simpler**
than v1's: no `IActivePeriodProvider` resolution, no id set — the three cached readers
(`ListGradeTopicAssignments`, `ListActivityGroupTopicAssignments`,
`ListGradeTopicCurriculumByGrade`) swap the active-period-id exclusion for
`ExceptedTopicIdsOnDateAsync(db, tenantId, ownerFilter, effectiveDate)`, and their HybridCache
keys **lose the active-period segment** (back to date-only; the cache-poisoning failure mode
RISK-2 guarded against is gone because the predicate no longer depends on period state).
Exception writes still invalidate the `students` tag — the invalidation tests carry over.

### (g) EXPECTED FILES — exhaustive, derived by grep (65 entries; renames counted once, deletions counted)

**Production — Students.Core (32)**

| # | Path | Change |
|---|---|---|
| P1 | `src/Students/SchoolCollab.Students.Core/Domain/SubjectEnrollmentException.cs` | RENAME of `Domain/SubjectEnrollmentBlock.cs` + RESHAPE per (c): `PeriodId` out; `Division`, `StartDate?`, `EndDate?` in; `Create` factory enforces owner-XOR / ≥1 bound / End≥Start. Private setters, no public setters. |
| P2 | `src/Students/SchoolCollab.Students.Core/Domain/Exceptions/DuplicateSubjectEnrollmentException.cs` | RENAME of `DuplicateSubjectBlockException.cs`; 409 mapping unchanged. |
| P3 | `src/Students/SchoolCollab.Students.Core/Data/StudentsDbContext.cs` | `DbSet` + `ApplyConfiguration` renames only (narrow edit). |
| P4 | `src/Students/SchoolCollab.Students.Core/Data/Configurations/SubjectEnrollmentExceptionConfiguration.cs` | RENAME + reshape: plain (non-unique) indexes incl. the new columns; FKs grade/topic/activity-group Cascade; **no period FK**; soft-delete query filter. **The two unique COALESCE expression indexes are NOT in the model** — raw SQL only (§(d)). |
| P5 | `src/Students/SchoolCollab.Students.Core/Data/Repositories/ISubjectEnrollmentExceptionRepository.cs` | RENAME of `ISubjectEnrollmentBlockRepository.cs`. |
| P6 | `src/Students/SchoolCollab.Students.Core/Data/Repositories/SubjectEnrollmentExceptionRepository.cs` | RENAME of implementation. |
| P7 | `src/Students/SchoolCollab.Students.Core/Extensions.cs` | DI line rename (`AddScoped<ISubjectEnrollmentExceptionRepository, …>`). |
| P8 | `src/Students/SchoolCollab.Students.Core/Migrations/20260926170504_AddSubjectEnrollmentBlocks.cs` | EDITED IN PLACE (uncommitted): class → `AddSubjectEnrollmentExceptions`; table rename; **drop `period_id` + its FK + its index**; add `division` (int, default None) / `start_date` / `end_date`; **two raw-SQL COALESCE expression unique indexes created in `Up()` and dropped in `Down()`** (§(d)); Step-0 rule preserved. |
| P9 | `src/Students/SchoolCollab.Students.Core/Migrations/20260926170504_AddSubjectEnrollmentBlocks.Designer.cs` | Regenerated (renamed partial class, new model). |
| P10 | `src/Students/SchoolCollab.Students.Core/Migrations/StudentsDbContextModelSnapshot.cs` | Regenerated (plain indexes only; raw SQL invisible to the guard). |
| P11 | `src/Students/SchoolCollab.Students.Core/CQRS/SubjectEnrollmentExceptions/Commands/CreateSubjectEnrollmentException/CreateSubjectEnrollmentException.cs` | RENAME+RESHAPE of `CQRS/SubjectEnrollmentBlocks/Commands/BlockSubject/BlockSubject.cs` — `Division` + `StartDate?`/`EndDate?` + `Reason?`; no `PeriodId`. |
| P12 | `…/Commands/CreateSubjectEnrollmentException/CreateSubjectEnrollmentExceptionHandler.cs` | Shape validation (≥1 bound, End≥Start) + FR-56 mapping (§(f)) + duplicate → 409. |
| P13 | `…/Commands/RemoveSubjectEnrollmentException/RemoveSubjectEnrollmentException.cs` | RENAME of `RemoveSubjectBlock/…`; idempotent soft delete unchanged. |
| P14 | `…/Commands/RemoveSubjectEnrollmentException/RemoveSubjectEnrollmentExceptionHandler.cs` | RENAME; `students` cache-tag invalidation **preserved**. |
| P15 | `…/Queries/ListSubjectEnrollmentExceptions/ListSubjectEnrollmentExceptions.cs` | RENAME of `ListSubjectBlocks/…`; `PeriodId` param dropped; `topicId` retained. |
| P16 | `…/Queries/ListSubjectEnrollmentExceptions/ListSubjectEnrollmentExceptionsHandler.cs` | RENAME; period-name/status projection gone (DTO reshaped); tenant-scoped lookup inside any cache factory preserved. |
| P17 | `src/Students/SchoolCollab.Students.Core/CQRS/TopicAssignments/SubjectAvailability.cs` | REWRITTEN: `ExceptedTopicIdsOnDateAsync` (start ≤ d AND end ≥ d, null = open). **`ResolveActivePeriodIdsAsync` + `CacheKeySegment` DELETED** (RISK-2 dissolved). |
| P18 | `…/Queries/ListGradeTopicAssignments/ListGradeTopicAssignmentsHandler.cs` | Date-test exclusion; `IActivePeriodProvider` injection removed; cache key loses the active-period segment. |
| P19 | `…/Queries/ListActivityGroupTopicAssignments/ListActivityGroupTopicAssignmentsHandler.cs` | Same. |
| P20 | `…/Queries/ListGradeTopicCurriculumByGrade/ListGradeTopicCurriculumByGradeHandler.cs` | Same. |
| P21 | `…/TopicAssignments/TopicAssignmentPeriodValidator.cs` | Block validators replaced by exception-shape validators (FR-56 matrix + DateRange containment, null bound = unbounded); **retired-period + active-year rules DELETED**; the bridge FR-56/57 methods stay for the deprecated write paths. |
| P22 | `…/CQRS/Periods/Commands/DeletePeriod/DeletePeriodHandler.cs` | v1 block guard **REVERTED** (`ISubjectEnrollmentExceptionRepository` dependency removed; nothing can reference a period); FR-D2/FR-D6 guards untouched. |
| P23 | `src/Students/SchoolCollab.Students.Core/DTOs/SubjectEnrollmentExceptionDto.cs` | RENAME+RESHAPE of `SubjectEnrollmentBlockDto.cs`: Id, GradeLevelId?, ActivityGroupId?, TopicId, Division, StartDate?, EndDate?, Reason?, CreatedAt, UpdatedAt. `PeriodId`/`PeriodName`/`PeriodStatus` **gone**. |
| P24 | `…/Commands/AssignGradeTopic/AssignGradeTopicHandler.cs` | Comment-only: deprecation-comment token rename (handler half; the command record's doc comment is P41 — **both halves required**). |
| P25 | `…/Commands/AssignActivityGroupTopic/AssignActivityGroupTopicHandler.cs` | Comment-only: same (command record = P42). |
| P26 | `…/Topics/Commands/CreateTopicForGrade/CreateTopicForGradeHandler.cs` | Comment-only: same (command record = P43). |
| P41 | `…/Commands/AssignGradeTopic/AssignGradeTopic.cs:14` | Comment-only: `<c>SubjectEnrollmentBlock</c>` doc-comment token (command half of P24). AC-4/AC-10 whole-tree grep gates. |
| P42 | `…/Commands/AssignActivityGroupTopic/AssignActivityGroupTopic.cs:13` | Comment-only: same (command half of P25). |
| P43 | `…/Topics/Commands/CreateTopicForGrade/CreateTopicForGrade.cs:21` | Comment-only: same (command half of P26). |
| P44 | `…/Commands/UpdateTopicAssignmentPeriod/UpdateTopicAssignmentPeriodHandler.cs:20` | Comment-only: `<see cref="Domain.SubjectEnrollmentBlock"/>` → renamed cref — an unresolved cref is **CS1574 post-rename**, so this is compile-mandatory, not cosmetic. |
| P47 | `src/Students/SchoolCollab.Students.Core/CQRS/Topics/Queries/ListTopicsByGrade/ListTopicsByGrade.cs` | **Q5 — N1:** drop the `Guid? PeriodId = null` record member. **Compile-chained to P29:** `TopicRoutes.cs:105` constructs `new ListTopicsByGrade(gradeLevelId, effectiveDate, periodId)`, so dropping the route param forces this member drop in the same pass or the build breaks. |
| P48 | `…/CQRS/Topics/Queries/ListTopicsByGrade/ListTopicsByGradeHandler.cs:30-36` | **Q5 — N1:** drop the `query.PeriodId == null \|\| a.PeriodId == query.PeriodId` predicate (line 36). Compile-chained to P47. This retires the **last period-aware read path**. |

**Production — Students.Api (4)**

| # | Path | Change |
|---|---|---|
| P27 | `src/Students/SchoolCollab.Students.Api/Endpoints/EnrollmentExceptionRoutes.cs` | RENAME of `SubjectBlockRoutes.cs` + reshape: the four `/enrollment-exceptions` routes per (e); no `periodId` anywhere; `/check` computes containment on `onDate`. |
| P28 | `src/Students/SchoolCollab.Students.Api/StudentEndpoints.cs` | Chain call rename (`.MapEnrollmentExceptionRoutes()`). `Program.cs` NOT touched. |
| P29 | `src/Students/SchoolCollab.Students.Api/Endpoints/TopicRoutes.cs` | **Q5**: drop `Guid? periodId` from the by-grade GET (`:96-105`) and its pass-through. |
| P45 | `src/Students/SchoolCollab.Students.Api/Endpoints/TopicAssignmentRoutes.cs:92` | Comment-only: the deprecation comment names the v1 route `POST /students/subject-blocks` — the token must be renamed (AC-4's whole-tree grep gate). |

**Production — Students.Application (12)**

| # | Path | Change |
|---|---|---|
| P30 | `src/Students/SchoolCollab.Students.Application/Services/StudentsApiClient.cs` | Client rename/reshape (`ListSubjectEnrollmentExceptionsAsync` / `CreateSubjectEnrollmentExceptionAsync` / `RemoveSubjectEnrollmentExceptionAsync`; reshaped request/response records). **Q5**: `ListSubjectsByGradeAsync` loses `periodId` + the `?periodId=` branch; stale "Rev. 6 `PeriodId`" doc comments on the effective readers corrected. |
| P31 | `…/Components/Students/SubjectBlocksDialog.razor` | **DELETED** (retired in v3; list/add/delete body becomes the page's list section). |
| P32 | `…/Components/Students/SubjectBlocksDialog.razor.css` | **DELETED**. |
| P33 | `…/Components/Students/SubjectBlockLabels.cs` | **DELETED** — dissolved (`Describe`/`IsRetiredPeriod`/`NoExceptions`/`RetiredMarker` are all period-vocabulary; 5 call sites die in P34–P36). Span rendering becomes page-owned formatting. |
| P34 | `…/Components/Pages/Students/Subjects/Subjects.razor` | Landing: the label column becomes a **count badge**; the kebab's "Enrollment exceptions" action **navigates** to the page (no dialog open) — `?gradeLevelId=…&topicId=…` for grade-owned rows **or** `?activityGroupId=…&topicId=…` for group-owned rows. **Owner gate WIDENED per Q1 (owner decision 2026-09-26; spec §5.2 row 1):** the badge + kebab appear on **grade- and group-owned** rows — the v1 grade-only A.1 ruling is void; `Subjects.razor:559` already resolves `ActivityGroupId` per row, and the stale `:368` "group-owned period editing is NOT offered here" comment must be **corrected, not obeyed**. **Q5**: `:95` param + `:386` argument removed. |
| P35 | `…/Components/Pages/Students/GradeLevels/Detail.razor` | Card meta: **count badge** (`n exceptions`) replacing `SubjectBlockLabels.Describe(BlocksForTopic(…))`; kebab **navigates**; `BlocksForTopic` → exception-count helper; `BlocksByTopicKey`/`.GroupBy` cascade reshaped to counts (the two-blocks-per-topic crash guard may dissolve with the dictionary — the rewrite must say so). |
| P36 | `…/Components/Students/GradeTopicsDialog.razor` | Same badge + navigating action; `BlocksByTopicKey` cascade reshaped. |
| P37 | `…/Components/Students/TopicCreateDialog.razor` | Comment-only token rename (`:432`). |
| P38 | `…/Components/Students/TopicEditDialog.razor` | Comment-only token rename (`:7`). |
| P39 | `…/Components/Pages/Students/EnrollmentExceptions.razor` | **NEW page** per spec §5.1: owner selection (grade level **or** activity group, group side gated by `FEATURE:EnableActivityGroups`); list (topic, part label Term/Semester/—, human-readable span `Term 3` / `1–14 Mar 2027` / `from …` / `to …`, optional reason, delete); add (topic picker; part picker per **OOD-1**; term/semester picker resolving to that part's dates **or** free start/end dates, each independently clearable = open); **empty state is normal**; **immediate write, no Save button, no form model**; 409 surfaces the server's message. Route `/students/enrollment-exceptions`. **N4:** the page **reads** `?gradeLevelId=` / `?activityGroupId=` / `?topicId=` from its query string and **pre-selects** owner + topic (spec §5.2 row 1 requires the navigation to land pre-selected). |
| P40 | `…/Components/Pages/Students/EnrollmentExceptions.razor.css` | NEW — isolated CSS per the blazor-css-isolation skill (no inline `<style>`). |
| P49 | `…/Components/Students/EnrollmentExceptionLabels.cs` | **NEW (declared addition, P2-3).** Single producer of the count string (`FormatCount`), consumed by all four surfaces: the page, `Subjects.razor`, `Detail.razor`, `GradeTopicsDialog.razor`. Preserves the exact `"2 exceptions"` wording the landing test pins. |
| — | *(P30 note)* `StudentsApiClient.IsSubjectExceptedAsync` | **Pass-3 addition inside an existing file:** the `/check` client wrapper (either owner form + `topicId`/`onDate`) that §6's route requires and F7 flagged as having no client half. Not a separate file, so no new P-number. |
| P46 | `…/Components/Students/TopicEditDialog.razor.css:31` | Comment-only: the CSS comment names `SubjectBlocksDialog` (AC-10's whole-tree grep gate). |

**Assignments.Core / Assignments.Api — zero production changes** (verified by grep: the only
cross-context consumer is `TopicAssignmentLookupHttpClient`, which reduces by `TopicId`).

**Tests — Students.Tests.Unit (11)**

| # | Path | Change |
|---|---|---|
| T1 | `tests/SchoolCollab.Students.Tests.Unit/Domain/SubjectEnrollmentExceptionTests.cs` | RENAME+REWRITE of `Domain/SubjectEnrollmentBlockTests.cs`: owner XOR; ≥1 bound (both-null rejected); `EndDate >= StartDate`; `Division` is a kind (no period ref). |
| T2 | `…/CreateSubjectEnrollmentExceptionHandlerTests.cs` | RENAME+REWRITE of `BlockSubjectHandlerTests.cs`. **Retired v1 tests:** `Block_GradeArchivedPeriod_Throws422`, `Block_GradeDeactivatedPeriod_Throws422`, `Block_GradeTermOutsideActiveAcademicYear_Throws422`, `Block_GradeAcademicYearPeriod_IsAllowedEvenWhenNotTheActiveYear`, `Block_TermlyGroup_TermPeriod_IsAllowed` (period-shaped), `Block_TermlyGroup_SemesterPeriod_Throws422` (period-shaped), `Block_WholeAcademicYearGroup_AcademicYear_IsAllowed` (period-shaped), `Block_OpenEndedGroup_Throws422`, `Block_DateRangeGroup_Throws422`, `Block_UnknownPeriod_Throws422`. **Replacements:** the FR-56 **division** matrix (Termly→`Terms`, Semester→`Semesters`, WholeAcademicYear/OpenEnded→`None`, DateRange→`None` **+ containment** incl. null-bound cases), shape invariants (no bound → 422; End<Start → 422), duplicate → 409 (incl. the soft-deleted-row guard), owner XOR errors, unknown grade. Every retired test's replacement is named here — none deleted silently. |
| T3 | `…/SubjectAvailabilityTests.cs` | REWRITTEN to date semantics: bridge + no exception ⇒ available; exception span contains date ⇒ **not**; does not contain ⇒ available; **open start** matches any earlier date; **open end** matches any later date; no bridge ⇒ not available regardless; the **v1 rollover regression guard re-expressed** (bridge row, no exception, prior terms archived ⇒ still available — now trivially true and pinned anyway; **→ AC-19**); un-block ⇒ available again. **RISK-2 tests retired** (`GradeTopic_BlockedOnActiveAcademicYear_IsNotAvailable`, `GradeTopic_BlockedOnActiveSubPeriod_IsNotAvailable`, `GradeTopic_BlockedOnANonActivePeriod_IsStillAvailable`, `GroupTopic_BlockedOnActiveSubPeriod_IsNotAvailable`) — replaced by AC-5's "no active period at all" test. `BlockOnArchivedPeriod_StaysListable_AndDeletable` retires with the period reference (a span cannot be "archived"); its surviving intent — a removed exception can be re-added — lives in T2/T11. |
| T4 | `…/ListGradeTopicAssignmentsCacheKeyTests.cs` | REWRITTEN: the active-period key-segment tests (`*_DifferentActivePeriod_*`, `*_DifferentActiveSubPeriod_*`) **retire** — the key reverts to date-only and the RISK-2 failure mode is structurally gone; the **invalidation** tests (exception create/remove ⇒ cache recomputes) are retained and re-pointed at exception writes. |
| T5 | `…/DeletePeriodBlockGuardTests.cs` | **DELETED** — the guard dissolves (nothing references a period). |
| T6 | `…/PeriodDeleteHandlerTests.cs` | Block-guard tests removed; `DeletePeriodHandler` construction at `:25` reverts (no block repository). FR-D2/D6 tests untouched. |
| T7 | `…/ListSubjectsByGradeHandlerTests.cs` | **Q5 replacement** (never delete silently): new test pinning that a bridge row carrying a non-null (ignored) `PeriodId` **no longer filters the listing** — the retired whitelist read-path's replacement coverage. **F5 relabel: a regression guard, not a discriminator** — the handler filter applies only when the query **supplies** a `PeriodId`, so this test passes pre-round; the discriminator is the API-level half in T11 / AC-12. |
| T8 | `…/ListSubjectsByGradeHandlerErrorTests.cs` | Stale `PeriodIdSpecified_NoMatchingAssignment_ReturnsEmpty` doc-comment item (`:31-32`) corrected; no behavioural change. |
| T9 | `…/ListGradeTopicCurriculumByGradeHandlerTests.cs` | Compile-forced (P20's ctor loses `IActivePeriodProvider`) + RISK-2 assertions retired. |
| T10 | `…/StubActivePeriodProvider.cs` | **EDITED, not deleted**: the availability/cache-key consumers die with RISK-2, but `EnrollStudentHandlerTests` and `RemoveTopicAssignmentHandlerTests` still consume it (grep-verified) — trim only what loses its consumers. |
| T11 | `tests/SchoolCollab.Students.Tests.Integration/EnrollmentExceptionAvailabilityEndpointTests.cs` | RENAME+REWRITE of `SubjectBlockAvailabilityEndpointTests.cs` (real Postgres / Testcontainers): feed exclusion re-expressed against a **span** (exception containing `onDate` excludes; non-containing span leaves present — both on `GET /students/topic-assignments/by-grade/{id}?effectiveDate=…`, the array `TopicAssignmentLookupHttpClient.cs:50-53` reduces); open-ended duplicate POST → **409** (the expression index's whole purpose — the case a plain index would let through); remove-then-re-add the same span ⇒ 201. **F5 addition — the Q5 API-level discriminator:** pre-round, an extra `?periodId=` query param on the by-grade listing exact-match-filters (a non-matching id ⇒ empty list); post-round it is **ignored** and the list is unchanged. **F6b addition — the AC-18 `/check` test:** `GET /students/enrollment-exceptions/check?gradeLevelId=…&topicId=…&onDate=…` returns the containment answer for that date — for **both** owner forms (`?gradeLevelId=…` and `?activityGroupId=…`, mirroring the list route). |

**Tests — Admin.Tests.Unit (7)**

| # | Path | Change |
|---|---|---|
| T12 | `tests/SchoolCollab.Admin.Tests.Unit/SubjectBlocksDialogTests.cs` | **DELETED** with the dialog. Rules that survive are re-hosted in T13: empty-state-normal, add-with-nothing-selected-writes-nothing, 409-surfaces-server-message, no-Save-control. The retired-period picker half retires outright. |
| T13 | `tests/SchoolCollab.Admin.Tests.Unit/EnrollmentExceptionsPageTests.cs` | **NEW** bUnit page tests per spec §9: empty state normal (no warning); a term-shaped and a plain-window row both render part + span; open-start/open-end render `from …` / `to …`; removing the last exception succeeds; add-with-nothing-selected writes nothing; **no Save control exists**; server 409 surfaces the server's message; owner toggle (group side gated); **pre-selection from the query params (N4)**. **→ AC-17.** |
| T14 | `tests/SchoolCollab.Admin.Tests.Unit/SubjectsLandingPageTests.cs` | REWRITE: the label column assertions (`GradeOwner_SubjectBlockedInTwoTerms_ShowsBothPeriodNamesInTheColumn`, `:520`) become **count-badge** assertions; `GradeOwner_RowOffersEnrollmentExceptionsAction` / `EnrollmentExceptionsAction_OpensTheBlocksDialogForThatRow` become **navigation** assertions (the action routes to the page — no `ShowReadonlyDialogAsync`); `NonGradeOwner_RowOmits…` (`:577`) is **REPLACED per Q1** by an assertion that group-owned rows **do** offer the action — the gating intent survives only for whatever *genuinely* has no owner; `BlockLoadFails_TopicsStillLoadAndColumnFallsBack` re-expressed (badge renders `0`/empty on load failure); **Q5**: any landing `periodId` plumbing assertions removed with the feature. |
| T15 | `tests/SchoolCollab.Admin.Tests.Unit/GradeLevelDetailPageTests.cs` | REWRITE incl. the two **trap** replacements (see AC-9): `:1030` `Contain("SubjectBlockLabels.Describe(BlocksForTopic(t.TopicId)")` → badge-source assertion; `:1032` `NotContain("\"Enrollment exceptions\"")` → **navigation** assertion; `:1036` `BlocksByTopicKey`/`.GroupBy` assertions reshaped (or replaced by the count-cascade shape); `:379` `Detail_TopicsCard_SubjectBlockedInTwoPeriods_ShowsBoth_AndViewAllDoesNotCrash` rewritten to badge semantics (two exceptions ⇒ badge "2"; the crash-guard source assertion survives only if the dictionary survives). |
| T16 | `tests/SchoolCollab.Admin.Tests.Unit/TopicCreateDialogTests.cs` | Verify-only; comment tokens if any (`CreateDialog_GradeOwner_NullPeriodId_PostsPeriodIdNull` etc. pin the deprecated no-op — must keep passing). |
| T17 | `tests/SchoolCollab.Admin.Tests.Unit/TopicEditDialogTests.cs` | Comment-only (`:27` names the moved editor). |
| T18 | `tests/SchoolCollab.ArchitectureTests.Unit/DeprecatedPeriodWritePathArchitectureTests.cs` | **MUST SURVIVE UNTOUCHED** — both guards pass unchanged (deprecated PUT retained + marked; zero production callers). Listed so the reviewer audits non-edit, not forgotten. |
| T19 | `tests/SchoolCollab.Students.Tests.Unit/TopicAssignmentPeriodTests.cs:22-23` | Comment-only: the doc comment names `SubjectEnrollmentBlock` + `BlockSubjectHandlerTests` (AC-4/AC-10 whole-tree grep gates); rename the tokens, touch no assertion. |

**Total: 67 entries** (32 Core incl. 7 comment-only, 4 Api, 12 Application, 11
Students.Tests.Unit, 1 Integration, 7 Admin.Tests.Unit). Nothing else. If the worker finds a
needed file not on this list, it reports it in the Worker Report rather than silently widening
scope.

### (h) OPEN OWNER DECISIONS

**OOD-1 — spec §8 Q11: SETTLED — the owner answered "recommended as-is" *before* this plan
was written; the plan's first draft read the spec's pre-answer text and wrongly recorded this
as open. Corrected by the parent, not by a review pass.** The add-form's *part* picker offers
**only the divisions the tenant actually has** (derived from its period hierarchy), plus an
explicit *"not term-scoped"* option storing `Division = None`. A tenant with `Division = None`
years therefore never sees an empty Term/Semester list, and `None` gets an honest label instead
of reading as a magic value. **No blocker remains — pass 3's add-form is unblocked;** the worker
implements option (a) and must not choose an alternative.

**OOD-2 — Q5 is SETTLED (recorded, not open):** the owner answered **REMOVE** during this
round's planning (2026-09-26); encoded in (e) and AC-12. No longer open.

**Settled owner decisions — 2026-09-26 (post plan-review step 2b; parent-routed, owner-answered):**

- **Q1 = WIDEN.** The Topics landing's count badge + navigating kebab appear on **grade- and
  group-owned** rows (spec §5.2 row 1, owner-confirmed); the v1 A.1 grade-only ruling is
  **void**. Encoded in P34, T14, and AC-9. The stale `Subjects.razor:368` "group-owned period
  editing is NOT offered here" comment is corrected, not obeyed.
- **Q2 = DEFER spec §5.2 row 2 — owner sign-off recorded; a declared deviation, not a silent
  omission.** The ActivityGroups landing/details pages render **no subject list** (every
  apparent hit is a `catch (Exception ex)`), so that row presupposes a surface that does not
  exist; the group side stays reachable via the management page's owner toggle (§5.1).
  **No P entries, no tests** for it in this round.
- **Q3 = MERGE to 4 passes.** The DTO reshape and the v1-UI retirement are **one indivisible
  pass** (pass 2) — the dialog's *suppliers* die in pass 2, so a later retirement pass could
  never end buildable. Encoded in Phasing.

**No open decision remains.** (Previous-round A.1 is superseded by Q1; A.2 is dissolved by v3;
A.3 conversion deferral and A.5 UI-tester timing carry unchanged. Spec §8 Q1–Q7, Q9, Q10 are
answered in the spec; OOD-1 and OOD-2 above are settled.)

### (i) ACCEPTANCE CRITERIA — each must FAIL against pre-round code, or be honestly labelled a guard

Pre-round state = v1 as shipped: `SubjectEnrollmentBlock` with **non-nullable `PeriodId`**, a
`POST` that requires a period, active-period-id-based exclusion, label-based UI, the dialog, and
the `?periodId=` read filter live. So a criterion that passes pre-round is not evidence — such
criteria are labelled **guards**.

| # | Criterion | Why it fails pre-round / carries weight |
|---|---|---|
| AC-1 | **The date test (integration, real Postgres, feed point).** An exception with `Division = None` and a free span **containing** `effectiveDate` excludes the topic from `GET /students/topic-assignments/by-grade/{id}?effectiveDate=…` (the exact array the unchanged `TopicAssignmentLookupHttpClient` reduces); the same exception whose span does **not** contain the date leaves the topic present. Mutation-verified (M1). | Pre-round a `POST` body without `PeriodId` is impossible (validation fails) — no span-shaped exception can exist, so the test fails pre-round. |
| AC-2 | **Open bounds + shape invariants.** Open-start matches any earlier date; open-end matches any later date; both-null → **422**; `EndDate < StartDate` → **422**; owner-XOR → 422. | Pre-round the entity has none of these fields/shapes. |
| AC-3 | **Duplicate span → 409, index-proven.** Two exceptions with the same `(owner, topic, division, coalesced span)` → 409 — specifically **two open-ended exceptions with the same start** are rejected on **real Postgres** (the expression index's whole purpose; a plain nullable-column index would allow them). Remove-then-re-add the same span ⇒ 201. Handler-half + index-half evidence, per the v1 P2-1 lesson. | Pre-round: the span-shaped POST is impossible; the expression index does not exist. |
| AC-4 | **Rename integrity.** `/students/enrollment-exceptions` routes live; `/students/subject-blocks` returns 404; table `subject_enrollment_exceptions`; zero `subject_enrollment_block` / `subject-blocks` tokens anywhere in `src/`+`tests/` (grep gate). | Pre-round: the old routes/table exist; the new ones do not. |
| AC-5 | **RISK-2 dissolved.** With **no active period resolvable at all**, a span-containing exception still excludes the topic (pre-round v1's predicate returns `[]` without an active period ⇒ not excluded ⇒ fails). `ResolveActivePeriodIdsAsync`/`CacheKeySegment` are gone; the three readers no longer inject `IActivePeriodProvider`; cache keys carry no period segment. | Directly discriminates the dissolution; a worker who ports RISK-2 instead of deleting it fails this. |
| AC-6 | **FR-56 division matrix.** `Termly` group requires `Terms`; `Semester` requires `Semesters`; `WholeAcademicYear`/`OpenEnded` require `None`; `DateRange` requires `None` **and** containment inside the group's window (null group bound = unbounded). | Pre-round `OpenEnded`/`DateRange` groups are **unexceptable** (422) — the matrix's bottom half fails pre-round by construction. |
| AC-7 | **FR-57: any Division, any span.** A grade-owned exception with any `Division` and any span — including one in a far-future academic year — is accepted. **This is the named replacement for the retired `Block_GradeTermOutsideActiveAcademicYear_Throws422` test** — the v1 rejection is gone and its coverage is replaced, not deleted silently. | Pre-round the active-year rejection 422s the very payload this accepts. |
| AC-8 | **FR-58 via the unchanged port.** The v1 `ByGradeFeed_BlockOnActiveAcademicYear_ExcludesTopic` integration test is **re-expressed against a span** and passes at the feed point; `src/Assignments` diff is EMPTY. | The span-shaped exclusion does not exist pre-round (AC-1's argument applies at the feed). |
| AC-9 | **Badge + navigating kebab on all four surfaces** (landing, grade-detail card, View-all, and implicitly the page): count badge shows `n exceptions` for a subject with exceptions and **no badge** without; the kebab **navigates** to `/students/enrollment-exceptions` with owner/topic pre-selected — on **grade- and group-owned** rows, per Q1 (spec §5.2 row 1); **no dialog opens from any card**; `"Offered in every period"` is gone; `SubjectBlockLabels` has zero references. **The two trap guards are replaced intent-preserving, explicitly:** (i) `GradeLevelDetailPageTests.cs:1030`'s label-source assertion is replaced by a **badge-source assertion** (the count wiring exists in `Detail.razor` source); (ii) `:1032`'s `NotContain("\"Enrollment exceptions\"")` is replaced by a **navigation assertion** — the kebab's action routes to the page (and no `ShowShellDialogAsync`/`ShowReadonlyDialogAsync` exception path exists on the card) — preserving the guard's intent ("no inline editor; the affordance navigates") under the new mechanism, **not by deleting the guard**. | Pre-round the labels and the dialog exist; the kebab opens a dialog; the badge does not exist. |
| AC-10 | **Retirement.** `SubjectBlocksDialog.razor`(+css), `SubjectBlockLabels.cs`, `SubjectBlocksDialogTests.cs`, `DeletePeriodBlockGuardTests.cs` are deleted; zero grep hits for `SubjectBlocksDialog|SubjectBlockLabels|IsRetiredPeriod|ResolveActivePeriodIds|DeletePeriodBlockGuard` in `src/`+`tests/`. | Pre-round all of these exist. Structural half is guard-labelled; the deletion half is a real discriminator. |
| AC-11 | **Deprecated-period guard intact (guard — stated as such).** `DeprecatedPeriodWritePathArchitectureTests` passes **untouched**; the deprecated `PUT /topic-assignments/{id}/period` and `PeriodId` on `AssignGradeTopicRequest` stay accepted-and-ignored no-ops. | Gate on the deprecate-don't-delete posture; must not be weakened to pass anything else. |
| AC-12 | **Q5 executed.** `?periodId=` is gone from the by-grade route, `ListTopicsByGrade`, the client, and the landing. Two halves (F5): (i) the **API-level discriminator** — pre-round, an extra `?periodId=` query param on the by-grade listing exact-match-filters (a non-matching id ⇒ empty list); post-round it is **ignored** and the list is unchanged — this fails pre-round; (ii) the handler test (T7) pins that a bridge row carrying a non-null (ignored) `PeriodId` **no longer filters the listing** — **a regression guard, not a discriminator** (the handler filter applies only when the query supplies a `PeriodId`, so it passes pre-round). | Half (i) fails pre-round (the filter is live and does filter); half (ii) is honestly labelled a guard. |
| AC-13 | **Cache invalidation on exception writes (guard — honestly labelled).** Creating/removing an exception invalidates the `students` cache tag so the three readers recompute. | v1 already invalidates on block writes, so this passes pre-round — it is a carried-forward invariant, labelled a guard, kept because the rewrite must not lose it. |
| AC-14 | **DeletePeriod guard reverted.** Deleting a `Draft` period succeeds (204) while span-shaped exceptions exist for its grade's subjects — no exception consult, no 422; the guard code and `DeletePeriodBlockGuardTests` are gone. | Pre-round v1's guard 422s a `Draft` period carrying a block row; post-round the guard cannot fire (no period reference exists). Discriminator is behavioural where seedable, structural otherwise — stated as such. |
| AC-15 | **Strands/lessons diff is EMPTY (guard, not a pre-round discriminator — stated as such).** | Prevents scope creep into `<StrandsEditor>`. |
| AC-16 | **Gates.** `dotnet build SchoolCollab.slnx` — **0 errors**; affected test projects — **0 failures** (`dotnet test`). | Gate (not a discriminator); parent runs the authoritative suite at freeze. |
| AC-17 | **Page behaviours (spec §5.1/§9 — F6a).** On `EnrollmentExceptions.razor`: empty state is normal (no warning); a term-shaped and a plain-window row both render part + span; open-start/open-end render `from …` / `to …`; add-with-nothing-selected writes nothing; **no Save control exists**; server 409 surfaces the server's message; owner toggle gates the group side. Tested in T13. | Pre-round the page does not exist — every clause fails by construction. |
| AC-18 | **`/check` endpoint (spec §6 — F6b).** `GET /students/enrollment-exceptions/check?gradeLevelId=…&topicId=…&onDate=…` returns the containment answer for that date (span containing `onDate` ⇒ excepted; non-containing or absent ⇒ not). Integration-tested at the real endpoint (test in T11). Accepts **either** owner form — `?gradeLevelId=` **or** `?activityGroupId=` — mirroring the list route's owner forms (Q1 widened the gate to group-owned rows, so both sides of the owner toggle need it). | Pre-round the route does not exist (v3-new; P27) — the v1 `SubjectBlockRoutes.cs` has no `/check` route. |
| AC-19 | **Rollover regression guard (guard — honestly labelled, F6c).** With a bridge row and no exception, prior terms archived ⇒ the topic remains available at the feed point (T3's re-expressed §9 guard). | Passes pre-round under v1's rules too — structurally trivial post-round (no period reference exists) and pinned anyway; kept because the rewrite must not lose the §9 intent. |

### (j) TEST REQUIREMENTS

Per `.github/copilot/rules/testing.md` (MSTest + Moq + FluentAssertions, MTP); read
`.github/copilot/rules/dotnet-best-practices.md` + its SKILL before any C#.

- **Moq must not be used for HTTP mocking** — use the `ScriptedHandler` pattern for HTTP-backed
  bUnit clients (precedent: `SubjectsLandingPageTests.cs` harness, `AssignmentAiPromptPageTests.cs:33`).
- **Mutation-verified criteria (name the mutation in the test's comment):**
  - **M1 → AC-1/AC-5/AC-8:** remove the `NOT exceptionContains(date)` predicate (or make it
    match everything) — the span-containment, no-active-period, and feed tests must fail.
  - **M2 → AC-3:** drop the `COALESCE` expression index or its `is_deleted` filter — the
    open-ended-duplicate re-add test must fail with `23505` absent / duplicate present (real
    Postgres is the only place the expression index can be observed; unit tests over EF InMemory
    must **disclaim** index evidence, per the v1 P2-1 lesson).
  - **M3 → AC-9:** make a card kebab open a dialog instead of navigating — the navigation
    assertion fails.
  - **M4 → AC-7:** resurrect any period rule in the exception validators — the any-Division/
    any-span test fails.
  - **M5 → AC-12:** re-add a period filter to `ListTopicsByGrade` — the ignored-`PeriodId`
    listing test fails.
  - **M6 → AC-18:** make `/check` ignore `onDate` (return a constant answer) — the AC-18
    containment test fails.
- **bUnit gotchas (pitfall doc §8):** `Error="@Error"` never `Error="Error"`;
  `FluentSelect`/`FluentMenu` don't materialise children while closed — assert on bound
  `Items`/delegates; invoke row actions via `cut.InvokeAsync(...)`; never key subject collections
  by `TopicId`; no inline `<style>` (CSS isolation skill).
- **Every retired v1/v2 test named in (g)/(b) has its replacement named — replace, never delete
  silently.** The dissolution is the round's point; silent test deletion is a review blocker.
- Integration tests only where a Testcontainers fixture already exists
  (`SubjectBlockAvailabilityEndpointTests` pattern) — do not write speculative fixtures.

### (k) CONSTRAINTS for the worker

1. **Bin locks:** before any `dotnet` invocation, check for running holders
   (`Get-Process dotnet`); `MSB3021`/`MSB3027` is a **LOCK, not a code failure** — report it as
   such and let the parent clear it. Do not retry builds in a loop.
2. **Build discipline:** `dotnet build SchoolCollab.slnx` after every pass; fix compiler errors
   before continuing. The worker runs build + affected unit suites; the **parent runs the
   authoritative full suite at freeze** (the previous round's §F lesson: never let a long test
   run eat a worker's cap — for any small fix pass, worker edits + build only).
3. Repo-scoped searches only — never `find /` or searches outside the working tree.
4. **Central Package Management:** all versions in `Directory.Packages.props`; no `Version=`
   on any `PackageReference` (NU1008/NU1009). No new packages are expected this round.
5. **All new endpoints grouped via an extension method** on the students group chain
   (`StudentEndpoints.cs`) — never inline in `Program.cs` (v1 P1-3: an unauthenticated group).
6. **New entity properties get domain methods, not public setters** — `Create` factory +
   private setters, mirroring `TopicAssignment`.
7. Target `net10.0`; CQRS via `ICommandHandler`/`IQueryHandler` + Scrutor scanning — no MediatR;
   `xmin` row-version concurrency (`IHasRowVersion`).
8. **Tenant isolation:** the exception lookup inside any HybridCache factory filters `TenantId`
   explicitly (the ambient tenant is unavailable there — preserve the v1 comment pattern);
   exception writes invalidate the `students` tag.
9. **Cross-context:** zero changes under `src/Assignments/**`; no new ports, base addresses,
   AppHost `WithReference`s, or project-reference changes. `CrossModuleWiringTests` red = blocker.
10. **Diff isolation:** all diffs computed against round base `3e408bf` (the tree is dirty with
    uncommitted v1 work — see the header). Patch-freeze idiom:
    `git add -N -- src tests` → `git diff 3e408bf -- src tests > documents/rounds/diffs-enrollment-exceptions.patch` →
    `git reset -q -- src tests`.
11. Do not create the PR; do not commit unless the parent says "commit" (main-branch merge
    policy applies; the parent owns git).

### Phasing — four sequential passes, each ends buildable and testable

- **Pass 1 — RENAME ONLY (mechanical, behaviour-preserving; touches every file later passes
  touch, so it lands first).** Rename everything without changing any shape:
  entity/exception class + file, exception type, DTO, repository pair, EF configuration,
  `CQRS/SubjectEnrollmentBlocks/` → `CQRS/SubjectEnrollmentExceptions/` (commands + query),
  `SubjectBlockRoutes.cs` → `EnrollmentExceptionRoutes.cs`, route strings
  `/subject-blocks` → `/enrollment-exceptions`, `StudentEndpoints.cs` chain call,
  client method/record names, migration class + table rename
  (`subject_enrollment_blocks` → `subject_enrollment_exceptions`) **keeping `period_id` and the
  v1 indexes intact**, comment-token renames (P24–P26 + their command halves P41–P43, P44,
  P45, P37, P38, P46, T19), and the mechanical test-file
  renames/URL-string updates (T1/T2/T11 names; integration route strings). **Zero behavioural
  change.** Must leave green: build **0 errors**; `Students.Tests.Unit`,
  `Students.Tests.Integration`, `Admin.Tests.Unit`, `ArchitectureTests.Unit` all pass with
  unchanged counts (renamed only).
- **Pass 2 — RESHAPE + RETIRE (one atomic pass — Q3).** The reshape and the v1-UI retirement
  are **indivisible**: the DTO reshape (P23) deletes `PeriodId`/`PeriodName`/`PeriodStatus`,
  which breaks `SubjectBlocksDialog.razor` (`:54/:55/:182/:242/:226/:266`) and
  `SubjectBlockLabels.DescribePeriods`, transitively breaking `Detail.razor:300`,
  `Subjects.razor:440`, `GradeTopicsDialog.razor:147` — the v1 dialog's *suppliers* die here,
  so the dialog cannot survive this pass buildably. Contents, in one pass: entity fields (P1
  reshape), migration column drop/add + the two raw-SQL expression unique indexes (P8–P10),
  `Create` invariants, FR-56 exception-shape validators + dissolution of the
  retired-period/active-year rules (P21), `SubjectAvailability` date rewrite + RISK-2 deletion
  (P17), the three readers + cache key (P18–P20), `DeletePeriodHandler` guard revert (P22),
  DTO reshape (P23), CQRS command/query reshape (P11–P16), **Q5 read-side removal** (P29,
  P30-client-half, `ListTopicsByGrade`, handler predicate) and the `Subjects.razor` two-line
  Q5 edit (part of P34, narrowly); **plus the retirement:** `SubjectBlocksDialog.razor`(+css)
  and `SubjectBlockLabels.cs` deleted (P31–P33) with T12, the three label surfaces rewired to
  the badge + navigating kebab (P34–P36 — grade- **and** group-owned rows per Q1), and the
  T14/T15 rewrites (incl. the AC-9 trap replacements); test rewrites T1–T11.
  **OOD-1 does not block this pass.** Must leave green: build **0 errors**;
  `Students.Tests.Unit` + `Students.Tests.Integration` pass with the rewritten suites;
  `Admin.Tests.Unit` passes with T12 deleted and T14/T15 rewritten; `ArchitectureTests.Unit`
  green; `MigrationGuardTests` green (raw SQL invisible to the model guard).
- **Pass 3 — THE PAGE.** `EnrollmentExceptions.razor` + `.razor.css` (P39/P40) consuming the
  reshaped client; `EnrollmentExceptionsPageTests` (T13 — AC-17). The add-form's part picker
  follows **OOD-1 (settled)**: offer only the divisions the tenant actually has, plus an explicit
  *"not term-scoped"* option storing `Division = None`. The v1 dialog is already gone (retired in
  pass 2) — there is no coexistence period. Must leave green: build **0 errors**; new page tests
  pass; full existing suite unaffected.
- **Pass 4 — RETIREMENT SWEEP + gates.** Grep-verified zero residue:
  `SubjectEnrollmentBlock`, `subject-blocks`, `SubjectBlocksDialog`, `SubjectBlockLabels`,
  `IsRetiredPeriod`, `BlockedTopicIds`, `ResolveActivePeriodIds`, `subject_enrollment_blocks`
  across `src/` and `tests/` (renamed successors only). T16/T17 verify-only. Freeze the round
  patch vs `3e408bf`. Must leave green: build **0 errors**; parent-authoritative full suite
  (1170-test baseline: Admin 570 / Students.Unit 466 / Integration 62 / Architecture 72,
  adjusted for this round's net test-count change) — **0 failures**.

The worker report must state which pass it reached and, for OOD-1, present the spec's
recommendation back to the parent verbatim if unanswered.

### Worker task spec — Pass 1 (rename only)

**Role:** worker, pass 1 of 4 of round `round-enrollment-exceptions.md`. **Rename only — zero
behavioural change.** Read the round doc §(c)–(g) first; this brief is the contract for scope.

**Do exactly this:**

1. Rename files and symbols (old → new), fixing compile sites mechanically. Symbol map:
   `SubjectEnrollmentBlock` → `SubjectEnrollmentException`;
   `SubjectEnrollmentBlockDto` → `SubjectEnrollmentExceptionDto`;
   `ISubjectEnrollmentBlockRepository`/`SubjectEnrollmentBlockRepository` →
   `ISubjectEnrollmentExceptionRepository`/`SubjectEnrollmentExceptionRepository`;
   `SubjectEnrollmentBlockConfiguration` → `SubjectEnrollmentExceptionConfiguration`;
   `DuplicateSubjectBlockException` → `DuplicateSubjectEnrollmentException`;
   `BlockSubject` → `CreateSubjectEnrollmentException`;
   `RemoveSubjectBlock` → `RemoveSubjectEnrollmentException`;
   `ListSubjectBlocks` → `ListSubjectEnrollmentException`s;
   `BlockSubjectRequest` → `CreateSubjectEnrollmentExceptionRequest`;
   `SubjectBlockRoutes`/`MapSubjectBlockRoutes` → `EnrollmentExceptionRoutes`/
   `MapEnrollmentExceptionRoutes`;
   `SubjectBlocksDialog`/`SubjectBlockLabels` symbols stay for now (they are deleted in pass 2 —
   but their **file names stay too**; do not rename them, just leave them compiling).
2. Route strings: `/students/subject-blocks` → `/students/enrollment-exceptions` in
   `EnrollmentExceptionRoutes` (renamed file) and `StudentsApiClient`; update the integration
   test URLs in `SubjectBlockAvailabilityEndpointTests` accordingly. The GET list route's
   `periodId` parameter stays this pass (it is dropped in pass 2).
   **Also (N2a):** three **Admin** test files pin `/students/subject-blocks` URL literals and go
   runtime-red the moment the client URL renames — `SubjectBlocksDialogTests.cs:99,236,261,275,293,312-313,332,364`,
   `SubjectsLandingPageTests.cs:151` (the `BlocksUrl` constant), and `GradeLevelDetailPageTests.cs:172`.
   Update those **URL literals** — they are not assertions, so step 6's "do not weaken an assertion"
   rule is not violated by touching them — or the pass-1 Admin-green gate fails.
3. Migration: in `20260926170504_AddSubjectEnrollmentBlocks.cs` (+ Designer) rename the partial
   class to `AddSubjectEnrollmentExceptions` and the table + pk/index/FK names
   `subject_enrollment_blocks` → `subject_enrollment_exceptions`. **Do not** touch columns or
   indexes otherwise (that is pass 2). Regenerate/adjust the snapshot's table name so
   `MigrationGuardTests` stays green.
4. Folder renames: `CQRS/SubjectEnrollmentBlocks/` → `CQRS/SubjectEnrollmentExceptions/` (keep
   `BlockSubject/`→`CreateSubjectEnrollmentException/`, `RemoveSubjectBlock/`→
   `RemoveSubjectEnrollmentException/`, `ListSubjectBlocks/`→`ListSubjectEnrollmentExceptions/`
   folder names matching the renamed records).
5. Comment-only token renames in P24+P41 (`AssignGradeTopicHandler.cs` +
   `AssignGradeTopic.cs:14`), P25+P42 (`AssignActivityGroupTopicHandler.cs` +
   `AssignActivityGroupTopic.cs:13`), P26+P43 (`CreateTopicForGradeHandler.cs` +
   `CreateTopicForGrade.cs:21`), P44 (`UpdateTopicAssignmentPeriodHandler.cs:20` — the
   `<see cref=…/>` cref), P45 (`TopicAssignmentRoutes.cs:92` — the `POST
   /students/subject-blocks` comment), P37 (`TopicCreateDialog.razor:432`), P38
   (`TopicEditDialog.razor:7`), P46 (`TopicEditDialog.razor.css:31` — the `SubjectBlocksDialog`
   comment), and T19 (`TopicAssignmentPeriodTests.cs:22-23`) — the referenced type/route names
   change, nothing else. **N2b:** for P38/P46/T17 the v1 token `SubjectBlocksDialog` has **no
   renamed successor in pass 1** (the dialog is deleted in pass 2; the page arrives in pass 3) —
   reword those comments to point at *"the EnrollmentExceptions management page"*, or drop the
   reference. Do not invent a type name that does not exist yet.
6. Test files: rename `Domain/SubjectEnrollmentBlockTests.cs` →
   `Domain/SubjectEnrollmentExceptionTests.cs`, `BlockSubjectHandlerTests.cs` →
   `CreateSubjectEnrollmentExceptionHandlerTests.cs`; update their internal symbol tokens
   mechanically. **Do not add, remove, or weaken any assertion.**

**Do NOT:** change any entity property, DTO member, validator rule, availability predicate,
cache key, route parameter, or UI behaviour; touch `Program.cs`, `src/Assignments/**`,
`DeprecatedPeriodWritePathArchitectureTests.cs`; commit; add packages.

**Must leave green (verify before reporting):** `dotnet build SchoolCollab.slnx` — 0 errors;
`dotnet test` on `Students.Tests.Unit`, `Students.Tests.Integration`, `Admin.Tests.Unit`,
`ArchitectureTests.Unit` — 0 failures, counts unchanged from the baseline modulo file renames.

**Report:** files changed (old→new), the grep proving zero `SubjectEnrollmentBlock`/`subject-blocks`
tokens remain outside the pass-2-bound files (`SubjectBlocksDialog*`, `SubjectBlockLabels.cs`,
and their tests), build/test results verbatim, and any compile site the map above did not
anticipate.

---

## Worker Report

### Pass 1 — RENAME ONLY (complete; green after a parent repair)

- **Pass reached:** 1 of 4 — **complete**.
- **Step-0 measurement:** unchanged — v1 measured **non-zero** (one pinned dev row), so the additive-only / no-conversion rule stands.
- **Files changed:** 48. **Renamed:** entity, exception type, DTO, repository pair, EF configuration, `CQRS/SubjectEnrollmentBlocks/` → `…SubjectEnrollmentExceptions/` (+ its 6 files), `SubjectBlockRoutes.cs` → `EnrollmentExceptionRoutes.cs`, the migration class/table/ID, 3 test files. **Content edits:** the DbSet property, migration Designer + snapshot, the 7 comment-token files (P41–P46, T19), 3 Admin URL literals.
- **Grep proof (worker):** `SubjectEnrollmentBlock` → **0** hits in `src`+`tests`; `subject_enrollment_blocks` → **0**; `subject-blocks` → only CSS class/id strings inside the pass-2-deleted `SubjectBlocksDialog.razor(.css)`.
- **Build (worker):** `dotnet build SchoolCollab.slnx` → **0 errors** (17 warnings incremental / 79 cold, all pre-existing). No MSB3021/MSB3027.
- **Worker-reported deviations:** the migration **ID string** changed with its class (flagged for a parent ruling); 8 compile sites the symbol map did not anticipate were **reported, not silently absorbed**; `SubjectBlocksDialog`/`SubjectBlockLabels` left compiling for pass 2.

#### PARENT REPAIR — EF model snapshot desync (found by the parent's authoritative suite)

**The worker's build-only gate passed, but the rename was NOT behaviour-preserving.** The authoritative suite found **64 failures**:

| Project | Baseline | After pass 1, before repair |
|---|---|---|
| Students.Tests.Integration | 62 | **62 failed — every test** |
| Students.Tests.Unit | 466 | **1 failed** — `NoUncommittedModelChanges` |
| ArchitectureTests.Unit | 72 | **1 failed** — `NoDomainDbContext_HasPendingModelChanges` |
| Admin.Tests.Unit | 570 | 570 ✓ |

(The other 3 solution-wide failures — `ChatAsync_WithOpenRouter_*` in `Settings.Tests.Integration` — are **environmental** ("Service request failed"), unrelated to the rename.)

**Root cause — a 63-character Postgres identifier limit, invisible to any token grep.** The table rename
`subject_enrollment_blocks` → `subject_enrollment_exceptions` (+4 chars) pushed the auto-generated FK name
from **61** to **66** characters: `fk_subject_enrollment_exceptions_activity_groups_activity_group_id`.
Postgres caps identifiers at 63 bytes, so EF now **truncates it in the model** (`…_activity_grou`) while the
snapshot, Designer and migration kept the untruncated string → `HasPendingModelChanges()` = **true** →
`Migrator.ValidateMigrations` threw inside `ApiFactory.InitializeAsync()` (`ClassInitialize`), so **every**
test in the 62-test integration class died. The other three FKs (60/50/50 chars) are fine — only this one
overflows.

**Diagnosis (parent):** `dotnet ef migrations has-pending-model-changes` → "Changes have been made to the
model…"; a throwaway `dotnet ef migrations add _DiagPendingDiff` exposed the delta as a single
`DropForeignKey`/`AddForeignKey` pair differing **only by the last two characters**. The probe `add`
incidentally regenerated `StudentsDbContextModelSnapshot.cs` from the live model (now correct);
`migrations remove` needs a DB at `127.0.0.1:5432` and failed, so the parent deleted the two probe files
directly and reconciled the remaining strings.

**Repair:** the migration declares its FKs inline in `CreateTable` (`Down()` just drops the table), so exactly
**two strings** changed — `…_activity_group_id` → `…_activity_grou` in
`20260926170504_AddSubjectEnrollmentExceptions.cs` and its `.Designer.cs` — making **Designer == snapshot ==
model**. Behaviour-preserving: Postgres would have stored the 62-char truncated name regardless, so the
declared name is now honest rather than silently truncated.

**Verified after repair (parent, authoritative):** `has-pending-model-changes` → **"No changes have been
made to the model since the last migration."**; `Students.Tests.Unit` **466/466**, `ArchitectureTests.Unit`
**72/72**, `Students.Tests.Integration` **62/62** — **0 failures**, the baseline exactly restored.

**Rulings recorded:**
- **Migration ID change — ACCEPTED.** The migration is unreleased and uncommitted, so renaming its ID with its
  class is free (the old name would otherwise fail the `SubjectEnrollmentBlock` grep gate). Consequence: a DB
  that already applied the v1 migration holds a stale `__EFMigrationsHistory` row — only the local dev DB can
  be in that state, and the round already treats it as disposable (Step-0).
- **FK name — left truncated for pass 1.** An explicit shorter `HasConstraintName` would be a *model* change,
  which pass 1 (rename-only) must not contain; pass 2's reshape may revisit it if a clearer name is wanted.

**Lesson for the round (record):** a **build-only** worker gate cannot catch a rename whose *emergent* effect is
a provider-limit overflow. The parent's authoritative suite is what caught it — the split worked exactly as
intended, and the sweep in pass 4 must not assume "rename-only" means "nothing can break".

**Diff-review run hygiene (verified 2026-09-26):** the freeze idiom (`git add -N -- src tests` → `git diff
<base>` → `git reset -q -- src tests`) does **not** leave residue when a review run dies before its reset.
Checked after the first attempt (`17124502`, provider connection error): `git status --porcelain` carried
**zero `A`** (intent-to-add) entries. The seemingly-dirty `git diff --name-only` count of 39 is simply
`37 M + 2 D` modified/deleted tracked files — the round's ordinary working state. **Detect intent-to-add via
`git status --porcelain` looking for `A`, not via `git diff --name-only`.**

**Grep hygiene (same check, recorded because it nearly produced two false findings):** when verifying an
identifier rename is complete, grep for the **full identifier**, not a trailing fragment. The fragment
`activity_groups_activity_group_id` matched `fk_activity_group_memberships_…` and
`fk_topic_assignments_…` (other tables' FKs) and looked like un-renamed residue; the full name
`fk_subject_enrollment_exceptions_activity_groups_activity_group_id` correctly showed **0 old / 1 new** in
the migration, its Designer and the snapshot.

**Review-run cost (found 2026-09-26):** the review briefs instruct whole-tree token sweeps
(`grep -rn "…" src tests`). **Without `--exclude-dir=bin --exclude-dir=obj`** those crawl thousands of
build-output files and block `bash` for many minutes — the diff-review run `348c789d` tripped the 240s
tool-timeout attention signal exactly this way (its log shows `exclude-dir` appears **0** times). Findings
were unaffected; only wall-clock was. Review briefs should mandate the exclusions, or prefer `git grep`,
which respects gitignore.

### Pass 2 — RESHAPE + RETIRE (complete; green)

- **Pass reached:** 2 of 4 — **complete**.
- **Scope:** Atomic reshape + retire:
  - `SubjectEnrollmentException` entity reshaped: `PeriodId` removed; `Division` (`AcademicYearDivision`), `StartDate?`, `EndDate?`, `Reason?` added; invariants in `Create` factory (owner XOR, at least one bound, `EndDate >= StartDate`).
  - Migration edited in place: `period_id` + FK + index dropped; `division`, `start_date`, `end_date` added; two raw-SQL unique `COALESCE` expression indexes added in `Up()` and dropped in `Down()`.
  - All model indexes and FKs given explicit database names under 63 bytes (longest is 62 bytes, model indexes 60 bytes).
  - `has-pending-model-changes` verified: *"No changes have been made to the model since the last migration."*
  - `TopicAssignmentPeriodValidator` rewritten to exception-shape validators (FR-56 division matrix + DateRange window containment); retired-period and active-year rules deleted.
  - `SubjectAvailability` rewritten to single date test (`start <= d && (end == null || end >= d)`); `ResolveActivePeriodIdsAsync` + `CacheKeySegment` deleted (RISK-2 dissolved).
  - Three cached readers updated to date test; `IActivePeriodProvider` dependency removed; cache keys reverted to date-only.
  - `DeletePeriodHandler` block guard reverted; `ISubjectEnrollmentExceptionRepository` dependency removed.
  - Q5 read-side removal: `PeriodId` dropped from `ListTopicsByGrade.cs`, `ListTopicsByGradeHandler.cs`, `TopicRoutes.cs`, `StudentsApiClient.cs`, and `Subjects.razor`.
  - Retirement: `SubjectBlocksDialog.razor` (+ `.css`) and `SubjectBlockLabels.cs` deleted.
  - Surfaces rewired: `Subjects.razor`, `Detail.razor`, `GradeTopicsDialog.razor` rewired to count badge + navigating kebab (including group-owned rows per Q1).
  - Tests rewritten/updated: T1–T11, T14, T15; `SubjectBlocksDialogTests.cs` and `DeletePeriodBlockGuardTests.cs` deleted; all retired tests replaced with named replacements. Traps in `GradeLevelDetailPageTests.cs` preserved via intent-preserving assertions.
- **Unanticipated compile sites (reported, not silently absorbed):**
  1. `tests/SchoolCollab.Students.Tests.Unit/RemoveTopicAssignmentHandlerTests.cs:50` — dropped `StubActivePeriodProvider` argument due to P20 ctor change.
  2. `CreateTopicForGradeHandler.cs:163` — cleaned up stale cref to deleted validator method.
  3. `tests/SchoolCollab.Students.Tests.Unit/StubActivePeriodProvider.cs` (T10) — now has 0 consumers; kept per plan until pass 4 sweep.
- **Parent authoritative test suite results:**
  - `Students.Tests.Unit`: **476 / 476** passed (0 failures)
  - `ArchitectureTests.Unit`: **72 / 72** passed (0 failures; `DeprecatedPeriodWritePathArchitectureTests` intact)
  - `Admin.Tests.Unit`: **562 / 562** passed (0 failures)
  - `Students.Tests.Integration`: **68 / 68** passed (0 failures; real Postgres Testcontainers integration tests passing including COALESCE expression index duplicate guard and `/check` endpoint)
  - **Total: 1178 in-scope tests, 0 failures.**

### Parent rulings on the Pass-2 open questions

**T10 — `StubActivePeriodProvider.cs`: DELETE (done).** Verified: every `StubActivePeriodProvider`
reference resolves to a **nested private class** declared inside `EnrollStudentHandlerTests.cs:70`, so the
standalone file had **zero** consumers. **The plan's T10 entry was factually wrong** — it listed
`EnrollStudentHandlerTests` as a surviving consumer of the *file* when it actually consumes its own nested
duplicate; the parent's earlier "T10 is right" confirmation repeated the same name-grep mistake (a name
grep cannot distinguish a nested class from a file). Deleting it is therefore **not** a §(g) violation: §(g)
said "edited, not deleted" on a false premise, and the file is untracked (round-created, never committed).
`IActivePeriodProvider` itself **stays** — it is legitimately used by 9 production files and 7 other test
files; only the orphan stub goes.

**Count label (`"{n} exceptions"`) — accept the inlining for pass 2, unify in pass 3 if the page needs it.**
P33 deleted the shared label type by design (it was period-vocabulary), so the three surfaces inline the
count text. That is acceptable as three short strings. **If pass 3's page also renders a count**, it must
introduce **one** small shared formatter and switch all surfaces to it (declaring the new file as a small
§(g) addition) — the round must not end with four copies of the same pluralisation.

### Diff review — Pass 2 (`openrouter/stealth/space-bunny-alpha`, high; run `83230795`)

**Verdict: REVISE — 2 × P1, 7 × P2.** Both P1s are **plan** defects (the worker followed the contract).
Parent verified both independently before ruling. Everything else the reviewer checked came back clean:
reshape semantics faithful to spec v3; **RISK-2 fully dissolved** (`ResolveActivePeriodIds|CacheKeySegment|
BlockedTopicIds|activePeriodIds` → 0 hits; all three cache keys date-only; `students` tag kept); migration and
identifier naming safe (longest 62, model indexes 60; `COALESCE` count in snapshot/Designer = 0/0; raw
indexes dropped by name in `Down()` before `DropTable`); retirement clean (0 dangling tokens); **Q1 widen
verified** (kebab gated on a *resolvable owner*, not owner type; both `GradeLevelDetailPageTests` traps
replaced intent-preservingly); all scope gates pass; security posture unchanged.

**P1-1 — the grade-detail card and View-all can never show the badge they were built for. (Plan defect: P20.)**
Verified: `ListGradeTopicCurriculumByGradeHandler.cs:58-63` removes excepted topics, while `Detail.razor:403`
/`:813` call it with **no `effectiveDate`** (server defaults to today) and `:736` feeds that filtered
`_curriculum` into `GradeTopicsDialog.TopicsKey`. So on any day inside an exception span the subject
**disappears** from both surfaces — the two AC-9 requires to render the `n exceptions` badge and the
navigating kebab — and `:745`'s `ExceptionCountsByTopicKey` has nothing to attach to. The landing is
unaffected (`ListTopicsByGrade` has no exception filter), so the same subject is visible-with-badge on
`/students/subjects` and invisible on the grade detail; the entry point to manage the exception vanishes
exactly when it matters.

> **PARENT RULING — fix (a): drop the exception filter from the curriculum reader.**
> **Rationale:** spec §5.2 rows 3–4 and **AC-9** (owner-approved) require the card and View-all to carry the
> badge + navigating kebab, which is only possible if the subject is listed. The curriculum read is a
> **management** surface — an administrator must see a subject in order to manage its exceptions. Filtering
> by availability belongs to the **publish/feed** readers that FR-58 governs, not to the management read. So
> the exception filter stays on `ListGradeTopicAssignments` and `ListActivityGroupTopicAssignments` (the
> feed) and **comes off** `ListGradeTopicCurriculumByGrade`; the bridge-effectiveness date filter stays.
> **Owner may override to (b)** — "the card lists only what is offered today" — but that would also require
> deleting the card's badge requirement from §5.2/AC-9.

**P1-2 — M2/AC-3 index evidence is missing: nothing proves the COALESCE index is load-bearing.** Verified:
`CreateSubjectEnrollmentExceptionHandler.cs:67-71` throws the 409 from `repository.ExistsAsync` **before**
`AddAsync` (`:86`), and that pre-check mirrors COALESCE in C# (`SubjectEnrollmentExceptionRepository.cs:66-78`)
— so Postgres is never reached and the expression index is never exercised. **Replacing the expression index
with a plain nullable-column unique index would keep every test green** — precisely what M2 and the v1 P2-1
lesson forbid. (The `is_deleted = false` half *is* genuinely proven: the re-add test would hit a raw 23505.)
**Fix:** add integration evidence that bypasses the handler — insert two identical open-ended rows directly
and assert `PostgresException.SqlState == "23505"` — and mutation-verify it (M2).

**P2s:** P2-3 count label triplicated (already ruled — pass 3 unifies if the page needs it;
`StudentsLandingPageTests` asserts the exact `"2 exceptions"` string, so the formatter must preserve it);
**P2-4** `POST` never validates `topicId` → an unknown id surfaces as an unhandled `DbUpdateException` → 500
(the pass-3 page can reach it); **P2-5** `Division` accepts an undefined enum value (grade-owned stores it
verbatim); **P2-6** the activity-group FK is still the 62-char truncated name (safe, `model == Designer ==
snapshot`) — **accepted as a residual**, since churning that identifier re-enters the 63-byte danger zone for
no functional gain; **P2-7** the expression indexes' deliberately short prefix is undocumented (one comment
line); **P2-8** the `…/enrollment-exceptions` nav target 404s until pass 3 — expected, and it fixes the
**UI-tester dispatch timing: after pass 3, never before**; **P2-9** the `ExistsAsync` comment overstates EF's
null-parameter translation (harmless, one comment line).

**Scheduled:** P1-1 + P1-2 + P2-4 + P2-5 + P2-7 + P2-9 go to a rework pass (Pass 2b); P2-3 is pass 3; P2-6,
P2-8 are accepted.

### Pass 2b — rework (complete; green)

Fixed P1-1, P1-2, P2-4, P2-5, P2-7, P2-9 (11 files). No pass-3 work.

- P1-1: the `exceptedTopicIds` filter and `SubjectAvailability` usage are gone from
  `ListGradeTopicCurriculumByGradeHandler`; the bridge-effectiveness date filter and cache key stay. Both
  **feed** readers keep their exception filters — the split is the ruling.
- P1-2: new integration test `TwoIdenticalOpenEndedRows_InsertedDirectly_AreRejectedByTheExpressionIndex`
  (`:214`) inserts two identical open-ended rows directly via `InTenantAsync`, bypassing the handler's
  COALESCE-mirroring pre-check, and asserts `PostgresException.SqlState == 23505`.
- P2-4 topic existence → 404; P2-5 `Enum.IsDefined` → 422; P2-7 migration comment on the short index prefix;
  P2-9 `ExistsAsync` comment corrected.
- Curriculum-reader tests now assert the excepted topic **is still listed** while both feed readers assert it
  is excluded.

**P1-2 mutation-verified by the worker (M2).** With the grade expression index temporarily replaced by a
plain nullable-column unique index, the new direct-insert test **FAILED** (no exception thrown — both rows
inserted) while the endpoint/handler test **still passed** — proving only a direct-insert test can observe
the index. Restored; class green 9/9.

**Parent gate after 2b:** `has-pending-model-changes` → "No changes have been made to the model since the
last migration."; `Students.Tests.Unit` 482/482, `ArchitectureTests.Unit` 72/72,
`Students.Tests.Integration` 69/69, `Admin.Tests.Unit` 562/562 — **1185 tests, 0 failures.** Parent also
confirmed P1-1 landed and that both feed readers still filter.

#### METHODOLOGY FINDING — `git grep` skips untracked files (found by the parent)

Every residue sweep in this round — the worker's, the reviewer's, and the parent's — used `git grep`, which
searches only *tracked* files. Every file this round renamed or created is **untracked**, so those "0 hits"
results were partly vacuous. Re-running with **plain `grep -rn`** (excluding `bin`/`obj`) immediately found a
live **AC-10 violation**: `SubjectAvailability.cs:20` named the two deleted helpers in its header comment.

**Fixed (parent, comment-only):** the comment now says v1's "active period id set" mechanism and its
resolution helpers/cache-key segment are gone, and cites spec v3 §0 decision 9 — explanation kept, dead
symbols dropped, AC-10 stays a clean binary grep. Re-verified: the plain-grep sweep over `src`+`tests` now
returns **zero** hits for the whole retired-token set.

**Lesson for pass 4 and future rounds:** residue sweeps must use **plain `grep -rn`** (or
`git grep --untracked`), never bare `git grep`, while the work under review is uncommitted.

### Diff review — Pass 2b re-verify (`space-bunny-alpha`, high; run `9aae8077`)

**Verdict: APPROVE — Pass 2 CLOSED.** All six scheduled items plus the parent's comment fix verified closed.
The reviewer **independently ran the full suite** and reproduced the parent's numbers exactly (1185, 0
failures), re-ran the retired-token sweep under **plain grep** (zero) and widened it (`subject_block`,
`PeriodBlock`, `IsBlockedPeriod`, `blockLabel`, `activePeriodIds` → 0). It confirmed P1-2's discrimination
structurally as well: the table's only model indexes are **non-unique** and `COALESCE` appears **0** times in
snapshot and Designer, so the raw-SQL expression index is the sole uniqueness source besides the PK.

**2 cosmetic P2s — folded into Pass 3 as one-line comment edits:**
1. "three cached availability readers" is now stale in `SubjectAvailability.cs:18` and
   `ListGradeTopicAssignmentsCacheKeyTests.cs:15` — there are **two** (the FR-58 feed readers). Plain-grep
   visible: exactly the overstatement the old `git grep` sweeps could not have found.
2. `EnrollmentExceptionAvailabilityEndpointTests.cs:219` says "…above" for a test located **below** (`:255`).

**Naming list for pass 4 (reviewer-raised):** `Detail.razor`'s `_blocks` / `ReloadBlocksAsync` still use the
retired "blocks" vocabulary — outside AC-10's token set and outside 2b's scope, so it goes on Pass 4's list.
**P2-3** (count-label triplication) stays a Pass-3 item.

### Pass 3 — THE PAGE (complete; green)

- **Created:** `EnrollmentExceptions.razor` + `.razor.css` (P39/P40, route `/students/enrollment-exceptions`),
  `Students/EnrollmentExceptionLabels.cs` (**declared §(g) addition**, P2-3), and
  `tests/.../EnrollmentExceptionsPageTests.cs` (T13, **15 tests**, AC-17).
- **Delivered:** owner toggle (grade or group, group side flag-gated); list (subject · part `Term`/`Semester`/`—`
  · human span · optional reason · delete); add panel (subject → part per **OOD-1** → term/semester picker
  resolving to that part's dates, or free `From`/`To` each independently clearable = open); empty list as plain
  prose (no warning); **immediate write, no form, no Save**; 409/422 surfaced verbatim; **N4** pre-selection
  from `?gradeLevelId=`/`?activityGroupId=`/`?topicId=`.
- **Also:** the `/check` client wrapper (`IsSubjectExceptedAsync`, either owner form); P2-3 unified —
  `EnrollmentExceptionLabels.FormatCount` is consumed by **all four** surfaces (parent-verified), so the
  triplication is gone and `"2 exceptions"` is preserved by construction; the two stale-comment corrections.
- **Parent authoritative gate:** `Admin.Tests.Unit` **577/577**, `Students.Tests.Unit` **482/482**,
  `Students.Tests.Integration` **69/69**, `ArchitectureTests.Unit` **72/72** — **1200 tests, 0 failures.**
- **Unanticipated (reported, not absorbed):** bUnit cannot supply `[SupplyParameterFromQuery]` to a
  directly-rendered component, so the N4 tests render through a real `Router`
  (`AppAssembly = Students.Application`); `GradeLevelDetailPageTests.cs:1037` was **forced** by the P2-3
  unification (same intent, now pinning `FormatCount`); the add panel carries the optional **Reason** field
  (§5.1's List and §8 Q4 require it, and this page is the only place exceptions are edited);
  `OnParametersSetAsync` re-applies pre-selection on a query change behind a first-load flag; with no owner in
  the query the page starts on the first grade level (mirrors the Topics landing).
- **Spec ambiguity (accepted):** §5.1's "term/semester choices … filtered by the owner's rule" is implemented
  as deriving the picker's contents from the **OOD-1 part** (Terms → the tenant's Terms periods); a part a
  `Termly`/`Semester` group forbids is rejected **server-side (422)** with the message shown, rather than
  hiding parts client-side — narrowing further would have been an unapproved decision.
- **Residuals:** layout (three pickers + span row wrapping) needs the **UI tester**; the subject picker is fed
  by date-effective listings, so a not-currently-effective subject renders the neutral "A subject no longer
  listed here" label (still removable) — not covered by the spec; the pre-selected topic lands in the **add
  panel** while the list shows the whole owner ("list only that subject" would be a one-line follow-up); the
  `/check` wrapper has no client-level route test (exercised end-to-end through the page hint test).

#### Diff review — Pass 3 (`space-bunny-alpha`, high; run `bbee3c9e`)

**Verdict: APPROVE — 0 P0, 0 P1, 6 P2.** The reviewer independently ruled AC-17's seven clauses
**non-vacuous** (the `FluentSelect` absence check is a genuine absence; the Add guard is doubly protected so
the no-POST assertion would fail if it broke; the "no Save" check is correctly scoped to the page subtree),
proved the test `Router` is **necessary** rather than cosmetic (without query supply both N4 discriminators
fail), confirmed P2-3 by plain grep, and found no constraint violation (CSS isolation only, `ScriptedHandler`
— not Moq — for HTTP, width ladder honoured, no `TopicId`-keyed collection). Scope and security posture clean.

**Dispositions:**
- **P2-1 — the reviewer's diagnosis proved WRONG; the worker found the real defect instead.** The reviewed
  scenario (an in-page query change loses the pre-selection via `DropdownComponent`'s reset path) is
  **unreachable**: `ComponentBase` renders only *after* `OnParametersSetAsync` completes, and
  `LoadOwnerDataAsync` clears and refills `_topics` inside that single awaited pass, so no intermediate render
  exists on that path. The worker **mutation-verified** this — the instructed test **passes with the pre-fix
  code**, so it is not a discriminator. It then found the genuinely reachable defect in the same area: a query
  topic the owner does **not** list left the subject **armed but invisible**, so `CanAdd` was true and the Add
  button was **enabled** behind a subject the user never saw — a complete-looking panel that would POST it.
  `QueryString_TopicTheOwnerDoesNotList_LeavesNothingArmed` **fails pre-fix / passes post-fix**. Parent ruled
  **A**: keep the fix (pending field applied at the end of `LoadOwnerDataAsync`), accept that test as P2-1's
  real discriminator, and keep the in-page test as an honestly-labelled **regression guard** pinning the
  no-intermediate-render invariant. **Reviewed wording corrected here by the parent.**
- **P2-2 (§5.1 deviation)** — the part picker is derived from OOD-1 alone, so a `WholeAcademicYear`/`OpenEnded`
  group is still offered Term/Semester, costing a 422 round-trip, though §5.1 says the choices are *"filtered by
  the owner's rule (§4.2)"*. **→ Pass 4 fix, as spec conformance:** offer exactly the divisions FR-56 permits for
  that owner (grade → all, per FR-57; group → its span's required division), with "not term-scoped" present only
  when `None` is permitted. *Owner may override to "amend §5.1 and keep the server 422" — this is the one
  judgement call in the batch.*
- **P2-3** — 409/422 render as `409 Conflict: {"message":"…"}`; the server's words are present but buried.
  **→ Pass 4 fix:** parse `message`/`Message`/`detail` out of the body.
- **P2-4 (accepted, app-wide convention — not a pass-3 regression)** — the subject picker offers only subjects
  effective **today** (both `ListTopicsBy*` default a null date to today, mirroring the Topics landing), so a
  forward-dated exception cannot be created for a bridge row whose window starts in the future. Flagged to the
  owner; no change in this round.
- **P2-5** — §(g) under-declares the two legitimate pass-3 additions (`EnrollmentExceptionLabels.cs`, the
  `IsSubjectExceptedAsync` wrapper). **→ Parent adds the inventory rows to this doc.**
- **P2-6 + the NULLS comment** — a CSS comment maps to a rule that does not exist, and
  `SubjectEnrollmentExceptionRepository.cs:34` claims "Postgres orders NULLs first in ASC" (**wrong** — `ASC`
  orders NULLs **last**, so an open-start exception sorts to the bottom). **→ Pass 4 fix** (and if the code
  intended open-starts first, add an explicit `NULLS FIRST`, not just the comment).

**Naming list added by this review:** `Detail.razor`'s `_blocks` / `ReloadBlocksAsync` (retired "blocks"
vocabulary) — Pass 4.

### Pass 4 — fixes + sweep + freeze (complete; green)

**Part A — the four Pass-3 review fixes**, all mutation-verified (**4 failed pre-fix / 19 green post-fix** in
`EnrollmentExceptionsPageTests`):
1. **P2-1** — the query's subject is parked in `_pendingTopicId` and applied at every exit of
   `LoadOwnerDataAsync` **only if the owner's list contains it**. Discriminator:
   `QueryString_TopicTheOwnerDoesNotList_LeavesNothingArmed` (fails pre-fix). The in-page test is kept as a
   labelled **regression guard**. See the corrected P2-1 wording above.
2. **P2-2** — the part picker is owner-aware (`BuildDivisionOptions()` + `RequiredDivision(group.Span)`,
   mirroring `TopicGroupExceptionDivisionAsync`'s FR-56 mapping): group-owned → exactly one part (`Termly`→Term,
   `Semester`→Semester, `WholeAcademicYear`/`OpenEnded`/`DateRange`→"Not term-scoped"); grade-owned → OOD-1's
   tenant parts + "not term-scoped" (FR-57). Two tests, both failing pre-fix.
3. **P2-3** — `CreateSubjectEnrollmentExceptionAsync` reads `message`/`Message`/`detail` from the failure body
   (`ServerMessage`, following the `AssignmentsApiClient` precedent) and throws the server's sentence; the 409
   test gained two `NotContain` assertions so it discriminates.
4. **P2-6 + the wrong comment** — the `.razor.css` map now lists only rules that exist;
   `SubjectEnrollmentExceptionRepository`'s comment corrected (**ASC orders NULLs LAST**) **and** the ordering
   now expresses the intent it always claimed.

**⚠️ DISCLOSED BEHAVIOUR CHANGE (flagged for the re-verify to rule on):** item 4 is **not** the comment-only fix
the reviewer scoped. The exception list is now **null-first** (open-start rows lead) — which the old code
*claimed* in a comment but never did. Npgsql exposes no per-query `NULLS FIRST` (only the context-wide
`ReverseNullOrdering`), so it is expressed as an explicit sort key:
`.OrderBy(b => b.StartDate == null ? 0 : 1).ThenBy(b => b.StartDate).ThenBy(b => b.CreatedAt)`. **No test pinned
the old order.**

**Part B — retirement sweep (plain `grep -rn`, `src`+`tests`, bin/obj excluded):** **0 hits** for all ten
patterns (`SubjectEnrollmentBlock`, `subject-blocks`, `subject_enrollment_blocks`, `SubjectBlocksDialog`,
`SubjectBlockLabels`, `IsRetiredPeriod`, `DeletePeriodBlockGuard`, `BlockedTopicIds`, `ResolveActivePeriodIds`,
`CacheKeySegment`). Widened set also 0 (`subject_block`, `PeriodBlock`, `IsBlockedPeriod`, `blockLabel`,
`activePeriodIds`). Coverage proven — the sweep sees untracked files: `SubjectEnrollmentException` → 43 files,
including every new one. Two non-sweep-set hits **reported, not hidden**: `_blocks` ×16 in Python `.venv`
site-packages under `src/` (unrelated), and `BlocksDialog` ×1 at `SubjectsLandingPageTests.cs:568` (a
deliberate "Replaces v1's …" note, required by §(j)'s replace-never-delete-silently rule). **Naming done:**
`Detail.razor` `_blocks`→`_exceptions`, `ReloadBlocksAsync`→`ReloadExceptionsAsync`. No
`*SubjectBlock*`/`*BlockSubject*`/`*TopicPeriodsEdit*` file remains; `DeletePeriodHandler` takes
`IPeriodRepository, HybridCache, ILogger` only.

**Part C — parent-authoritative gate:** build **0 errors** · `has-pending-model-changes` → *"No changes have
been made to the model since the last migration."* · `Admin.Tests.Unit` **581/581** · `Students.Tests.Unit`
**482/482** · `Students.Tests.Integration` **69/69** · `ArchitectureTests.Unit` **72/72** — **1204 tests,
0 failures.** **Patch frozen:** `documents/rounds/diffs-enrollment-exceptions.patch`, **11176 lines**
(550,067 bytes); nothing staged, no `A` entries, nothing committed.

**Pass-1 review F2 — CLOSED by the parent:** the stale v1 patch is now annotated by
`documents/rounds/diffs-subject-period-blocks.STALE.md`; re-freezing is impossible (v1's tree no longer
exists), so the note names what the patch omits and how to reconstruct a true baseline instead.

### Final re-verify — Pass 4 (`space-bunny-alpha`, high; run `5d703f21`)

**Verdict: APPROVE — 0 P0, 0 P1, 2 P2 residuals. Safe to accept: YES.** (A first attempt, run `841c529c`,
died on a provider connection error before producing findings; retried on the same model per protocol.)

- **The reviewer WITHDREW its own Pass-3 P2-1**, confirming the worker's mutation evidence from the code:
  `ComponentBase.SetParametersAsync` awaits `OnParametersSetAsync` and only then calls `StateHasChanged()`, so
  `DropdownComponent.OnParametersSet` can never observe the emptied `_topics` — the diagnosed reset-path
  scenario is unreachable. The **reachable** defect (armed-but-invisible subject enabling Add behind a subject
  the user never saw) is real, and `QueryString_TopicTheOwnerDoesNotList_LeavesNothingArmed` is a genuine
  discriminator. The corrected framing stands.
- **NULLS behaviour change — ACCEPTED and kept.** A null start *is* the widest span, so the delta makes the
  code match the repository comment's stated intent ("the UI lists the widest span first"); the ordering is
  deterministic and preserves the old tie-break. The reviewer confirmed the provider claim (Npgsql has no
  per-query `NULLS FIRST`) and **proved runtime translation rather than assuming it** — `/check` is served by
  the same `ListDtosAsync`, so the ternary is exercised on real Postgres in the 69/69 integration suite. No
  assertion changed, no caller broke.
- **Sweep confirmed zero independently:** all ten patterns plus five widened ones → 0 hits under plain grep.
  The three surviving "block" tokens are documentation required by §(j)'s replace-never-delete-silently rule.
  The `Detail.razor` rename verified complete (`_exceptions`, `ReloadExceptionsAsync`).
- **Frozen patch confirmed sound:** recomputed fresh and **byte-identical** (`cmp`) — 11176 lines /
  550,067 bytes; 26 `new file mode`, 2 `deleted file mode`; nothing staged, no `A` entries, HEAD still `3e408bfa`.
- **P2-2** verified as an exact mirror of the server's FR-56 switch (the `ActivityGroupDto.Span` string bridge is
  sound); **P2-3** verified contained (exactly one caller).

**2 residual P2s (accepted, non-blocking):**
1. **DateRange containment is still server-only** — `BuildDivisionOptions()` filters the *part* per FR-56, but a
   `DateRange` group's span-inside-window rule is enforced only server-side, costing a 422 round-trip. The data
   is already client-side so it *could* be pre-empted, but that is a product decision beyond the approved fix.
   The server remains the gate and P2-3 makes the failure legible.
2. **The null-first order is unasserted** — no test pins exception order (0 index-based assertions; the other
   consumers only `GroupBy`/`Count`). A one-line integration assertion would close it; not required.

**Cosmetic:** `EnrollmentExceptionsPageTests.cs:158`'s doc comment says "terms **and** semesters" while the
fixture seeds only a Terms period (Pass-3 vintage).

## Review

*(plan review step 2b, then per-pass diff reviews by `openrouter/stealth/space-bunny-alpha`)*

### Parent pre-review verification (2026-09-26)

Before dispatching step 2b, the parent spot-checked the plan's factual claims by grep/read.
**All verified accurate** — recorded because the previous round's enumerations came up short
*three times*, so these claims were not taken on trust:

- **Cited-path audit.** 26 distinct backticked `src/`/`tests/` paths; 11 are not on disk and
  **all 11 are expected** — 10 are pass-1 renames (`SubjectEnrollmentException.cs`,
  `EnrollmentExceptionRoutes.cs`, `SubjectEnrollmentExceptionDto.cs`, the repository pair, the
  configuration, `DuplicateSubjectEnrollmentException.cs`, `CreateSubjectEnrollmentException.cs`,
  `Domain/SubjectEnrollmentExceptionTests.cs`, `EnrollmentExceptionAvailabilityEndpointTests.cs`)
  and 1 is the pass-3 NEW file `EnrollmentExceptionsPageTests.cs`. **No stale path exists.**
- **Q5 is one chain.** `ListTopicsByGradeHandler.cs:30-36` holds the `PeriodId` predicate, exactly
  as §(e) cites. `ListSubjectsByGradeHandler` **does not exist as a class** — only
  `ListSubjectsByGradeHandlerTests.cs` / `…ErrorTests.cs`
  (`tests/SchoolCollab.Students.Tests.Unit/`), legacy-named test files covering that handler
  chain. So T7/T8 are **edits to existing files**, and the plan is consistent.
- **P22 is real.** `DeletePeriod/DeletePeriodHandler.cs:22` takes `ISubjectEnrollmentBlockRepository`.
- **T10 is right.** `StubActivePeriodProvider`'s surviving consumers are
  `EnrollStudentHandlerTests` + `RemoveTopicAssignmentHandlerTests`; the other four lose their
  consumers with RISK-2.

**Parent corrections applied to this doc** (found on read, before review — not review findings):
§(h) OOD-1 was wrongly recorded **open** — the owner answered spec §8 Q11 "recommended as-is"
*before* the plan was written, so it is **settled** and pass 3 is unblocked. Also fixed: the
`Students.Tests.Unit` table heading count (`(11)` → `(10)`; T11 is the Integration file) and a
`Pass 4` typo (`KEBAVS` → `KEBABS`).

### Plan review — step 2b (`ollama/glm-5.3:cloud`, run `095c3c73`)

**Verdict: REVISE — 4 × P1, 3 × P2.** The reviewer recomputed the file list by grep, confirmed
all 58 listed entries are sound (arithmetic 40 P + 18 T), then found **7 further mandatory files
(→ 65)** plus three structural defects. **The parent independently re-verified every P1 below.**

| # | Sev | Finding | Parent verdict |
|---|---|---|---|
| F1 | P1 | 7 comment-token files missing from §(g): `TopicAssignmentRoutes.cs:92` (`subject-blocks`), `AssignGradeTopic.cs:14`, `AssignActivityGroupTopic.cs:13`, `CreateTopicForGrade.cs:21`, `UpdateTopicAssignmentPeriodHandler.cs:20` (unresolved cref → CS1574), `TopicAssignmentPeriodTests.cs:22-23`, `TopicEditDialog.razor.css:31`. AC-4/AC-10 are whole-tree grep gates, so each is mandatory. | **VERIFIED** — all 7 carry real tokens and appear in this doc **only in the `git status` porcelain record (lines 9–44), never as §(g) work items**. Exactly the previous round's P1-4 shape. |
| F2 | P1 | Pass 2 cannot end buildable. The DTO reshape (`PeriodId`/`PeriodName`/`PeriodStatus` gone) breaks `SubjectBlocksDialog.razor` (`:54/:55/:182/:242/:226/:266`) and `SubjectBlockLabels.DescribePeriods`, transitively breaking `Detail.razor:300`, `Subjects.razor:440`, `GradeTopicsDialog.razor:147` — all pass-4-bound. Admin.Tests.Unit also goes runtime-red at pass 2. Pass 3's "both UIs coexist — deliberate and buildable" is **false**: the v1 dialog's *suppliers* die in pass 2, not its consumers in pass 4. | **VERIFIED** — the dependency direction is inverted. |
| F3 | P1 | Contradicts spec §5.2 row 1 ("Owner gate widened to include activity groups **(v2)**"). P34 carries the **v1** A.1 grade-only ruling forward; T14 pins `NonGradeOwner_RowOmits…` (`SubjectsLandingPageTests.cs:577`). | **VERIFIED** — `Subjects.razor:48/:65/:160-178/:344` is a full ActivityGroup owner mode, `:559` **already resolves `ActivityGroupId` per row**, and `:368` records the v1 grade-only rationale that v3 voids. A silent narrowing of the authority. **Routed to the owner.** |
| F4 | P1 | Spec §5.2 row 2 (Activity groups landing / details entry points) is absent — **0** mentions in the plan. | **VERIFIED**, and the reviewer's caveat holds: **neither page renders a subject/topic list** (every `topic\|subject\|exception` hit is a `catch (Exception ex)`). Row 2 as written presupposes a surface that does not exist. **Routed to the owner.** |
| F5 | P2 | AC-12's handler-test clause is a guard, not a discriminator — the filter applies only when the query supplies a `PeriodId`, and post-round T7's test constructs none, so it passes pre-round. | Sound. Relabel T7 as a guard; make the discriminator API-level. |
| F6 | P2 | Coverage gaps: (a) §5.1/§9 page behaviours have T13 but no AC; (b) **§6's `GET …/check` endpoint has no test and no AC**; (c) §9's rollover guard has no AC. | Sound — (b) is a genuine spec-vs-test gap. |
| F7 | P2 | Pass-1 brief cites a phantom `/check` route; v1 `SubjectBlockRoutes.cs` has only GET list / POST / DELETE — `/check` is **new in v3** (P27). | Sound. |

**Reviewer-confirmed sound (no work):** the dissolution list (all five constructs are planned for
**deletion, not porting**); the migration (raw-SQL indexes dropped before `DropTable`, invisible to
`MigrationGuardTests.NoUncommittedModelChanges`, valid `'-infinity'`/`'infinity'` sentinels, Step-0
preserved); the availability predicate + cache-key safety (key retains owner + date; the 5 other
`IActivePeriodProvider` injection sites correctly stay); AC-9's two trap replacements are
intent-preserving; every cited v1 test name exists verbatim; AC-1/2/3/5/6/7/8/9/10/14 are genuine
discriminators, AC-11/13/15/16 honestly labelled guards.

**State: blocked** on owner decisions (F3, F4) and a parent phasing call (F2) before revision.
The reviewer pre-authorised re-reading **only the revised sections** (≤1 iteration).

### Revision applied (2026-09-26, post-review — F1–F7 all applied)

Owner decisions recorded as settled in §(h): **Q1 = widen**, **Q2 = defer spec §5.2 row 2 with
sign-off**, **Q3 = merge → 4 passes**; no open decision remains. Sections to re-read:

- **§(g)** — P24–P26 widened to name the command halves; new entries P41–P46 + T19 (the 7
  missing files); table counts and total restated (58 → **65**).
- **§(h)** — Q1/Q2/Q3 settled owner decisions (2026-09-26) recorded; "no open decision remains".
- **Phasing** — 5 passes → **4 passes**; pass 2 = reshape + retire (atomic, per F2, with the
  dependency direction stated); "both UIs may briefly coexist" claim deleted; each pass's green
  gate restated.

### Plan review — re-read pass (`ollama/glm-5.3:cloud`, run `c6f50ee1`)

**Verdict: REVISE — one new P1 (N1); all seven prior findings CLOSED.** As this was the single
permitted re-read, the reviewer **pre-authorized the parent to apply exactly the N1 fix and start
the round without a further plan-review pass**. The parent verified N1 independently, applied it,
and also applied the three non-blocking P2s.

- **F1–F7 — all CLOSED** (reviewer-confirmed with per-finding evidence): F1's seven files are
  numbered work items verified on disk at the cited lines; F2's 4-pass phasing folds the
  retirement + the three surface rewires into pass 2 with the dependency direction corrected and
  the coexistence claim deleted; F3 widens to grade- **and** group-owned rows with T14's
  `NonGradeOwner_RowOmits…` replaced; F4 is a declared deviation with sign-off and zero P entries;
  F5 relabels T7 a guard and moves the discriminator to T11/AC-12(i); F6's AC-17/18/19 close the
  three gaps; F7 removed the phantom `/check`.
- **N1 (P1) — §(g) missing two compile-mandatory Q5 files → true count 67.** `ListTopicsByGrade.cs`
  (record `Guid? PeriodId = null`) and `ListTopicsByGradeHandler.cs:30-36` (the predicate) were
  named only in prose, never numbered, and are **compile-chained to P29** (`TopicRoutes.cs:105`
  constructs `new ListTopicsByGrade(gradeLevelId, effectiveDate, periodId)`). **Parent verified:**
  record params at `:10-13`, predicate at `:36`, construction at `:105` — real. **Fixed: P47/P48
  added, Core (30) → (32), total 65 → 67.**
- **N2–N4 (P2) — applied by the parent anyway** (doc-only, non-blocking): N2a names the three
  Admin test files pinning `/students/subject-blocks` literals in the Pass-1 brief; N2b states the
  comment-rewrite target where `SubjectBlocksDialog` has no pass-1 successor; N3 corrects §(e)'s
  "three handler files" → four (P44 is the fourth) and replaces pass 3's dead "Gated on OOD-1 …
  if unanswered" text with the settled OOD-1 rule; N4 adds the page's query-param
  **pre-selection** requirement to P39/T13.
- **The reviewer's open question, resolved by the parent:** spec §6's `/check` specified only its
  `gradeLevelId` form. Since Q1 widened the gate to group-owned rows, **§6 now specifies both
  forms** (`?gradeLevelId=` **or** `?activityGroupId=`), and **AC-18/T11 require both**.

**Plan state: FROZEN and approved to start.** No open finding, no open decision.

### Diff review — Pass 1 (`openrouter/stealth/space-bunny-alpha`, thinking high; run `348c789d`)

**Verdict: APPROVE — 0 P0, 0 P1, 2 P2.** (The first attempt, run `17124502`, died on a provider
connection error before reading anything; retried on the same model per protocol.)

**Method note from the reviewer — keep this.** The v1-frozen patch (`diffs-subject-period-blocks.patch`) is
a **stale** baseline: the v1 round doc was edited *after* the patch froze, so the patch omits the AC-5
architecture guard and the two late-v1 polish fixes. The reviewer reconstructed the true v1 tree from
`3e408bf` + patch and adjudicated every delta against the v1 round doc instead of trusting the patch.
→ **Pass-4 action: re-freeze that patch (or annotate it), or the next reviewer who trusts it as a baseline
will produce false findings.**

**Rulings — all favourable:**
- **Rename is behaviour-preserving — YES, by proof not assurance.** Diffing the reconstructed v1 tree against
the current tree: **every** added/removed line in `src` (643) and `tests` (401) contains a rename token —
`grep -E "^[<>]" | grep -vE "<rename tokens>"` returns **empty** for both. Spot-audits of the load-bearing
surfaces confirm no predicate, validator rule, cache key, route parameter or assertion changed.
- **Token sweep complete — YES.** `git grep -E "SubjectEnrollmentBlock|subject-blocks|subject_enrollment_blocks"`
→ **exit 1, zero matches**. All 7 files the earlier plan drafts omitted are confirmed done; the only
surviving `subject-blocks` hits are CSS class/id strings inside the pass-2-deleted dialog (+ gitignored
`obj/`).
- **Parent repair correct and complete — YES on all five sub-questions**, including: exactly two places
changed; byte-identical across migration `:35` = Designer `:2089` = snapshot `:2086`; behaviour-preserving;
`_DiagPendingDiff` leaves no residue (only the narrative mention here); and **no unrelated drift** — the
snapshot diff is **112 added, 0 removed**, every line inside the `SubjectEnrollmentException` entity block.
- **Scope gates — all PASS.** `src/Assignments/**` diff empty; no `Program.cs`; no `.csproj`/CPM change;
`DeprecatedPeriodWritePathArchitectureTests.cs` correctly untouched (it carries **zero** rename tokens, so
the pass-1 rename leaving it alone is right — AC-11 intact). `securityPosture` unchanged: the renamed route
group extends the existing `/students` group and inherits its OIDC `RequireAuthorization`.
- The reviewer correctly classified the two non-rename UI deltas (`RetiredMarker`, `Subjects.razor:83`
title) as **late-v1, not pass-1**, proven by the *unchanged* tests that assert them.

**F1 — P2 (documentation).** The truncated FK name is **62** chars, not 63 — measured: source name **66**,
declared name **62**. No code impact (consistent across all three files; `has-pending-model-changes` clean).
**Corrected in this doc.**
**F2 — P2 (artifact).** The stale v1 patch, above — recorded as a pass-4 action.

**Forward risk, converted into a Pass-2 requirement (would have been a P1):** the planned model indexes on
`tenant+owner+topic+division` would be **auto-named by EF** as
`ix_subject_enrollment_exceptions_tenant_id_grade_level_id_topic_id_division_id` = **78 chars** — over the
63-byte limit, reproducing the exact pass-1 desync. The Pass-2 brief therefore **mandates an explicit
`HasDatabaseName` under 63 chars** for every index/FK/constraint added or renamed, plus a post-edit
`has-pending-model-changes` check that must print "No changes have been made to the model since the last
migration." The two planned raw-SQL index names are already safe at **48** chars.

**Pass 1 ACCEPTED.**
- **Worker task spec — Pass-1 brief** — role line (1 of 4), step 1 (dialog deletion now pass 2),
  step 2 (F7 reword: no phantom `/check`; the GET list's `periodId` parameter stays this pass),
  step 5 (extended with the 7 new comment-token files), report line (pass-4-bound →
  pass-2-bound).
- **§(i)** — AC-9 (grade- **and** group-owned rows), AC-12 reworded (T7 = regression guard; the
  API-level `?periodId=` discriminator added), new **AC-17** (page behaviours, F6a), **AC-18**
  (`/check`, F6b), **AC-19** (rollover guard, F6c).
- **§(g) rows touched for F5/F6:** T3 (→ AC-19), T7 (guard relabel), T11 (Q5 API-level
  discriminator + AC-18 `/check` test), T13 (→ AC-17), T14 (Q1: `NonGradeOwner_RowOmits…`
  replaced).
- **§(j)** — M6 added (→ AC-18).

## Acceptance

*(orchestrator acceptance, 2026-09-26 — Tier 3 full. Every AC below is judged against spec
v3 (`documents/specs/subject-period-exception-model.md`) as the authority; pre-round state =
v1 as shipped. Guard-labelled criteria are kept guards — none is upgraded to a discriminator.
Numbers marked *(parent)* were measured by the parent's authoritative run, not re-measured here.)*

### Criteria — AC-1 … AC-19

| # | Verdict | Evidence | Mutation |
|---|---|---|---|
| AC-1 | **Satisfied.** | T11 integration at the real-Postgres feed point: an exception (`Division = None`, free span) containing `effectiveDate` excludes the topic from `GET /students/topic-assignments/by-grade/{id}?effectiveDate=…`; a non-containing span leaves it present. Fails pre-round by construction (a span-shaped POST is impossible). | M1 planned; a mutation run is **not separately recorded** — the discriminator is the pre-round-failing integration test. |
| AC-2 | **Satisfied.** | T1 entity invariants (owner XOR, both-null → 422, `End >= Start`) + T2 handler shape tests (no bound → 422; End<Start → 422; owner XOR → 422) + T3's open-start/open-end matching. | — |
| AC-3 | **Satisfied.** | Handler half: duplicate span → 409 incl. the soft-deleted-row guard; remove-then-re-add ⇒ 201. **Index half:** `TwoIdenticalOpenEndedRows_InsertedDirectly_AreRejectedByTheExpressionIndex` (Pass 2b) bypasses the handler's COALESCE-mirroring pre-check and asserts `PostgresException.SqlState == "23505"` on real Postgres. | **M2 exercised** (Pass 2b): with the expression index swapped for a plain nullable-column unique index, the direct-insert test **failed** (no exception thrown) while the endpoint test still passed — proving only a direct-insert test can observe the index. |
| AC-4 | **Satisfied.** | `/students/enrollment-exceptions` routes live; old routes/table gone; plain-grep sweep (Pass 4 Part B, re-run independently by the final re-verify, re-run again at acceptance) → **0 hits** for `SubjectEnrollmentBlock`, `subject-blocks`, `subject_enrollment_blocks` across `src`+`tests` (bin/obj excluded). | — |
| AC-5 | **Satisfied — mechanism substituted, per the owner.** | See the mechanism note below. `ResolveActivePeriodIdsAsync`/`CacheKeySegment` → 0 hits; the three feed readers no longer inject `IActivePeriodProvider`; cache keys date-only (Pass-2 review verified all three keys and the kept `students` tag). | M1 family; see AC-1. |
| AC-6 | **Satisfied.** | T2's FR-56 **division** matrix (Termly→`Terms`, Semester→`Semesters`, WholeAcademicYear/OpenEnded→`None`, DateRange→`None` + containment incl. null-bound cases); the bottom half fails pre-round by construction (v1 422'd OpenEnded/DateRange outright). Pass-4 P2-2 mirrored the same FR-56 switch in the page's part picker (both tests failing pre-fix). | — |
| AC-7 | **Satisfied.** | T2's any-Division/any-span acceptance test — the named replacement for the retired `Block_GradeTermOutsideActiveAcademicYear_Throws422`; the v1 payload that 422'd pre-round is accepted post-round. | M4 planned; a mutation run is not separately recorded. |
| AC-8 | **Satisfied.** | The v1 `ByGradeFeed_…` integration test re-expressed against a span, passing at the feed point; `src/Assignments/**` diff verified **empty** by every pass reviewer (the lookup client reduces by `TopicId` only). | — |
| AC-9 | **Satisfied.** | Badge + navigating kebab on all four surfaces (T14 landing incl. group-owned rows per Q1; T15 card incl. both trap replacements); Pass-2 review verified Q1 widen (kebab gated on a *resolvable owner*, not owner type) and both trap replacements intent-preserving; P1-1 fixed in 2b (curriculum reader stopped filtering, so the subject is listed on the card exactly when its exceptions exist); `"Offered in every period"` and `SubjectBlockLabels` gone (0 hits). | M3 planned; a mutation run is not separately recorded. |
| AC-10 | **Satisfied.** | `SubjectBlocksDialog.razor`(+css), `SubjectBlockLabels.cs`, `SubjectBlocksDialogTests.cs`, `DeletePeriodBlockGuardTests.cs` deleted; plain-grep sweep → 0 hits for `SubjectBlocksDialog|SubjectBlockLabels|IsRetiredPeriod|ResolveActivePeriodIds|DeletePeriodBlockGuard` (and the widened set `subject_block|PeriodBlock|IsBlockedPeriod|blockLabel|activePeriodIds` → 0). The three surviving "block" tokens are documentation required by §(j)'s replace-never-delete-silently rule. The deletion half is a real discriminator. | — |
| AC-11 | **Satisfied — guard, stated as such.** | `DeprecatedPeriodWritePathArchitectureTests.cs` untouched; passes inside `ArchitectureTests.Unit` **72/72** *(parent)*; deprecated `PUT /topic-assignments/{id}/period` and `PeriodId` on `AssignGradeTopicRequest` remain accepted-and-ignored no-ops. Gate on the deprecate-don't-delete posture — never weakened to pass anything else. | — |
| AC-12 | **Satisfied.** | `?periodId=` removed from the by-grade route, `ListTopicsByGrade`, the client, and the landing (Q5 — owner decision, REMOVE). API-level discriminator in T11: pre-round a non-matching `?periodId=` exact-match-filtered to an empty list; post-round it is ignored. T7's handler test pins the ignored non-null bridge `PeriodId`. | M5 planned; a mutation run is not separately recorded. T7's half remains an honestly-labelled **regression guard** (F5), not a discriminator. |
| AC-13 | **Satisfied — guard, honestly labelled.** | T4's invalidation tests retained and re-pointed at exception writes (create/remove ⇒ the `students` tag invalidates and the readers recompute). Carried-forward invariant; the rewrite did not lose it. | — |
| AC-14 | **Satisfied.** | `DeletePeriodHandler` reverted to `IPeriodRepository, HybridCache, ILogger` only (final re-verify confirmed); `DeletePeriodBlockGuardTests` deleted; T6's construction reverted with FR-D2/D6 tests untouched and passing within `Students.Tests.Unit` 482/482 *(parent)*. Behavioural where seedable, structural otherwise — as stated. | — |
| AC-15 | **Satisfied — guard, stated as such.** | Strands/lessons untouched; reviewers verified scope gates on every pass; no `<StrandsEditor>` tokens appear in the frozen patch. | — |
| AC-16 | **Satisfied — gate, not a discriminator.** | Final gate (Pass 4 Part C, parent-authoritative): build **0 errors**; `has-pending-model-changes` clean; **1204 tests, 0 failures**. | — |
| AC-17 | **Satisfied.** | T13, **15 page tests** (Pass 3) + Pass-4 fixes: the Pass-3 reviewer independently ruled the seven clauses **non-vacuous** and proved the test `Router` necessary (both N4 discriminators fail without query supply); Pass 4 added the owner-aware part picker and the 409 `ServerMessage` parsing with `NotContain` discriminators (**4 failed pre-fix / 19 green post-fix** in `EnrollmentExceptionsPageTests`). | exercised via the Pass-4 pre-fix/post-fix runs. |
| AC-18 | **Satisfied.** | T11's `/check` integration test at the real endpoint, **both owner forms** (`?gradeLevelId=` and `?activityGroupId=`), containment on `onDate`; the final re-verify proved runtime translation rather than assuming it (`/check` is served by the same `ListDtosAsync`, so the ordering is exercised on real Postgres in the 69/69 suite). | M6 planned; a mutation run is not separately recorded. |
| AC-19 | **Satisfied — guard, honestly labelled (F6c).** | T3's re-expressed §9 rollover guard: bridge row, no exception, prior terms archived ⇒ still available. Passes pre-round too — structurally trivial post-round — and pinned anyway. | — |

**AC-5's mechanism note.** AC-5's invariant ("RISK-2 dissolved — no active period resolves, the
helpers are gone, the readers are clean") was first planned to be discharged by a bUnit
assertion. The owner chose an **architecture source-scan test** (`DeprecatedPeriodWritePathArchitectureTests`,
which also carries the deprecate-don't-delete guard it always did) in its place. The substitution
is **stronger**: a source-scan over production code proves a negative — the dissolved helpers
have zero references and the feed readers carry no `IActivePeriodProvider` injection — and it
cannot be satisfied by incidental component rendering, is independent of bUnit's child-materialisation
pitfalls, and re-fails immediately if the dissolution is ever reverted. The behavioural half
(no active period ⇒ still excluded) remains covered by T3's rewritten availability tests. The
architecture guard itself is **untouched** (T18) — it guards the deprecated write path (AC-11),
and AC-5 rides the same suite's green result plus the plain-grep absence sweep.

### Scope gates — all held

- `src/Assignments/**` diff is **empty** (verified by every pass reviewer; the lookup client reduces by `TopicId` only).
- `Program.cs` untouched; the `/enrollment-exceptions` group is chained via `StudentEndpoints.cs` (v1 P1-3 posture stands).
- The deprecated period write path is **retained and guarded** — `DeprecatedPeriodWritePathArchitectureTests` untouched, both guards passing.
- No new ports, base addresses, AppHost `WithReference`s, or project-reference changes; no `.csproj`/CPM change; `CrossModuleWiringTests` green within `ArchitectureTests.Unit` 72/72.

### Residual risks (accepted, with honest severity)

1. **DateRange containment is still server-only** (Pass-4 residual P2). `BuildDivisionOptions()` filters the *part* per FR-56, but a `DateRange` group's span-inside-window rule is enforced only server-side — a 422 round-trip the client could pre-empt. The server remains the gate and P2-3's `ServerMessage` parsing makes the failure legible. Low.
2. **The null-first order is unasserted** (Pass-4 residual P2). The exception list is now null-first (open-start rows lead), runtime-proven via `/check` sharing `ListDtosAsync`, but no test pins the order; a one-line integration assertion would close it. Low.
3. **`P2-4` — the subject picker offers only currently-effective subjects** (app-wide convention, flagged to the owner, unchanged): a forward-dated exception cannot be created for a bridge row whose window starts in the future. Not a pass-3 regression; no change this round.
4. **The truncated 62-character FK name** (`fk_subject_enrollment_exceptions_activity_groups_activity_grou`): consistent across migration, Designer and snapshot; `has-pending-model-changes` clean. Left truncated deliberately — renaming re-enters the 63-byte danger zone for no functional gain (Pass-2 review, P2-6, accepted as a residual).
5. **Step-0 conversion stays deferred** — the one pinned dev bridge row remains inert; the whitelist→exception conversion ships only as a separately reviewed script under owner sign-off (carried from v1, unchanged).
6. **Cosmetic:** `EnrollmentExceptionsPageTests.cs:158`'s doc comment says "terms and semesters" while the fixture seeds only a Terms period (Pass-3 vintage).

### §(g) inventory reconciliation

The plan (as revised) declared **67 files** (P1–P49 + T1–T19, including the two compile-mandatory
Q5 additions P47/P48 from plan-review N1). The round touched **67 declared files plus two declared
additions**: `EnrollmentExceptionLabels.cs` (P49, the P2-3 count-label unifier) and the
`IsSubjectExceptedApi`/`IsSubjectExceptedAsync` client wrapper inside P30 — both §(g)-declared in
this doc by the parent (Pass-3 review P2-5), never silent scope widening. Final tree state at
acceptance (re-verified at acceptance by `git status --porcelain`): **26 untracked + 40 modified +
2 deleted = 68 paths**; nothing staged (0 cached paths, no `A` entries); HEAD still `3e408bfa`.
The 68th path is the round doc itself, which is tracked and modified but not part of the frozen
`src`+`tests` patch. Pass-2's `StubActivePeriodProvider` deletion (T10) proceeded on a parent
ruling that the plan's T10 entry was factually wrong — the file's only apparent consumers were
nested private classes — and is recorded here as a declared, evidenced deletion, not a §(g) violation.

### Round artifacts

- `documents/rounds/round-enrollment-exceptions.md` — this document (plan + worker report + reviews + acceptance).
- `documents/rounds/diffs-enrollment-exceptions.patch` — **11176 lines** (550,067 bytes), frozen in Pass 4 Part C against round base `3e408bf` via the §(k)-10 idiom, and verified **byte-identical** (`cmp`) to a fresh recomputation by the Pass-4 re-verify reviewer.
- `documents/rounds/diffs-subject-period-blocks.STALE.md` — the Pass-1 review **F2** annotation: the v1 patch is stale (the v1 round doc was edited after its patch froze) and cannot be re-frozen; the note names what the patch omits and how to reconstruct a true baseline.

### Verification record (the final authoritative gate — Pass 4 Part C, parent-run)

| Gate | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** |
| `has-pending-model-changes` | *"No changes have been made to the model since the last migration."* |
| `dotnet test` — full suite | **1204 tests, 0 failures** *(parent)*: `Admin.Tests.Unit` **581/581**, `Students.Tests.Unit` **482/482**, `Students.Tests.Integration` **69/69** (real Postgres), `ArchitectureTests.Unit` **72/72** |
| Retired-token sweep (plain `grep -rn`, `src`+`tests`, bin/obj excluded) | **0 hits** for all ten patterns; the widened five-pattern set also 0. Sweep coverage proven — it sees untracked files. Re-run at acceptance: **0**. |
| Git state | nothing staged, no `A` entries, nothing committed |

### Open items at acceptance

1. **The UI-tester pass has NOT been run.** It is the one Tier-3-full step outstanding
   (`## UI Tester` below is still a stub). It needs the AppHost running, and the owner has not
   yet said whether to run it or defer with sign-off. The reviewer's residual list for it:
   group-owned single-part behaviour; open-start rows leading the list (the unasserted null-first
   order, residual 2); the second-add state; the `.add-label { align-self: center }` layout
   question in a `flex-end` row; and entry-point pre-selection from both the landing and the
   grade-detail card.
2. **Two owner decisions are still outstanding** (see *What to do next*): the UI tester, and
   whether the page's list should filter to the pre-selected subject (Pass-3 residual: the
   pre-selected topic lands in the **add panel** while the list shows the whole owner;
   "list only that subject" would be a one-line follow-up).
3. The round is therefore **accepted with open items**, not unconditionally closed: the code,
   tests and artifacts are complete and green, but the UI-tester step and the two owner
   decisions above remain live.

### What to do next

- **Merge-policy posture:** nothing is committed — no staged files, no `A` entries, the working
  tree is intentionally dirty (the round continues the uncommitted v1 stack on
  `feat/subject-period-blocks`). The branch/commit/push/PR steps each need the owner's explicit
  instruction per the main-branch merge policy; nothing here authorises them.
- **Owner decision 1 — the UI tester:** run the Tier-3-full bug-hunt (AppHost up) or defer with
  explicit sign-off.
- **Owner decision 2 — the page's list filter:** whether the list should narrow to the
  pre-selected subject or keep showing the whole owner (current behaviour).
- Optional one-liners, if the owner wants them: the null-first order assertion (residual 2) and
  the stale fixture doc comment (residual 6).

### Round lessons (consolidated from the `## Worker Report`)

1. **A build-only worker gate cannot catch an emergent provider-limit overflow.** Pass 1's
   table rename pushed an auto-generated FK name past Postgres's 63-byte identifier cap; EF
   truncated the model name while the snapshot/Designer kept the full string — the build passed,
   and **64 integration/unit tests failed** until the parent's authoritative suite diagnosed it.
   The split (worker builds small, parent runs the full suite at freeze) is what caught it.
2. **`git grep` skips untracked files.** Every file this round renamed or created was untracked,
   so `git grep`-based "0 hits" sweeps were partly vacuous — plain `grep -rn` (or
   `git grep --untracked`) immediately exposed a live AC-10 violation in a comment. Residue
   sweeps must use plain grep while the work under review is uncommitted.
3. **Grep the full identifier, not a trailing fragment.** The fragment
   `activity_groups_activity_group_id` matched other tables' FKs and looked like un-renamed
   residue; the full FK name showed 0 old / 1 new correctly.
4. **A reviewer's static diagnosis must be mutation-tested before it is implemented.** Pass-3's
   P2-1 (pre-selection lost via `DropdownComponent`'s reset path) was unreachable — the worker's
   instructed test *passed pre-fix*, and the final re-verify withdrew the finding; the worker's
   mutation-first method found the genuinely reachable defect instead.
5. **Review greps must exclude `bin`/`obj`.** Whole-tree token sweeps without the exclusions
   crawl thousands of build-output files and block the tool for many minutes (run `348c789d`
   tripped the 240s timeout signal exactly this way). Mandate `--exclude-dir=bin --exclude-dir=obj`.

*(Not recorded here because it is not a lesson about method: pass-specific fixes, dispositions
and rulings live in the `## Review` and `## Worker Report` sections above.)*

### Amendment — owner-directed list filter (post-acceptance, 2026-09-26)

After this acceptance was written, the owner directed one behaviour change: **the page's list must filter to the
pre-selected subject** (previously the `?topicId=` subject was pre-selected only into the add panel while the
list showed the owner's whole set).

**Implemented:** `_filterTopicId` / `VisibleExceptions` / `FilterTopicName` / `ShowAll()`;
`ApplyPendingTopicSelection()` now resolves the query subject once and applies it to **both** the list and the add
panel. Clear affordance `Show all` (`.filter-bar` / `.filter-note` / `::deep .show-all`, isolated CSS — no inline
`<style>`).

**Two distinct empty states, both non-warning prose:**
- default: `No exceptions — every subject is offered on every date.`
- filtered: `No exceptions for {subject} — this subject is offered on every date.`

**Tests:** 5 added (page test file 19 → **24**; `Admin.Tests.Unit` 581 → **586**):
`QueryString_FiltersTheListToThePreSelectedSubject`, `ShowAll_ClearsTheFilter_AndRestoresTheOwnersWholeSet`,
`FilteredEmpty_SaysTheSubjectHasNone_NeverThePageDefaultEmptyState`, `NoTopicInTheQuery_ListsEverySubjectsExceptions`
(guard for the default path), and `QueryString_TopicTheOwnerDoesNotList_LeavesTheListUnfiltered`. (The three
filter-behaviour tests are `public void` — the assertions are pure DOM/source reads, no await — which is worth
knowing before grepping for them by `async Task`.)

**Mutation-verified:** removing the filter (`_filterTopicId = null`) failed **3** of them —
`…FiltersTheListToThePreSelectedSubject` (*"Expected … FindAll(`.exception-row`) to contain 1 item(s), but found
2"*) plus the `ShowAll` and `FilteredEmpty` tests — while the two guards correctly stayed green. The filter was
then restored **byte-identically** (re-diffed against the round's frozen patch).

**Re-verified at amendment (parent, authoritative):** `Admin.Tests.Unit` **586/586**, `Students.Tests.Unit`
**482/482**, `Students.Tests.Integration` **69/69**, `ArchitectureTests.Unit` **72/72** — **1209 tests,
0 failures.** HEAD still `3e408bfa`; nothing staged; no `A` entries. *(The round patch is **not** re-frozen;
re-freezing happens only if the owner asks.)*

**Consequences recorded by the worker (flagged for the owner):**
1. **The count badge stays the owner's total under a filter** — deliberate: it is the owner's heading, and under a
   filter it is what tells the reader the rows are a subset. Asserted in two tests.
2. Switching owner **clears** the filter, so nobody is stranded on a subject the new owner may not list.
3. `Show all` is **view-only** — the URL keeps `?topicId=`, so a fresh kebab navigation filters again.

**New residual (unfixed — needs a decision):** while filtered, adding an exception for a **different** subject
re-reads the list but the new row falls **outside** the filter, so the write can look like a no-op. `Show all` is
the escape. Options: auto-clear the filter on a successful add of a non-filtered subject, or keep the behaviour
and document it.

### Static UI/design review (`ollama/minimax-m3:cloud`, run `bc7f986c`)

**Verdict: APPROVE — 0 P0, 2 P1, 6 P2.** Explicitly a **static** review: no browser tooling exists in this
environment (only `fluent-ui-blazor` + `memory` MCP servers; no Playwright), so the run was forbidden from
claiming visual verification and required to emit a **"cannot verify statically"** list — 10 items, recorded
below. It **corrected the parent's own count**: the page test file holds **24** tests, not the 19 the brief
said (the 3 filter-behaviour tests are `public void`, not `async Task`, which is what defeated two earlier
greps). It judged **all 24 non-vacuous** with two mild caveats: `AddWithNothingSelected_WritesNothing` is
partly vacuous (a disabled button never fires OnClick — the `disabled` assertion is the real check) and
`PageOffersNoSaveControl` would also pass on a page that failed to render the add panel. Design-system
compliance verified across CSS isolation, `::deep` usage, the `DropdownComponent` wrapper, the `FieldWidth`
ladder, badge/button `Appearance` values, the icon shorthand, and message-bar intents.

**P1-1 — brittle `align-self` override in a `flex-end` row.** `.add-row { align-items: flex-end }` +
`.add-label { align-self: center }` (`EnrollmentExceptions.razor.css:135-138`) — the exact pattern the repo's
own **`flex-row-input-alignment`** skill warns against (it prescribes `align-items: flex-start` for rows mixing
tall composites with single-line siblings, and calls per-child `align-self` "brittle"). **This was already
flagged by the Pass-3 diff reviewer**, so the worker had two signals and used neither. Fix: `flex-start` on the
row, drop `.add-label { align-self: center }` and the redundant `::deep .add-exception { align-self: flex-end }`;
no markup change.

**P1-2 — `min-width: 12rem` with no `overflow-wrap`, in a nowrap row.** `.exception-topic` / `.exception-span`
(`:103-110`) set `min-width: 12rem` while `.exception-row` is `flex-wrap: nowrap` (default) and no `overflow`
rule exists on the section, so a long subject name (e.g. "Advanced Placement Physics C: Electricity and
Magnetism") pushes the row past the section width. `.exception-reason` **does** have `overflow-wrap: anywhere` —
the subject and span columns do not. Fix: `overflow-wrap: anywhere; min-width: 0;` on both.

**P2s (polish/a11y):** no `aria-label` on the loading `FluentProgressRing`; the empty-state and filter-bar prose
are not announced on transition (no `role="status"`/`aria-live`) while the `FluentMessageBar` states are;
date-picker placeholders ("Any date before/after") are ambiguous with the explanation living on the adjacent
Clear button; the **Clear button click is not directly tested** (only its `disabled` state); the owner-change
panel reset (`ClearAddSelection`) has no direct test; and the `Span` header has no programmatic association with
the FROM/TO inputs.

**Cannot verify statically (10, for a browser pass):** real vertical alignment in the add row; wrap behaviour at
narrow widths; the `min-width` overflow at <480px; real badge tints; whether the filter bar + `Show all` read as
one line; whether W5 fits the longest reason; actual icon rendering at runtime; spinner visibility/placement;
the section-vs-panel separator weights; keyboard focus order.

### UI-quality pass — P1 fixes, a11y, and Q2(a) (owner-approved, 2026-09-26)

**P1-1 applied as prescribed:** `.add-row { align-items: flex-start }`; **both** per-child overrides deleted
(`.add-label { align-self: center }` and `::deep .add-exception { align-self: flex-end }`); `.date-field`'s inner
`align-items: flex-end` kept (that one is correct). `.span-group { align-items: flex-start }` added for the
a11y group. **`align-self` no longer appears anywhere in the file.**

**P1-2 applied with the necessary adjustment:** the 12rem moved from `min-width` to **`flex-basis`**
(`flex: 0 1 12rem`) on `.exception-topic` / `.exception-span`, plus `min-width: 0; overflow-wrap: anywhere;` on
both — the worker correctly noted the fix **cannot bite** while a `min-width` floor stands in a `nowrap` row, and
left that reasoning as a comment at the rule.

**Accessibility:** `aria-label` on the loading `FluentProgressRing`; `role="status"` + `aria-live="polite"` on the
filter note and both empty-state sentences; the `Span` header and its two date fields wrapped in
`role="group" aria-label="Span"`.

**Q2(a):** on a **successful** add, if the added subject differs from the active filter the filter is cleared so
the written row is visible; rejected writes (409/422) leave the filter alone.

**Tests:** 5 added (page file 24 → **29**; `Admin.Tests.Unit` 586 → **591**) —
`ClearDate_UnbindsItsOwnDate_AndRecomputesTheWriteAffordance`, `ChangingOwner_ResetsTheAddPanel`,
`AddForAnotherSubject_ClearsTheFilter_SoTheWrittenRowIsVisible`, `FailedAdd_LeavesTheFilterAlone`, and
`FilterLine_EmptyState_AndSpanGroup_CarryTheirAccessibleNames` (an **extra** test beyond the approved list, added
because the three a11y items were otherwise assertion-free — worker-disclosed and justified).

**Mutation-verified — four mutations, each failing exactly 1/29, each reverted byte-identically (`diff`-verified):**

| Mutation | Result |
|---|---|
| Q2(a) auto-clear removed | `AddForAnotherSubject_…` **failed** — *"…the row just written is visible …, but it misses {Biology}"*; `FailedAdd_LeavesTheFilterAlone` stayed green |
| `ClearAddSelection()` removed from `OnOwnerChangedAsync` | `ChangingOwner_ResetsTheAddPanel` **failed** — reason still `"Staffing"` |
| `_startDate = null` removed from `ClearStartAsync` | `ClearDate_…` **failed** — picker still `2027-03-01` |
| `role`/`aria-live` removed from the default empty state | a11y test **failed** — `role` was `<null>` |

**Trap found and documented (worth keeping):** `FluentDatePicker` renders an **internal `FluentTextField`**, so this
page has **3** `FluentTextField` components and `FindComponent<FluentTextField>()` returns a *date picker's inner
field*, not the reason box. The worker's first owner-reset test silently wrote `"Staffing"` into the start-date
picker; it caught this, switched to a DOM lookup (`fluent-text-field[placeholder='Why?']`), and documented the trap
on the helper. Any future `FluentTextField` lookup on this page hits it.

**Final gate (parent, authoritative):** `Admin.Tests.Unit` **591/591**, `Students.Tests.Unit` **482/482**,
`Students.Tests.Integration` **69/69**, `ArchitectureTests.Unit` **72/72** — **1214 tests, 0 failures.** Build
0 errors. (An initial `MSB4166` was 15 stale MSBuild node-reuse servers, **not** a lock — retried clean; the worker
reported it honestly rather than as a code failure.)

**Residual — the static blind spot is now sharper, not closed:** the fixes were applied to the reviewer's
**prescription**, not to observed pixels. `align-items: flex-start` plus the deleted `.add-exception
{ align-self: flex-end }` means the Add button now top-aligns with the **label line** rather than the input
bottoms, and long subject/span text now **wraps inside** its 12rem column instead of widening the row. Both are
unconfirmed without a browser, as are the live-region announcement behaviour (regions inserted *with* their text
are announced inconsistently across browsers) and the loading ring's `aria-label`.

## UI Tester

*(stub — bug-hunt of the delivered UI; EXPECTED this round per the traceability line. Surfaces:
`/students/enrollment-exceptions` page, landing badge+kebab, grade-detail card badge+kebab,
View-all badge+kebab.)*

- Surfaces covered:
- Defects found: