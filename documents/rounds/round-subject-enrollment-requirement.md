# Round 13 — subject enrolment requirement, layer 1 (data + API)

**Tier:** 2 (light round) · **Mode:** attended · **Round base:** `1c1892fd` (main, after PR #421)
**Provider profile:** pi / `ollama-cloud` · **Parent (orchestrator + acceptor):** `ollama-cloud/deepseek-v4.1-flash:1017`
**Plan gate:** `ollama-cloud/deepseek-v4-pro:0813` · **Worker:** `ollama-cloud/deepseek-v4.1-flash:1017` · **Diff reviewer:** `ollama-cloud/deepseek-v4-pro:0813`
**Spec:** `documents/specs/subject-enrollment-requirement-flag.md` · **Ephemeral:** whole folder safe to discard once the round closes

---

## Settled by the owner (rounds 13 and 14, 2026-10-26/27)

| # | Decision |
|---|---|
| Q38 | Scope = the **minimum coherent slice**, now corrected below — a flag nobody can switch is dead code. |
| Q39 + Q44 | The switch lives on **`GradeTopicsDialog`'s per-row kebab** (it already carries the bridge `AssignmentId`). The topic edit dialog is **wrong** — it edits the shared `Topic`, and one topic can be assigned to several grades with different flags (review R2). |
| Q40 | **No feature flag.** `RequiresEnrollment` defaults `false`, so behaviour is unchanged until an admin flips one subject; the default is the safety. `feature-flag-workflow.md` § "A flag is the OWNER's decision" records the rule. |
| Q41 + R1 | **One migration**, and it must **add a transient default, backfill, then DROP the default**. No `HasDefaultValue` in the model configuration — §2.2 of the spec tells a reviewer to reject that, and `GradeLevelConfiguration.cs:46-49` is the pattern that would otherwise leak in. |
| Q42 | Amber band = every `true` subject: enrolled ones as normal chips, un-enrolled struck through with "not chosen". Four bands total: Placement · Activity groups · Needs explicit enrolment · Assumed. **(Layer 2.)** |
| Q43 | **FR-ER-24 will be amended** to the shipped shape (rail bands inside the Enrollment panel), not "Subjects and Activity Groups sections in the same dialog". Deferred to the doc layer that closes this round. |
| Q45 | A struck-through "not chosen" chip is **actionable** — clicking enrols that student (`AssignStudentTopicAsync`, which already exists and is referenced by no `.razor`). **(Layer 2.)** |
| Q46 | **Two layers, Tier 2 each.** Layer 1 = this round: data + API, no UI. Layer 2 = bands + kebab switch + enrol action. |
| R3 | The eligibility endpoint **resolves the active academic year server-side**; it must not accept a caller-supplied `periodId` (FR-ER-3 says the period is server-resolved and cannot be caller-chosen). |

**Two-model round** (owner 2026-10-23): worker and reviewer/plan-gate/acceptance are the only models dispatched. No UI tester — layer 1 has no UI, and that is why layer 2 exists rather than riding along.

---

## Plan

Ordering is the review's, because the pieces are load-bearing for each other: the publish filter and the bands are both unobservable until the switch + grandfather exist.

### P1 — Domain: the flag on the TPH bridge

- `TopicAssignment` (`Core/Domain/TopicAssignment.cs`, `abstract class … : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion`): add
  `public bool RequiresEnrollment { get; private set; }` with the spec's XML doc, plus
  `public void SetRequiresEnrollment(bool value)` that **no-ops when unchanged** and bumps `UpdatedAt` otherwise (the `SetEnrollmentBlocked` shape the spec calls a "mirror" — see the drift note below, which is why the *transactional* part does not live here).
- Factories set the default **in code, never in schema** (FR-ER-2): `GradeTopicAssignment.Create` → `false`; `ActivityGroupTopicAssignment.Create` → `true`.
- **Mechanism pinned (gate F4):** `protected void Initialize(...)` (`TopicAssignment.cs:66`) gains a `bool requiresEnrollment` parameter and assigns the field directly; the two factories pass literal `false` / `true` through it. `SetRequiresEnrollment` remains the **mutation** path only. Calling the setter straight after `Initialize` would bump `UpdatedAt` a second time on a brand-new row, and would make "created with the default" indistinguishable from "someone changed it" in the audit shape.
- `TopicAssignmentDto` gains `bool RequiresEnrollment` **as the last positional member**. **Eight** construction sites exist, all positional, and **all eight** must be updated in the same change (gate N1: the draft claimed three; adding a 12th member to a positional record breaks the build at every site — criterion 13 would fail):
  1. `GradeTopicAssignmentRepository.cs:18` — project `x.RequiresEnrollment`
  2. `ActivityGroupTopicAssignmentRepository.cs:17` — project `x.RequiresEnrollment`
  3. `ListGradeTopicAssignmentsHandler` (in-memory projection)
  4. `ListActivityGroupTopicAssignmentsHandler.cs:49`
  5. `UpdateTopicAssignmentTagsHandler.cs:21`
  6. `UpdateTopicAssignmentTagsHandler.cs:25`
  7. `UpdateTopicAssignmentPeriodHandler.cs:60`
  8. `UpdateTopicAssignmentPeriodHandler.cs:64`
  Every site projects the value **from the entity**, never a literal — a hard-coded `false` compiles and passes a structural grep, which is why criterion 5 asserts the value and not just the count.

### P0 — Endpoint response convention (gate F3, one rule for all three new endpoints)

`Students.Api` uses the `Results` factory everywhere today, but `AGENTS.md` requires **`TypedResults`** for new or edited endpoints so OpenAPI emits real response schemas, and `MinimalApiCompositionArchitectureTests` deliberately does **not** enforce it (`:18`) — so this is a plan decision, not a build constraint.

**Rule: all three new endpoints (P3 `/students/{id}/subjects`, P4 `/topic-enrollment`, P6 `PUT …/requires-enrollment`) return `TypedResults`.** There is **no** `TypedResults` precedent in `src/Students` to copy — the `Guid[]` endpoint at `StudentRoutes.cs:69` returns `Results.Ok(...)` (`:76`), and grep finds zero `TypedResults` in the project (gate N2: my earlier citation was wrong). The support for the decision is therefore the **`Settings.Api` typed-union precedent**: `Results<Ok<T>, NotFound<MessageResponse>, BadRequest<MessageResponse>, Conflict<MessageResponse>, ProblemHttpResult>` with `ApiResponsePayloads.cs` / `CodedValueResponsePayloads.cs` as the payload records. P6's success payload (the three counts) and its catches express cleanly as such a union; an arm that cannot be typed gets one recorded inline deviation, mirroring Settings' existing D19 rather than silently returning `Results`.

*Trade-off, stated once:* this round's three endpoints will not look like their neighbours. That is the cost of the AGENTS.md rule; making the whole host consistent is a separate change and is not in scope.

### P2 — Migration (the R1 pin is the acceptance test here)

- Add `requires_enrollment` to `topic_assignments`: `nullable: false, defaultValue: false` — the default exists **only** so existing rows can take a NOT NULL column.
- `migrationBuilder.Sql` backfill: `UPDATE topic_assignments SET requires_enrollment = true WHERE topic_assignment_type = 'activity_group'` — same idiom as `20260930104306_AddBridgeDisplayOrders.cs` (AddColumn + `Sql`).
- `migrationBuilder.Sql`: `ALTER TABLE topic_assignments ALTER COLUMN requires_enrollment DROP DEFAULT` — **the default must not survive the migration.**
- **No** `HasDefaultValue` in `TopicAssignmentConfiguration`. The generated `Down` drops the column.
- Note for the reviewer: the backfill is expected to touch ~0 rows (§11 records the group↔subject bridge as dead scaffolding), so it is correctness, not data volume.

### P3 — Union query + `/subjects` endpoint (B4)

- New query `ListStudentSubjects` + handler in `Students.Core/CQRS/StudentTopicAssignments/Queries/`, producing the §6 **union**: every `false` subject on the student's grade **plus** every `true` subject the student holds a row for in the active academic year.
- The join precedent exists: `ListGradeTopicCurriculumByGradeHandler.cs:61` already does `join t in db.Topics … select new { AssignmentId = a.Id, TopicId = t.Id, t.Name, t.Code }`. Reuse that shape; **do not** add a join to `IGradeTopicAssignmentRepository` (its contract is bridge-columns-only).
- New DTO `StudentSubjectEnrollmentDto(Guid TopicId, string Name, string? Code, Guid AssignmentId, bool RequiresEnrollment, bool IsEnrolled)` — name + flag + enrolled state is exactly what the bands need and what `TopicAssignmentDto` cannot supply.
- Endpoint `GET /students/{id}/subjects` — **two changes from spec §10, both recorded in P7** (gate F2): the doubled `students` segment is a typo (existing routes are `/students/student-topics/...`), and the spec's `?gradeLevelId` query parameter is **dropped**. The grade is derived server-side from the student's **active enrolment** (`StudentEnrollment.GradeLevelId`, the same source `StudentEnrollmentRepository.GetActiveEnrollmentByStudentAndPeriodAsync` supplies) in the active academic year, because a caller-supplied grade could disagree with the student's actual placement and silently produce an empty or wrong union. No active enrolment ⇒ `200` with an empty array, never a 404.
- Returns `StudentSubjectEnrollmentDto[]`.
- Client method on `StudentsApiClient` for layer 2 to call.

### P4 — Eligibility endpoint (FR-ER-14, R3)

- `GET /students/topic-enrollment?gradeLevelId&topicId` — **no `periodId` parameter** (R3). The handler resolves the active academic year via `IActivePeriodProvider.GetActiveAcademicYearAsync` (the same dependency `AssignStudentTopicHandler` uses).
- **Response shape — two id arrays, not one (gate F1, the blocker).** `TopicEnrollmentDto(Guid[] Audience, Guid[] Eligible)`. FR-ER-14a's **spirit** holds — only ids cross, no domain type — but its **letter is amended**: the response is a two-`Guid[]` record rather than a bare `Guid[]`, and that one trivial record is the extent of the new coupling (gate N3).
  *Why the shape changed:* rule 3 of P5 excludes a student who "belongs to a targeted grade and is absent from that grade's eligible set", but `ResolveSubscribersRequest` carries only a flat `StudentIds` and `GradeLevelIds` (`IContactResolver.cs:46-54`) and `ResolveStudentIdsAsync` returns `Guid[]` with no per-student grade (`IAssignmentTargetResolver.cs:32`). With the single array the resolver **cannot tell** a grade member who is not enrolled from a student who arrived on a stream/activity-group leg — so both of F1's failure directions stayed open. Returning the audience alongside the eligible set makes the exclusion computable in **one** hop with no roster re-read (and none of the `students/by-grade/{id}` fallback that `StudentsContactResolver.cs:13-18` records as deleted).
  *Behaviour:* `false` subject ⇒ `Audience = Eligible =` the grade's roster (nothing to exclude). `true` subject ⇒ `Audience =` the grade's roster, `Eligible =` the students holding an active-year row.
- This is a **documented deviation from spec §10/FR-ER-14**, corrected in the spec by P7.
- **Edge cases pinned (gate F5):** the flag is read from the grade↔topic bridge row that is **effective today** — the same `StartDate <= today && (EndDate ?? today) >= today` filter every existing reader applies (`ListGradeTopicCurriculumByGradeHandler.cs:62-64`). **No effective bridge row ⇒ an empty `Guid[]`** (never the whole audience: an absent link means no subject to be enrolled in, and guessing `false` would over-send). No active academic year resolves ⇒ **the endpoint does not error**: `Eligible` is empty (nothing is enrolled) while `Audience` remains the grade roster, so a `true` subject excludes the whole grade. Both arrays empty would be a different — and wrong — answer: the resolver would compute `excluded = ∅` and over-send, contradicting "nothing is enrolled" (gate N4). A `false` subject never consults the active year, so it is unaffected.
- Returns `TypedResults.Ok(ids)` per P0.

### P5 — Publish filter (FR-ER-13/13a/13b/15)

- `ResolveSubscribersRequest` (`Assignments.Core/Services/IContactResolver.cs:46`) gains `Guid? TopicId = null` as its last member (FR-ER-13b — the field is genuinely absent).
- Call site `PublishAssignmentCommandHandler.cs:192` passes `assignment.TopicId` (already in scope, used at :168/:179).
**Where the filter applies — pinned (gate F1, this was the P1 blocker).** `request.StudentIds` is a **flat roster assembled from every targeting leg** (`PublishAssignmentCommandHandler.cs:192`; stream/student legs impose no gate — comment `:161-163`), so an unconditional intersect against one grade's eligibility would silently truncate every non-grade cohort. The rules, in order:

1. **Skip the hop entirely when `request.TopicId` is `null`** — a publish with no subject is not subject-scoped and must behave exactly as today. This is the first statement in the filter, not a fallthrough.
2. **When `TopicId` is present, resolve eligibility per grade in `request.GradeLevelIds`** (the grade-targeted legs), one call per grade, unioning the returned ids.
3. **A student is excluded only if they belong to a targeted grade and are absent from that grade's eligible set** — and the membership source is **P4's `Audience` array**, not a guess: excluded = `Audience(g) \ Eligible(g)` for each targeted grade `g`, unioned. A student who is in no `Audience(g)` (stream / student / all-students / activity-group legs) is **never** removed — spec decision 10 puts enforcement on grades only in v1, and over-filtering a mixed cohort is the failure the spec calls silent truncation.
4. `request.GradeLevelIds` empty ⇒ no grade scope ⇒ **no filtering**, same as rule 1.

- `StudentsContactResolver.ResolveSubscribersAsync` (`Assignments.Api/Services/StudentsContactResolver.cs:29`) applies the above over `request.StudentIds` (line 33) **before** the guardian/teacher/subscribed legs.
- **Fail-open on transport error** (FR-ER-15): a `catch (HttpRequestException)` that logs and **keeps that grade's students unfiltered** — matching the three existing per-leg catches at :47/:72/:96. With P4's two-array response this is expressible per grade: a failed call adds **no** exclusions for that grade (there is no `Audience` to subtract from), while other grades' results still apply. A `false` subject must be byte-identical to today (assert it, for grade cohorts and for `TopicId is null`).
- Reuse the existing `students-api` client registration (`Program.cs:126-137`, `propagateTenant: false`) — no new base address, no new `WithReference` (FR-ER-16b/c; `CrossModuleWiringTests` enforces it).

### P6 — Switch endpoint + transactional grandfather (B1, B2)

- `PUT /students/topic-assignments/{id}/requires-enrollment` (spec §10) → command `SetRequiresEnrollment(Guid assignmentId, bool value)`.
- **Grandfather on `false → true`**, in **one transaction**: `IUnitOfWork<StudentsDbContext>.ExecuteAsync` (precedent `CreateStudentWithLinkedDataHandler.cs:65`) writing the flag update **and** one `StudentTopicAssignment` per current audience member, then a single `ctx.SaveChangesAsync`. **Never** a loop of `AssignStudentTopicHandler` — `RepositoryBase.AddAsync` saves per entity (`RepositoryBase.cs:31-35`), so a mid-loop failure would leave the flag set with students missing, which FR-ER-10 calls worse than no grandfather.
- Idempotent: a row that already exists for the active year is not re-inserted (DB unique index `(tenant_id, student_id, topic_id, period_id)` would otherwise throw mid-transaction).
- `true → false` only flips the flag; rows stay as inert history (FR-ER-9).
- Returns the counts the switch surface needs (audience / enrolled / would-be-un-enrolled) so layer 2's confirm copy does not need a second endpoint (review D1, partially absorbed here as a response shape — **no** warning UI in this layer).

### P7 — Spec drift fixes carried with the code (owner Q43 defers FR-ER-24 to layer 2)

Fix **in** `documents/specs/subject-enrollment-requirement-flag.md` in this round, because the plan cites them:

- §5.3's flow is stale: `StudentsContactResolver` starts from `request.StudentIds` (`:33`) and the by-grade roster fallback is documented as **gone** (class summary `:16-18`). FR-ER-13's "insert a step after step 1" must be rewritten against the current shape.
- FR-ER-13b's open question is answered: `ResolveSubscribersRequest` has no `TopicId` — say so.
- §10 route typo `/students/students/{id}/subjects` → `/students/{id}/subjects`, the `?gradeLevelId` parameter is dropped (grade derived from the active enrolment), and the eligibility route loses `periodId`.
- **FR-ER-14/14a response shape**: the endpoint returns **two** id arrays — `Audience` and `Eligible` — not one. FR-ER-14a's "`Guid[]` only, no Students domain or DTO types" still holds (both are plain `Guid[]`); what changes is that one array cannot express the exclusion the resolver must compute. This is the one place the plan knowingly deviates from the spec's letter, and the spec is corrected in this round.
- §6/FR-ER-16 names `ListGradeTopicsByGradeAsync` (an *Application client* method returning a DTO with no name and no flag); the source is the grade↔topic bridge plus a `Topics` join, and neither existing method produces name+flag.
- §2.1's "mirror of `SetEnrollmentBlocked`" is misleading for the grandfather: the mutator no-ops, the **transactional materialisation** is FR-ER-7/10's job and lives in the handler.

> FR-ER-24's rewrite (rail bands, not "sections in the same dialog") is **layer 2** (Q43).

---

## Explicitly NOT in this round

| Deferred | Why |
|---|---|
| The bands, the kebab switch UI, the enrol action | Layer 2 (Q46a) |
| The grades × subjects rollover screen (§7, FR-ER-18…23) | Not in the slice. **Recorded as an operational cliff** (review D2): the unique index is per-year, so at the next academic-year boundary every `true` subject's rows stop matching FR-ER-3's active year and all students read "not enrolled" — with no in-scope surface to re-issue. |
| Grade-card count "20 of 30 enrolled" (FR-ER-26) | Deferred with the grade-card column (review D3); until then the only grade-side visibility is absent, and that is stated, not implied. |
| Bulk "enrol the remaining N" (FR-ER-8 second half) | Layer 2 ships the **per-student** action (Q45); bulk is a separate change. |
| Any feature flag | Q40 — the default is the safety. |

## Acceptance criteria (checkable, one by one)

1. `TopicAssignment.RequiresEnrollment` exists on the TPH base; `SetRequiresEnrollment` no-ops when unchanged and bumps `UpdatedAt` when it changes (**test**).
2. `GradeTopicAssignment.Create` yields `false`; `ActivityGroupTopicAssignment.Create` yields `true` (**test**, FR-ER-2).
3. Migration: column NOT NULL, **no surviving schema default** (`\d topic_assignments` after migrate shows no default), group rows backfilled to `true`, `Down` reverses. **Manual check** — `gates.sh` never runs a live Postgres, so this is verified against the dev container and recorded here, not claimed as gate-covered (gate note).
4. No `HasDefaultValue` for `requires_enrollment` anywhere in `Configuration/`.
5. `TopicAssignmentDto` carries `RequiresEnrollment`, **all eight** construction sites are updated (a grep for `new TopicAssignmentDto(` returns **eight** hits, none stale), **and each projects the value from the entity rather than a constant** — asserted by a test that reads the flag off the DTO for a `true` group assignment and a `false` grade assignment (gate note: the structural grep alone would pass a hard-coded `false`).
6. `GET /students/{id}/subjects` returns the union: a `false` subject appears for every grade student; a `true` subject appears only with a row in the active year (**test**).
7. `GET /students/topic-enrollment` takes **no** period parameter, resolves the active year server-side, and returns **both** arrays: `Audience` is the grade roster and `Eligible` the enrolled subset; for a `false` subject `Audience == Eligible` (**test**).
8. Publish filter — pinned to its scope (gate rewrite): **(a)** with `TopicId is null` the eligibility hop is **not called at all** and the recipient set is identical to pre-change; **(b)** a `false` subject on a grade-targeted cohort produces a set identical to pre-change; **(c)** a `true` subject excludes exactly `Audience(g) \ Eligible(g)` for each targeted grade, while students in no audience array (stream/student/all-students/activity-group legs) are **untouched** — asserted with a mixed cohort fixture that contains both a grade member and a non-grade-leg student (**test** per case).
9. Eligibility transport failure leaves the recipient set **unfiltered** and logs a warning (**test**, FR-ER-15).
10. `SetRequiresEnrollment(true)` grandfathers the whole audience in **one transaction**; a forced failure mid-write leaves **neither** the flag nor partial rows (**test**, FR-ER-7/10).
11. `SetRequiresEnrollment(false)` keeps rows (FR-ER-9) and the response reports the audience/enrolled/un-enrolled counts.
12. The six spec-drift items in P7 are corrected in the spec, and the plan's cited line numbers still resolve.
13. `dotnet build SchoolCollab.slnx` 0 errors; `Students.Tests.Unit`, `Students.Api.Tests.Unit`, `Assignments.Tests.Unit`, `Assignments.Api.Tests.Unit`, `ArchitectureTests.Unit` green.
14. Escalated gate: `scripts/round/gates.sh` with **no flags** (the diff carries a migration), every leg named, skips recorded.
15. **Tenant isolation** (gate addition): the eligibility endpoint and `/students/{id}/subjects` return **no rows** for a grade, student or topic id belonging to another tenant — asserted per `.github/copilot/rules/testing.md` §5 (operational data), because both endpoints return student-level data.
16. **Auth** (gate addition): all three new endpoints sit under `RequireAuthIfOidc` and return 401 when OIDC is not disabled — endpoint-auth test, `GradeStreamEndpointAuthTests` precedent.

## Gates (parent-owned, after the worker)

`gates.sh` no flags → then the static diff review → then acceptance transcribed here.

---

## Worker Report

Worker: `ollama-cloud/deepseek-v4.1-flash:1017`, run `2eee6f51`, branch `feat/subject-enrollment-requirement-layer1`.
Diff artifact: `documents/rounds/diffs-subject-enrollment-requirement-layer1.patch` (**38 files, +4139 / −43**, generated with `git add -N` so the new files are in it).

### Parent's authoritative numbers (child numbers discarded)

| Command | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors**, 17 warnings |
| `Students.Tests.Unit` | **680 / 680**, 0 failed *(pre-rework; **681** after the regression test — see `### Rework`)* |
| `Students.Api.Tests.Unit` | **29 / 29**, 0 failed |
| `Assignments.Tests.Unit` | **1390 / 1390**, 0 failed |
| `Assignments.Api.Tests.Unit` | **207 / 207**, 0 failed |
| `ArchitectureTests.Unit` | **146 / 146**, 0 failed |

32 new tests. These are the numbers acceptance is written against.

### Criteria the worker reported

1, 2, 4, 5, 6, 7, 8(a–c), 9, 10, 11, 12, 13, 15, 16 — met with a named test. **3** — not verified (manual, live Postgres, by design). **14** — parent-owned `gates.sh`, still to run.

### Rulings on the worker's ten deviations (plan author)

| # | Deviation | Ruling |
|---|---|---|
| 1 | Grade roster falls back to active enrolments across periods when no year resolves | **Accepted** — it is the only source that keeps P4's pinned answer ("a `true` subject excludes the whole grade") reachable when the year is missing. The fallback is bounded to roster membership, never to enrolment state. |
| 2 | The switch requires an active academic year in **both** directions → `PeriodNotOpenException` → 409 | **Accepted** — rows are year-keyed (FR-ER-3), and a `false → true` flip with no year could only produce "everyone unenrolled". Requiring a year for the `false` direction is justified by the counts, not by the write; noted so a later round can relax it if the counts become optional. |
| 3 | Counts are **pre-mutation** | **Accepted, and correct** — FR-ER-8 wants the audience / enrolled / un-enrolled triple *before* confirming; a post-mutation `UnenrolledCount` would always be 0. |
| 4 | The switch is grade-only (other audiences → 404) | **Accepted** — matches decision 10 (grades-only enforcement) and `SetGradeTopicOrder`'s discrimination. |
| 5 | The union uses bridge rows **effective today** | **Accepted** — P3's stated rule. |
| 6 | `OpenApiSurfaceArchitectureTests` pin 0 → 3 | **Accepted** — not a weakened test: it fails *because* the three endpoints now document schemas, and its own message instructs raising the pin in the converting PR. 3 is the measured count. |
| 7 | `StudentsTestScope` gained an optional `SaveChangesInterceptor` + `NewContext()` | **Accepted** — the only way to assert single-`SaveChanges` atomicity and post-failure persistence without duplicating tenancy setup. |
| 8 | The two new DTOs are uncached | **Accepted for layer 1**; a per-student read has no sensible cache key without invalidation churn. Recorded so layer 2 can add caching if it proves hot. |
| 9 | `/students/{id}/subjects` lives in `StudentTopicAssignmentRoutes.cs` | **Accepted** — mirrors the `/students/{id}/activity-groups` precedent of a student-scoped path in a specialty file. |
| 10 | Two §10 rows marked "not mapped yet" (enroll-bulk, rollover) | **Accepted** — accuracy in a table P7 already rewrites. |

### Rework (one iteration, the round's budget)

**Trigger:** the diff review's non-blocking finding — `SetRequiresEnrollmentHandler` gated the grandfather on `if (command.Value)` instead of the pre-flip state, so a redundant `PUT { true }` re-grandfathered grade members added since the first flip. Ruled **not acceptable as a residual** (it silently writes rows and contradicts FR-ER-7).

**Fix:** `var wasImplied = !assignment.RequiresEnrollment;` captured before the setter; gate `if (command.Value && wasImplied)`. The handler-side capture was chosen over changing `SetRequiresEnrollment`'s return shape, because criterion 1 pins the mutator's no-op contract.

**Regression test:** `RedundantTrueFlip_DoesNotEnrolNewlyAddedGradeMembers` — verified to **fail** without the fix (the worker reverted it deliberately and re-ran the filtered test). Worth recording: the pre-existing `FlippingToTrue_IsIdempotent_AndDoesNotDuplicateRows` does **not** catch this bug, because it adds no member between flips — a test named for idempotency is not a test that pins it.

**Post-rework authoritative numbers:** build 0 errors; `Students` **681**, `Students.Api` 29, `Assignments` 1390, `Assignments.Api` 207, `Architecture` 146 — all 0 failed. Diff artifact regenerated (4718 lines).

**Rework review verdict:** `CLOSED` — fix complete on all four paths (`false→true` grandfathers; `true→true` writes nothing; `true→false` keeps rows; `false→false` writes nothing), and the new test discriminates.

### Criterion 3 — the manual live-Postgres check (parent, 2026-10-27 07:39 UTC)

Ran `SchoolCollab.MigrationService` standalone against the dev container (`postgres-057ea9d7`, `students-db`) per the `run-migration-service-standalone` procedure. Exit 0, "Unified migration service completed successfully", and the emitted SQL is exactly the R1 sequence:

```
ALTER TABLE topic_assignments ADD requires_enrollment boolean NOT NULL DEFAULT FALSE;
SET requires_enrollment = true
ALTER TABLE topic_assignments ALTER COLUMN requires_enrollment DROP DEFAULT;
```

Verified after apply:

| Check | Result |
|---|---|
| `\d topic_assignments` → Default column for `requires_enrollment` | **empty** — no schema default survived |
| `topic_assignment_type` truth table | `grade / f / 2 rows` — the backfill's WHERE did not over-apply |
| `activity_group` rows backfilled to `true` | **0 rows exist in this dev DB** — the plan predicted this (§11 records the group↔subject bridge as dead scaffolding). So the backfill is *correct by inspection and by the emitted SQL*, but it is **not exercised** here. Named, not implied. |
| `__EFMigrationsHistory` latest | `20261027071001_AddTopicAssignmentRequiresEnrollment` |

### Not done, and named rather than implied

Criterion 3 (live Postgres `\d topic_assignments` + backfill count) and criterion 14 (`gates.sh`, no flags) — both parent-owned, both run before acceptance.

## Review

### Plan-review pass (step 2b, gate) — `ollama-cloud/deepseek-v4-pro:0813`

**Verdict: `P2` — closes after three passes.** Recorded verbatim in `d93b14d9`; the two blocking passes before it are part of the round's record, not noise:

| Pass | Verdict | What it caught |
|---|---|---|
| 1 | **`P1`** | F1 — the publish filter never said **where grade membership comes from**, so a naive intersect would truncate every mixed/non-grade cohort. Plus F2 (P3 silently dropped the spec's `?gradeLevelId`), F3 (`TypedResults` decision premised on a false fact), F4 (factory mechanism delegated to the worker), F5 (eligibility edge cases open) |
| 2 | **`P1`** | F1 **still open** — rules named the exclusion but not its data source; N1 — the plan claimed **three** `TopicAssignmentDto` construction sites when there are **eight** (a positional-record change would have broken the build); N2 — the `TypedResults` citation was invented (`StudentRoutes.cs` returns `Results.Ok`; `src/Students` has no `TypedResults` at all) |
| 3 | **`P2`** | F1/N1/N2 closed. Three report-only notes, **all applied to the plan before dispatch**: N3 (FR-ER-14a "spirit holds, letter amended"), N4 (no-active-year ⇒ `Audience` intact / `Eligible` empty, else the resolver over-sends), N5 (`:13-15` → `:16-18`) |

**Pins re-ruled and held:** R1, R3, FR-ER-10, FR-ER-15 (per-grade refinement noted), and the `TypedResults` decision.

**Known limit, recorded rather than hidden:** criterion 5 asserts "projects the value from the entity" by test at **2 of 8** sites; the other six rest on the grep plus diff review.

### Diff-review pass (step 4) — `ollama-cloud/deepseek-v4-pro:0813`, run `0e59a629`

**Verdict: `APPROVED WITH NOTES`** — every pinned axis holds, **no blocking findings**, one substantive non-blocking correctness gap that the plan author is **not** accepting as a residual (see the rework below).

| Pin | Ruling |
|---|---|
| R1 (transient default → backfill → DROP; no `HasDefaultValue`; `Down` drops) | **holds** — discriminator `activity_group` matches `TopicAssignmentConfiguration`'s `HasValue<ActivityGroupTopicAssignment>("activity_group")`; snapshot/Designer carry no default; grep for `RequiresEnrollment` + `HasDefaultValue` = 0 |
| R3 (no `periodId`, server-resolved year) | **holds** |
| FR-ER-10 (single `SaveChanges` via `ExecuteAsync`) | **holds**, with the caveat below |
| P4 semantics (two arrays, four cases) | **holds** |
| P5 rules 1–4 + per-grade fail-open | **holds** |
| P0 (`TypedResults` on all three) | **holds** — the PUT uses a typed `Results<Ok<TopicEnrollmentCountsDto>, NotFound, Conflict<MessageResponse>>` |
| P1-eight-sites | **holds** — grep = 8, all projecting the entity |

**Rework finding (fixed in the rework run, not shipped as a residual).** `SetRequiresEnrollmentHandler.cs:76-78` gated the grandfather on `if (command.Value)` instead of the **pre-flip** state, so a redundant `PUT { true }` on an already-`true` subject would enrol every grade member added since the first flip — silently enrolling students who, under explicit-enrolment semantics, must be enrolled explicitly. It contradicted FR-ER-7 ("currently enrols by implication"), P6 ("grandfather on **false → true**") and the handler's own doc. Not accepted as a residual: it is a two-line semantic bug that silently writes rows, so it gets the round's one rework iteration plus a test that must fail without the fix.

**Two seams recorded, deliberately not fixed here:**

1. **`/recipient-preview` over-counts `true` subjects** (`AssignmentRoutes.cs:508`): it builds `ResolveSubscribersRequest` with no `TopicId`, so after the filter landed, preview and publish can disagree for a `true` subject — preview counts students publish will exclude. The plan scoped the filter to publish only, so this is a seam the shared resolver opened. Goes to layer 2 (the wizard) with a `topicId` parameter.
2. **`ListStudentSubjectsHandler` cannot express the "not chosen" band** — its FR-ER-16 union filters out un-enrolled `true` subjects, so `IsEnrolled` is `true` for every row it returns. Correct for "my subjects"; **insufficient** for layer 2's amber band, which needs every `true` subject on the grade plus its enrolment state. Layer 2 extends this query (or reuses `GetTopicEnrollment` per subject).

**Criteria the diff evidences by itself:** 1, 2, 4, 5, 6, 7, 8(a–c), 9, 10, 11, 12, 15, 16 — including that the single-`SaveChanges` test genuinely discriminates (`counter.Calls == 1` fails a loop-of-saves) and the fail-open test genuinely proves the unfiltered set. **Unevidenced by the diff, parent-owned by design:** 3 (live Postgres), 13 (build/test numbers), 14 (`gates.sh`).

## Acceptance

**Verdict: CLOSED — ACCEPTED.** Written and signed by the plan's author (parent, `ollama-cloud/deepseek-v4.1-flash:1017`), on the independent verification pass of `ollama-cloud/deepseek-v4-pro:0813` (`1b734fcc`) checked against the diff and the parent's authoritative numbers. **No residuals were accepted on trust**: the three known limits are listed below with the reason each is acceptable, and two seams are deferred to layer 2 by name.

| # | Criterion | Verdict | Evidence |
|---|---|---|---|
| 1 | Flag + no-op setter on the TPH base | **met** | `TopicAssignment.cs` + `SetRequiresEnrollment_NoOpsWhenUnchanged_AndDoesNotBumpUpdatedAt` / `…_ChangesTheValue_AndBumpsUpdatedAt` |
| 2 | Factory defaults `false` / `true` | **met** | both `Create` methods + `Factories_StampOppositeDefaults_GradeFalse_GroupTrue` |
| 3 | Migration: NOT NULL, no surviving default, group backfill, `Down` | **met** (residual 2) | migration file + live Postgres: empty Default, `grade/f/2`, migration recorded; 0 group rows existed to backfill |
| 4 | No `HasDefaultValue` in configuration | **met** | `TopicAssignmentConfiguration` + snapshot/Designer clean |
| 5 | DTO carries the flag at all 8 sites, projected from the entity | **met** (residual 1) | grep = 8, no literals; value asserted on the two feed readers |
| 6 | `/subjects` union semantics | **met** | `ListStudentSubjectsHandlerTests` ×3 (implied always, explicit only with a row, prior-year excluded, non-effective bridge skipped) |
| 7 | Eligibility: no `periodId`, server-resolved year, both arrays | **met** (residual 3) | `GetTopicEnrollment` record + route signature; year resolution behaviourally tested |
| 8 | Publish filter (a) no hop on null `TopicId`, (b) `false` identical, (c) mixed cohort | **met** | the three named cases, incl. the mixed-cohort fixture that keeps the stream-leg student |
| 9 | Fail-open leaves the cohort unfiltered + logs | **met** | 500 test + per-grade test (one grade fails, the other still excludes) |
| 10 | Grandfather in one transaction | **met** | interceptor-counted `SaveChanges == 1`; forced failure leaves flag `false` and zero rows from a fresh context |
| 11 | `true → false` keeps rows; counts reported | **met** | keeps both rows, adds none, reports 3/2/1 |
| 12 | The six P7 spec corrections | **met** | spec diff (§5.3, FR-ER-13b, §10 ×2, FR-ER-14/14a, §6, §2.1) |
| 13 | Build + project suites | **met** | parent's numbers: 0 errors; 681 / 29 / 1390 / 207 / 146, all 0 failed |
| 14 | `gates.sh` no flags | **met** | exit 0; unit **4583 / 4583**; MAUI 0 errors; Portals 88; AuthPortal 123; integration 146/151 (two allow-listed `OpenRouter*` live tests) |
| 15 | Tenant isolation on both read endpoints | **met** | `ReturnsNothing_ForAnotherTenantsGradeAndSubject`, `Union_ReturnsNothing_ForAStudentOfAnotherTenant` |
| 16 | Auth on all three endpoints | **met** | `studentsGroup` + `RequireAuthIfOidc`; `SubjectEnrollmentEndpointAuthTests` |

**Accepted residuals**

1. **Criterion 5 — value assertions at 2 of 8 sites.** Accepted: the other six are single-line projections (`x./a./grade./group.RequiresEnrollment`) with zero literals in the diff, and the assertion sits on the two feed readers where a hard-coded value would actually ship.
2. **Criterion 3 — the group backfill is unexercised.** Accepted: the dev DB holds **0** `activity_group` rows (§11 records the bridge as dead scaffolding), so zero rows is the predicted case, not a missed test. The load-bearing half (NOT NULL, transient default, `DROP DEFAULT`, `Down`) was verified live.
3. **Criterion 7 — "no `periodId`" proven by signature, not a wire test.** Accepted: absence of a parameter is a structural fact; the behaviour a caller-supplied period would corrupt *is* tested (`NoActiveAcademicYear`, `TrueSubject_IgnoresARowIssuedForAPriorYear`).

**Deferred to layer 2, by name**

- **`/recipient-preview` over-counts `true` subjects** (`AssignmentRoutes.cs:508` builds the request with no `TopicId`, so rule 1 short-circuits). Publish and preview can disagree until the wizard threads a `topicId`. Confirmed out of layer 1's scope.
- **`ListStudentSubjectsHandler` cannot express the "not chosen" state** — its union drops un-enrolled `true` subjects, so `IsEnrolled` is always `true`. Correct for §6's "my subjects"; insufficient for the amber band. Confirmed layer 2.

**Not carried as residuals, because they were fixed rather than accepted:** the grandfather pre-flip gate (the round used its one rework iteration, with a regression test proven to fail without the fix).

**Not run:** none. Criterion 3 (live Postgres) and criterion 14 (`gates.sh` no flags) both ran; nothing in this round is unverified or silently skipped.
