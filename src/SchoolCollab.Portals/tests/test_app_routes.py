"""Route-level tests for the thin app, wired to a stubbed transport.

Uses FastAPI's ``TestClient`` (no uvicorn) plus ``app.dependency_overrides`` —
container-free coverage of the whole chain route -> client -> Prefab render,
which is the Python analogue of the ar-24 decision to test rejection on a
TestServer instead of a running host.

``TestClient`` stays synchronous even though the routes are async: it drives the
ASGI app on its own event loop, which is exactly what the async client needs.
"""

from __future__ import annotations

from typing import Any

import httpx
import pytest
from fastapi.testclient import TestClient

import app as portal_app
from api import AssignmentsApiClient, ServiceEndpoint

STUB_ENDPOINT = ServiceEndpoint(
    service="assignments-api", base_url="http://assignments-api.test", env_var="test"
)


def _stub_override(rows: list[dict[str, Any]]):
    """A dependency override serving ``rows`` from a MockTransport.

    Declared ``async`` so FastAPI builds the client inside the running event
    loop, and deliberately left unclosed — ``httpx.MockTransport`` holds no
    resources.
    """
    handler = lambda request: httpx.Response(200, json=rows)  # noqa: E731

    async def override() -> AssignmentsApiClient:
        return AssignmentsApiClient(
            httpx.AsyncClient(transport=httpx.MockTransport(handler)), STUB_ENDPOINT
        )

    return override


def test_ward_view_renders_rows_through_the_injected_client() -> None:
    portal_app.app.dependency_overrides[portal_app.get_assignments_client] = _stub_override(
        [
            {
                "id": "a1",
                "title": "Reading response",
                "status": "Published",
                "dueDate": "2026-10-01",
                "createdAt": "2026-09-21",
            }
        ]
    )
    try:
        with TestClient(portal_app.app) as client:
            response = client.get("/")
    finally:
        portal_app.app.dependency_overrides.clear()

    assert response.status_code == 200
    assert "Reading response" in response.text
    assert "1 assignments" in response.text


def test_ward_view_renders_an_error_card_when_the_api_is_unreachable(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv("services__assignments-api__http__0", "http://127.0.0.1:9")

    with TestClient(portal_app.app) as client:
        response = client.get("/")

    assert response.status_code == 200  # a page, never a traceback
    assert "unavailable" in response.text


def test_ward_view_renders_an_error_card_when_discovery_fails(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.delenv("services__assignments-api__http__0", raising=False)
    monkeypatch.delenv("ASSIGNMENTS_API_HTTP", raising=False)

    with TestClient(portal_app.app) as client:
        response = client.get("/")

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
