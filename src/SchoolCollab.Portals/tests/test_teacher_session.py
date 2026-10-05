"""Session-path tests for the teacher portal (round ``portal-session-adoption``, D8/D19).

FastAPI's ``TestClient`` plus ``app.dependency_overrides`` — the container-free shape
``test_teacher_views.py`` established, now with **two** stub transports: the assignments-api
(which that file already drives) and the auth service's session surface. No server, no Docker, no
Keycloak, no browser.

The wire contract *is* the assertion: the ``X-Portal-Session`` header the portal presents on every
teacher-route call, the ``redirectUri`` it redeems a handoff code with, the cookie it sets, clears
or keeps per D18's two dead-session states, and the opaque ``end_session`` URL it hands the browser
byte-for-byte. Every stubbed request is recorded — a header that is *claimed* to be threaded is not
a header on the wire.

The gate's own rule ladder gets its cases here too: rule 3 (the target-keyed redirect) both ways,
and rule 2 (the dev posture, with no sign-in target configured) unchanged — including the
``MissingConfigurationError`` refusal and the promise that the session path never consults the dev
teacher id.
"""

from __future__ import annotations

import json
import os
from collections.abc import Callable, Iterator
from contextlib import contextmanager
from typing import Any
from urllib.parse import quote

import httpx
import pytest
from fastapi.testclient import TestClient

import app as portal_app
from api import (
    AssignmentsApiClient,
    AuthApiClient,
    AuthServiceEndpoint,
    ServiceEndpoint,
    TokenInResponseError,
)

Handler = Callable[[httpx.Request], httpx.Response]

ASSIGNMENTS_ENDPOINT = ServiceEndpoint(
    service="assignments-api", base_url="http://assignments-api.test", env_var="test"
)
AUTH_ENDPOINT = AuthServiceEndpoint(service="auth", base_url="http://auth.test", env_var="test")

SESSION_COOKIE = portal_app.SESSION_COOKIE_NAME
SESSION_ID = "opaque-session-id-7"

ASSIGNMENT_ID = "11111111-1111-1111-1111-111111111111"
STUDENT_ID = "22222222-2222-2222-2222-222222222222"
CONFIGURED_DEV_TEACHER_ID = "33333333-3333-3333-3333-333333333333"
SESSION_TEACHER_ID = "55555555-5555-5555-5555-555555555555"

QUEUE_PATH = f"/teacher/assignments/{ASSIGNMENT_ID}"
SUBMISSION_PATH = f"{QUEUE_PATH}/students/{STUDENT_ID}"

PORTAL_BASE_URL = "http://portals.test"
CALLBACK_URI = f"{PORTAL_BASE_URL}/auth/callback"
SIGN_IN_URL = "http://auth-portal.test/login"
END_SESSION_URL = (
    "http://keycloak.test/realms/school-collab/protocol/openid-connect/logout"
    "?id_token_hint=opaque&post_logout_redirect_uri=http%3A%2F%2Flocalhost%3A5700%2F"
)

#: The D18 session read's body, camelCase as the auth service serializes its record.
SESSION_PAYLOAD: dict[str, Any] = {
    "sessionId": SESSION_ID,
    "tenantId": "00000000-0000-0000-0000-000000000002",
    "tenantName": "Dev School",
    "tenantType": "School",
    "teacherId": SESSION_TEACHER_ID,
    "roles": ["teacher"],
    "expiresInSeconds": 3600,
}

ASSIGNMENT_PAYLOAD: list[dict[str, Any]] = [
    {
        "id": ASSIGNMENT_ID,
        "title": "Reading response",
        "status": "Published",
        "dueDate": "2026-10-01",
        "createdAt": "2026-09-21T09:00:00Z",
    }
]


# ── stub transports ────────────────────────────────────────────────────────────────


def _assignments_override(handler: Handler):
    """The assignments-api seam ``test_teacher_views.py`` drives, reused verbatim in shape."""

    async def override() -> AssignmentsApiClient:
        return AssignmentsApiClient(
            httpx.AsyncClient(transport=httpx.MockTransport(handler)), ASSIGNMENTS_ENDPOINT
        )

    return override


def _auth_override(handler: Handler):
    """The auth-service seam: one dependency override covers all three session routes."""

    async def override() -> AuthApiClient:
        return AuthApiClient(
            httpx.AsyncClient(transport=httpx.MockTransport(handler)), AUTH_ENDPOINT
        )

    return override


def _redeem_ok(request: httpx.Request) -> httpx.Response:
    return httpx.Response(200, json={"sessionId": SESSION_ID})


def _session_ok(request: httpx.Request) -> httpx.Response:
    return httpx.Response(200, json=SESSION_PAYLOAD)


def _revoke_ok(request: httpx.Request) -> httpx.Response:
    return httpx.Response(200, json={"endSessionUrl": END_SESSION_URL})


def _auth_transport(
    seen: list[httpx.Request],
    *,
    redeem: Handler = _redeem_ok,
    session: Handler = _session_ok,
    revoke: Handler = _revoke_ok,
) -> Handler:
    """A recording transport for the auth service's three session routes.

    Each route defaults to its happy path, so a test overrides only the one it is about — and any
    call the portal should not have made fails loudly instead of silently answering.
    """

    def handler(request: httpx.Request) -> httpx.Response:
        seen.append(request)
        path = request.url.path
        if path == "/auth/handshake/session":
            assert request.method == "POST", f"unexpected method for {path}: {request.method}"
            return redeem(request)
        if path.startswith("/auth/session/"):
            if request.method == "DELETE":
                return revoke(request)
            assert request.method == "GET", f"unexpected method for {path}: {request.method}"
            return session(request)
        raise AssertionError(f"the portal called an un-stubbed auth route: {request.method} {path}")

    return handler


def _assignments_transport(seen: list[httpx.Request], payload: Any = ASSIGNMENT_PAYLOAD) -> Handler:
    def handler(request: httpx.Request) -> httpx.Response:
        seen.append(request)
        return httpx.Response(200, json=payload)

    return handler


def _unreachable() -> Handler:
    def handler(request: httpx.Request) -> httpx.Response:
        raise httpx.ConnectError("connection refused", request=request)

    return handler


@contextmanager
def _portal(
    *,
    assignments: Handler | None = None,
    auth: Handler | None = None,
) -> Iterator[TestClient]:
    """A ``TestClient`` with one or both upstreams stubbed by a ``MockTransport``."""
    if assignments is not None:
        portal_app.app.dependency_overrides[portal_app.get_assignments_client] = (
            _assignments_override(assignments)
        )
    if auth is not None:
        portal_app.app.dependency_overrides[portal_app.get_auth_client] = _auth_override(auth)
    try:
        with TestClient(portal_app.app) as client:
            yield client
    finally:
        portal_app.app.dependency_overrides.clear()


def _session_headers() -> dict[str, str]:
    """Present the portal's session cookie as a request header.

    Set explicitly rather than through the client's cookie jar (the auth portal's test
    convention): what the portal *does* with the cookie — presents it, clears it, keeps it — is
    the assertion here, and a jar deciding part of that for us would weaken it.
    """
    return {"cookie": f"{SESSION_COOKIE}={SESSION_ID}"}


def _cookie_header(response: httpx.Response) -> str:
    return response.headers.get("set-cookie", "")


def _presented_session(request: httpx.Request) -> str | None:
    return request.headers.get("X-Portal-Session")


def _no_sign_in_target(monkeypatch: pytest.MonkeyPatch) -> None:
    """Rule 2's posture: no ``PORTALS_LOGIN_URL``, so the gate does not act.

    Asserted rather than assumed (AC7): every pre-existing dev-path expectation in this module
    depends on it, and the AppHost's real fan-out would silently make the gate redirect instead.
    """
    monkeypatch.delenv(portal_app.LOGIN_URL_KEY, raising=False)
    assert os.environ.get(portal_app.LOGIN_URL_KEY) is None


# ── the bootstrap: /auth/callback ──────────────────────────────────────────────────


def test_handshake_redemption_sets_the_cookie_and_lands_on_the_teacher_list(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """The password path's landing: redeem the D6 code, set our OWN cookie, go to /teacher."""
    _no_sign_in_target(monkeypatch)
    monkeypatch.setenv(portal_app.PUBLIC_BASE_URL_KEY, PORTAL_BASE_URL)
    auth_seen: list[httpx.Request] = []
    assignments_seen: list[httpx.Request] = []

    with _portal(
        assignments=_assignments_transport(assignments_seen),
        auth=_auth_transport(auth_seen),
    ) as client:
        response = client.get(
            "/auth/callback", params={"code": "one-time-code"}, follow_redirects=False
        )
        cookie = client.cookies.get(SESSION_COOKIE)
        page = client.get("/teacher")

    assert response.status_code == 302
    assert response.headers["location"] == "/teacher"
    assert f"{SESSION_COOKIE}={SESSION_ID}" in _cookie_header(response)
    header = _cookie_header(response).lower()
    assert "httponly" in header and "samesite=lax" in header

    # The redemption carried the code and the URI it was bound to — this portal's own callback.
    redemption = next(r for r in auth_seen if r.url.path == "/auth/handshake/session")
    assert json.loads(redemption.read().decode()) == {
        "code": "one-time-code",
        "redirectUri": CALLBACK_URI,
    }

    assert cookie == SESSION_ID
    assert page.status_code == 200
    assert "Reading response" in page.text
    assert f"Signed in as teacher {SESSION_TEACHER_ID}" in page.text
    assert "Dev School" in page.text


def test_passkey_continuation_lands_on_the_same_callback(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """The passkey path needs no route of its own: the auth portal 302s here with the same code.

    ``PasskeyEndpoints.Complete`` mints the D6 continuation for a non-portal-origin sign-in, the
    auth portal's ``/bootstrap`` re-validates it against its own allowlist and redirects the
    browser to ``{this callback}?code=…`` — so this is the identical redemption.
    """
    _no_sign_in_target(monkeypatch)
    monkeypatch.setenv(portal_app.PUBLIC_BASE_URL_KEY, PORTAL_BASE_URL)
    auth_seen: list[httpx.Request] = []

    with _portal(
        assignments=_assignments_transport([]),
        auth=_auth_transport(auth_seen),
    ) as client:
        response = client.get(
            "/auth/callback", params={"code": "passkey-continuation"}, follow_redirects=False
        )

    assert response.status_code == 302
    assert response.headers["location"] == "/teacher"
    assert client.cookies.get(SESSION_COOKIE) == SESSION_ID
    assert [r.url.path for r in auth_seen] == ["/auth/handshake/session"]


def test_a_replayed_or_expired_code_sets_no_cookie(monkeypatch: pytest.MonkeyPatch) -> None:
    """A refused handoff is a card at HTTP 200, never a half-signed-in browser."""
    _no_sign_in_target(monkeypatch)
    monkeypatch.setenv(portal_app.PUBLIC_BASE_URL_KEY, PORTAL_BASE_URL)

    with _portal(
        assignments=_assignments_transport([]),
        auth=_auth_transport(
            [],
            redeem=lambda request: httpx.Response(404, json={"error": "invalid_code"}),
        ),
    ) as client:
        response = client.get("/auth/callback", params={"code": "replayed"})

    assert response.status_code == 200
    assert "Sign-in handoff rejected" in response.text
    assert "invalid_code" in response.text
    assert SESSION_COOKIE not in response.cookies


def test_a_callback_without_a_code_is_refused_without_an_upstream_call(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    _no_sign_in_target(monkeypatch)
    auth_seen: list[httpx.Request] = []

    with _portal(auth=_auth_transport(auth_seen)) as client:
        response = client.get("/auth/callback")

    assert response.status_code == 200
    assert "handshake_code_missing" in response.text
    assert auth_seen == []


# ── the header on every teacher-route call ─────────────────────────────────────────


@pytest.mark.parametrize(
    ("path", "payload"),
    [
        ("/teacher", ASSIGNMENT_PAYLOAD),
        (QUEUE_PATH, []),
        (SUBMISSION_PATH, {"submissionId": "sub-1", "versions": [], "review": None}),
    ],
    ids=["list", "queue", "submission"],
)
def test_every_teacher_route_presents_the_session_header(
    monkeypatch: pytest.MonkeyPatch, path: str, payload: Any
) -> None:
    """AC6: asserted at the transport, per route — not at a header the route claims to set."""
    _no_sign_in_target(monkeypatch)
    assignments_seen: list[httpx.Request] = []
    auth_seen: list[httpx.Request] = []

    with _portal(
        assignments=_assignments_transport(assignments_seen, payload),
        auth=_auth_transport(auth_seen),
    ) as client:
        response = client.get(path, headers=_session_headers())

    assert response.status_code == 200
    assert len(assignments_seen) == 1
    assert _presented_session(assignments_seen[0]) == SESSION_ID
    # The identity read is what makes the header trustworthy: one read, the session's own claims.
    assert [r.url.path for r in auth_seen] == [f"/auth/session/{SESSION_ID}"]


def test_the_ward_route_presents_no_session_header(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Only the teacher surface threads the session — the ward route is untouched (D19's scope)."""
    _no_sign_in_target(monkeypatch)
    assignments_seen: list[httpx.Request] = []

    with _portal(
        assignments=_assignments_transport(assignments_seen),
        auth=_auth_transport([]),
    ) as client:
        response = client.get("/", headers=_session_headers())

    assert response.status_code == 200
    assert assignments_seen
    assert all(_presented_session(request) is None for request in assignments_seen)


# ── the two dead-session states vs. a degraded read ────────────────────────────────


@pytest.mark.parametrize("status", [410, 404], ids=["session-ended", "session-not-found"])
def test_a_dead_session_clears_the_cookie_and_renders_the_card(
    monkeypatch: pytest.MonkeyPatch, status: int
) -> None:
    """AC6: the card is a page at 200, the cookie is cleared, and the route body never runs."""
    _no_sign_in_target(monkeypatch)
    cardinality = "session_ended" if status == 410 else "session_not_found"
    assignments_seen: list[httpx.Request] = []

    with _portal(
        assignments=_assignments_transport(assignments_seen),
        auth=_auth_transport(
            [],
            session=lambda request: httpx.Response(status, json={"error": cardinality}),
        ),
    ) as client:
        response = client.get("/teacher", headers=_session_headers())

    assert response.status_code == 200
    assert cardinality in response.text
    cleared = _cookie_header(response).lower()
    # ``path=/`` is asserted with the value: a cookie cleared on another path would survive on
    # the routes that read it, so value + expiry alone cannot see the regression.
    assert f'{SESSION_COOKIE}=""' in cleared and "max-age=0" in cleared and "path=/" in cleared
    assert assignments_seen == []  # the route body never ran; a page, never a partial render


def test_a_degraded_session_read_renders_the_card_and_keeps_the_cookie(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """D18: the session's state is UNKNOWN — discarding a live session would sign a teacher out."""
    _no_sign_in_target(monkeypatch)
    assignments_seen: list[httpx.Request] = []

    with _portal(
        assignments=_assignments_transport(assignments_seen),
        auth=_auth_transport([], session=_unreachable()),
    ) as client:
        response = client.get("/teacher", headers=_session_headers())

    assert response.status_code == 200
    assert "unusable_response" in response.text
    assert "unreachable" in response.text
    # The kept cookie means the state is unknown, so the card may not claim the browser is
    # signed out: that badge would contradict the "state unknown" copy right above it.
    assert "No portal session" not in response.text
    assert _cookie_header(response) == ""  # no Set-Cookie: the cookie's fate is unknown
    assert assignments_seen == []  # the route body never ran


def test_a_session_read_without_an_auth_endpoint_keeps_the_cookie(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """No auth-service endpoint: the session path says so; the cookie's fate is still unknown."""
    _no_sign_in_target(monkeypatch)
    monkeypatch.delenv("services__auth__http__0", raising=False)
    monkeypatch.delenv("AUTH_HTTP", raising=False)

    with _portal(assignments=_assignments_transport([])) as client:
        response = client.get("/teacher", headers=_session_headers())

    assert response.status_code == 200
    assert "auth_service_unconfigured" in response.text
    assert _cookie_header(response) == ""


# ── dev-bypass coexistence (D4) ────────────────────────────────────────────────────


def test_the_session_path_never_consults_the_dev_teacher_id(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Rule 1: the session's own ``teacherId`` is the identity — a dev-env gap cannot degrade it."""
    _no_sign_in_target(monkeypatch)
    monkeypatch.delenv(portal_app.DEV_TEACHER_ID_KEY, raising=False)
    assignments_seen: list[httpx.Request] = []

    with _portal(
        assignments=_assignments_transport(assignments_seen, []),
        auth=_auth_transport([]),
    ) as client:
        response = client.get(QUEUE_PATH, headers=_session_headers())

    assert response.status_code == 200
    assert "MissingConfigurationError" not in response.text
    assert assignments_seen[0].url.params["teacherId"] == SESSION_TEACHER_ID


def test_the_session_less_queue_still_refuses_without_a_dev_teacher_id(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Rule 2, byte for byte: the refusal is intact and it costs no wire call."""
    _no_sign_in_target(monkeypatch)
    monkeypatch.delenv(portal_app.DEV_TEACHER_ID_KEY, raising=False)
    assignments_seen: list[httpx.Request] = []

    with _portal(assignments=_assignments_transport(assignments_seen, [])) as client:
        response = client.get(QUEUE_PATH)

    assert response.status_code == 200
    assert "review queue degraded" in response.text
    assert "MissingConfigurationError" in response.text
    assert assignments_seen == []


def test_the_dev_bypass_path_presents_no_header(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Rule 2: no session, no header — the dev bypass is the no-session dev path, nothing more."""
    _no_sign_in_target(monkeypatch)
    monkeypatch.setenv(portal_app.DEV_TEACHER_ID_KEY, CONFIGURED_DEV_TEACHER_ID)
    assignments_seen: list[httpx.Request] = []
    auth_seen: list[httpx.Request] = []

    with _portal(
        assignments=_assignments_transport(assignments_seen, []),
        auth=_auth_transport(auth_seen),
    ) as client:
        response = client.get(QUEUE_PATH)

    assert response.status_code == 200
    assert _presented_session(assignments_seen[0]) is None
    assert assignments_seen[0].url.params["teacherId"] == CONFIGURED_DEV_TEACHER_ID
    assert auth_seen == []
    assert "No portal session" in response.text


# ── the gate's rule 3, both ways ───────────────────────────────────────────────────


def test_the_gate_redirects_to_the_configured_sign_in_target(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """No cookie + ``PORTALS_LOGIN_URL`` ⇒ 302 with this portal's callback as ``return_uri``."""
    monkeypatch.setenv(portal_app.LOGIN_URL_KEY, SIGN_IN_URL)
    monkeypatch.setenv(portal_app.PUBLIC_BASE_URL_KEY, PORTAL_BASE_URL)
    assignments_seen: list[httpx.Request] = []

    with _portal(assignments=_assignments_transport(assignments_seen)) as client:
        response = client.get("/teacher", follow_redirects=False)

    assert response.status_code == 302
    assert response.headers["location"] == (
        f"{SIGN_IN_URL}?return_uri={quote(CALLBACK_URI, safe='')}"
    )
    assert assignments_seen == []  # the route body never ran


def test_the_gate_does_not_act_without_a_configured_sign_in_target(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """The dev posture: no target ⇒ today's render, and no URL is invented."""
    _no_sign_in_target(monkeypatch)
    monkeypatch.delenv(portal_app.PUBLIC_BASE_URL_KEY, raising=False)

    with _portal(assignments=_assignments_transport([])) as client:
        response = client.get("/teacher", follow_redirects=False)

    assert response.status_code == 200
    assert "No portal session" in response.text
    assert "No sign-in URL is configured" in response.text
    assert "auth-portal" not in response.text


def test_the_ward_route_is_never_gated(monkeypatch: pytest.MonkeyPatch) -> None:
    """Rule 3 is scoped to the teacher surface: the ward route keeps rendering."""
    monkeypatch.setenv(portal_app.LOGIN_URL_KEY, SIGN_IN_URL)
    monkeypatch.setenv(portal_app.PUBLIC_BASE_URL_KEY, PORTAL_BASE_URL)

    with _portal(assignments=_assignments_transport([])) as client:
        response = client.get("/", follow_redirects=False)

    assert response.status_code == 200
    assert "Ward" in response.text


def test_the_sign_in_link_falls_back_to_the_origin_the_browser_reached(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """With no configured base URL the callback is the observed origin — never an invented host."""
    monkeypatch.setenv(portal_app.LOGIN_URL_KEY, SIGN_IN_URL)
    monkeypatch.delenv(portal_app.PUBLIC_BASE_URL_KEY, raising=False)

    with _portal(assignments=_assignments_transport([])) as client:
        response = client.get("/teacher", follow_redirects=False)

    callback = f"{response.request.url.scheme}://{response.request.url.host}/auth/callback"
    assert response.status_code == 302
    assert response.headers["location"] == f"{SIGN_IN_URL}?return_uri={quote(callback, safe='')}"


# ── the affordances come from the environment, or they do not exist ────────────────


def test_the_passkey_link_is_built_from_the_auth_service_endpoint(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    _no_sign_in_target(monkeypatch)
    monkeypatch.setenv(portal_app.PUBLIC_BASE_URL_KEY, PORTAL_BASE_URL)
    monkeypatch.setenv("services__auth__http__0", "http://auth.test")

    with _portal(assignments=_assignments_transport([])) as client:
        response = client.get("/teacher")

    assert response.status_code == 200
    assert (
        "http://auth.test/auth/passkey/login?app_redirect_uri="
        + quote(CALLBACK_URI, safe="") in response.text
    )


def test_no_link_is_invented_when_the_environment_supplies_nothing(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    _no_sign_in_target(monkeypatch)
    monkeypatch.delenv(portal_app.PUBLIC_BASE_URL_KEY, raising=False)
    monkeypatch.delenv("services__auth__http__0", raising=False)
    monkeypatch.delenv("AUTH_HTTP", raising=False)

    with _portal(assignments=_assignments_transport([])) as client:
        response = client.get("/teacher")

    assert response.status_code == 200
    assert "No portal session" in response.text
    assert "passkey" not in response.text
    assert "/login" not in response.text


def test_a_signed_in_page_offers_the_logout_link(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    _no_sign_in_target(monkeypatch)
    monkeypatch.setenv(portal_app.PUBLIC_BASE_URL_KEY, PORTAL_BASE_URL)

    with _portal(
        assignments=_assignments_transport([]),
        auth=_auth_transport([]),
    ) as client:
        response = client.get("/teacher", headers=_session_headers())

    assert response.status_code == 200
    assert f"Signed in as teacher {SESSION_TEACHER_ID}" in response.text
    assert portal_app.LOGOUT_PATH in response.text


# ── logout ─────────────────────────────────────────────────────────────────────────


def test_logout_revokes_clears_and_redirects_to_the_opaque_url(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """The received URL is 302'd byte-for-byte: never parsed, re-encoded, logged or persisted."""
    _no_sign_in_target(monkeypatch)
    auth_seen: list[httpx.Request] = []

    with _portal(
        assignments=_assignments_transport([]), auth=_auth_transport(auth_seen)
    ) as client:
        response = client.get(
            portal_app.LOGOUT_PATH, headers=_session_headers(), follow_redirects=False
        )
        page = client.get("/teacher", follow_redirects=False)

    assert response.status_code == 302
    assert response.headers["location"] == END_SESSION_URL
    cleared = _cookie_header(response).lower()
    assert (
        f'{SESSION_COOKIE}=""' in cleared
        and "max-age=0" in cleared
        and "path=/" in cleared
    )
    assert [(r.method, r.url.path) for r in auth_seen] == [
        ("DELETE", f"/auth/session/{SESSION_ID}")
    ]
    # With the cookie cleared the surface is session-less, and (no sign-in target) it renders.
    assert page.status_code == 200
    assert "No portal session" in page.text


def test_logout_without_a_cookie_is_already_signed_out(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    _no_sign_in_target(monkeypatch)
    auth_seen: list[httpx.Request] = []

    with _portal(auth=_auth_transport(auth_seen)) as client:
        response = client.get(portal_app.LOGOUT_PATH, follow_redirects=False)

    assert response.status_code == 302
    assert response.headers["location"] == "/"
    assert auth_seen == []


def test_a_failed_revocation_still_clears_the_cookie(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """A logout that leaves a cookie behind is the one thing this route must never do."""
    _no_sign_in_target(monkeypatch)

    with _portal(auth=_auth_transport([], revoke=_unreachable())) as client:
        response = client.get(portal_app.LOGOUT_PATH, headers=_session_headers())

    assert response.status_code == 200
    assert "unusable_response" in response.text
    cleared = _cookie_header(response).lower()
    assert (
        f'{SESSION_COOKIE}=""' in cleared
        and "max-age=0" in cleared
        and "path=/" in cleared
    )


# ── AC11: no token is ever ingested ────────────────────────────────────────────────


def test_a_token_shaped_response_key_is_refused(monkeypatch: pytest.MonkeyPatch) -> None:
    """The auth service answering with a token is refused, at the client and at the route."""
    _no_sign_in_target(monkeypatch)
    leaked = {**SESSION_PAYLOAD, "accessToken": "header.payload.signature"}

    with _portal(
        assignments=_assignments_transport([]),
        auth=_auth_transport([], session=lambda request: httpx.Response(200, json=leaked)),
    ) as client:
        response = client.get("/teacher", headers=_session_headers())

    assert response.status_code == 200
    assert "token_in_response" in response.text
    assert "header.payload.signature" not in response.text


async def test_the_auth_client_raises_on_a_token_shaped_body() -> None:
    """The client-level half: the scan runs on every response, nested keys included."""
    handler: Handler = lambda request: httpx.Response(  # noqa: E731
        200, json={"sessionId": SESSION_ID, "roles": [{"idToken": "x"}]}
    )
    async with httpx.AsyncClient(transport=httpx.MockTransport(handler)) as http:
        client = AuthApiClient(http, AUTH_ENDPOINT)
        with pytest.raises(TokenInResponseError):
            await client.read_session(SESSION_ID)


# ── AC7(b): the wire spelling, pinned from the parsing end ──────────────────────────

#: The D18 read body's EXACT key set (AC7). The C# half of the pair
#: (``tests/SchoolCollab.Auth.Tests.Unit/SessionWireSpellingTests.cs``) pins the same set from
#: the serializing end, so a drift on either side reddens exactly one of the two pins.
SESSION_WIRE_KEYS = frozenset(
    {"sessionId", "tenantId", "tenantName", "tenantType", "teacherId", "roles", "expiresInSeconds"}
)


async def test_the_real_client_parses_the_pinned_camel_case_session_body() -> None:
    """AC7(b): the wire spelling, driven through the REAL client over exactly those keys.

    This is the Python half of the two one-way pins: the tolerance ``SessionData.from_payload``
    carries is a pin here, not an accident — the auth service must serialize exactly these keys,
    and this client must populate every ``SessionData`` field from them. The pin is a
    **contract pin** (drift detection), not a pre-change failing test: the spelling is already
    correct, and what it buys is that the two halves can no longer drift apart silently.
    """
    assert set(SESSION_PAYLOAD) == SESSION_WIRE_KEYS

    handler: Handler = lambda request: httpx.Response(200, json=SESSION_PAYLOAD)  # noqa: E731
    async with httpx.AsyncClient(transport=httpx.MockTransport(handler)) as http:
        data = await AuthApiClient(http, AUTH_ENDPOINT).read_session(SESSION_ID)

    assert data.session_id == SESSION_ID
    assert data.tenant_id == SESSION_PAYLOAD["tenantId"]
    assert data.tenant_name == "Dev School"
    assert data.tenant_type == "School"
    assert data.teacher_id == SESSION_TEACHER_ID
    assert data.roles == ("teacher",)
