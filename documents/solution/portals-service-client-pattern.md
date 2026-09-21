# Portals — isolating service calls behind typed clients

> Status: **implemented** (2026-09-21, branch `refactor/portals-service-client`, stacked on the portal-relocation PR).
> Related: `documents/specs/teachers-ward-portal-prefab-plan.md` (§4 layout, risk row
> "isolate API client + view layers"), `documents/configuration.md` §1 (the `portals` resource),
> `src/SchoolCollab.Portals/`.

## Problem

The Phase-0 spike put everything in one module: discovery, HTTP calls, Prefab
component trees, and routes all lived in `src/SchoolCollab.Portals/app.py`, with a
module-level mutable `_last_fetch` for diagnostics. That is fine for a spike, but:

- transport and presentation churn for different reasons — **prefab-ui is 0.x**
  (0.1 → 0.20 in four months), so a component rename should not touch HTTP code;
- there was **no seam for testing** — every test would have needed a live API
  (and therefore Docker);
- the ad-hoc payload handling (`payload if isinstance(payload, list) else
  payload.get("items", [])`) and `raise_for_status()` hid real failure classes,
  including one this repo already fixed on the .NET side.

## Findings

| Finding | Evidence |
|---|---|
| The .NET hosts already use one typed client per bounded context | `FamiliesApiClient` (11 methods: `ListWardAssignmentsAsync`, `GetWardAssignmentViewAsync`, `ReportModuleProgressAsync`, `SubmitAsync`, …) and `AssignmentsApiClient` (`ListAsync`, `CreateAsync`, `PublishAsync`, `ApproveAsync`, …) — `sealed class`, ctor-injected `HttpClient`, DTO records |
| The prefab plan already prescribes the target layout | plan §4: `api/` ("typed httpx clients over the existing REST APIs"), `views/ward/`, `views/teacher/`; §6 risk row requires the isolation |
| Aspire injects the **.NET-style** discovery key | spike finding (ar-23): `services__assignments-api__http__0` resolved; the plan's assumed simplified `ASSIGNMENTS_API_HTTP` form was **absent** at `Aspire.Hosting.Python` 13.4.5. The migration left both names checked, in that order |
| A 2xx is not proof of data | ar-24 closed the same fail-open class in .NET: `TeacherDirectoryHttpClient` followed an OIDC 302 challenge to a **200 HTML login page** and counted it as "teacher exists" (fixed with `AllowAutoRedirect=false`). In Python the equivalent guard is asserting the `content-type`, not just `raise_for_status()` |
| The dev database legitimately answers `[]` | ar-21 AC5 / ar-23 note: zero assignment rows. An empty result is a **success**, not an error |

## Decision — shape

```
src/SchoolCollab.Portals/
├── app.py                        # thin: FastAPI app, lifespan, Depends, 2 routes
├── api/
│   ├── errors.py                 # PortalApiError / ServiceDiscoveryError / ApiUnavailableError / ApiResponseError
│   ├── service_discovery.py      # the Aspire ".NET-ism" quarantined: env var -> ServiceEndpoint(base_url, env_var)
│   ├── dto.py                    # AssignmentRow (frozen dataclass, tolerant from_payload + as_table_row)
│   └── assignments_api_client.py # class AssignmentsApiClient — one method per endpoint
└── views/
    └── ward.py                   # build_ward_view(...) / build_error_view(...) -> PrefabApp
```

Rules the shape encodes:

1. **One client class per bounded context**, named `{Context}ApiClient`, one method
   per endpoint named after the .NET client's methods (`list_assignments`, and next
   `list_ward_assignments`, `report_module_progress`, …). Adding an endpoint is one
   method; the docstring shows the template.
2. **The client is base-URL agnostic** — it is *handed* a `ServiceEndpoint` instead of
   reading the environment, so it can be driven by any transport (`httpx.MockTransport`
   in tests) and `ServiceEndpoint` remains the single source of endpoint diagnostics.
3. **Views are pure functions**: DTOs + endpoint metadata in, `PrefabApp` out. No HTTP,
   no `os.environ`.
4. **Typed errors, one place**: the client raises `ApiUnavailableError` (transport) or
   `ApiResponseError` (status / content-type / shape); routes catch `PortalApiError`
   and render an **error card with HTTP 200** — a page, never a raw traceback. Service
   discovery failures are mapped the same way by an app-level exception handler.
5. **No module-level mutable state**: `PortalState` (http client, endpoint, last fetch,
   discovery error) lives on `app.state.portal`, created in the FastAPI lifespan.
6. **`Depends(get_assignments_client)`** is the injection seam: one pooled client per
   process, overridable in tests via `app.dependency_overrides`.

### Decisions taken (and the alternatives rejected)

| Decision | Taken | Rejected because |
|---|---|---|
| HTTP style | sync `httpx.Client` (pooled, lifespan-owned) | `AsyncClient` + `async def` routes is the natural follow-up, but it would drag `pytest-asyncio`/`anyio` in for the first tests; the sync client keeps the suite plain pytest. FastAPI runs sync routes in a threadpool, which is fine at portal scale. **Deferred, not rejected on merit.** |
| Discovery placement | outside the client (`service_discovery.py`) | inside the client would make it untestable without env monkeypatching, and would hide the resolved env var from `/health` |
| Row shape | frozen dataclasses parsed in the client | raw `dict` (spike) forces every view to guess keys and pushes shape drift to the UI |
| Error surface | typed errors + error card | `raise_for_status()` alone produced a raw 500 page for an unreachable API |

## Implementation steps

1. `api/errors.py`, `api/service_discovery.py`, `api/dto.py`, `api/assignments_api_client.py`,
   `views/ward.py` added; `api/__init__.py` / `views/__init__.py` re-export the public surface.
2. `app.py` reduced to the app object, lifespan, one dependency, and the `GET /` + `GET /health`
   routes. `/health` keeps its ar-23 contract keys (`api_base_url`, `api_env_var`,
   `last_fetch{status,row_count,base_url,env_var}`) and adds `discovery_error`.
3. `pyproject.toml`: `[dependency-groups] dev = ["pytest"]` plus
   `[tool.pytest.ini_options] pythonpath = ["."], testpaths = ["tests"]`.
4. `tests/`: `test_service_discovery.py`, `test_assignments_api_client.py`,
   `test_app_routes.py` — 14 tests, all container-free.
5. `.gitignore`: `.pytest_cache/` (`.venv/` was already ignored).

## Verification

| Check | Result |
|---|---|
| `uv run pytest -q` | **14 passed** (discovery keys; URL + parse; empty-is-success; envelope + non-object rows; HTML content-type rejection; HTTP 500 detail; transport failure; route renders rows via an overridden dependency; route renders an error card for unreachable API **and** for failed discovery; `/health` ok + degraded) |
| `uv run python -c "import app"` | imports cleanly (no environment needed at import time) |
| Real ASGI entrypoint, no Docker | `env "services__assignments-api__http__0=http://127.0.0.1:9" uv run uvicorn app:app --port 5310` → `GET /health` **200** reporting the resolved endpoint; `GET /` **200** rendering the degraded card (`ApiUnavailableError`, server log: `ward view degraded: … actively refused it`) |
| Behavioural parity with the spike | same routes, same `/health` keys, same Prefab table columns; the only intentional changes are the degraded-state page (was a 500) and the content-type guard |

## Pitfalls worth remembering

- **Hyphenated environment-variable names cannot use the shell's `VAR=value cmd`
  form** — `services__assignments-api__http__0=…` makes bash look for a command of
  that name. Use `env "services__…=…" cmd` (already recorded for the MigrationService
  recipe).
- **`pythonpath = ["."]` is required** for `import api` / `import views` under pytest,
  because the project is a uv *virtual* project (no build backend), so nothing installs
  the package.
- **`httpx.MockTransport` must be given an explicit `content-type`** when testing
  rejection: `httpx.Response(200, text="...")` defaults to `text/plain`, so the HTML-login-page
  case has to set `headers={"content-type": "text/html"}` to model the real challenge.
- **Never kill processes with a filter string that also appears in the killing shell's
  own command line** — the filter matched nine processes (including the shell) and killed
  the tool call. Kill by PID read from `netstat`, or filter on `Name` only.

## Not done (deliberately)

- `AsyncClient` conversion (see the decision table) — mechanical, worth doing when the
  MVP adds mutating endpoints.
- The remaining MVP endpoints (`list_ward_assignments`, `report_module_progress`,
  `submit`, the module-progress POST) — one method each, per the template in the client
  docstring. They arrive with the MVP, not speculatively now.
- Richer degraded-state styling (prefab `Alert`) — only the two container-proven badge
  variants (`default`, `success`) are used so far.
- A CI story for `portals/` (no workflow runs these tests yet) and Playwright — both are
  post-re-decide per the plan.
