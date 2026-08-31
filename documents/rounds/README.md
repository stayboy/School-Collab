# documents/rounds — ephemeral per-round agent docs

This folder is the **quarantine area for the working docs produced by the
four-agent workflow** (`orchestrator-worker-reviewer` skill): the
orchestrator's `plan-*.md` and `acceptance-*.md`, the reviewer's
`review-*.md` (and `implementation-review-*.md` follow-ups), and the UI
tester's `ui-tester-*.md`.

It exists so that `documents/specs/` stays reserved for **durable feature
specs that remain the source of truth**.

## Rules

- Name each doc `<kind>-<round-slug>.md` (e.g. `plan-period-followups-r1.md`).
- Round docs are **ephemeral residue**: once a round's durable outcomes are
  folded into the feature spec in `documents/specs/`, this whole folder is
  **safe to bulk-trash**.
- **Never** write durable specs here — durable specs belong in
  `documents/specs/`.
- The workflow and this policy are defined in
  `.pi/skills/orchestrator-worker-reviewer/SKILL.md` (§ "Round docs location")
  and in the repo-root `AGENTS.md` (docs-layout table).