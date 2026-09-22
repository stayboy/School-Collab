---
name: orchestrator-worker-reviewer
description: Tiered orchestrator-led workflow for implementing features and fixes in the School-Collab repo, optimized for speed and token usage. Tier 1 collapses to a single worker run with the parent authoring the plan and transcribing acceptance; Tier 2 adds a static diff-only reviewer; Tier 3 runs the full four-agent pipeline - orchestrator (document owner) plans and owns the round doc, worker implements, reviewer statically verifies against the plan, acceptance is written by the orchestrator or transcribed by the parent on lean no-UI rounds, and a UI tester bug-hunts delivered UI work when the UI trigger fires. Use for feature implementation, multi-fix rounds, or any work where plans and reviews must be checked by a document owner before closing. Do NOT use for trivial single-file non-behavioural changes (do those solo).
---

# Orchestrator-Worker-Reviewer (with UI Tester) — tiered

Four roles collaborate on spec/plan-owned implementation with independent
verification, executed at the cheapest tier the task safely supports:

1. **Orchestrator** — document owner. Reads specs, plans, authors acceptance
   criteria, owns the round doc, and writes the acceptance verdict. In Tier 3
   it also derives the UI-tester scope handover. In Tiers 1–2 the **parent
   acts as orchestrator** (parent-authored plan, parent-transcribed verdict).
2. **Worker** — implements exactly the plan; runs build + affected tests; never
   edits the round doc; returns a structured WORKER REPORT.
3. **Reviewer** — **static; never builds or tests**. Two passes on the same
   model: the **plan-review pass** (Tier 3, *before* the worker is dispatched —
   feasibility, scope gates, acceptance honesty, security posture, judged
   against the plan doc + the seams it cites) and the **diff-review pass**
   (the worker's diff against the plan plus the best-coding-practices check).
   Returns a structured PLAN REVIEW or REVIEW block inline.
4. **UI Tester** — adversarial bug hunter over delivered UI, scoped
   **verbatim** to the orchestrator's handover. Not a second reviewer.
   Returns a structured UI TEST block inline.

Cost rules that apply to every round (speed + token budget):

- **Cheapest safe tier** — never pay for agents a task does not need.
- **One authoritative build/test pass** — the worker builds + tests the
  affected projects; the parent reruns the authoritative pass once
  (incremental); the reviewer never builds. Parent numbers are the only
  source of truth.
- **One round doc + one diff artifact** — hand-offs pass paths, not payloads;
  the plan is the single source of truth for worker and reviewer.

## Tiers

| Tier | Use when | Child runs | Acceptance |
|---|---|---|---|
| 0 | Trivial non-behavioural change (typo, comment, config tweak) | 0 — do not invoke this skill | solo, per `AGENTS.md` |
| 1 | Small behavioural fix passing the eligibility checklist | 1 (worker) | parent: scope check + authoritative build/test + transcribed verdict |
| 2 | Behavioural, no UI, single-context plan | 2 (worker + static reviewer); ≤1 rework iteration (worker + reviewer) | parent adjudicates REVIEW + transcribes verdict |
| 3 | Feature rounds, any UI round, anything failing the Tier-1 checklist | 4–6: orchestrator-plan, **reviewer plan-review**, worker, reviewer diff-review, then orchestrator-accept OR parent-transcribed acceptance (**lean**, no-UI rounds) + UI tester when the UI trigger fires | orchestrator writes the verdict, or the parent transcribes it on lean rounds (+ tester-scope handover when the tester fires) |

Default to the **lowest tier that qualifies**; when ambiguous, go one tier up.
When starting a feature or fix, offer the user the menu (solo / light round /
full four-agent) per repo `AGENTS.md` — do not default silently.

### Tier-1 eligibility checklist (ALL must hold)

- Single bounded context; expected diff ≤ ~4 files.
- No UI surfaces: no `.razor`, `.razor.css`, `.css`, or `.js` files, nothing
  under `wwwroot/`, no ApiClient / Blazor client project files.
- No EF migration, schema, or MassTransit contract changes; no new public API.
- Existing tests cover it, or the plan states why a test change is unnecessary.
- No interplay with other in-flight work.

### Tier 3 lean (no-UI feature rounds)

Formalized 2026-09-17 (owner), codifying what ar-17 already practised: when a
Tier 3 round's UI trigger does **not** fire, the round runs **lean** — the
orchestrator-plan, worker, and reviewer runs and the Tier-3 model ladder are
unchanged, but the parent **transcribes the acceptance** (on the reviewer's
verdict + its own authoritative pass) instead of dispatching an
orchestrator-accept run, and no UI tester is dispatched (there is nothing to
bug-hunt). Rework bound stays ≤2.

**The plan-review pass is NOT part of the lean/full split** (added 2026-09-18,
owner): every Tier 3 round — lean or full — has the static reviewer read the
**plan before the worker runs** (procedure step 2b). The plan is the round's most
load-bearing artifact and a defect in it is the cheapest defect to fix; ar-19
proved a diff-review alone cannot catch an unworkable premise (an EF helper that
would not translate survived the entire implementation and surfaced only in the
parent's own verification).

**Plan-review model per sub-mode (owner default 2026-09-18):** on **Tier 3 full**
the plan gate runs on the round's **higher model** — `ollama/glm-5.3:cloud` on the
pi profile, `cline-pass/glm-5.3` on clinepass — because it is the
highest-leverage review in the round (ar-20's plan review found 7 P1s before any
code was accepted, including a security hole in a pinned decision). On **Tier 3
lean** it runs on the round's *reviewer* model (`kimi-k2.7-code`). Both use
the read-only `reviewer` shell; a user-named model still wins (precedence item 1).

**Hand rule: lean drops the accept-run, never the plan-run.** The
orchestrator-plan pass is what separates Tier 3 from a stretched Tier 2 — it is
where the design questions get *resolved* (worker tenancy, dedupe/backfill
strategy for a mutating migration, contract inventories, idempotency
semantics). It is mandatory on every Tier 3 round, lean or full. Full Tier 3
(with the orchestrator-accept run) is for UI rounds — the tester fires anyway
and the document owner hands over tester scope — or whenever the owner wants
the plan's author to adjudicate the REVIEW personally.

There is no defined tier between 2 and 3. A round that fails the Tier-2
checklist only on file-level gates (migration, contracts, new project, diff
size) with its design fully settled may be run at Tier 2 as an explicit
**owner override**, recorded as a deviation on round-doc line 1 (the ar-16
precedent); if the design is open, the round is Tier 3.

### Mid-round escalation

If any agent (or the parent) discovers scope creep — more files than planned,
UI touched, schema/contract changes, wider behavioural surface — stop the fast
path and **bump the tier**; continue the round at the higher tier. Never force
a light tier through. Record escalations in the round doc.

## Round docs — one doc, one diff artifact

- `documents/rounds/round-<round-slug>.md` — the **single round doc** with
  sections `## Plan`, `## Worker Report`, `## Review`, `## Acceptance`,
  `## UI Tester` (fill only the tier-appropriate ones). Sole writer: the
  orchestrator run (Tier 3) or the parent (Tiers 1–2). Reviewer and tester
  never write files — they return structured blocks inline and the parent
  persists them into the doc. On Tier 3, `## Review` carries the **PLAN REVIEW**
  verdict first (from step 2b, with its dispatch timestamp) and the
  diff-review verdict beneath it, so the plan's own review is traceable.
- `documents/rounds/diffs-<round-slug>.patch` — written **once** by the parent
  from `git diff` immediately after the worker run; passed by path to the
  reviewer and tester instead of inline hunks.
- Round doc line 1 records provider + models (traceability — format in
  `references/models.md`).
- Never write durable specs here; fold a round's durable outcomes into
  `documents/specs/` when it closes. `documents/rounds/` is ephemeral — see
  `documents/rounds/README.md`.

## Models and per-tier strategy

Exact id tables, substitution rules, and the traceability format live in
`references/models.md`. Summary:

**Provider is a per-round choice, not a fixed setting.** Pick ONE profile for
the whole round, record it on round-doc line 1, and never mix providers
mid-round. Both profiles below are supported options; resolve the exact ids at
dispatch time with `subagent({ action: "models" })` and copy `provider/id`
verbatim (the `ollama*` ids have drifted historically — verify, don't assume).

| Role | pi `ollama` profile (long-standing default) | `clinepass` profile (option) | Tiers |
|---|---|---|---|
| Orchestrator | `ollama-cloud/glm-5.3-flash` | `cline-pass/glm-5.3` | 3 only |
| Worker | `ollama/deepseek-v4-flash:0731-cloud` | `cline-pass/deepseek-v4-flash` | 1–3 |
| Reviewer | `ollama-cloud/kimi-k2.7-code` (owner default 2026-09-22) | `cline-pass/deepseek-v4.1-flash` | 2–3 |
| **Plan reviewer** (Tier 3 **full**) | `ollama/glm-5.3:cloud` | `cline-pass/glm-5.3` | 3 full only — owner default 2026-09-18; lean rounds use the Reviewer row |
| UI Tester | `ollama/minimax-m3:cloud` | `cline-pass/minimax-m3` | 3 + UI |
| Escalator (blocked-pass rework) | the round's reviewer model | the round's reviewer model | on block |
| Higher-model re-verify | `ollama/glm-5.3:cloud` | `cline-pass/glm-5.3` | on escalation |

From pi, opt into the clinepass profile for a round by passing
`clinepass/cline-pass/<id>` (e.g. `clinepass/cline-pass/deepseek-v4.1-flash`) in
every role's `runs.run` — and record that choice in the round doc header.

**Per-mode model sets (owner overrides 2026-09-16):**

| Mode | Worker (implementer) | Orchestrator | Reviewer | Notes |
|---|---|---|---|---|
| **Solo** | the single agent does plan + implement + check itself | — | — | ask the user first (see the solo rule) |
| **Light (Tiers 1–2)** | `deepseek-v4.1-flash` | `glm-5.3-flash` (if dispatched) | `kimi-k2.7-code` | **the reviewer/orchestrator must NOT share the worker's model** — the verifier must not be the implementer's own model |
| **Tier 3** | `deepseek-v4-flash-0731` | `glm-5.3-flash` | `kimi-k2.7-code` | full ladder + UI tester `minimax-m3`; **plan-review on full rounds = `glm-5.3:cloud`** (lean = the reviewer model) |

**Solo rule:** a solo round is ONE agent doing everything (planner, implementer
and its own acceptance check) — the same shape as a light round's worker. Before
running solo, **always ask the user whether to use the current session model**;
use the session model only on their yes, otherwise fall back to the skill's solo
default **`glm-5.3-flash`**. Record the choice (session model or default) on
round-doc line 1.

**Defaults and overrides — precedence, highest first:**

1. **A per-role model the user names** for this round (e.g. “reviewer = X”) —
   that role only; every other role keeps the profile defaults.
2. **A provider the user names** for this round (“use clinepass”) — that
   profile's defaults for every role the user did not name individually.
3. **The profile already recorded in the round doc** (resumed or continuing
   rounds — the earlier decision carries forward).
4. **The skill default: the pi `ollama` profile** in the table above, per tier —
   subject to the **per-mode model sets** below it (solo / light / Tier 3), which
   the owner overrode on 2026-09-16; the pi-profile reviewer default was
   replaced on 2026-09-22). In light mode the worker runs `deepseek-v4.1-flash`
   while the orchestrator runs `glm-5.3-flash` and the reviewer runs
   `kimi-k2.7-code` — deliberately different models, so the verifier never shares
   the implementer's model. Escalating to Tier 3 restores the standard ladder
   (`glm-5.3-flash` orchestrator, `deepseek-v4-flash-0731` worker).
5. **Cline is the exception, not an override** — Cline cannot resolve `ollama`
   ids, so it always uses the `clinepass` profile.

Rules: an override governs the **whole round**, not a single dispatch — apply it
to every dispatch that has not yet started and never run two providers in one
round. **Exception:** a per-role model override that the user names explicitly
(precedence item 1) MAY sit on a different provider than the round's profile —
that is the one sanctioned way a round spans providers. Record it as an explicit
per-role override on round-doc line 1, and keep every role the user did not name
on the profile default. Record the effective choice on round-doc line 1; if the
override arrives
mid-round, also log it in the execution log with the reason. Roles already
dispatched keep the model they actually ran on (traceability). A user-named
**escalation** or **higher-model re-verify** model wins over the profile default
for that step. If an id is unavailable, substitute within the same tier and
record the substitute. **Never omit the `model` field** — an omitted field is
NOT the skill default: the child then inherits the current session model (one
id, on whatever provider the session is using), collapsing every role onto it.
Treat an omitted `model` as a dispatch error to fix, not a fallback.

## Provider profiles

- **pi (default):** run all phases as ONE `workflowScript` call with
  `async: true` using `await runs.run(...)`. Never combine structured
  single-child execution (`agent`+`task`) with `workflowScript`. Resolve the
  round's provider ids with `subagent({ action: "models" })` and copy exact
  `provider/id` strings — the `ollama*` ladder is the long-standing default, and
  the **`clinepass` profile is an available option** (`clinepass/cline-pass/<id>`
  for every role in the round).
- **Cline:** spawn teammates named `orchestrator`, `worker`, `reviewer`,
  `ui-tester` with the compact contracts from `references/role-contracts.md`.
  **Spawn once per session and reuse them across rounds** — do not re-spawn
  per round. The session provider is `clinepass`; `ollama/<id>:cloud` ids do
  not resolve there. If teammate dispatch
  returns `Unauthorized: ... re-authenticate your Cline account`, stop and
  re-authenticate before rerunning — a round must not proceed on a dead
  session.
- **Pick one provider per round; never mix mid-round.**

## Role contracts and structured output

Each child's task = its compact contract (see `references/role-contracts.md`)
+ the plan + the round-doc and patch paths. Children read the plan and patch
artifact from disk; they do NOT receive full reports verbatim and do NOT
re-read source specs — the plan is the single source of truth, and specs are
opened only to resolve ambiguity.

Structured blocks (formats in `references/role-contracts.md`): **WORKER
REPORT** (changed files, build/test verdicts, deviations), **PLAN REVIEW**
(Tier 3 only: feasibility/scope gates/acceptance honesty/security + P1/P2 with
file:line), **REVIEW** (P1/P2 with file:line evidence + best-practices check),
**UI TEST** (scope ack, P1/P2 with file:line, out-of-round observations).

## Deterministic UI-round trigger

After the worker run, the parent derives the UI verdict from
`git diff --name-only`: the round is a **UI round** iff the changed-file list
contains any `.razor`, `.razor.css`, `.css`, or `.js` file, anything under
`wwwroot/`, or a file in the ApiClient / Blazor client projects. Only UI
rounds get a tester pass; the tester-scope handover enumerates exactly these
surfaces — the changed files, the pages/dialogs/landing pages that render
them, the ApiClient methods they call, and the navigation entry points — and
the tester never derives or expands its own scope.

## Procedure

0. **Select the tier.** Apply the eligibility checklist; default to the lowest
   safe tier; a no-UI Tier 3 round runs **lean** (see "Tier 3 lean" above);
   offer the AGENTS.md menu when the choice is not obvious. Record
   the tier + provider + models in the round doc header, plus the round base
   (`git rev-parse HEAD`; if the tree is dirty at round start, also record
   `git diff --name-only` so the round diff can be isolated).
1. **Setup (once per session).** Resolve model ids (pi:
   `subagent({ action: "models" })` — choose the round's provider profile there).
   Cline:
   spawn the four teammates once and reuse them across rounds.
2. **Plan.** Tiers 1–2: the parent writes the `## Plan` section (goal, scope,
   expected files, acceptance criteria; Tier 2 adds the reviewer's acceptance
   criteria). Tier 3: the orchestrator run reads the source specs/review
   docs, writes `## Plan`, and authors the worker/reviewer task specs and
   acceptance criteria. The plan must be implementable standalone.
2b. **Plan review (Tier 3 — BEFORE the worker runs).** Dispatch the static
   reviewer against the **plan**, not a diff: the plan doc plus the code/spec
   seams it cites. It judges (i) **feasibility** — do the named files, routes,
   entities, clients and patterns actually exist and behave as claimed? (ii)
   **scope gates** — UI files, contract shape, migrations, committed secrets,
   CPM rules: is the in/out boundary honest? (iii) **acceptance honesty** —
   would each test the plan calls "discriminating" actually fail against the
   pre-fix code? (iv) **security posture** — auth schemes, claim handling,
   token validation, credential handling, tenant isolation; (v) **pinned
   decisions** — does the plan contradict or silently re-open one? It returns a
   **PLAN REVIEW** block. A plan **P1** → the orchestrator (Tier 3 full) or the
   parent (lean) revises `## Plan` **before dispatch**; the reviewer then
   re-reads only the revised sections. **≤1 plan-review iteration.** The worker
   is never dispatched on a plan with an open P1. The pass is static — any
   build/test numbers it volunteers are discarded like any other child's.
   **Model:** Tier 3 **full** → `ollama/glm-5.3:cloud` (`cline-pass/glm-5.3`);
   Tier 3 **lean** → the round's reviewer model. Read-only `reviewer` shell in
   both cases; a user-named model wins.
3. **Worker run.** Task = worker contract + the plan inline + expected files +
   round-doc path (the worker does not edit it). The worker implements, runs
   build + affected tests, returns WORKER REPORT. The parent persists the
   report into the doc.
   **Build-escalation pattern (worker block rule):** when a worker pass times
   out, stalls, or hangs mid-round (30-min cap, runaway shell command, repeated
   build failures it cannot recover from), the parent interrupts it and
   re-dispatches the SAME pass scope as an ESCALATION PASS on
   the round's reviewer model — the reviewer model IS the escalation
   executor — via a WRITE-CAPABLE agent shell (the `worker` or `delegate`
   agent; the `reviewer` agent shell is read-only by design and cannot
   execute passes) — which reconciles the on-disk state first, then
   completes the blocked pass. Subsequent worker passes revert to the worker
   model. **Escalated work is reviewed by the HIGHER model: the static
   re-verification of any pass completed via escalation runs on
   the round's provider's higher model (`ollama/glm-5.3:cloud` on the ollama
   profile, `clinepass/cline-pass/glm-5.3` on clinepass)** (user-set default
   2026-09-08; the escalator never
   re-verifies its own pass). One escalation per blocked pass; record
   the provenance in the round doc (e.g. "pass 3 completed via escalation").
   Do not steer or revive a run whose bash has been open past a plausible
   build/test window — interrupt it; a hung process never settles.
4. **Freeze the diff, then verify in parallel.** The parent writes
   `diffs-<slug>.patch` (`git diff`, or `git diff <base-sha>` when the tree
   was dirty at start), then concurrently:
   - (a) The parent runs the authoritative `dotnet build SchoolCollab.slnx`
     (incremental after the worker) and `dotnet test` on the affected
     projects **plus `SchoolCollab.ArchitectureTests.Unit`** (repo-wide
     scanner — always include it).
   - (b) Tiers 2–3: dispatch the static reviewer with the plan + patch path +
     WORKER REPORT; the reviewer returns the REVIEW block. Tier 1 has no
     reviewer — the parent does the scope check itself (diff-stat vs plan
     scope; unrelated deletions/reformatting are findings).

   Never start a build while a child may still write to the working tree
   (MSB3027 file locks). The static reviewer is safe to run in parallel
   precisely because it never builds.
5. **Accept.** Tiers 1–2: the parent adjudicates findings and transcribes the
   verdict into `## Acceptance` (criteria checklist, build/test numbers, P1
   list or CLOSED, residual P2s). Tier 3 full: the orchestrator-accept run
   receives the REVIEW block + the parent's build/test numbers and writes
   `## Acceptance`. Tier 3 **lean** (no-UI rounds): the parent transcribes the
   acceptance itself, on the reviewer's verdict plus its own authoritative pass
   — the ar-17 precedent. When the verdict is CLOSED and the UI trigger fires,
   the acceptance also appends the tester-scope handover.
6. **UI tester pass (Tier 3, UI rounds).** Task = tester contract + the
   handover verbatim + patch path. The tester bug-hunts only the handed-over
   surfaces and returns UI TEST; the parent persists it into `## UI Tester`.
7. **Bounded rework loops.**
   - Reviewer P1s → worker rework task (failing items + patch path only, no
     full history) → re-verify statically (reviewer, or parent scope-check
     for tiny rework diffs). **Tier 2: ≤1 iteration. Tier 3: ≤2.**
   - Tester P1s → orchestrator (Tier 3) or parent appends a rework plan → one
     worker run → **tester re-verifies only**; the parent statically checks
     the rework diff for plan conformance (no reviewer re-run). **≤2
     iterations.**
   - At the bound, surface residuals to the user instead of looping.
8. **Report.** Per-agent status, build/test counts, findings, rework
   iterations, round-doc path. Fold durable outcomes into the spec and update
   the backlog.

## Pitfalls

- **Never combine structured single-child execution with `workflowScript`.**
- **Test-output starvation kills worker passes.** Two worker timeouts in
  round ar-15 were self-reported as *"I cannot clearly see pass/fail due to
  tooling"* — the worker burned its 30-minute cap fighting truncated MSTest
  output, not the code. Treat the output rule as a hard gate in every worker
  task spec: exactly one `dotnet test tests/<X> 2>&1 | grep -E "^\s*failed
  |total:|failed:" | head -40`; no ad-hoc pipelines, no `zz*.log` debug
  files; at most 2 attempts per result read; if output is truncated, redirect
  to a file once and read the tail. A worker that starts narrating tooling
  problems instead of failures is on the timeout path — interrupt early.
- **A MockHttp matcher without an explicit HTTP method shadows later
  method-specific matchers** (first match wins). In ar-15 the context mock's
  method-agnostic `When(url)` swallowed the sign **POST**, so the bUnit test
  named for that POST never exercised it and failed on a 2 s
  `WaitForAssertion`. Always pass `HttpMethod.Get`/`HttpMethod.Post`; when the
  same URL answers before *and* after a mutation, use a counter-based
  `.Respond(_ => …)` sequence, not two same-URL matchers.
- **FluentUI web components cannot be driven with generic bUnit events.**
  `TriggerEvent("oncheckedchange", new ChangeEventArgs{…})` throws inside the
  page's `ErrorBoundary` (`ChangeEventArgs` cannot convert to
  `CheckboxChangeEventArgs`), which reads as a page defect when it is a test
  defect. Drive the bound callback instead:
  `cut.InvokeAsync(() => cut.FindComponent<FluentCheckbox>().Instance.ValueChanged.InvokeAsync(true))`
  (the `InvokeAsync` wrapper is required — invoking off the renderer thread
  raises *"not associated with the Dispatcher"*).
- **`workflowScript` continuation may not persist across detached children** —
  if the workflow errors `unsupported-continuation`, recover each child via
  `subagent({ action: "status", id, view: "transcript" })`. All durable round
  state is already on disk (round doc + patch), so resuming is cheap.
- **The reviewer is static by design.** If it reports build/test numbers,
  discard them — the parent is the only build/test authority; never trust
  child-reported counts.
- **Workers sometimes overwrite code, ignore repo skills, or skip repo
  conventions** — the reviewer's best-coding-practices check exists for this.
  A P1 overwrite finding goes through the rework loop like any other P1.
- **Do not build while a child can still write to the tree** (MSB3027 locks).
  Safe pattern: worker settled → patch frozen → parent build in parallel with
  the static reviewer.
- **Bare model ids resolve only when unique** — copy exact `provider/id`
  strings (`references/models.md`).
- **Only the orchestrator (Tier 3) or the parent (Tiers 1–2) writes the round
  doc** — never the worker, reviewer, or tester.
- **The UI tester is not a second reviewer** — no plan conformance; scope
  comes verbatim from the handover; out-of-scope findings are parent
  observations (optional backlog item), never rework.
- **Escalate instead of forcing a light tier through.**
- Children may pause for supervisor decisions via intercom (pi) — reply, then
  wait for the child to settle.
- **Runaway shell commands hang worker runs** — a worker once launched
  `find / -name "..."` (a filesystem-wide scan from the root) and blocked
  the run for 28 minutes. Worker task specs must carry the guard: repo-scoped
  searches only (`grep`/`rg` under `src/`, `tests/`, or the NuGet cache under
  `~/.nuget/packages`), never `find /`. If a worker's bash stays open past a
  plausible build/test window, interrupt and escalate the pass per the
  build-escalation pattern in step 3 — steering a hung process is wasted
  quota.

## Verification

1. All child runs completed (subagent status completed, exit 0).
2. Parent-run `dotnet build SchoolCollab.slnx -c Debug`: 0 errors.
3. Parent-run `dotnet test` — affected projects **plus
   `SchoolCollab.ArchitectureTests.Unit`**: 0 failures; pass counts recorded
   in the round doc.
4. The round doc exists with tier-appropriate sections filled and an explicit
   verdict (CLOSED, or remaining P1s listed), plus the provider/models header.
5. No P1 findings remain unaddressed, or the user explicitly accepted the
   residuals. Loop bounds respected (Tier 2 reviewer loop ≤1; Tier 3 reviewer
   ≤2, tester ≤2).
6. The relevant backlog/spec doc is updated with completion notes; durable
   outcomes folded into `documents/specs/`.
7. **Tier 3 only:** the plan carries a PLAN REVIEW with no open P1 from *before*
   the worker was dispatched (step 2b), recorded in the round doc's `## Review`
   above the diff-review verdict.