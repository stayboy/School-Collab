# Round — portal-teacher-surface

Round `portal-teacher-surface` · **Tier 2 (Light)** · 2026-10-03
Models: worker `ollama-cloud/deepseek-v4.1-flash` · reviewer `ollama-cloud/kimi-k2.7-code`
Base: `stack/8-teacher-scope-auth` (`560bef93`, unmerged layer behind PR #296) · Branch: `stack/9-portal-teacher-surface`

Settled inputs: round-2 grill, 2026-10-03 — **Q1** all three read surfaces as a **drill-down** over existing
endpoints · **Q2** a third CI job `portals-python` · **Q3** **pytest + `httpx.MockTransport` only** (Playwright
deferred) · **Q4** **dev-bypass-only**, the real-auth gap recorded · **Q5** a **flat `views/teacher.py`** ·
**Q6** **Light (Tier 2)**. Round 1 (the authorization base) is `stack/8`.

## Plan

### Goal

The **read-only teacher surface** in the Prefab portal — assignment list → per-assignment review queue →
submission detail — plus the **Python CI job** that gates the portal's tests for the first time, and the
solution-items tripwire updates the new files require.

### Deliverables

**D1 — typed client methods** (`api/assignments_api_client.py`, one method per endpoint, per the file's own
doc-comment). `list_review_queue(assignment_id, teacher_id)` → `GET /assignments/{id}/submissions/review-queue`
and `get_submission(assignment_id, student_id)` → `GET /assignments/{id}/students/{studentId}/submission`.
Non-2xx and transport failures must surface as the existing **typed** portal errors
(`ApiUnavailableError` / `ApiResponseError`) — never a raw `httpx` exception and never a traceback.

**D2 — tolerant row DTOs** (`api/dto.py`, following `AssignmentRow`'s shape: a frozen dataclass with a
`from_payload` classmethod using `_text()`): a review-queue row (from the API's `SubmissionForReviewDto`) and
a submission detail (from `SubmissionDetailDto`, including its answers/review fields). Tolerant by contract —
the module's doc-comment says *"a shape drift should degrade one cell, not the whole page"*.

**D3 — the views** (`views/teacher.py`, a **flat module** per Q5 — `views/ward.py` is the precedent):
`build_teacher_list_view`, `build_review_queue_view`, `build_submission_view`, plus a degraded view built from a
typed portal error. Same component vocabulary as `ward.py` (`PrefabApp`, `Card`, `DataTable`, `Badge`, …).

**D4 — routes** (`app.py`): `/teacher`, `/teacher/assignments/{assignment_id}`,
`/teacher/assignments/{assignment_id}/students/{student_id}`. Each resolves the endpoint + client through the
existing dependency, and catches `PortalApiError` into the degraded view — the existing ward route is the
pattern, including its "never show a raw 500" posture.

**D5 — the dev teacher id (a real seam).** `GET /{id}/submissions/review-queue` takes a **`teacherId` query
parameter** and uses it only as the dev-bypass fallback (`currentUser.TeacherId ?? (isRealAuth ? throw : teacherId)`).
The portal has **no** identity plumbing, so it must pass a **configured** dev teacher id (its own environment
value, injected by the AppHost beside the API's `TestAuth:TeacherId`). It must **never invent or default to a
constant**: if the value is unset the queue route cannot be called, and the surface must say so honestly rather
than showing an empty queue. Under real auth the API prefers the claim, so the parameter is inert there.

**D6 — tests** (`tests/test_teacher_views.py`): `pytest` + `httpx.MockTransport` + FastAPI `TestClient` via
`app.dependency_overrides`, exactly as `test_app_routes.py` does — no server, no Docker, no browser. Cover each
route's happy path through the injected client, each degraded case (transport failure, non-2xx, a 404/403 from
the gated reads), and the empty-result state.

**D7 — the solution-items tripwire (mandatory, not incidental).**
`PortalsSolutionItems_AreTheReviewedSet` asserts **exact** equivalence — its own comment: *"Adding or removing
a portal file must update this list consciously."* So add the two new files to **both** `SchoolCollab.slnx`
(under the existing `/src/SchoolCollab.Portals/views/` and `/tests/` folder nodes) **and**
`PortalsSolutionItemsArchitectureTests.PortalSolutionItems_AreTheReviewedSet`. Because Q5 chose a flat module,
**no new folder node is added** and `PortalSolutionFolderNodes_MirrorThePackageLayout` stays unchanged — state
that explicitly so a later reader sees it was considered rather than missed.

**D8 — the Python CI job** in `.github/workflows/ci.yml`: a third job `portals-python`
(`actions/checkout` → `actions/setup-python` → `astral-sh/setup-uv` → `uv sync` → `uv run pytest`), scoped to
`src/SchoolCollab.Portals`. It must not touch the .NET jobs, and must run the suite the portal already has
plus the new tests.

**D9 — docs.** Correct `documents/specs/teachers-ward-portal-prefab-plan.md` Phase 2's test story, which cites
`.github/copilot/rules/testing.md` *for Playwright* — that rule never mentions Playwright (verified). Record
the **real-auth portal gap** (the portal sends no credentials; when `FEATURE:DisableOIDCAuth` flips, the six
gated reads require a bearer the portal cannot supply) **beside** the D11 role-assignment prerequisite, so both
"must happen before the flag flips" items live together.

### Acceptance criteria

1. `uv run pytest` green locally, and the new `portals-python` CI job runs it (demonstrated on the PR).
2. The tripwire passes — `SchoolCollab.slnx` and the pinned list both updated, no folder-node change.
3. All three routes render **live** data through the injected client, and **every** degraded case shows a typed
   error view: no traceback, no raw 500, no silent empty list standing in for a failure.
4. The unset dev teacher id surfaces honestly (D5) — it must not be papered over with a default.
5. **No `.NET` production change**: this round adds no `src/**/*.cs`, no migration, no contract change. The
   existing architecture/Assignments/Api/Integration suites are unchanged.
6. No new Python dependency beyond what `pyproject.toml` already declares (pytest + pytest-asyncio are present).

### Expected files

| File | Change |
|---|---|
| `src/SchoolCollab.Portals/api/assignments_api_client.py` | +2 methods |
| `src/SchoolCollab.Portals/api/dto.py` | +2 tolerant row DTOs |
| `src/SchoolCollab.Portals/views/teacher.py` | **new** (flat module) |
| `src/SchoolCollab.Portals/app.py` | +3 routes, + dev teacher id wiring |
| `src/SchoolCollab.Portals/tests/test_teacher_views.py` | **new** |
| `SchoolCollab.slnx` + `tests/SchoolCollab.ArchitectureTests.Unit/PortalsSolutionItemsArchitectureTests.cs` | tripwire |
| `.github/workflows/ci.yml` | +`portals-python` job |
| `documents/specs/teachers-ward-portal-prefab-plan.md` | Phase 2 citation + recorded gaps |

### Out of scope

Playwright E2E (Q3 — deferred, and the cited rule never mandated it); real-auth bearer forwarding in the portal
(Q4 — its own design round, recorded in D9); the grade **write** (Q2 of round 1 — read-only); `views/teacher/` as
a package (Q5 — superseded by the flat module); any `.NET` change; the ward surface.

## Worker Report

_(pending)_

## Review

**Diff reviewer `04d66c9f` (`kimi-k2.7-code`) — Verdict: `P2-only`, no P1, "round can be accepted: yes".**

| Seam | Verdict |
|---|---|
| **D5 refusal path** | ok — `app.py:146-174` resolves `PORTAL_DEV_TEACHER_ID` **before any client call**; unset/blank/malformed/all-zero raise `MissingConfigurationError`, and `test_teacher_views.py:235-285` proves `seen == []` (no request issued) |
| **Discovery handler** | ok — `_teacher_surface_for` (`app.py:351-362`) returns `None` for non-teacher prefixes, so the **ward path is unchanged** and an unrelated prefix cannot misroute |
| **Typed errors + empty states** | ok — the client's content-type guard + `ValueError` wrap turn HTML/non-JSON bodies into typed errors; 4xx/5xx/transport become `ApiResponseError`/`ApiUnavailableError`; every empty state has an explicit success message, never an empty table standing in for an error |
| **Tripwire** | ok — both files in `SchoolCollab.slnx` **and** the pinned list (`:109-126`); **no** folder node; `PortalSolutionFolderNodes_MirrorThePackageLayout` (`:136`) unchanged |
| **Spec corrections truthful** | yes — `testing.md` covers only the .NET stack and never mentions Playwright; `views/teacher/` now reads `views/teacher.py`; the two-prerequisites block is accurate |
| **"Dev-bypass-only" claim honest** | **yes** — it ships a surface that will stay in the `MissingConfigurationError` degraded state in a live `aspire run` because no AppHost `.cs` change fans the variable out |
| **CI job** | ok — appended without touching the two existing jobs; correct `working-directory`; `uv sync` before `uv run pytest`; no path filter, so it cannot pass vacuously |
| Best practices | conventions honoured — frozen dataclasses, tolerant `from_payload` DTOs, pure view functions, no module-level mutable globals, typed errors, no new Python dependency |

**P2-1 — the 80 → 83 suite-count delta: RESOLVED by the parent, not carried.** The guard is data-free (no `[DataRow]`/`DynamicData`; the reviewer confirmed, and the parent confirmed the project has **none** — the `DataRow` grep hits were binary DLLs). The arithmetic closes exactly: `main` **75** + `stack/7`'s snapshot guard (**3** — `OwnedKeyValueGenerationArchitectureTests`) = 78, + round 1's `TeacherScopeWiringArchitectureTests` (**5**) = **83**, + **round 2 adds 0**. The earlier *80* was measured while `stack/8` was still rooted on `main`, i.e. **before it was re-rooted onto `stack/7`** for the stacked PR — so the three came from the snapshot guard entering the branch, not from this round. Statically confirmed: 83 `[TestMethod]` attributes and 83 executed.

**P2-2 — the live-`aspire run` gap (carried, and it is the headline surface's end-to-end gap).** `PORTAL_DEV_TEACHER_ID` has **no AppHost fan-out** — the AppHost is C# and this round forbids `src/**/*.cs`. So the review queue stays honestly degraded in a live run. **The same gap affects round 1's `TestAuth:TeacherId`**, which the AppHost also does not inject (it is API-appsettings-only), so the two settings that jointly decide the dev teacher identity are both unfanned. That is the parent's constraint over-reaching: *"no `.NET` production change"* was meant to bar service/domain/contract changes, but the AppHost is a **wiring** project and `.WithEnvironment(...)` is this repo's established mechanism. Carried as its own round (see Acceptance).

## Acceptance

**Verdict: CLOSED.**

**Tier 2 (Light)** — parent authored the plan; worker `3ebaffef` implemented; reviewer `04d66c9f` reviewed (**P2-only**); no rework needed.

| Gate | Result |
|---|---|
| `uv run pytest` (portal, parent-run) | **34 / 0** (14 pre-existing + 20 new) |
| `dotnet build SchoolCollab.slnx` | **0 errors** |
| `ArchitectureTests.Unit` | **83 / 0** |
| **`src/**/*.cs` production change** | **none** — the only `.cs` edit is the architecture *test* |
| Tripwire | 2 pinned paths, **no** folder node added |

**Acceptance criteria:** 1 (portal suite green; the CI job runs it) · 2 (tripwire passes — `.slnx` + pinned list updated, no folder node) · 3 (all three routes render live data; every degraded case shows a typed error view — no traceback, no raw 500, no silent empty list standing in for a failure) · 4 (the unset dev teacher id surfaces honestly — `MissingConfigurationError`, **no request issued**, never defaulted) · 5 (no `.NET` production change; the architecture suite is otherwise unchanged) · 6 (no new Python dependency). **All met.**

**What this round bought:** the portal's first real surface (assignment list → per-assignment review queue → submission detail), its first CI coverage (the suite had never executed in CI), and the solution-items tripwire updated *consciously* — its own stated purpose. Also two spec corrections that were quietly false: Phase 2's Playwright citation, and `views/teacher/` vs the flat module actually shipped.

**Carried, and they must not be lost — three prerequisites now sit outside the two feature rounds:**

1. **D11 role assignment** before `FEATURE:DisableOIDCAuth` flips (or the reader policy 403s every existing assignment reader).
2. **The portal's real-auth gap** before the same flip (it sends no credentials; all six gated reads would 401).
3. **The AppHost fan-out for the dev teacher identity** — `PORTAL_DEV_TEACHER_ID` *and* `TestAuth:TeacherId` — before the queue surface works in a live `aspire run` at all.

(3) is the next unit of work: a small wiring round, because it spans **both** feature rounds and fixing one without the other leaves the identity half-wired. (1) and (2) are rollout prerequisites recorded in the spec.
