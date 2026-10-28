# Issue tracker: GitHub

Issues and specs for this repo live as GitHub issues in `stayboy/School-Collab`. Use the `gh` CLI
for all operations — it infers the repo from `git remote -v` when run inside a clone.

## Conventions

- **Create an issue**: `gh issue create --title "..." --body "..."` (heredoc for multi-line bodies).
- **Read an issue**: `gh issue view <number> --json number,title,body,labels,comments`.
- **List issues**: `gh issue list --state open --json number,title,body,labels,comments`, with the
  usual `--label` / `--state` filters.
- **Make an issue a sub-issue of a parent**: `gh issue create --parent <parent> ...`, or
  `gh issue edit <parent> --add-sub-issue <child>` (`gh` 2.94+). Older `gh`: put `Part of #<parent>`
  at the top of the child body.
- **Comment on an issue**: `gh issue comment <number> --body "..."`.
- **Apply / remove labels**: `gh issue edit <number> --add-label "..."` / `--remove-label "..."`.
- **Close**: `gh issue close <number> --comment "..."`.

## Closing an issue when the work lands

**How a ticket closes in this repo is settled elsewhere — follow it, do not re-derive it.** Acceptance
closes a round's tickets; the rule and its detail live in
`documents/solution/spec-and-ticket-skills-in-the-orchestrator.md` §6 (owner 2026-10-21) and
`documents/rounds/README.md`.

What this file adds is the trap, because it costs a wrongly-closed ticket:

- GitHub also honours closing keywords — `close`/`closes`/`closed`, `fix`/`fixes`/`fixed`,
  `resolve`/`resolves`/`resolved` — written as `KEYWORD #<n>` in a **PR description or a commit message**.
  Those fire when the PR merges into the default branch: before acceptance, and whether or not the ticket
  was deferred. Do not use one as the close gesture here.
- A **PR title is never scanned** for a keyword, and the `(#447)` in a squash-merge title is the
  **pull-request** number, not an issue.
- Tests and CI never close issues: a close carries an evidence row and a verdict, and a build outcome is
  neither.

## Repo specifics

- **Durable specs stay in `documents/specs/`.** The tracker issue is the work item; the spec file is
  the source of truth (`documents/README.md`). "Publish to the issue tracker" means: create the issue
  and link the spec path in the body. Never move a spec out of the repo, and never put per-round work
  papers in the tracker — those belong in `documents/rounds/`.
- **Issues are the only tracker writes.** Commits, pushes, PRs and merges are gated by `AGENTS.md`
  ("Main branch merge policy") and each needs its own explicit owner instruction. A triage, publish
  or ticket-fetch operation never implies any of them.
- **Round execution is not tracker-driven here.** A ticket with `ready-for-agent` selects the work;
  the tier (0–3) and attended/unattended modifier are still chosen by the owner in the menu
  `AGENTS.md` defines.

## Pull requests as a triage surface

**PRs as a request surface: no.** _(Set to `yes` if this repo treats external PRs as feature
requests; `/triage` reads this flag.)_

If flipped to `yes`, PRs run through the same labels and states using the `gh pr` equivalents:
`gh pr view <n> --comments`, `gh pr diff <n>`, `gh pr comment`, `gh pr edit --add-label`,
`gh pr close`. GitHub shares one number space across issues and PRs, so a bare `#42` is resolved with
`gh pr view 42` first and `gh issue view 42` as the fallback.

## When a skill says "publish to the issue tracker"

Create a GitHub issue. When the work came from a spec file, link that path in the body (see **Repo
specifics**).

## When a skill says "fetch the relevant ticket"

`gh issue view <number> --json number,title,body,labels,comments`.

## Wayfinding operations

Used by `/wayfinder`. The **map** is a single issue; child issues are the tickets.

- **Map**: one issue labelled `wayfinder:map` holding the Notes / Decisions-so-far / Fog body.
- **Child ticket**: a GitHub sub-issue of the map (or a `Part of #<map>` line plus a task-list entry
  where sub-issues are unavailable), labelled `wayfinder:<type>`
  (`research`/`prototype`/`grilling`/`task`). Once claimed, assigned to the driving dev.
- **Blocking**: native issue dependencies —
  `gh api --method POST repos/stayboy/School-Collab/issues/<child>/dependencies/blocked_by -F issue_id=<blocker-db-id>`,
  where the id is the blocker's numeric **database id** (`gh api repos/stayboy/School-Collab/issues/<n> --jq .id`),
  not `#number`. Fallback: a `Blocked by: #<n>, #<n>` line at the top of the child body.
- **Frontier query**: the map's open children, minus any with an open blocker or an assignee —
  first in map order wins.
- **Claim**: `gh issue edit <n> --add-assignee @me`.
- **Resolve**: `gh issue comment <n>`, `gh issue close <n>`, then append a pointer to the map's
  Decisions-so-far.
