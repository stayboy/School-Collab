# Preflight PR

Run the full pre-flight pull request workflow: review changes, fix issues, create tests, and merge.

## Steps

Follow these steps **in order**. Do not skip a step unless the user explicitly asks to skip it.

### 1. Check working tree

- Run `git status` to see if there are uncommitted changes.
- If there are uncommitted changes:
  - Ask the user for a descriptive branch name, or suggest one based on the changes.
  - Create a feature branch: `git checkout -b <branch-name>`
  - Stage and commit all changes with a meaningful commit message.

### 2. Push branch

- Do **not** push automatically.
- Stop after committing and ask for explicit user instruction to push.
- When the user explicitly instructs to push, use:
  ```bash
  SCHOOLCOLLAB_ALLOW_PUSH=1 git push -u origin <branch-name>
  ```
- If the branch already exists remotely, push new commits only after the user explicitly instructs to push.

### 3. Build & test locally

- Run `dotnet restore`, then `dotnet build --no-restore --configuration Release`.
- Run unit tests: `dotnet test --no-build --configuration Release --filter "FullyQualifiedName~Tests.Unit"`.
- If the build or tests fail, **stop and fix the errors** before continuing. Re-run tests after each fix until they pass.

### 4. Run code review

- Use the code-review sub-agent to review the current changes (staged or on the branch vs `main`).
- Focus on bugs, logic errors, security vulnerabilities, and missing patterns — **not** style or formatting.
- If issues are found:
  - Fix each issue with surgical edits.
  - Re-run tests to confirm the fix doesn't break anything.
  - Commit and push the fixes only after the user explicitly instructs to push:
    ```bash
    SCHOOLCOLLAB_ALLOW_PUSH=1 git push
    ```

### 5. Check for missing tests

- After reviewing, assess whether the changes need additional tests:
  - Domain logic, command/query handlers, and API endpoints should have unit tests.
  - Blazor page lifecycle patterns (IDisposable, CancellationToken, optimistic UI) should have Playwright integration tests.
- If tests are missing, write them. Run them to confirm they pass. Commit, then ask for explicit user instruction before pushing.

### 6. EF migration guard

- If any `IEntityTypeConfiguration`, `DbContext`, or domain entity was modified:
  - Run `dotnet ef migrations add DiagnosePendingChanges --project <core-project> --context <DbContext>` to check for pending model changes.
  - If the migration is empty (no Up/Down), remove it: `dotnet ef migrations remove --project <core-project> --context <DbContext>`.
  - If it produces a real migration, review it — make sure `Down()` reverses `Up()` — then keep it and commit.
  - Run the `NoUncommittedModelChanges` unit test to confirm the snapshot is in sync.

### 7. Create or update PR

- Check if a PR already exists for this branch: `gh pr list --head <branch-name>`.
- If a PR exists, note its number.
- If not, create one:
  - Generate a title from the branch name (strip `feature/` prefix, replace hyphens with spaces, title-case).
  - Write a detailed description summarizing the changes, referencing the review and any fixes applied.
  - Run: `gh pr create --base main --title "<title>" --body "<description>"`

### 8. Wait for CI

- Run `gh pr checks <pr-number>` to see CI status.
- Poll every 30 seconds until all checks pass or one fails.
- If CI fails, read the logs, fix the issue, commit, then ask for explicit user instruction before pushing. Wait again after the user instructs to push.

### 9. Merge

- Once CI is green and all issues are resolved, merge the PR through the PR workflow:
  - Default strategy: squash merge (`gh pr merge <pr-number> --squash --delete-branch`).
  - Do not push or merge directly to `main`; follow `.github/merge-policy.md`.
  - If the user prefers a different strategy, use `--merge` or `--rebase` instead.
- After merging, switch to `main` and pull: `git checkout main && git pull origin main`.

### 10. Cleanup

- Delete the local feature branch if it was merged and deleted remotely.
- Report the final status: PR number, merge commit, and any remaining items.

## Options

The user may pass these as natural language modifiers:

| Flag | Meaning |
|---|---|
| `--skip-tests` | Skip local build & test step |
| `--skip-review` | Skip code review step |
| `--skip-merge` | Stop after CI passes, don't merge |
| `--merge-strategy <squash\|merge\|rebase>` | Merge strategy (default: squash) |
| `--base <branch>` | Target branch (default: main) |

## Notes

- This project uses **Central Package Management** — all NuGet versions are in `Directory.Packages.props`. Never add `Version` to `<PackageReference>`.
- This project uses **Serilog via Aspire's OTLP pipeline** — never add `builder.Logging.AddConsole()` or `Console.WriteLine`.
- Blazor pages must use `@implements IDisposable` with `CancellationTokenSource` and `_disposed` guard pattern on all async API calls.
- EF Core migrations must never be modified after creation — always add a new migration.
- The migration guard test must mirror the design-time factory configuration (including `UseSnakeCaseNamingConvention()`).