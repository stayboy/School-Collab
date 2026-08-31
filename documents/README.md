# documents/ — documentation tree map

Single map of where every kind of documentation lives and the decision rules
for picking a folder. Rules live **once** here; `AGENTS.md` links to this file
(same pattern as `documents/rounds/README.md`).

## Folder map

| Folder | What belongs there | Lifetime |
|---|---|---|
| `specs/` | Durable feature specs that remain the **source of truth**: FRs, NFRs, scope, behavior contracts; planning specs that define what a feature/system must be | Permanent |
| `solution/` | Durable technical memory (the Finding → Implementation standard): research findings, decision records, implementation steps, review findings, follow-up plans, work tracking | Permanent |
| `rounds/` | **Only** ephemeral per-round docs from the four-agent `orchestrator-worker-reviewer` workflow (`plan-*`, `review-*`, `acceptance-*`, `ui-tester-*`) | Bulk-trashable — rules: `rounds/README.md` |
| `runbooks/` | Operational runbooks: recurring procedures and incident/ops behavior for operating the system | Permanent |
| `ai-prompts/` | Reference archive of AI system-prompt variants (never loaded at runtime) | Permanent — see its `README.md` |

## Decision rules

The core question: **does the doc define what the system must *be*, or record
*why/how* the work happened?**

- Defines requirements / behavior / scope → `specs/`
- Records findings, decisions, rationale, implementation steps, review
  results, follow-up work → `solution/`
- Four-agent workflow working papers → `rounds/` (never `specs/`; nothing
  else goes in `rounds/`)
- Ops procedure / incident behavior → `runbooks/`

### Precedents

| Doc type | Folder | Example |
|---|---|---|
| Feature spec | `specs/` | `specs/periods-landing-grid-beautify.md` |
| Planning spec (defines a future feature) | `specs/` | `specs/grade-detail-modern-ui-plan.md` |
| Decision record (why X was chosen) | `solution/` | `solution/rounds-docs-quarantine.md`, `solution/agents-md-consolidation.md` |
| Findings + implementation steps | `solution/` | `solution/landing-page-wrapper.md` |
| Review findings / follow-up plan / work tracking | `solution/` | `solution/periods-branch-post-push-followups.md` |

**Legacy exception:** `specs/ui-implementation-backlog.md` and
`specs/backend-implementation-backlog.md` predate this convention and remain
in `specs/`. Do **not** add new work-tracking docs to `specs/` — new ones go
to `solution/`. (Reclassifying the two backlogs is a separate decision.)

## Naming conventions

- `specs/`: `<feature-slug>.md`, optionally suffixed `-plan.md` / `-impl.md`.
- `solution/`: `<topic-slug>.md` — descriptive, **no** `plan-`/`review-`/
  `acceptance-`/`ui-tester-` prefixes (those are the `rounds/` convention and
  will misclassify the doc as ephemeral residue). A doc superseded by a newer
  one keeps its history via the `.superseded.md` suffix
  (e.g. `centralized-feature-flags-implementation.superseded.md`) rather than
  deletion.
- `rounds/`: strictly `<kind>-<round-slug>.md`.
- `runbooks/`: `<slug>.md` describing the procedure.

## Anti-patterns

- Writing four-agent round docs anywhere except `rounds/`.
- Putting findings / follow-up / decision docs in `specs/` — they are not the
  source of truth for a feature.
- Putting durable specs in `rounds/` — that folder is bulk-trashable.
- Creating a new top-level folder under `documents/` without updating this
  map and the `AGENTS.md` docs-layout table in the same change.