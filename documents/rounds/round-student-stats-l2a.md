Provider: pi — parent on `ollama-cloud/deepseek-v4.1-flash:1017`; Tier 2 plan gate on `ollama-cloud/deepseek-v4-pro:0813` (a different seat from the plan author, per skill step 2b).
Mode: attended
Round base: `a3c61014` — tree clean at round start (`git rev-parse HEAD` = `a3c6101430ba1c2fcfed08385bb09fd81f2bdda9`).
Tier override (owner Q146, 2026-10-28): **L2b runs Tier 2 as well**, against the skill's Tiers table which puts endpoint + multi-context + UI at Tier 3. Recorded here so a later reader sees the override, not a mistake. L2a is Tier 2 by the table.

# Round — student assignment stats, layer L2a: the Assignments query and endpoint

Implements the **L2a** half of [#444](https://github.com/stayboy/School-Collab/issues/444). L2b (the Students port, client, endpoint and rail panel) is the next round and is **out of scope here**.

## Settled by the owner

| # | Ruling |
|---|---|
| Q122 | The stats panel is its own stack layer, not part of the page round |
| Q123 | Students.Api enriches — port in `Students.Core`, client in `Students.Api`, `studentsApi.WithReference(assignmentsApi)` already wired |
| Q126 | "Sent out" = `AssignmentStatus.Published`, plus a `Scheduled` assignment whose publish time has passed |
| Q138 | Split into L2a (Assignments) and L2b (Students + UI) |
| Q139 | **No period filter** — the counts join on the student, so the panel cannot inherit #427's silent-empty failure mode |
| Q142 | **Tenant-exact**: the stats read uses a tenant-propagating client, not the no-tenant `assignments-api` client its neighbour uses. The tenant-scoped test lands in **L2b** (where the client is registered); L2a must nevertheless prove the query is tenant-filtered |
| Q146 | L2b runs Tier 2 too (override, see the header) |

## Plan

### Goal

Serve the six counts this student's assignment record implies, in one round trip, from the context that owns the data: a grouped-count query in `Assignments.Core`, a JSON endpoint in `Assignments.Api`, tests in `Assignments.Tests.Unit`. **No UI, no Students change, no migration.**

### Scope — expected files

| File | Change |
|---|---|
| `src/Assignments/SchoolCollab.Assignments.Contracts/ContractTypes.cs` | **add** `StudentAssignmentStatsDto` — the six counts + documented derived invariants. It crosses the API boundary, so it belongs beside `WardAssignmentListItemDto` (`ContractTypes.cs:536`), **not** in `Core/DTOs`. Students does not reference `Assignments.Contracts` (0 references, pinned by `TeacherScopeWiringArchitectureTests`), so L2b mirrors this shape in its own port DTO — the `AssignmentReferenceDto` precedent |
| `src/Assignments/SchoolCollab.Assignments.Core/DTOs/StudentAssignmentStatRow.cs` | **new** — the per-assignment flag projection the fold consumes: `AssignmentStatus Status`, `DateTimeOffset? AvailableFromUtc`, `bool MandatoryReview`, `bool HasFinalizedSubmission`, `ReviewState? ReviewState`, `bool HasReviewedGate` (gate review folded to a bool by the read, so the handler never reasons about row existence) |
| `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Queries/GetStudentAssignmentStats/GetStudentAssignmentStatsQuery.cs` | **new** — `record GetStudentAssignmentStats(Guid StudentId)` |
| `.../GetStudentAssignmentStatsQueryHandler.cs` | **new** — `IQueryHandler<GetStudentAssignmentStats, StudentAssignmentStatsDto>`; injects **`TimeProvider`** (the pinned clock C5 needs; `FakeTimeProvider` already exists) |
| `src/Assignments/SchoolCollab.Assignments.Core/Data/Repositories/IStudentAssignmentStatsRepository.cs` | **new** — `Task<List<StudentAssignmentStatRow>> ListStudentAssignmentStatRowsAsync(Guid studentId, CancellationToken ct)` |
| `src/Assignments/SchoolCollab.Assignments.Core/Data/Repositories/StudentAssignmentStatsRepository.cs` | **new** — EF `Select` over `db.Assignments` filtered **only** by the recipient link (no status predicate — see Open decision B) |
| `src/Assignments/SchoolCollab.Assignments.Core/Extensions.cs` | **one line** — `AddScoped<IStudentAssignmentStatsRepository, StudentAssignmentStatsRepository>()` beside the ward read at `:53` |
| `src/Assignments/SchoolCollab.Assignments.Api/Endpoints/StudentStatsRoutes.cs` | **new** — `MapStudentStatsRoutes(this RouteGroupBuilder group)`, relative path `/{studentId:guid}/assignment-stats`, `TypedResults`, never touches auth |
| `.../AssignmentEndpoints.cs` | **one chained call** `group.MapStudentStatsRoutes();` on the `/assignments` group, so the route inherits that group's `RequireAuthIfOidc` policy |
| `tests/SchoolCollab.Assignments.Tests.Unit/.../GetStudentAssignmentStatsHandlerTests.cs` | **new** — the count rules, over `FakeTimeProvider` |
| `tests/SchoolCollab.Assignments.Tests.Unit/.../StudentAssignmentStatsRepositoryTenantTests.cs` | **new** — C7 at the EF layer, over `AssignmentsDbContext` with two tenants, where the global query filter lives |
| `tests/SchoolCollab.Assignments.Tests.Unit/.../StudentStatsRouteCompositionTests.cs` | **new** — the route lives on the `/assignments` group and its handler is the query |

Nothing else. If the diff needs a file outside this table, that is a plan defect: stop and report it rather than widening silently.

### Count semantics (the contract L2b's UI will read)

```csharp
record StudentAssignmentStatsDto(
    int Assigned,        // the assignment is RECIPIENT-LINKED to this student (any status, incl. Draft)
    int SentOut,         // …and it is Published, or Scheduled with its publish time passed (Q126) — tested in the FOLD, never in the SQL WHERE
    int Submitted,       // …and a submission exists with FinalizedAt set
    int Marked,          // …and that SAME submission is finalized AND ReviewState.Graded (never Graded alone)
    int ReviewRequired,  // sent + Assignment.MandatoryReview — the review axis's OWN denominator
    int Reviewed);       // …and a GuardianSubmissionGate row has ReviewedAt set
```

Invariants, each one a test: `AwaitingMarking = Submitted − Marked` (never counted as *Marked*); `AwaitingReview = ReviewRequired − Reviewed` (never counted as *awaiting submission*); `ReviewRequired ≤ SentOut` (an unsent draft cannot be gated).

### Acceptance criteria

| # | Criterion | Evidence |
|---|---|---|
| C1 | The four progress counts match a hand-computed fixture | handler test with an explicit per-assignment fixture, not a count assertion on a loop |
| C2 | A **submitted but unmarked** assignment lands in awaiting marking, never in *Marked* — and `Marked` requires the **same** submission to be both finalized and graded | handler test naming both cases: `FinalizedAt` set with `ReviewState.Pending`, **and** `ReviewState.Graded` with `FinalizedAt` null (which the domain permits — `ApplyReview` is independent of the sign-off chain), the second proving `Marked` cannot exceed `Submitted` |
| C3 | An assignment **held by the guardian gate** lands in awaiting review, never in awaiting submission | handler test naming that case |
| C4 | `ReviewRequired` counts only `MandatoryReview` among **sent** assignments | handler test with a mandatory-review assignment still in `Draft` |
| C5 | A `Scheduled` assignment whose publish time has **passed** counts as sent; one whose time has not, does not | handler test with both, at a **pinned `FakeTimeProvider`** — the handler reads `now` from the injected `TimeProvider`, never `DateTimeOffset.UtcNow` |
| C6 | **No period filter**: a student whose placement is a sub-period yields the same counts as one whose placement is the academic year, given the same audience | handler test driven only by `StudentId` — the query takes no period, so the test proves the signature cannot regress |
| C7 | Rows from another tenant never contribute | `StudentAssignmentStatsRepositoryTenantTests` over `AssignmentsDbContext` with two tenants — the global `HasQueryFilter("Tenant", …)` is what does the work, and `ValidateTenantFilters` already fails the model build if a filter is missing |
| C8 | The handler performs a **constant** number of repository calls — no N+1 | test runs the handler against a 1-assignment and a 25-assignment fixture and asserts the same call count. **Honest about its strength:** with one repository call the criterion is structurally true today, so it guards a future rewrite rather than discriminating now — kept, and labelled as a guard |
| C9 | Build + tests green | `dotnet build SchoolCollab.slnx`; `Assemblies.Tests.Unit` project green |

### Reviewer acceptance criteria (Tier 2 adds these)

R1 `WardAssignmentListItemDto` is **not** widened — the ward contract serves the Families portal and mobile, and is an N+1 by recorded decision; a stat read must not ride it.
R2 The route is declared in a `*Routes.cs` with a relative path, and the group, its prefix and its authorization policy are untouched — no inline `MapGet` in `AssignmentEndpoints.cs`, no hand-rolled feature-flag `if`.
R3 Results are `TypedResults`, so OpenAPI emits a real schema.
R4 No repository method loads entities to count them in memory where a projection suffices; no new `SaveChanges`.
R5 Nothing outside the expected-files table changed.

### Open decisions (declared, not silently decided)

**A. Grouped in SQL, or folded in C#?** The repository can return (i) **already-grouped counts** (SQL `GROUP BY`), or (ii) **one row per candidate assignment carrying its five flags**, folded in the handler.
➡️ **(ii).** A student's candidate set is tens, not thousands, so the memory cost is nil; and the *rules* (submitted-but-unmarked, gate-held) stay in testable C#, where C2/C3/C5 are plain assertions rather than SQL you cannot unit-test. The single round trip the issue asks for is preserved either way — this decision is about where the arithmetic lives.

**B. Reuse the ward projection repository, or a new read?** *(Reversed after plan review — the original recommendation was wrong.)*
➡️ **A new, status-independent read.** The first draft proposed reusing `IWardAssignmentProjectionRepository.ListWardAssignmentsAsync`, on the reasoning that one definition of *assigned* keeps the panel and the ward list consistent. **That reasoning is defeated by that read's own WHERE clause**: `WardAssignmentProjectionRepository.ListWardAssignmentsAsync` already filters
`(Status == Published || (Status == Scheduled && AvailableFromUtc != null && AvailableFromUtc <= nowUtc)) && HasRecipientLink`.
Reusing it makes the candidate set **identical to the sent set**, so `Assigned` collapses into `SentOut`, the handler can never observe a `Draft` or a not-yet-published `Scheduled` row, and **C4 and C5 would pass vacuously** — excluded by the SQL predicate, not by the fold they claim to test.

The two reads therefore answer two different questions, and the divergence is deliberate:

| Read | Question | Set |
|---|---|---|
| `ListWardAssignmentsAsync` | what may this **ward** see? | recipient-linked **and** visible (`Published`, or `Scheduled` already active) |
| `ListStudentAssignmentStatRowsAsync` | what has been **assigned to this student**? | recipient-linked, **any status**, with the sent test left to the fold |

A future reader must not "simplify" this back to one read: collapsing them re-creates a funnel whose first two bars are the same number.

**C. "Assigned" means recipient-linked, not audience** *(added after plan review).* Grade/group targeting resolution is cross-context and deferred (slice 2b); `WardStudentId` link rows are what exists today. The DTO's doc comment and the handler comment say **recipient-linked**; the word "audience" is not used where it would imply a resolution this code does not perform.

## Plan review (skill step 2b — iteration 1)

Seat: `ollama-cloud/deepseek-v4-pro:0813` (plan author was `deepseek-v4.1-flash:1017`, so the gate is a different
seat; no escalation). Verdict **NO-GO**, one P1, two P2, and a p3 list the plan now incorporates.

```
PLAN REVIEW
verdict: NO-GO
p1:
- Decision B reuses `ListWardAssignmentsAsync`, whose WHERE already filters to Published ||
  (Scheduled && AvailableFromUtc <= now) (WardAssignmentProjectionRepository.cs:19-21). The plan's own
  contract needs Assigned != SentOut ... Reusing the status-filtered read makes the candidate set = the
  sent set, so `Assigned` collapses into `SentOut` ... and C4/C5 pass vacuously. Fix: the stats candidate
  set must be the status-independent recipient-link set (any status), with the sent flag computed in the
  fold - a new read/method, not a reuse.
p2:
- C7's evidence (an EF-layer tenant-isolation test) is not in the expected-files table ...
- The expected-files table omits the per-assignment flag projection type that decision A(ii) requires ...
p3:
- "Assigned = the assignment's audience includes this student" overstates what the candidate set computes
  today: only recipient-link targeting (WardStudentId) ... wording should say "recipient-linked".
- Tenant isolation is sound and correctly layered: EF's global Tenant query filter ... applies to every
  ITenantEntity table ... ValidateTenantFilters fails the model build if any is missing.
- C5's "pinned clock" requires the handler to source `now` itself ... can inject a TimeProvider.
- C8 asserts a constant call count that decision A(ii) makes structurally true ... harmless but weak.
- The route group is `/assignments` and the route is relative `/{studentId:guid}/assignment-stats`.
open_decisions:
- A: agree
- B: disagree: ListWardAssignmentsAsync's WHERE already applies the sent-status filter ...
frontier: Q122 upheld, Q123 upheld, Q126 upheld, Q138 upheld, Q139 upheld, Q142 upheld, Q146 upheld
accepted: 7 of 7
```

### Adjudication (parent, against the source — not against the reviewer's prose)

| Finding | Verdict | Evidence |
|---|---|---|
| P1 (reuse collapses `Assigned` into `SentOut`) | **accepted — the plan was wrong** | Read `WardAssignmentProjectionRepository.ListWardAssignmentsAsync` directly: its `Where` carries the Published/Scheduled-active predicate, so the handler never sees a `Draft`. Decision B reversed; a new status-independent read named in the expected-files table |
| P2a (C7's test file missing) | **accepted** | `StudentAssignmentStatsRepositoryTenantTests` added |
| P2b (flag-projection type homeless) | **accepted** | `StudentAssignmentStatRow` added, with its field list |
| p3 "recipient-linked, not audience" | **accepted** | count semantics, decision C |
| p3 tenant filter is EF-global + model-build validated | **accepted, and it strengthens C7** | `AssignmentsDbContext.cs:92` `ValidateTenantFilters` |
| p3 clock via `TimeProvider` | **accepted** | handler ctor + C5 evidence |
| p3 C8 is structurally true today | **accepted, kept as a labelled guard** | C8 wording |
| p3 route group is `/assignments`, relative path | **accepted** | expected-files row |

## Plan review (skill step 2b — iteration 2, final)

Same seat, second and last pass (the skill allows one plan-review iteration).

```
PLAN REVIEW
verdict: GO
p1: none
p2: none
p3:
- Plan:33 names `AssignmentSubmissionReviewState? ReviewState`, but the enum is `ReviewState`
  (Domain/ReviewState.cs:4).
- Plan:53 implies, but does not pin, that the fold ANDs `HasFinalizedSubmission && ReviewState == Graded`.
  `ReviewState.Graded` is reachable without `FinalizedAt`, so a bare `ReviewState == Graded` fold could make
  Marked exceed Submitted.
- Plan:32 puts an endpoint-returned DTO in Core/DTOs, whereas `WardAssignmentListItemDto` lives in
  Assignments.Contracts (ContractTypes.cs:536). Contracts is the repo convention for results crossing the
  API boundary; Students never references it, so L2b will mirror its own DTO regardless.
p1_resolution: resolved
frontier: Q122 upheld, Q123 upheld, Q126 upheld, Q138 upheld, Q139 upheld, Q142 upheld, Q146 upheld
accepted: 7 of 7
```

All three p3 notes were **accepted and folded into the plan before the worker was dispatched** — the DTO moved
to `Assignments.Contracts`, the row field renamed to `ReviewState? ReviewState`, and C2 widened to the
`Graded`-without-`FinalizedAt` case that would otherwise let `Marked` exceed `Submitted`.

## Worker Report

Seat: `ollama-cloud/deepseek-v4.1-flash:1017`. Transcribed by the parent from the seat's inline block; the
numbers below are the parent's own re-measurement, not the seat's.

**Delivered** — 8 new files, 3 one-edit changes, exactly the expected-files table:

| File | What |
|---|---|
| `Contracts/ContractTypes.cs` (+25) | `StudentAssignmentStatsDto` — six counts + the derived invariants, beside `WardAssignmentListItemDto` |
| `Core/DTOs/StudentAssignmentStatRow.cs` | the per-assignment flag projection; its doc pins the no-status-predicate rule |
| `Core/CQRS/.../GetStudentAssignmentStats/{Query,QueryHandler}.cs` | the fold; `TimeProvider` clock; `Marked` ANDs finalized + graded; review axis counted only among sent + mandatory |
| `Core/Data/Repositories/IStudentAssignmentStatsRepository.cs` + impl | `AsNoTracking` projection whose only `Where` is the recipient link |
| `Api/Endpoints/StudentStatsRoutes.cs` | relative `/{studentId:guid}/assignment-stats`, `TypedResults.Ok`, no auth or flag code |
| `Core/Extensions.cs` (+3) | the `AddScoped` registration, beside the ward read |
| `Api/AssignmentEndpoints.cs` (+6) | one chained `group.MapStudentStatsRoutes();` on the `/assignments` group |
| 3 test files | C1–C8, C7's tenant isolation, route composition |

**Parent's authoritative validation:** `dotnet build SchoolCollab.slnx` **0 errors**; `Assignments.Tests.Unit -c Release`
**1401 total / 0 failed / 0 skipped**. `git status` matched the table exactly — nothing outside it.

**Deviations reported by the seat, both adjudicated below rather than waved through:**

1. `StudentStatsRouteCompositionTests` asserts on source text, because the test project has no reference to
   `SchoolCollab.Assignments.Api` and no `TestServer` package, so the orchestrator cannot be mapped in-process.
   The seat labelled the test accordingly; the static reviewer confirmed the labelling is accurate.
2. `StudentAssignmentStatRow`'s namespace is `...Core.DTOs` (folder-consistent) rather than `...Data.Repositories`
   where the legacy `AssignmentSummary` sits.



## Review

Seat: `ollama-cloud/deepseek-v4-pro:0813`, static, against `documents/rounds/diffs-student-stats-l2a.patch` (12 files),
with the parent's authoritative numbers attached and instructions not to trust any number in the diff's comments.

```
DIFF REVIEW
verdict: CLEAN
p1: none
p2: none
p3: Source-level route test is honest but must be labelled as such. StudentStatsRouteCompositionTests asserts
    on file text, not a started host: the test project references Core/Contracts/Application/Worker only and has
    no TestServer package, so the orchestrator cannot be mapped in-process — the claim in the test doc is
    accurate. It still proves R2/R3 at source level (relative path, no Results factory, no route-level auth,
    chained from the /assignments group), and tests/SchoolCollab.ArchitectureTests.Unit/
    MinimalApiCompositionArchitectureTests.cs independently enforces the same delegation structure. A reader
    should be told: the test pins source text, so runtime OpenAPI schema emission and actual route
    registration are NOT asserted here — they ride on TypedResults-by-construction and the L2b wire check.
    Also note C6's behavioural half is vacuous (see below); the reflection guard is the real assertion.
criteria:
  C1 met, C2 met (incl. Graded-with-FinalizedAt-null), C3 met, C4 met, C5 met (past/future/null + clock.Advance),
  C6 met-but-weak, C7 met, C8 met (labelled guard)
  R1 met, R2 met, R3 met, R4 met, R5 met
semantics: safe — marked/reviewed/reviewRequired increment only inside the sent+finalized/mandatory guards, so
           Marked<=Submitted, Reviewed<=ReviewRequired, ReviewRequired<=SentOut, and Assigned=rows.Count>=SentOut
           hold for every input
security: sound — route inherits /assignments RequireAuthIfOidc (Bearer + tenant context), same posture as the
          sibling /students ward group; counts-only, EF-global tenant filter (no IgnoreQueryFilters), no
          per-student role gate added and none intended
scope: inside table — the only deviation is the technique of StudentStatsRouteCompositionTests, the same file the
       table names, honestly documented
```

### Parent adjudication of the review

| Finding | Verdict |
|---|---|
| P1 still fixed | **confirmed against the source**: the read's only `Where` is the recipient link; no status predicate in the projection, the fold or anywhere as an `IgnoreQueryFilters` |
| All eight criteria + five reviewer criteria | **accepted**, with C6 downgraded to a recorded residual (below) |
| p3 route test is source-level | **accepted and labelled**: the test pins source text. Runtime OpenAPI schema emission and actual route registration are **not** asserted by it; they rest on `TypedResults` by construction plus L2b's wire check, and `MinimalApiCompositionArchitectureTests` polices the same delegation structure independently |
| C6 met-but-weak | **accepted as a residual, not reworked** — see `## Acceptance` |

## Acceptance

Seat: `ollama-cloud/deepseek-v4-pro:0813`, dispatched on its own seat after the static review. Its verdict:
**RESIDUALS — no rework.** Transcribed verbatim below; the numbers it cites are the parent's, not a child's.

```
ACCEPTANCE
verdict: RESIDUALS
criteria:
- C1 met: GetStudentAssignmentStatsHandlerTests.HandleAsync_FoldsTheSixCounts_FromAPerAssignmentFixture
  (hand-verified 7-row fixture: 7/5/2/1/3/1)
- C2 met: HandleAsync_Marked_RequiresTheSameSubmissionFinalizedAndGraded (FinalizedAt+Pending, and
  Graded-with-FinalizedAt-null both excluded from Marked)
- C3 met: HandleAsync_GateHeldAssignment_LandsInAwaitingReview_NeverAwaitingSubmission
  (Submitted=0, ReviewRequired-Reviewed=1)
- C4 met: HandleAsync_ReviewRequired_CountsOnlyMandatoryReviewAmongSentAssignments (Draft-mandatory excluded)
- C5 met: HandleAsync_Scheduled_CountsAsSentOnlyOnceItsPublishTimeHasPassed (past/future/null +
  clock.Advance; handler reads timeProvider.GetUtcNow)
- C6 met (residual): reflection guard GetProperties().ContainSingle() -> StudentId; behavioural half vacuous
- C7 met: StudentAssignmentStatsRepositoryTenantTests.ListStudentAssignmentStatRowsAsync_ExcludesAnotherTenantsRows
  (tenant A=1, B=2 over InMemory + global Tenant filter)
- C8 met: HandleAsync_IssuesAConstantNumberOfRepositoryCalls_NoNPlusOne (1 vs 25 rows, Calls==1)
- C9 met: dotnet build 0 errors; Assignments.Tests.Unit 1401 total / 0 failed / 0 skipped (parent's numbers)
- R1 met: ContractTypes.cs - WardAssignmentListItemDto unchanged, only StudentAssignmentStatsDto added
- R2 met: StudentStatsRoutes.cs relative path; AssignmentEndpoints.cs chain;
  MinimalApiCompositionArchitectureTests enforces the same structure
- R3 met: StudentStatsRoutes.cs TypedResults.Ok (no Results. factory)
- R4 met: StudentAssignmentStatsRepository.cs AsNoTracking().Select() projection, no SaveChanges
- R5 met: diff is exactly the 12 files in the expected-files table
residuals:
- C6 behavioural half is vacuous (fake repository ignores studentId) ... acceptable because the reflection
  guard on the query type is the criterion's real evidence - a period parameter would appear as a second
  query property and fail ContainSingle - and no placement concept exists in this layer to test.
- Route-composition test asserts on source text, not a started host; acceptable because the test project
  has no Api reference/TestServer, the source assertions still pin R2/R3's code-level properties, and
  MinimalApiCompositionArchitectureTests behaviourally enforces the same delegation structure across src.
rework: none
reader_must_be_told:
- C6's second half compares two folds over a repository that ignores studentId, so it does not model a
  sub-period vs academic-year placement - the "no period filter" guarantee rests on the reflection guard,
  not the equality assertion.
- The route test pins source text, not a started host: runtime route registration and OpenAPI schema
  emission are not asserted here; they ride on TypedResults-by-construction plus L2b's wire check.
```

### Accepted residuals (named here, and repeated in the report to the owner)

| Residual | Why it is acceptable at this layer | Where it is closed |
|---|---|---|
| **C6's behavioural half is vacuous** — `FakeStatsRepository` ignores `studentId`, so the placement-equality assertion folds the same rows twice | The "no period filter" guarantee rests on the **reflection guard**: the query type's only property is `StudentId`, so a period parameter would fail `GetProperties().ContainSingle()`. There is no placement concept in this layer to model against | L2b's wire check exercises the real read end to end |
| **The route test pins source text**, not a started host | The test project has no `Assignments.Api` reference and no `TestServer`; in-process mapping is impossible there. `TypedResults` is typed by construction, and `MinimalApiCompositionArchitectureTests` enforces the delegation structure across `src` at build level | L2b's live probe hits the route for real |

**Verdict: CLOSED, with the two residuals above.** L2a is accepted: the Assignments-side query, endpoint and
tests are in place, the P1 that the plan review caught is fixed and independently confirmed, and nothing
outside the plan's file table changed. L2a is **not committed** — the commit gate is the owner's, and the
branch is still carrying only layer 1's `a3c61014`.



## Not run

| Step | Reason | Owner |
|---|---|---|
| UI mock gate (skill step 2c) | L2a touches no user-visible surface — its expected files contain no `.razor`, `.razor.css`, `.css`, `.js`, nothing under `wwwroot/`. The mock belongs to L2b, which does | parent |
| Live probe | Nothing to look at until L2b's rail panel exists; the probe runs in L2b | parent |
