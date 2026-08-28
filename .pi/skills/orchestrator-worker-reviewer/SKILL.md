---
name: orchestrator-worker-reviewer
description: Four-agent orchestrator-led workflow for implementing features and fixes with spec/plan ownership in the School-Collab repo. An orchestrator (document owner) plans and owns the plan/acceptance docs, a worker implements, a reviewer verifies against the plan, and the orchestrator runs an acceptance pass — then a UI tester (4th agent) bug-hunts delivered UI work after the round closes, with findings looping back to the orchestrator for rework planning. Default agents are provided; all four are configurable via exact provider/id strings. Use for feature implementation, multi-fix rounds, or any work where plans/review docs written must be checked by a document owner before closing.
---

# Orchestrator-Worker-Reviewer (with UI Tester)

A spec/plan-owned implementation workflow with independent verification. Four
agents collaborate inside one `workflowScript`:

1. **Orchestrator** — document owner. Reads specs/review docs, writes the plan,
   authors the worker + reviewer tasks, and runs the final acceptance pass.
   Only the orchestrator edits the plan/acceptance docs. When the UI tester
   reports findings, the orchestrator authors the rework plan for the worker.
2. **Worker** — implements exactly what the orchestrator's plan specifies.
   Runs build + affected tests. Does **not** touch the plan/acceptance docs.
3. **Reviewer** — verifies the worker's diffs against the orchestrator's plan
   and the source specs; runs build + tests independently; writes a review doc.
   The reviewer's task ALSO includes a **best-coding-practices check**: verify
   the worker did not overwrite or delete pre-existing code outside the
   plan's scope (no destructive rewrites of untouched regions); that it used
   the repo's installed skills where applicable (e.g. `dotnet-best-practices`,
   `dialog-ui`, `blazor-css-isolation`, `fluentui-*`, `author-component`,
   `collect-user-input`); and that the new code is concise, readable, and
   maintainable (naming, minimal diff, no dead code, follows repo
   conventions). Violations are findings (P1 for destructive overwrites /
   ignored mandated skills, P2 for readability nits), each with file+line
   evidence. If the reviewer lacks shell/file-write tools, it returns the full
   report + acceptance JSON inline so the parent persists it.
4. **UI Tester** — adversarial bug hunter over the **delivered UI work**, run
   after the orchestrator-accept pass closes the round. Its scope comes
   **verbatim from the orchestrator's tester-scope handover** (the affected
   pages/dialogs/landing pages/clients the orchestrator enumerated from the
   worker's diff); the tester neither derives nor expands its own scope. NOT a
   second reviewer: it does not check plan conformance; it hunts for real
   user-facing defects the conformance pass structurally misses (swallowed
   errors, perpetual spinners, missing error surfaces, invisible validation,
   wrong bindings, silent no-ops, accessibility regressions, broken
   refresh/navigation). Anything outside the handover scope is reported as an
   out-of-round observation for the parent, not a rework item. Its findings
   (P1/P2/pass, with file+line evidence) loop back to the orchestrator,
   which appends a rework plan for the worker; the loop repeats until the
   tester passes (bounded — see Pitfalls).

## Default agents

These defaults are wired for the current School-Collab multi-model setup. Copy
an exact `provider/id` from `subagent({ action: "models" })` to override any of
them (bare ids resolve only when unique in the registry).

| Role | Default agent definition | Default model |
|------|--------------------------|---------------|
| Orchestrator | `delegate` (or `oracle`) | `ollama/glm-5.3-flash:cloud` |
| Worker | `worker` | `ollama/deepseek-v4-flash:0731-cloud` |
| Reviewer | `reviewer` | `ollama/kimi-k2.7-code:cloud` |
| UI Tester | `worker` (or custom) | `ollama/minimax-m3:cloud` |

To use different models, pass the exact `provider/id` in each `runs.run`'s
`model` field, e.g. `model: "github-copilot/claude-sonnet-5"`. To use a
different agent definition, pass `agent: "<name>"` (one of the builtin
`delegate`/`oracle`/`reviewer`/`worker`/`scout`/`researcher`, or a custom agent).

## When to Use

Use when a task needs a spec/plan-owned implementation pass with independent
verification: feature implementation, multi-fix rounds, or any work where a
document-owner should plan, a worker should code, and a reviewer should verify
— with the orchestrator accepting the review for correctness before the round
closes. Especially suited to rounds where review docs or plans written must be
checked by a document owner before closing.

## Procedure

1. **Confirm the three agents are registered.** Run
   `subagent({ action: "models" })` and copy exact `provider/id` strings for
   orchestrator, worker, reviewer (or accept the defaults above). Override any
   by copying exact ids.
2. **Author the orchestrator task.** Read the source specs/review docs, write a
   plan (or refine an existing one), define the worker's implementable task and
   the reviewer's acceptance criteria, and state which doc(s) the orchestrator
   owns. Orchestrator output must include (a) the plan, (b) the worker task
   text, (c) the reviewer task text, and (d) the acceptance doc path it will
   own.
3. **Author the worker task.** Implement exactly what the orchestrator plan
   specifies; run build + affected tests; do **not** edit the orchestrator's
   plan/acceptance docs. Return a concise changed-files + build/test report.
4. **Author the reviewer task.** Verify the worker's diffs against the
   orchestrator plan and the source specs; run build + tests independently;
   write findings to a review doc. The task MUST also instruct the reviewer to
   run a **best-coding-practices check** on the worker's diff:
   - **No overwrites:** the diff must not rewrite or delete pre-existing code
     outside the plan's scope (flag unrelated deletions/reformatting/
     "improvements" the plan never asked for — they hide real changes and
     break review).
   - **Repo skills honored:** where the touched surface has an applicable
     installed skill (e.g. `dotnet-best-practices`, `dialog-ui`,
     `blazor-css-isolation`, `fluentui-*`, `author-component`,
     `collect-user-input`), the implementation must follow it; deviations are
     findings.
   - **Readability & maintenance (concise):** naming matches repo conventions,
     minimal focused diff, no dead/duplicated code, follows the repo's
     established patterns.
   Report violations as P1 (destructive overwrites, ignored mandated skills) /
   P2 (readability nits) with file+line evidence. If the reviewer lacks shell
   tools, instruct it to return the full report + acceptance JSON in its final
   response so the parent can persist and run build/tests.
5. **Run all phases as ONE `workflowScript` call with `async: true`.** Order:
   - `await runs.run('orchestrator', { agent: 'delegate', model: <orchestratorId>, task: orchestratorTask })`
   - `await runs.run('worker', { agent: 'worker', model: <workerId>, task: workerTask })`
   - `await runs.run('reviewer', { agent: 'reviewer', model: <reviewerId>, task: reviewerTask })`
   - `await runs.run('orchestrator-accept', { agent: 'delegate', model: <orchestratorId>, task: acceptTask })`

   Pass the orchestrator's plan text into the worker task; pass the worker's
   report (or the plan + git-diff instructions) into the reviewer task; pass
   the reviewer's report + worker report into the acceptance task. Await
   sequentially.
6. **Loop on P1 gaps.** After the reviewer settles, read its report. If it
   raised P1 gaps the worker can fix, send a follow-up worker task referencing
   the P1 items, then re-run the reviewer. Stop when no P1 remains or the user
   accepts the residual risk.
7. **Orchestrator acceptance pass + tester-scope handover.** Run a final
   `runs.run('orchestrator-accept', ...)` giving the orchestrator the
   reviewer's report and the final build/test results; it writes/appends the
   acceptance verdict to its owned doc and either closes the round or lists
   remaining P1 items. **When the verdict is CLOSED and the round touched UI,
   the acceptance pass must also produce the UI-tester scope handover**: from
   the worker's changed-files report (and `git diff --name-only`), the
   orchestrator enumerates every affected UI surface — the changed
   `.razor`/`.razor.css` files themselves, the pages/landing pages/dialogs
   that render them, the ApiClient methods they call, and any navigation
   entry points that reach them — and appends that explicit, closed list
   (with a one-line rationale per entry) to its owned doc. This list is the
   tester's ENTIRE scope; the tester must not derive or expand its own scope.
8. **UI-tester pass (4th agent).** When the round touched UI and the
   acceptance verdict is CLOSED, run
   `runs.run('ui-tester', { agent: 'worker', model: <testerId>, task: testerTask })`.
   **The task must carry the orchestrator's scope handover verbatim** (the
   affected-surfaces list from step 7) as the tester's complete scope — the
   tester bug-hunts ONLY those surfaces: swallowed errors, perpetual
   spinners, missing state rendering, invisible validation, wrong bindings,
   DTO/property mismatches that surface as silent no-ops, accessibility
   regressions. It must NOT review unrelated UI pages/components; findings
   outside the handover scope are returned as out-of-round observations for
   the parent, not rework items. It returns P1 / P2 / pass with file+line
   evidence, inline in its final response; the parent persists the report.
9. **Tester-findings rework loop.** If the tester reports P1 (or P2 the user
   wants fixed), run `runs.run('orchestrator-rework', { agent: 'delegate',
   model: <orchestratorId>, task: reworkTask })` with the tester's findings —
   the orchestrator appends a rework plan to its owned doc and emits a focused
   worker task. Then worker fixes → tester re-verifies (re-run step 8 with the
   rework diff folded into the handover scope). Stop when the tester passes or
   the user accepts the residuals. **Bound the loop at ~2 rework iterations** —
   after that surface residuals to the user instead of looping forever.
10. **Parent is the source of truth for build/test numbers.** From the parent,
   run `dotnet build SchoolCollab.sln -c Debug --nologo -v q` and the affected
   `dotnet test` projects yourself; merge the authoritative numbers into the
   orchestrator's acceptance doc. Persist any reviewer report the reviewer
   could not write itself.
11. **Report to the user:** per-agent status, build/test counts, reviewer +
   tester findings, rework iterations, and the acceptance-doc path. Update the
   relevant backlog with completion notes.

## Pitfalls

- **Never combine structured single-child execution (`agent`+`task`) with
  `workflowScript`.** Use `workflowScript` with `async: true` and
  `runs.run` / `runs.all` inside it, or use standalone sequential `subagent`
  calls. Do not mix.
- **`workflowScript` continuation is not always persisted across detached
  children.** If the workflow errors with `unsupported-continuation` after a
  child settles, recover each child result with
  `subagent({ action: "status", id, view: "transcript" })` and read its output
  file, then finish from the parent.
- **Reviewer read-only agents often lack bash/file-write tools.** Tell the
  reviewer up front to return the full report text + acceptance JSON in its
  final response; the parent persists the review doc and runs build/tests.
- **Workers sometimes overwrite code, ignore repo skills, or skip repo
  conventions.** That is why the reviewer task explicitly includes the
  best-coding-practices check (no overwrites / repo skills honored /
  readability & maintenance). Do not let the round close on "plan-conformance
  only" when these violations exist — a P1 overwrite finding goes through the
  same rework loop as any other P1.
- **Children may pause and request supervisor decisions via intercom.** Reply
  with `subagent_supervisor({ action: "reply", replyTo: <id>, message: ... })`
  then `subagent_wait({ id: <childId> })` to resume.
- **Bare model ids resolve only when unique.** Always copy the exact
  `provider/id` from the models list (e.g. `ollama/glm-5.3-flash:cloud`, not
  `glm-5.2`).
- **Do not let the worker edit the orchestrator's plan or acceptance docs.**
  That breaks the ownership/correctness contract. Only the orchestrator owns
  those.
- **The parent is the final source of truth for build/test numbers.** Always
  rerun them from the parent; do not trust child-reported counts blindly.
- **The UI tester is NOT a second reviewer.** Do not ask it to verify plan
  conformance (the reviewer did that) — ask it to hunt for user-facing defects
  the plan-conformance pass structurally misses (silent failures, missing
  error surfaces, loading states that never resolve).
- **The UI tester must stay scoped to the round's touched UI surfaces**
  (changed files + the pages/dialogs they directly render into). Findings
  about unrelated UI pages/components are out of round: record them as parent
  observations (optionally a backlog item), never as rework for this round.
  Overreach wastes cycles and muddies the acceptance doc.
- **Bound the tester-rework loop** (max ~2 rework iterations per round); after
  that surface residuals to the user instead of looping forever.
- **Name collisions warn and keep the first skill found.** This local skill
  intentionally mirrors the global `global:orchestrator-worker-reviewer` skill;
  the repo-local copy wins when working in this repo.

## Verification

1. All child runs complete (`subagent` status shows each as completed exit 0).
2. Parent-run `dotnet build SchoolCollab.sln -c Debug` reports 0 errors.
3. Parent-run `dotnet test` for affected projects reports 0 failures; record
   the pass counts.
4. The orchestrator's acceptance doc exists and contains the plan, the
   reviewer's findings, and an explicit close-or-remaining-P1 verdict — plus,
   for UI rounds, the tester-scope handover (affected UI surfaces list), the
   UI tester's findings, and the final tester verdict.
5. No P1 findings (reviewer — including best-practices violations — or
   tester) remain unaddressed, or the user has explicitly accepted them.
6. The relevant backlog/spec doc is updated with completion notes.