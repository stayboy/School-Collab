"""Route-level tests for the thin app's entry point and diagnostics.

Uses FastAPI's ``TestClient`` (no uvicorn) with the real service-discovery path
driven from the environment: the redirect at ``/``, the degraded teacher-list
renders, and ``/health``. The teacher surface's own route -> client -> Prefab
chain (``test_teacher_views.py``) drives it through a stubbed transport via
``app.dependency_overrides`` instead.

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
