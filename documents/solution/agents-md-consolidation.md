# AGENTS.md Consolidation — Single Point of Entry

## Findings

The repo had accumulated multiple agent-entry instruction files, each
targeting a different tool: `.github/copilot-instructions.md` (Copilot),
`CLINE.md` (Cline memory conventions), and root `AGENTS.md` (created for pi).
Rules were duplicated or split across them, inviting drift — the same failure
mode that caused the `documents/specs/` round-doc residue.

Research (2026-08-30):

- **AGENTS.md is the emerging industry standard.** An open format stewarded
  by the Agentic AI Foundation under the Linux Foundation (agents.md): "a
  README for agents," adopted by 60k+ open-source projects and natively
  supported by OpenAI Codex, Google Jules, Cursor, Amp, Gemini CLI, VS Code,
  GitHub Copilot coding agent, Windsurf, Devin, Zed, and ~20 more tools.
  Guidance: one `AGENTS.md` at the repo root; monorepos may nest
  per-package files ("nearest file wins"); conflicts resolve to the closest
  file, and explicit user prompts override everything.
- **Cline auto-loads `AGENTS.md`.** Cline's Rules documentation lists
  `AGENTS.md` (plus global `~/.agents/AGENTS.md`) as a supported rule type
  alongside `.clinerules/`, `.cursorrules`, and `.windsurfrules` —
  "standard format for cross-tool compatibility." Cline needs no duplicated
  rules file.
- **pi auto-loads `AGENTS.md`** (and `CLAUDE.md`) at session start — its
  `-nc/--no-context-files` flag exists specifically to disable that discovery.
- **Copilot surfaces still auto-load `.github/copilot-instructions.md` by
  name**, so that path must keep existing — but only as a pointer.
- **Symlinks are a poor fit here.** The agents.md FAQ suggests symlinking
  legacy names (`CLAUDE.md → AGENTS.md`), but git symlinks on Windows require
  developer mode / `core.symlinks` handling and break for teammates without
  that setup. Text shims are version-control-safe. `CLAUDE.md` was therefore
  deliberately not created — pi and Claude Code read `AGENTS.md` directly.

## Decision

Adopt root `AGENTS.md` as the single point of entry — the full repository-wide
manifest, absorbing the content of `.github/copilot-instructions.md`. Every
other agent-entry file becomes a thin compatibility shim or a Cline-specific
doc:

| File | Role after consolidation |
|---|---|
| `AGENTS.md` | **Single point of entry.** All repo-wide rules: communication style, skill discovery, specialty-rule table, tenancy, docs standards, build verification, CPM, architecture reminders, pre-flight/PR checks, merge policy. |
| `.github/copilot-instructions.md` | Pointer shim only — kept because Copilot surfaces load that path by name. |
| `CLINE.md` | Cline memory-graph (MCP) conventions + pointer to `AGENTS.md`. No general rules (Cline auto-reads `AGENTS.md`). |
| `.github/copilot/rules/*`, `.github/skills/*`, `.pi/skills/*`, `.github/merge-policy.md`, `documents/rounds/README.md` | Detail layer — unchanged; linked from `AGENTS.md`. |

## Implementation

- Moved every section of `.github/copilot-instructions.md` into `AGENTS.md`
  (Communication style, Skill discovery, Specialty instructions + default C#
  rule + ask-before-starting rule, Tenancy & Operational Standards,
  Documentation & Knowledge Management incl. the docs layout table, Build
  verification, CPM, Target framework, Architecture reminders, Pre-flight
  review & PR creation, Main branch merge policy).
- Folded the "Topic links" section into the specialty table (added the CSS
  isolation anchor row) — it duplicated the table's entries.
- Fixed self-references: "Keep `.github/copilot-instructions.md` for
  repository-wide rules" → "Keep `AGENTS.md` for repository-wide rules".
- Added a "Cline memory graph" section pointing to `CLINE.md`.
- `.github/copilot-instructions.md` rewritten as a pointer shim.
- `CLINE.md` deduplicated: removed the per-round docs bullet and the C#
  best-practices bullet (both live in `AGENTS.md`, which Cline auto-reads);
  added a pointer header.
- Cross-references updated: `.github/copilot/rules/README.md` (global
  manifest is now `AGENTS.md`), `.github/copilot/rules/configuration-documentation.md`
  (pre-flight section link now targets `AGENTS.md`).
- Appended a superseding update to
  `documents/solution/copilot-instructions-split.md`.

## Verification

- Re-read `AGENTS.md` end-to-end after assembly; section order and content
  match the former manifest plus the new header and Cline memory-graph note.
- `git grep -n "copilot-instructions"` returns only the shim itself,
  historical solution docs, and ephemeral `documents/rounds/` notes (that
  folder is bulk-trashable by design).
- No rule text exists in more than one place: `CLINE.md` and the shim contain
  pointers only.