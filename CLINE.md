# CLINE.md — Cline memory-graph conventions for the School-Collab repo

> **All repository-wide agent rules live in `AGENTS.md` (repo root)** — the
> AGENTS.md open standard, which Cline auto-loads as a rule file (visible in
> the Rules panel). This file covers only Cline-specific memory-graph usage
> and deliberately contains no general repo rules, so nothing is duplicated
> between the two files.

This repo is wired with a **Memory MCP server** (`@modelcontextprotocol/server-memory`)
that maintains a persistent local knowledge graph at `.cline/memory.jsonl`. Use it to
recall and record structured facts about this codebase across sessions.

## When you start a task

1. **Recall relevant context.** Call the `search_nodes` MCP tool with a **single keyword**
   from the task (e.g. `search_nodes "Students"`, `search_nodes "outbox"`, `search_nodes "CQRS"`).
   `search_nodes` does whole-substring matching against entity names, types, and observations,
   so use one keyword at a time — a multi-word phrase only matches if it appears verbatim.
   This returns matching entities + their relations from the graph.
2. If a question is about architecture, file relationships, or how pieces connect, also
   consider `open_nodes` for specific named entities, or `read_graph` to see the whole
   (small) graph.

## While you work

3. **Record durable, reusable facts.** When you learn something that will help future
   sessions — a new architecture decision, a convention, a gotcha, a new bounded context
   or aggregate root — call:
   - `create_entities` for new named things (bounded contexts, patterns, services,
     aggregate roots, endpoints).
   - `create_relations` to connect them (`Students` → `uses` → `TransactionalOutbox`).
   - `add_observations` to attach atomic facts to an existing entity
     (one fact per observation string).
4. Use **active voice** for relation types (`depends_on`, `uses`, `communicates_with`,
   `contains`, `orchestrates`).
5. Keep observations **atomic** — one fact each — so they can be removed independently
   later via `delete_observations` if they go stale.

## What to record vs. not

- ✅ Record: bounded contexts and their project layout, aggregate roots, cross-context
  integration events, architecture patterns in use, non-obvious conventions, build/test
  commands, feature flags, branch/PR-level decisions that become permanent.
- ❌ Don't record: ephemeral task progress, transient debugging state, anything already
  obvious from a single file's contents.

## Notes

- The graph file (`.cline/memory.jsonl`) is git-ignored — it is per-machine state.
- A separate, larger graphify graph exists at `graphify-out/graph.json` (9957 nodes),
  built by the `/graphify` skill. The Memory MCP graph is a small, hand-curated
  companion for cross-session recall, not a full code extraction.
- Repo-local skills in `.github/skills/` (`bounded-context`, `dotnet-best-practices`,
  `coded-values`, `dialog-ui`, `fluentui-component-props`, `fluentui-icons`) encode the
  authoritative conventions; the memory graph is a quick-recall index, not a replacement.

