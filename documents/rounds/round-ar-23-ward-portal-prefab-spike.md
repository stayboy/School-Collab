# Round ar-23 — Ward-portal Prefab-UI Phase-0 spike

- **Status: IN PROGRESS — Tier 1 light round, started 2026-09-19.** Provider: pi (`ollama`); worker `ollama-cloud/deepseek-v4.1-flash` (one run, no reviewer — Tier 1); parent plans, transcribes the worker report, and accepts. Branch `stack/23-ward-portal-prefab-spike` cut at `f87a1987` (the ar-22 commit, PR #246 — this round is stacked on it; its PR will retarget to `main` after #246 merges).
- **Source of truth:** `documents/specs/teachers-ward-portal-prefab-plan.md` (locked Q1–Q6 grill decisions). This round executes **Phase 0 — Spike** of that plan and nothing beyond it. The plan's §5 Phase-0 exit criterion is the acceptance bar; the re-decide gate afterwards belongs to the owner.

## Scope

Execute the prefab plan's Phase 0:

| # | Item |
|---|---|
| 1 | `Aspire.Hosting.Python` via CPM (`Directory.Packages.props` + AppHost csproj) |
| 2 | Root-level `portals/` Python app (uv-managed, pinned `prefab-ui`, FastAPI + Prefab ward view) |
| 3 | One additive AppHost block hosting it with service discovery to `assignments-api` |
| 4 | `.gitignore`: ignore `.venv/` |
| 5 | `documents/configuration.md` §1: `portals` row in the resource table |
| 6 | Runtime evidence: the portals resource live in the Aspire dashboard, rendering one **live API call** to assignments-api as a Prefab view |

**Out of scope (the re-decide gate):** any MVP work (ward assignment player, module progress, submission), teacher portal views, OIDC/auth beyond the existing dev bypass, Playwright tests, CI for `portals/`, and any backend change. Zero `.razor`/`.cs` changes outside the AppHost block and CPM files. No migrations, no contract changes, no new secrets.

## Plan (worker contract)

### Machine facts (verified 2026-09-19 by the parent)

- Python **3.14.6** on PATH; **uv 0.11.19** on PATH; pip 26.1.2. No installation needed.
- The AppHost starts on this machine (the `~/.dcp/state.elevated` blocker is fixed; see `documents/runbooks/aspire-apphost-silent-hang.md`). Launch scripts with all secret parameters (keycloak client secret, smtp user/password, etc.) already exist at `C:/Users/skwar/AppData/Local/Temp/ar21/run-apphost3.cmd` and `run-apphost4.cmd` — reuse/adapt one (change only the log-file path).
- Under the AppHost the assignments-api runs with the dev bypass (`FEATURE:DisableOIDCAuth` default `true` in Development) → **anonymous HTTP works**; no token dance needed.
- The dev database currently holds **zero assignment rows** → `GET /assignments` returns **200 `[]`**. A `[]` response still satisfies the spike (wiring proven); record the data-visibility note, do not seed unless trivial (see AC3).
- postgres/rabbitmq containers are `Exited` — the AppHost will start them (persistent volumes, data intact).

### Prefab-UI API essentials (from https://prefab.prefect.io/docs, fetched 2026-09-19 by the parent; the worker has no web access)

- Package: `prefab-ui` **0.20.2** (PrefectHQ; requires Python 3.10+; 0.x — PIN the exact version). Docs: `https://prefab.prefect.io/docs/welcome`, quickstart `…/docs/getting-started/quickstart.md`, REST-server pattern `…/docs/running/api.md`, full index `…/docs/llms.txt`.
- DSL (quickstart example):
  ```python
  from prefab_ui.app import PrefabApp
  from prefab_ui.components import Badge, Card, CardContent, CardFooter, Column, H3, Input, Muted, Row
  from prefab_ui.rx import Rx

  name = Rx("name").default("world")

  with PrefabApp(css_class="max-w-md mx-auto") as app:
      with Card():
          with CardContent():
              with Column(gap=3):
                  H3(f"Hello, {name}!")
                  Input(name="name", placeholder="Your name...")
  ```
- Serving: `prefab serve app.py --reload` (dev CLI, opens :5175) — **not** what we use under Aspire. For an HTTP server: "Prefab has no dependency on any web framework… the pattern works with anything that can serve HTML and JSON" — FastAPI pattern: `PrefabApp(view=view, state={...}).html()` returned via `HTMLResponse(...)`. Interactivity via `prefab_ui.actions` (`Fetch`, `SetState`, `ShowToast`) + `prefab_ui.rx` (`Rx`, `RESULT`).
- Component imports seen working in the docs: `from prefab_ui.components import …` incl. `DataTable, DataTableColumn, Card…, Badge, Button, Column, Row, Text, Muted`; charts under `prefab_ui.components.charts`; conditional flow `prefab_ui.components.control_flow` (`If`, `Else`).
- **After `uv add prefab-ui`, the installed package source under `portals/.venv/Lib/site-packages/prefab_ui/` is the authoritative local reference** — read/grep it for exact signatures rather than guessing.

### Implementation rows

| Row | File(s) | What |
|---|---|---|
| 1 | `Directory.Packages.props` | `<PackageVersion Include="Aspire.Hosting.Python" Version="13.4.5" />` under the Aspire group. **First verify** the exact available patch (e.g. `dotnet package search Aspire.Hosting.Python --take 5` or nuget.org via the existing tooling): if 13.4.5 does not exist, pin the closest 13.4.x and record the deviation. Never hand-edit another package's version. |
| 2 | `src/AppHost/SchoolCollab.AppHost/SchoolCollab.AppHost.csproj` | `<PackageReference Include="Aspire.Hosting.Python" />` (no Version — CPM). |
| 3 | `portals/pyproject.toml` + `uv.lock` | New root-level Python project, uv-managed. Deps: `prefab-ui==0.20.2` (pinned exact), `fastapi`, `uvicorn`, `httpx`. Python requires-python >= 3.10. Generate the lock with `uv lock` (run from `portals/`), create the venv with `uv sync`. |
| 4 | `portals/app.py` | FastAPI app exposing `app = FastAPI()` (ASGI `app:app`) with one ward view route (e.g. `GET /` or `GET /ward`): fetch assignments from assignments-api over httpx **using Aspire service discovery env vars**, then return `HTMLResponse(PrefabApp(view=<view over the fetched rows>, state=…).html())`. Prefer `DataTable`/`DataTableColumn` over the fetched rows; include a heading like "Ward portal (prefab spike)" and a muted note showing the API base URL used. Accept the discovery env var **defensively**: try `services__assignments-api__http__0` and the simplified `ASSIGNMENTS_API_HTTP` form, log which one was found (raise a clear error listing the candidates if neither exists). No auth (dev bypass). |
| 5 | `src/AppHost/SchoolCollab.AppHost/Program.cs` | One additive block after the `families` block, mirroring its shape: `builder.AddUvicornApp("portals", "..\\..\\..\\portals", "app:app").WithUv().WithReference(assignmentsApi).WaitFor(assignmentsApi)` — **verify `AddUvicornApp` exists in the pinned `Aspire.Hosting.Python` first** (reflection probe like ar-21's `.WithHttpHealthCheck` check, or package-source grep under `~/.nuget/packages/aspire.hosting.python/<ver>/`); if absent, fall back to `builder.AddPythonApp("portals", "..\\..\\..\\portals", "run.py")` with a tiny `run.py` that boots uvicorn against `app:app` — record which variant shipped and why. Add a short comment: Phase-0 spike per the prefab plan, re-decide gate before MVP. |
| 6 | `.gitignore` | Add `.venv/` (the repo already ignores `__pycache__/` and `*.pyc`; do not duplicate those). |
| 7 | `documents/configuration.md` | §1 resource-name table: one new row `\| portals \| Python app (uv / FastAPI + Prefab UI) \| — \|` placed after the `students-worker` row. Nothing else. |

### Verification (the worker runs all of these)

1. `dotnet build SchoolCollab.sln` — **0 errors** (warnings acceptable; expect the pre-existing `NU1801`/`MSTEST0042` set).
2. `dotnet test tests/SchoolCollab.ArchitectureTests.Unit` — 29/0 (the AppHost parse guards must still pass with the new block; `AppHostSettingsDbWiringArchitectureTests` matches only `AddProject` chains, so it must be unaffected — confirm, don't assume).
3. `dotnet test tests/SchoolCollab.Core.Tests.Unit` — 92/0 (CrossModuleWiringTests must not trip on the new resource).
4. Runtime: launch the AppHead via an adapted `run-apphost*.cmd` (log to a fresh file), wait for `Distributed application started`, then capture: (a) the `portals` resource present with an endpoint and logs (`aspire describe`, or grep the AppHost log), (b) `GET http://localhost:<port>/` returns **200** with the Prefab HTML (curl to a file, then grep for a known string like the page heading and the React renderer bundle), (c) the assignments-api call actually happened — the view route logs the fetched status code / row count, or the assignments-api log shows the inbound request, (d) `127.0.0.1:5432`-style fallback errors: 0 for all hosts (ar-22 regression check).
5. **Kill the AppHost + DCP cleanly when done** (PowerShell: stop `dotnet.exe` whose CommandLine matches `*AppHost*`, `dcp.exe`, and any `python.exe`/`uv.exe` spawned by the run — never match your own shell) so the parent can build afterward.

### ACs (acceptance bar — all runtime-only items evidenced by the worker's verbatim output)

1. **AC1 — package wiring**: `Aspire.Hosting.Python` pinned via CPM (exact version recorded), csproj reference added, build 0 errors.
2. **AC2 — portals app**: `portals/` exists with pinned pyproject + uv.lock; the venv is NOT committed (ignored); `app.py` renders a Prefab view through FastAPI.
3. **AC3 — live API call**: the rendered view contains data fetched from assignments-api through Aspire service discovery at request time; status code recorded. `[]` is acceptable **only with** the data-visibility note; optionally (cut-line protected — abandon after ~2 failed attempts) seed one assignment via the API so a row renders.
4. **AC4 — dashboard**: the AppHost cold start shows the `portals` resource with an endpoint; no host regressed (all Healthy; 0 fallback errors).
5. **AC5 — docs + hygiene**: configuration.md row added; `.gitignore` covers `.venv/`; no file outside the plan rows was touched.

### Constraints (binding)

- **30-minute cap.** At the cap, write the report with what is done/what remains and stop — do not rush past verification quality.
- **STOP and report** (no workarounds) if: ENOSPC ("no space left"), `Aspire.Hosting.Python` has no compatible 13.4.x, `uv lock/sync` fails, or the AppHost fails to start for a reason outside this plan.
- **Report file MANDATORY**: `documents/rounds/.ar-23-worker-report.md` — files changed, build/test results with per-suite counts, pinned versions (prefab-ui, Aspire.Hosting.Python, uv, Python), which AppHost variant shipped (`AddUvicornApp` vs fallback) and why, runtime evidence **verbatim** (commands + responses), deviations, residuals. The worker must NEVER edit this round doc or any `documents/specs/` file.
- No commit, no push, no `gh`, no edits to `AGENTS.md`, the skills, or anything under `documents/` beyond rows 7 and the report file. Never build while unsure whether another process holds output binaries; kill strays first.

## Worker Report (transcribed by the parent — full report in `.ar-23-worker-report.md` scratch)

- **Status: DONE** — all 7 rows + every verification step, within the 30-min cap. Worker `ollama-cloud/deepseek-v4.1-flash` (Tier 1). Machine left clean (0 dcp / 0 python / 0 AppHost).
- **Variant shipped: `AddUvicornApp(...).WithUv()`** — primary, no fallback; `AddUvicornApp`/`WithUv` confirmed present in the `Aspire.Hosting.Python` 13.4.5 DLL **and** by compilation. `WithMcpServer` absent at this pin.
- Pinned: `Aspire.Hosting.Python` 13.4.5 (13.4.0–13.4.6 all exist), `prefab-ui==0.20.2`, uv 0.11.19 (pre-installed), Python 3.14.5 in the venv (uv-resolved).
- Worker verification: build 0 errors; ArchitectureTests 29/0; Core.Tests.Unit 92/0; runtime cold start ~190 s, `portals` Running + Healthy, `GET /` 200 with the Prefab page, `/health` last_fetch = `{status:200, row_count:0}`, 43 Healthy / 0 unhealthy, 0 fallback errors.
- **Worker findings** (all carried into Acceptance/Residuals below): discovery env var is the .NET-style `services__assignments-api__http__0` (the plan's simplified `ASSIGNMENTS_API_HTTP` is ABSENT — plan §3 assumption wrong for this pin); `WithUv()` creates a `portals-installer` resource (fresh clones self-bootstrap via the committed `uv.lock`); the renderer loads from jsDelivr CDN pinned to 0.20.2 (external dependency at page load); the view compiles to the documented JSON protocol (`{"$prefab":{"version":"0.3"},…}` in `prefab:initial-data`); the app models as `Executable` (internal uvicorn :63073 mapped to the allocated :50984); `WithMcpServer` does not exist at 13.4.5; route-level `logger.info` does not reach `aspire logs portals` (uvicorn logging config — live-call proof came from `/health` `last_fetch`).
- **Deviation 1 (machine-side, needs an owner decision):** the user-level NuGet source `C:\Telerik Collection for .NET 2025 Q2` is missing → `NU1301` on any restore that must download. The worker used a one-off `-s https://api.nuget.org/v3/index.json` restore (no repo change); **no Telerik package is referenced anywhere**; the plain build then succeeds. CI unaffected.
- Deviation 2 (cosmetic): the launch script's log path was not rewritten, so the run logged to `apphost6.log` (read from the real path). Deviation 3 (by design): a small `GET /health` diagnostics route beyond the ward view — the spike's observability affordance.

## Parent verification (first-hand re-run — Tier 1 has no reviewer)

The parent re-ran the acceptance pass independently: plain `dotnet build SchoolCollab.sln` **0 errors** (12 warnings incremental) — confirming the NU1301 deviation is resolved by the cached package; ArchitectureTests **29/0**; Core.Tests.Unit **92/0**; and a fresh orchestrated cold start (log `apphost8.log`, launched hidden — note: a previous visible-console launch died from a `^C` reaching the console window; `Start-Process -WindowStyle Hidden` is the robust recipe):

```text
Distributed application started: 1   port warnings: 0
portals URL: https://localhost:62143
GET /  -> HTTP 200, "Ward portal" x2, prefab:initial-data x1,
         renderer refs cdn.jsdelivr.net/npm/@prefecthq/prefab-ui@0.20.2/dist/app/renderer.{css,js}
GET /health -> {"status":"ok","api_base_url":"http://localhost:5199",
  "api_env_var":"services__assignments-api__http__0",
  "last_fetch":{"status":200,"row_count":0,...}}
aspire describe: 43 Healthy / 0 Unhealthy-Error-Failed
fallback errors: assignments-api 0, students-api 0, settings-api 0 (ar-22 regression: none)
portals log: "Application startup complete" (uvicorn) x1
```

Teardown after verification: 13 dcp + 8 SchoolCollab.* + 3 python + 1 dotnet killed; containers stopped, the 4 ephemeral ones removed; `ar10repro` untouched.

## Acceptance

| AC | Verdict | Basis |
|---|---|---|
| AC1 package wiring | **PASS** | CPM pin 13.4.5 + csproj reference; plain build 0 errors (parent re-run) |
| AC2 portals app | **PASS** | `portals/{app.py,pyproject.toml,uv.lock}`; prefab-ui==0.20.2 exact; `.venv/` ignored (git would add exactly the 3 files) |
| AC3 live API call | **PASS** | First-hand: `/health` last_fetch status 200 via `services__assignments-api__http__0`; row_count 0 recorded as the known dev-DB data-visibility limit (seeding left un-attempted, cut-line protected — the call itself is proven) |
| AC4 dashboard / no regression | **PASS** | First-hand: portals Running + Healthy, 43 Healthy / 0 unhealthy, 0 fallback errors across all hosts |
| AC5 docs + hygiene | **PASS** | configuration.md row, `.gitignore` `.venv/`, diff = exactly the plan rows (8 files, +638) |

**Round verdict: CLOSED — Phase-0 spike delivered.** Patch frozen at `diffs-ar-23-ward-portal-prefab-spike.patch`. Per plan Q6, the MVP go/no-go is now an **owner re-decide**, not part of this round.

## Residuals / re-decide inputs (for the owner)

- **Re-decide gate inputs:** renderer is **CDN-loaded** (jsDelivr, version-pinned) — offline/air-gapped needs `renderer_mode`/local bundle; discovery env var is `.NET`-style (plan §3 wording wrong for this pin); `WithMcpServer` unavailable at 13.4.5; `Executable` modelling means no container image (uv-aware Dockerfile is a deploy-time item); route logs don't reach the dashboard without a logging-config fix.
- **Machine-side:** the broken user-level NuGet source (`C:\Telerik Collection for .NET 2025 Q2`, missing → NU1301 on cold restores) should be removed/repaired — owner decision, machine-local, no repo impact.
- **Post-spike (Phase 1+, only on go):** auth (currently dev bypass), Playwright tests, CI for `portals/`, publish/deploy story, seeding a visible assignment row.