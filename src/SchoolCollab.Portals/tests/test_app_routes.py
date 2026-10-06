"""Route-level tests for the thin app's entry point and diagnostics.

Uses FastAPI's ``TestClient`` (no uvicorn) with the real service-discovery path
driven from the environment: the redirect at ``/``, the degraded teacher-list
renders, and ``/health``. The teacher surface's own route -> client -> Prefab
chain (``test_teacher_views.py``) drives it through a stubbed transport via
``app.dependency_overrides`` instead.

The module also pins the portal's whole **route set**
(``test_route_set_is_the_reviewed_set``) against the live route table — the
mechanical half of the scope decision that closed the portal at read/review/grade.

``TestClient`` stays synchronous even though the routes are async: it drives the
ASGI app on its own event loop, which is exactly what the async client needs.
"""

from __future__ import annotations

import pytest
from fastapi.testclient import TestClient

import app as portal_app


def test_root_redirects_to_the_teacher_list() -> None:
    """The portal's entry point is a redirect: the teacher list is the only surface (AC2)."""
    with TestClient(portal_app.app) as client:
        response = client.get("/", follow_redirects=False)

    assert response.status_code == 302
    assert response.headers["location"] == "/teacher"


def test_teacher_list_renders_an_error_card_when_the_api_is_unreachable(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Retargeted from the retired portal-root page: the teacher list's transport failure is a
    page."""
    monkeypatch.setenv("services__assignments-api__http__0", "http://127.0.0.1:9")

    with TestClient(portal_app.app) as client:
        response = client.get("/teacher")

    assert response.status_code == 200  # a page, never a traceback
    assert "assignment list degraded" in response.text
    assert "ApiUnavailableError" in response.text


def test_teacher_list_renders_an_error_card_when_discovery_fails(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Retargeted from the retired portal-root page: a discovery failure is a page, not a 500."""
    monkeypatch.delenv("services__assignments-api__http__0", raising=False)
    monkeypatch.delenv("ASSIGNMENTS_API_HTTP", raising=False)

    with TestClient(portal_app.app) as client:
        response = client.get("/teacher")

    assert response.status_code == 200
    assert "ServiceDiscoveryError" in response.text


def test_health_reports_the_resolved_endpoint(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("services__assignments-api__http__0", "http://localhost:5199")

    with TestClient(portal_app.app) as client:
        body = client.get("/health").json()

    assert body["status"] == "ok"
    assert body["api_base_url"] == "http://localhost:5199"
    assert body["api_env_var"] == "services__assignments-api__http__0"


def test_health_reports_degraded_when_discovery_fails(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.delenv("services__assignments-api__http__0", raising=False)
    monkeypatch.delenv("ASSIGNMENTS_API_HTTP", raising=False)

    with TestClient(portal_app.app) as client:
        body = client.get("/health").json()

    assert body["status"] == "degraded"
    assert body["api_base_url"] is None
    assert "services__assignments-api__http__0" in body["discovery_error"]


#: The portal's complete HTTP surface. Pinned deliberately: the portal's scope is closed at
#: read/review/grade (``documents/solution/portal-scope-decision.md``), so a route that could
#: author — or otherwise write — must arrive as a reviewed decision, never as a quiet addition.
REVIEWED_ROUTES = frozenset(
    {
        ("GET", "/"),
        ("GET", "/teacher"),
        ("GET", "/teacher/assignments/{assignment_id}"),
        ("GET", "/teacher/assignments/{assignment_id}/students/{student_id}"),
        ("POST", "/teacher/assignments/{assignment_id}/students/{student_id}/review"),
        ("GET", "/auth/callback"),
        ("GET", "/teacher/logout"),
        ("GET", "/health"),
    }
)

#: The one route T5's write half added, and the only non-GET the portal may serve.
REVIEW_ROUTE = (
    "POST",
    "/teacher/assignments/{assignment_id}/students/{student_id}/review",
)

#: FastAPI's own routes, which sit in ``app.routes`` beside the portal's. Pinned so a version
#: bump that adds or renames one is reported as a framework change, not a portal one. Unlike
#: FastAPI's ``APIRoute``, Starlette's own routes mirror every GET as a HEAD (observed, not
#: assumed: the portal's routes report ``{"GET"}`` alone).
FRAMEWORK_ROUTES = frozenset(
    {
        ("GET", "/openapi.json"),
        ("HEAD", "/openapi.json"),
        ("GET", "/docs"),
        ("HEAD", "/docs"),
        ("GET", "/docs/oauth2-redirect"),
        ("HEAD", "/docs/oauth2-redirect"),
        ("GET", "/redoc"),
        ("HEAD", "/redoc"),
    }
)


def test_route_set_is_the_reviewed_set() -> None:
    """The portal's whole HTTP surface is pinned — the scope decision's mechanical half.

    A tripwire rather than a tautology: it reads the live route table, so it cannot pass
    vacuously, and every route must be either reviewed or an explicitly pinned framework route.
    """
    declared = {
        (method, route.path)
        for route in portal_app.app.routes
        for method in getattr(route, "methods", None) or ()
    }

    # Anti-vacuity: the walk really reached the app's own routes, including the ones whose path
    # is a module constant rather than a literal.
    assert ("GET", "/health") in declared
    assert REVIEW_ROUTE in declared

    # The three route constants must still resolve to their pinned paths. app.py matches the
    # review route by comparing ``route.path == REVIEW_PATH`` in its outcome handler, so a
    # constant that drifted from the surface would break that match with nothing else noticing.
    assert portal_app.REVIEW_PATH == REVIEW_ROUTE[1]
    assert portal_app.AUTH_CALLBACK_PATH == "/auth/callback"
    assert portal_app.LOGOUT_PATH == "/teacher/logout"

    # The write surface is exactly one route. This is where a portal-side authoring route — the
    # thing the scope decision rules out — would stop.
    writes = {entry for entry in declared if entry[0] not in {"GET", "HEAD"}}
    assert writes == {REVIEW_ROUTE}

    moved = (declared & FRAMEWORK_ROUTES) ^ FRAMEWORK_ROUTES
    assert not moved, (
        "FastAPI's own routes moved — a version bump added or renamed one, so update "
        f"FRAMEWORK_ROUTES deliberately. Moved: {sorted(moved)}"
    )

    added = declared - REVIEWED_ROUTES - FRAMEWORK_ROUTES
    removed = REVIEWED_ROUTES - declared
    assert not added and not removed, (
        "the portal's route set changed. This pin exists to make that a reviewed act "
        "(documents/solution/portal-scope-decision.md): update REVIEWED_ROUTES in the same "
        f"change. Added: {sorted(added)}; removed: {sorted(removed)}"
    )
