# Parallel workers (opt-in fan-out) — orchestrator-worker-reviewer

The step-3 **worker-dispatch fan-out variant**: N workers in parallel, one per
slice, instead of one worker for the whole plan. **Worker role only** — the
plan, plan review, freeze, review, acceptance, tester, rework and escalation
steps run unchanged; this file changes only how workers are dispatched,
isolated and integrated. Adopted 2026-10-19 via grill rounds (owner settled
Q1–Q16). Design record — why these rules, and which alternatives were
rejected: `documents/solution/parallel-worker-fan-out.md`.

## When it fires (eligibility — mechanical, dispatch-time)

Opt-in per round — the owner's explicit choice, recorded on round-doc line 1
as `Workers: parallel (slices: A, B)`; never automatic. The gate is a
mechanical dispatch-time check, **not a tier gate** — the tiers table is
unchanged and self-selection does the tiering (a Tier 1 "small fix" cannot
partition into ≥2 slices of ~10+ files):

1. The plan's expected-files list partitions into **≥2 slices**: pairwise
   file-disjoint, none dropped, none in two slices (the same set-diff gate as
   the review split).
2. Each slice is **≥~10 expected files** (provisional — revisit after the first
   qualifying round; the concurrency default is 2 regardless, see below).
3. The seam scan (below) finds no unresolved cross-slice coupling.

## Slicing and seams (L1–L2)

- **L1 — partition check (mechanical):** the slices' file lists must partition
  the plan's expected-files list exactly. A file two slices both need means the
  slices are not independent — merge them into one slice.
- **L2 — seam scan (heuristic):** the parent scans for cross-slice couplings —
  shared symbols, DI registration pairings, wire contracts, call chains that
  cross the cut. A found seam is handled by:
  - **Merge by default** — the coupled slices become one slice, one worker,
    sequential inside it (the seam usually means the work is one logical unit).
  - **Serialize only** when the merged slice would exceed the ~3k-line
    review-split threshold: dispatch slice A, then materialize B's worktree
    at base + A's applied patch before dispatching B.

## Dispatch and isolation (L3–L4)

- **2 concurrent building workers by default; a user-named count wins** (record
  it on round-doc line 1). The owner ruling is the only mechanism that changes
  the default — the first qualifying round records memory/build behavior as
  *evidence*, not as a gate on the number. 3+ concurrent slots only for
  non-building slices (docs/research). Rationale: the OOM history — a default
  parallel build has killed the agent process (`0xC0000409`), and each worktree
  build is a *full* build, heavier than the incremental builds that history
  records.
- **Persistent per-slot worktrees** (e.g. `School-Collab-wa`, `School-Collab-wb`),
  created once and reused across rounds — matching the repo's existing
  multi-worktree practice. Retained incremental build state offsets the
  full-build cost of a fresh worktree; housekeeping (`git clean`) on re-use.
- **Parent-owned git — workers never run git.** Worktree create/clean/diff/
  apply all happen at setup/teardown, never while workers run.
- **Base materialization (no commit, no stash):** at fan-out setup the parent
  materializes the round base into each slot's worktree — checkout of the
  recorded base SHA **plus** the recorded dirty diff (the `git diff` captured
  at round start) applied — so slice patches diff against the true base and
  apply cleanly. The commit hold stands; no commit or stash is ever needed.
- **Brief:** each worker brief = worker contract + the plan inline + the
  slice's exact file list + **hard-pinned worktree path**. The worker touches
  nothing outside its worktree and nothing outside its slice.
- **L3 — confinement tripwire (mechanical):** after each worker settles, the
  parent checks the round tree's `git status --short` is unchanged AND the
  worktree diff's file list ⊆ the slice's list. Violation = revert the
  worktree (`git -C <worktree> checkout -- .`), re-dispatch with the path
  re-pinned; a worker that touched the round tree is a dispatch failure.
- **L4 — per-slice build (mechanical):** each worker builds its slice in its
  worktree against the materialized base; a forward cross-dependency (B's code
  referencing a symbol A has not written yet) fails B's build before merge.

## Worker verification scope in fan-out

Build + **unit tests only**, inside the worktree. **Never Testcontainers-based
integration tests** — per-project Docker Postgres + RabbitMQ ×N concurrent
workers on one host is the hazard. Integration coverage stays with the
parent's existing step-4 authoritative pass on the merged tree; the parent's
numbers remain the only source of truth. The WORKER REPORT gains the
slice/worktree prefix fields (see `role-contracts.md` — Worker); the parent
persists N sub-reports into the round doc.

## Integration (parent, at dispatch end)

Workers leave work **uncommitted**. The parent, per slice:
`git -C <worktree> diff > slice-<id>.patch`, then applies each onto the round
tree at the recorded base. File-disjointness (L1) makes application
conflict-free by construction. Step 4 then freezes **one combined
`diffs-<slug>.patch`** from the round tree, verbatim — the one-artifact cost
rule holds. Review reuses the existing **area-split pattern** (partition gate
+ seam tracing) when the combined patch exceeds the ~3k-line split threshold.

**L5 — authoritative merged pass (mechanical):** the parent's single build +
test + ArchitectureTests pass on the merged tree owns composition — per-slice
green never proves the integration (backward drift: A renamed a symbol B uses;
registration pairings; contract breaks surface only here). Residual leaks run
the existing bounded rework loop (L6) — on the round tree, one rework worker
at a time; per-slice worktrees are for the first implementation pass only.

## Independence — honest limits

File-level independence is airtight (L1 + L3 are set operations).
Compile-level independence is *detected* (L4 + L5). Semantic independence is
*bounded* (L6) — no layer proves it; the seam scan (L2) is the only
pre-dispatch defense.

## Park and resume

A slice that hits its time box parks the **round as a whole** (`Status:
PARKED`, no combined patch) — a partial freeze yields a
reviewed-but-never-integrated artifact whose cross-slice seams are unverified;
the acceptance gate cannot honestly pass it. Persistent worktrees keep
unfinished state alive; on resume, re-dispatch **only the unfinished slices**,
re-verifying each surviving worktree's diff against the recorded base before
trusting it.
