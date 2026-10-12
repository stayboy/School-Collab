# Merge policy

This repository uses pull requests as the only supported path for changes to `main`.

## Feature flags

Feature flags are centralized in the AppHost's `Parameters:` block
(see `src/AppHost/SchoolCollab.AppHost/appsettings.json`) and fanned out to
consumers via `WithEnvironment("FeatureFlags__FEATURE__...", param)`. Do not
duplicate flag values across service-level `appsettings.json` files, and do
not reintroduce the deprecated `SchoolCollab.Config` HTTP overlay
(`AddRemoteFeatureFlags`). See `documents/configuration.md` §2 and §5.

## Required merge path

1. Create a feature/fix branch from `main`.
2. Add or update tests for behavioural changes.
3. Commit locally on the branch only after the user explicitly says
   "commit" (see Local commit hold below).
4. Push only after the user explicitly instructs to push:
   ```bash
   SCHOOLCOLLAB_ALLOW_PUSH=1 git push -u origin <branch-name>
   ```
5. Open a PR targeting `main`. If a previous related PR is already open (same
   area, arc, or feature train), deliver the new work as a `gh stack` layer on top
   of it rather than an independent PR (see the `gh-stack-pr-train` skill: a
   registered layer runs the stack root's CI checks, and the train reviews as
   one ordered sequence). With no related PR open, a plain PR from `main` is
   correct.
6. Run the local pre-flight checks:
   - code review
   - `dotnet build`
   - `dotnet test`
7. Wait for GitHub Actions CI to pass on the PR.
8. Merge with a squash merge by default:
   ```bash
   gh pr merge <pr-number> --squash --delete-branch
   ```
9. Switch back to `main` and pull the merged result:
   ```bash
   git checkout main
   git pull origin main
   ```

## Status checks before merge

The `Build & Test` workflow is the required status check for PRs targeting `main`, and it is **enforced
server-side** since 2026-10-10: ruleset `24835514` — *main - require Build & Test, branches up to date* —
requires the check with `strict_required_status_checks_policy` (so a branch must be up to date before it
can merge), blocks force pushes and branch deletion, and carries **no bypass actors**. Before that date
the check was a convention only. The ruleset's provenance, and the approximations it stands in for a
merge queue this repository cannot have, are in `documents/runbooks/merge-queue-and-red-main.md`.

- Do not merge a PR while `Build & Test` is still running.
- Do not merge a PR while any required check is failing.
- If CI fails, fix the failure on the branch and wait for a green workflow before merging.

Enforce this server-side (the runbook is the checklist):

- Require pull requests before merging.
- Require the `Build & Test` status check to pass — and **require branches to be up to date before
  merging**, so the checks run against the `main` the PR will actually land on. That is the closest
  available substitute for a merge queue, which this repository cannot have while it is user-owned.
- Require approvals when review policy is enabled.
- Disallow force pushes.
- Prevent direct pushes to `main`.

## After the merge: `main`'s run is a health signal, not a gate

The merge has already happened — a post-merge failure cannot un-merge it. Read `main`'s run **once**, at a
decision point: before starting new work off `main`, or when a failure notification arrives. Never poll it
as a gate, and never as a blocking wait loop.

A red `main` is an **incident**:

- **Failure in the code just merged** → fix forward on a **new branch, through a PR**. Never push to
  `main` directly, not even to repair it.
- **Reverting a merged PR is a manual, owner-agreed action** — it is never taken unilaterally, no matter
  how red the trunk is.
- **Failure in another session's job** (for example the `Mobile (MAUI)` job the hybrid-mobile train added)
  → report it and leave it; that train owns it.

## Merge strategy

Use squash merge by default for feature and bug-fix PRs. This keeps `main` focused on shipped changes while preserving detailed history on the source branch.

Use merge or rebase only when the user explicitly requests it or when the PR requires preserving a multi-commit history.

## Local push hold

The local `pre-push` hook in `.githooks/pre-push` holds all local pushes by default until the user explicitly allows the push.

This prevents agents or automation from pushing commits without an explicit user instruction.

To allow one push after the user says to push, run:

```bash
SCHOOLCOLLAB_ALLOW_PUSH=1 git push <remote> <branch>
```

PowerShell example:

```powershell
$env:SCHOOLCOLLAB_ALLOW_PUSH='1'
git push <remote> <branch>
Remove-Item Env:SCHOOLCOLLAB_ALLOW_PUSH
```

The hook still blocks direct pushes to `main` even after the explicit push allow flag is set.

## Local commit hold

Agents and automation must **not** commit staged or unstaged changes to a branch, and must **not** push uncommitted changes to `origin`, without an explicit user instruction such as "commit" or "push".

Keep working-tree changes local and uncommitted by default. When the user asks to commit, create the commit and then stop; do not push unless the user explicitly asks to push. When the user asks to push, use `SCHOOLCOLLAB_ALLOW_PUSH=1` and follow the required PR workflow above.
