# Merge policy

This repository uses pull requests as the only supported path for changes to `main`.

## Required merge path

1. Create a feature/fix branch from `main`.
2. Add or update tests for behavioural changes.
3. Push the branch and open a PR targeting `main`.
4. Run the local pre-flight checks:
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

## Direct pushes to `main`

Direct pushes to `main` are not part of the normal workflow.

The repository includes a tracked `pre-push` hook in `.githooks/pre-push` that blocks local pushes to `main`. To use it locally:

```bash
git config core.hooksPath .githooks
```

GitHub branch protection or rulesets should be configured in repository settings to enforce the same rule server-side.
