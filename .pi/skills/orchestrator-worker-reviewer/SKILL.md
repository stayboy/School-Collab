---
name: orchestrator-worker-reviewer
description: Three-agent orchestrator-led workflow for implementing features and fixes with spec/plan ownership in the School-Collab repo. An orchestrator (document owner) plans and owns the plan/acceptance docs, a worker implements, a reviewer verifies against the plan, and the orchestrator runs an acceptance pass. Default agents are provided; all three agents are configurable via exact provider/id strings. Use for feature implementation, multi-fix rounds, or any work where plans/review docs written must be checked by a document owner before closing.
---

# Orchestrator-Worker-Reviewer

A spec/plan-owned implementation workflow with independent verification. Three
agents collaborate inside one `workflowScript`:

1. **Orchestrator** — document owner. Reads specs/review docs, writes the plan,
   authors the worker + reviewer tasks, and runs the final acceptance pass.
   Only the orchestrator edits the plan/acceptance docs.
2. **Worker** — implements exactly what the orchestrator's plan specifies.
   Runs build + affected tests. Does **not** touch the plan/acceptance docs.
3. **Reviewer** — verifies the worker's diffs against the orchestrator's plan
   and the source specs; runs build + tests independently; writes a review doc.
   If the reviewer lacks shell/file-write tools, it returns the full report +
   acceptance JSON inline so the parent persists it.

## Default agents

These defaults are wired for the current School-Collab multi-model setup. Copy
an exact `provider/id` from `subagent({ action: "models" })` to override any of
them (bare ids resolve only when unique in the registry).

| Role | Default agent definition | Default model |
|------|--------------------------|---------------|
| Orchestrator | `delegate` (or `oracle`) | `ollama/glm-5.3-flash:cloud` |
| Worker | `worker` | `ollama/deepseek-v4-flash:0731-cloud` |
| Reviewer | `reviewer` | `ollama/kimi-k2.7-code:cloud` |

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
   write findings to a review doc. If the reviewer lacks shell tools, instruct
   it to return the full report + acceptance JSON in its final response so the
   parent can persist and run build/tests.
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
7. **Orchestrator acceptance pass.** Run a final
   `runs.run('orchestrator-accept', ...)` giving the orchestrator the
   reviewer's report and the final build/test results; it writes/appends the
   acceptance verdict to its owned doc and either closes the round or lists
   remaining P1 items.
8. **Parent is the source of truth for build/test numbers.** From the parent,
   run `dotnet build SchoolCollab.sln -c Debug --nologo -v q` and the affected
   `dotnet test` projects yourself; merge the authoritative numbers into the
   orchestrator's acceptance doc. Persist any reviewer report the reviewer
   could not write itself.
9. **Report to the user:** per-agent status, build/test counts, P1/P2 findings,
   and the acceptance-doc path. Update the relevant backlog with completion
   notes.

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
- **Name collisions warn and keep the first skill found.** This local skill
  intentionally mirrors the global `global:orchestrator-worker-reviewer` skill;
  the repo-local copy wins when working in this repo.

## Verification

1. All child runs complete (`subagent` status shows each as completed exit 0).
2. Parent-run `dotnet build SchoolCollab.sln -c Debug` reports 0 errors.
3. Parent-run `dotnet test` for affected projects reports 0 failures; record
   the pass counts.
4. The orchestrator's acceptance doc exists and contains the plan, the
   reviewer's findings, and an explicit close-or-remaining-P1 verdict.
5. No P1 findings remain unaddressed, or the user has explicitly accepted them.
6. The relevant backlog/spec doc is updated with completion notes.