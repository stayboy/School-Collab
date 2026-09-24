# Round assignments-worker-outbox-config — the worker cannot start (missing `Outbox__ExchangeName`)

Provider: pi **ollama-cloud** profile — worker `ollama-cloud/deepseek-v4.1-flash`, reviewer `ollama-cloud/kimi-k2.7-code` (light-mode set; reviewer default per owner 2026-09-22 — the verifier must not share the worker's model; ids verified in the session registry). **Tier 2 light round**, base `ba155117` (merged #253; `main` == `origin/main`, tracked tree clean). Pre-existing untracked scratch unrelated to this round: `brainstorms/`, `documents/rounds/.session-state.md`.

## Plan

### Goal

`SchoolCollab.Assignments.Worker` currently **cannot start**: it throws
`Microsoft.Extensions.Options.OptionsValidationException: ExchangeName must be set in the 'Outbox' configuration section.`
at `src/Assignments/SchoolCollab.Assignments.Worker/Program.cs:56` (`host.Run()`). Make the worker start by supplying the one configuration value the AppHost never injected for it.

### Diagnosis (verified, with evidence)

- `AddAssignmentsCore` (`src/Assignments/SchoolCollab.Assignments.Core/Extensions.cs:93`) **unconditionally** calls `AddOutbox<AssignmentsDbContext>`. That helper binds `OutboxOptions` from section `Outbox` and `.ValidateOnStart()`-requires a non-empty `ExchangeName` (`src/SchoolCollab.Core/Messaging/OutboxExtensions.cs:75-79`). It also registers `OutboxDispatcher<TContext>` as a hosted service.
- The worker's `appsettings.json` has **no** `Outbox` section (only `RabbitMq:Subscriber`).
- The AppHost's `assignments-worker` block (`src/AppHost/SchoolCollab.AppHost/Program.cs:317-324`) sets **only** `RabbitMq__Subscriber__ExchangeName`.
- The AppHost's own comment at `:110` states the exchange names are *"fanned out to the matching API/**Worker** via `WithEnvironment("Outbox__ExchangeName", param)`"*, and `students-worker:330` **does** set it. So the omission is a defect against documented intent, not a design choice.
- Introduced by `11cc37e1` (ar-19); never present. **Not** caused by the merged #253 — that PR's `Program.cs` diff contains **0** `assignments-worker` lines.

**Exactly ONE value is missing.** `grep -rn ValidateOnStart src` returns only two calls in the whole tree — the outbox (`:79`) and the RabbitMQ subscriber (`:126`). The subscriber's `ExchangeName` + `QueueName` are already satisfied by the worker's `appsettings.json` plus the AppHost env var.

**`Smtp__*` is deliberately NOT added.** `SmtpOptions` has no startup validation, and the worker never resolves `IEmailSender`: the drain is an **API-side** hosted service (`src/Assignments/SchoolCollab.Assignments.Api/Services/NotificationDispatchSweepService.cs`), and `assignments-api` already receives `Smtp__*` (`:284-288`). The worker *queues* `NotificationLog` rows; the API sends them. Adding `Smtp__*` to the worker would be cargo-cult.

### Scope IN

1. `src/AppHost/SchoolCollab.AppHost/Program.cs` — one statement + the block comment.
2. `tests/SchoolCollab.ArchitectureTests.Unit/AppHostOutboxExchangeWiringArchitectureTests.cs` — **new** guard.
3. `documents/configuration.md` — the §2 `assignments-worker` paragraph (`:144-155`).
4. `documents/solution/assignments-worker-outbox-exchange-fix.md` — **new** Finding→Implementation note.

### Scope OUT (do not touch)

- `Smtp__*` on the worker (decision (b) below — it is not needed).
- The worker's `appsettings.json` — do **not** add an `Outbox` section (decision (c)).
- `AddAssignmentsCore` / `AddOutbox` / `OutboxOptions` / any Core code — no refactor, no new parameter/flag.
- Any other host's AppHost block (`assignments-api`, `settings-api`, `students-api`, `students-worker` already carry the env var).
- `RabbitMq__Subscriber__ExchangeName` on the worker (already correct).

### Binding decisions (implement as written)

**(a) Fix = supply the value from the AppHost**, as `.WithEnvironment("Outbox__ExchangeName", assignmentsOutboxExchange)` inside the `assignments-worker` chain. Rationale: the AppHost is the documented single fan-out point for `Outbox__ExchangeName`; `students-worker:330` is the working precedent; the parameter `outbox-exchange-assignments` already exists and is already reused by this block for the subscriber exchange. Rejected alternative: not registering the outbox in `AddAssignmentsCore` for workers — it would change a shared extension used by `assignments-api` too, is far more invasive, and contradicts the AppHost's stated design.

**(b) Do NOT add `Smtp__*` to the worker.** The drain is API-side; the worker only queues. Nothing in the worker resolves `IEmailSender`, and `SmtpOptions` has no startup validation, so the omission is not a defect.

**(c) Do NOT add an `Outbox` section to `src/Assignments/SchoolCollab.Assignments.Worker/appsettings.json`.** The AppHost injects it (`:110-113` comment: "so no per-service appsettings.json needs an Outbox section"). The worker's local file keeps only the RabbitMQ subscriber defaults.

**(d) The guard mirrors `AppHostSettingsDbWiringArchitectureTests`** — see step 2. It must be *discriminating*: it must fail against the current (pre-fix) tree.

### Implementation steps

1. **`src/AppHost/SchoolCollab.AppHost/Program.cs`** — in the `assignments-worker` chain (currently `:317-324`), add
   `.WithEnvironment("Outbox__ExchangeName", assignmentsOutboxExchange)`.
   Place it **adjacent to** the existing `.WithEnvironment("RabbitMq__Subscriber__ExchangeName", assignmentsOutboxExchange)` line, mirroring the ordering convention used by the `students-worker` chain (`Outbox__…` before `RabbitMq__Subscriber__…`). Extend the block comment (`:311-316`) so it no longer implies the block is subscriber-only: state that the worker registers the outbox via `AddAssignmentsCore`, so it also needs `Outbox__ExchangeName` (the same reused parameter), and that the worker does **not** need `Smtp__*` because the drain is API-side. Keep the "no new `Parameters:` entries" statement — it stays true.

2. **New guard** `tests/SchoolCollab.ArchitectureTests.Unit/AppHostOutboxExchangeWiringArchitectureTests.cs`.
   Model it directly on `AppHostSettingsDbWiringArchitectureTests.cs` (same namespace, MSTest + FluentAssertions, same repo-root walk-up, same `AddProject<Projects.X>("name")` regex chain parser, same line-comment stripping, and the same `ResourceName`/`ProjectConstant`/`Chain` record shape — copy the private helpers rather than modifying the existing file). It must contain exactly these tests:
   - `OutboxRegisteringHosts_GetOutboxExchangeName` — for every AppHost project resource whose host `Program.cs` registers the outbox, the resource's chain must contain `WithEnvironment("Outbox__ExchangeName"`. The chain scan must be anchored on the declaration-to-next-`AddProject<` boundary, exactly as the precedent does.
   - `OutboxRegisteringHosts_AreTheReviewedSet` — a tripwire pinning the resolved set to exactly `["assignments-api", "assignments-worker", "settings-api", "students-api", "students-worker"]` (the union of hosts whose `Program.cs` calls an `Add*Core` that registers the outbox), with a message saying a new host must consciously update the list.
   - `Scan_ActuallyFoundTheAppHostAndTheOutboxHosts` — a non-vacuity guard: the parsed project-resource count must be `>= 6` and the resolved outbox-host set must be non-empty, so a broken regex cannot make the assertions above pass while inspecting nothing.
   - A fourth test pinning the **registration side**, so the derivation cannot silently go stale: assert that the set of `src/**/Extensions.cs`-style files containing `AddOutbox<` is exactly the three known module cores — `Assignments.Core/Extensions.cs`, `Settings.Core/Extensions.cs`, `Students.Core/Extensions.cs` — mapped to the extension method names `AddAssignmentsCore`, `AddSettingsCore`, `AddStudentsCore`. (A 4th module core registering the outbox must trip this and force the reviewer of that change to decide the env-var question.)
   - **Derivation requirement:** resolve "which hosts register the outbox" by scanning real `src/**/Program.cs` files (skipping `bin`/`obj`) for calls to those three extension method names — do **not** derive it from the AppHost's own text, or the guard becomes tautological. If the two-step derivation proves impractical in the time available, fall back to the documented host list **plus** a `Program.cs`-scan assertion for each of the five names, and report the fallback as a deviation.

3. **`documents/configuration.md`** — the §2 `assignments-worker` paragraph (`:144-155`). It currently says the parameter is injected *"as `RabbitMq__Subscriber__ExchangeName = assignments`"* and that the worker's local `appsettings.json` *"carries only the RabbitMq subscriber defaults"*. Correct it to state **both** injections — `Outbox__ExchangeName` (the outbox dispatcher that `AddAssignmentsCore` registers, which is what makes the worker start) and `RabbitMq__Subscriber__ExchangeName` (the subscription) — and keep the "no new `Parameters:` entry" statement. Add one sentence recording that the worker deliberately does **not** receive `Smtp__*` because the notification drain runs in `assignments-api`. Per `.github/copilot/rules/configuration-documentation.md` this is the same-change-set requirement; do not add or remove any §11/§12 row (no key is added, renamed or removed — only a new injection of an existing key).

4. **`documents/solution/assignments-worker-outbox-exchange-fix.md`** — short durable note per `AGENTS.md` "Finding → Implementation": the Finding (worker `OptionsValidationException`; `AddAssignmentsCore` unconditionally registers the outbox; the AppHost never injected the exchange for this host; the AppHost's own comment says it should), the decision ((a)/(b)/(c)), why `Smtp__*` was considered and rejected, and the verification results including the runtime cold start.

5. **Verify** — `dotnet build SchoolCollab.slnx`, then `dotnet test tests/SchoolCollab.ArchitectureTests.Unit`, reading only the `total:`/`failed:` lines with a single grep.

### Expected files (the reviewer's conformance baseline)

| # | Path | Change |
|---|---|---|
| 1 | `src/AppHost/SchoolCollab.AppHost/Program.cs` | modified — 1 statement + block comment |
| 2 | `tests/SchoolCollab.ArchitectureTests.Unit/AppHostOutboxExchangeWiringArchitectureTests.cs` | **new** |
| 3 | `documents/configuration.md` | modified — §2 `assignments-worker` paragraph only |
| 4 | `documents/solution/assignments-worker-outbox-exchange-fix.md` | **new** |

Any other changed path is a scope deviation and must be reported. Any of these four missing is a plan violation.

### Acceptance criteria

- **AC1** — the `assignments-worker` AppHost chain contains `.WithEnvironment("Outbox__ExchangeName", assignmentsOutboxExchange)`, and the block comment no longer implies the block is subscriber-only.
- **AC2** — the new guard exists and **is discriminating**: with the added `.WithEnvironment` line removed, `OutboxRegisteringHosts_GetOutboxExchangeName` must fail. The worker must verify this empirically (add → run → remove → run → restore) and report the observed counts.
- **AC3** — the guard's tripwire pins exactly the five hosts, and the non-vacuity guard is present and meaningful.
- **AC4** — `dotnet build SchoolCollab.slnx` → **0 errors**.
- **AC5** — `dotnet test tests/SchoolCollab.ArchitectureTests.Unit` → **0 failures**, total **≥ 45** (41 at base; the four new tests bring it to 45).
- **AC6** — `Program.cs` diff is limited to the worker block: no other host's block and no `AddParameter` / `WithReference` line is altered.
- **AC7** — `documents/configuration.md` §2's `assignments-worker` paragraph names both injections and records the deliberate `Smtp__*` exclusion; `documents/solution/assignments-worker-outbox-exchange-fix.md` exists.
- **AC8 (parent-run, needs Docker)** — after the change, an AppHost cold start brings `assignments-worker` up **Running/Healthy** (not `Finished`) and its resource log contains no `OptionsValidationException`. The parent runs this; if Docker is unavailable, it is recorded as an environment residual rather than skipped silently.

### Reviewer acceptance criteria (Tier 2)

1. **Scope conformance** — the changed-path set equals the 4 expected files (no extras, none missing); the worker's `appsettings.json` and Core files are untouched.
2. **AC2 by reading the assertions** — the guard's outbox-host derivation comes from `Program.cs` source scanning, not from the AppHost's own text (a tautological guard is a P1); the tripwire and non-vacuity tests are present and can actually fail.
3. **AC6 by diff inspection** — the `Program.cs` hunk touches only the `assignments-worker` chain + its comment.
4. **Decision conformance** — `Smtp__*` was **not** added to the worker; no `Outbox` section was added to the worker's `appsettings.json`; no Core/`AddOutbox` change.
5. **No overwrites** — nothing pre-existing deleted or reformatted; `AppHostSettingsDbWiringArchitectureTests.cs` untouched (copy its helpers, do not edit it).
6. **Skills honored** — `dotnet-best-practices` for the new C# test, `configuration-documentation` for step 3.

### Constraints for the worker

- Repo-scoped searches only — **never** `find /` or a filesystem-wide scan.
- Build: `dotnet build SchoolCollab.slnx` (the `.slnx`). Tests: `dotnet test tests/SchoolCollab.ArchitectureTests.Unit` with **no extra flags**; read results with one `grep -E "^\s*(total|failed|succeeded):"`. At most 2 attempts per result read; no ad-hoc debug logs.
- Do **not** edit this round doc. Do **not** run `git commit` / `git push` / `gh`. Do not create branches. Do not start the AppHost (the parent owns the runtime check — AC8).
- If the build or the guard fails in a way this plan does not cover, STOP and report it as a deviation instead of improvising scope.

### Worker task spec

You are the WORKER for Tier-2 round `assignments-worker-outbox-config`. Read this `## Plan` completely — it is your single source of truth. Implement steps 1–5 in order, stay strictly inside Scope IN and the 4-file expected-files table, honour decisions (a)–(d) as written, then run the build and the `SchoolCollab.ArchitectureTests.Unit` suite (including the AC2 discrimination probe). Read `AGENTS.md` and `.github/copilot/rules/dotnet-best-practices.md` before the `.cs` work, and `.github/copilot/rules/configuration-documentation.md` before the docs. Return **only** the WORKER REPORT block.

## Worker Report

> Worker `ollama-cloud/deepseek-v4.1-flash`, 250s. Returned verbatim.

```
WORKER REPORT
Changed files:
- src/AppHost/SchoolCollab.AppHost/Program.cs (M — 1 statement + block comment; +11 −1)
- tests/SchoolCollab.ArchitectureTests.Unit/AppHostOutboxExchangeWiringArchitectureTests.cs (new — 4 tests)
- documents/configuration.md (M — §2 assignments-worker paragraph only; single hunk @149, +15 −7)
- documents/solution/assignments-worker-outbox-exchange-fix.md (new)

Build: 0 errors (dotnet build SchoolCollab.slnx). No warning originates from either touched file — verified by grepping the build output for the two paths (0 hits). The 88 warnings on a clean full rebuild are the pre-existing NU1902/NU1903/ASPIRE010 + CS families; an incremental build reports only 26, which is why the count reads differently from a partial build.

Tests: tests/SchoolCollab.ArchitectureTests.Unit: 45 passed, 0 failed (41 at base -> 45; AC5's >=45 target met exactly).

AC2 probe: pre-removal 45 total / 0 failed. Post-removal 45 total / 1 failed / 44 succeeded, and the single failure was exactly `OutboxRegisteringHosts_GetOutboxExchangeName` (398ms) — the guard is discriminating, not tautological. Post-restore 45 total / 0 failed, and the restored chain was printed and confirmed byte-exact against the intended block.

Deviations from plan: none. The two-step source derivation was implemented as specified (scan `services.AddOutbox<` -> the `Add{Layer}()Core` it declares -> the `Program.cs` files calling those), so the documented fallback was not needed. Two observations, neither a deviation: (1) the derivation returns the 3 module cores and the 5 expected hosts exactly as the plan predicted; (2) `Program.cs` line 110's existing comment ("fanned out to the matching API/Worker") was left untouched — it was already correct, and this fix makes it true.
```

## Review

> Static diff-only reviewer `ollama-cloud/kimi-k2.7-code` — the **first** round to use the new reviewer default (owner 2026-09-22). No plan-review pass (Tier 2 has no plan gate). The reviewer never built or tested.

```
REVIEW
Verdict: PASS
P1: none
P2: documents/configuration.md:120 — §2 Parameters table still says `outbox-exchange-assignments` is injected "into `assignments-api`" only; it should now also list `assignments-worker` since the same parameter is reused there.
P2: documents/solution/assignments-worker-outbox-exchange-fix.md:60 — "the drain that sends them — the only component that resolves `IEmailSender` — is the API-side `NotificationDispatchSweepService`" is slightly imprecise; the sweep service creates a scope and delegates to `NotificationDispatchService`, which actually has `IEmailSender` injected. The underlying claim (worker never resolves it) is correct.
Best-practices: no overwrites; skills honored; readable
```

**Guard judgment (the round's main risk, independently confirmed):** the derivation is **non-tautological** — the guard discovers `Add{Layer}Core` names by scanning `src/**/Extensions.cs` for `services.AddOutbox<`, then discovers the consuming host projects by scanning `src/**/Program.cs` for those method calls, and only then maps project names to AppHost resource chains. `OutboxRegisteringHosts_GetOutboxExchangeName` **can** fail: removing the new `.WithEnvironment(...)` line from the `assignments-worker` chain removes the expected substring, and the failure message names `assignments-worker`. No vacuous assertions — the non-vacuity test and both tripwires are present and would fail if the regex/parser found nothing.

**P2 disposition — both folded, 1 rework iteration (bound 1/1).** Both findings were documentation-accuracy nits, not code defects. They were applied as a tiny two-hunk docs-only rework and **parent scope-checked** (diff re-inspected: the two edits touch only the named sentences; no code path affected), so no reviewer re-run was warranted per the skill. `documents/configuration.md` now lists `assignments-worker` in the `outbox-exchange-assignments` row and notes the `RabbitMq__Subscriber__ExchangeName` reuse; the solution note now attributes `IEmailSender` to `NotificationDispatchService`. The frozen patch was re-written after the rework.

## Acceptance

> Tier 2 — parent-authored plan, parent-transcribed verdict on the reviewer's PASS plus the parent's own authoritative pass and runtime check.

| Criterion | Verdict | Evidence |
|---|---|---|
| AC1 worker chain carries the injection | PASS | `git diff` inspection: `.WithEnvironment("Outbox__ExchangeName", assignmentsOutboxExchange)` added adjacent to the subscriber line; block comment now explains the outbox requirement and the deliberate `Smtp__*` exclusion |
| AC2 guard is discriminating | PASS | worker probe: removal → **45 total / 1 failed / 44 succeeded**, the single failure being exactly `OutboxRegisteringHosts_GetOutboxExchangeName`; restore → 45/0. Reviewer independently confirmed by reading the chain parser and assertion |
| AC3 tripwire + non-vacuity | PASS | `OutboxRegisteringHosts_AreTheReviewedSet` pins the 5 hosts; `Scan_ActuallyFoundTheAppHostAndTheOutboxHosts` requires ≥6 resources and non-empty sets; a 4th test `ModuleCoresRegisteringTheOutbox_AreTheReviewedSet` pins the 3 module cores |
| AC4 build | PASS | `dotnet build SchoolCollab.slnx` → **0 Error(s)**, 13 pre-existing warnings |
| AC5 affected suite | PASS | `dotnet test tests/SchoolCollab.ArchitectureTests.Unit` → **total 45, failed 0** (41 at base) |
| AC6 diff limited to the worker block | PASS | parent inspection: the only product change is the one env-var line + its comment; no other host block, `WithReference`, or `AddParameter` altered |
| AC7 docs | PASS | §2 `assignments-worker` paragraph names both injections + records the `Smtp__*` exclusion; `documents/solution/assignments-worker-outbox-exchange-fix.md` created |
| AC8 runtime (parent-run) | **PASS** | cold start on `13.5.4`: `assignments-worker` shows **Running / Healthy** (was `Finished` = crashed). `aspire logs assignments-worker` shows the dispatcher polling `SELECT * FROM outbox_messages … FOR UPDATE SKIP LOCKED` once per second and **no** `OptionsValidationException`. AppHost torn down afterwards; containers removed, `ar10repro` kept. |

**Findings:** 0 P1; 2 P2, both folded (rework iterations 1/1). Reviewer loop 1/1 used.

**Round verdict: CLOSED.** Change set is exactly the 4 expected files; the frozen patch is `diffs-assignments-worker-outbox-config.patch`.

**Out-of-scope observations for the owner (not defects):**

1. The runtime log shows the worker now genuinely **runs** the outbox dispatcher — polling the assignments `outbox_messages` table every second, in addition to `assignments-api`'s dispatcher. This is safe by construction (`FOR UPDATE SKIP LOCKED`, documented as supporting multiple instances) and adds throughput, but it is work the worker was never designed to do. If that is unwanted, the cleaner shape is not registering the outbox at all for worker hosts (a shared-extension change), which this round deliberately did not attempt.
2. `Aspire` reporting a crashed project resource as **`Finished`** (indistinguishable in `aspire describe` from a legitimately completed one-shot resource such as `migrator`) is what let this defect survive earlier cold starts — including the parent's own §2.1 run, where the worker was recorded as "Finished as designed". Recording `Finished` + a startup exception as a defect signature is worth a runbook line.
