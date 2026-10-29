Provider: pi — parent on `ollama-cloud/deepseek-v4.1-flash:1017`; Tier 2 plan gate on `ollama-cloud/deepseek-v4-pro:0813` (a different seat from the plan author, per skill step 2b).
Mode: attended
Round base: `f864bfac` — tree clean at round start (`git rev-parse HEAD` = `f864bfac` on `feat/student-view-stats-l2a`).
Tier override (owner Q146, 2026-10-28): **L2b runs Tier 2**, against the skill's Tiers table which puts endpoint + multi-context + UI at Tier 3. Recorded here so a later reader sees the override, not a mistake.
Mock gate (owner Q148): satisfied by the **already-approved** `.pi/mock/student-view-v1.html` § rail *Assignments* (plus `state-light/dark/narrow.png`). The console-box-drawing format was **not** re-rendered; this substitution is a recorded decision, not an omission.

# Round — student assignment stats, layer L2b: the Students port, endpoint and rail panel

Implements the **L2b** half of [#444](https://github.com/stayboy/School-Collab/issues/444), on top of L2a ([#447](https://github.com/stayboy/School-Collab/pull/447)). L2a delivered the Assignments query and endpoint; **nothing in this round may change `src/Assignments/**`**.

## Settled by the owner

| # | Ruling |
|---|---|
| Q123 | Students.Api enriches — port in `Students.Core`, client in `Students.Api`; `studentsApi.WithReference(assignmentsApi)` is already wired (`AppHost/Program.cs:386`), so **no AppHost change and `CrossModuleWiringTests` untouched** |
| Q124 | The stats panel is the **first** panel in the rail |
| Q139 | **No period filter** — the counts join on the student, so #427 cannot reach this panel |
| Q145 | Probe fixtures live in a **dev-only script** under `documents/runbooks/`, never in `MigrationService` (which ships to every environment) |
| Q148 | The mock gate is satisfied by the approved HTML mock; no console re-render |
| Q151 | **Reuse the existing `assignments-api` registration** (`propagateTenant: false` + `TenantForwardingDelegatingHandler` + `BearerForwardingDelegatingHandler`, `Students.Api/Program.cs:85-92`), **not** a second named client. This *corrects* Q142's mechanism: the tenant-exactness comes from the handler chain, not from `propagateTenant: true` |
| Q152 | The six counts are **mirrored** in `Students.Core`; `Students.Application` gains **no** `Assignments.Contracts` reference. *(Pin attribution corrected after plan review: the guarantee comes from this round's new `StudentAssignmentStatsPortPlacementTests`. `TeacherScopeWiringArchitectureTests.CrossContextProjectReferences_DoNotGrow` counts Assignments→Students edges, **not** Students.Application→Assignments — quoting it here would have been a false citation.)* |
| Q153 | Hop failure renders a distinct **unavailable** state with **no numbers** — zeros are a claim, unavailable is a fact |
| Q154 | `Assigned = 0` renders **no bars** — a bar at 0% implies "0% of something" |
| Q155 | Funnel segments are **derived in the UI**; a **negative** difference means an inconsistent payload and renders the **unavailable** state — never clamped |
| Q156 | The dev-only seed script **and** the guardian-card probe ride in this round |
| Q157 | Four tests: render, client (path + tenant/bearer headers), port-placement guard, degradation. **Extend** the two existing fixtures rather than adding near-duplicates |

## Plan

### Goal

Put the six counts L2a already serves in front of a human: a port in `Students.Core`, an HTTP impl in `Students.Api` on the existing cross-module client, a `GET /students/{id}/assignment-stats` endpoint, a client method in `Students.Application`, and one rail panel that renders tiles, a funnel bar and a review bar — degrading to "unavailable" without ever failing the page.

### Scope — expected files

| File | Change |
|---|---|
| `src/Students/SchoolCollab.Students.Core/DTOs/StudentAssignmentStats.cs` | **new** — the six-int mirror `(Assigned, SentOut, Submitted, Marked, ReviewRequired, Reviewed)` + documented derived invariants. Same property names as `Assignments.Contracts.StudentAssignmentStatsDto` so `System.Text.Json` binds without converters; **no enum crosses this hop** (all six are `int`) |
| `src/Students/SchoolCollab.Students.Core/Services/IStudentAssignmentStatsReader.cs` | **new** — `Task<StudentAssignmentStats> GetAsync(Guid studentId, CancellationToken ct)`; **throws** when the Assignments API is unreachable, matching the `IActivityGroupAssignmentQuery` precedent (`Students.Core/Services/IActivityGroupAssignmentQuery.cs`) |
| `src/Students/SchoolCollab.Students.Api/Services/StudentAssignmentStatsReaderHttpClient.cs` | **new** — implements the port over the **existing** named client `assignments-api`; `GET assignments/{studentId}/assignment-stats` (the `/assignments` segment is **required**: that client's base address is the host root, and L2a's route lives on the `/assignments` group) |
| `src/Students/SchoolCollab.Students.Api/Program.cs` | **one line** — `AddScoped<IStudentAssignmentStatsReader, StudentAssignmentStatsReaderHttpClient>()` beside the activity-group query registration at `:94`. **No new `AddCrossModuleHttpClient`** (Q151) |
| `src/Students/SchoolCollab.Students.Api/Endpoints/StudentStatsRoutes.cs` | **new** — `MapStudentStatsRoutes(this RouteGroupBuilder group)`, relative `/{id:guid}/assignment-stats`, `TypedResults.Ok`; on a hop failure returns a logged problem. **The route lives here, not in the orchestrator** (`endpoint-organization-pattern.md` §2/§6; `MinimalApiCompositionArchitectureTests.Orchestrators_MapNoRoutesInline` asserts `MapGet(` count 0 in `StudentEndpoints.cs`) |
| `src/Students/SchoolCollab.Students.Api/StudentEndpoints.cs` | **one chained call** — `studentsGroup.MapStudentStatsRoutes();` beside the other `*.Routes()` chains. **No new route, and no sixth `RequireAuthIfOidc`**: `BearerForwardingWiringArchitectureTests.RequireAuthIfOidc_PerGroup_StudentsAssignmentsAndSettings` pins that count at 5, and the route inherits the group's policy |
| `src/Students/SchoolCollab.Students.Application/Services/StudentsApiClient.cs` | **one method** — `GetAssignmentStatsAsync(Guid id, CancellationToken ct = default)` returning `StudentAssignmentStats?`: `try/catch` (`HttpRequestException`, `JsonException`) → `null` + `_logger.LogWarning`. **This is new behaviour, not an inherited convention** — that class's reads use `GetFromJsonAsync` and throw, and `_logger` is injected but unused today |
| `src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Detail.razor` | the rail panel (first in the rail) + its load in `OnInitializedAsync`'s fan-out + the three render states (loaded / zero / unavailable) |
| `src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Detail.razor.css` | only the styles the new markup consumes: stat tiles, funnel stack bar, review bar, unavailable line |
| `tests/SchoolCollab.Admin.Tests.Unit/StudentViewRendersTests.cs` | **extend** — the loaded state (tiles/bar labels), the **zero** state, the **unavailable** state (hop 500 → no numbers, rest of the page still renders) |
| `tests/SchoolCollab.Admin.Tests.Unit/StudentDetailSectionsTests.cs` | **extend** — the panel's section identity and its **first-in-rail** position; the no-tabs pin stays |
| `tests/SchoolCollab.Students.Api.Tests.Unit/StudentAssignmentStatsReaderHttpClientTests.cs` | **new** — (i) the request path is `assignments/{studentId}/assignment-stats` on the `assignments-api` base address — **including the `/assignments` prefix**, which the first draft of this plan omitted and which would have 404'd into a convincing "unavailable"; (ii) the tenant + bearer handlers are attached, by wrapping the **real** `TenantForwardingDelegatingHandler` (stub `IHttpContextAccessor` carrying `x-tenant-id`) and `BearerForwardingDelegatingHandler` (`DisableOIDCAuth=false`, inbound `Authorization`) around a capturing inner handler; (iii) a `ReadFromJsonAsync` **round-trip** asserting all six values, so a misspelled mirror member fails here instead of deserializing to 0 |
| `tests/SchoolCollab.Students.Api.Tests.Unit/StudentStatsRoutesTests.cs` | **new** — the endpoint's own behaviour (`StudentTargetRoutesTests` precedent): a 200 with the six counts when the port answers, and a non-2xx **problem** when the port throws. This is what makes Open decision A falsifiable instead of merely declared |
| `tests/SchoolCollab.ArchitectureTests.Unit/StudentAssignmentStatsPortPlacementTests.cs` | **new** — port interface in `Students.Core`, impl in `Students.Api`, and **both** `Students.Application` **and** `Students.Core` carry no Assignments assembly reference (R1's full claim; the pin Q152 actually rests on) |
| `tests/SchoolCollab.ArchitectureTests.Unit/OpenApiSurfaceArchitectureTests.cs` | **one constant** — `StudentsSchemaPin` **3 → 4**. The new typed `Ok<StudentAssignmentStats>` endpoint adds a schema-bearing operation, and `StudentsApi_DocumentCoversEveryMappedOperation` asserts that count exactly |
| `documents/runbooks/student-view-probe-seed.md` (+ its `.sql`) | **new** — dev-only fixtures: assignments in draft / sent / submitted-unmarked / marked / gate-held statuses **and a guardian**, so the panel probe and the deferred guardian-card probe (Q141) both run in one AppHost session. Never `MigrationService` (Q145) |
| `documents/solution/student-view-redesign.md` | §8 implementation record: findings → implementation, with the rendered proof |
| `documents/rounds/round-student-stats-l2b.md`, `documents/rounds/diffs-student-stats-l2b.patch` | this doc + the frozen diff |

Nothing else. If the diff needs a file outside this table, that is a plan defect: stop and report it rather than widening silently.

### Display semantics — what each control claims

| Control | Claim | Denominator |
|---|---|---|
| Panel count (header) | `Assigned` | — |
| Tile `Assigned` / `Sent out` / `Submitted` / `Marked` | the four progress counts, verbatim from the DTO | — |
| Funnel bar, 4 segments | `draft = Assigned − SentOut`, `awaiting submission = SentOut − Submitted`, `awaiting marking = Submitted − Marked`, `marked = Marked` | **`Assigned`** |
| Review bar, 2 segments | `reviewed = Reviewed`, `awaiting review = ReviewRequired − Reviewed` | **`ReviewRequired`** — the foot text says "required on N of the M sent", **never** "of Assigned" |
| Unavailable line | "Assignment stats are unavailable right now." (one muted line, **no tiles, no bars**) | — |
| Zero line | "No assignments for this student yet." (tiles at 0, **no bars**) | — |

Colour never carries the meaning alone (Q141 of the page round): every segment is named in text beside or beneath the bar; decorative dots are `aria-hidden`; the bar carries an accessible label naming the four counts.

**The three states are a tri-state, and it needs an explicit signal.** `null` from the client means both "not loaded yet" and "failed", so the page carries a separate `_assignmentStatsUnavailable` flag: `null && !unavailable` = spinner, `null && unavailable` = the muted line, non-null = the panel. Without that flag the panel either flashes "unavailable" during every load or never shows it at all.

**Load sequencing is part of the contract.** The stats load is its own sequential `await` **after** `_student` has been assigned — never folded into the first `Task.WhenAll(studentTask, enrollmentsTask, …)`. Folded in, a throwing await rejects before `_student` is set, and the whole page collapses to a spinner plus an error bar; sequenced after, the worst case is one unavailable panel.

### Acceptance criteria

| # | Criterion | Evidence |
|---|---|---|
| C1 | The panel is the **first** rail panel, titled `Assignments`, its header count equals `Assigned` | `StudentDetailSectionsTests` — asserts on **markup**, not on a comment |
| C2 | The four tiles read `Assigned`, `Sent out`, `Submitted`, `Marked` with the DTO's values | render test on the loaded state |
| C3 | The funnel bar totals **`Assigned`** and its four segments are the derived differences | render test: segment widths/labels sum to Assigned for a hand-picked payload |
| C4 | The review bar totals **`ReviewRequired`**, and its foot text reads "required on N of the **M sent**" | render test with `Assigned=24, SentOut=21, ReviewRequired=12` — a payload where `of Assigned` and `of Sent out` differ, so the assertion can fail |
| C5 | `Assigned = 0` renders tiles at 0, **no bars**, and the explicit empty line | render test; a bar element in that state fails the test |
| C6 | A failed hop renders **no numbers at all** — one muted line — and the page's other panels still render | render test with the scripted handler returning 500 for the stats route only. **Premise corrected after plan review:** the client must `try/catch` (`HttpRequestException`, `JsonException`) and return `null` with `_logger.LogWarning` — the reads in that class use `GetFromJsonAsync`, which **throws** on non-success, and `_logger` is injected but currently unused. "Following the existing convention" would have thrown and broken this criterion |
| C7 | A **negative** derived difference renders the unavailable state, not a clamped chart | render test with an inconsistent payload (`Marked > Submitted`) |
| C8 | The outbound hop is authenticated **and** tenant-exact: the request carries the tenant header and the bearer token, to `assignments/{studentId}/assignment-stats` on the `assignments-api` base address | `StudentAssignmentStatsReaderHttpClientTests` over the **real handler chain** — a bare mocked factory yields a handler-less `HttpClient` with no headers, which would make the assertion vacuous |
| C9 | The Students endpoint returns a typed result and serves the documented path; `StudentEndpoints.cs` still contains **exactly five** `RequireAuthIfOidc` calls and **zero** inline `MapGet(` | `StudentStatsRoutesTests` (behavioural: 200 with counts, problem when the port throws) + the two existing architecture pins |
| C10 | **Neither** `Students.Application` **nor** `Students.Core` references an Assignments assembly; the port interface is in `Students.Core`, its impl in `Students.Api` | `StudentAssignmentStatsPortPlacementTests` — plus a negative assertion that the mirror binds all six values (C8 iii), so a silent zero cannot pass |
| C11 | Build + the CI-parity gate are green, and the live probe is recorded | `dotnet build SchoolCollab.slnx`; `scripts/round/gates.sh --serial`; probe transcript |

### Reviewer acceptance criteria (Tier 2 adds these)

R1 No `Assignments.Contracts` (or any Assignments assembly) reference from `Students.Application` or `Students.Core`.
R2 **No new** `AddCrossModuleHttpClient` registration — the port rides the existing `assignments-api` client, whose handler chain already supplies tenant + bearer.
R3 `src/Assignments/**` is untouched: L2a shipped that side, and a diff that reaches back into it means the wire shape was wrong.
R4 The panel is **inline markup**, not a new component (one call site — component-first §2/§6), and adds no tabs.
R5 `Detail.razor.css` additions are only what the new markup consumes: no dead selectors, no literal hex where an `app.css` token exists, no `#fafafa`-class dead v4 token.
R6 Nothing outside the expected-files table changed — **including** the two inventory assertions this round must update rather than discover in CI: `StudentViewRendersTests`' `.region-grid__rail > *` count (3 → 4) and `StudentDetailSectionsTests.Detail_Renders_Each_Block_As_A_Panel_In_The_Documented_Order`'s rail list, which gains the new block **first**. Both are named in the table; a diff that leaves either stale is R6-violating, not "pre-existing".

### Open decisions (declared, not silently decided)

**A. What the Students endpoint returns when the Assignments hop throws — 5xx, or 200-with-a-flag?**
➡️ **5xx (`Problem`), with the hop failure logged distinctly at the endpoint; the UI client catches and returns `null`.** A 200 carrying `available: false` would make the failure invisible to telemetry — the one place a cross-context breakage should be loud. *(Corrected after plan review: the earlier draft justified this by an "existing log-and-return-null convention" in `StudentsApiClient`, which does not exist — its reads use `GetFromJsonAsync` and throw, and `_logger` is injected but unused. The catch is therefore **new code this round must add**, not an inherited behaviour, and `StudentStatsRoutesTests` asserts the non-2xx side of it.)*

**B. Where the funnel arithmetic lives.**
➡️ **In the UI, from the six counts** (Q155), never in a pre-computed field on the DTO. A `Draft` count on the wire would make the invariant `Draft = Assigned − SentOut` unverifiable — two numbers that can disagree. Deriving means one source of truth, and the negative case is detectable: it renders unavailable.

**C. The mirror's property names.**
➡️ **Identical to `StudentAssignmentStatsDto`'s.** `System.Text.Json` binds by name, so a mirror with different names (`Required` vs `ReviewRequired`) would silently deserialize to zero — the worst failure mode available. All six members are `int`, so the enum-as-number hazard that bit the L2a render fixture does not apply here.

**D. Where the panel's load sits in `OnInitializedAsync`.**
➡️ **Its own sequential `await`, after `_student` is set** — see the sequencing paragraph under `### Display semantics`. The measured failure mode if folded into the first `Task.WhenAll`: the throwing await rejects before `_student = studentTask.Result`, so the page renders a spinner plus an error bar and *skips the later-sequenced loads*. Sequenced after, a stats failure costs one muted line and nothing else. The render test (C6) pins the difference.

**E. The seed fixtures' form.**
➡️ **A `.sql` script beside a runbook `.md` that documents how to run it** (`psql` against the dev container), not a C# seeding path and emphatically not `MigrationService` (Q145). The script is idempotent by fixed GUIDs and prefixed rows, so re-running it on a probe database converges rather than duplicating.

## Plan review (skill step 2b — iteration 1)

Seat: `ollama-cloud/deepseek-v4-pro:0813` — a different seat from the plan's author. Verdict: **NO-GO**, three P1s.

```
PLAN REVIEW
verdict: NO-GO
p1:
- Route placed inline in the orchestrator. The file table puts GET /{id:guid}/assignment-stats in
  StudentEndpoints.cs "on the existing studentsGroup". endpoint-organization-pattern.md §2/§6: "The
  orchestrator file … must NOT contain any inline endpoint mappings", and
  MinimalApiCompositionArchitectureTests.Orchestrators_MapNoRoutesInline asserts
  Count(StudentEndpoints.cs, ".MapGet(") == 0. Fails at test time. Fix: add
  Endpoints/StudentStatsRoutes.cs + one chain call in the orchestrator.
- OpenAPI pin will break, and the file is missing from the table. OpenApiSurfaceArchitectureTests
  .StudentsSchemaPin = 3; a typed Ok<StudentAssignmentStats> endpoint makes it 4.
- Client URL omits the /assignments prefix. The shipped L2a route is
  GET /assignments/{studentId}/assignment-stats; the client would 404 and the panel would render
  "unavailable" against a working endpoint, and C8's test would ENCODE the wrong path.
p2:
- "null on failure with a logged warning, matching the existing convention" is false on both halves:
  reads use GetFromJsonAsync (throws on non-success); _logger is injected but never used. C6 hinges on
  the client catching the 500 — state an explicit try/catch → null + LogWarning.
- Degradation tri-state unspecified: null is both "not loaded" and "failed" — needs a distinct flag.
- C8 is assertable only over the REAL handler chain; a bare mocked factory yields no tenant/bearer headers.
- Mirror binding unguarded: a misspelled member deserializes to 0 with no automated failure — add a
  ReadFromJsonAsync round-trip asserting all six.
p3:
- Q152's pin attribution is wrong: TeacherScopeWiringArchitectureTests counts Assignments→Students edges,
  not Students.Application→Assignments.
- C9's "the route test" is not in the file table.
- Two existing tests hard-code the panel inventory and will break: StudentViewRendersTests'
  `.region-grid__rail > *` count (3 → 4) and StudentDetailSectionsTests' rail-block list.
criteria_honesty: C1-C5 hold; C6 weak (premise contradicted); C7 holds; C8 weak (wrong path, header
  assertion needs the real chain); C9 weak (evidence not in the table); C10 holds; C11 holds;
  R1-R5 hold; R6 weak (cannot pass with the fixes the P1s require).
wire: broken — names sound, all int, no enum crossing; but the client path omits /assignments and
  nothing pins the JSON-name binding.
degradation: sound IF the client catches; unsafe if it throws (folded into the first Task.WhenAll it
  leaves _student null → spinner + error bar, whole page fails). Pin the load as a sequential await.
file_table: wrong — route must move to Endpoints/StudentStatsRoutes.cs; OpenApiSurfaceArchitectureTests.cs
  missing. All other paths real and correctly placed.
```

### Adjudication (parent, verified against the source — not accepted on the reviewer's word)

| Finding | Verified how | Verdict |
|---|---|---|
| **P1 route inline in the orchestrator** | `grep 'MapGet(' StudentEndpoints.cs` → **zero** hits; an `Endpoints/` folder exists; `Orchestrators_MapNoRoutesInline` walks `<Domain>Endpoints.cs` and asserts no inline mappings (only `LeafGroupExemptions` skip) | **correct** — plan amended: new `Endpoints/StudentStatsRoutes.cs` + one chained call |
| **P1 OpenAPI pin 3 → 4** | `StudentsSchemaPin = 3` at `OpenApiSurfaceArchitectureTests.cs:66`, asserted by `StudentsApi_DocumentCoversEveryMappedOperation` | **correct** — the constant bump joins the file table |
| **P1 client path missing `/assignments`** | `MapGroup("/assignments")` in `AssignmentEndpoints.cs` + relative `/{studentId:guid}/assignment-stats` in `StudentStatsRoutes.cs:22` | **correct, and the worst of the three**: the panel would show "unavailable" against a healthy endpoint and every test would agree |
| P2 client convention is not "log and return null" | reads call `GetFromJsonAsync`; `_logger` declared `:682`, ctor `:688`, never used | **correct** — the catch is new code, and C6 now says so |
| P2 tri-state needs a flag | `null` is ambiguous by construction | **correct** — `_assignmentStatsUnavailable` added to the display contract |
| P2 C8 needs the real handler chain | the client resolves a named factory client; a mocked factory has no handlers | **correct** — the test wraps the real tenant/bearer handlers |
| P2 mirror binding unguarded | identical names bind, but a typo yields `0` silently | **correct** — round-trip assertion added to C8 |
| P3 Q152 pin attribution | `TeacherScopeWiringArchitectureTests` pins Assignments→Students edges | **correct** — citation replaced with the new test |
| P3 C9 evidence missing from the table | — | **correct** — `StudentStatsRoutesTests` added, which also makes Open decision A falsifiable |
| P3 two inventory tests will break | rail child count and rail-block list | **correct** — both named in R6 |

Every finding was upheld; none was waved through unverified, and none was dismissed. The plan above is the amended one.


## Plan review (skill step 2b — iteration 2, FINAL)

Seat: `ollama-cloud/deepseek-v4-pro:0813`. Verdict: **NO-GO** — on a contradiction inside this document,
not on the design.

```
PLAN REVIEW (iteration 2)
verdict: NO-GO
p1: The StudentAssignmentStatsReaderHttpClient.cs row still says GET {studentId}/assignment-stats
    "relative to that client's base address" - the named client's base is https+http://assignments-api
    (host root), so it resolves to /{studentId}/assignment-stats and 404s against the shipped L2a route
    /assignments/{studentId}/assignment-stats. It contradicts C8 and the test row, which were corrected.
    This is iteration-1 P1 #3 not closed in the file table - the row the worker implements from.
p2: The StudentsApiClient.cs row still ends "matching the existing nullable-return convention of that
    class" - false on both halves (reads use GetFromJsonAsync, which throws; _logger is declared and
    never used). C6 and Open decision A correctly repudiate it, so the row contradicts the criterion.
p3: R1's "or Students.Core" half is not covered by the port-placement test's stated scope.
p1_closure: #1 route-inline -> fixed (Endpoints/StudentStatsRoutes.cs + chained call, matches the
            this RouteGroupBuilder convention, keeps Orchestrators_MapNoRoutesInline green);
            #2 OpenAPI pin -> fixed (row added, StudentsSchemaPin 3 -> 4 verified at :66);
            #3 client path -> PAPER ONLY (C8 and the test row corrected, the implementation row not).
new_defects: none - the amendment's new rows are correctly placed and shaped.
inventory_tests: confirmed - StudentViewRendersTests asserts .region-grid__rail > * == 3 ("Activity
            groups, Subjects, Contact history") and .region-grid__main > * == 4; StudentDetailSectionsTests
            holds railBlocks = [groups, subjects, contact-history] and asserts in order. Stats panel first
            => rail count 3 -> 4 and the new Id at index 0. The stated change is right.
route_test_feasible: yes - the StudentTargetRoutesTests.BuildApp precedent (WebApplication.
            CreateBuilder + UseTestServer, stub IStudentAssignmentStatsReader + stub IFeatureFlagService
            with DisableOIDCAuth=true, MapStudentEndpoints). No WebApplicationFactory<Program>, no Postgres.
criteria_left_weak: R1/C10 - the test must also assert Students.Core carries no Assignments reference.
scope: complete.
```

### Adjudication and the parking ruling (parent)

| Finding | Verdict | Action |
|---|---|---|
| **P1: the implementation row still carried the wrong path** | **correct, and the most useful finding either gate produced** | Fixed. The `/assignments` segment is now stated **in the row the worker reads**, with the reason. C8 and the test row had been corrected while the implementation row was left stale — the document said two different things and the worker would have implemented the wrong one |
| P2: the false "existing nullable-return convention" clause | **correct** | Fixed. The row now says the `try/catch` is **new behaviour, not an inherited convention** |
| P3: R1's `Students.Core` half uncovered | **correct** | Fixed — C10 and the test row both name `Students.Application` **and** `Students.Core` |
| Route test feasibility | **confirmed viable** via the `StudentTargetRoutesTests.BuildApp` precedent | `StudentStatsRoutesTests` stays in the table |
| Inventory tests 3 → 4 | **confirmed literally** | R6 unchanged |
| New defects from the amendment | none | — |

**The round is PARKED at the plan gate.** Two iterations, two NO-GOs. The skill's budget is one revision
and a single further pass; that budget is now spent, so this round does **not** proceed to a worker on the
author's own authority. The three corrections above are applied because leaving a known-false sentence in a
durable document is worse than the park — but applying them is **not** a substitute for a verdict.

What is on record, and what a reader should take from it: the design survived both gates intact (route
placement, pin bump, wire path, degradation, tri-state, sequencing all confirmed), and the only blocking
defect at the end was **the plan arguing with itself**. A reviewer reading a table and its criteria
separately cannot catch that; a writer who edits three of four places can.

Resolution needs an owner ruling: either accept the three applied corrections as mechanical text fixes and
dispatch the worker, or authorise a further gate pass against the corrected document (re-opening the
budget deliberately rather than by drift).

## Worker Report

Seat: `ollama-cloud/deepseek-v4.1-flash:1017`. Transcribed by the parent. The numbers in the table are the
**parent's own re-measurement**; the seat's counts matched but are not the evidence.

**Delivered — 19 files in the frozen patch** (1777 lines, `documents/rounds/diffs-student-stats-l2b.patch`),
exactly the amended file table.

| Piece | Delivered |
|---|---|
| Mirror + port | `Students.Core/DTOs/StudentAssignmentStats.cs` (six ints, identical member names), `Students.Core/Services/IStudentAssignmentStatsReader.cs` (throws — the `IActivityGroupAssignmentQuery` precedent) |
| Hop | `Students.Api/Services/StudentAssignmentStatsReaderHttpClient.cs` — **`GET assignments/{studentId}/assignment-stats`** over the existing named client |
| Route | `Students.Api/Endpoints/StudentStatsRoutes.cs` + **one chained call**; orchestrator `MapGet(` = **0**, `RequireAuthIfOidc` = **5** |
| Registration | `Program.cs` — one `AddScoped`; **no** new `AddCrossModuleHttpClient` |
| UI client | `GetAssignmentStatsAsync` with `try/catch (HttpRequestException)` and `(JsonException)` → `null` + `LogWarning` |
| Page | rail panel **first**, `_assignmentStats` + `_assignmentStatsUnavailable` tri-state, `Funnel` derived with a **negative → null (never clamped)** guard, sequential `await LoadAssignmentStatsAsync(ct)` as step 5 |
| Pin | `StudentsSchemaPin` 3 → **4** |
| Fixtures | `StudentViewRendersTests` rail count 3→4 + 5 new renders; `StudentDetailSectionsTests` rail list gains `student-view-assignments` first |
| Probe | `documents/runbooks/student-view-probe-seed.md` + `.students.sql` + `.assignments.sql` (dev-only, not `MigrationService`) |

**Parent's authoritative validation:** `dotnet build SchoolCollab.slnx -c Release` **0 errors**;
`Admin.Tests.Unit` **870/0/0**, `Students.Api.Tests.Unit` **37/0/0**, `Students.Tests.Unit` **695/0/0**,
`ArchitectureTests.Unit` **160/0/0**.

**Deviations, each adjudicated (all inside the plan's scope):**

1. **Two seed `.sql` files, not one** — the fixtures span `students-db` and `assignments-db`; one file cannot address both. **Accepted** — a `\connect` would prompt for a password mid-script.
2. **The review bar is omitted when `ReviewRequired = 0`** — Q154 applied to this axis. **Accepted** — same reasoning as the funnel: a 0% bar claims a fraction of nothing.
3. **No review percentage and no two review tiles** — the plan's display table listed a 2-segment review bar plus foot text; the delivered caption is stated over its own denominator (`ReviewBarLabel` names "required on {ReviewRequired} of the {SentOut} sent"). **Accepted** — C4's requirement is the wording, and the wording is there and asserted.
4. **The stats load is a sequential `await`, not a fan-out member** — **required**, not a deviation: the plan's sequencing paragraph is binding and the file table's word "fan-out" is read through it.
5. **The runbook carries a `## Symptom` block** — mandatory: `RunbookSymptomRouterTests` fails otherwise (the seat's own run caught it, 2 red → green). **Accepted** — the repo's guard working on its author.
6. **An empty stats body throws `HttpRequestException`** rather than returning six zeros — **accepted**: `dotnet-best-practices.md` forbids `InvalidOperationException` from repo code, and the type keeps the "hop failed" meaning the endpoint already handles. Returning zeros would be the Q153 lie in a different costume.

**Not done by the seat:** the live probe (C11) and `scripts/round/gates.sh` — both are the parent's, per the brief. §8.7 records them as not run rather than claiming them.



## Review

Seat: `ollama-cloud/deepseek-v4-pro:0813`, static, against `documents/rounds/diffs-student-stats-l2b.patch`
(19 files), with the parent's authoritative numbers attached and the note that C11 had not yet run.

```
DIFF REVIEW
verdict: CLEAN
p1: none
p2: none
p3: (1) The UI client's try/catch is not independently pinned: if GetAssignmentStatsAsync threw instead of
    returning null, LoadAssignmentStatsAsync's own catch (Detail.razor:1596-1607) would still set
    _assignmentStatsUnavailable=true, so the C6 render test passes either way. The end-to-end criterion is
    honestly verified; the client-level catch is a redundant belt, not a tested contract.
    (2) The negative guard covers the four derived DIFFERENCES only (Detail.razor:739-749); a raw negative
    count (e.g. reviewed:-1, reviewRequired:5) would pass the guard and the foot would read "-1 of 5
    reviewed". Not producible by the shipped L2a handler (it counts rows, all >= 0), and the plan scopes
    the guard to "negative difference" - a defensive note, not a defect.
criteria: C1-C10 all met (C4's ".Contain("required on 12 of the 21 sent")" + ".NotContain("of 24")" pins the
    exact wording; C5's zero state asserts no bar; C6's 500 route empties tiles/bar/count; C7's
    Marked 25 > Submitted 18 renders unavailable; C8 asserts the path, the real-handler-chain headers and a
    six-value JSON round trip; C9's route tests give 200-with-counts and a 502 problem); R1-R6 all met
p1_closure: all three still fixed - route in Endpoints/StudentStatsRoutes.cs:26 with zero MapGet( in the
    orchestrator (:28 chains only); StudentsSchemaPin = 4 (:68); the hop path carries the /assignments
    segment (:37), and the client test asserts AbsolutePath == "/assignments/{id}/assignment-stats"
degradation: chain intact: reader throws -> endpoint catch (not OCE) returns 502 Problem -> UI client
    GetFromJsonAsync throws -> catch returns null -> _assignmentStatsUnavailable=true -> unavailable line, no
    numbers; the sequential await at :1152 is genuinely after _student = studentTask.Result at :1079
display_truth: safe - all four derived differences guarded (<0 -> null, never clamped); funnel sums to
    Assigned, review sums to ReviewRequired; foot + aria-label say "required on N of the M sent"; every
    segment named in legend/foot; swatches aria-hidden; Assigned=0 renders no bars
deviations: all six in scope (see the adjudication below)
scope: inside table
```

### Parent adjudication of the review

| Finding | Verdict |
|---|---|
| CLEAN, all C1–C10 and R1–R6 met | **accepted** |
| p3 (1): the client's `try/catch` is a redundant belt — C6's render test would pass on the page's own catch either way | **accepted as a recorded residual.** The end-to-end behaviour is pinned; the client-level catch is defence in depth. It is not worth a synthetic test that reaches into a private path, and saying so is better than a test that pretends to prove it |
| p3 (2): a raw negative *count* would pass the difference guard | **accepted as a note, no change.** The shipped L2a handler counts rows, so a negative count is unreachable; the plan scoped the guard to differences (Q155). Widening it would be guarding an input no producer can emit |
| All six declared deviations in scope | **accepted** — the two that looked like scope drift (two `.sql` files, omitted zero-review bar) are consequences of Q154 and of the two-database split, both adjudicated in `## Worker Report` |



## Live probe (C11) — run by the parent, after the static review

AppHost from this worktree, real dev database, `msedge` via Playwright at 1440x1300, probe student
`5c0a0000-…001` seeded from `student-view-probe-seed.{students,assignments}.sql` under tenant *Hydeson School*.

**Healthy state — every number the runbook predicts, and nothing else:**

```
RAIL_IDS: [student-view-assignments, student-view-groups, student-view-subjects, student-view-contact-history]
Assignments (6)
  6 Assigned   5 Sent out   2 Submitted   1 Marked      (the Marked tile is the only tinted one)
  bar[0] aria-label: "6 assigned: 1 not sent, 3 awaiting submission, 1 awaiting marking, 1 marked"
  bar[1] aria-label: "Guardian review: 1 reviewed, 1 awaiting review, required on 2 of the 5 sent"
  foot: "1 of 2 reviewed · 1 awaiting — required on 2 of the 5 sent."
  segments: 4 funnel (flex 1/3/1/1) + 2 review (flex 1/1); 5 decorative nodes aria-hidden
  headings inside the panel: exactly one (H4 "Assignments")
  console errors: none
```

The two things only a browser could settle: the panel is **first in the rail**, and the review axis states its
own denominator (*"of the 5 sent"*, never *"of 6"*) in the rendered foot, not merely in a test string.

**Degraded state — `assignments-api` (pid 10952) stopped, page reloaded, polled:**

```
after 30s (the cross-module client's retry backoff): "Assignments — Assignment stats are unavailable right now."
count chip: empty | tiles: none | bars: 0 | main children: 4 | rail children: 4 | error bar: none
```

No numbers at all (Q153), and every other block still renders (C6) — the criterion proved end to end rather
than argued from a stubbed 500.

### Two defects the probe found in the delivered seed fixtures

Both were written by the implementer and **never executed** — exactly what C11 exists to catch.

| Defect | Evidence | Fix |
|---|---|---|
| `gender_coded_value_id` is NOT NULL and the script inserted `NULL` | `ERROR: null value in column "gender_coded_value_id" of relation "students" violates not-null constraint` — the script aborted on its first run | The script now inherits the value from an existing student (`\gset`) or takes `-v gender_id=<guid>`, with a hard stop and a message if neither is available. No environment GUID is hard-coded |
| The script seeded **three** sent+mandatory rows while its own runbook table documents **two**, so the self-check printed `review_required 3` against a documented 2 | the fixture row *"awaiting submission"* carried `mandatory_review = true` | Set to `false`: a gated assignment belongs in *awaiting review* and must not double as a second review fixture. Self-check now prints `6 / 5 / 2 / 1 / 2 / 1`, exactly the documented expectation |

**Second finding is the sharper one:** the script *and* the runbook shipped together and disagreed, and only
execution could reveal it. A fixture that asserts its own counts is worth nothing until someone runs it.

**Cleanup:** probe rows deleted from both databases (students back to its original 3), AppHost stopped, this
worktree's six containers removed, the main checkout's `postgres-057ea9d7` / `rabbitmq-057ea9d7` untouched.

**Gate:** `scripts/round/gates.sh --serial` — build 0 errors, integration 151 with only the 2 known OpenRouter
live failures, cross-module 2/0, MAUI 0 errors, Portals 88, AuthPortal 123, and the **unit leg failed once**
on `CodedValueDropdown_WithSelectedId_MarksOptionSelected` (`Settings.Tests.Unit`, a bUnit `WaitForElement`
timeout, "Total render count across all components: 0"). Isolated: **3/3 standalone passes**, and the leg
re-run with the identical command came back **4670 / 0 / 0**. Recorded as a flake, not an L2b regression —
the file it lives in is untouched by this round. **The durable record of that failure is [#443](https://github.com/stayboy/School-Collab/issues/443) (comment, 2026-10-28), because this folder is bulk-trashable; it is recorded and NOT scheduled — the owner declined flaky-test fix work on 2026-10-28.**

## Acceptance

Seat: `ollama-cloud/deepseek-v4-pro:0813`, on its own seat after the static review and after the live probe.
Verdict: **RESIDUALS — no rework.** Transcribed verbatim; the numbers it cites are the parent's.

```
ACCEPTANCE
verdict: RESIDUALS
criteria:
- C1: met: PanelSection Id="student-view-assignments" first in .region-grid__rail; railBlocks[0] pinned, and
  the render test asserts rail[0].Id, title "Assignments" and header count "24"
- C2: met: four .stat tiles assert ("24","Assigned"),("21","Sent out"),("12","Submitted"),("7","Marked") in order
- C3: met: Funnel derives the four segments; aria-label "24 assigned: 3 not sent, 9 awaiting submission,
  5 awaiting marking, 7 marked" (3+9+5+7=24=Assigned)
- C4: met: review foot "required on 12 of the 21 sent" on an Assigned=24/SentOut=21/ReviewRequired=12 payload,
  with NotContain("of 24")
- C5: met: zero state shows tiles at 0, no .stat-bar, the empty line
- C6: met: 500 -> muted unavailable line, no tiles/bar/count chip, other blocks still render
- C7: met: Marked 25 > Submitted 18 -> unavailable, never clamped
- C8: met: real Tenant/Bearer handler chain; AbsolutePath == "/assignments/{id}/assignment-stats"; six-value
  ReadFromJsonAsync round trip
- C9: met: 200-with-counts and a 502 problem; orchestrator adds only the chain call (0 MapGet, 5 gates)
- C10: met: port in Core, impl in Api, both csproj NotContain("Assignments")
- C11: met: build 0 errors; gate recorded honestly (unit leg failed once on the unrelated Settings flake ->
  3/3 standalone + re-run 4670/0/0; integration 151 with only the 2 script-classified OpenRouter tests);
  healthy + degraded probe transcripts captured
- R1..R6: met (csproj pin, one AddScoped and no new client, no src/Assignments change, inline PanelSection with
  no tabs, every selector consumed and tokenised, 19 files inside the table with both inventory tests updated)
post_review_fixes:
- (a) sound: \if :{?gender_id} is the correct defined-ness test; \gset sets it only when the SELECT returns a
  row; the second \if -> \echo + \quit 1 fires only when neither -v nor an existing student supplies it;
  ON_ERROR_STOP on + non-zero exit fail loudly; no environment GUID hard-coded
- (b) sound: mandatory_review=false on "awaiting submission" matches the runbook's own per-row table and the
  review-axis rule; the self-check now folds to 6/5/2/1/2/1, exactly the documented expectation. Moving the
  runbook's counts to 3 instead would have contradicted the runbook's own row documentation
flake: acceptable: the failing test lives in Settings.Tests.Unit (untouched by this round), it is a bUnit
  WaitForElement render timeout rather than a semantic assertion, it passes 3/3 standalone, the identical
  re-run is 4670/0/0, and the round records the failure rather than claiming a clean sweep. Precedent: a
  one-off unrelated bUnit timeout that goes green on isolation and identical re-run closes as a RECORDED
  flake, never as a swept-clean pass - the recording, not the green re-run, is what makes it acceptable
residuals:
- p3(1) the client try/catch is an untested redundant belt: acceptable, C6 pins the end-to-end degradation
- p3(2) a raw negative count passes the difference guard: acceptable, unreachable from the shipped L2a handler
- NEW p2: students.sql still carried the stale "left NULL ... nullable in the schema" comment above the
  corrected note (contradicted by migration 20260716183515_RequireStudentDobAndGender, nullable: false), and
  the hard-stop's "see the runbook" parenthetical was unmet
rework: none
reader_must_be_told: the first two probe-student comment lines in students.sql were the stale pre-fix text and
  were wrong - the column is NOT NULL and the fixture inherits/passes gender_id; delete them and add the
  -v gender_id fallback to the runbook's run steps.
```

### Parent adjudication

| Item | Verdict | Action |
|---|---|---|
| C1–C11, R1–R6 all met | **accepted** | — |
| Both post-review fixes judged sound | **accepted** | — |
| Flake precedence ("recorded, never swept clean") | **accepted**, and it is the wording the round needed | recorded above and in the probe section |
| **NEW p2** — stale `nullable in the schema` comment | **correct, and the same defect class as the lesson written earlier today**: a corrected value beside its own contradiction, in a delivered artifact | **Closed after the verdict**: the two stale lines are deleted, the surviving note now cites the migration, and the runbook documents the `-v gender_id=<guid>` fallback with a real query to obtain the id. Patch re-frozen (19 files, 1813 lines) |
| p3(1), p3(2) | accepted as residuals | — |

### Ticket closure (transcribed by the parent from the acceptance seat's verdict)

Per `documents/solution/spec-and-ticket-skills-in-the-orchestrator.md` §6 (Q90-Q94, owner 2026-10-21): a round
that names ticket issues carries one row per ticket, and **the parent** executes the close with this row, the
round-doc path and the carrying PR. No closing keyword is used — a merge-time keyword would fire before this
row exists and would close a deferred ticket that the rule keeps open.

| Ticket | Its own acceptance evidence | Verdict |
|---|---|---|
| [#444](https://github.com/stayboy/School-Collab/issues/444) — *Student view layer 2: assignment stats rail panel* | L2a ([#447](https://github.com/stayboy/School-Collab/pull/447), merged `a94ecf2e`): `StudentAssignmentStatsDto` + status-independent read + endpoint, `Assignments.Tests.Unit` 1401/0/0. L2b (this round): port, hop, `/students/{id}/assignment-stats`, rail panel, rail-first position, funnel and review denominators, degradation; authoritative numbers build 0 errors, `Admin` 870/0/0, `Students.Api` 37/0/0, `Students` 695/0/0, `Architecture` 160/0/0; live probe healthy + degraded transcripts. Acceptance seat: **RESIDUALS, no rework** — all C1-C11 and R1-R6 met | **closes**, after the carrying PR merges |

**Verdict: CLOSED, with the residuals above.** L2b is accepted: the port, hop, endpoint, UI client and rail panel
are in place, the three plan-gate P1s never regressed, the degradation chain was proved in a browser rather than
argued from a stub, and the round found and fixed two defects in its own seed fixtures. **Not committed** — the
commit gate is the owner's.



## Not run

| Seat | Why |
|---|---|
| UI tester | The owner's Q146 override runs this round at **Tier 2**; the live probe (C11) substitutes for a UI-tester bug-hunt, and it is the stronger check because it runs against a real AppHost and a seeded database |
