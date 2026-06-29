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
5. Open a PR targeting `main`.
6. Run the local pre-flight checks:
   - code review
   - `dotnet build`
   - `dotnet test`
5. Wait for GitHub Actions CI to pass on the PR.
6. Merge with a squash merge by default:
   ```bash
   gh pr merge <pr-number> --squash --delete-branch
   ```
7. Switch back to `main` and pull the merged result:
   ```bash
   git checkout main
   git pull origin main
   ```

## Status checks before merge

The `Build & Test` workflow is the required status check for PRs targeting `main`.

- Do not merge a PR while `Build & Test` is still running.
- Do not merge a PR while any required check is failing.
- If CI fails, fix the failure on the branch and wait for a green workflow before merging.

GitHub branch protection or rulesets should enforce this server-side where available:

- Require pull requests before merging.
- Require the `Build & Test` status check to pass.
- Require approvals when review policy is enabled.
- Disallow force pushes.
- Prevent direct pushes to `main`.

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
