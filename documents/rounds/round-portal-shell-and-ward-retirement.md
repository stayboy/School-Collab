# Round `portal-shell-and-ward-retirement` — retire the ward page; add the shared page shell

- **Status: IN PROGRESS — Tier 2 light round, started 2026-10-06.** Provider: pi (session on `ollama-cloud`); worker `ollama-cloud/deepseek-v4.1-flash` (one run); reviewer `ollama-cloud/kimi-k2.7-code` (one static diff-only run); the parent authors the plan and transcribes the acceptance (Tiers 1–2). No orchestrator run, no UI tester — a Light round by owner decision.
- **Base:** `git rev-parse HEAD` = **`a1c6de53`** — the tip of the **unmerged** `feat/portal-submission-grade` layer (pushed, no PR yet), which this working tree sits on; `main` is `b99ae46d`. *Corrected at freeze time:* this line first recorded `b99ae46d` (`main`), taken from an earlier turn's `git branch --show-current` **before** that round's commit created the branch. The error was caught mechanically, not by review — the first freeze listed **14** files, including the prior round's `api/*` and `tests/test_teacher_grade.py` — and the base became `HEAD` before any reviewer was dispatched. **The tree is dirty at round start** with two *unrelated* change sets: the ward-decision record pass (`documents/configuration.md` M, `documents/specs/teachers-ward-portal-prefab-plan.md` M, `documents/solution/ward-surface-decision.md` ??) and a pre-existing `AGENTS.md` (M). The worker must **not** touch those files, and the frozen patch is therefore path-restricted to the round's own scope (see "Patch isolation").
- **Source of truth:** `documents/solution/ward-surface-decision.md` (the decision this round implements) + `documents/specs/teachers-ward-portal-prefab-plan.md` §7 items 3–4 (the two workstreams) + the owner's plan-review acceptance of a `views/shell.py` shell that reproduces Blazor `MainLayout`'s **outcome, not its mechanism**.
- **Out of scope (binding):** `src/SchoolCollab.Portals/api/**` (no client edits — report orphaned methods instead), the AppHost, `documents/**` (the parent owns doc updates), any new dependency, and any theme/`mode` work on `PrefabApp` (a named follow-up, not this round).

## Why these two workstreams share one round

They move the same two coupled artifacts in opposite directions: `views/ward.py` leaves the `.slnx` + the guard's pinned array, and `views/shell.py` joins them. `PortalsSolutionItemsArchitectureTests` proves disk ⊆ solution (`EveryPortalFile_IsListedInTheSolution`), solution ⊆ disk (`EveryPortalSolutionItem_ExistsOnDisk`) **and** pins the exact array (`PortalSolutionItems_AreTheReviewedSet`) — so both changes must land in one change set or that guard reddens either way.

## Scope — expected files

| # | File | Change |
|---|---|---|
| 1 | `src/SchoolCollab.Portals/views/shell.py` | **NEW** — the shared chrome composition function |
| 2 | `src/SchoolCollab.Portals/views/ward.py` | **DELETED** |
| 3 | `src/SchoolCollab.Portals/views/__init__.py` | drop the ward exports; export `shell` |
| 4 | `src/SchoolCollab.Portals/views/teacher.py` | all five page builders go through the shell |
| 5 | `src/SchoolCollab.Portals/app.py` | `/` becomes a redirect; ward import/route/comments/docstring removed; discovery-fallback loses its ward branch |
| 6 | `src/SchoolCollab.Portals/tests/test_app_routes.py` | 1 ward test deleted, 2 retargeted to the teacher surface, 1 new redirect test |
| 7 | `src/SchoolCollab.Portals/tests/test_teacher_session.py` | the 2 D19-scope ward tests retargeted to `/` |
| 8 | `src/SchoolCollab.Portals/tests/test_views_shell.py` | **NEW** — pins the shared chrome |
| 9 | `SchoolCollab.slnx` | `views/ward.py` row out, `views/shell.py` + `tests/test_views_shell.py` rows in |
| 10 | `tests/SchoolCollab.ArchitectureTests.Unit/PortalsSolutionItemsArchitectureTests.cs` | the pinned array (18 → 19 entries) + its "ward portal's solution items" docstring |

## Plan (implementable standalone)

### Workstream A — retire the ward page

**A1. Delete `src/SchoolCollab.Portals/views/ward.py`.** Its two builders (`build_ward_view`, `build_error_view`) and `_TABLE_COLUMNS` have no other consumer once A2/A5 land.

**A2. `views/__init__.py`** — remove `from views.ward import build_error_view, build_ward_view`, add `shell` to the package's exports (it is the shared surface, imported as `from views.shell import shell` by its single consumer — see B2 — while the package also re-exports it for consistency with the builders), and drop `build_error_view` / `build_ward_view` from `__all__`.

**A3. `app.py` — the root route becomes a redirect.** Replace the whole `@app.get("/", response_class=HTMLResponse)` / `async def ward_view(...)` block with:

```python
@app.get("/", include_in_schema=False)
async def root() -> RedirectResponse:
    """The portal's entry point: the teacher surface is now the only one it serves."""
    return RedirectResponse(url="/teacher", status_code=302)
```

with `from fastapi.responses import RedirectResponse` added to the imports. The redirect must **not** resolve `get_assignments_client` — no API call, so no service-discovery failure is reachable on `/`.

**A4. `app.py` — remove the ward import** (`from views.ward import build_error_view, build_ward_view`).

**A5. `app.py` — the discovery-failure fallback.** In `on_discovery_error`, the `surface is None` tail currently renders the deleted ward error view. Make it render the teacher degraded page with an honest, non-invented label — `build_teacher_error_view(error=error, endpoint=state.endpoint, surface="portal")` — and reword its log line and `_teacher_surface_for`'s docstring, which today documents `None` as "the ward page". Keep the branch reachable-but-honest; do not invent a new surface name beyond `"portal"`.

**A6. `app.py` — purge the ward prose.** The module docstring ("the ward and teacher surfaces", the `GET /` route line) and the two comments that reference "the ward route" (near the session-gate rules) must describe the teacher-only portal. After this round `git grep -in ward -- src/SchoolCollab.Portals` must return **nothing**.

**A7. Tests.**
- `test_app_routes.py`: **delete** `test_ward_view_renders_rows_through_the_injected_client` (ward-specific). **Retarget** `test_ward_view_renders_an_error_card_when_the_api_is_unreachable` and `…_when_discovery_fails` to the teacher surface — same body, `client.get("/teacher", follow_redirects=False)`, renamed `test_teacher_list_…`. If `test_teacher_views.py` already covers either case, retarget-or-drop rather than duplicate, and say which in the report.
- `test_app_routes.py`: **add** `test_root_redirects_to_the_teacher_list` — `response = client.get("/", follow_redirects=False)`; assert `status_code == 302` and `response.headers["location"] == "/teacher"`.
- `test_teacher_session.py`: retarget the two D19-scope tests (`test_the_ward_route_presents_no_session_header`, `test_the_ward_route_is_never_gated`) to `/` with `follow_redirects=False`, keeping both assertions' *intent* — an unauthenticated path still exists, it carries no `X-Portal-Session`, and the gate does not act on it. These two are the only witnesses that D19's gate rules are scoped to the teacher surface; do not simply delete them.

### Workstream B — the shared page shell

**B1. New `src/SchoolCollab.Portals/views/shell.py`.** A context manager that owns the portal's document + chrome layer, so `PrefabApp` is constructed **exactly once** in the whole `views/` package:

```python
@contextmanager
def shell(
    *,
    title: str,
    heading: str | None = None,
    state: Mapping[str, Any] | None = None,
    js_actions: Mapping[str, str] | None = None,
) -> Iterator[PrefabApp]:
    """The portal's shared page chrome: document title, page title, brand bar.

    The Prefab analogue of Blazor's ``MainLayout`` **outcome** (one chrome for every page),
    not its mechanism: Prefab has no router, no outlet and no ``@layout`` selection, so the
    shared layout is a Python composition function every page calls.
    """
```

It renders `PrefabApp(title=title, css_class="p-6")` around a `Column(gap=4)` whose first child is the brand bar — the existing `H3(heading or title)` — then yields the app so the caller's children render inside the same column. Keep the chrome minimal and static: **brand + page title only**.

**Plan defect, found by the worker mid-round and corrected here (owner decision (a), recorded):** this section first asserted that `title` + `css_class` were "the exact document attributes every page carries today". That is **false for `build_submission_view`**, which also passes `state={GRADE_ATTEMPTED_STATE: False}` and `js_actions={NAVIGATE_HANDLER: NAVIGATE_HANDLER_JS}` (the grade form's reactive attempted-state and its navigate handler). The worker stopped and asked rather than guessing; the owner's ruling widened the shell with the two keyword-only pass-throughs above — forwarded straight into `PrefabApp(...)` and **omitted entirely when `None`**, so the other four pages keep a byte-identical document — and explicitly forbade a blanket `**kwargs`/fields pass-through (it would make the shell's contract unreviewable for a two-field need). The alternative, assigning `application.state` after construction, was rejected as a post-construction side-channel on a pydantic model that moves document configuration outside the one place that now owns the document. Do not move the per-page session affordances (`_session_affordances`) or the breadcrumb `Link`s into it; they need per-page `session`/`links` data, and moving them is a larger refactor with no acceptance value.

**B2. `views/teacher.py` — all five builders use it.** `build_teacher_list_view`, `build_review_queue_view`, `build_submission_view`, `build_teacher_session_card`, `build_teacher_error_view` each replace their `with PrefabApp(...) as application:` / `with Column(gap=4):` / leading `H3(...)` with `with shell(title=…, heading=…) as application:` and keep every other child **byte-identical**.

**The string-preservation rule (load-bearing):** each page's existing visible strings must not change. Where a page's `H3` text differs from its document title — `build_teacher_session_card` renders `H3("Teacher portal")` while its title is `f"Teacher portal — {title}"` — pass `heading=` explicitly so the rendered text stays exactly as before. Verify this per page and report any other divergence you find. **Outcome: two pages diverge, not one** — the worker also found `build_submission_view` (`H3("Teacher portal — submission detail")` vs title `"Teacher portal — submission"`). Both pass `heading=`; the other three match their titles and omit it.

**B3. New `tests/test_views_shell.py`.** Pin the shell's two halves: (a) the shared chrome appears on more than one page (assert the brand/heading text is present in two different rendered pages), and (b) the refactor is real — read `views/teacher.py` and assert it contains **no** `PrefabApp(` occurrence, so a future page cannot quietly hand-roll its own app again (a source-inspection assertion, in the spirit of `PortalsSolutionItemsArchitectureTests`).

### Patch isolation

The frozen patch is written path-restricted, because the tree carries the unrelated record pass:

```
git add -N src/SchoolCollab.Portals/views/shell.py src/SchoolCollab.Portals/tests/test_views_shell.py
git diff HEAD -- src/SchoolCollab.Portals SchoolCollab.slnx \
    tests/SchoolCollab.ArchitectureTests.Unit/PortalsSolutionItemsArchitectureTests.cs
```

The two new files carry `git add -N` intent entries so they appear in that diff. **Frozen result: 1040 lines / 10 file headers — exactly the round's 10 expected files, `views/ward.py` as `D`.** A leak check confirms that neither the record pass (`configuration.md`, the plan, the solution doc) nor the prior round (`api/**`, `tests/test_teacher_grade.py`) is in it: because the base is the **layer tip**, the patch carries only this round's delta even though the tree holds two unrelated change sets.

## Verification (the worker runs all of these)

1. `cd src/SchoolCollab.Portals && uv run pytest -q` — record the total (84 before this round; it should end ≥84: −3 ward tests, +1 redirect test, +retargeted/new shell tests).
2. `dotnet test tests/SchoolCollab.ArchitectureTests.Unit` — **93/0** expected; the pinned array must be updated to land there.
3. `dotnet build SchoolCollab.slnx` — 0 errors (the round's only .NET change is a test file).
4. `git grep -in ward -- src/SchoolCollab.Portals` → empty.
5. `git grep -c 'PrefabApp(' -- src/SchoolCollab.Portals/views/teacher.py` → `0`; the same over `src/SchoolCollab.Portals/views/` → exactly one hit, in `shell.py`.
6. `git status --short` — exactly the expected files touched; **no `documents/**` and no `api/**` change**.

**Test-output discipline (hard gate):** exactly one command per suite — `uv run pytest -q 2>&1 | tail -5` and `dotnet test tests/<X> 2>&1 | grep -E "passed|failed:|total:" | head -10`; no ad-hoc pipelines, no debug log files, at most 2 attempts per result read. Repo-scoped searches only (never a filesystem-wide scan).

## Acceptance criteria (Tier 2 — parent-authored; the reviewer verifies the diff against these)

| # | AC | Discriminating? |
|---|---|---|
| AC1 | `views/ward.py` is gone, its `.slnx` row is gone, and no **scope-eligible** file references the retired surface. *(Amended after the worker's report: the original “the grep is empty” is unattainable inside this round's own fence — `api/**` prose (11 hits, incl. a docstring example) and `pyproject.toml`'s `description` (1) are out of scope, `test_teacher_views.py` (3) must stay unchanged per AC4, plus incidental `forward`/`onward` substrings (5), round-name traceability (3), and the grade form's field description `"Feedback for the ward"` (1).)* | yes (pre-fix: a file plus 9 references in it) |
| AC2 | `GET /` → **302**, `location: /teacher`, and resolves no assignments client (no API call) | yes (pre-fix: 200 HTML ward page) |
| AC3 | `PrefabApp(` appears exactly once under `views/`, in `shell.py` | yes (pre-fix: 7 sites) |
| AC4 | **`test_teacher_views.py` and `test_teacher_grade.py` pass with zero source changes** — the shell preserved every page's visible strings | yes (a string/структура change reddens them) |
| AC5 | The degraded path still renders an HTML page at HTTP 200 on the teacher surface (retargeted tests) | yes |
| AC6 | `views/__init__.py` exports no ward symbols; `shell` is exported; the package imports cleanly | yes |
| AC7 | `PortalsSolutionItemsArchitectureTests` **93/0** with the array updated (−`views/ward.py`, +`views/shell.py`, +`tests/test_views_shell.py`) | yes |
| AC8 | Portal pytest total recorded and green; the two D19-scope witnesses now target `/` and still assert no session header + no gating | yes |
| AC9 | No `api/**` change; any orphaned client method is **reported**, not deleted | yes |
| AC10 | No `documents/**` change from the worker; `app.py` prose is teacher-only | yes |

## Constraints (binding)

- **Never edit the round doc** (`documents/rounds/round-portal-shell-and-ward-retirement.md`) or anything under `documents/`. The parent persists your report and owns every doc update.
- No new dependency; no `pyproject.toml`/`uv.lock` change; no `.razor`/`.cs` change beyond the one test file named in row 10.
- Do not delete `AssignmentsApiClient` methods or their client tests if the ward route was their only caller — report the orphan instead (AC9).
- 30-minute cap; at the cap, stop and report what is done and what remains. **STOP and report** (no workarounds) if the pytest suite reddens in a way you cannot fix within the plan's scope, or if `uv run` fails.
- Report file: `documents/rounds/.portal-shell-ward-worker-report.md` is **not** permitted (no `documents/**` writes) — return the WORKER REPORT inline instead.

## Worker Report

**Status: DONE** (transcribed by the parent from the worker's inline report). Worker `ollama-cloud/deepseek-v4.1-flash`, one run, Tier 2 — one supervisor round-trip, for the plan defect corrected above. All 10 expected files, nothing else touched: no `api/**`, no `documents/**`, no `pyproject.toml`/`uv.lock` change.

| File | Change |
|---|---|
| `views/shell.py` | **NEW** — the one place a page document is assembled (`PrefabApp(**document)` → `Column(gap=4)` → `H3(heading or title)`), then yields the app |
| `views/ward.py` | **DELETED** |
| `views/__init__.py` | ward imports/`__all__` entries out; `shell` re-exported |
| `views/teacher.py` | all five builders `with shell(...)`; their `Column`/`H3` lines gone, every other child unchanged; construction-time `PrefabApp`/`H3` imports dropped (annotation-only `PrefabApp` import kept for the `-> PrefabApp` signatures) |
| `app.py` | `/` → `RedirectResponse("/teacher", 302)` with `include_in_schema=False`; ward import/route/log lines gone; discovery fallback → `build_teacher_error_view(..., surface="portal")`; docstring, gate comments and `_teacher_surface_for` prose are teacher-only |
| `tests/test_app_routes.py` | ward rows test deleted; 2 degraded tests retargeted to `/teacher`; `test_root_redirects_to_the_teacher_list` added; dead `_stub_override`/`STUB_ENDPOINT` + imports removed; module docstring re-framed |
| `tests/test_teacher_session.py` | the 2 D19-scope witnesses retargeted to `/` and renamed off "ward" |
| `tests/test_views_shell.py` | **NEW** — shared-chrome witness (2 pages), the source-inspection "no page hand-rolls `PrefabApp`", and a pass-through witness |
| `SchoolCollab.slnx` | `views/ward.py` out; `views/shell.py` + `tests/test_views_shell.py` in |
| `PortalsSolutionItemsArchitectureTests.cs` | pinned array 18 → 19 (−`views/ward.py`, +`views/shell.py`, +`tests/test_views_shell.py`); docstring "ward portal's solution items" → "teacher portal's" |

**Worker verification (steps 1–6):** pytest **87 passed** (baseline 84: −1 deleted ward test, +1 redirect, +3 shell); `ArchitectureTests` **93/0**; build **0 errors** (79 warnings); `views/teacher.py` has **0** `PrefabApp(`, `views/` has exactly **1** (in `shell.py`, the AC3 pair); `api/**` untouched, no orphaned client method (AC9 — all four real client methods keep callers; `list_ward_assignments` was only a docstring example).

**Deviations (all accepted):** (1) the B1 signature widening — the plan defect above; (2) a third test in `test_views_shell.py` pinning the `state`/`js_actions` pass-through, because nothing else asserted the submission page's `grade_attempted` state or its navigate handler — the refactor's riskiest line was otherwise unwitnessed; (3) removal of the now-dead `_stub_override`/`STUB_ENDPOINT` (and their imports) from `test_app_routes.py`, whose sole consumer was the deleted ward test, plus a re-framed module docstring that had claimed a stubbed transport it no longer has; (4) the `(see documents/specs/… Q5)` citation dropped from `app.py`'s docstring — A6's purge cannot coexist with a filename containing "ward"; (5) retargeted-test docstrings say "the retired portal-root page" rather than "ward page" to avoid gratuitous grep hits.

**Parent adjudication of the residuals:**

- **AC1 amended** (see the table above) — “the grep is empty” was this plan's error, unattainable inside the round's own fence.
- **A5's `surface="portal"` fallback is unreachable by any route** (every client-depending route is a teacher route). Kept rather than deleted: the alternative is an unhandled `ServiceDiscoveryError` → 500. Recorded as deliberately defensive and untested by construction.
- **The two retargeted degraded tests stay**: they are *route-level* (`TestClient` + dependency override) while the tests the worker flagged as overlapping (`test_teacher_views.py::test_teacher_list_degrades_when_the_api_is_unreachable`, `…::test_missing_service_discovery_names_the_requested_surface[list]`) are *view-level* (pure functions). Different layers — and the route layer is what this round changed.
- **Follow-ups (out of scope, named so they are not lost):** stale ward prose survives in `api/**` (a docstring example) and in `pyproject.toml`'s `description` ("Phase-0 prefab-UI spike: ward portal surface…"). `test_teacher_views.py`'s `assert "Ward portal" not in response.text` is **correct as-is** — a teacher page must not render a ward title — and stays.
- **Honest note:** after its green suite run the worker made docstring-only edits and re-added the annotation-only `PrefabApp` import, so the frozen tree is *not* exactly the tested tree. It respected the one-command-per-suite gate and said so; the parent's authoritative pass below re-runs the frozen tree.
- **Parent's authoritative pass on the frozen tree** (base `a1c6de53` + the round's delta): portal pytest **87 passed**; `dotnet build SchoolCollab.slnx` **0 errors**; `ArchitectureTests` **93/0** (the `.slnx` ↔ guard coupling lands). Recorded here as well as in `## Acceptance`, because the round doc is this round's only durable copy.

## Review

**Static diff-only reviewer** — `ollama-cloud/kimi-k2.7-code`, one run (Tier 2), reading the plan of record plus `documents/rounds/diffs-portal-shell-and-ward-retirement.patch`. Static by design: it built and ran nothing (any numbers it volunteered would be discarded — the parent is the only build/test authority).

**P1: none. Verdict: `Round can be accepted: yes`.**

What it verified, per area: **plan conformance** (exactly the 10 expected file headers in the patch, and the unrelated dirty-tree files — `AGENTS.md`, `documents/configuration.md`, `documents/specs/teachers-ward-portal-prefab-plan.md` — absent from it); **AC1–AC10**, each walked, including the two discriminating ones (**AC3**: exactly one `PrefabApp(` site under `views/`, at `views/shell.py:56`; **AC4**: all five builders route through `shell()` with visible strings preserved — two of them passing `heading=` where the title and the `H3` diverge, the other three using the title); **the shell's document construction** (the four pages passing neither `state` nor `js_actions` get PrefabApp's own defaults; `build_submission_view`'s pair is forwarded untouched); **the redirect** (`RedirectResponse` imported, `/` resolves no client, and every redirect-aware test uses `follow_redirects=False`); and **non-vacuity of the retargeted D19 witnesses** (they assert the redirect target and/or that no assignments call was made). All four parent-adjudicated residuals — the `state`/`js_actions` widening, the defensive `surface="portal"` fallback, keeping the route-level degraded tests, and the amended AC1 wording — were judged soundly handled.

**P2 findings (report-only):**

| P2 | Disposition |
|---|---|
| `src/SchoolCollab.Portals/views/teacher.py:49` — `PrefabApp` is imported only for the builders' return annotations while the module carries `from __future__ import annotations`, so the runtime import is unnecessary; guard it with `typing.TYPE_CHECKING` | **FIXED pre-commit** by the parent |
| `src/SchoolCollab.Portals/tests/test_app_routes.py:15` — a top-level function separated from its imports by zero blank lines (PEP 8 E302), introduced when the worker removed the stub helpers | **FIXED pre-commit** by the parent |

Both fixes are parent-applied tiny rework: the skill allows a parent scope-check instead of a second reviewer run for a rework diff this small, and the parent's post-fix authoritative pass is recorded in `## Acceptance` below.

## Acceptance

**Verdict: CLOSED — no open P1, no open P2.** Tier-2 acceptance transcribed by the parent (the Tiers 1–2 shape) on the reviewer's verdict plus the parent's own authoritative pass. Loop bounds respected: zero reviewer-P1 iterations (there were none to iterate on); the two P2s were fixed in one parent pass.

| # | AC | Verdict | Basis |
|---|---|---|---|
| AC1 | the ward surface is retired and no scope-eligible file references it | **PASS** | `views/ward.py` `D` in the patch; `.slnx` row and guard-array entry gone; the reviewer walked AC1 in its amended wording |
| AC2 | `/` → **302** `/teacher`, resolving no client | **PASS** | `RedirectResponse` imported; no `get_assignments_client` dependency; `test_root_redirects_to_the_teacher_list` asserts 302 + `location` with `follow_redirects=False` |
| AC3 | `PrefabApp(` exactly once under `views/` | **PASS** | parent re-check on the final tree: `views/teacher.py` **0** hits, `views/` exactly one at `views/shell.py:56` (`PrefabApp(**document)`) |
| AC4 | all five builders through the shell, visible strings preserved, `test_teacher_views.py` + `test_teacher_grade.py` unmodified | **PASS** | reviewer read every builder and confirmed string preservation (two pass `heading=`, three use the title); both suites are untouched in the patch and green |
| AC5 | the degraded path still renders an HTML page at 200 on the teacher surface | **PASS** | the two retargeted route-level tests assert 200 on `/teacher` |
| AC6 | `views/__init__.py` exports are clean | **PASS** | no ward symbols; `shell` exported; package imports cleanly |
| AC7 | `.slnx` ↔ disk ↔ guard: the reviewed set is correct | **PASS** | array 18 → 19; `ArchitectureTests` **93/0** |
| AC8 | portal pytest green; the two D19-scope witnesses retargeted and non-vacuous | **PASS** | **87 passed**; reviewer judged both witnesses non-vacuous (they assert the redirect target and/or that no assignments call was made) |
| AC9 | no `api/**` change; no orphaned client method | **PASS** | patch has no `api/**` file; all four real client methods keep callers |
| AC10 | no worker `documents/**` change; `app.py` prose is teacher-only | **PASS** | the worker touched no doc; `app.py`'s docstring, route list, gate comments and `_teacher_surface_for` prose are teacher-only |

**Parent's authoritative pass on the final (post-P2-fix) tree — base `a1c6de53` + the round's delta:**

| Check | Result |
|---|---|
| `uv run pytest -q` (`src/SchoolCollab.Portals`) | **87 passed**, 2 warnings, 3.70 s (baseline 84: −1 deleted ward test, +1 redirect, +3 shell) |
| `dotnet build SchoolCollab.slnx` | **0 errors** (79 warnings) |
| `dotnet test tests/SchoolCollab.ArchitectureTests.Unit` | **93 / 0** — including the spec-reading guards, re-run after the parent's plan edits |
| Frozen patch, re-frozen post-fix | **1054 lines / 10 file headers** |

**The two P2 fixes (parent-applied, in the frozen patch):** `views/teacher.py` now imports `PrefabApp` under `if TYPE_CHECKING:` (the module's `from __future__ import annotations` makes the builders' return annotation lazy, so the runtime import was unnecessary), and `tests/test_app_routes.py` regained the blank line PEP 8 E302 wants between its imports and its first test. Both were re-verified by the parent's pass above; no second reviewer run was bought for a four-line rework — the skill sanctions a parent scope-check for a rework diff this small, and that choice is recorded rather than implied.

**Residuals carried (none blocking):**

1. **Stale ward prose outside the round's fence** — `api/**` (a client docstring example) and `pyproject.toml`'s `description` ("Phase-0 prefab-UI spike: ward portal surface…"). Deliberate: the plan fenced `api/**` and dependency metadata out of this round. Named in the plan's §7 item 4 so they are not lost.
2. **The defensive `surface="portal"` fallback in `on_discovery_error` is unreachable by any route** (every client-depending route is a teacher route). Kept because the alternative is an unhandled `ServiceDiscoveryError` → 500. Untested by construction.
3. **`test_teacher_views.py`'s `assert "Ward portal" not in response.text`** stays — a teacher page must not render a ward title; it is correct, not stale.

**State at close (for the commit decision — the round does not commit itself):** this round's 10 files (frozen patch above) sit in a tree that also carries the *unrelated* ward-decision record pass (`documents/configuration.md`, `documents/specs/teachers-ward-portal-prefab-plan.md` — now including the §4 sketch and §7 completion notes for this round — and the new `documents/solution/ward-surface-decision.md`) plus a pre-existing `AGENTS.md` modification, and the round doc + patch artifacts. The base `a1c6de53` is **unmerged** (`feat/portal-submission-grade` is pushed with no PR), so the commit either stacks on that branch or waits for it to reach `main`.
