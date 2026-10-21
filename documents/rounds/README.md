# documents/rounds — ephemeral per-round agent docs

This folder is the **quarantine area for the working docs produced by the
tiered four-agent workflow** (`orchestrator-worker-reviewer` skill). Each
round produces exactly two files:

- `round-<round-slug>.md` — the single round doc, with sections `## Plan`,
  `## Worker Report`, `## Review`, `## Acceptance`, `## UI Tester`. Fill only
  the tier-appropriate ones: Tiers 1–2 fill Plan + Worker Report + Acceptance
  (Tier 2 adds Review); **Tier 0 (Solo) fills Plan + Worker Report + Review (marked self-run) +
  Acceptance** — the diff review is never skipped, only its independent seat; Tier 3 fills
  them all (+ UI Tester for UI rounds).
- `diffs-<round-slug>.patch` — the round's `git diff`, written once by the
  parent after the worker run and shared with the reviewer/tester by path.

It exists so that `documents/specs/` stays reserved for **durable feature
specs that remain the source of truth**.

## Rules

- One round doc per round — do not scatter `plan-*` / `review-*` /
  `acceptance-*` / `ui-tester-*` files. The orchestrator run (Tier 3) or the
  parent (Tiers 1–2) is the sole writer; reviewer and tester findings are
  persisted by the parent from their inline blocks.
- **Every round ships its own `diffs-<slug>.patch`.** A round without its patch
  cannot be reviewed or reconstructed later: on 2026-10-07 a reviewer could diff a
  round only by rebuilding its delta from a *sibling* round's patch. Generate the
  patch alongside the round doc, from the round's own base.
- **An unattended round lists its pinned decisions twice.** The round doc carries
  `## Pinned decisions (unattended …)`, `## Acceptance` repeats the same entries, and the
  completion report repeats them again as a table. This folder is ephemeral, so a pin that
  lives only here is a pin the owner will never see again (owner, 2026-10-21).
- **Flip the round doc's `**Status:**` line when the round closes** — set it to
  `CLOSED` and name the PR/commit that carried it, or to `PARKED` when the round was
  stopped mid-flight by the time-box rule
  (`.pi/skills/orchestrator-worker-reviewer/SKILL.md`, step 3). A parked round names the
  backlog/follow-up item it was parked as and ships its `diffs-<slug>.patch` frozen at the
  point of the park, so `resume-interrupted-orchestrator-round` can pick it up. Round docs
  left at `planned`
  rot silently: three were found stale on 2026-10-06 (their rounds had merged days
  earlier), and a reader cannot tell a finished round from an abandoned one. Same
  rule as the status-line convention in `documents/README.md`.
- Round docs and patches are **ephemeral residue**: once a round's durable
  outcomes are folded into the feature spec in `documents/specs/`, this whole
  folder is **safe to bulk-trash**.
- **Trash cadence (owner, optimisation round Q105).** The folder grows by two
  files per round (117 docs + 117 patches at the time of writing). Keep, and
  trash the rest **when an arc closes** (its PRs merged, its outcomes folded
  into `documents/specs/`):

  | Keep | Why |
  |---|---|
  | every round whose PR/stack is still **open** | a reviewer may still need its frozen patch |
  | the **last 2 closed** rounds | the arc's most recent evidence, still being cited |
  | any round named as **PARKED** | `resume-interrupted-orchestrator-round` needs it |

  Everything else goes in one bulk delete, in its own commit — never mixed with a
  round's own change set. Keep committing the patch while you keep the doc: the
  frozen patch is what let the diff gate work statically, and (c) "stop
  committing patches" was considered and rejected (Q105).
- **Record the wait cost (owner, optimisation round Q113).** Three numbers at round
  close, beside the gate results — they are what would have caught the 919k-token
  worker timeout while it was happening instead of afterwards:

  | Number | How to read it |
  |---|---|
  | **wait calls** | tool calls whose entire output was a status line — more than one per round means a loop crept back in |
  | **commands over 90 s** | the seat-level budget; anything over it inside a *seat* is a design smell, not bad luck |
  | **per-seat tokens** | the worker/reviewer/tester totals; a seat that costs more than the round's code is worth is a waiting or re-reading problem |

  Non-blocking, like the Q107 baseline: the numbers are read at close, not enforced.
- **Never** write durable specs here — durable specs belong in
  `documents/specs/`.
- The workflow and this policy are defined in
  `.pi/skills/orchestrator-worker-reviewer/SKILL.md` (§ "Round docs") and in
  the repo-root `AGENTS.md` (docs-layout table).