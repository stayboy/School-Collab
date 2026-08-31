# Round-docs quarantine (`documents/rounds/`)

## Finding

- The four-agent `orchestrator-worker-reviewer` workflow (see
  `.pi/skills/orchestrator-worker-reviewer/SKILL.md`) produces per-round
  working docs (`plan-*`, `review-*`, `acceptance-*`, `ui-tester-*`,
  `implementation-review-*`). They accumulated in `documents/specs/`
  (e.g. PR #198 "add period-findings-fix round docs"), polluting the
  durable-specs folder with ephemeral residue.
- 2026-08-30: a parallel process (the `drop-periodtype` round) again wrote
  four round docs into `documents/specs/` — the residue pattern recurs any
  time an agent runs without the current instructions loaded.

## Decision

- Create a quarantine folder, `documents/rounds/`: all round docs MUST live
  there, never in `documents/specs/`. The folder is safe to bulk-trash once
  a round's durable outcomes are folded into the feature spec.
- Codified in: `.pi/skills/orchestrator-worker-reviewer/SKILL.md`
  (§ "Round docs location"), repo-root `AGENTS.md` (docs-layout table), and
  the `.github/copilot-instructions.md` shim (quick pointers).
- Suffix-named files (e.g. `grade-detail-modern-ui-plan.md`) do not match the
  round-doc prefix convention and stay in `documents/specs/` as durable
  planning specs unless reclassified.

## Implementation (2026-08-30 re-run)

The earlier quarantine state did not survive in the working tree (no
tracked files under `documents/rounds/`; the folder was missing; old round
docs found back in `documents/specs/`; the original README unrecoverable
from git objects or compaction snapshots). Re-ran the migration:

1. Recreated `documents/rounds/` and moved the 4 new
   `*-drop-periodtype.md` round docs out of `documents/specs/`.
2. `git mv`-ed the 25 remaining round-doc-prefixed files from
   `documents/specs/` to `documents/rounds/` (staged as renames).
3. Reconstructed `documents/rounds/README.md` from the convention as
   codified in the SKILL.md and `AGENTS.md`.

## Verification

- `documents/specs/` contains no files matching
  `^(plan-|review-|acceptance-|ui-tester-|implementation-review)` after the move.
- `documents/rounds/` holds the 25 migrated docs + 4 `drop-periodtype` docs + README.
- The `documents/runbooks/fr58-fail-open-behavior.md` reference to
  `documents/rounds/review-phases-completed.md` resolves again.