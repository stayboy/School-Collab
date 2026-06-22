# Copilot Instructions — SchoolCollab

These instructions apply to every file in this repository.

---

## Skill discovery (read first)

When you need a skill — for code review, PR description, testing, deployment,
documentation, design review, etc. — **always start at one of these two
canonical catalogs** before any other source:

1. **[https://awesome-copilot.github.com/skills/](https://awesome-copilot.github.com/skills/)**
   — community-curated Copilot skills (Skill `name`/description metadata, with a
   machine-readable `llms.txt` at
   [https://awesome-copilot.github.com/llms.txt](https://awesome-copilot.github.com/llms.txt)).
   Skills live at
   `https://raw.githubusercontent.com/github/awesome-copilot/main/skills/<skill-name>/SKILL.md`.
2. **[https://github.com/microsoft/skills](https://github.com/microsoft/skills)**
   — Microsoft-authored skills, MCP servers, and tools. Use this catalog when
   looking for first-party Microsoft patterns (Aspire, Azure SDKs, .NET,
   TypeScript/JS, etc.) or for the official Microsoft MCP / tool
   implementations that ship alongside a service.

Workflow:

- Pick the catalog that best matches the source: **awesome-copilot** for
  community/third-party patterns, **microsoft/skills** for first-party
  Microsoft/Microsoft-owned tooling.
- Search the chosen catalog (use `llms.txt` for awesome-copilot when doing bulk
  discovery). For microsoft/skills, browse the repo's `skills/`, `mcp/`, and
  `tools/` directories.
- If a suitable skill exists, use it (or install it via the documented install
  command) before falling back to ad-hoc authoring.
- If the catalog has nothing relevant, say so explicitly, then propose a
  custom approach. Do not silently swap in a different source (e.g.
  `awesome-skills`, `kevintsengtw/*`, etc.) without an explicit user
  request.

---

## Specialty instructions

Read the relevant specialty rule file before changing code in that area. Keep
`.github/copilot-instructions.md` for repository-wide rules and link to topic-specific
guidance instead of duplicating it.

| Area | File |
|---|---|
| Blazor components, Fluent UI, and styling | `.github/copilot/rules/blazor-components.md` |
| Entity Framework Core migrations | `.github/copilot/rules/ef-migrations.md` |
| Logging and Aspire observability | `.github/copilot/rules/logging-aspire.md` |
| Testing (MTP Standard) | `.github/copilot/rules/testing.md` |
| Fluent UI icons | `.github/skills/fluentui-icons/SKILL.md` |
| Fluent UI component props | `.github/skills/fluentui-component-props/SKILL.md` |
| Bounded context creation | `.github/skills/bounded-context/SKILL.md` |
| Coded values domain | `.github/skills/coded-values/SKILL.md` |
| Azure AI OpenAI .NET | `.github/skills/azure-ai-openai-dotnet/SKILL.md` |

## Tenancy & Operational Standards

### Tenancy Patterns
The repository follows two distinct tenancy patterns based on the data type:

1. **Override Pattern (Reference Data)**: 
   Used for system-wide blueprints (e.g., Coded Values). Implements a `Global Value` $\rightarrow$ `Tenant Override` $\rightarrow$ `Resolved Value` flow.
   - **Pattern Guide**: See `.skills/tenancy-override-pattern/SKILL.md`.
   - **Key Component**: `CodedValueResolver` logic.

2. **Direct Tenancy (Operational Data)**: 
   Used for tenant-created entities (e.g., Students, Assignments). Entities inherit from `BaseTenantEntity` and are filtered directly by `TenantId`.
   - **Restriction**: Do not use the override pattern for operational data. Use a Permissions/ACL system for specialized access.

### Implementation Rule
Any new auditable entity or feature requiring tenancy **must** follow the patterns defined in `SchoolCollab.Core/Tenancy` and the corresponding skill documentation. Always verify implementations with:
- `dotnet build` (zero errors).
- Unit tests covering the merge/resolution logic.
- Tenant-isolated cache keys.

## Documentation & Knowledge Management

All research, architectural decisions, and implementation steps must be documented in `documents/solution/`.

### The "Finding $\rightarrow$ Implementation" Standard
Whenever a new feature or architectural change is requested:
1. **Research Phase**: Document "Findings" (the research, alternatives analyzed, and the chosen "why") in a `.md` file.
2. **Implementation Phase**: Document "Implementation Steps" (the *how*, the code changes, and the verification results) in the same or a related file.
3. **Outcome**: The `documents/solution/` folder must act as the project's technical memory, allowing any developer to trace *why* a decision was made before seeing *how* it was executed.

---

## Topic links

- **Blazor components, Fluent UI, and styling:** `.github/copilot/rules/blazor-components.md`
- **Entity Framework Core migrations:** `.github/copilot/rules/ef-migrations.md`
- **CSS isolation and styling:** `.github/copilot/rules/blazor-components.md#blazor-css-isolation-and-styling`
- **Testing:** `.github/copilot/rules/testing.md`

## Central Package Management (CPM)

All NuGet package versions are managed centrally in **`Directory.Packages.props`** at the
repository root. This prevents version drift across the 10-project solution.

### How it works

`Directory.Build.props` sets `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>`.
All `<PackageReference>` elements in `.csproj` files **must not** include a `Version`
attribute — the version is resolved from `Directory.Packages.props`.

### Adding a new package

1. Add a `<PackageVersion>` entry in `Directory.Packages.props` under the appropriate
   label group:
   ```xml
   <PackageVersion Include="My.New.Package" Version="1.2.3" />
   ```

2. Add `<PackageReference Include="My.New.Package" />` (no `Version`) in the target
   `.csproj`.

3. **Never** add `Version="..."` directly to a `<PackageReference>` — CPM will raise
   NU1008 / NU1009 errors at build time if you do.

### Updating a package version

Change the version only in `Directory.Packages.props`. The update applies to every
project that references it automatically.

### Exceptions

- `PrivateAssets="all"` (and other metadata attributes like `IncludeAssets`, `ExcludeAssets`)
  **stay** in the `<PackageReference>` element inside the `.csproj` — they are not version
  metadata and are not moved to `Directory.Packages.props`.
  ```xml
  <!-- csproj -->
  <PackageReference Include="Microsoft.EntityFrameworkCore.Design" PrivateAssets="all" />
  ```

- `<Sdk Name="..." Version="..." />` at the top of a `.csproj` (e.g. `Aspire.AppHost.Sdk`)
  is an **MSBuild SDK reference**, not a NuGet package — CPM does not manage it, leave the
  `Version` attribute in place.

---

## Target framework

All projects target **net10.0**. Do not downgrade to net9.0 or earlier.

## Architecture reminders

- No direct project references between bounded contexts — use MassTransit contracts.
- No MediatR — CQRS is implemented via `ICommandHandler<T>` / `IQueryHandler<T,R>` with
  Scrutor assembly scanning.
- Domain entities use PostgreSQL `xmin` (row version) for optimistic concurrency.
- **API Endpoint Grouping**: All API endpoints must be grouped using an extension method (e.g., `MapStudentEndpoints(this WebApplication app, IFeatureFlagService featureFlags)`). Do not define routes inline in `Program.cs`. Authorization requirements on these groups should be conditional based on `IFeatureFlagService` (e.g., `FEATURE:DisableOIDCAuth`).

## Pre-flight review & PR creation

Before creating **any** pull request, the following pre-flight checks must pass:

### 1. Run pre-flight code review

Execute a code-review pass on the branch changes **before** pushing or creating a PR.
This catches issues early and avoids back-and-forth on the PR.

```
# Use the code-review skill or agent to review staged/unstaged changes
# Focus on: bugs, security vulnerabilities, logic errors, missing tests
```

### 2. Verify tests exist for the feature

- Every new feature, service, or behavioural class added on the branch **must** have
  corresponding unit tests (see `.github/copilot/rules/testing.md`).
- If the PR introduces new behavioural code without tests, **do not create the PR** —
  write the tests first.

### 3. Run tests and confirm they pass

```bash
dotnet test
```

- `dotnet test` must complete with **0 failures** before the PR is created.
- If any test fails, fix the issue in the same branch before proceeding.

### 4. Build must succeed

```bash
dotnet build
```

- Zero errors. Warnings are acceptable but should be reviewed.

### Checklist (before `gh pr create`)

| Check | Command | Must be |
|-------|---------|---------|
| Code review | Review branch changes for bugs/security/logic | No issues found |
| Tests exist | New behavioural code has corresponding test files | Yes |
| Tests pass | `dotnet test` | 0 failures |
| Build succeeds | `dotnet build` | 0 errors |

**Do not skip these checks.** If any check fails, fix the issue on the branch before
creating the PR.

---

## Main branch merge policy

`main` is a protected delivery branch in process. Do not push or merge directly to
`main` from ad-hoc work.

Required workflow:

1. Create a feature or fix branch from `main`.
2. Add/update tests for behavioural changes.
3. Commit locally on the branch.
4. Push only after the user explicitly instructs to push:
   ```bash
   SCHOOLCOLLAB_ALLOW_PUSH=1 git push -u origin <branch-name>
   ```
5. Open a PR targeting `main`.
6. Wait for the GitHub Actions `Build & Test` check to pass.
7. Merge with squash merge by default:
   ```bash
   gh pr merge <pr-number> --squash --delete-branch
   ```
6. Switch to `main` and pull the merged result:
   ```bash
   git checkout main
   git pull origin main
   ```

Do not merge a PR while required status checks are still running or failing. If
CI fails, fix the branch and wait for a green workflow before merging.

The repository includes `.github/merge-policy.md` for the full policy and
`.githooks/pre-push` as a local convenience guard that holds all pushes until
the user explicitly allows them and still blocks direct pushes to `main`. GitHub
branch protection or rulesets should be enabled in repository settings where
available to enforce the same rule server-side.
