---
name: orchestrator-worker-reviewer
description: Tiered orchestrator-led workflow for implementing features and fixes in the School-Collab repo, optimized for speed and token usage. Tier 1 collapses to a single worker run with the parent authoring the plan and transcribing acceptance; Tier 2 adds a static diff-only reviewer; Tier 3 runs the full four-agent pipeline - orchestrator (document owner) plans and owns the round doc, worker implements, reviewer statically verifies against the plan, orchestrator accepts and hands over UI-tester scope, and a UI tester bug-hunts delivered UI work. Use for feature implementation, multi-fix rounds, or any work where plans and reviews must be checked by a document owner before closing. Do NOT use for trivial single-file non-behavioural changes (do those solo).
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
3. **Reviewer** — **static, diff-only**: verifies the worker's diff against
   the plan plus the best-coding-practices check. Never builds or tests.
   Returns a structured REVIEW block inline.
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
| 3 | Feature rounds, any UI round, anything failing the Tier-1 checklist | 4–5: orchestrator-plan, worker, reviewer, orchestrator-accept (+ UI tester when the UI trigger fires) | orchestrator writes the verdict (+ tester-scope handover) |

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
  persists them into the doc.
- `documents/rounds/diffs-<round-slug>.patch` — written **once** by the parent
  from `git diff` immediately after the worker run; passed by path to the
  reviewer and tester instead of inline hunks.
- Round doc line 1 records provider + models (traceability — format in
  `references/models.md`).
- Never write durable specs here; fold a round's durable outcomes into
  `documents/specs/` when it closes. `documents/rounds/` is ephemeral — see
  `documents/rounds/README.md`.

## Models and per-tier strategy

Exact id tables (pi + clinepass), substitution rules, and the traceability
format live in `references/models.md`. Summary:

| Role | pi default | clinepass | Tiers |
|---|---|---|---|
| Orchestrator | `ollama/glm-5.3-flash:cloud` | `cline-pass/glm-5.3` | 3 only |
| Worker | `ollama/deepseek-v4-flash:0731-cloud` | `cline-pass/deepseek-v4-flash` | 1–3 |
| Reviewer | `ollama/kimi-k2.7-code:cloud` | `cline-pass/kimi-k2.7-code` | 2–3 |
| UI Tester | `ollama/minimax-m3:cloud` | `cline-pass/minimax-m3` | 3 + UI |

## Provider profiles

- **pi (default):** run all phases as ONE `workflowScript` call with
  `async: true` using `await runs.run(...)`. Never combine structured
  single-child execution (`agent`+`task`) with `workflowScript`. Model ids come
  from `subagent({ action: "models" })` — copy exact `provider/id` strings.
- **Cline:** spawn teammates named `orchestrator`, `worker`, `reviewer`,
  `ui-tester` with the compact contracts from `references/role-contracts.md`.
  **Spawn once per session and reuse them across rounds** — do not re-spawn
  per round. Switch the session's provider to `clinepass` before starting
  (pi's `ollama/<id>:cloud` ids do not resolve in Cline). If teammate dispatch
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
REPORT** (changed files, build/test verdicts, deviations), **REVIEW** (P1/P2
with file:line evidence + best-practices check), **UI TEST** (scope ack,
P1/P2 with file:line, out-of-round observations).

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
   safe tier; offer the AGENTS.md menu when the choice is not obvious. Record
   the tier + provider + models in the round doc header, plus the round base
   (`git rev-parse HEAD`; if the tree is dirty at round start, also record
   `git diff --name-only` so the round diff can be isolated).
1. **Setup (once per session).** Resolve model ids (pi:
   `subagent({ action: "models" })`; Cline: switch to `clinepass`). Cline:
   spawn the four teammates once and reuse them across rounds.
2. **Plan.** Tiers 1–2: the parent writes the `## Plan` section (goal, scope,
   expected files, acceptance criteria; Tier 2 adds the reviewer's acceptance
   criteria). Tier 3: the orchestrator run reads the source specs/review
   docs, writes `## Plan`, and authors the worker/reviewer task specs and
   acceptance criteria. The plan must be implementable standalone.
3. **Worker run.** Task = worker contract + the plan inline + expected files +
   round-doc path (the worker does not edit it). The worker implements, runs
   build + affected tests, returns WORKER REPORT. The parent persists the
   report into the doc.
4. **Freeze the diff, then verify in parallel.** The parent writes
   `diffs-<slug>.patch` (`git diff`, or `git diff <base-sha>` when the tree
   was dirty at start), then concurrently:
   - (a) The parent runs the authoritative `dotnet build SchoolCollab.sln`
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
   list or CLOSED, residual P2s). Tier 3: the orchestrator-accept run receives
   the REVIEW block + the parent's build/test numbers and writes
   `## Acceptance`; when the verdict is CLOSED and the UI trigger fires, it
   also appends the tester-scope handover.
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

## Verification

1. All child runs completed (subagent status completed, exit 0).
2. Parent-run `dotnet build SchoolCollab.sln -c Debug`: 0 errors.
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