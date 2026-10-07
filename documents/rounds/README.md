# documents/rounds — ephemeral per-round agent docs

This folder is the **quarantine area for the working docs produced by the
tiered four-agent workflow** (`orchestrator-worker-reviewer` skill). Each
round produces exactly two files:

- `round-<round-slug>.md` — the single round doc, with sections `## Plan`,
  `## Worker Report`, `## Review`, `## Acceptance`, `## UI Tester`. Fill only
  the tier-appropriate ones: Tiers 1–2 fill Plan + Worker Report + Acceptance
  (Tier 2 adds Review); Tier 3 fills them all (+ UI Tester for UI rounds).
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
- **Never** write durable specs here — durable specs belong in
  `documents/specs/`.
- The workflow and this policy are defined in
  `.pi/skills/orchestrator-worker-reviewer/SKILL.md` (§ "Round docs") and in
  the repo-root `AGENTS.md` (docs-layout table).