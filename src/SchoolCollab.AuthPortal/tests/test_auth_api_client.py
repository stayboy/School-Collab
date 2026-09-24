"""Client tests driven by ``httpx.MockTransport`` — no server, no Docker, no Keycloak.

This is the Python counterpart of the .NET hosts' scripted ``HttpMessageHandler`` approach, and it
is what isolating the auth-service calls in their own class buys the portal. The client is async,
so each case owns its transport through an ``async with`` block (the same lifespan-owned-client
discipline as the app).

Two axes are load-bearing:

* **the request shapes** the auth service must serve (paths, bodies, verb) — the portal's half of
  the round's cross-service contract;
* **AC11** — no token ever enters Python state. The client refuses to ingest a body carrying a
  token-shaped field, and a structural assertion proves no DTO in ``api.dto`` declares one.
"""

from __future__ import annotations

import dataclasses
import json
from collections.abc import AsyncIterator, Callable
from contextlib import asynccontextmanager

import httpx
import pytest

import api.dto
from api import (
    ApiResponseError,
    ApiUnavailableError,
    AuthApiClient,
    AuthCredentialsRejectedError,
    AuthServiceEndpoint,
    AuthUpstreamError,
    AuthUserDisabledError,
    BootstrapCodeRejectedError,
    SessionEndedError,
    SessionNotFoundError,
    TokenInResponseError,
)
from api.auth_api_client import TOKEN_SHAPED_KEYS

ENDPOINT = AuthServiceEndpoint(service="auth", base_url="http://auth.test", env_var="test")

Handler = Callable[[httpx.Request], httpx.Response]


@asynccontextmanager
async def _client(handler: Handler) -> AsyncIterator[AuthApiClient]:
    async with httpx.AsyncClient(transport=httpx.MockTransport(handler)) as http:
        yield AuthApiClient(http, ENDPOINT)


def _json_response(payload: object, status_code: int = 200) -> httpx.Response:
    return httpx.Response(status_code, json=payload, headers={"content-type": "application/json"})


async def test_exchange_posts_the_credentials_and_the_redirect_uri() -> None:
    seen: dict[str, object] = {}

    def handler(request: httpx.Request) -> httpx.Response:
        seen["method"] = request.method
        seen["url"] = str(request.url)
        seen["body"] = json.loads(request.content)
        return _json_response({"code": "one-time-code", "sessionId": "session-1", "expiresInSeconds": 300})

    async with _client(handler) as client:
        result = await client.exchange("dev-teacher", "hunter2", "https://localhost:5300/signin-handshake")

    assert seen["method"] == "POST"
    assert seen["url"] == "http://auth.test/auth/exchange"
    assert seen["body"] == {
        "username": "dev-teacher",
        "password": "hunter2",
        "redirectUri": "https://localhost:5300/signin-handshake",
    }
    assert result.code == "one-time-code"
    assert result.session_id == "session-1"
    assert result.expires_in_seconds == 300


@pytest.mark.parametrize(
    ("status_code", "error", "expected"),
    [
        (401, "invalid_credentials", AuthCredentialsRejectedError),
        (403, "disabled_user", AuthUserDisabledError),
        (502, "keycloak_unreachable", AuthUpstreamError),
    ],
)
async def test_exchange_maps_the_failure_taxonomy(
    status_code: int, error: str, expected: type[Exception]
) -> None:
    handler: Handler = lambda request: _json_response(  # noqa: E731
        {"error": error, "detail": "from keycloak"}, status_code
    )

    async with _client(handler) as client:
        with pytest.raises(expected) as failure:
            await client.exchange("dev-teacher", "wrong", "https://localhost:5300/signin-handshake")

    assert failure.value.code == error  # type: ignore[attr-defined]
    assert "from keycloak" in str(failure.value)


async def test_bootstrap_redeem_posts_the_code_bound_to_the_portal_redirect_uri() -> None:
    seen: dict[str, object] = {}

    def handler(request: httpx.Request) -> httpx.Response:
        seen["url"] = str(request.url)
        seen["body"] = json.loads(request.content)
        return _json_response(
            {
                "sessionId": "session-2",
                "appRedirectUri": "https://localhost:5300/signin-handshake",
                "appCode": "app-one-time-code",
            }
        )

    async with _client(handler) as client:
        result = await client.redeem_bootstrap_code(
            "bootstrap-code", "http://localhost:5310/bootstrap"
        )

    assert seen["url"] == "http://auth.test/auth/bootstrap/redeem"
    assert seen["body"] == {"code": "bootstrap-code", "redirectUri": "http://localhost:5310/bootstrap"}
    assert result.session_id == "session-2"
    assert result.app_redirect_uri == "https://localhost:5300/signin-handshake"
    assert result.app_code == "app-one-time-code"


async def test_bootstrap_redeem_without_a_blazor_continuation() -> None:
    handler: Handler = lambda request: _json_response({"sessionId": "session-3"})  # noqa: E731

    async with _client(handler) as client:
        result = await client.redeem_bootstrap_code("bootstrap-code", "http://localhost:5310/bootstrap")

    assert result.session_id == "session-3"
    assert result.app_redirect_uri is None
    assert result.app_code is None


@pytest.mark.parametrize(
    ("status_code", "error"),
    [(404, "invalid_code"), (400, "code_expired"), (400, "redirect_uri_mismatch")],
)
async def test_bootstrap_redeem_rejections_are_typed(status_code: int, error: str) -> None:
    handler: Handler = lambda request: _json_response({"error": error}, status_code)  # noqa: E731

    async with _client(handler) as client:
        with pytest.raises(BootstrapCodeRejectedError) as failure:
            await client.redeem_bootstrap_code("replayed", "http://localhost:5310/bootstrap")

    assert failure.value.code == error


async def test_read_session_returns_the_claim_set_as_data() -> None:
    seen: dict[str, str] = {}

    def handler(request: httpx.Request) -> httpx.Response:
        seen["url"] = str(request.url)
        return _json_response(
            {
                "tenantId": "tenant-1",
                "tenantName": "Acme Academy",
                "tenantType": "school",
                "teacherId": "teacher-7",
                "roles": ["user-admin", "platform-admin"],
                "expiresInSeconds": 240,
            }
        )

    async with _client(handler) as client:
        session = await client.read_session("session-1")

    assert seen["url"] == "http://auth.test/auth/session/session-1"
    assert session.tenant_name == "Acme Academy"
    assert session.teacher_id == "teacher-7"
    assert session.roles == ("user-admin", "platform-admin")
    assert session.is_admin is True
    assert session.expires_in_seconds == 240


async def test_read_session_tolerates_a_comma_separated_role_claim() -> None:
    handler: Handler = lambda request: _json_response(  # noqa: E731
        {"tenant_id": "tenant-1", "teacher_id": "teacher-7", "roles": "user-admin"}
    )

    async with _client(handler) as client:
        session = await client.read_session("session-1")

    assert session.roles == ("user-admin",)
    assert session.is_admin is True


async def test_read_session_missing_role_is_not_admin() -> None:
    handler: Handler = lambda request: _json_response({"tenantId": "tenant-1"})  # noqa: E731

    async with _client(handler) as client:
        session = await client.read_session("session-1")

    assert session.roles == ()
    assert session.is_admin is False


async def test_read_session_ends_distinctly_from_not_found() -> None:
    """D18: 'session ended' is its own status, so the portal can clear the cookie and say why."""
    ended: Handler = lambda request: _json_response({"error": "session_ended"}, 401)  # noqa: E731

    async with _client(ended) as client:
        with pytest.raises(SessionEndedError):
            await client.read_session("session-1")

    missing: Handler = lambda request: _json_response({"error": "session_not_found"}, 404)  # noqa: E731

    async with _client(missing) as client:
        with pytest.raises(SessionNotFoundError):
            await client.read_session("gone")


async def test_read_session_quotes_the_session_id_into_the_path() -> None:
    seen: dict[str, str] = {}

    def handler(request: httpx.Request) -> httpx.Response:
        seen["url"] = str(request.url)
        return _json_response({"teacherId": "teacher-7"})

    async with _client(handler) as client:
        await client.read_session("session/../1")

    assert seen["url"] == "http://auth.test/auth/session/session%2F..%2F1"


async def test_revoke_session_returns_the_end_session_url_and_no_token() -> None:
    seen: dict[str, str] = {}

    def handler(request: httpx.Request) -> httpx.Response:
        seen["method"] = request.method
        seen["url"] = str(request.url)
        return _json_response({"end_session_url": "https://keycloak/realms/school-collab/protocol/openid-connect/logout?id_token_hint=..."})

    async with _client(handler) as client:
        revocation = await client.revoke_session("session-1")

    assert seen["method"] == "DELETE"
    assert seen["url"] == "http://auth.test/auth/session/session-1"
    assert revocation.end_session_url is not None
    assert "logout" in revocation.end_session_url


@pytest.mark.parametrize("token_key", sorted(TOKEN_SHAPED_KEYS))
async def test_a_token_shaped_response_body_is_never_ingested(token_key: str) -> None:
    """AC11: the portal refuses a token even when the auth service hands it one."""
    handler: Handler = lambda request: _json_response(  # noqa: E731
        {"sessionId": "session-1", token_key: "a-token-that-must-not-reach-python"}
    )

    async with _client(handler) as client:
        with pytest.raises(TokenInResponseError) as failure:
            await client.revoke_session("session-1")

    assert token_key in failure.value.keys


async def test_a_nested_token_shaped_field_is_caught_too() -> None:
    handler: Handler = lambda request: _json_response(  # noqa: E731
        {"sessionId": "session-1", "tokens": {"access_token": "nested"}}
    )

    async with _client(handler) as client:
        with pytest.raises(TokenInResponseError):
            await client.revoke_session("session-1")


async def test_transport_failure_is_reported_as_unavailable() -> None:
    def handler(request: httpx.Request) -> httpx.Response:
        raise httpx.ConnectError("connection refused", request=request)

    async with _client(handler) as client:
        with pytest.raises(ApiUnavailableError) as failure:
            await client.read_session("session-1")

    assert "unreachable" in str(failure.value)
    assert failure.value.base_url == "http://auth.test"


async def test_html_login_page_is_rejected_as_data() -> None:
    """A proxy or OIDC challenge can answer 2xx with an HTML page; it must never count as data."""
    handler: Handler = lambda request: httpx.Response(  # noqa: E731
        200, text="<html>login</html>", headers={"content-type": "text/html"}
    )

    async with _client(handler) as client:
        with pytest.raises(ApiResponseError) as failure:
            await client.exchange("dev-teacher", "hunter2", "https://localhost:5300/signin-handshake")

    assert "text/html" in str(failure.value)


async def test_an_unknown_error_code_degrades_as_an_unusable_response() -> None:
    """Fail-closed: an unrecognized code is never treated as a usable response."""
    handler: Handler = lambda request: _json_response({"error": "brand_new_failure"}, 418)  # noqa: E731

    async with _client(handler) as client:
        with pytest.raises(ApiResponseError) as failure:
            await client.read_session("session-1")

    assert "HTTP 418: brand_new_failure" in str(failure.value)


async def test_a_non_object_success_body_is_rejected() -> None:
    handler: Handler = lambda request: _json_response(["not", "an", "object"])  # noqa: E731

    async with _client(handler) as client:
        with pytest.raises(ApiResponseError) as failure:
            await client.redeem_bootstrap_code("bootstrap-code", "http://localhost:5310/bootstrap")

    assert "expected a JSON object" in str(failure.value)


def test_no_dto_declares_a_token_shaped_field() -> None:
    """AC11, structurally: no DTO in ``api.dto`` can carry a token.

    Introspected from the dataclasses themselves, so adding ``refresh_token`` to any DTO fails
    here rather than silently putting a token in Python state.
    """
    declared = {
        f"{dataclass_type.__name__}.{field.name}"
        for _, dataclass_type in vars(api.dto).items()
        if dataclasses.is_dataclass(dataclass_type)
        for field in dataclasses.fields(dataclass_type)
    }

    assert declared, "api.dto must declare its DTOs for this assertion to mean anything"
    token_shaped = sorted(
        name for name in declared if name.split(".")[-1] in TOKEN_SHAPED_KEYS
    )
    assert token_shaped == []
