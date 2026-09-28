tier 3 full; provider ollama; orchestrator ollama-cloud/glm-5.3-flash; worker ollama-cloud/deepseek-v4.1-flash; plan-reviewer ollama/glm-5.3:cloud; code/diff-reviewer openrouter/stealth/space-bunny-alpha (thinking high, per-role override named by the owner for the post-worker diff review only); ui-tester ollama/minimax-m3:cloud; round base 3e408bf; UI round EXPECTED (spec has .razor surfaces in scope).

# Round subject-period-blocks — Subject Period Exceptions (decouple subject availability from the period lifecycle)

Implements `documents/specs/subject-period-exception-model.md` **only** (corrected
2026-09-26 — its §1 framing, §7 Step-0 SQL, §8 Q3 base class and the new §8 Q5 are
authoritative). The block/exception model retires the half-wired
`GradeTopicAssignment.PeriodId` whitelist and adds the exception capability the current model
cannot express in any form.

---

## Context

- **Lead spec (implement THIS only):** `documents/specs/subject-period-exception-model.md`
  (Draft v1, §8 Q1 answered 2026-09-26 — blocks for real exceptions, **no hybrid**, no
  scheduling whitelist; corrected 2026-09-26 after plan review — §1, §7, §8 Q3, §8 Q5
  authoritative).
- **Context, not scope:** `documents/specs/topic-edit-dialog-redesign.md` — its **§4 is
  SUPERSEDED** by the lead spec and is **not** re-planned here. Its §3 (coded-value override)
  and §5 (layout) stand but are **not implemented in this round** (they belong to the
  `feat/topic-edit-dialog-redesign` branch/round). This round takes from it only the surviving
  rules the lead spec names: FR-ED-15 (period names, never GUID prefixes), FR-ED-16
  (grade-scoped section hides without a grade), and FR-ED-11 inverted (block picker excludes
  `Archived`/`Deactivated`; existing blocks on retired periods stay visible **and deletable**).
- **Context ONLY — must NOT be implemented here:** `documents/specs/subject-enrollment-requirement-flag.md`
  (`RequiresEnrollment`). It is a separate spec, separate migration, own cross-context hop. The
  two specs share the `TopicAssignment` bridge but are orthogonal: this round **removes** the
  period meaning from the bridge; that spec **adds** a boolean to it. Do not conflate, do not
  add `RequiresEnrollment` anywhere.
- **Pitfall list (read before touching the UI):**
  `documents/solution/subject-topic-delivery-periods.md` — esp. §8 pitfalls 1–12 (period on the
  bridge, never key by `TopicId`, `Error="@Error"` literal-string trap, `FluentSelect`/`FluentMenu`
  don't materialise children while closed, invoke row actions via `cut.InvokeAsync`).
- **Architecture ground truth (verified this round, see §Plan (d)):** the filtered unique index
  `ix_topic_assignments_tenant_grade_topic_unique` on `(tenant_id, grade_level_id, topic_id)`
  (`topic_assignment_type = 'grade'`) means **at most one** bridge row per (tenant, grade, topic).
- **Branch:** `feat/subject-period-blocks`, round base `3e408bf`. This round doc replaces a
  prior planning run's doc (different model); the old plan is discarded.

---

## Plan

### (a) GOAL

**Framing corrected 2026-09-26 after plan review.** The spec's original §1 claimed a pinned
subject "silently disappears from the grade's curriculum" at year rollover. **That is false and
is retracted** — every availability read path is period-blind (evidence in the corrected spec
§1: no availability handler reads `PeriodStatus`; four of five readers ignore `PeriodId`
altogether). This round is **not** a vanishing-subject bug fix. Its real, verifiable
justification is threefold:

1. **`PeriodId` is half-wired.** Four of the five availability readers ignore it entirely
   (`ListGradeTopicAssignments`, `ListActivityGroupTopicAssignments`,
   `ListGradeTopicCurriculumByGrade`, plus every other non-picker reader) — no period
   predicate at all. The single reader that uses it, `ListTopicsByGrade`, matches `PeriodId`
   **exactly** and only under an explicit `?periodId=`; that exact match excludes the
   year-spanning `PeriodId = null` case the design itself treats as the primary state. Half
   the field does nothing; the half that does anything is wrong-shaped.
2. **The whitelist set was never representable.** The unique index
   `ix_topic_assignments_tenant_grade_topic_unique` on `(tenant_id, grade_level_id, topic_id)`,
   filtered `topic_assignment_type = 'grade'`, permits at most ONE bridge row per
   (tenant, grade, topic) — so "N rows for N periods" was never possible, and
   `TopicAssignment.UpdatePeriod(Guid?)` is the sole mutation path. A set of delivery periods
   cannot be expressed in any form.
3. **The block/exception capability does not exist.** The owner's stated need — "block this
   subject for Term 3" — has no representation today: one bridge row carries one period, and
   `null` is the only alternative.

This round therefore inverts the default: the bridge row keeps **no period meaning at all**, and
a new `SubjectEnrollmentBlock` entity records the *exception* ("not offered in this one period,
e.g. teacher on leave"). Availability becomes **bridge exists AND no block on the active
period** — an exact id match, not a date-range resolution. Year rollover and period archival
become no-ops because nothing on the bridge is period-scoped any more — a simplification, not a
repaired bug. The refinement is entirely Students-side; `Assignments.Core`/`Assignments.Api`
production code does not change.

### (b) SCOPE GATES

**IN — the block/exception model only:**
- `SubjectEnrollmentBlock` entity + EF migration + block CQRS (create/list/remove) + block API routes.
- Availability semantics: `EXISTS bridge AND NOT EXISTS block(owner, topic, activePeriod)`.
- The "Enrollment exceptions" section in `TopicEditDialog` and the rewiring of the three
  `TopicPeriodsEditDialog` entry points (one of which is a BLOCKING owner decision — see (h)).
- Cache-key hardening of the three cached availability queries (see (i) cache-key criterion).
- Deprecation (not deletion) of **every** period write path — grade, group AND create-with-link
  (`PUT …/period`, `AssignGradeTopicRequest.PeriodId`, `AssignActivityGroupTopic.PeriodId`,
  `CreateTopicForGrade.PeriodId`).
- **Removal of the `TopicCreateDialog` period picker** — decided THIS round (see (e)).

**OUT — explicitly, do not touch:**
- `RequiresEnrollment` flag (sibling spec — separate round: separate migration + own
  cross-context hop via `StudentsContactResolver`).
- Strands and lessons — `<StrandsEditor>` untouched; the `strand-lesson-unification-plan`
  surfaces are not in this round. Acceptance asserts the diff is EMPTY (AC-7).
- Subscription-only activity groups (deferred per the sibling spec §0 decision 11).
- The students × subjects matrix — not built.
- **ANY change to the `Assignments.Core → Students.Core` project reference.** Deferred to
  `documents/solution/cross-context-rule-followups.md` §1 (architect decision pending). The
  worker must not remove, add, or allow-list that reference, and must not add the mechanical
  guard from that doc's §2.
- The coded-value sections of `TopicEditDialog` (redesign spec §3) — different round.
- `SubjectEditDialog` internals and the `?periodId=` landing filter UI (pitfall doc §7 item 9).
  `TopicCreateDialog` is only partially in scope (picker removal, (e)); its other internals are
  untouched.
- Any change to the `?periodId=` surface semantics — its post-round behaviour (it silently
  returns nothing once rows carry `PeriodId = null`) is an **owner decision**, corrected spec
  §8 Q5. Not in scope.

### (c) DATA MODEL — `SubjectEnrollmentBlock`

New entity `src/Students/SchoolCollab.Students.Core/Domain/SubjectEnrollmentBlock.cs`:

```csharp
public sealed class SubjectEnrollmentBlock : BaseTenantEntityWithAudit, IHasRowVersion
    // soft delete + audit stamps + xmin row version — per spec §8 Q3 (corrected 2026-09-26);
    // mirrors Domain/GradeAssignmentPolicy.cs + GradeAssignmentPolicyConfiguration.cs.
    // BaseTenantEntity supplies ONLY Id + TenantId (no is_deleted, no audit, no row version)
    // and CANNOT deliver the soft-delete invariant below.
{
    public Guid Id { get; private set; }
    public Guid? GradeLevelId { get; private set; }       // mutually exclusive with ActivityGroupId
    public Guid? ActivityGroupId { get; private set; }    // mutually exclusive with GradeLevelId
    public Guid TopicId { get; private set; }
    public Guid PeriodId { get; private set; }            // REQUIRED — non-nullable
    public string? Reason { get; private set; }           // optional free text, never load-bearing
}
```

Invariants the entity, factory method, and validator must enforce:

1. **Exactly one** of `GradeLevelId` / `ActivityGroupId` is set (mirrors the bridge rule).
2. **`PeriodId` is NON-NULLABLE.** A block with no period is meaningless — "no period" *is* the
   default (available) state. This non-nullability is the whole point of the inversion; the
   nullable-whitelist case has no analogue.
3. **No status/lifecycle column.** A block is immutable history. Un-blocking deletes the row
   (soft delete via `BaseTenantEntityWithAudit`, which supplies `is_deleted` + audit stamps,
   plus `IHasRowVersion` for `xmin` — `BaseTenantEntity` supplies only `Id` + `TenantId`,
   corrected spec §8 Q3). A status column would recreate the
   coupling this spec exists to remove.
4. **Tenant isolation + composite unique index including `tenant_id`** (repo convention — see
   `20260708143206_AddTenantIdToStudentsOperationalEntities.cs` and
   `20260826180912_AddActivePeriodUniqueIndexes.cs` for the existing filtered-unique-index
   shapes). Two filtered unique indexes:
   - `(tenant_id, grade_level_id, topic_id, period_id)` UNIQUE — the owner-not-null predicate
     AND the soft-delete exclusion merged into ONE `HasFilter` string:
     `.HasFilter("grade_level_id IS NOT NULL AND is_deleted = false")`
   - `(tenant_id, activity_group_id, topic_id, period_id)` UNIQUE —
     `.HasFilter("activity_group_id IS NOT NULL AND is_deleted = false")`
   (Merged-string precedent: `20260826180912_AddActivePeriodUniqueIndexes.cs:25` —
   `.HasFilter("period_type = 0 AND status = 1")`.) **REQUIRED, not pattern-conditional**:
   without it,
   removing a block and re-adding the same (owner, topic, period) collides on the unique index
   (corrected spec §8 Q3). Duplicate blocks are a **409**, not a
   silent second row.
5. **FK behaviours:** `GradeLevelId` → Cascade (a block dies with its grade), `TopicId` →
   Cascade, **`PeriodId` → `ON DELETE RESTRICT` at the DB level.** The spec's argument that
   RESTRICT is "effectively guaranteed" (period deletion is Draft-only; a block never targets a
   Draft-only-deletable period) is the belt — but note §4.2 of the lead spec *permits* blocking
   a `Draft` period, and Draft is exactly the deletable status. Do **not** rely on the status
   lifecycle: declare the FK `Restrict` in the migration so orphaning is impossible regardless
   of status transitions.
6. **Period admissibility (validator):** a block may target `Draft`, `Active`, or `Completed`;
   `Archived` and `Deactivated` are rejected (they are not submittable). Grade-owned blocks:
   academic year, or Term/Semester within the tenant's **active** academic year (FR-57 rule).
   Group-owned blocks: period type must match the group's `EnrollmentSpan`
   (`Termly`→`Term`, `Semester`→`Semester`, `WholeAcademicYear`→`AcademicYear`);
   `OpenEnded`/`DateRange` groups cannot be blocked at all (FR-56 rule). Implement these as new
   methods on/next to `TopicAssignmentPeriodValidator` (it already centralises FR-56/57 for the
   bridge) so the block rules live beside the rules they mirror — do **not** try to extract the
   private `FilterPeriodsForGrade`/`FilterPeriodsForGroup` from `TopicCreateDialog.razor`;
   those are client-side helpers, the server-side equivalents already exist in the validator.
7. **Availability semantics (single source of truth):**
   `SubjectAvailable(owner, topicId, activePeriodId) = EXISTS bridge(owner, topicId) AND NOT
   EXISTS block(owner, topicId, activePeriodId)`. `periodId` is always the **active** period
   (`EnrollStudentHandler` already hard-requires enrollment to target it), so the check is an
   exact id match — today's date-vs-period range fuzziness disappears.

**Schema-constraint consequence (ground truth — treat as verified):**
Migration `src/Students/SchoolCollab.Students.Core/Migrations/20260803171345_SplitGradeSubjectAssignmentIntoTopicAssignments.cs:119`
creates `ix_topic_assignments_tenant_grade_topic_unique` on `(tenant_id, grade_level_id, topic_id)`,
UNIQUE, filtered `topic_assignment_type = 'grade'`. Therefore:

- **The old multi-period whitelist was NEVER representable in the database.** `PeriodId` was
  always a single mutable value per (tenant, grade, topic); `TopicAssignment.UpdatePeriod(Guid?)`
  (Domain/TopicAssignment.cs:127) is the only mutation path. The two-bridge-row premise that the
  shipped period editor is built on is unreachable in a real database.
- **Do not change this index.** The block model is **consistent with it**: one bridge row per
  grade+topic (the durable relationship), exceptions in a separate table. This is not a
  coincidence to paper over — it strengthens the case for the inversion, and the round doc must
  say so: the whitelist required multi-row semantics the index forbids, so it could only ever
  behave as a single mutable value — a set of delivery periods was never expressible
  (justification 2 in (a)).
- **`GradeTopicAssignment.PeriodId` is KEPT AND IGNORED.** Do not drop the column (spec §8 Q2:
  recommend keep-and-ignore; it is a useful forensic record and dropping it is a second
  migration for no gain). `UpdatePeriod` stays on the domain type but gains no new callers.
- **Tests that seed two bridge rows for the same (grade, topic) assert a state the database
  would reject.** They are mocked-HTTP bUnit tests so they pass today, but the premise is
  unreachable. Affected (all to be deleted or rewritten in this round — see (g)):
  - `GradeLevelDetailPageTests.Detail_TopicsCard_SubjectRunningInTwoTerms_ShowsBothPeriods_AndViewAllDoesNotCrash`
    (tests/SchoolCollab.Admin.Tests.Unit/GradeLevelDetailPageTests.cs:383)
  - the two-term cases in `TopicPeriodsEditDialogTests`
    (`Dialog_SubjectRunningInTwoTerms_RendersAndKeepsBothPeriods`, :193, and siblings)
  - `SubjectsLandingPageTests.EditPeriods_OpensEditorWithOneRowPerBridgeRow`
    (tests/SchoolCollab.Admin.Tests.Unit/SubjectsLandingPageTests.cs)
  This round **retires `TopicPeriodsEditDialog`**, so its test file goes with it (deleted, or
  rewritten against the exceptions section); the Detail-page and landing tests are rewritten to
  the block model (one bridge row per grade+topic; period display now derives from blocks).

### (d) MIGRATION — explicit EF migration step

Migrations live in `src/Students/SchoolCollab.Students.Core/Migrations/` (latest:
`20260910125042_AddGradeAssignmentPolicy`). **Inspect the folder first** — do not assume helper
shapes; follow `.github/copilot/rules/ef-migrations.md` (and read
`.github/copilot/rules/dotnet-best-practices.md` + its SKILL before writing any C#). Note
`SchoolCollab.ArchitectureTests.Unit` carries migration-guard tests; keep the Designer + model
snapshot consistent.

**Step 0 — measurement precondition (the worker MUST run and report before writing the migration):**

```sql
-- There is NO grade_topic_assignments table. The bridge is the TPH table
-- topic_assignments, discriminated by topic_assignment_type. Group-owned rows
-- carry period_id too — measure BOTH audiences (corrected spec §7).
SELECT topic_assignment_type, grade_level_id, activity_group_id, count(*)
FROM topic_assignments
WHERE period_id IS NOT NULL
GROUP BY 1, 2, 3;
```

Run against the dev postgres container (see the `run-migration-service-standalone` skill for how
to reach it without the full AppHost). Report the row count verbatim in the Worker Report. The
two outcomes:

- **Outcome A — zero rows:** the whitelist was never used. No data to convert, and the
  migration is **purely additive** (new `subject_enrollment_blocks` table + its indexes + FKs).
  Proceed with the round as planned.
- **Outcome B — non-zero rows:** each whitelist row must be converted (null its `PeriodId`,
  insert sibling-period blocks per spec §7 step 2, reviewed row-by-row before applying). **The
  spec's own flag suggestion is self-contradictory and this plan resolves it:** spec §7 says
  apply outcome B "behind `FEATURE:UseSubjectPeriodExceptions`", but spec §6 says **no feature
  flag** for the model because two divergent availability rules are unacceptable for enrollment
  correctness — and per `AGENTS.md` (feature-flag-workflow) a flag here would be a deployment-time
  startup switch anyway, which cannot gate a data conversion per tenant at runtime. **Resolved
  rule for this round: the EF migration ships ONLY the additive table (outcome-A shape). If the
  measurement returns rows, the worker STOPS, does not write the conversion into the migration,
  and reports the counts to the parent** — the conversion then becomes a separately reviewed
  script applied under owner sign-off, not a runtime-flagged code path. This keeps "no feature
  flag" intact (there is never a flag-gated availability rule) and keeps a live-data conversion
  from riding silently inside a code round.

**Draft-block × `DeletePeriod` interaction (P2-10 — specified, not silently accepted):**
`DeletePeriodHandler` guards only period *status* (FR-D2 Draft-only) and dangling
`NextPeriodId` links (FR-D6); it knows nothing about blocks. Because AC-3 permits blocking a
`Draft` period, a Draft period carrying a block passes the status guard and then fails the
`ON DELETE RESTRICT` FK at `SaveChanges` — a raw Postgres exception surfacing as a **500**, not
a 422. Handling specified: `DeletePeriodHandler` gains a pre-mutation guard that loads blocking
`SubjectEnrollmentBlock` rows via `ISubjectEnrollmentBlockRepository` and throws
`PeriodNotDeletableException` (422) naming the block — the same message shape as the FR-D3
sub-period blocker (file #20, test #36). The DB-level RESTRICT stays as the belt; the guard
only corrects the status code and the message.

### (e) API surface + deprecations

New routes (grouped in an extension method — see (g) files #22/#23; wired on the **students
group chain, never in `Program.cs`**), in or beside
`src/Students/SchoolCollab.Students.Api/Endpoints/TopicAssignmentRoutes.cs`:

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/students/subject-blocks?gradeLevelId={id}` | list blocks for a grade (or `?activityGroupId=`) |
| `GET` | `/students/subject-blocks?gradeLevelId={id}&topicId={id}&periodId={id}` | is this topic blocked in this period (for pickers) |
| `POST` | `/students/subject-blocks` | create a block (409 on duplicate) |
| `DELETE` | `/students/subject-blocks/{id}` | remove a block (idempotent) |

**Endpoint seam — P1 security finding (P1-3, closed):** the `/students` group, its
`RequireAuthorization` + Bearer scheme, and the route-extension chain live in
`StudentEndpoints.cs:12-32`; `Program.cs:103` has exactly one mapping call
(`app.MapStudentEndpoints(featureFlags)`). The new routes are added as
`.MapSubjectBlockRoutes(this RouteGroupBuilder group)` — mirroring
`MapTopicAssignmentRoutes(this RouteGroupBuilder group)` — and wired **only** by appending to
that chain (file #23). Wiring a fresh `/students` group in `Program.cs` would ship these
endpoints **UNAUTHENTICATED** (no OIDC-gated `RequireAuthorization`), which is a **P1 security
finding**; the worker must not touch `Program.cs`.

**Deprecate, do not delete — every period write path (P1-4 + new P1):**
- `PUT /topic-assignments/{id}/period` (`TopicAssignmentRoutes.cs:88`) — stays functional for
  wire compat but is **no longer called by any UI** after this round; deprecation comment.
- `PeriodId` on `AssignGradeTopicRequest` (grade path) — stays on the contract (back-compat),
  ignored by the handler from now on; deprecation comment.
- **`AssignActivityGroupTopic` — the GROUP write path.** It carries `PeriodId`
  (`AssignActivityGroupTopic.cs:11`), validates it
  (`AssignActivityGroupTopicHandler.cs:23-24`), persists it (`:40`), and duplicate-guards on it
  (`:31-32`). Deprecated identically: `PeriodId` accepted and ignored from now on, with
  deprecation comments on the command and the handler. Without this, whitelist semantics stay
  live for group-owned topics and (a)'s "the bridge row keeps no period meaning at all" is
  false for half the model.
- **`CreateTopicForGrade` — the FOURTH period write path (new P1).** The create-grade command
  carries `Guid? PeriodId = null` (`CreateTopicForGrade.cs:22`), which the handler validates via
  the Rev. 6 FR-57 `ValidatePeriodAsync(command.PeriodId, …)`
  (`CreateTopicForGradeHandler.cs:46-48`), duplicate-guards on (`:119` —
  `a.TopicId == subject.Id && a.PeriodId == command.PeriodId`), and PERSISTS onto the fresh
  `GradeTopicAssignment` (`:127` — `periodId: command.PeriodId`). Deprecated identically:
  `PeriodId` stays on the contract for back-compat, is **accepted and ignored**, and the handler
  stops persisting it — retiring the FR-57 validation (`:46-48`) and the `PeriodId`-scoped
  duplicate predicate (`:119`) to dead code, with a comment on the
  `ix_topic_assignments_tenant_grade_topic_unique` one-row-per-(grade, topic) reality; the
  `:111-115` comment asserting "the domain permits multiple bridge rows per (grade, topic)" —
  the exact premise this round refutes — is corrected in the same pass. The wire contract
  carries the field: `TopicRoutes.cs:355` (server record), `StudentsApiClient.cs:458` (client
  record), sent from `TopicCreateDialog.razor:548-554`.

**`TopicCreateDialog` period picker — decided THIS round:** the picker
(`TopicCreateDialog.razor:109-118`, model `:258`) sends `PeriodId` on both the group path
(`:544`) and the grade path (`:554`). Because the field is being retired, the picker is
**REMOVED** in this round (file #27); the dialog keeps creating period-less bridge rows on both
paths. Stated explicitly so no half-wired write path survives the round.

**No feature flag** anywhere in this round (see (d) for the resolved migration tension).
`documents/configuration.md` needs no change.

### (f) FR-58 / cross-context — ZERO Assignments-side production changes

The refinement is entirely Students-side. `TopicAssignmentLookupHttpClient`
(`src/Assignments/SchoolCollab.Assignments.Api/Services/TopicAssignmentLookupHttpClient.cs`)
already reduces with `dtos?.Any(d => d.TopicId == topicId)` (both the grade path :53 and the
group path :69) — so **if the Students lookup endpoint excludes blocked topics from the returned
array (relative to the active period), the unmodified client is correct**. State explicitly in
the plan and in the PR: `Assignments.Core` and `Assignments.Api` need **ZERO production
changes**; the `ITopicAssignmentLookup` contract signature is unchanged.

**Forbidden:** any new cross-context port, any new HTTP client base address, any new AppHost
`WithReference`, any change to the `Assignments.Core → Students.Core` project reference
(deferred — `documents/solution/cross-context-rule-followups.md` §1). Governing docs:
`documents/solution/adr-cross-module-calls.md` and
`documents/solution/cross-module-http-client-pattern.md`. Enforcement already exists:
`CrossModuleWiringTests` (tests/SchoolCollab.Core.Tests.Unit/Architecture) + the CI
"Cross-module wiring guard" job will catch any stray wiring — treat a red guard as a blocker,
not a test to weaken.

**Availability asymmetry (P2-8 — stated, accepted):** the block predicate lands on only **3 of
5** availability read paths (files #12–#14: `ListGradeTopicAssignments`,
`ListActivityGroupTopicAssignments`, `ListGradeTopicCurriculumByGrade`).
`ListTopicsByGrade`/`ListTopicsByGroup` — which feed the Assignments Create pickers and the
Subjects landing — stay **block-unaware this round** (out of scope; the FR-58 lookup client
does not consume them, so no Assignments-side change is implied). Residual: those two surfaces
can still offer a topic that is blocked on the active period until a follow-up round.

### (g) EXPECTED FILES — exhaustive list (40 files; phase split in "Phasing" below)

**Students.Core (domain + data + CQRS)**

| # | Path | Change |
|---|---|---|
| 1 | `src/Students/SchoolCollab.Students.Core/Domain/SubjectEnrollmentBlock.cs` | NEW — entity (`BaseTenantEntityWithAudit, IHasRowVersion`, see (c)) + `Create` factory (sets exactly one owner, non-null period, optional reason). Private setters, domain methods, no public setters — mirror the `TopicAssignment`/`GradeAssignmentPolicy` pattern. |
| 2 | `src/Students/SchoolCollab.Students.Core/Data/StudentsDbContext.cs` | NARROW: only the `DbSet<SubjectEnrollmentBlock>` property + the `ApplyConfiguration(new SubjectEnrollmentBlockConfiguration(...))` line. All index/FK/filter detail lives in file #3. |
| 3 | `src/Students/SchoolCollab.Students.Core/Data/Configurations/SubjectEnrollmentBlockConfiguration.cs` | NEW — `TenantEntityTypeConfigurationBase<SubjectEnrollmentBlock>` (Strict/audited entities require it — mirrors `GradeAssignmentPolicyConfiguration`): two filtered composite unique indexes incl. `tenant_id`, each with the REQUIRED `.HasFilter("is_deleted = false")`; FKs Cascade/Cascade/**Restrict**; soft-delete query filter. |
| 4 | `src/Students/SchoolCollab.Students.Core/Data/Repositories/ISubjectEnrollmentBlockRepository.cs` | NEW — interface, mirroring `IGradeTopicAssignmentRepository`. |
| 5 | `src/Students/SchoolCollab.Students.Core/Data/Repositories/SubjectEnrollmentBlockRepository.cs` | NEW — implementation. |
| 6 | `src/Students/SchoolCollab.Students.Core/Extensions.cs` | One `AddScoped<ISubjectEnrollmentBlockRepository, SubjectEnrollmentBlockRepository>()` line. (Path corrected per review: the repo `AddScoped` registrations live at the project root, not `Data/Extensions.cs`.) |
| 7 | `src/Students/SchoolCollab.Students.Core/Migrations/<timestamp>_AddSubjectEnrollmentBlocks.cs` (+ `.Designer.cs` + `StudentsDbContextModelSnapshot.cs` regen) | Additive migration (outcome-A shape only — see (d)). |
| 8 | `src/Students/SchoolCollab.Students.Core/CQRS/SubjectEnrollmentBlocks/Commands/BlockSubject/` | `BlockSubject.cs` (command) + `BlockSubjectHandler.cs` (validator calls, duplicate → 409 `DuplicateSubjectBlockException`). |
| 9 | `src/Students/SchoolCollab.Students.Core/CQRS/SubjectEnrollmentBlocks/Commands/RemoveSubjectBlock/` | Command + handler (idempotent; soft delete via the audit base). |
| 10 | `src/Students/SchoolCollab.Students.Core/CQRS/SubjectEnrollmentBlocks/Queries/ListSubjectBlocks/` | Query + handler (by grade or by group). Not cache-stamped or short-TTL cached — blocks are written rarely, read rarely; no stale-window risk. |
| 11 | `src/Students/SchoolCollab.Students.Core/CQRS/TopicAssignments/TopicAssignmentPeriodValidator.cs` | New block validators (retired-period exclusion, active-year rule, group-span rule) beside the existing FR-56/57 methods. |
| 12 | `src/Students/SchoolCollab.Students.Core/CQRS/TopicAssignments/Queries/ListGradeTopicAssignments/ListGradeTopicAssignmentsHandler.cs` | Exclude topics blocked on the active period from the returned array; cache key gains the active period id (see (i)). |
| 13 | `src/Students/SchoolCollab.Students.Core/CQRS/TopicAssignments/Queries/ListActivityGroupTopicAssignments/ListActivityGroupTopicAssignmentsHandler.cs` | Same exclusion + cache-key change for the group path. |
| 14 | `src/Students/SchoolCollab.Students.Core/CQRS/TopicAssignments/Queries/ListGradeTopicCurriculumByGrade/ListGradeTopicCurriculumByGradeHandler.cs` | Same exclusion + cache-key change (curriculum view). |
| 15 | `src/Students/SchoolCollab.Students.Core/CQRS/TopicAssignments/Commands/UpdateTopicAssignmentPeriod/UpdateTopicAssignmentPeriodHandler.cs` | Deprecate only — no behavioural change; comment + keep handler (its tests may gain a "still works but deprecated" note). |
| 16 | `src/Students/SchoolCollab.Students.Core/CQRS/TopicAssignments/Commands/AssignGradeTopic/AssignGradeTopic.cs` | `PeriodId` param deprecated (accepted, ignored) — comment; handler may stop persisting it. |
| 17 | `src/Students/SchoolCollab.Students.Core/CQRS/TopicAssignments/Commands/AssignActivityGroupTopic/` (`AssignActivityGroupTopic.cs` + `AssignActivityGroupTopicHandler.cs`) | GROUP write path deprecated (P1-4): `PeriodId` accepted and ignored; deprecation comments on command (`:11`) and handler (`:23-24` validation, `:40` persistence, `:31-32` duplicate guard unchanged). |
| 18 | `src/Students/SchoolCollab.Students.Core/CQRS/Topics/Commands/CreateTopicForGrade/CreateTopicForGrade.cs` | FOURTH period write path deprecated (new P1, see (e)): `Guid? PeriodId = null` param (`:22`) stays on the contract for back-compat, **accepted and ignored**; deprecation comment on the command. |
| 19 | `src/Students/SchoolCollab.Students.Core/CQRS/Topics/Commands/CreateTopicForGrade/CreateTopicForGradeHandler.cs` | Handler stops persisting `PeriodId` (`:127`): retires the FR-57 `ValidatePeriodAsync` call (`:46-48`) and the `PeriodId`-scoped duplicate predicate (`:119`) to dead code with a comment on the `ix_topic_assignments_tenant_grade_topic_unique` one-row-per-(grade, topic) reality; the `:111-115` "multiple bridge rows per (grade, topic)" comment is corrected in the same pass. |
| 20 | `src/Students/SchoolCollab.Students.Core/CQRS/Periods/Commands/DeletePeriod/DeletePeriodHandler.cs` | NEW pre-mutation guard (P2-10, see (d)): blocking `SubjectEnrollmentBlock` rows on a to-be-deleted Draft period → `PeriodNotDeletableException` (422) naming the block, instead of a raw FK/500. |
| 21 | `src/Students/SchoolCollab.Students.Core/Domain/Exceptions/DuplicateSubjectBlockException.cs` | NEW — 409 mapping, mirroring `DuplicateTopicAssignmentException`. |


**Students.Api**

| # | Path | Change |
|---|---|---|
| 22 | `src/Students/SchoolCollab.Students.Api/Endpoints/SubjectBlockRoutes.cs` | NEW extension method `MapSubjectBlockRoutes(this RouteGroupBuilder group)` (endpoint-grouping rule) mapping the four `/students/subject-blocks` routes. |
| 23 | `src/Students/SchoolCollab.Students.Api/StudentEndpoints.cs` | Add `.MapSubjectBlockRoutes()` to the students group chain (`:12-32`). **`Program.cs` is NOT touched** — see the P1 security finding in (e). |
| 24 | `src/Students/SchoolCollab.Students.Api/Endpoints/TopicAssignmentRoutes.cs` | Deprecation comments on the `PUT …/period` route (:88); no route removed. |

**Students.Application (UI)**

| # | Path | Change |
|---|---|---|
| 25 | `src/Students/SchoolCollab.Students.Application/Services/StudentsApiClient.cs` | Add `ListSubjectBlocksAsync` / `BlockSubjectAsync` / `RemoveSubjectBlockAsync`; `ListGradeTopicsByGradeAsync` (:1580) unchanged. |
| 26 | `src/Students/SchoolCollab.Students.Application/Components/Students/TopicEditDialog.razor` (+ `.razor.css`) | New **"Enrollment exceptions"** section (replaces nothing in this dialog — it never had periods): list of blocked periods for this grade, each deletable, add-affordance; picker excludes `Archived`/`Deactivated`; blocks on retired periods render read-only with an archived marker **and stay deletable**; empty state is normal (no warning). Uses `Error="@Error"`. `DialogSize.Large` unchanged. |
| 27 | `src/Students/SchoolCollab.Students.Application/Components/Students/TopicCreateDialog.razor` | **Period picker REMOVED** (decision in (e)): picker `:109-118`, model `:258`, `PeriodId` sends `:544`/`:554` gone; both create paths now build period-less bridge rows. Other internals untouched. |
| 28 | `src/Students/SchoolCollab.Students.Application/Components/Students/TopicPeriodsEditDialog.razor` | **DELETED** in phase 2 (after the owner decision (h)) — replaced by the exceptions section. Its per-row period-PUT save logic dies with it. |
| 29 | `src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Detail.razor` | Card meta line derives from blocks (no period on the bridge any more); "Edit periods" kebab rewires to the exceptions section (opens `TopicEditDialog`); two-term dictionary shape follows the block model. |
| 30 | `src/Students/SchoolCollab.Students.Application/Components/Students/GradeTopicsDialog.razor` | Period column/labels switch from bridge `PeriodId` to blocks; "Edit periods" entry point rewired (see (h) — grade-detail-side entries are NOT the blocking decision). |
| 31 | `src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Subjects/Subjects.razor` | **BLOCKING owner decision (h)** — landing "Edit periods"/"Delivery periods" handling is decided by the owner before phase 2; until then this file is untouched. |

**Assignments.Core / Assignments.Api** — **no production changes. Zero.** (Verified: the client
already reduces with `Any(d => d.TopicId == topicId)`.)

**Tests**

| # | Path | Change |
|---|---|---|
| 32 | `tests/SchoolCollab.Students.Tests.Unit/SubjectEnrollmentBlockTests.cs` | NEW — entity invariants (exactly-one-owner, non-null period, no status). |
| 33 | `tests/SchoolCollab.Students.Tests.Unit/BlockSubjectHandlerTests.cs` | NEW — validator matrix (retired not blockable, draft/active/completed ok, FR-56 spans, FR-57 active year), duplicate → 409. |
| 34 | `tests/SchoolCollab.Students.Tests.Unit/SubjectAvailabilityTests.cs` | NEW — bridge+no-block ⇒ available; bridge+block ⇒ not; no bridge ⇒ not regardless; **post-round rollover regression guard** (AC-1); archived-period block stays listable AND deletable. |
| 35 | `tests/SchoolCollab.Students.Tests.Unit/ListGradeTopicAssignmentsCacheKeyTests.cs` | NEW — proves the cache key gains the active period id (see (i)). |
| 36 | `tests/SchoolCollab.Students.Tests.Unit/DeletePeriodBlockGuardTests.cs` | NEW — deleting a Draft period carrying a block ⇒ 422 naming the block, not a raw FK 500 (P2-10). |
| 37 | `tests/SchoolCollab.Admin.Tests.Unit/TopicEditDialogTests.cs` | Extend — exceptions section bUnit tests (empty state, picker exclusions, archived-block marker + delete, duplicate prevented). |
| 38 | `tests/SchoolCollab.Admin.Tests.Unit/TopicPeriodsEditDialogTests.cs` | **Deleted** (dialog retired) — the two-term premise tests die with it; any still-relevant rule (e.g. retired periods not selectable) moves to the new tests. |
| 39 | `tests/SchoolCollab.Admin.Tests.Unit/GradeLevelDetailPageTests.cs` | Rewrite `Detail_TopicsCard_SubjectRunningInTwoTerms_ShowsBothPeriods_AndViewAllDoesNotCrash` (and the period-chip assertions) to the block model — the two-bridge-row seed is unreachable in the DB. |
| 40 | `tests/SchoolCollab.Admin.Tests.Unit/SubjectsLandingPageTests.cs` | Rewrite the period-column and `EditPeriods_OpensEditorWithOneRowPerBridgeRow` tests to the block model / owner decision (h) outcome. |

Nothing else. If the worker finds a needed file not on this list, it reports it in the Worker
Report rather than silently widening scope. (Exception: if the (h) owner decision is option 2,
two conditional UI files join — see (h).)

### (h) OPEN OWNER DECISION — BLOCKING (do not decide in this plan)

The spec folds period editing into `TopicEditDialog` as a section and retires
`TopicPeriodsEditDialog`. Three entry points reach the old dialog today:

1. `GradeLevels/Detail.razor` (grade-detail Subjects card kebab) — **fine**: the card's primary
   affordance already opens `TopicEditDialog`; the exceptions section lands there. No capability
   lost.
2. `GradeTopicsDialog.razor` ("View all subjects" from the grade card) — same grade-detail
   surface; its "Edit periods" entry rewires to `TopicEditDialog`. No capability lost.
3. **`Subjects/Subjects.razor` (the Topics landing)** — **the problem.** The landing has **NO
   route to `TopicEditDialog`**: its edit dialog is `SubjectEditDialog`, a rename-only surface
   with no owner/grade context. Deleting its "Edit periods" action removes a working capability
   the owner explicitly requested earlier (pitfall doc §7 item 6, Gap 7).

The options, with trade-offs — **the owner must choose before phase 2; this plan does not pick:**

| Option | What it means | Trade-offs |
|---|---|---|
| **(1) Add a `TopicEditDialog` route from the landing** | The landing kebab opens `TopicEditDialog` with the row's `GradeLevelId`; the exceptions section is reachable. | Single edit surface, consistent with option 2 elsewhere. Cost: two edit dialogs coexist on one surface (`SubjectEditDialog` for rename, `TopicEditDialog` for blocks) — the Gap-4-style asymmetry returns in a new shape; `TopicEditDialog` must render correctly when opened with blocks-only intent. |
| **(2) Keep a standalone blocks dialog on the landing** | A new small `SubjectBlocksDialog` (or a thin wrapper over the exceptions section) reachable from the landing kebab only. | Preserves today's UX (edit where you see it), no route surgery. Cost: a second component to maintain, and the section exists in two places unless the section is extracted — extraction the lead spec explicitly *withdrew* (§5). **Files (previously unlisted):** this option requires `SubjectBlocksDialog.razor` + `SubjectBlocksDialog.razor.css`; if the owner picks option 2, both join the (g) list and the count becomes 42 before phase 2 starts. |
| **(3) Accept deletion; landing becomes view-only for periods** | The "Edit periods" action is removed; the "Delivery periods" column is dropped or repurposed to show blocked periods read-only. | Smallest diff, matches the model (landing shows the catalog; grade pages own delivery). Cost: a working, owner-requested capability is removed — the owner must accept that loss explicitly. |

Marked **BLOCKING**: phase 2 (file #28/#31 and the AC-5 "dialog gone" criterion) does not start
until the owner answers. Phase 1 (entity, migration, CQRS, API, `TopicEditDialog` section,
Detail/GradeTopicsDialog rewiring, all Students.Tests.Unit work) is **not** blocked.

### (i) ACCEPTANCE CRITERIA — each must FAIL against pre-round code

Pre-round state: there are no block endpoints and no block table, and every shipped
availability read path is period-blind (corrected spec §1). So a criterion that passes
pre-round is not evidence — such criteria are labelled **guards** (AC-1, AC-7).

| # | Criterion | Why it fails pre-round / carries weight |
|---|---|---|
| AC-1 | **Post-round regression guard — NOT a pre-round discriminator (same class as AC-7):** a subject with a bridge row and no block is **available** in the new active period after the prior year's terms are `Archived`. | Pre-round this **passes** — every availability read path is period-blind (corrected spec §1); the reviewer's P1-5 finding is closed by this relabelling. Kept because it pins the availability invariant and catches any reintroduction of period-scoped availability. The genuinely-broken counterpart — the `?periodId=` exact-match surface silently returning nothing once rows are `null` — stays out of scope; its semantics is an **owner decision** pointing at corrected spec §8 Q5. |
| AC-2 | **A block on an archived period stays listable AND deletable.** Handlers never read period status for blocks; the UI renders the archived block read-only with a marker and a working delete. | The load-bearing one: block retention is a UI/data consequence, so a **naive handler-only
test would pass pre-round and post-naive-implementation**. The bUnit half (archived block
renders + delete succeeds) plus a handler test (delete succeeds after `ArchivePeriod`) is what
discriminates. |
| AC-3 | `Archived`/`Deactivated` periods **cannot** be blocked (422); `Draft`/`Active`/`Completed` **can**. | Pre-round: no endpoint at all. Post-round, a naive validator that filters "not active" fails the `Draft`/`Completed` half. |
| AC-4 | Duplicate `(owner, topic, period)` rejected with **409**, not a second row. | Pre-round: 404. Post-round, without the filtered unique index + handler check: silent second row. |
| AC-5 | `TopicPeriodsEditDialog` **deleted**; all its entry points rewired or removed per the (h) decision; **no UI code path issues the deprecated `PUT /topic-assignments/{id}/period`** (bUnit ScriptedHandler asserts zero PUT calls to the period endpoint), **and `TopicCreateDialog` no longer sends `PeriodId` on either create path** (picker removed — see (e)). | Pre-round: the dialog exists, the editor's save path issues the PUT, and the create dialog sends `PeriodId` (`:544`/`:554`). |
| AC-6 | FR-58 behaviour via the unchanged port: publishing an assignment whose subject is blocked on the active period is **rejected** with the server's own message. | Pre-round: blocks don't exist, so nothing is excluded — the assignment would publish. |
| AC-7 | Strands/lessons diff is **EMPTY** (guard, not a pre-round discriminator — stated as such). | Prevents scope creep into `<StrandsEditor>`. |
| AC-8 | **Cache-key:** the HybridCache keys of `ListGradeTopicAssignments`, `ListActivityGroupTopicAssignments` and `ListGradeTopicCurriculumByGrade` gain the **active period id**; a test proves that with the same effective date but a different active period, the handler does **not** serve the stale cached array. | Pre-round the keys are date-only (`grade-level:{id}:effective:{date:yyyyMMdd}:…`), so a rollover/period switch within a date window serves stale availability — the test fails pre-round and fails any implementation that forgets the key change. |
| AC-9 | Build: `dotnet build SchoolCollab.slnx` — **0 errors**. | Gate (not a discriminator). |
| AC-10 | Affected test projects — **0 failures** (`dotnet test`). | Gate (not a discriminator). |

### (j) TEST REQUIREMENTS

Per `.github/copilot/rules/testing.md`: MSTest + Moq + FluentAssertions, bUnit for components,
MTP. Read `.github/copilot/rules/dotnet-best-practices.md` + its SKILL before any C#.

- **Moq must not be used for HTTP mocking** — the repo forbids it. Use the `ScriptedHandler`
  pattern (e.g. `AssignmentAiPromptPageTests.cs:33`) for HTTP-backed bUnit clients.
- **Mutation-verified criteria (name the mutation in the test's comment):**
  - **M1 → AC-6:** remove the `NOT EXISTS block(...)` predicate from the availability
    resolver — the "blocked ⇒ not available" test and the publish-rejection test must fail.
    (AC-1 is a post-round regression guard, not a mutation target — it passes pre-round by
    design.)
  - **M2 → AC-8:** drop the active-period id from a HybridCache key (revert to date-only) —
    the cache-key test must fail.
  - **M3 → AC-2:** in the UI, gate the delete control of an archived-period block out (or filter
    archived blocks from the list) — the bUnit test asserting the archived block renders AND its
    delete fires must fail.
- bUnit gotchas (pitfall doc §8): `Error="@Error"` never `Error="Error"`; `FluentSelect`/
  `FluentMenu` don't materialise children while closed — assert on bound `Items`/delegates;
  invoke row actions via `cut.InvokeAsync(...)`; never key subject collections by `TopicId`.
- Integration (only if a fixture exists for the conversion path): the §7 conversion against a
  whitelist fixture is **deferred** with outcome B (see (d)) — do not write it speculatively.

### (k) CONSTRAINTS for the worker

1. **Bin locks:** Visual Studio **PID 37940** and `SchoolCollab.Admin` **PID 43280** currently
   hold output-bin locks. `MSB3021`/`MSB3027` is a **LOCK, not a code failure** — report it as
   such. **Do NOT run `dotnet` (build or test) until the parent confirms the locks are cleared.**
2. Repo-scoped searches only — never `find /` or searches outside the working tree.
3. **Central Package Management:** all versions in `Directory.Packages.props`; **no `Version=`
   on any `PackageReference`** (NU1008/NU1009). Add a `PackageVersion` entry first if a new
   package is truly needed.
4. **All new endpoints grouped via an extension method** — never inline in `Program.cs`
   (see files #22/#23: `MapSubjectBlockRoutes` on the students group chain in
   `StudentEndpoints.cs` — wiring `Program.cs` would create an unauthenticated group, the P1
   security finding in (e)).
5. **New entity properties get domain methods, not public setters** — mirror the `TopicAssignment`
   pattern (`Create` factory + private setters).
6. Target `net10.0`; CQRS via `ICommandHandler`/`IQueryHandler` + Scrutor scanning — no MediatR.
7. `xmin` row-version concurrency convention for the new entity, per repo standard.
8. Tenant-isolated anything cached; the tenant-capture-inside-HybridCache-factory comment pattern
   in the three list handlers must be preserved when editing them.
9. Do not create the PR; do not commit unless the parent says "commit" (main-branch merge policy
   applies; the parent owns git).

### Phasing

- **Phase 1 (not blocked):** files #1–#27, #29, #30 (Students.Core #1–#21, Students.Api
  #22–#24, Application #25 + #26 + #27 + #29 + #30), tests #32–#37, #39; AC-1..4, AC-6, AC-8..10.
  (`TopicEditDialog` #26 and its test #37 are phase 1 — files #29/#30 and the AC-5 assertions
  consume them; this closes the phase-enumeration gap flagged in review. Enumeration recomputed
  after the CreateTopicForGrade amendment inserted #18/#19, shifting every later number by +2.)
- **Phase 2 (blocked on the (h) owner decision):** file #28 deletion, file #31, tests #38
  (deleted with the dialog) and #40 (rewritten to the (h) outcome), and the AC-5 UI rewiring
  assertions. If the (h) outcome is option 2, files `SubjectBlocksDialog.razor` +
  `.razor.css` join (see (h)).
- The worker report must state which phase it reached and, for (h), present the three options
  back to the parent verbatim.

---

## Worker Report

*Persisted by the parent from worker run `0e179bd4` (`ollama-cloud/deepseek-v4.1-flash`) — phase 1 part 1 of 2 (backend). The worker's own build/test numbers are discarded per the skill; the parent-authoritative numbers are below.*

**Scope reached:** files #1–#25 + backend tests #32–#36 only. No UI file (#26–#31), no UI test (#37/#39), `Program.cs` untouched.

**Step-0 measurement — OUTCOME B (non-zero):**

```sql
SELECT topic_assignment_type, grade_level_id, activity_group_id, count(*)
FROM topic_assignments WHERE period_id IS NOT NULL GROUP BY 1, 2, 3;
-- grade | 4becb21a-0b71-42b6-80f9-a5ac60b1816c | (null) | 1
-- total topic_assignments rows = 2
```

Local **dev** data only. Per §(d)'s resolved rule the migration ships **only the additive table**; the conversion is deliberately **not** written. **Parent correction:** the worker's brief said "do not author the migration" — that was the *parent's* error. §(d) requires the additive table and stops only the **conversion**; the worker correctly overrode the brief and flagged the conflict.

**Files changed.** All of #1–#25, plus **3 unlisted new files** — `DTOs/SubjectEnrollmentBlockDto.cs`, `CQRS/TopicAssignments/SubjectAvailability.cs` (shared predicate, avoids triplicating it across 3 handlers), `tests/…/StubActivePeriodProvider.cs` — and **6 unlisted test files** edited/rewritten: `PeriodDeleteHandlerTests`, `RemoveTopicAssignmentHandlerTests`, `ListGradeTopicCurriculumByGradeHandlerTests`, `UpdateTopicAssignmentPeriodTests`, `TopicAssignmentPeriodTests` (11 tests rewritten off the retired whitelist semantics), `CreateSubjectForGradeHandlerTests` (4 tests). The worker reports that §(c)'s "unreachable tests" list wrongly omitted **every** `Students.Tests.Unit` file — i.e. the plan was defective here, not the worker out of scope.

**Build/test — worker-reported (discarded):** 0 errors; Students.Tests.Unit 466/466; ArchitectureTests.Unit 70/70.

**Build/test — PARENT-AUTHORITATIVE** (`dotnet build SchoolCollab.slnx` + `dotnet test` @ the frozen tree):

| Target | Result |
|---|---|
| build | **0 errors**, 17 warnings |
| `Students.Tests.Unit` | **466 passed / 0 failed** |
| `Students.Api.Tests.Unit` | **1 passed / 0 failed** |
| `ArchitectureTests.Unit` | **70 passed / 0 failed** |

**Frozen artifact:** `documents/rounds/diffs-subject-period-blocks.patch` (5790 lines, vs base `3e408bf`, `src/` + `tests/` only; index clean).

**Open issues / risks raised by the worker:**

1. **RISK-1 — Step-0 Outcome B.** One pinned bridge row exists (dev). Additive migration shipped; the conversion is unwritten and deferred to a separately reviewed script under owner sign-off. **Owner decision.**
2. **RISK-2 — active-period granularity (unapproved interpretation, load-bearing).** The plan/spec say "the active period" *singular*, citing `EnrollStudentHandler` (which uses the active **academic year**). The worker resolves it as a **two-id set {active academic year, active sub-period}** using exact-id `Contains` (no date ranges), in both the availability predicate and the cache key — arguing year-only would leave every Term/Semester block permanently inert while sub-period-only would leave year blocks inert. **Needs confirmation**; reverting is ~10 lines across 3 handlers + 4 tests.
3. `?periodId=` surface (spec §8 Q5) and `Subjects.razor` deliberately untouched (phase 2 / owner decision (h)).
4. AC-6 satisfied structurally (zero Assignments-side change); no Assignments publish-path test added — that is Assignments-side scope.
5. `DeletePeriodHandler` guard widened to the year→sub-period cascade (`ListByPeriodsAsync`); group duplicate guard changed to topic-only. Both beyond the plan's letter, both reported rather than silently absorbed.
6. Warm-tree warning count (17) vs the briefed 97 is a build-graph artefact, not a regression.

**Blocked items:** none.

**PARENT RATIFICATIONS (2026-09-26):**

- **RISK-2 — RATIFIED (adopted, not reverted).** The diff reviewer independently ruled the two-id active-period set **correct** and the singular plan wording the looser phrase (reasoning in `## Review`). Adopted as the round's semantics. *Owner may still overrule; the revert is ~10 lines in 3 handlers + 4 tests.*
- **RISK-1 — AUTHORISED with a condition.** The additive table ships now; the dev-row conversion is deferred to a separately reviewed script under owner sign-off, exactly as §(d) prescribes. One pinned dev row exists; no availability path reads bridge `PeriodId` any more, so it is inert. *Owner ratification of the deferral still required.*
- **Parent error corrected:** this pass's brief said "do not author the migration", contradicting §(d). The worker correctly overrode it and flagged the conflict.

**Residuals recorded (not fixed this pass):**

1. **P2-2 — term blocks inert between sub-periods.** `SubjectAvailability.cs:38` returns `{activeYear}` when no sub-period is `Active`; a block on a still-`Draft` next term (AC-3 permits it) suppresses nothing until that term activates. Consistent, but user-visible. **Owner decision:** resolve the sub-period as "the active term, else the earliest `Draft`/`Active` term of the active year", or accept status-only and do not let the UI imply otherwise.
2. **P2-3 — date-scoped duplicate guard vs the date-blind unique index** (pre-existing): an *ended* bridge row for the same `(owner, topic)` passes the today-scoped guard then collides with the unique index as a raw 500. Grade path too (`CreateTopicForGradeHandler.cs:119`). Follow-up ticket.
3. **P2-5 — plan-sanctioned dead code** (`CreateTopicForGradeHandler.ValidatePeriodAsync`, uncalled, retained for greppability). Compliant; flagged so a later cleanup does not mistake it for live logic.
4. **Inherited: the three HybridCache keys carry no tenant id** (pre-existing, unchanged by this round; low practical risk — grade ids are per-tenant GUIDs). Follow-up ticket.
5. **AC-2 is only half discharged** — the handler half ships; the bUnit archived-marker/deletable half is phase 2. Do not mark AC-2 satisfied until it lands.

---

## Review

### PLAN REVIEW (step 2b) — dispatched 2026-09-26, reviewer `ollama/glm-5.3:cloud`

**VERDICT: REVISE** — 5 × P1, 6 × P2. Plan not executable as written; the worker is NOT dispatched.

**P1-1 — Step-0 SQL targets a table that does not exist.** Plan `:178` selects from
`grade_topic_assignments`; no such table exists in any migration. The bridge is the
TPH table `topic_assignments`, discriminated by `topic_assignment_type = 'grade'`
(renamed from `grade_subject_assignments` in `20260803171345…cs`). The plan's
*mandatory first step* would fail at runtime, and being grade-only it also
misses group-owned pinned rows (`AssignActivityGroupTopic` carries `PeriodId`).

**P1-2 — Entity base class cannot deliver the plan's own invariants.** `:84`/`:102`
name `BaseTenantEntity`, which supplies only `Id` + `TenantId`
(`SchoolCollab.Core/Tenancy/BaseTenantEntity.cs:10-27`) — no audit, no soft delete,
no row version. Soft delete lives on `BaseTenantEntityWithAudit`; `xmin` needs
`IHasRowVersion`. Followed literally there is no `is_deleted`, so invariant (c)3,
the `:110` index filter and file #5 are unimplementable. Mirror
`GradeAssignmentPolicy : BaseTenantEntityWithAudit, IHasRowVersion`.

**P1-3 — Wrong call-site seam; new endpoints would ship unauthenticated.**
File #15 wires in `Program.cs`, but the `/students` group, its
`RequireAuthorization` + Bearer scheme, and the route-extension chain live in
`StudentEndpoints.cs:12-32`. `Program.cs:103` has exactly one mapping call.
File #14 mirrors `MapTopicAssignmentRoutes(this RouteGroupBuilder group)`, which
cannot be invoked from `Program.cs` — wiring a fresh group there creates a second
`/students` group **without the OIDC-gated authorization**.

**P1-4 — The period write-path deprecation is half-scoped.** Only
`PUT …/period` and `AssignGradeTopicRequest.PeriodId` are deprecated, while
`TopicCreateDialog` is marked OUT yet still sends `PeriodId` on both paths
(`:544`, `:554`, picker `:118`/`:258`). The **group** write path
(`AssignActivityGroupTopic.cs:11`, handler `:23-40`, duplicate guard `:31`) is
absent from every list — so whitelist semantics stay live for group-owned topics,
contradicting (a)'s "the bridge row keeps no period meaning at all".

**P1-5 — AC-1's pre-round claim is false; the criterion is not evidence.**
Plan `:336` asserts a pinned row resolves *not available* pre-round. Every
availability read path is period-blind (see the corrected spec §1). The one
surface that does fail is the `?periodId=` exact match, which the plan neither
claims nor changes — so AC-1 passes pre-round, violating the plan's own rule at
`:330`, and the genuinely-broken surface stays broken post-round.

**P2s.** #6 exhaustive list omits `SubjectEnrollmentBlockConfiguration`,
the repository pair, and the `Extensions.cs` DI line (Strict entities need
`TenantEntityTypeConfigurationBase<T>`); #7 phase-1 enumeration omits file #18;
#8 block predicate lands on only 3 of 5 read paths, leaving the Assignments
Create pickers block-unaware — unstated; #9 `GET /students/subject-blocks` row has
no `topicId`, so it cannot answer its stated purpose; #10 a block on a `Draft`
period makes that period undeletable and surfaces as a raw Postgres 500, not 422;
#11 option 2 of §(h) needs an unlisted `SubjectBlocksDialog` + `.razor.css`.

**Judgement-call rulings.** 2 (DB-level RESTRICT), 3 (landing gating — real, with
the two P2 dents above), 5 (AC-7 correctly labelled a guard) and 6
(`DuplicateSubjectBlockException`) **upheld**. 1 (additive-only migration + STOP)
**upheld**; its "rollover bug still live" framing is inaccurate per P1-5. 4
(soft delete) sound **only after** the P1-2 base-class fix makes the `is_deleted`
filter possible.

**Roster line 1: accurate.** No stale single-model deviation claim.

**Next action (≤1 plan-review iteration):** orchestrator revises `## Plan` against
the five P1s; the reviewer then re-reads only the revised sections. Worker stays
blocked.

### Diff review (post-worker) — per-role override reviewer `openrouter/stealth/space-bunny-alpha` (thinking high)

**VERDICT: P1s** — 1 × P1, 5 × P2, 0 × P0. The P1 is an **evidence gap in the round, not a code defect**; no shipped line blocks a build.

**P1-1 — AC-6 (FR-58 via the unchanged port) has no test at all.** Plan §(j) M1 requires removing the `NOT EXISTS block(...)` predicate to break *"the 'blocked ⇒ not available' test **and the publish-rejection test**"*; the second does not exist. Every existing `ITopicAssignmentLookup` test stubs the port and cannot see a block. The mechanism is verified by reading (`TopicAssignmentLookupHttpClient.cs:52/68` → `TopicAssignmentRoutes.cs:23-31`/`:33-41` → the two filtered handlers), so AC-6 is **argued, not proven** — indistinguishable from pre-round code by any test. Fix: one `ApiFactory`-backed integration test at the exact feed point the port consumes.

**P2-1** — `BlockSubjectHandlerTests.cs:271-273` overclaims: the unit suite runs EF **InMemory** (`StudentsTestScope.cs:36`), which enforces neither unique indexes nor `HasFilter`, so the test passes with or without the merged filter. The invariant **is** satisfied (migration + snapshot), but the test is not its evidence.
**P2-2** — Term/semester blocks go inert in the gap between sub-periods: `SubjectAvailability.cs:38` yields `{activeYear}` whenever no sub-period is `Active`, and `GetActiveSubPeriodAsync` (`ActivePeriodProvider.cs:62-92`) filters on status, not date. A Term block on a still-`Draft` next term (which AC-3 explicitly permits) suppresses nothing. Consistent, but user-visible ("I blocked Term 2 and the subject still shows"). **Owner decision.**
**P2-3** — `AssignActivityGroupTopicHandler.cs:31-33` still guards on rows effective *today*; an ended bridge row for the same `(group, topic)` passes it and then collides with the date-blind `ix_topic_assignments_tenant_group_topic_unique` → raw 500. Same shape on the grade path (`CreateTopicForGradeHandler.cs:119`). **Pre-existing**, not introduced here. Follow-up.
**P2-4** — Step-0 Outcome B: the STOP's *ratification* is the parent's to record (now done below). The reviewer's judgement: the worker's reasoning was *technically sound, procedurally incomplete* — the additive table was genuinely required by `MigrationGuardTests.NoUncommittedModelChanges`, and the leftover dev row is semantically inert.
**P2-5** — Plan-sanctioned dead code (`CreateTopicForGradeHandler.ValidatePeriodAsync`, retained uncalled so the retired rule stays greppable). Compliant; noted so a later cleanup does not read it as live.

**risk2Ruling: CORRECT — adopt, do not revert.** The two-id set is the right reading and the singular "the active period" is the *looser* phrase, not a stricter contract: `IActivePeriodProvider.GetActivePeriodAsync:30` documents the active **AcademicYear**, so year-only would make every Term block permanently inert (contradicting AC-3/FR-56/FR-57); sub-period-only would make year blocks inert and strand `EnrollmentSpan.WholeAcademicYear` groups, which *require* a year period (`TopicAssignmentPeriodValidator.cs:196-201`). Exact-id with no date ranges is what the spec demands. Cross-term contamination is structurally impossible — the set is one year plus one sub-period **of that year** (`ActivePeriodProvider.cs:78-84`), and predicate + cache key are both derived from the same `Guid[]` (`SubjectAvailability.cs:88`, `:46`), so they cannot drift.

**deviationRulings: all legitimate.** The three unlisted production files (`SubjectEnrollmentBlockDto`, `SubjectAvailability`, `StubActivePeriodProvider`) are additive and justified; the 6 unlisted test files are **compile-forced** (three handler constructors changed) or are retired-whitelist-semantics rewrites — and the worker's claim that §(c) omitted every `Students.Tests.Unit` file is **substantively correct**.

**AC evidence audit:** AC-1 **guard, honestly labelled** ✅ · AC-2 **partial — handler half only**; the bUnit archived-marker half is phase 2, so AC-2 must not be marked satisfied · AC-3 **real discriminator** ✅ · AC-4 real for the handler path, **index path unproven** (P2-1) · AC-6 **unproven** (P1-1) · AC-8 **strongest in the round** (4 discriminators + invalidation) ✅ · AC-9/10 gates (parent run is the evidence).

**scopeGateCheck: all PASS** — no UI files, no UI tests, `Program.cs` untouched, no new package / no `Version=`, no cross-context reference, no new base address, no AppHost change, zero `src/Assignments` change.

**securityPosture: clean.** The P1-3 seam is closed **by file**, not convention: `StudentEndpoints.cs:31` appends `.MapSubjectBlockRoutes()` to the existing `studentsGroup` chain (`:12`, `RequireAuthorization` + `BearerScheme` at `:14-19`); `MapGroup("/students")` has exactly two hits, the other pre-existing and untouched. **Inherited residual (not introduced here):** the three HybridCache keys still carry **no tenant id** — equally true pre-round; follow-up ticket, low practical risk since grade ids are per-tenant GUIDs.

**Unlisted changed files ruled legitimate:** production — `DTOs/SubjectEnrollmentBlockDto.cs`, `CQRS/TopicAssignments/SubjectAvailability.cs`; tests — `StubActivePeriodProvider.cs` + 6 edited test files. `documents/specs/subject-period-exception-model.md` is the round's **input**, not an output, but must be committed alongside the code or the two desync.

**Follow-ups to open after this round:** (a) tenant-less HybridCache keys; (b) the date-scoped duplicate-guard hole vs the date-blind unique index (P2-3); (c) the now-dead `?periodId=` landing surface (spec §8 Q5).

### Rework pass + re-verification — phase 1 part 1 **CLOSED**

**Rework (run `25d2262b`, test-only, zero production change).** Two additions in `tests/SchoolCollab.Students.Tests.Integration/SubjectBlockAvailabilityEndpointTests.cs`:
- `ByGradeFeed_BlockOnActiveAcademicYear_ExcludesTopic` — closes **P1-1**. Control GET first (no block ⇒ topic present), then `POST /students/subject-blocks` ⇒ topic **absent**, against `GET /students/topic-assignments/by-grade/{id}?effectiveDate=…` — the exact array `TopicAssignmentLookupHttpClient.cs:50-53` reduces.
- `RemovedBlock_CanBeReAdded_ForTheSameOwnerTopicPeriod` — closes **P2-1**: POST 201 → DELETE 204 (soft) → POST the same `(owner, topic, period)` ⇒ 201 with a **new** id, on real Postgres (Testcontainers), which is the only place the merged `HasFilter` can be observed.
- `BlockSubjectHandlerTests.cs` — test renamed to `…_DuplicateGuardIgnoresSoftDeletedRows` and its comment reworded to claim only the handler half, explicitly disclaiming index/`HasFilter` evidence.

**Worker mutation evidence (both mutations reverted by the worker):** forcing `BlockedTopicIdsAsync` to `return []` failed the feed test while the control passed; dropping `is_deleted = false` from the migration filter failed the re-add test with `Npgsql.PostgresException: 23505` on `ix_subject_enrollment_blocks_tenant_grade_topic_period`. The reviewer assessed both outcomes as supported by the code as written.

**Re-verification (run `61a96f83`, `space-bunny-alpha` high): `VERDICT: CLOSED`.** P1 and P2 both closed; **no new findings**; production change in the rework: **none**. Verified in particular that the route→handler mapping is 1:1 with the client's request, that the control is non-vacuous (a dead seed would fail the control, not pass the absence assertion), that pre-round the POST is a 404, and that `ApiFactory` runs `postgres:16-alpine` with `MigrateAsync` so indexes and filters are genuinely enforced. Two non-blocking observations: the new class shares `TestTenantA` (safe — `TestInitialize` truncates and clears the cache tag, class is `[DoNotParallelize]`), and the feed test has no negative control for a non-active-period block (additive coverage, not a claimed AC).

**PARENT-AUTHORITATIVE at the frozen tree** (`dotnet build SchoolCollab.slnx` + `dotnet test`): build **0 errors** · `Students.Tests.Integration` **62/62** · `Students.Tests.Unit` **466/466** · `ArchitectureTests.Unit` **70/70**. Frozen artifact re-frozen: `diffs-subject-period-blocks.patch`, 6027 lines vs `3e408bf`, index clean.

### Diff review — phase 1 part 2 (UI half) — run `474e9823` (`space-bunny-alpha`, high)

**VERDICT: CLOSED** — 0 P0, 0 P1, 6 P2 (all polish or doc drift). **Phase 1 is code-complete and reviewed.**

**AC audit.** **AC-2 UI half — real discriminator, not vacuous** (`TopicEditDialogTests.cs:452-486`): the `DELETE` handler clears a `live` flag so the later `Contain("No exceptions")` wait only holds if the request fired; `handler.Calls` asserts the request **reached the client seam**; and the control must be enabled. Mutation-sane: removing the badge breaks the `aria-label` lookup, gating the delete out breaks the `"2019 Academic Year"` wait. **AC-5 (create-dialog half) — discharged**: the picker markup, `Model.PeriodId`, `FilterPeriodsForGrade/Group`, the `/students/periods` loads and **both** send sites are gone; the replacement tests assert `FindAll("#topic-create-period")` is empty **and** that no `/students/periods` call was made — strictly stronger than a markup check. **AC-5 (PUT half) — still OPEN, by plan**: `TopicPeriodsEditDialog.razor:313` still issues the PUT and is still reachable from `Subjects.razor:481`; phase 2 per §Phasing. **AC-5 must not be marked satisfied at acceptance.**

**Vacuous-pass audit — all four named risks cleared:** picker assertions read bound `Instance.Items` and drive `SelectedOptionChanged`; the "excludes retired/already-blocked" test **positively asserts** `ActiveYearId`/`Term2Id` are present first, so an empty picker fails; "add with no selection posts nothing" passes for two independent reasons (disabled attribute + the guard); "section hidden without a grade" cannot pass on a pending load, and both `handler.Calls` negatives pin the FR-ED-16 "pays nothing" claim.

**The rewritten two-term test is real, not hollowed.** One bridge row (was two) + two blocks, matching the filtered unique index. The rewrite **keeps** the crash guard in its new shape: `Detail.razor:728-730` uses `.GroupBy(b => b.TopicId)` (a flat `ToDictionary` would throw on the two blocks' duplicate `TopicId`), the test seeds exactly that, and a source assertion pins the `.GroupBy`. Card and View-all both assert `Blocked in Term 1 · Term 2 (archived)`, so the `BlocksByTopicKey` wiring is verified end-to-end.

**Deviations all ruled legitimate.** `SubjectBlockLabels.cs` (3 consumers must render **byte-identical** text); the `TopicCreateDialogTests.cs` edits (compile-forced + strictly stronger replacements); picker-side duplicate prevention is **sufficient** because the DB filtered unique index is the real gate — a stale picker produces exactly one outcome, a POST → 409 → server message, proven end-to-end. No AC or spec text depends on the retired "Edit periods" labels (zero remaining referents in `src/`+`tests/`).

**Scope gates: all PASS** — #28/#31/#38/#40 untouched, no `Program.cs`, no `src/Assignments`, no `.csproj`, conventions clean (`Error="@Error"`, no inline `<style>`, `::deep` where required).

**P2s (6, non-blocking):**
1. `TopicEditDialog.razor` applies `Class="exceptions-retired"` but **no such rule exists** in the `.razor.css` — a silent no-op, exactly what dialog-ui §3 warns about. *(code — polish pass)*
2. `SubjectBlockLabels.NoExceptions` (`"Offered in every period"`) renders for **every** unblocked subject yet **no test asserts it**. *(code — polish pass)*
3. `documents/solution/subject-topic-delivery-periods.md` still documents the old "Edit periods" kebab and delivery-period chip on `Detail.razor`/`GradeTopicsDialog.razor`. *(doc — parent)*
4. `_blockablePeriods` is a computed property rebuilding+re-sorting a new array **every render**, handed to `FluentSelect.Items` each time — needless churn and a latent trap. *(code — polish pass)*
5. The dialog badge renders the raw enum `"Archived"`/`"Deactivated"` while the card renders `"Term 2 (archived)"` — same fact, two spellings. *(code — polish pass)*
6. Spec FR-ED-13 says the UI **"greys out"** already-blocked periods; the implementation **removes** them (FluentUI `FluentSelect` has no per-option disabled state). Amend the spec line. *(doc — parent)*

**P2 disposition (2026-09-26):**

- **Items 3 and 6 — FIXED by the parent.** A partially-superseded banner was added to `documents/solution/subject-topic-delivery-periods.md` (leaving §5 item 6, the landing, marked as still-accurate-until-phase-2); FR-ED-13 in the spec was amended from "greys out" to **"omits already-blocked periods from the picker"**, with the 409 recorded as the stale-list backstop.
- **Items 1, 2, 4, 5 — REMAIN OPEN, accepted as non-blocking residuals.** A polish pass (`7d25b144`) was dispatched for exactly these four and **timed out at the 30-min cap having produced no file changes at all** — `step.failed`, `exitCode 1`, `durationMs 3,401,077` (~57 min) with only **2,134 output tokens**, i.e. the test-output-starvation shape the skill's pitfalls warn about, not a code failure. The working tree was verified byte-identical to the CLOSED state afterwards and builds **0 errors**, so both CLOSED verdicts above still stand over the current tree.
- **No further polish loop was run.** A code edit now would invalidate both CLOSED reviews and require re-verification — a poor trade for four cosmetic/coverage items, and the skill says surface residuals rather than loop at the bound. **The owner may direct a re-attempt;** a build-only brief (parent runs the tests) removes the starvation cause entirely.
- The four open items, for reference: (1) dead `exceptions-retired` class; (2) `NoExceptions` default label unasserted; (4) `_blockablePeriods` per-render churn; (5) badge/card spelling mismatch. All four are one-line-to-small fixes.

**Residuals:** AC-5 PUT half open (phase 2); **group-owned blocks have no UI** (`TopicEditDialog` always posts `ActivityGroupId: null`), so FR-56's group-span rule is API-reachable only — gated behind §(h); part 1's P2-2 (term blocks inert between sub-periods) is now *more* visible, since the UI lets a user block a `Draft` next term while the card won't reflect it until that term activates.

**PARENT-AUTHORITATIVE at the frozen tree:** build **0 errors** · `Admin.Tests.Unit` **575/575** · `Students.Tests.Unit` **466/466** · `ArchitectureTests.Unit` **70/70**. Patch re-frozen, 7785 lines.

---

## Acceptance

*(stub — orchestrator acceptance)*

- Criteria satisfied:
- Residual risks:
- Round artifacts:

---

## UI Tester

*(stub — bug-hunt of the delivered UI; EXPECTED this round per the traceability line)*

- Surfaces covered:
- Defects found:

---

## Next round — fixes and misses (prepared 2026-09-26)

Consolidated from the phase-1 CLOSED reviews, the spec-compliance audit, and the owner
decision log. Nothing here is speculative; every item is traceable to a review finding,
a spec clause, or a measured fact. **This round cannot close until §A.1 and §C are done.**

### §A — BLOCKING: owner decisions (nothing below may start without these)

| # | Decision | Options | Consequence if unanswered |
|---|---|---|---|
| **A.1** | **§(h) Topics landing** | **✅ SETTLED 2026-09-26** — see *Owner decisions — settled* below. Resolution: a landing-owned, grade-scoped `SubjectBlocksDialog`; grade-detail keeps a read-only label and loses its editor. | Resolved. |
| **A.2** | **P2-2 term-gap semantics** | **(a)** resolve the sub-period as *"the active term, else the earliest `Draft`/`Active` term of the active year"* · **(b)** keep status-only and stop the UI implying otherwise | `SubjectAvailability.cs:38` returns `{activeYear}` whenever no sub-period is `Active`, so a block on a still-`Draft` next term — which **AC-3 explicitly permits** — suppresses nothing until that term activates. Directly affects the *"block Term 3"* use case. |
| **A.3** | **RISK-1 ratification** | Ratify *"additive migration ships now; the pinned dev row's conversion is deferred to a separately reviewed script under owner sign-off"* | Step-0 measured **non-zero** (1 pinned grade bridge row, dev). §7's conversion + its integration test remain unwritten. |
| **A.4** | **Spec §8 Q5 — the `?periodId=` surface** | **(a)** remove the parameter + its `Subjects.razor` plumbing *(recommended — a period filter contradicts the whole inversion)* · **(b)** make it block-aware · **(c)** leave it dead | Post-round **every** row carries `PeriodId = null`, so an explicit `periodId` filter returns nothing. The surface is already dead; this decides whether it is deleted or resurrected. |
| **A.5** | **UI-tester timing** | Run now on the part-2 surface, or once after phase 2? | Needs the **AppHost running**, so it cannot overlap a build. Waiting lets one pass cover the whole round. |

### §B — READY NOW: no decision required (4 cosmetic P2s, 1 component + 1 shared helper + 1 test file)

All from the part-2 review; the polish pass `7d25b144` timed out having produced **zero** changes (see §F). File count is unchanged; nothing here is behavioural except P2-4.

| # | Item | Fix | Caution |
|---|---|---|---|
| **B.1** | `TopicEditDialog.razor` applies `Class="exceptions-retired"` to the archived badge, but **no such rule exists** in `TopicEditDialog.razor.css` — a silent no-op (exactly what dialog-ui §3 warns about) | add the rule (e.g. dim) **or** drop the attribute | pick one; do not leave a class with no rule |
| **B.2** | `SubjectBlockLabels.NoExceptions` (`"Offered in every period"`) is what **every** unblocked subject renders on the card meta and in the View-all dialog, and **no test asserts it** | one card test with an empty blocks payload asserting `Contain("Offered in every period")`, or a direct unit test over `SubjectBlockLabels.Describe([])` | — |
| **B.3** | `_blockablePeriods` is a computed property rebuilding **and re-sorting a new array on every render**, handed to `FluentSelect.Items` each time | memoise against `_blocks` / `_periods` / `_activeYearId`, or filter once inside `LoadBlocksAsync` | **The two select-then-click tests** (`Exceptions_BlockPeriod_PostsTheBlock_AndReloadsTheList`, `Exceptions_DuplicateRejectedByTheServer_ShowsTheServerMessage`) depend on this exact path. A failure there is a **real defect, not flake** — fix forward, never weaken the test. |
| **B.4** | The dialog badge renders the raw enum (`"Archived"`/`"Deactivated"`) while the card renders `"Term 2 (archived)"` — same fact, two spellings, badge also untranslated | route the badge through `SubjectBlockLabels` (e.g. `DescribePeriods(new[]{ block })[0]`, or a small `RetiredLabel`) | do **not** change the *card* wording — it is asserted by tests |

**Plus, already closed by the parent (no action):** P2-3 (superseded banner in
`documents/solution/subject-topic-delivery-periods.md`) and P2-6 (spec FR-ED-13 reworded
to "omits … from the picker").

### §C — PHASE 2 (gated on A.1) — 4 base files, 6 with option 2

| # | Path | Change |
|---|---|---|
| 28 | `src/Students/SchoolCollab.Students.Application/Components/Students/TopicPeriodsEditDialog.razor` | **DELETE** (replaced by the exceptions section) |
| 31 | `src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Subjects/Subjects.razor` | landing rewiring per the A.1 outcome |
| 38 | `tests/SchoolCollab.Admin.Tests.Unit/TopicPeriodsEditDialogTests.cs` | **DELETE** with the dialog (its two-bridge-row premise is unreachable) |
| 40 | `tests/SchoolCollab.Admin.Tests.Unit/SubjectsLandingPageTests.cs` | rewrite the period-column + `EditPeriods_OpensEditorWithOneRowPerBridgeRow` tests to the block model / A.1 outcome |
| opt 2 | `SubjectBlocksDialog.razor` + `.razor.css` | conditional on A.1 = option 2 (count 40 → 42) |

**Discharges:** AC-5's PUT half — after this, **no** UI path issues
`PUT /topic-assignments/{id}/period`, and `TopicPeriodsEditDialog.razor:313` (the only
remaining issuer) is gone.

### §D — Spec gaps: amendments needed, not code

Found by the spec-compliance audit (45 requirements checked, **43 met**).

| # | Gap | Action |
|---|---|---|
| **D.1** | **§7 conversion + §9's integration test for it — NOT DONE.** Step 0 returned non-zero, so the conversion is deferred by design. §9 nonetheless requires: *"the §7 conversion script against a fixture containing a whitelist row in two terms, asserting the subject is available in neither **other** term and **is** available in the original two."* | after A.3 ratifies, author the script (null the `PeriodId`, insert sibling blocks), review row-by-row, then add the §9 integration test. Note §7 says apply behind `FEATURE:UseSubjectPeriodExceptions` while §6 forbids a flag — the plan resolved this toward **no flag**, so the script is operator-run, not runtime-gated. |
| **D.2** | **§9 FR-58 wording.** The spec asks for *"rejected **at publish** with the server's own message"*. What is proven is the **feed** contract — `ByGradeFeed_BlockOnActiveAcademicYear_ExcludesTopic` hits the exact endpoint `TopicAssignmentLookupHttpClient.cs:50-53` reduces, mutation-verified. The publish call itself is Assignments-side and the zero-`src/Assignments`-change gate forbids it. | either amend §9 to "excluded from the by-grade/by-group feed the unchanged port consumes", or accept with a recorded note. **Do not** quietly close AC-6 as fully met. |
| **D.3** | **RISK-2 is implemented but the spec still says "the active period" (singular).** §2's availability clause and the `EnrollStudentHandler` citation both imply a single id; the delivered code uses `{active academic year, active sub-period}`. | amend the spec to the two-id set so the source of truth matches the build. Reviewer independently ruled the delivered reading correct (year-only strands all term blocks; sub-period-only strands all year blocks, including `EnrollmentSpan.WholeAcademicYear` groups). |
| **D.4** | §6 places the new routes *"in `TopicAssignmentRoutes`"*; they ship as a new `SubjectBlockRoutes.cs`. | optional — the endpoint-grouping rule is satisfied either way. Amend or leave. |
| **D.5** | **Group-owned blocks are API-only.** `TopicEditDialog` always posts `ActivityGroupId: null`, so FR-56's group-span rule is unreachable from UI. | resolves under A.1, or record as an accepted v1 limitation. |

### §E — Follow-up tickets — explicitly NOT this round

- **(E.1)** The three `HybridCache` availability keys carry **no tenant id** (pre-existing; low practical risk — grade ids are per-tenant GUIDs). Follow `documents/solution/` conventions.
- **(E.2)** **P2-3 — latent 500:** the duplicate guard checks only rows effective *today*, so an **ended** bridge row for the same `(owner, topic)` passes the guard and then collides with the date-blind unique index. Grade path (`CreateTopicForGradeHandler.cs:119`) and group path (`AssignActivityGroupTopicHandler.cs:31-33`). Pre-existing, not introduced here.
- **(E.3)** Plan-sanctioned dead code: `CreateTopicForGradeHandler.ValidatePeriodAsync` (uncalled, retained for greppability). Compliant — flag so a later cleanup does not read it as live.
- **(E.4)** `Assignments.Core → Students.Core` `ProjectReference` violation. **Deliberately not bundled** into this round; recorded at `documents/solution/cross-context-rule-followups.md` §1, architect decision pending. No architecture test guards it.

### §F — Process notes for whoever picks this up

- **A polish pass must be briefed build-only.** `7d25b144` burned its 30-min cap with **2,134 output tokens over ~57 min** and **zero** file changes — the test-output-starvation shape the skill's pitfalls document, not a code difficulty. For any small pass: **worker edits + `dotnet build` only; the parent runs the tests.** The tree was verified byte-identical and green afterwards, so nothing was lost.
- **This round's recurring lesson:** the plan's *enumerations* were the weak point three separate times (missed group write path, missed create write path, §(c)'s unreachable-test list omitted every `Students.Tests.Unit` file). Derive file and test lists **by grep**, not by enumeration.
- **Any code edit now invalidates both CLOSED reviews** and needs re-verification. That is the deliberate reason §B was not simply done inline.
- Tooling notes for this environment: the default shell is **PowerShell** (`bash` tool), `grep` is **not** on the `ctx` sandbox `PATH`, and `cwd` is **ignored for non-shell languages** — use `fs` + `process.chdir` in `ctx_execute`. Patch-freeze idiom that works: `git add -N -- src tests` → `git diff <base> -- src tests > <patch>` → `git reset -q -- src tests`.
- Stale paused run `0792d0da` (stop requested twice, terminal proof never arrived) is **inert** — capacity is `0/unlimited`. Cosmetic only.
- **PRs #266 and #267 are green and `MERGEABLE`.** Merge is the owner's call and must be **sequential: #266 first** (#267's base is #266's branch). The round's branch will need a rebase onto `main` once the stack lands.

### §G — Closure state

| | |
|---|---|
| Phase 1 part 1 (backend) | **CLOSED** (`0e179bd4` + rework `25d2262b` + re-verify `61a96f83`) |
| Phase 1 part 2 (UI) | **CLOSED** (`c8fb3424` + review `474e9823`) |
| Phase 2 | **not started** — blocked on A.1 |
| `## Acceptance` | **stub** — cannot be written until phase 2 lands |
| `## UI Tester` | **stub** — see A.5 |
| Authoritative gate | build **0 errors** · `Admin.Tests.Unit` **575/575** · `Students.Tests.Unit` **466/466** · `Students.Tests.Integration` **62/62** · `ArchitectureTests.Unit` **70/70** |

**AC status:** AC-1 ✔ (guard, honestly labelled) · AC-2 ✔ **discharged** (the bUnit half is now hosted by `SubjectBlocksDialogTests` after the reversal below) · AC-3 ✔ · AC-4 ✔ handler, index proven by the Postgres re-add test · AC-5 ✔ **fully discharged** — the PUT half closed with the `TopicPeriodsEditDialog` deletion **and is now mechanically guarded** (see *AC-5 guard* below) · AC-6 ✔ at the feed, see D.2 · AC-7 ✔ · AC-8 ✔ (strongest) · AC-9/10 ✔ gates.

**PARENT-AUTHORITATIVE (final, post-reversal + post-guard):** build **0 errors** · `Admin.Tests.Unit` **570/570** · `Students.Tests.Unit` **466/466** · `Students.Tests.Integration` **62/62** · `ArchitectureTests.Unit` **72/72** — **1170 tests, 0 failures**.

---

## Owner decisions — settled (A.1 grill, 2026-09-26)

The landing decision was put through a structured grill. Four questions were put; the
owner answered with corrections that **changed the shape of the round**, so the original
three-option framing in §(h) is superseded. Recorded here rather than in §(h) so the plan
text stays a faithful record of what was planned at review time.

**Settled:**

1. **Blocks live on the Topics landing, not the grade-detail page.** A dedicated
   `SubjectBlocksDialog` (blocks-only, no-submit, immediate-write, read-only subject+owner
   header) is opened from the landing's row action, relabelled **"Enrollment exceptions"**,
   offered only for a **grade owner** — the same gate the retired "Edit periods" action used.
2. **The grade-detail card keeps a read-only block label and loses its editor.** Option (a)
   of three: `Blocked in Term 1 · Term 2 (archived)` / `Offered in every period` stay on the
   `Detail.razor` card and the `GradeTopicsDialog` row; the kebab and row action that opened
   the editor are removed. Rationale: full removal (b) would make a restricted subject look
   identical to an unrestricted one on the page an admin uses to judge a grade.
3. **The Q2 column rework is folded into this pass** — the landing's "Delivery periods"
   column becomes block-derived and read-only, killing the dead "one subject, several bridge
   rows" premise.
4. **Q7 — `#28`/`#38` ship in the same pass**, not standalone. Once the landing is being
   rewritten anyway, splitting the deletion out is pointless and would leave the landing
   briefly misleading.
5. **Q4 — grade-only; group blocks are API-only in v1.** A fact corrected during the grill:
   `Subjects.razor:542` and `:376` show the landing has *never* offered period editing for
   activity groups ("the landing claims no group periods"). So the group branch is a **new
   capability**, not a restoration, and it is deferred — written down as an explicit
   limitation so it cannot be mistaken for an oversight. FR-56 remains enforced and tested
   server-side.

**A correction this round had to make to itself:** the parent's original recommendation for
option (1) (`TopicEditDialog` from the landing) was priced as "two dialogs coexist". Reading
the code showed it is neither cheap nor complete — that dialog is a *submit* dialog
(`SubmitAsync` → `UpdateTopicAsync`, plus a `<StrandsEditor>`), and its section is gated
`HasGradeScope` on `GradeLevelId`, so it would render **nothing** for activity-group subjects.
The option was rejected on those grounds, not on taste.

**Spec impact:** spec §5 has been amended to match ("Effect on the UI — the blocks live on the
Topics landing"), including the no-submit rationale, the grade-only limitation, and why
`TopicEditDialog` was not reused. The rule set in §5 is otherwise unchanged — only the host
surface moved.

**Reversal of already-reviewed work.** Phase 1 part 2 (run `c8fb3424`, review `474e9823`,
verdict CLOSED) had built the section into `TopicEditDialog` and rewired both grade-detail
surfaces to it. That work is **relocated, not discarded** — the archived marker, the
immediate-write semantics, the picker exclusions, the empty state, the 409 surfacing and
`SubjectBlockLabels` all carry over into `SubjectBlocksDialog`. What is reverted: the section
in `TopicEditDialog` (files #26, #37) and the grade-detail editor entry points (#29, #30, and
the kebab assertion in #39). The next review must treat the part-2 CLOSED verdict as
**superseded for those files** and re-verify the relocated code.

### AC-5 guard — settled by owner decision "lean to (b)", 2026-09-26

The plan's AC-5 wanted a **bUnit** assertion that the UI issues zero `PUT /topic-assignments/{id}/period` calls. That assertion lived in `TopicPeriodsEditDialogTests.cs` and died with the dialog, leaving the invariant merely grep-verified — someone could re-wire the deprecated endpoint from a component and nothing would fail. The owner chose the stronger option over accepting the gap.

**Delivered:** `tests/SchoolCollab.ArchitectureTests.Unit/DeprecatedPeriodWritePathArchitectureTests.cs` — a source-scan guard in the repo-wide scanner project, pinning **both** failure modes of the deprecate-don't-delete posture:

1. `DeprecatedPeriodPutClient_HasNoProductionCallers` — `StudentsApiClient.UpdateTopicAssignmentPeriodAsync` may occur **exactly once** under `src/` (its definition) and nowhere else. A re-wire from any component fails the build. `bin/`/`obj/` are excluded so a stale generated artifact can never fail or mask it. Scope is production only — a test that deliberately exercises the wire-compat endpoint does not violate the invariant.
2. `DeprecatedPeriodPutEndpoint_IsRetained_AndMarkedDeprecated` — the route `/topic-assignments/{id:guid}/period` must still be mapped and the file must carry `DEPRECATED`, so deleting the endpoint outright (instead of deprecating it, spec §6) also fails.

**Method change, recorded honestly:** AC-5's invariant is now guarded by an **architecture source-scan**, not the bUnit assertion the plan named. Same invariant, different mechanism — stronger, because it covers *any* future caller rather than one dialog's call path. `ArchitectureTests.Unit` went 70 → 72 and is already part of the round's always-run gate, so the guard cannot be skipped.

**Verification:** build 0 errors; the two new tests pass; the mutation check confirms exactly one occurrence of the token in `src/`.