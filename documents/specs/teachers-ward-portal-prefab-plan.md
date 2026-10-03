# Teachers & Ward Portal — Prefab UI (Python) integration plan

Status: **Phase-0 spike LANDED (ar-23, PR #247 → `8d1c8a20`, 2026-09-21)** — phases 1+ remain a proposal pending the owner's MVP go/no-go. The portal's service-client structure (`api/` / `views/` / `tests/`, plus its `.slnx` solution items and their guard) landed on `main` in PR #250 (`a6933c40`); pattern and decisions: `documents/solution/portals-service-client-pattern.md`.
Date: 2026-09-16 (updated after grill-me session — see `brainstorms/prefab-ui-portals.md`; **2026-09-21:** Phase 0 recorded as landed and **Q1 revised** to a module folder under `src/`; **2026-10-01:** teacher-portal spike + auth-role/policy decisions adopted — §1 T1–T7; **2026-10-03:** the read-only teacher surface landed (round `portal-teacher-surface`) — a flat `views/teacher.py`, the `portals-python` CI job, and the two pre-flip prerequisites recorded in §1)
Branch context: authored alongside the AR-13 round (branch `stack/13-ar-13-families-ward-surface`, since squash-merged to `main` as PR #236); the AR train (ar-13/ar-14/ar-15 — #236/#237/#239) has since fully merged to `main`. No code for this plan has landed.

## 1. Goal

Evaluate and plan a Python-hosted portal surface — built with **Prefab UI**
(`prefab-ui` on PyPI) — covering the **Teachers portal** and the **Ward portal**,
orchestrated by the Aspire AppHost, consuming existing bounded-context REST APIs.
This is the *second-surface* option alongside the existing Blazor hosts:

| Existing surface | Host project | Role today |
|---|---|---|
| Admin (Blazor/Fluent UI) | `src/SchoolCollab.Admin` | Teacher-facing management UI |
| Families (Blazor) | `src/SchoolCollab.Families` | Ward/guardian surface (ward list + assignment player, deep-link landing, guardian e-sign — F1 2b + E1 + WS-F3) |

A Prefab-based portal would **not replace** either host initially; it is a
**new side project** (its own module folder `src/SchoolCollab.Portals/`, same repo) that consumes
the same APIs. The backend (C# bounded-context APIs + Aspire AppHost) stays
**exactly as it is today** — the Python app is a pure HTTP consumer; prefab-ui
is frontend-only.

### Locked decisions (grill-me session, 2026-09-16)

| # | Branch | Decision |
|---|---|---|
| Q1 | Repo placement | Same repo, its own **module folder under `src/`** — `src/SchoolCollab.Portals/`, mirroring the two existing UI hosts `src/SchoolCollab.Admin/` + `src/SchoolCollab.Families/` — registered in the AppHost via one additive block. **Revised 2026-09-21:** the original "root-level `portals/`" decision broke the repo convention that every module lives in its own folder under `src/`; the spike landed root-level and was relocated under owner instruction |
| Q2 | Auth | Dev bypass first (TestAuth-style, ward id in route, mirroring Families / `FEATURE:DisableOIDCAuth`), OIDC deferred |
| Q3 | API fit | Existing Assignments API routes already cover ward completion and teacher review — zero backend changes (see §4) |
| Q4 | MVP order | Ward portal first, teacher review second |
| Q5 | Serving | Aspire AppHost caters for it solely — app launched via `AddPythonApp` entrypoint only; no separate serving-stack layer |
| Q6 | Definition of done | **Spike only**: AppHost + prefab + one live API call in the Aspire dashboard, then re-decide before any MVP work |

### Teacher-portal spike + auth roles/policies (grill session, 2026-10-01)

Owner-ratified decisions for the **teacher portal** — the staff/teacher-facing
workspace in the same Prefab app. Direction: assignment creation and management
are staff/teacher-facing, and the portal is the eventual home for the working
assignment feature set.

| # | Branch | Decision |
|---|---|---|
| T1 | Container project | **`src/SchoolCollab.Portals/`** already hosts it — add `views/teacher/` + `api/` client methods. **No new project.** (The sibling `src/SchoolCollab.AuthPortal` owns identity/login/user-admin, not the teacher workspace.) |
| T2 | Role taxonomy | Add **`teacher`** and **`staff`** as declarative Keycloak realm roles in `school-collab-realm.json` (D11: definitions declarative; assignment via the auth admin UI). Approval is a **policy** over (staff ∨ admin), not a fourth role. |
| T3 | Teacher data scope | A `teacher` sees assignments they **created** (`CreatedByTeacherId`) **plus** assignments for the **subject + grade** they teach, resolved from `TeacherGradeLevel` (`TopicId` = subject, `GradeLevelId` = grade, optional `TeacherRoleCodedValueId` = `TCHROLES` role). `staff` and admins see tenant-wide. |
| T4 | Enforcement point | **API-side authorization policies** on the assignment endpoint groups (fail-closed), conditional on `FEATURE:DisableOIDCAuth` per `AGENTS.md`. Portal-side hiding is UX only, never the control. |
| T5 | Spike boundary | **Read-only review**: assignment list + review queue + submission detail + submission review/grade. **No** create/edit/publish in the portal (those stay on the Blazor Admin surface until migrated). |
| T6 | Auth mode | Dev bypass first (`FEATURE:DisableOIDCAuth=true`, TestAuth) keeps CI container-free; the role/policy wiring lands in the same change and activates when the flag flips. |
| T7 | Relationship to the Blazor compartments | **Both tracks run** (owner Q5 = A). `documents/specs/assignment-authoring-compartments.md` (Blazor) remains the **authoring destination**; the Prefab teacher portal is a **read/review second surface** for the spike and the eventual migration target for the rest. |

**Consequence — Q3 (“zero backend changes”) is revised for the teacher portal
only.** T2–T4 require backend work the ward portal does not:

- **Roles** — add `teacher` + `staff` to the realm import and ship the `roles`
  claim mapper. `PortalSessionAuthenticationHandler` already projects realm roles
  onto `ClaimTypes.Role`, so `RequireRole` resolves without new plumbing.
- **Policies** — one or more authorization policies (e.g. `require-teacher`,
  `require-staff`) applied to the assignment endpoint groups, conditional on the
  auth flag.
- **Scope** — a teacher-scope filter on the assignment list/review queries fed by
  a **new Assignments→Students cross-context port** over `TeacherGradeLevel`
  (existing Students queries `ListGradeLevelsForTeacher` / `ListTeacherGradeAssignments`,
  routes in `TeacherRoutes.cs`). `ListAssignmentsQueryHandler` does **not** filter
  by teacher today.

**Two prerequisites must land before `FEATURE:DisableOIDCAuth` flips** (recorded
2026-10-03, round `portal-teacher-surface`). Both block the same flip and neither is
visible from the flag's own value — the flag only decides which auth scheme is
registered:

- **Assign the roles (§1 T2, D11).** Declaring `teacher` + `staff` in the realm import
  assigns them to nobody, and the seeded `dev-teacher` carries attributes without a
  realm role, so the T4 policy would 403 the very users the flip is meant to admit.
  Assignment is manual, in the auth admin UI's realm-role screen
  (`documents/specs/keycloak-ui-auth-integration.md` §8, D11) — every existing
  assignment-reading user needs a role **before** the flip.
- **Give the portal a credential path.** The portal sends **no** credentials — no
  cookie, no bearer — which the dev bypass hides, because `TestAuthHandler`
  authenticates every request without one. With the flag OFF, all **six** reads this
  plan consumes answer **401** (`GET /assignments` · `GET /assignments/{id}` ·
  `GET /{id}/submissions` · `GET /{id}/submissions/review-queue` ·
  `GET /{id}/students/{studentId}/submission` · `GET /{id}/sign-off-statuses` — the
  set `stack/8-teacher-scope-auth` gates), and every portal surface renders its
  degraded card. Forwarding a bearer from the portal is the ar-24 delegation
  pattern and needs its own design round (the Phase 3 OIDC item below); until then
  the portal is a **dev-bypass-only** surface.

## 2. Findings — Prefab UI

Researched from the [PyPI page](https://pypi.org/project/prefab-ui/) (v0.20.2,
Jun 2026; web search engines were unreachable, so docs/GitHub links below are
indicative and should be re-verified):

- **What it is**: a Python DSL for composing UIs from 100+ prebuilt components
  (shadcn/ui-styled). Context managers express nesting; a reactive `Rx` class
  handles client-side state with no JavaScript.
- **How it renders**: the component tree compiles to a **JSON protocol** and is
  rendered by a **bundled React frontend** shipped inside the wheel — the
  interface definition stays in Python and is serializable/portable.
- **Backends**: REST and MCP backends supported out of the box; ships natively
  inside **FastMCP** (MCP Apps first-class). Python 3 wheels, ~1.9 MB.
- **Maturity risk**: 0.x, "under very active development", fast release cadence
  (0.1 → 0.20 in ~4 months). Pin the version; expect breaking changes.

### Why it fits School-Collab

- The portal UIs are **read-mostly, form/table surfaces** over existing REST
  endpoints (ward assignment lists/player, teacher dashboards) — exactly
  Prefab's dashboard/tool sweet spot.
- The JSON protocol + declarative Python makes the surfaces **agent-generatable**,
  aligning with the repo's AI/agent conventions.
- One Python app can host **both portals** (teacher + ward) as separate Prefab
  views, sharing an API client layer.

## 3. Findings — Aspire Python hosting

The AppHost (`src/AppHost/SchoolCollab.AppHost`, Aspire SDK **13.5.4**, net10.0)
does **not** include Python support in `Aspire.Hosting` today (verified: no
`AddPython*`/`PythonApp` symbols in the local `Aspire.Hosting.dll`). Python app
orchestration requires the `Aspire.Hosting.Python` package:

1. Add to `Directory.Packages.props` (CPM — version-pinned, 13.5.4 to match the
   other hosting integrations):
   ```xml
   <PackageVersion Include="Aspire.Hosting.Python" Version="13.5.4" />
   ```
2. `<PackageReference Include="Aspire.Hosting.Python" />` in
   `SchoolCollab.AppHost.csproj` (no `Version` — CPM).
3. Register in `Program.cs`, mirroring the `families` block (the app lives in its
   own module folder, so the relative path from the AppHost project is
   `..\..\SchoolCollab.Portals`):
   ```csharp
   builder.AddUvicornApp("portals", "..\\..\\SchoolCollab.Portals", "app:app")
       .WithUv()                       // uv-managed venv (uv.lock in app dir)
       .WithReference(assignmentsApi)  // Aspire service discovery → https://assignments-api
       .WaitFor(assignmentsApi);
   ```
   *As built in ar-23: the plan's `AddPythonApp` + `studentsApi` reference were
   superseded — `AddUvicornApp` ships the ASGI entrypoint, and the students-api
   reference belongs to the deferred teacher-portal phase.*
   The AppHost caters for hosting **solely** (Q5): endpoints, env vars, and
   health are owned by the AppHost; the Python entrypoint is the lightest thing
   prefab needs (its own server or bare ASGI) — pinned in the spike.

**API verified 2026-09-16** (aspire.dev Python integration reference + Microsoft
Learn API docs): `Aspire.Hosting.Python` is the **official** package since
Aspire 13 (the Community Toolkit variant is deprecated); `AddPythonApp(name,
appDirectory, scriptPath)` and `WithUv()` are real, non-obsolete APIs exactly as
used above — note `WithUv()` also overrides the automatic `WithPip()` default that
kicks in when a `pyproject.toml` exists, so keep it explicit. Service discovery
feeds Python simplified env vars (`SERVICENAME_HTTP`-style), matching the httpx
client layer. Two refinements from the same sources:

- **`AddUvicornApp("portals", dir, "app:app")`** is the better entrypoint if the
  prefab app serves as ASGI (it ships inside FastMCP, so likely) — it
  pre-configures the HTTP endpoint (`env:"PORT"`), `--reload` in dev, and the
  publish entrypoint.
- **`WithMcpServer("/mcp")`** (experimental, `ASPIREMCP001`) can be chained on the
  Python resource: prefab is FastMCP-native, so the portal's MCP tools would be
  discoverable through the Aspire MCP server (`aspire mcp tools` /
  `aspire mcp call`) — a natural fit for the repo's agent conventions and a
  candidate spike demo.

Also automatic (no code needed): OTLP env wiring for Python (the dashboard's
"live API call" gets tracing for free), VS Code zero-config Python debugging,
and a uv-aware production Dockerfile on publish (relevant to D-6 later). Pin the
exact 13.4.x patch version matching the AppHost SDK at spike time — docs
referenced 13.4.0/13.1.0, not 13.4.5.

Environment plumbing follows the repo standard: parameters live in the AppHost's
`appsettings.json` `Parameters:` block, fanned out via `WithEnvironment`, and get
mapped into `documents/configuration.md` §2.

## 4. Proposed architecture

```
AppHost (orchestrator)
├── postgres / rabbitmq / redis            (existing)
├── settings-api, students-api, assignments-api, families  (existing .NET)
└── portals  (NEW — Python, Prefab UI, uv; module folder `src/SchoolCollab.Portals/`)
     ├── consumes assignments-api ward endpoints (ward portal views)
     ├── consumes students/assignments APIs (teacher portal views)
     └── auth: pass-through — dev bypass first (Q2); OIDC deferred to Phase 3
```

Layout (**settled** — implemented; pattern, decisions and pitfalls in `documents/solution/portals-service-client-pattern.md`):

```
School-Collab/
└── src/
    └── SchoolCollab.Portals/     (NEW — own module folder, Python, Prefab UI, uv)
        ├── pyproject.toml / uv.lock  (deps: prefab-ui, httpx)
        ├── app.py                    (thin FastAPI app; entrypoint launched solely by AddUvicornApp)
        ├── api/                      (typed client + service discovery + DTOs + typed errors)
        ├── views/ward.py             (Prefab component trees — ward portal, MVP 1)
        ├── views/teacher.py          (Prefab component trees — teacher portal, MVP 2; a flat module per round-2 grill Q5)
        └── tests/                    (pytest + httpx.MockTransport — no server, no Docker)
```

### API contract (Q3 — zero backend changes for the ward portal; revised for the teacher portal)

- **Ward completion** (MVP 1): `GET /{studentId}/assignments`,
  `GET /{id}/students/{studentId}/modules`,
  `POST /{id}/students/{studentId}/modules/{moduleId}/progress`,
  `POST /{id}/students/{studentId}/submission` (Assignments API).
- **Teacher review** (spike, T5 read-only subset): `GET /{id}/submissions`,
  `GET /{id}/submissions/review-queue`,
  `GET /{id}/students/{studentId}/submission`,
  `POST /{id}/students/{studentId}/submission/review`.
  Unlike the ward endpoints these are **not** zero-change: they need the T2–T4
  roles, policies and teacher-scope filter; `review-queue` is already
  principal-first (`ar-24`). Publish/approve stay out of the spike.

**Architecture constraints honored:**
- No direct project references between bounded contexts — the Python app talks
  **only HTTP** via Aspire service discovery (`WithReference`), same as the Admin
  and Families Blazor hosts. It is a *consumer*, so MassTransit contracts are
  irrelevant to it.
- Operational data (assignments, students) is read/written through the existing
  APIs; the portal never touches Postgres directly (Direct-Tenancy pattern —
  ACL/permission work stays server-side in the APIs).
- Feature flags come only from the AppHost `Parameters:` → `WithEnvironment`
  fan-out, never a per-service appsettings value.

## 5. Phased plan

### Phase 0 — Spike (the committed deliverable; definition of done per Q6)
- Add `Aspire.Hosting.Python` (CPM + AppHost csproj).
- Scaffold `src/SchoolCollab.Portals/` with a one-page Prefab app; register with
  `AddPythonApp`; verify the Python resource appears in the Aspire dashboard
  with logs + endpoint, env-var injection works, and `WithReference` service
  discovery resolves `https://assignments-api`.
- Prove **one live API call** (e.g. `GET /{studentId}/assignments` against the
  seeded database) rendered as a Prefab view.
- Pin during the spike: exact `Aspire.Hosting.Python` 13.4.5 API variants
  (`WithUv` vs pip), prefab's minimal entrypoint, Python/uv versions.
- **Exit criterion**: dashboard shows the Python resource making one live API
  call. **Then re-decide** the ward-MVP go/no-go before any further work.

### Phase 1 — Ward portal MVP (contingent on Phase-0 re-decision)
- Assignment list → module progress ("mark watched") → submission, backed by
  the existing ward REST endpoints (§4). Dev-bypass auth (Q2): ward id in the
  route, mirroring the Families Blazor pattern.
- Test story: Playwright per `.github/copilot/rules/testing.md`.

### Phase 2 — Teacher review portal spike (2026-10-01 decisions T1–T7)
- **Read-only** teacher workspace in `views/teacher.py` (a **flat module** — round-2
  grill Q5, superseding T1's `views/teacher/` wording): assignment list (scoped per
  T3) → review queue (principal-first, ar-24) → submission detail → submission
  review/grade. No create/edit/publish in the spike (T5).
- **Auth/roles (T2, T4, T6)**: add `teacher` + `staff` realm roles, the
  assignment endpoint-group authorization policies (flag-conditional), and the
  teacher-scope filter fed by a new Assignments→Students `TeacherGradeLevel` port.
- **T7**: the Blazor compartment spec
  (`documents/specs/assignment-authoring-compartments.md`) remains the authoring
  destination; both tracks run.
- Test story: **pytest + `httpx.MockTransport` + FastAPI's `TestClient`** over the
  view/client/route layer (`src/SchoolCollab.Portals/tests/`), gating the portal in
  CI. *Correction (round `portal-teacher-surface`, 2026-10-03):* this line used to
  cite `.github/copilot/rules/testing.md` **for Playwright** — that rule covers the
  .NET stack only (MSTest on MTP, Moq, FluentAssertions, bUnit) and never mentions
  Playwright (verified). Playwright is **deferred**: it needs the full AppHost (the
  `*.Tests.Playwright` projects assume a running stack), which round-2 grill Q3
  rejected for this surface — the suite stays server-free, Docker-free and
  browser-free.

### Phase 3 — Auth & tenancy (partially pulled forward)
- The **role/policy** half of this phase is pulled into Phase 2 (§1 T2–T6).
- **OIDC flow** for the portal (token pass-through / mediation) remains deferred;
  tenant scoping stays server-side as today.

### Phase 4 — Decision review (deferred)
- Go/no-go vs extending the Blazor Families host; document in
  `documents/solution/`.

## 6. Risks

| Risk | Mitigation |
|---|---|
| Prefab is 0.x with fast breaking releases | Pin exact version in `pyproject.toml`; isolate API client + view layers |
| Python auth story (OIDC) is unproven here | Deferred to Phase 3; dev bypass flag keeps Phases 0–2 unblocked |
| No bUnit equivalent | Playwright tests per `.github/copilot/rules/testing.md` conventions |
| `Aspire.Hosting.Python` API surface (verified 2026-09-16 against aspire.dev + Learn docs; exact patch version still to pin at spike time) | Phase 0 spike pins the version and settles the ASGI-vs-script entrypoint choice (`AddUvicornApp` vs `AddPythonApp`) |
| Dual-stack surface drift (Blazor Families vs Prefab portal) | Phase 4 decision review; keep ward REST contract as single source of truth |
| Teacher scope depends on cross-context data (`TeacherGradeLevel`) | New Assignments→Students port; scope filter is fail-closed and server-side (T3/T4); spike stays read-only until it lands |

## 7. Open questions → resolved / remaining

Resolved by the grill-me session (see §1 decision table): repo placement,
auth model, API contract fit, MVP order, serving stack, definition of done.

**Remaining flags (all post-spike):**

1. **Spike-success criteria for the ward MVP go/no-go** — define after Phase 0,
   before any MVP work (prefab reactivity/form/table ergonomics is the thing
   being evaluated).
2. **Teacher portal — remaining after the 2026-10-01 decisions (T1–T7):** the
   spike-success criteria for the teacher workspace, OIDC (still deferred; the
   role/policy half moved to Phase 2), CI for the `src/SchoolCollab.Portals/`
   folder, and the migration order for the remaining assignment
   create/edit/publish features once the read-only review spike is accepted.
