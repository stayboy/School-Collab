# AGENTS.md — single point of entry for AI coding agents

This repository follows the [AGENTS.md open standard](https://agents.md): all
repository-wide agent instructions live here, **once**. Tool-specific entry
files are compatibility shims, not copies:

- `CLINE.md` — Cline's memory-graph (MCP) conventions plus a pointer here.
- `.github/copilot-instructions.md` — a pointer here, kept only because some
  Copilot surfaces (VS Code Copilot custom instructions, GitHub Copilot
  coding agent) auto-load that path by name. It deliberately contains no
  rules of its own.

pi, Cline, GitHub Copilot coding agent, VS Code Copilot, Cursor, OpenAI
Codex, Gemini CLI, Amp, Windsurf, and other agents auto-discover this file.
If your agent does not, read it manually before starting work.

---

## Communication style

Be concise. Do not over-explain, apologize repeatedly, or include large
unchanged code blocks in responses. For every change:

1. State what you changed and why in one or two sentences.
2. List the affected file paths.
3. Show only the relevant diff snippet or minimal example, not the whole file.
4. Report build/test results in a compact table.

When a user reports a symptom (e.g., "an error pops on config-flags landing
page"), focus on diagnosing and fixing it. Avoid verbose step-by-step
narration of every tool call. If the fix is straightforward, present the
result, not the journey.

Do not emit empty shell commands or placeholder comments as output.

### Posing questions and decisions

When you need a decision from the user — a design choice, a scope call, an
ambiguous requirement — **use the `grill-me` skill** rather than asking ad hoc:

- Map the decision as a **design tree** and ask the whole **frontier** — every
  decision whose prerequisites are already settled — in **one round**.
- Number each question and **always attach a recommended answer** with its
  trade-offs and cost. Never ask a bare "what do you think?" or present options
  without a recommendation.
- **Resolve facts yourself first**: read the repo, run the search, inspect the
  migration. Never ask the user for something the codebase, config, or docs can
  answer — surface what you found, then ask the decision.
- A question whose answer depends on another **still-open** question belongs to a
  **later** round, not this one. Asking it now is a guess wearing a question mark.
- **Never act on the plan** until the frontier is empty and the user has
  confirmed — then summarise the settled decisions and anything explicitly
  deferred.

Applies to plan/spec pressure-testing and to any request that hides several
decisions.

**Always, with no threshold (owner, 2026-10-10).** *Every* question the repo
cannot answer is an open question and gets this treatment — a wording choice, a
layout tweak, a one-line CI gate, a repository setting, a merge-ordering practice,
a "should we enable X" that arrived as a footnote. The test is **decidability, not
size**: if the codebase, config, or docs settle it, resolve it yourself and say
what you found; if they cannot, it goes in a **grill round** — never as prose
questions tacked onto the end of a reply, and never as a recommendation the agent
quietly acts on. "I mentioned it in passing" is not a substitute for a round
(that exact miss happened on 2026-10-10 with repository auto-merge and PR merge
ordering, both raised as afterthoughts). The only exemption is a pure factual
question, where a direct question is cheaper.

**Round format, and how answers come back (owner, 2026-10-12).** A grill round is
**prose, not a structured question prompt** — the owner cancelled a structured
prompt in favour of it. So: map the **design tree**, ask the **whole frontier**
in one message as numbered questions (`❓ **Q1** — **Title**: …` / `➡️ recommendation`),
word each so a plain "yes" accepts the recommended answer, and **number questions
continuously across rounds**. State what each round settled before opening the
next, and recompute the frontier from the answers: a question whose prerequisite
is still open belongs to a **later** round. The owner commonly answers "all as
recommended", which settles the round and pushes the frontier outward — only then
may you act on it. Skill source: `grill-me` (which forwards to `grilling`).


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

**Precedence (owner, 2026-10-21).** On conflict: **this file** wins over a repo-authored
skill (`.github/skills/**`, `.pi/skills/**`), which wins over a vendored skill
(`~/.agents/skills`, `~/.claude/skills`, `~/.pi/agent/skills`). A skill carries
**procedure** and points here for **definitions** — it never restates one. When a conflict
is real, amend the losing file in the same change; never silently override it. A skill does
own the boundaries of its own workflow (which work it refuses, how it runs a round), and
those boundaries stand where this file names that skill.

### Installed skills are read-only — refresh, never patch

Skill folders installed outside this repo (e.g. `~/.agents/skills/` and the
`~/.pi/agent/skills/`, `~/.claude/skills/` entries linked into it) are
**vendored copies of an external source** — provenance and the upstream URL live
in the frontmatter `source:` field (or, for installer-managed skills, in
`~/.agents/.skill-lock.json`, whose entry carries e.g. `source`, `sourceUrl`,
`skillPath`, `skillFolderHash` and `updatedAt`). Never edit them locally: a
local patch drifts
from upstream and silently changes how the skill behaves in later sessions —
exactly how `grill-me` got polluted before its 2026-10-05 refresh from
`mattpocock/skills`.

- **Wrong or stale skill?** Re-fetch from the upstream URL (frontmatter
  `source:`, or the lock entry's `sourceUrl` for installer-managed skills), bump
  `version` / `updated` — do not patch in place.
- **Enforcement (deny-ACL):** a write-locked skill file carries a deny `(W)` —
  which `icacls` renders as *Write, Synchronize*, and since
  almost every open requests Synchronize it refuses **reads as well as writes**
  (verified 2026-10-05: `.NET OpenRead`, `OpenText`, `Get-Content -ErrorAction
  Stop` and `cmd /c type` all fail with *Access denied*, while an undenyed skill
  such as `gh-stack` reads fine). To keep the skill usable, prefer the write-only
  mask `(WD,AD)`, verified to allow `OpenRead`/`OpenText` while still refusing
  `OpenWrite` and `Add-Content`. **Do not re-add a deny on the folder:** the
  folder-level `(W,D,DC)` deny was removed on 2026-10-05 after the skill tooling
  began failing against it (`skill_manage create` reported `EPERM … scandir
  '<…>/<skill-folder>'`) — that is the strongly-indicated cause, though the failing
  `create` was never retried. With it gone, delete-and-recreate is no longer
  blocked; the file deny is the whole enforcement. Nothing was write-locked as of
  2026-10-12 — the sequence below is the convention to use *if* you lock one.
  Unlock → refresh → re-apply:
  ```powershell
  $file = "<skill-folder>/SKILL.md"
  icacls $file /remove:d $env:USERNAME
  # ...re-fetch from upstream...
  icacls $file /deny "${env:USERNAME}:(WD,AD)"   # write-only; (W) also blocks reads
  ```
- **Cost to the author — for a write-locked skill, updating to the latest
  upstream is deliberately awkward.** It can no longer be refreshed by simply
  editing the file: every update must go through the unlock → re-fetch → re-lock
  sequence above,
  or the write fails with *Access denied*. Budget one extra unlock + one re-lock
  per refreshed file; when several skills need updating, run all the unlocks in one
  pass, re-fetch each, then re-apply every deny afterwards. If this friction
  turns out to outweigh the protection, drop the ACL and rely on the rule
  above alone.
- The ACL is friction, not a security boundary — agents run as the file's
  owner and can re-grant themselves write. Treat the rule above as the
  primary guard; the ACL only makes accidental edits fail loudly.
- Repo-authored skills under `.github/skills/` are ordinary repo content —
  change them via branch + PR like any other file.

### Lessons learned → repo-owned skills

After every round or defect pass, **fold the durable lessons into the repo's
own skills and rules** — the files agents actually read before the mistake:
`.github/skills/*` (repo-authored, editable), `.github/copilot/rules/*`,
in-repo `.pi/skills/*`, and `documents/runbooks/*`. The owner decides which
lessons are durable (a lessons grill round; "all as recommended" settles it).
**Never patch installed/locked skills** (`~/.agents/skills`,
`~/.pi/agent/skills`) with a lesson — they are vendored upstream copies
(refresh rules above); a lesson that belongs there is filed upstream, or
restated in a repo-owned file carrying the same trigger. Cite the incident
date in one clause (evidence, not a changelog); the round doc holds the full
story.

**Propose, never pre-write (owner, 2026-10-23).** A durable lesson is **proposed in the reply** as a
candidate — what to write, where it goes, and the evidence — and reaches a repo-owned file **only
when the owner explicitly accepts it**. Writing the file first is a rule violation even when the
content is right: an unaccepted lesson riding a code commit makes the diff unreviewable, and the
next agent reads an unratified rule as ratified. Acceptance is explicit ("write it", "accepted", or
an answered lessons round); silence, a passing "all as recommended" on *other* questions, and the
agent's own judgment are not acceptance. If a lesson is proposed and not answered, it stays prose in
the reply and dies with the session — that is the correct outcome, not a loss.

**`/retro` is the capture step (owner, 2026-10-23).** Run the installed `/retro` skill (it lives in
`~/.agents/skills/retro` — installer-managed, never edit it) at the end of a build or a defect pass.
It reviews the session for environment-level improvements (navigation pointers, automated checks,
coding standards, AGENTS.md bloat, tool economy, no-ops, information access) and **presents
candidates to the owner**; the owner accepts, and only then are those candidates written into the
repo-owned files named above. A mechanical mistake becomes a deterministic check; a judgement call
becomes a coding standard. This paragraph is the pointer, not a copy of retro's categories — the
skill owns those and refreshes from upstream.

---

## Agent skills

### Issue tracker

Work is tracked as GitHub issues in `stayboy/School-Collab` via the `gh` CLI. Durable specs remain
the source of truth in `documents/specs/`; "publish to the tracker" creates an issue that links the
spec path. See `docs/agents/issue-tracker.md`.

### Triage labels

Five canonical triage roles, each label string equal to its role name
(`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`). See
`docs/agents/triage-labels.md`.

### Domain docs

Single-context: `GLOSSARY.md` at the repo root, ADRs in `documents/solution/adr-*.md`, doc-tree map
in `documents/README.md`. See `docs/agents/domain.md`.

---

## Specialty instructions

Read the relevant specialty rule file before changing code in that area. Keep
`AGENTS.md` for repository-wide rules and link to topic-specific guidance
instead of duplicating it.

| Area | File |
|---|---|
| Blazor components, Fluent UI, and styling | `.github/copilot/rules/blazor-components.md` |
| **Create / edit page layout** (main + rail skeleton, the single panel box, header/actions row, rail-membership test, control widths) | `documents/solution/panel-layout-pattern.md` — the **only** place these rules live; every other file points here |
| CSS isolation and styling | `.github/copilot/rules/blazor-components.md#blazor-css-isolation-and-styling` |
| Entity Framework Core migrations | `.github/copilot/rules/ef-migrations.md` |
| Logging and Aspire observability | `.github/copilot/rules/logging-aspire.md` |
| Testing (MTP Standard) | `.github/copilot/rules/testing.md` |
| Configuration documentation | `.github/copilot/rules/configuration-documentation.md` |
| AI services (`SchoolCollab.AI`, chat clients, tool calls) | `.github/copilot/rules/ai-services.md` |
| Card-level create / edit dialogs and sections | `.github/copilot/rules/section-card.md` |
| Shared constants and enums | `.github/copilot/rules/shared-constants.md` |
| Dialogs (shared form-fields, no nesting) | `.github/skills/dialog-ui/SKILL.md` |
| Dropdowns & selects (wrappers, binding, width) | `.github/skills/dropdown-ui/SKILL.md` |
| Fluent UI icons | `.github/skills/fluentui-icons/SKILL.md` |
| Fluent UI component props | `.github/skills/fluentui-component-props/SKILL.md` |
| Minimal-API composition (grouping, auth helper, typed results) | `.github/skills/minimal-api-composition/SKILL.md` + `documents/solution/endpoint-organization-pattern.md` |
| Bounded context creation | `.github/skills/bounded-context/SKILL.md` |
| Coded values domain | `.github/skills/coded-values/SKILL.md` |
| Azure AI OpenAI .NET | `.github/skills/azure-ai-openai-dotnet/SKILL.md` |
| **.NET/C# best practices (all C# changes)** | `.github/copilot/rules/dotnet-best-practices.md` + `.github/skills/dotnet-best-practices/SKILL.md` |
| **Portal (Python/FastAPI + Prefab UI)** | `documents/solution/portals-service-client-pattern.md` (pytest / `httpx.MockTransport` / FastAPI `TestClient` story, `.slnx` solution-items tripwire). The C# **code** rules do not apply to Python source in `src/SchoolCollab.Portals/` or `src/SchoolCollab.AuthPortal/`; their `.slnx` solution items are policed by .NET-side guards, and so is the auth portal's AppHost wiring. |
| **Generic Blazor framework patterns** (render modes, prerendering, JS interop, disposal, cascading state, forms) | `documents/solution/blazor-agent-skills-adoption.md` — the nine installer-managed `dotnet/skills` → `dotnet-blazor` skills. Repo rules win on conflict. |

**Generic Blazor skills vs. repo rules.** Nine user-level Blazor skills are installed from the
`dotnet/skills` → `dotnet-blazor` plugin (`author-component`, `collect-user-input`,
`configure-auth`, `coordinate-components`, `create-blazor-project`, `fetch-and-send-data`,
`plan-ui-change`, `support-prerendering`, `use-js-interop`). They are installer-managed
(`npx skills`, canonical copies in `~/.agents/skills/`, tracked in `~/.agents/.skill-lock.json`)
and cover the **framework** baseline only. Where one disagrees with this repo's own rules or
skills, the repo wins — and two of them never describe this repo's stack: `configure-auth`
(Identity / cascading auth state; this repo is Keycloak OIDC + `FEATURE:DisableOIDCAuth`) and
`create-blazor-project` (scaffolds `dotnet new blazor`; not used here). Refresh with
`npx --yes skills update <name> -g -y`, never by editing the installed file — see the
`refresh-vendored-agent-skill` skill.

**Default rule for C#:** For **any** change that touches a `.cs` or `.razor` code-behind, read
`.github/copilot/rules/dotnet-best-practices.md` (and its backing skill
`.github/skills/dotnet-best-practices/SKILL.md`) **before** writing code. This is not
optional-on-demand — it applies to every C# edit in this repo, and the rule's "Never" list is
enforced in CI by `SchoolCollab.ArchitectureTests.Unit`.

**Feature/fix implementation — ask before starting:** Before beginning a feature or fix
implementation, **ask the user which execution mode to use** — do not default to one;
the user wants to choose explicitly:

1. **Tier 0 (Solo)** — the session agent implements, reviews its own diff and accepts. The tier's
   definition lives in the skill's § "Tiers" (row 0) — the single place the ladder is defined; this
   file keeps the menu and the repo-wide constraints on tiers. Small, contained work, trivial
   changes included.
2. **Light round** — the tiered `orchestrator-worker-reviewer` skill, Tiers 1–2: the
   parent plans and accepts; a plan gate plus one worker run (Tier 1), adding a static
   diff-only reviewer (Tier 2). Small-to-medium behavioural fixes.
3. **Full four-agent round** — the same skill, Tier 3: orchestrator plans + owns the
   round doc, **the plan is independently reviewed before implementation** (skill
   step 2b — on every tier by `glm-5.3:cloud`), worker implements, reviewer
   statically verifies, orchestrator accepts and hands over UI-tester scope, UI
   tester bug-hunts delivered UI. No-UI rounds run **lean** (skill § "Tier 3
   lean"): the parent transcribes acceptance and no UI tester is dispatched — the
   orchestrator plan pass and its review are never skipped.

**Unattended (a modifier on any tier — 0, 1, 2 or 3 — never a tier of its own).** The owner may
run a round unattended **for that round only** — never a default, never inferred from
silence, never on by absence. Asking for it is part of this menu's question, not a
separate flag. In an unattended round the parent **still holds** the grill round. At step 2b the
**plan gate** — the only non-author seat that runs on every tier — adjudicates the enumerated
frontier in its FRONTIER block: it echoes each accepted question number and ends
`accepted: N of N`; any refusal line, missing echo, count mismatch or silence fails the block
and parks the round, and it may **never** auto-accept the plan author's own recommendations. At
step 5 (acceptance) a verdict that is anything other than a clean CLOSED **parks** — the residual
grill has no auto-accepting seat on any tier, so its questions are recorded unratified. At
**Tier 0 the plan's author is its only seat**, so `grill-me` is
mandatory and its answers are **recorded, not accepted**: the whole frontier goes into the round doc
under `## Open decisions (unattended solo - recorded, NOT accepted)` and every pin is marked
`unratified`. Migrations, contract/wire shape, security and auth, tenant isolation, and deleting a
public route **always reach a human** unless the owner pre-authorises that specific round by name.
Record it on round-doc line 1 as `Mode: unattended (auto-accept recommendations)` for Tiers 1–3, or
`Mode: unattended solo (recorded frontier, unratified)` for Tier 0, and set the ledger row's
`Mode` cell — one of `attended` · `unattended` · `unattended solo` (owner, 2026-10-21) — an
unattended pin and an owner pin must never look alike. **The report repeats them:** an unattended
round's completion message lists every pinned decision — the same entries its `## Acceptance`
carries — as a compact table; a pin must never survive only in the round doc, which this repo
treats as ephemeral. Full rules: skill § "Unattended rounds".

Each round produces a single `round-<slug>.md` (+ `diffs-<slug>.patch`) in
`documents/rounds/` — see "Repository docs layout" below.

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

### Repository docs layout

| Folder | Purpose |
|---|---|
| `documents/solution/` | Durable research, decisions, and implementation steps (technical memory — standard above). |
| `documents/specs/` | Durable feature specs that remain the source of truth. |
| `documents/rounds/` | **Ephemeral per-round agent docs** (one `round-*.md` + one `diffs-*.patch` per round) from the tiered `orchestrator-worker-reviewer` skill. Never write round docs into `documents/specs/`. The whole folder is safe to bulk-trash once rounds close — rules: `documents/rounds/README.md`. |
| `documents/runbooks/` | Operational runbooks: recurring procedures and incident/ops behavior. |
| `documents/ai-prompts/` | Reference archive of AI system-prompt variants — never loaded at runtime. |

Full folder decision rules, precedents, and naming conventions: `documents/README.md`.

---

## Build verification (during development)

This rule applies **inside a coding session**, every time production or test code
is changed — distinct from the PR-time `dotnet build` check in the pre-flight
section below.

- **Run `dotnet build` after every code change** (every `edit` / `write` that
  touches a `.cs`, `.razor`, `.csproj`, `Directory.Packages.props`,
  `Directory.Build.props`, or `appsettings*.json`). Build before reporting the
  change as done.
- **Fix compiler errors before continuing.** If the build fails, treat the
  failures as the next task — do not move on to feature work, do not mark the
  step complete, and do not commit.
- **Targets:** run `dotnet build SchoolCollab.slnx` from the repo root — the solution uses the **`.slnx`** XML format. Do **not** re-add a `SchoolCollab.sln`: CI runs no-arg `dotnet build` and a filtered `dotnet test` and relies on single-solution discovery (two solution files in one directory is an error). Tests
  aren't required for the in-session check unless the change touches test
  files directly.
- **Second target — the MAUI head is NOT in that solution.** `src/Mobile/SchoolCollab.Mobile` is
  deliberately not registered in `SchoolCollab.slnx` (it builds only from
  `src/Mobile/SchoolCollab.Mobile.slnx`), so a change to a shared component —
  `SchoolCollab.Admin.Shared` in particular — can break the mobile head with **no** signal from the
  main build. That is not hypothetical: the FluentUI 4.14.2 → 5.0.1 bump (2026-10-16) left the head
  with 22 v5-invalid sites while `SchoolCollab.slnx` stayed green. When a change touches a shared
  component or any package version, also run:
  `dotnet build src/Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj -f net10.0-windows10.0.19041.0 -m:1 -p:BuildInParallel=false`
  The `-m:1 -p:BuildInParallel=false` flags are **not** optional: the default parallel build exhausts
  memory on this machine and has repeatedly killed the agent process (~`0xC0000409`) instead of
  reporting a compile error.
  **The CI `Mobile (MAUI)` job is informational only (owner 2026-10-18).** It is path-filtered to
  `src/Mobile/**`, `SchoolCollab.Admin.Shared/**`, the two `Directory.*.props` pins and `ci.yml`, and
  it is **not** a gate to wait on: never let it block or delay a round report (it takes ~10 minutes)
  and surface a failure as a note, not a blocker. The mobile head is not feature-ready — this local
  build, not the CI job, is the shared-component tripwire.
- **Fast UI-iteration loop (hot reload):** for local visual iteration, prefer IDE hot
  reload (human) or a detached `dotnet watch` on the AppHost (agent) over the
  kill→build→restart cycle — markup/CSS/method-body edits then apply in place and
  Playwright can re-assert without a restart. Full recipe, observed caveats and
  recovery: `documents/runbooks/dev-loop-hot-reload.md`. Hot reload accelerates
  observation; it never replaces the build/test gates below.
- **Tests green but the preview "doesn't work" ⇒ stale circuit, not code.** Hot
  reload cannot apply "rude edits" — a new component `[Parameter]`, a changed
  record's shape, new `@implements`/`@inject`, or a **new static asset** (a
  collocated `*.razor.js`, served from `_content/...` only after rebuild).
  Restart the preview (full stop/start) before debugging the code; bUnit loose
  mode masks the missing-asset 404 (observed 2026-10-12, recorder pill round —
  the runbook's failure-modes list carries the full symptom set).
- **Locks (MSB3021 / MSB3027).** `dotnet build` can fail because a running
  service — a plain `dotnet run` (AppHost, API, worker), an IDE-launched debugger
  session, or a detached `dotnet watch` — is still holding output binaries (an
  open Visual Studio counts too). **Any running process from the repo's output
  dirs locks its own outputs**: before a full-solution build, stop them all
  (`taskkill /PID <pid> /T /F` on the watcher/apphost, close the IDE session);
  a project-scoped build (`dotnet build <proj.csproj>`) is safe while unrelated
  services run. Don't retry the build in a loop — surface the lock to
  the user and let them choose how to stop the offending process.

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

- MSBuild **SDK references** are not NuGet packages — CPM does not manage them, so their
  versions stay inline. Two forms exist in this repo:
  - `<Sdk Name="..." Version="..." />` as a child element of a `Microsoft.NET.Sdk` project
    (the older style);
  - `<Project Sdk="Aspire.AppHost.Sdk/13.5.4">` — the form `aspire update` migrated the
    AppHost to at 13.5.4. In that form the SDK also supplies `Aspire.Hosting.AppHost`, which
    is therefore **no longer a `PackageReference`** (and its `PackageVersion` entry was
    removed from `Directory.Packages.props`).

## Target framework

All projects target **net10.0**. Do not downgrade to net9.0 or earlier.

## Architecture reminders

- **Cross-context access** — the mechanism depends on **what crosses the boundary**. (The single bullet
  here used to read "use MassTransit contracts", which conflated two mechanisms and named a library this
  repo does not use: the bus is **RabbitMQ** — `RabbitMQ.Client` + the transactional outbox.)
  - **No direct project references between bounded contexts.** The shared kernel (`SchoolCollab.Core`,
    `SchoolCollab.ServiceDefaults`) and `*.Contracts` are allowed; another context's `.Core` is not.
  - **Events** (state changes) → the transactional outbox + integration contracts. **Never publish to a
    bus from a handler.**
  - **Synchronous reads** → a port interface in the calling context's `.Core` with the HTTP client in its
    `.Api`, registered via `AddCrossModuleHttpClient`, plus an AppHost `.WithReference` on the calling
    project (enforced by `CrossModuleWiringTests`).
  - **No new sync hop on a command's write path for reference data** — replicate it via events and read a
    local projection; a sync reference-data hop is acceptable only after replication was considered and
    **rejected on record**. Live-consistency write-path hops stay sync but must be hardened (explicit
    timeout, circuit breaker, long handler lifetime, retryable `ObjectDisposedException`) with a documented
    degradation policy. Read/enrichment hops are allowed and must degrade gracefully (never fail the parent
    operation). Any PR adding a **write-path** sync hop carries the `adr-cross-module-calls` tag and needs
    architect sign-off.
  - Governing docs: `documents/solution/adr-cross-module-calls.md` (which class is which) and
    `documents/solution/cross-module-http-client-pattern.md` (the HTTP reference pattern).
- No MediatR — CQRS is implemented via `ICommandHandler<T>` / `IQueryHandler<T,R>` with
  Scrutor assembly scanning.
- Domain entities use PostgreSQL `xmin` (row version) for optimistic concurrency.
- **Reuse before new infrastructure.** Before pricing an endpoint, schema, or new
  module, inventory what the repo already ships (streaming routes, client-side
  players, staging seams, JS modules) and name the reuse in the plan. Owner
  precedent 2026-10-12: a pill-playback round initially priced as Tier-3 endpoint
  work became a reuse of the recorder's own review player + a kept Blob URL.
- **API Endpoint Grouping** — two layers, full pattern and templates in
  `documents/solution/endpoint-organization-pattern.md`: (1) a `<Domain>Endpoints` orchestrator,
  `Map<Domain>Endpoints(this WebApplication app, IFeatureFlagService featureFlags)`, owns
  `MapGroup(prefix)`, the flag-conditional authorization policy and the chained `Map*Routes()` calls;
  (2) one `<Specialty>Routes.cs` per specialty, `Map<Specialty>Routes(this RouteGroupBuilder group)`,
  maps relative paths only and never touches authorization or feature flags. Never define routes
  inline in `Program.cs`. Apply a group's policy through `RequireAuthIfOidc(...)` — never a
  hand-rolled `if (!featureFlags.IsEnabled(FeatureFlagKeys.DisableOIDCAuth)) { … }` block; enforced by
  `MinimalApiCompositionArchitectureTests`. New or edited endpoints return `TypedResults`, not the
  `Results` factory, so OpenAPI emits real response schemas.
- **Feature Flags**: there are **two kinds**, each with exactly one write-point — read `documents/solution/feature-flag-workflow.md` before adding one.
  - **Runtime, mutable, tenant-overridable flags** (e.g. `FEATURE:EnableCodedValuesAiChat`) are owned by the central **Config service** (`settings-api` + `config-db`, admin UI `/config-flags`, audited, tenant-overridable) — **not** the AppHost.
  - **Deployment-time startup switches** (e.g. `FEATURE:DisableOIDCAuth`, `FEATURE:DisableKeycloakLoginUi` — read once at process start, so they can never be Config flags) live in the AppHost's `Parameters:` block (see `src/AppHost/SchoolCollab.AppHost/appsettings.json`) and are fanned out via `WithEnvironment("FeatureFlags__FEATURE__...", param)`, with the dev posture committed in the AppHost's own `appsettings.Development.json`.
  - A startup switch's committed **base default is fail-closed** (a publish must never bake a dev bypass into a manifest), and `appsettings.json` under `Parameters:` is the canonical record of every deployment-time parameter default — update it in the same PR that adds one. Adopted design, evidence and guards: `documents/specs/startup-flag-governance.md`.
  - Each flag must also be mapped onto the consumer sites in `documents/configuration.md` §2. **Never scatter a flag value across individual service `appsettings.json` files** — an env-var fan-out silently masks such a copy, which is exactly why the startup switches are guarded by `AppHostStartupFlagWiringArchitectureTests` — and do not rely on the `SchoolCollab.Config` HTTP overlay (it was removed — see `documents/solution/centralized-feature-flags-implementation.superseded.md`).

---

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

### 3. Run the CI-parity test gate

Run the repo's gate, not no-arg `dotnet test` (owner, 2026-10-24):

```bash
scripts/round/gates.sh --no-maui --no-integration   # baseline
scripts/round/gates.sh                              # escalation — every leg, no skips
```

- **0 failures on every leg that ran.** `gates.sh` mirrors CI's filter legs (unit +
  integration), the repo-wide `ArchitectureTests.Unit` scanner, the cross-module wiring
  guard and both portal suites. A hand-picked per-project list is false confidence —
  `scripts/round/README.md` says why, and `LocalGateCiParityTests` pins it.
- **A leg that matches nothing is a failure, never a pass.** `gates.sh` fails on "zero tests
  ran"; CI tolerates that exit code (`--ignore-exit-code 8`, `Directory.Build.props`) and the
  repo has paid for the tolerance twice (2026-10-12, 2026-10-15).
- **Escalate to the full gate — no flags — when the diff touches** shared chrome
  (`SchoolCollab.Admin.Shared`, `SchoolCollab.Core`, `SchoolCollab.ServiceDefaults`), a
  migration, an integration contract, or auth. A filter cannot see the consumers it broke,
  and "the diff is three lines" is not a risk argument.
- **`--no-maui` and `--no-integration` are baseline flags, never escalations.** The MAUI
  head is outside `SchoolCollab.slnx`, so it is the only tripwire for a shared-component
  break — the same case that already forces the full gate. The integration leg is skipped
  locally because its two OpenRouter live tests fail here (HTTP 400) while CI self-skips them
  for lack of a key, so the leg cannot reach green on a developer machine; CI owns it.
- **A skipped leg is recorded, never implied**: one line in the PR body (and the round doc)
  naming each leg that did not run and why. `gates.sh` prints that list — paste it.
- This gate decides *which tests run*, never *whether they are written*: §2 above and
  `.github/copilot/rules/testing.md` are unchanged.

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
| Tests pass | `scripts/round/gates.sh --no-maui --no-integration` (full gate, no flags, on shared-chrome / migration / contract / auth diffs) | 0 failures; every skipped leg named |
| Build succeeds | `dotnet build` | 0 errors |

**Do not skip these checks.** If any check fails, fix the issue on the branch before
creating the PR.

---

## Main branch merge policy

`main` is a protected delivery branch in process. Do not push or merge directly to
`main` from ad-hoc work.

Required workflow (every commit/push/PR/merge action is gated — "create a
branch" or "fix X" authorizes only the local change, not committing,
pushing, opening a PR, or merging; each of those needs its own explicit
user instruction):

1. Create a feature or fix branch from `main`.
2. Add/update tests for behavioural changes.
3. Commit locally on the branch only after the user explicitly says
   "commit" — do not commit automatically after steps 1–2.
4. Push only after the user explicitly instructs to push:
   ```bash
   SCHOOLCOLLAB_ALLOW_PUSH=1 git push -u origin <branch-name>
   ```
5. Open a PR targeting `main` only after the user asks to open/create a PR.
   **If a previous related PR is already open** (same area, arc, or feature
   train), do **not** open an independent PR — deliver the new work as a
   `gh stack` layer on top of it instead (see the `gh-stack-pr-train` skill: a
   registered layer runs the stack root's CI checks, and the train reviews as
   one ordered sequence). With no related PR open, a plain PR from `main`
   is correct.
6. Wait for the GitHub Actions `Build & Test` check to pass.
7. Merge only after the user asks to merge — **squash merge, source branch deleted**. The command and
   the retention rule are canonical in `.github/merge-policy.md` § Merge strategy; do not restate them
   here.
8. Switch to `main` and pull the merged result:
   ```bash
   git checkout main
   git pull origin main
   ```

**Execution model for commit / gh-PR tasks:** Once the user authorizes a
commit (and the related PR/stack steps per this policy), the mechanical
git/gh execution — staging the named change set, the commit, `gh stack`
unstack/init/submit, and `gh pr` operations — is delegated to a lightweight
subagent running `ollama-cloud/deepseek-v4.1-flash:1017` (pi provider; Cline
Pass equivalent, provider-qualified so it resolves as written:
`clinepass/cline-pass/deepseek-v4.1-flash-1017`; owner 2026-10-18 — the worker id
is pinned **with its build suffix** because the registry carries several
v4.1-flash builds and a bare id is ambiguous; supersedes the 2026-09-25
suffix-less spelling). **Suffix spelling is per provider** (corrected
2026-10-23): ollama-cloud writes the build as a COLON (`:1017`), ClinePass as a
HYPHEN (`-1017`) — and ClinePass publishes **both** the suffixed and the bare
`cline-pass/deepseek-v4.1-flash`, plus the free-tier `cline-free/deepseek-v4.1-flash`.
An earlier note here claimed ClinePass carried no suffixed id; that was wrong,
produced by grepping the extension's static catalog, which never lists a
dynamically discovered id. Resolve against the live registry
(`pi --list-models flash-1017`) before pinning, never against a source grep. The parent agent shows and
confirms the change set, then hands the execution to that agent. Every gate
in this policy (explicit authorization for each action, the change-set
review, `SCHOOLCOLLAB_ALLOW_PUSH=1` for pushes, no merge without
instruction) applies unchanged to the agent's steps; the agent reports the
staged paths, commit SHA, and PR links back.

**One writer per working tree.** Never run two writing subagents concurrently in
the same checkout: their edits land in one shared diff that cannot be attributed
to either run, and a reviewer cannot diff it. Run writers sequentially, or isolate
each in its own git worktree. Writing the plan, spec, or round doc is the parent
orchestrator's own work, never a delegate's.

**Keep delegate briefs short and bounded.** Multi-minute waits (CI polling,
sleeps) belong in the **parent**, which survives an interrupted call or a
compaction; hand the subagent only the quick git/gh legs (stage/commit/push/PR,
or merge + pull). A brief containing a poll is the fragile shape: on 2026-09-25 a
delegate was aborted mid-poll by a session compaction, and only its
already-completed steps (commit, push, PR) had persisted. Recover by reading the
live state (`git log`, `git ls-remote`, `gh pr view`, `gh pr checks`) and resuming
from the first unfinished step — never by re-running the whole brief.

**Local commit/push hold (the enforcement layer for commits):** Do not
commit, push, open a PR, or merge without an explicit user instruction
("commit", "push", "open a PR", "merge"). When the user asks to commit,
create the commit and STOP — do not push unless they then explicitly ask
to push. Keep working-tree changes uncommitted by default. The
`.githooks/pre-push` hook enforces the push half mechanically; this
instruction is the enforcement layer for the commit half, since commits
are local and cannot be blocked server-side.

**User reviews local changes before each commit:** When the user says
"commit" (or any variant — "commit changes", "commit it", "commit the
changes"), and the working tree has uncommitted changes, the agent must
**first show the change set** (`git status --short` + `git diff --stat`,
or `git diff <path>` for the relevant files) and **wait for the user to
confirm the change set to be committed** before running `git commit`. The
agent does not commit whatever happens to be in the working tree on the
user's behalf. If the user has already named the change set ("commit
src/Foo.cs", "commit the rule file"), the agent may commit exactly that
and report. The default is: keep working-tree changes uncommitted by
default; the user reviews and authorises each commit explicitly.

Do not merge a PR while required status checks are still running or failing. If
CI fails, fix the branch and wait for a green workflow before merging.

The repository includes `.github/merge-policy.md` for the full policy and
`.githooks/pre-push` as a local convenience guard that holds all pushes until
the user explicitly allows them and still blocks direct pushes to `main`. GitHub
branch protection or rulesets should be enabled in repository settings where
available to enforce the same rule server-side.

---

## Cline memory graph

This repo wires a **Memory MCP server** for cross-session recall. Cline (and any
agent using that MCP server) follows the recall/recording conventions in
`CLINE.md` — that file covers only memory-graph usage, so it stays separate
from this manifest.
