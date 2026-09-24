"""Route-level tests for the thin app, wired to a stubbed auth service.

Uses FastAPI's ``TestClient`` (no uvicorn, no Docker, no Keycloak) plus
``app.dependency_overrides`` — container-free coverage of the whole chain route -> client -> page,
which is the Python analogue of the .NET hosts' container-free ``TestServer`` approach.

``TestClient`` stays synchronous even though the routes are async: it drives the ASGI app on its
own event loop, which is exactly what the async client needs.
"""

from __future__ import annotations

from collections.abc import Callable
from typing import Any

import httpx
import pytest
from fastapi.testclient import TestClient

import app as portal_app
from api import AuthApiClient, AuthServiceEndpoint

STUB_ENDPOINT = AuthServiceEndpoint(
    service="auth", base_url="http://auth.test", env_var="test"
)

Handler = Callable[[httpx.Request], httpx.Response]


def _stub_override(handler: Handler):
    """A dependency override serving ``handler`` from a MockTransport.

    Declared ``async`` so FastAPI builds the client inside the running event loop, and
    deliberately left unclosed — ``httpx.MockTransport`` holds no resources.
    """

    async def override() -> AuthApiClient:
        return AuthApiClient(httpx.AsyncClient(transport=httpx.MockTransport(handler)), STUB_ENDPOINT)

    return override


def _json_response(payload: object, status_code: int = 200) -> httpx.Response:
    return httpx.Response(status_code, json=payload, headers={"content-type": "application/json"})


def _get(path: str, *, session_id: str | None = None) -> httpx.Response:
    with TestClient(portal_app.app) as client:
        if session_id is not None:
            client.cookies.set(portal_app.SESSION_COOKIE_NAME, session_id)
        return client.get(path)


def test_index_renders_the_route_table_and_the_resolved_auth_endpoint(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv("services__auth__http__0", "http://localhost:5301")

    response = _get("/")

    assert response.status_code == 200
    assert "School-Collab auth portal" in response.text
    assert "http://localhost:5301" in response.text
    # The route table is read from the app itself, so a route cannot exist unlisted.
    assert "GET /health" in response.text
    assert "GET /" in response.text


def test_index_reports_a_signed_out_visitor(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("services__auth__http__0", "http://localhost:5301")

    response = _get("/")

    assert response.status_code == 200
    assert "Not signed in." in response.text


def test_index_renders_the_session_state_from_a_mediated_read(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv("services__auth__http__0", "http://localhost:5301")
    seen: dict[str, Any] = {}

    def handler(request: httpx.Request) -> httpx.Response:
        seen["url"] = str(request.url)
        return _json_response(
            {
                "tenantId": "tenant-1",
                "tenantName": "Acme Academy",
                "tenantType": "school",
                "teacherId": "teacher-7",
                "roles": ["user-admin"],
            }
        )

    portal_app.app.dependency_overrides[portal_app.get_auth_api_client] = _stub_override(handler)
    try:
        response = _get("/", session_id="session-1")
    finally:
        portal_app.app.dependency_overrides.clear()

    assert response.status_code == 200
    assert seen["url"] == "http://auth.test/auth/session/session-1"
    assert "teacher-7" in response.text
    assert "Acme Academy" in response.text
    assert "user-admin" in response.text
    # The opaque session id is a capability: the portal never renders it back.
    assert "session-1" not in response.text


def test_index_renders_a_degraded_state_when_the_session_ended(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv("services__auth__http__0", "http://localhost:5301")
    handler: Handler = lambda request: _json_response({"error": "session_ended"}, 401)  # noqa: E731

    portal_app.app.dependency_overrides[portal_app.get_auth_api_client] = _stub_override(handler)
    try:
        response = _get("/", session_id="session-1")
    finally:
        portal_app.app.dependency_overrides.clear()

    assert response.status_code == 200  # a page, never a traceback
    assert "SessionEndedError" in response.text
    assert "session ended" in response.text


def test_index_renders_a_degraded_state_when_the_auth_service_is_unreachable(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv("services__auth__http__0", "http://127.0.0.1:9")

    def handler(request: httpx.Request) -> httpx.Response:
        raise httpx.ConnectError("connection refused", request=request)

    portal_app.app.dependency_overrides[portal_app.get_auth_api_client] = _stub_override(handler)
    try:
        response = _get("/", session_id="session-1")
    finally:
        portal_app.app.dependency_overrides.clear()

    assert response.status_code == 200
    assert "unreachable" in response.text


def test_index_renders_a_degraded_state_when_discovery_fails(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.delenv("services__auth__http__0", raising=False)
    monkeypatch.delenv("AUTH_HTTP", raising=False)

    response = _get("/")

    assert response.status_code == 200
    assert "ServiceDiscoveryError" in response.text
    assert "GET /health" in response.text  # the route table survives a degraded upstream


def test_health_reports_the_resolved_endpoint(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("services__auth__http__0", "http://localhost:5301")

    with TestClient(portal_app.app) as client:
        body = client.get("/health").json()

    assert body["status"] == "ok"
    assert body["auth_base_url"] == "http://localhost:5301"
    assert body["auth_env_var"] == "services__auth__http__0"
    assert body["discovery_error"] is None


def test_health_reports_the_last_call_outcome(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("services__auth__http__0", "http://localhost:5301")
    handler: Handler = lambda request: _json_response({"teacherId": "teacher-7"})  # noqa: E731

    portal_app.app.dependency_overrides[portal_app.get_auth_api_client] = _stub_override(handler)
    try:
        with TestClient(portal_app.app) as client:
            client.cookies.set(portal_app.SESSION_COOKIE_NAME, "session-1")
            client.get("/")
            body = client.get("/health").json()
    finally:
        portal_app.app.dependency_overrides.clear()

    assert body["last_call"]["call"] == "read_session"
    assert body["last_call"]["status"] == "ok"


def test_health_reports_degraded_when_discovery_fails(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.delenv("services__auth__http__0", raising=False)
    monkeypatch.delenv("AUTH_HTTP", raising=False)

    with TestClient(portal_app.app) as client:
        body = client.get("/health").json()

    assert body["status"] == "degraded"
    assert body["auth_base_url"] is None
    assert "services__auth__http__0" in body["discovery_error"]
