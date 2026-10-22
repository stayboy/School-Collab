# Domain Docs

How the engineering skills consume this repo's domain documentation while exploring the codebase.

## Before exploring, read these

- **`GLOSSARY.md`** at the repo root — the project vocabulary (Tier, Seat, Round, Unattended, …). This
  repo has no `GLOSSARY-MAP.md`: it is a **single-context** repo.
- **ADRs**: `documents/solution/adr-*.md` — currently `adr-authoring-surface-pattern.md`,
  `adr-cross-module-calls.md`, `adr-execution-tiers.md`. Read the ones that touch the area you are
  about to work in. **This repo has no `docs/adr/`**; do not create one, and do not add a
  per-context `src/<context>/docs/adr/`.
- **`documents/README.md`** — the doc-tree map and the decision rules for which folder a new doc
  belongs in (`specs/` = what the system must be, `solution/` = why/how the work happened,
  `rounds/` = ephemeral per-round papers, `runbooks/` = ops behaviour).
- **`AGENTS.md`** — repo-wide agent rules. It wins over any skill when they conflict.

If `GLOSSARY.md` or an ADR you need is missing, proceed silently; don't flag the absence and don't
propose creating one upfront. `/domain-modeling` (via `/grill-with-docs`) creates them lazily when a
term or decision actually gets resolved.

## Layout

```
/
├── AGENTS.md                     ← repo-wide agent rules (single source of truth)
├── GLOSSARY.md                   ← single-context vocabulary
├── documents/
│   ├── README.md                 ← the doc-tree map + folder decision rules
│   ├── specs/                    ← durable specs, source of truth
│   ├── solution/                 ← ADRs + findings/implementation memory
│   ├── rounds/                   ← ephemeral per-round docs (trashable)
│   ├── runbooks/                 ← ops procedures
│   └── ai-prompts/               ← reference prompt archive
├── docs/
│   ├── agents/                   ← this setup (issue tracker, triage labels, domain docs)
│   ├── patterns/                 ← legacy, superseded by documents/solution/
│   └── plans/                    ← legacy, superseded by documents/specs/
└── src/                          ← bounded contexts
```

`docs/patterns/` and `docs/plans/` are legacy history: read them for prior art, never write new docs
there.

## Use the glossary's vocabulary

When output names a domain concept — an issue title, a ticket, a refactor proposal, a test name — use
the term as `GLOSSARY.md` (and the area's spec, for feature nouns) defines it. Don't drift to
synonyms the glossary avoids.

If the concept you need is missing, that is a signal: either you are inventing language the project
does not use, or there is a real gap to note for `/domain-modeling`.

## Flag ADR conflicts

If your output contradicts an existing ADR, surface it explicitly rather than silently overriding it:

> _Contradicts `documents/solution/adr-cross-module-calls.md`, but worth reopening because…_

Repo rule: when a conflict is real, the losing file is amended **in the same change** — never
silently overridden.
