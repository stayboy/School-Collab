# Preflight PR — Copilot Prompt

A single-command Copilot prompt that runs the full pre-flight pull request workflow: review changes, fix issues, write tests, and merge.

## Quick Start

```bash
copilot --prompt .github/copilot/preflight-pr.prompt.md
```

That's it. Copilot will walk through every step automatically.

## What It Does

| Step | Description |
|------|-------------|
| 1. Check working tree | Commit any uncommitted changes to a new feature branch |
| 2. Push branch | Push the feature branch to origin |
| 3. Build & test | Run `dotnet build` and unit tests locally, fix failures |
| 4. Code review | Use the code-review sub-agent to find bugs, security issues, and logic errors |
| 5. Write missing tests | Add unit/integration tests for uncovered domain logic and Blazor patterns |
| 6. EF migration guard | Check for pending model changes, validate snapshot sync |
| 7. Create PR | Generate title and description from the changes, create via `gh` |
| 8. Wait for CI | Poll until all checks pass or one fails |
| 9. Merge | Squash-merge by default, then pull `main` |
| 10. Cleanup | Delete the feature branch, report final status |

## Options

Pass these as natural language when starting the prompt:

| Flag | Meaning | Example |
|------|---------|---------|
| `--skip-tests` | Skip local build & test step | *"skip the tests"* |
| `--skip-review` | Skip code review step | *"skip the review"* |
| `--skip-merge` | Stop after CI passes, don't merge | *"don't merge"* |
| `--merge-strategy` | `squash` (default), `merge`, or `rebase` | *"use rebase merge"* |
| `--base` | Target branch (default: `main`) | *"target the release branch"* |

### Examples

```bash
# Full workflow (default)
copilot --prompt .github/copilot/preflight-pr.prompt.md

# Then tell Copilot: "skip the tests and use rebase merge"
```

## Why a Prompt Instead of a Script?

| Capability | Shell Script | Copilot Prompt |
|------------|-------------|----------------|
| Fix review issues | ❌ | ✅ |
| Write missing tests | ❌ | ✅ |
| Understand project conventions | ❌ | ✅ |
| Handle EF migration guard | ❌ | ✅ |
| Merge & cleanup | ✅ | ✅ |

The prompt encodes project-specific rules that shell scripts can't enforce:

- **Serilog via Aspire OTLP** — no `Console.WriteLine`, no `AddConsole()`
- **Central Package Management** — no `Version` on `<PackageReference>`
- **Blazor IDisposable/CancellationToken** pattern on all pages
- **Optimistic UI** mutations with rollback for single-row changes
- **Migration guard test** must mirror `UseSnakeCaseNamingConvention()`

## Companion Scripts

For CI environments or non-interactive use, shell scripts are also available:

| Script | Platform |
|--------|----------|
| `scripts/preflight-pr.sh` | Bash (Linux, macOS, Git Bash) |
| `scripts/preflight-pr.ps1` | PowerShell (Windows) |

These scripts handle the mechanical steps (branch, push, PR, CI, merge) but **cannot** review code, fix bugs, or write tests. Use the Copilot prompt for the full workflow.

## Project Conventions

This prompt assumes the conventions documented in the repository's Copilot instructions (`.github/copilot/instructions.md` or inline custom instructions). Key rules:

- All logging through `ILogger<T>` — never `Console.WriteLine`
- `builder.AddServiceDefaults()` must be the first call in `Program.cs`
- Structured logging with named properties, not string interpolation
- Blazor pages use `@implements IDisposable` with `CancellationTokenSource`
- `@key` on all `@foreach` loops
- `<FluentProgressRing />` for loading states, not plain text
- `<ErrorBoundary>` wrapping page content
- One EF migration per PR, never modify existing migrations
- `Down()` must reverse `Up()` in every migration