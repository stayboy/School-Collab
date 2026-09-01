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
- Round docs and patches are **ephemeral residue**: once a round's durable
  outcomes are folded into the feature spec in `documents/specs/`, this whole
  folder is **safe to bulk-trash**.
- **Never** write durable specs here — durable specs belong in
  `documents/specs/`.
- The workflow and this policy are defined in
  `.pi/skills/orchestrator-worker-reviewer/SKILL.md` (§ "Round docs") and in
  the repo-root `AGENTS.md` (docs-layout table).