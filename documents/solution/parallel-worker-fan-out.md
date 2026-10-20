# Parallel-worker fan-out — findings, decisions, implementation

**Status:** shipped 2026-10-19 (PR #388, squash `9cbb2871`). Operational rules:
`.pi/skills/orchestrator-worker-reviewer/references/parallel-workers.md`.
Feature scope: the **worker role only** — every other step of the round is unchanged.

## Problem

A round dispatched one worker for the whole plan, so an implementation leg with
genuinely independent file sets ran serially. The skill already sanctioned
concurrent *verification* (parent build ‖ static reviewer; area-split reviewers)
but never concurrent *writing* — because of a verified incident: on 2026-10-07
(round `targets-dialog-picked-zone`) two writers in one checkout produced a diff
no reviewer could attribute. Meanwhile the repo routinely ran slice work in
separate worktrees (six coexisted at decision time), so the isolation mechanism
already existed; the skill simply never named it as a worker-dispatch option.

## Findings that shaped the design

| Finding | Consequence |
|---|---|
| Path-disjoint writers in ONE checkout are not isolated (2026-10-07 incident). | Workers must be isolated by **filesystem** — one git worktree per slice. |
| `git worktree add` carries **committed** state only. | The round base must be materialized into each worktree (base SHA + recorded dirty diff); no commit or stash is needed. |
| Default parallel builds have OOM-killed the agent (`0xC0000409`). | Concurrency is capped; each worktree build is a full build, heavier than the incremental builds in that history. |
| Integration tests use Testcontainers Postgres + RabbitMQ per project. | Concurrent integration runs would multiply Docker load; workers run **unit tests only** and the parent's merged pass owns integration. |
| Per-slice green never proves composition (backward drift, registration pairings). | The parent's single authoritative merged pass stays mandatory. |
| The tier table already guarantees a worker run at every invoking tier. | The variant needs **no tier gate** — eligibility is a mechanical property of the plan's file list. |

## Decisions (owner, grill rounds 2026-10-19)

1. **Opt-in per round**, recorded on round-doc line 1
   (`Workers: parallel (slices: A, B)`); never automatic.
2. **Eligibility is mechanical at dispatch time**: the plan's expected-files list
   partitions into ≥2 file-disjoint, seam-free slices (each ≥~10 files,
   provisional floor). No tier gate — Tier 1 self-excludes through its own size
   definition; Tiers 2–3 may qualify.
3. **Seams**: merge coupled slices by default; serialize only when the merged
   slice would exceed the ~3k-line review-split threshold.
4. **Concurrency: 2 building workers by default; a user-named count wins.** The
   first qualifying round's memory/build numbers are evidence, not a gate.
5. **Isolation**: persistent per-slot worktrees; parent-owned git (workers never
   run git); base materialization without commit or stash.
6. **Worker scope in fan-out**: build + unit tests in its own worktree; never
   Testcontainers integration tests.
7. **Integration**: uncommitted slice patches applied at the recorded base;
   step 4 freezes one combined `diffs-<slug>.patch` — the one-artifact rule
   holds; review reuses the area-split pattern (partition gate + seam tracing).
8. **Park/resume**: a time-boxed slice parks the round as a whole; resume
   re-dispatches only unfinished slices, re-verified against the recorded base.
9. **Scope**: worker role only. Plan, plan review, freeze, review, acceptance,
   tester, rework and escalation steps are byte-for-byte unchanged.

## Independence — what is actually guaranteed

- **L1 partition check** and **L3 confinement tripwire** are set operations —
  airtight for *file* independence.
- **L4 per-slice build** detects forward cross-dependencies (slice B referencing
  a symbol slice A has not written yet).
- **L5 authoritative merged pass** detects backward drift (A renamed what B
  uses; registration pairings; wire-contract breaks).
- **L2 seam scan** is the only pre-dispatch defense and is heuristic.
- **L6 bounded rework** absorbs what leaks.
- No layer *proves* semantic independence; the design bounds the risk instead.

## Implementation

| File | Change |
|---|---|
| `references/parallel-workers.md` | new — operational mechanics (eligibility, slicing, dispatch, guards, integration, park/resume) |
| `SKILL.md` step 3 | opt-in fan-out clause + pointer; §Round docs gains the line-1 provenance field; the one-writer pitfall gains the sanctioned-variant pointer |
| `references/role-contracts.md` | Worker fan-out contract (slice scope verbatim, pinned worktree, never run git, unit tests only) |

## Follow-ups

- Record the first qualifying round's memory/build behaviour (evidence only;
  the default stays 2 unless the owner intervenes).
- The ~10-file slice floor and the ~3k-line serialize threshold remain
  provisional.
- The variant has not yet run — no round has recorded `Workers: parallel (…)`.
