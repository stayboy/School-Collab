"""Route-level tests for sign-in, the session cookie, bootstrap redemption and logout (B8).

Uses FastAPI's ``TestClient`` plus ``app.dependency_overrides`` — no container, no Docker, no
Keycloak: the whole chain route -> typed client -> stub transport -> page/redirect is covered the
way B2's route tests are. The stub records every request, because the wire contract *is* the
assertion: the ``redirectUri`` the portal sends, the ``redirect_uri`` it redirects to, and the
cookie it sets are all pinned here.
"""

from __future__ import annotations

import json
import re
from collections.abc import Callable

import httpx
import pytest
from fastapi.testclient import TestClient

import app as portal_app
from api import AuthApiClient, AuthServiceEndpoint

STUB_ENDPOINT = AuthServiceEndpoint(service="auth", base_url="http://auth.test", env_var="test")

#: The app callback B1's challenge handler mints (path + the nested ReturnUrl query), which the
#: portal must hand to the auth service byte-for-byte.
CALLBACK = "http://localhost:5300/signin-handshake?ReturnUrl=%2Fadmin"

PORTAL_BASE_URL = "http://localhost:5400"
PORTAL_BOOTSTRAP_URI = f"{PORTAL_BASE_URL}/bootstrap"
ALLOWLIST = "http://localhost:5300/signin-handshake;https://localhost:7300/signin-handshake"

ATTACKER = "https://attacker.example/signin-handshake"

Handler = Callable[[httpx.Request], httpx.Response]

_TOKEN_PATTERN = re.compile(r'"antiforgery_token":"([^"]+)"')


def _json_response(payload: object, status_code: int = 200) -> httpx.Response:
    return httpx.Response(
        status_code, json=payload, headers={"content-type": "application/json"}
    )


def _stub_override(handler: Handler):
    """A dependency override serving ``handler`` from a MockTransport, recording requests."""

    async def override() -> AuthApiClient:
        return AuthApiClient(
            httpx.AsyncClient(transport=httpx.MockTransport(handler)), STUB_ENDPOINT
        )

    return override


def _portal_environment(monkeypatch: pytest.MonkeyPatch) -> None:
    """The AppHost's fan-out for this portal (B3/B5b), as the routes read it."""
    monkeypatch.setenv("services__auth__http__0", "http://auth.test")
    monkeypatch.setenv(portal_app.PUBLIC_BASE_URL_KEY, PORTAL_BASE_URL)
    monkeypatch.setenv(portal_app.APP_CALLBACK_PREFIXES_KEY, ALLOWLIST)


def _set_cookie(response: httpx.Response) -> str:
    return response.headers.get("set-cookie", "")


def _exchange_handler(seen: list[httpx.Request]) -> Handler:
    def handler(request: httpx.Request) -> httpx.Response:
        seen.append(request)
        return _json_response(
            {"code": "one-time-code", "sessionId": "session-9", "expiresInSeconds": 300}
        )

    return handler


def test_login_post_exchanges_credentials_and_names_the_validated_return_uri(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    _portal_environment(monkeypatch)
    seen: list[httpx.Request] = []
    portal_app.app.dependency_overrides[portal_app.get_auth_api_client] = _stub_override(
        _exchange_handler(seen)
    )
    try:
        with TestClient(portal_app.app) as client:
            token = _TOKEN_PATTERN.search(
                client.get("/login", params={"return_uri": CALLBACK}).text
            )
            assert token is not None
            response = client.post(
                "/login",
                json={
                    "username": "teacher-7",
                    "password": "hunter2",
                    "antiforgery_token": token.group(1),
                    "return_uri": CALLBACK,
                },
            )
    finally:
        portal_app.app.dependency_overrides.clear()

    assert response.status_code == 200
    # The browser is told where to go — the app callback carrying the one-time code. The
    # callback already carries a query, so the code is appended rather than assumed.
    assert response.json() == {"redirect_uri": f"{CALLBACK}&code=one-time-code"}
    cookie = _set_cookie(response)
    assert f"{portal_app.SESSION_COOKIE_NAME}=session-9" in cookie
    assert "httponly" in cookie.lower() and "samesite=lax" in cookie.lower()

    # The auth service got the credentials and the *identical* return_uri (round A's redemption
    # binds the code to that exact string).
    assert len(seen) == 1
    assert str(seen[0].url) == "http://auth.test/auth/exchange"
    assert json.loads(seen[0].content) == {
        "username": "teacher-7",
        "password": "hunter2",
        "redirectUri": CALLBACK,
    }


def test_login_post_requires_a_one_time_antiforgery_token(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    _portal_environment(monkeypatch)
    seen: list[httpx.Request] = []
    portal_app.app.dependency_overrides[portal_app.get_auth_api_client] = _stub_override(
        _exchange_handler(seen)
    )
    try:
        with TestClient(portal_app.app) as client:
            without = client.post(
                "/login",
                json={"username": "u", "password": "p", "return_uri": CALLBACK},
            )
            token = _TOKEN_PATTERN.search(
                client.get("/login", params={"return_uri": CALLBACK}).text
            )
            assert token is not None
            body = {
                "username": "u",
                "password": "p",
                "antiforgery_token": token.group(1),
                "return_uri": CALLBACK,
            }
            first = client.post("/login", json=body)
            replay = client.post("/login", json=body)
    finally:
        portal_app.app.dependency_overrides.clear()

    assert without.status_code == 400
    assert without.json() == {"error": "antiforgery_rejected"}
    assert _set_cookie(without) == "" and "redirect_uri" not in without.json()

    assert first.status_code == 200
    # Single use: the token is consumed by the attempt, so a replayed body is refused.
    assert replay.status_code == 400
    assert replay.json() == {"error": "antiforgery_rejected"}
    assert _set_cookie(replay) == ""
    assert len(seen) == 1  # the replay never reached the auth service


def test_login_post_requires_a_json_body(monkeypatch: pytest.MonkeyPatch) -> None:
    _portal_environment(monkeypatch)
    seen: list[httpx.Request] = []
    portal_app.app.dependency_overrides[portal_app.get_auth_api_client] = _stub_override(
        _exchange_handler(seen)
    )
    try:
        with TestClient(portal_app.app) as client:
            token = _TOKEN_PATTERN.search(
                client.get("/login", params={"return_uri": CALLBACK}).text
            )
            assert token is not None
            # What a cross-site HTML form posts: no CORS preflight can make it application/json,
            # which is why the media type is checked ahead of everything else (spec §14).
            response = client.post(
                "/login",
                content=f"username=u&password=p&antiforgery_token={token.group(1)}",
                headers={"content-type": "application/x-www-form-urlencoded"},
            )
    finally:
        portal_app.app.dependency_overrides.clear()

    assert response.status_code == 415
    assert response.json() == {"error": "unsupported_media_type"}
    assert _set_cookie(response) == ""
    assert seen == []


def test_login_post_refuses_a_non_allowlisted_return_uri_without_redirecting_to_it(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    _portal_environment(monkeypatch)
    seen: list[httpx.Request] = []
    portal_app.app.dependency_overrides[portal_app.get_auth_api_client] = _stub_override(
        _exchange_handler(seen)
    )
    try:
        with TestClient(portal_app.app) as client:
            token = _TOKEN_PATTERN.search(
                client.get("/login", params={"return_uri": CALLBACK}).text
            )
            assert token is not None
            response = client.post(
                "/login",
                json={
                    "username": "u",
                    "password": "p",
                    "antiforgery_token": token.group(1),
                    "return_uri": ATTACKER,
                },
            )
    finally:
        portal_app.app.dependency_overrides.clear()

    assert response.status_code == 200
    # The only URL the browser is ever sent to is the portal's own card page: the rejected
    # address is neither redirected to nor reflected back.
    assert response.json() == {"redirect_uri": "/login?error=return_uri_not_allowed"}
    assert ATTACKER not in response.text
    assert _set_cookie(response) == ""
    assert seen == []  # refused before the credential exchange, so no code was ever minted


def test_login_get_renders_the_card_for_a_non_allowlisted_return_uri(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    _portal_environment(monkeypatch)

    with TestClient(portal_app.app) as client:
        response = client.get("/login", params={"return_uri": ATTACKER})

    assert response.status_code == 200  # a page, never a redirect and never a 500
    assert "Return address refused" in response.text
    assert ATTACKER not in response.text
    assert "antiforgery_token" not in response.text  # no form, so no token to spend


def test_login_post_renders_the_card_page_for_a_refused_submit(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    _portal_environment(monkeypatch)
    portal_app.app.dependency_overrides[portal_app.get_auth_api_client] = _stub_override(
        lambda request: _json_response({"error": "invalid_credentials"}, 401)
    )
    try:
        with TestClient(portal_app.app) as client:
            token = _TOKEN_PATTERN.search(
                client.get("/login", params={"return_uri": CALLBACK}).text
            )
            assert token is not None
            response = client.post(
                "/login",
                json={
                    "username": "u",
                    "password": "wrong",
                    "antiforgery_token": token.group(1),
                    "return_uri": CALLBACK,
                },
            )
            card = client.get("/login", params={"return_uri": CALLBACK, "error": "invalid_credentials"})
    finally:
        portal_app.app.dependency_overrides.clear()

    assert response.status_code == 200
    assert response.json()["redirect_uri"].startswith("/login?")
    assert "invalid_credentials" in response.json()["redirect_uri"]
    assert _set_cookie(response) == ""
    # ... and that page is the AC10 card the user actually sees.
    assert card.status_code == 200
    assert "The username or password was not recognized." in card.text


def test_login_page_hands_passkey_sign_in_to_the_auth_service_with_the_portals_own_origin(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    _portal_environment(monkeypatch)

    with TestClient(portal_app.app) as client:
        page = client.get("/login", params={"return_uri": CALLBACK}).text

    # B6's pin: the portal's own bootstrap URI — the one portal URL the auth service's allowlist
    # carries, and (sharing its origin) the value it treats as the portal's own sign-in.
    expected = (
        f"http://auth.test{portal_app.PASSKEY_LOGIN_PATH}"
        f"?{portal_app.PASSKEY_APP_REDIRECT_PARAMETER}="
        "http%3A%2F%2Flocalhost%3A5400%2Fbootstrap"
    )
    assert expected in page


def test_login_page_omits_the_passkey_button_without_the_portals_base_url(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    _portal_environment(monkeypatch)
    monkeypatch.delenv(portal_app.PUBLIC_BASE_URL_KEY, raising=False)

    with TestClient(portal_app.app) as client:
        page = client.get("/login", params={"return_uri": CALLBACK}).text

    assert "Sign in with passkey" not in page
    assert "AuthPortal:PublicBaseUrl" in page


def test_bootstrap_redeems_the_code_with_the_portals_own_uri_and_continues_to_the_app(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    _portal_environment(monkeypatch)
    seen: list[httpx.Request] = []

    def handler(request: httpx.Request) -> httpx.Response:
        seen.append(request)
        return _json_response(
            {
                "sessionId": "session-9",
                "appRedirectUri": CALLBACK,
                "appCode": "app-code",
            }
        )

    portal_app.app.dependency_overrides[portal_app.get_auth_api_client] = _stub_override(handler)
    try:
        with TestClient(portal_app.app) as client:
            response = client.get(
                "/bootstrap", params={"code": "boot-code"}, follow_redirects=False
            )
    finally:
        portal_app.app.dependency_overrides.clear()

    assert response.status_code == 302
    # The D6 continuation the auth service returned, handed onward to the originating app.
    assert response.headers["location"] == f"{CALLBACK}&code=app-code"
    assert f"{portal_app.SESSION_COOKIE_NAME}=session-9" in _set_cookie(response)

    # B6's normalized spelling, exactly: the URI the bootstrap code was bound to.
    assert json.loads(seen[0].content) == {
        "code": "boot-code",
        "redirectUri": PORTAL_BOOTSTRAP_URI,
    }


def test_bootstrap_sends_a_portal_own_sign_in_to_the_portal_landing(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    _portal_environment(monkeypatch)
    portal_app.app.dependency_overrides[portal_app.get_auth_api_client] = _stub_override(
        lambda request: _json_response({"sessionId": "session-9"})
    )
    try:
        with TestClient(portal_app.app) as client:
            response = client.get(
                "/bootstrap", params={"code": "boot-code"}, follow_redirects=False
            )
    finally:
        portal_app.app.dependency_overrides.clear()

    assert response.status_code == 302
    assert response.headers["location"] == "/"
    assert f"{portal_app.SESSION_COOKIE_NAME}=session-9" in _set_cookie(response)


def test_bootstrap_renders_a_card_when_the_code_is_rejected(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    _portal_environment(monkeypatch)
    portal_app.app.dependency_overrides[portal_app.get_auth_api_client] = _stub_override(
        lambda request: _json_response({"error": "invalid_code"}, 404)
    )
    try:
        with TestClient(portal_app.app) as client:
            response = client.get(
                "/bootstrap", params={"code": "used"}, follow_redirects=False
            )
    finally:
        portal_app.app.dependency_overrides.clear()

    assert response.status_code == 200
    assert "Sign-in handoff rejected" in response.text
    assert _set_cookie(response) == ""


def test_bootstrap_refuses_a_continuation_target_outside_the_allowlist(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    _portal_environment(monkeypatch)
    portal_app.app.dependency_overrides[portal_app.get_auth_api_client] = _stub_override(
        lambda request: _json_response(
            {"sessionId": "session-9", "appRedirectUri": ATTACKER, "appCode": "app-code"}
        )
    )
    try:
        with TestClient(portal_app.app) as client:
            response = client.get(
                "/bootstrap", params={"code": "boot-code"}, follow_redirects=False
            )
    finally:
        portal_app.app.dependency_overrides.clear()

    # Defense in depth: the auth service validated the persisted target, and the portal does not
    # redirect a browser anywhere it cannot vouch for either.
    assert response.status_code == 200
    assert "Return address refused" in response.text
    assert ATTACKER not in response.text
    assert _set_cookie(response) == ""


def test_bootstrap_renders_a_card_when_the_portals_base_url_is_not_configured(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    _portal_environment(monkeypatch)
    monkeypatch.delenv(portal_app.PUBLIC_BASE_URL_KEY, raising=False)
    seen: list[httpx.Request] = []
    portal_app.app.dependency_overrides[portal_app.get_auth_api_client] = _stub_override(
        lambda request: seen.append(request) or _json_response({"sessionId": "session-9"})
    )
    try:
        with TestClient(portal_app.app) as client:
            response = client.get("/bootstrap", params={"code": "boot-code"})
    finally:
        portal_app.app.dependency_overrides.clear()

    assert response.status_code == 200
    assert "Passkey sign-in unavailable" in response.text
    assert seen == []  # nothing to redeem against: the code is bound to a URI we cannot name


def test_session_ended_clears_the_cookie_and_renders_the_card(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    _portal_environment(monkeypatch)
    portal_app.app.dependency_overrides[portal_app.get_auth_api_client] = _stub_override(
        lambda request: _json_response({"error": "session_ended"}, 410)
    )
    try:
        with TestClient(portal_app.app) as client:
            response = client.get(
                "/", headers={"cookie": f"{portal_app.SESSION_COOKIE_NAME}=session-9"}
            )
    finally:
        portal_app.app.dependency_overrides.clear()

    assert response.status_code == 200
    assert "Session ended" in response.text
    assert "Your sign-in expired or was revoked." in response.text
    assert "session-9" not in response.text  # the opaque id is never echoed back
    cleared = _set_cookie(response)
    assert f'{portal_app.SESSION_COOKIE_NAME}=""' in cleared
    assert "max-age=0" in cleared.lower()


def test_index_clears_the_cookie_when_the_session_is_unknown(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    _portal_environment(monkeypatch)
    portal_app.app.dependency_overrides[portal_app.get_auth_api_client] = _stub_override(
        lambda request: _json_response({"error": "session_not_found"}, 404)
    )
    try:
        with TestClient(portal_app.app) as client:
            response = client.get(
                "/", headers={"cookie": f"{portal_app.SESSION_COOKIE_NAME}=stale"}
            )
    finally:
        portal_app.app.dependency_overrides.clear()

    assert response.status_code == 200
    assert "Session no longer valid" in response.text
    assert f'{portal_app.SESSION_COOKIE_NAME}=""' in _set_cookie(response)


def test_logout_revokes_the_session_and_redirects_to_the_end_session_url(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    _portal_environment(monkeypatch)
    seen: list[httpx.Request] = []
    # D13 option (ii): the auth service builds the end-session URL with both parameters — the id
    # token hint and the registered portal landing URI. The portal must forward it VERBATIM: the
    # encoded `post_logout_redirect_uri` below is exactly what Keycloak matches byte-for-byte, so
    # any parse-then-rebuild or re-encode would break the logout.
    end_session_url = (
        "https://keycloak.test/realms/school-collab/protocol/openid-connect/logout"
        "?id_token_hint=eyJhbGciOiJSUzI1NiJ9.hint-value.sig"
        "&post_logout_redirect_uri=http%3A%2F%2Flocalhost%3A5700%2F"
    )

    def handler(request: httpx.Request) -> httpx.Response:
        seen.append(request)
        return _json_response({"endSessionUrl": end_session_url})

    portal_app.app.dependency_overrides[portal_app.get_auth_api_client] = _stub_override(handler)
    try:
        with TestClient(portal_app.app) as client:
            response = client.get(
                "/logout",
                headers={"cookie": f"{portal_app.SESSION_COOKIE_NAME}=session-9"},
                follow_redirects=False,
            )
    finally:
        portal_app.app.dependency_overrides.clear()

    assert response.status_code == 302
    # Both parameters really are in the stubbed URL, so the byte-identity assertion below is not
    # passing over a parameter-free string.
    assert "id_token_hint=" in end_session_url
    assert "post_logout_redirect_uri=" in end_session_url
    assert response.headers["location"] == end_session_url
    assert f'{portal_app.SESSION_COOKIE_NAME}=""' in _set_cookie(response)
    # The revocation is the auth service's job: the portal never touches Keycloak's endpoints.
    assert [(request.method, str(request.url)) for request in seen] == [
        ("DELETE", "http://auth.test/auth/session/session-9")
    ]


def test_logout_without_a_session_lands_on_the_portal_and_clears_the_cookie(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    _portal_environment(monkeypatch)
    seen: list[httpx.Request] = []
    portal_app.app.dependency_overrides[portal_app.get_auth_api_client] = _stub_override(
        lambda request: seen.append(request) or _json_response({"endSessionUrl": "http://x"})
    )
    try:
        with TestClient(portal_app.app) as client:
            response = client.get("/logout", follow_redirects=False)
    finally:
        portal_app.app.dependency_overrides.clear()

    assert response.status_code == 302
    assert response.headers["location"] == "/"
    assert seen == []


def test_logout_renders_a_card_when_the_auth_service_cannot_revoke(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    _portal_environment(monkeypatch)
    portal_app.app.dependency_overrides[portal_app.get_auth_api_client] = _stub_override(
        lambda request: _json_response({"error": "upstream_unreachable"}, 502)
    )
    try:
        with TestClient(portal_app.app) as client:
            response = client.get(
                "/logout",
                headers={"cookie": f"{portal_app.SESSION_COOKIE_NAME}=session-9"},
                follow_redirects=False,
            )
    finally:
        portal_app.app.dependency_overrides.clear()

    # Fail-closed: the local sign-out stands and says so — never a silent half-logout.
    assert response.status_code == 200
    assert "Identity provider unavailable" in response.text
    assert f'{portal_app.SESSION_COOKIE_NAME}=""' in _set_cookie(response)


def test_login_page_reports_the_unconfigured_allowlist_as_a_refusal(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    _portal_environment(monkeypatch)
    monkeypatch.delenv(portal_app.APP_CALLBACK_PREFIXES_KEY, raising=False)

    with TestClient(portal_app.app) as client:
        page = client.get("/login", params={"return_uri": CALLBACK})

    # No allowlist is deny-everything, not accept-everything: the portal renders the card.
    assert page.status_code == 200
    assert "Return address refused" in page.text
    assert "antiforgery_token" not in page.text


def _sign_in_and_read_the_cookie(monkeypatch: pytest.MonkeyPatch) -> str:
    """Sign in through the form and return the raw `Set-Cookie` header the portal sent."""
    seen: list[httpx.Request] = []
    portal_app.app.dependency_overrides[portal_app.get_auth_api_client] = _stub_override(
        _exchange_handler(seen)
    )
    try:
        with TestClient(portal_app.app) as client:
            token = _TOKEN_PATTERN.search(
                client.get("/login", params={"return_uri": CALLBACK}).text
            )
            assert token is not None
            response = client.post(
                "/login",
                json={
                    "username": "u",
                    "password": "p",
                    "antiforgery_token": token.group(1),
                    "return_uri": CALLBACK,
                },
            )
    finally:
        portal_app.app.dependency_overrides.clear()

    assert response.status_code == 200
    return _set_cookie(response)


def test_session_cookie_has_no_secure_flag_over_http(monkeypatch: pytest.MonkeyPatch) -> None:
    # The dev endpoints are plain http, where a Secure cookie would never be sent back: the flag
    # follows the configured scheme rather than a constant.
    _portal_environment(monkeypatch)

    cookie = _sign_in_and_read_the_cookie(monkeypatch)

    assert f"{portal_app.SESSION_COOKIE_NAME}=session-9" in cookie
    assert "secure" not in cookie.lower()


def test_session_cookie_is_secure_over_https(monkeypatch: pytest.MonkeyPatch) -> None:
    # A production deployment reached over https must not ship a cookie the browser would also
    # send over http.
    _portal_environment(monkeypatch)
    monkeypatch.setenv(portal_app.PUBLIC_BASE_URL_KEY, "https://portal.school-collab.example")

    cookie = _sign_in_and_read_the_cookie(monkeypatch)

    assert f"{portal_app.SESSION_COOKIE_NAME}=session-9" in cookie
    assert "secure" in cookie.lower()
