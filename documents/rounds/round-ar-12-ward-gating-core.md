# Round ar-12-ward-gating-core — Phase 2 slice 2a: module progress + gating engine + ward queries (WS-D1 + WS-A5 core, NO UI)

Provider: pi. **EXECUTED 2026-09-15 as a LIGHT ROUND (Tiers 1–2: parent plans + one worker + static diff-only reviewer; no UI tester needed — no UI in scope) — owner choice ("use light round for ar-12").** **Round base: `main` @ `d2169aef` (the ar-11 squash); branch `stack/12-ar-12-ward-gating-core` cut from main, single-layer stack.** Pre-round carryover in the working tree (NOT part of the round diff; patch freeze excludes it): `documents/solution/assignment-request-go-forward-breakdown.md` (§6 refresh), `documents/specs/assignment-request-feature-spec.md` (§4 `certificate_storage_path` drift fix), untracked scratch (`diffs-period-upsert-single-page.patch`, `round-period-upsert-single-page.md`, `.ar-1…6-*`). Ladder: worker `ollama/deepseek-v4-flash:0731-cloud` → reviewer `ollama/kimi-k2.7-code:cloud`.

- **Tier check:** behavioural, ONE bounded context (Assignments), **no `.razor`/`.razor.css`/`.js`/`wwwroot`** (UI is slice 2b) → light-round eligible; ~16 expected files + **ONE additive migration**; **ZERO new packages**, **ZERO new feature flags**.
- **Sources:** `documents/specs/assignment-request-feature-spec.md` §3.3 (modules + gating: "questions unlock only after required video/guide is viewed (min watch % or scroll completion)", mixed types, pass/fail), §3.2 line 51 (status chain middle states: In Progress → Completed), §3.5 step 5 (Ward Completion); `documents/solution/assignment-request-go-forward-breakdown.md` §3.3 (§3.3 rows), WS-A5, WS-D1 (Phase 2 bullets: "D1 module progress + gating; A5 ward queries"), §4 Phase 2 acceptance; skills `dotnet-best-practices`, rule `.github/copilot/rules/ef-migrations.md` (BINDING — one migration this round).
- **Drift-refresh facts (verified 2026-09-15 against main `d2169aef`):**
  - `Core/Domain/ContentModule.cs` exists (ar-4): `ModuleType` (Video/Guide), `Url`, `Title`, `StoragePath`, `MinCompletionThresholdPercent` (default 100, validated 1–100 by `AssignmentContentValidator`), `IsRequired`, order — aggregate-add via `Assignment.AddModule(...)` (line ~483).
  - **NO per-recipient progress anywhere** (grep `ModuleProgress|WatchPercent|ScrollCompletion` = zero domain hits). `AssignmentSubmission`/`AssignmentSubmissionVersion` exist (ar-6 answers/score/pass).
  - Queries present: `GetAssignmentByIdQuery`, `GetSubmission`, `GetSubmissionsForReview`, `ListAssignmentGroups`, `ListAssignmentRecipients`, `GetGuardianGate` — **no ward-list/ward-view query**.
  - Routes: submission `POST /{id}/students/{studentId}/submission` (~line 646), gates `GET /{id}/gates/student/{studentId}` (~936) — **no module-progress route, no ward list/view route**. `SignOffRoutesTests.cs`/`CertificateRoutesTests.cs` are the minimal-TestServer route-test harness precedent.
  - Submission guard precedent: `SubmissionAttemptsExhaustedException`, `SubmissionLockedException` (typed, mapped); `GuardianSubmissionGate` is pre-submission semantics — DO NOT repurpose it (breakdown risk register).

## Plan

Execute the WS-D1 core + WS-A5 slice: a per-(ward, module) progress record, an idempotent progress-report command, a server-side module gate on submission, and the ward-facing list + view queries. UI rendering of these (D2 player, lock affordances) is slice 2b and is OUT.

### Decisions (binding)

- **(a) Progress entity.** New `Core/Domain/ModuleProgress.cs` (`ModuleProgress : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion`): `AssignmentId`, `StudentId`, `ContentModuleId`, `PercentComplete` (int, clamped 0–100), `CompletedAt` (DateTimeOffset?, stamped once). ONE numeric field serves both module types: video = watch %, guide = scroll-complete reported as percent (100 on complete). Unique index (TenantId, AssignmentId, StudentId, ContentModuleId). Config file + ONE additive migration (ef-migrations rule: never touch existing migrations; `MigrationGuardTests` stay green untouched).
- **(b) Idempotent monotonic recording.** Domain method `Record(int percent)`: clamps 0–100; monotonic (a lower/equal report is a no-op — replays and out-of-order heartbeat posts are safe); stamps `CompletedAt` the FIRST time `PercentComplete >= module.MinCompletionThresholdPercent` (the module is loaded alongside to compare); never unstamps. `UpdatedAt`/row-version per repo convention.
- **(c) Server-side module gate (the enforcement half of spec §3.3).** Submission (`CreateStudentSubmission` handler) gains a guard: if the assignment has any REQUIRED modules whose `ModuleProgress.CompletedAt is null` for that student → throw new typed `RequiredModuleIncompleteException` (mirrors `SubmissionAttemptsExhaustedException` posture) → route maps to **409** (existing lock/attempts precedent — worker verifies the exact status code those use and reports a deviation if different). Assignments with NO required modules pass unchanged (existing tests must stay green — the gate is additive).
- **(d) Progress-report command + route.** `RecordModuleProgressCommand(assignmentId, studentId, contentModuleId, percent)` + handler (load module via assignment aggregate, upsert progress via new `IModuleProgressRepository`, single `SaveChangesAsync`). Route: `POST /{id:guid}/students/{studentId:guid}/modules/{moduleId:guid}/progress` with `{ "percent": n }`; 204 on success; 404 when assignment/module missing; same authorization posture as the existing student-scoped routes (token-auth re-scoping is F1/2b — recorded OUT).
- **(e) Ward queries (WS-A5).** Two queries + routes, contracts in `ContractTypes.cs`:
  - `ListWardAssignments(studentId)` → `GET /students/{studentId:guid}/assignments`: Published (and Scheduled-active per existing visibility rules — worker verifies the existing list filter) assignments targeting the student (grade level via the existing targeting seams + activity groups), each with due date, title, submission state (None/In-Progress via `AssignmentSubmission` existence + `SignOffState` projection — the ar-9 per-ward precedent), and a `HasLockedModules` flag.
  - `GetWardAssignmentView(assignmentId, studentId)` → `GET /{id:guid}/students/{studentId:guid}/modules`: module list (ordered) + per-module `PercentComplete`/`CompletedAt` (null progress = 0/null) + `QuestionsUnlocked` (all required modules complete) — the lock flag D2's UI will bind to.
- **(f) Out (record — do not touch):** any `.razor`/`.js` surface (D2 player, F2 list UI — slice 2b); F1 host/auth decision + token deep links (WS-F/E1); feedback-mode config (held-until-submission vs immediate); video captions policy (WS-G1); rubrics (Phase 5); notifications (Phase 4); the pre-round uncommitted doc notes and scratch files in `documents/`.

### Expected files (~16, ±1)

**Created (11):** `Core/Domain/ModuleProgress.cs`; `Core/Domain/Exceptions/RequiredModuleIncompleteException.cs`; `Core/Data/Configurations/ModuleProgressConfiguration.cs`; `Core/Migrations/<ts>_AddModuleProgress.cs` + `.Designer.cs` (generated); `Core/CQRS/Assignments/Commands/RecordModuleProgress/` (command + handler); `Core/CQRS/Assignments/Queries/ListWardAssignments/` (query + handler); `Core/CQRS/Assignments/Queries/GetWardAssignmentView/` (query + handler); `Core/Data/IModuleProgressRepository.cs` (or the repo-pattern home the worker verifies — `ISubmissionRepository` precedent — deviation reported if different).
**Modified (5):** `Core/CQRS/Assignments/Commands/CreateStudentSubmission/` handler (gate); `Contracts/ContractTypes.cs` (progress + ward DTOs); `Api/Endpoints/AssignmentRoutes.cs` (3 routes); `Api` DI/Program if the repo needs registration (Scrutor scan — verify); tests below.

### Implementation steps (ordered; `dotnet build SchoolCollab.sln` after every step)

1. **Domain + persistence:** `ModuleProgress` + `Record` + config + migration (ef-migrations rule read FIRST). Build; `dotnet test tests/SchoolCollab.Assignments.Tests.Unit` (existing green + new `ModuleProgress` cases).
2. **Command + gate:** `IModuleProgressRepository` + `RecordModuleProgressCommand` handler; submission-gate addition with `RequiredModuleIncompleteException`. Build; handler + gate tests green.
3. **Queries + routes:** ward list/view queries + 3 routes + DTOs. Build; `dotnet test tests/SchoolCollab.Assignments.Api.Tests.Unit` (route tests via the TestServer harness).
4. **Full matrix + freeze:** `dotnet build SchoolCollab.sln` (0 errors) + the eight test projects + `git add -N -- src tests` + parent freezes the patch + worker self-report.

### Binding test coverage list (MSTest + FluentAssertions)

- `ModuleProgressTests`: `Record_ClampsPercent_To0And100`, `Record_Monotonic_LowerReportIsNoOp`, `CompletedAt_StampsOnce_AtThreshold`, `CompletedAt_NotStamped_BelowThreshold`, `Record_Idempotent_Replay`.
- Gate truth table (in the file that owns submission-command tests — worker verifies the home): `NoModules_Passes`, `RequiredIncomplete_Blocks` (typed exception), `RequiredComplete_Passes`, `OptionalIncomplete_Passes`, `ThresholdExactly_Meets_Passes`.
- `RecordModuleProgressCommandHandlerTests`: first-report upsert; replay no-op (no second row, row-version unchanged); 404-null module; tenant isolation (progress is only visible within its tenant).
- Route tests (`WardRoutesTests`, TestServer harness): progress POST 204/404; ward view 200 with per-module progress + `QuestionsUnlocked` truth; ward list 200 published-only + `HasLockedModules`; submission blocked → 409 when required module incomplete.

### Constraints (repo AGENTS.md — restated for the worker)

CPM (no new packages). net10.0. No MediatR (`ICommandHandler<T>`/`IQueryHandler<T,R>` + Scrutor). **Read `.github/copilot/rules/ef-migrations.md` BEFORE the migration; ONE additive migration only; never touch existing ones; `MigrationGuardTests` green untouched.** MSTest/FluentAssertions/bUnit/Moq; MTP invocation `dotnet test tests/SchoolCollab.<Name>` NO extra flags. `dotnet build SchoolCollab.sln` after every change. **No git commits — working tree only.** Primary constructors; XML docs on new public members; typed exceptions only; no `InvalidOperationException` from new code. **Repo-scoped searches only — NEVER `find /`.** Worker NEVER edits this round doc; self-report → `documents/rounds/.ar-12-worker-report.md`. 30-min cap + cut-line protocol.

### Acceptance criteria

Worker-facing: build 0 errors; the eight test projects 0 failures; changed files = expected list (±1); deviations reported, not silent.
Reviewer-facing: decisions (a)–(e) as written; monotonic/idempotent recording; gate additive-only (no repurposed `GuardianSubmissionGate`); 404/409 paths; ward queries honor existing visibility/targeting seams; best-practices check (no overwrites, skills honored, readable).
Parent-facing: authoritative build + matrix green; reviewer PASS or P2-only triaged; P1 empty → CLOSED.

### Residual risks / notes

- Percent-based guide progress: guide scroll-complete reported as percent is a simplification (v1 has no guide reader yet — 2b defines the reporting shape; the contract is deliberately percent-only).
- `HasLockedModules` on the ward list needs an efficient per-assignment check — watch N+1; a single projection query per the `AssignmentRepository` precedent.
- The 2b UI binds to `QuestionsUnlocked`/`PercentComplete` as-is — no planned contract break.
- Ward routes keep the current authorization posture (teacher/admin OIDC) until F1 lands — recorded, not a defect.

## Prepared dispatch (parent)

1. ✅ Carry riding docs onto the branch (owner: "carry riding docs onto branch") — branch cut from `main` @ `d2169aef` with the two doc notes + this plan untracked/staged-carrying.
2. ✅ `stack/12-ar-12-ward-gating-core` created (single-layer; no GitHub stack registration needed).
3. ✅ Execution mode: **light round** (owner choice).
4. ✅ Worker task composed from the plan; dispatched 2026-09-15.

### Plan amendments (parent-adjudicated during execution)

- **A-1 (ward-list read home).** `ListWardAssignmentsAsync` lives on the new `IModuleProgressRepository` rather than `IAssignmentRepository`; the worker avoided a ~13-file interface churn across existing `IAssignmentRepository` test fakes. Functionally sound; carries a reviewer **P2 cohesion/naming** note — migrate the read to `IAssignmentRepository` (or a dedicated ward-aggregation repository) in slice 2b. Recorded, not reworked.
- **A-2 (ward-list targeting source).** Targeting = recipient-link only (`Published/Scheduled-active` AND an `AssignmentRecipient.WardStudentId == student`). Recipients are materialized at publish, so this is the authoritative source; grade/group cross-context resolution deferred to 2b (cross-context calls cannot run in the Core query). Adjudicated sound.
- **A-3 (route host).** The ward list sits at exactly `GET /students/{studentId}/assignments` via a new sibling `/students` group in `Api/AssignmentEndpoints.cs` (+1 file beyond the plan's Modified list). Adjudicated sound.
- **A-4 (409 mapping).** The submission route gained the `RequiredModuleIncompleteException → 409` catch — plan-required; the existing route did not catch it. Keep.
- **A-5 (file count).** 26 files vs the plan's ~16: the migration `Designer` + regenerated `AssignmentsDbContextModelSnapshot` and the per-query file splits account for the difference. Verified additive-only; no unrelated rewrites.
- **A-6 (P2 rework, parent-side).** Two reviewer P2s were real defects, not nits, and were fixed parent-side (tiny-diff scope-check per the light-round rule): the vacuous `UpdatedAt.Should().Be(UpdatedAt)` self-comparison in `Record_Monotonic_LowerReportIsNoOp` (now captures `before` and asserts a no-op does not mutate) and the missing `AsNoTracking()` in `ListProgressForAssignmentStudentAsync`. Patch re-frozen (2,818 lines). The remaining P2 (A-1 cohesion) is deferred to 2b.

## Worker Report

Worker: `worker` agent on `ollama/deepseek-v4-flash:0731-cloud`, pass 1 (run `3b00427c`), 2026-09-15. Self-report also at `documents/rounds/.ar-12-worker-report.md`.

```text
WORKER REPORT
Changed files: 26 (16 created + 10 modified) under src/ + tests/; no pre-round carryover files touched
Created: Core/Domain/ModuleProgress.cs; Core/Domain/Exceptions/RequiredModuleIncompleteException.cs;
  Core/Data/Configurations/ModuleProgressConfiguration.cs; Core/Data/Repositories/{I,}ModuleProgressRepository.cs;
  Core/CQRS/.../Commands/RecordModuleProgress/{Command,Handler}.cs;
  Core/CQRS/.../Queries/Ward/{GetWardAssignmentViewQuery,ListWardAssignmentsQuery}(+Handlers).cs;
  Core/Migrations/20260915123726_AddModuleProgress.cs + .Designer.cs;
  tests: ModuleProgressTests.cs, RecordModuleProgressCommandHandlerTests.cs, WardRoutesTests.cs
Modified: Api/Endpoints/AssignmentRoutes.cs (progress POST + ward view GET + submission 409 catch);
  Api/AssignmentEndpoints.cs (/students ward-list group); Contracts/ContractTypes.cs (ward DTOs);
  Core/.../CreateStudentSubmissionCommandHandler.cs (module gate); Core/Data/AssignmentsDbContext.cs;
  Configurations/{ContentModule,ModuleProgress}Configuration.cs; Core/Extensions.cs (DI);
  Migrations/AssignmentsDbContextModelSnapshot.cs; tests: CreateStudentSubmissionScoringHandlerTests.cs, SubmissionEngineTests.cs
Build: 0 errors (dotnet build SchoolCollab.sln)
Tests: 1,691/0 across the 8 projects; +21 new (ModuleProgress 5, gate truth table 5, progress handler 5, WardRoutes 6);
  MigrationGuard/snapshot-consistency green
Deviations: (1) ward-list read on IModuleProgressRepository; (2) recipient-link targeting only (grade/group -> 2b);
  (3) new sibling /students route group; (4) submission 409 catch added; (5) 26 files vs ~16 (Designer+snapshot+splits)
```

## Review

Reviewer: static diff-only on `ollama/kimi-k2.7-code:cloud` (run `1476003a`), patch `diffs-ar-12-ward-gating-core.patch` + plan + worker self-report + repo skills (`dotnet-best-practices`, `ef-migrations`).

```text
REVIEW
Verdict: P2-only
P1: none
P2:
- tests/.../ModuleProgressTests.cs:49 — Record_Monotonic_LowerReportIsNoOp compares UpdatedAt to itself;
  should capture the value before the no-op calls (fixed parent-side, A-6)
- src/.../Data/Repositories/IModuleProgressRepository.cs:30 — ListWardAssignmentsAsync returning
  AssignmentSummary[] is a cohesion/naming smell on a module-progress repository (deferred to 2b, A-1)
- src/.../Data/Repositories/ModuleProgressRepository.cs:39 — ListProgressForAssignmentStudentAsync
  does not use AsNoTracking() for read-only callers (fixed parent-side, A-6)
Best-practices: no overwrites / skills honored / readable; deviations 1-5 adjudicated sound
```

Reviewer residual notes (accepted): N+1 in ward-list enrichment (v1); recipient-link targeting; first-report upsert is read-then-write so concurrent identical first reports can race on the unique index (rare in single-ward heartbeat use).

## Acceptance

**Verdict: CLOSED — P1 list empty.**

- [x] Build (parent-authoritative): `dotnet build SchoolCollab.sln` — **0 errors**.
- [x] Tests (parent-authoritative, MTP no-flags): Core 79/0 · **Assignments 591/0 (+15)** · **Assignments.Api 58/0 (+6)** · Students 422/0 · Students.Api 1/0 · Settings 519/0 · Settings.Api 1/0 · **ArchitectureTests 20/0** — **1,691 / 0 failures**; MigrationGuard green untouched.
- [x] Re-verified after the parent-side P2 rework: build 0 errors; Assignments 591/0 · Assignments.Api 58/0 · Architecture 20/0.
- [x] Scope check: 26 files, additive-only, one additive migration (Designer + snapshot regenerate), no UI, no new packages/flags; `GuardianSubmissionGate` untouched; deviations A-1…A-5 adjudicated sound.
- [x] Decisions (a)–(e) implemented; reviewer P2-only; P2s either fixed (A-6) or explicitly deferred (A-1 → 2b).
- [x] Loop bounds: 1 worker pass, 1 reviewer pass, 1 tiny parent-side P2 rework (Tier-2 bound ≤1 respected).

Residuals (recorded, not blocking): ward-list N+1 (v1 acceptable); recipient-link-only targeting (grade/group → 2b, A-2); repository cohesion (A-1 → 2b); read-then-write race on concurrent identical first reports (rare; unique index protects integrity); ward routes keep the current teacher/admin OIDC posture until F1 lands.

## Execution provenance

| Role | Model | Run | Outcome |
|---|---|---|---|
| Parent (orchestrator, Tier 2) | session | — | plan + amendments + acceptance + tiny P2 rework |
| Worker pass 1 | `ollama/deepseek-v4-flash:0731-cloud` | `3b00427c` | 26 files, build 0 errors, 1,691/0, 5 deviations |
| Reviewer 1 (static) | `ollama/kimi-k2.7-code:cloud` | `1476003a` | P2-only (3 findings), no P1, deviations sound |
| UI tester | — | — | not needed (no UI in scope) |