"""Typed HTTP client for the auth service — the portal's only upstream.

The Python counterpart of the repo's one-client-per-surface rule (see
``documents/solution/portals-service-client-pattern.md``): one class, one method per endpoint,
DTOs in and out, transport failures translated into typed portal errors. Every portal-facing
operation is a *mediated* call to the auth service: the portal never calls Keycloak, never calls
settings-api/students-api, and never holds a token or a client secret (D6/D7/D17, AC11).

The client is deliberately **base-URL agnostic** — it is handed a resolved
:class:`AuthServiceEndpoint` rather than reading the environment itself — so it can be driven by
any transport, including ``httpx.MockTransport``: that is what makes the portal testable with no
server, no Docker and no Keycloak. The client is **async** (``httpx.AsyncClient``): created once
in the FastAPI lifespan and awaited from async route handlers.

Endpoint paths this client pins (the auth service must serve exactly these):

* ``POST /auth/exchange`` — credentials in, one-time handshake code + opaque session id out.
* ``POST /auth/bootstrap/redeem`` — the D16 passkey-path handoff: bootstrap code in, session id
  out (plus the D6 continuation when a Blazor page originated the sign-in).
* ``GET /auth/session/{session_id}`` — the claim set **as data** (D18).
* ``DELETE /auth/session/{session_id}`` — server-side revocation, ``end_session`` URL out (D13).

Failure bodies carry a typed ``error`` code and an optional ``detail`` (the auth service's own
taxonomy); the client maps the code to a typed exception and, for an unrecognized code, raises
:class:`ApiResponseError` — never a half-working page.
"""

from __future__ import annotations

import os
from collections.abc import Mapping
from dataclasses import dataclass
from typing import Any
from urllib.parse import quote

import httpx

from api.dto import BootstrapRedemption, ExchangeResult, SessionData, SessionRevocation
from api.errors import (
    ApiResponseError,
    ApiUnavailableError,
    AuthCredentialsRejectedError,
    AuthServiceError,
    AuthUpstreamError,
    AuthUserDisabledError,
    BootstrapCodeRejectedError,
    ServiceDiscoveryError,
    SessionEndedError,
    SessionNotFoundError,
    TokenInResponseError,
)

#: The Aspire resource name of the auth service (``AddUvicornApp``/``WithReference`` in the
#: AppHost inject its discovery variable).
AUTH_SERVICE = "auth"

JSON_CONTENT_TYPE = "application/json"

EXCHANGE_PATH = "/auth/exchange"
BOOTSTRAP_REDEEM_PATH = "/auth/bootstrap/redeem"
SESSION_PATH = "/auth/session/{session_id}"

#: Fields that must never appear in a portal-bound body (AC11). The client scans every decoded
#: response — nested objects included — and refuses to ingest a body carrying one.
TOKEN_SHAPED_KEYS = frozenset(
    {
        "access_token",
        "accessToken",
        "refresh_token",
        "refreshToken",
        "id_token",
        "idToken",
    }
)

#: The auth service's typed failure codes -> the portal's exception taxonomy (fail-closed: an
#: unrecognized code degrades as an unusable response, never as "probably fine").
_AUTH_ERROR_TYPES: dict[str, type[AuthServiceError]] = {
    "invalid_credentials": AuthCredentialsRejectedError,
    "disabled_user": AuthUserDisabledError,
    "keycloak_unreachable": AuthUpstreamError,
    "upstream_unreachable": AuthUpstreamError,
    "session_ended": SessionEndedError,
    "session_not_found": SessionNotFoundError,
    "invalid_code": BootstrapCodeRejectedError,
    "code_expired": BootstrapCodeRejectedError,
    "redirect_uri_mismatch": BootstrapCodeRejectedError,
    "redirect_uri_required": BootstrapCodeRejectedError,
}

# Substrings that make an environment variable "discovery-shaped" — used only to build a helpful
# diagnostic when resolution fails.
_DISCOVERY_HINTS = ("AUTH", "SERVICE", "PORTAL", "KEYCLOAK")


@dataclass(frozen=True)
class AuthServiceEndpoint:
    """Where the auth service lives, and which environment variable said so."""

    service: str
    base_url: str
    env_var: str

    @property
    def label(self) -> str:
        return f"{self.service} at {self.base_url} (via {self.env_var})"


def candidate_env_vars(service: str) -> tuple[str, ...]:
    """The environment-variable names Aspire may have used, in priority order.

    ``WithReference(...)`` injects the .NET-style ``services__<name>__http__0`` (verified in the
    ward-portal spike); the simplified ``<NAME>_HTTP`` form is still checked second.
    """
    return (f"services__{service}__http__0", f"{service.upper().replace('-', '_')}_HTTP")


def resolve_auth_service_endpoint() -> AuthServiceEndpoint:
    """Resolve the auth service's base URL from the AppHost-injected environment.

    Lives beside the client rather than in a discovery module of its own: the auth service is
    this portal's only upstream, so endpoint resolution is a property of this client.
    """
    tried = candidate_env_vars(AUTH_SERVICE)
    for name in tried:
        value = os.environ.get(name)
        if value:
            return AuthServiceEndpoint(
                service=AUTH_SERVICE, base_url=value.rstrip("/"), env_var=name
            )

    present = {
        key: value
        for key, value in os.environ.items()
        if key.lower().startswith("services") or any(hint in key.upper() for hint in _DISCOVERY_HINTS)
    }
    raise ServiceDiscoveryError(AUTH_SERVICE, tried, present)


def _token_shaped_keys(payload: Any) -> tuple[str, ...]:
    """Every token-shaped key in a decoded body, at any depth (AC11)."""
    found: set[str] = set()
    stack: list[Any] = [payload]
    while stack:
        item = stack.pop()
        if isinstance(item, Mapping):
            for key, value in item.items():
                if isinstance(key, str) and key in TOKEN_SHAPED_KEYS:
                    found.add(key)
                stack.append(value)
        elif isinstance(item, (list, tuple)):
            stack.extend(item)
    return tuple(sorted(found))


def _string(value: Any) -> str | None:
    return value if isinstance(value, str) and value else None


class AuthApiClient:
    """The portal's view of the auth service."""

    def __init__(self, http: httpx.AsyncClient, endpoint: AuthServiceEndpoint) -> None:
        self._http = http
        self._endpoint = endpoint

    @property
    def endpoint(self) -> AuthServiceEndpoint:
        """The resolved endpoint, for diagnostics (``/health``, error cards)."""
        return self._endpoint

    async def exchange(self, username: str, password: str, redirect_uri: str) -> ExchangeResult:
        """``POST /auth/exchange`` — credentials in, handshake code + portal session id out.

        The credentials transit portal -> auth service inside the AppHost network; the auth
        service performs the Direct Grant (client secret server-side) and returns **no token**,
        only the single-use code the calling app will redeem and the opaque session id (D6).
        """
        payload = await self._request_json(
            "POST",
            EXCHANGE_PATH,
            body={"username": username, "password": password, "redirectUri": redirect_uri},
        )
        return ExchangeResult.from_payload(self._as_object(payload))

    async def redeem_bootstrap_code(
        self, bootstrap_code: str, redirect_uri: str
    ) -> BootstrapRedemption:
        """``POST /auth/bootstrap/redeem`` — the D16 passkey handoff, redeemed for the session id.

        ``redirect_uri`` is the portal's own bootstrap redemption URI — the URI the code was
        bound to when the auth service issued it (the service enforces the binding, D16/AC13).
        """
        payload = await self._request_json(
            "POST",
            BOOTSTRAP_REDEEM_PATH,
            body={"code": bootstrap_code, "redirectUri": redirect_uri},
        )
        return BootstrapRedemption.from_payload(self._as_object(payload))

    async def read_session(self, session_id: str) -> SessionData:
        """``GET /auth/session/{id}`` — the claim set as data (D18).

        Raises :class:`SessionEndedError` when the auth service reports the session ended and
        :class:`SessionNotFoundError` when it does not know the id — the two states a page must
        distinguish.
        """
        payload = await self._request_json(
            "GET", SESSION_PATH.format(session_id=quote(session_id, safe=""))
        )
        return SessionData.from_payload(self._as_object(payload))

    async def revoke_session(self, session_id: str) -> SessionRevocation:
        """``DELETE /auth/session/{id}`` — server-side revocation, ``end_session`` URL back (D13).

        The auth service revokes the refresh token itself and builds the URL (it holds the id
        token); the portal only redirects the browser to it.
        """
        payload = await self._request_json(
            "DELETE", SESSION_PATH.format(session_id=quote(session_id, safe=""))
        )
        return SessionRevocation.from_payload(self._as_object(payload))

    async def _request_json(
        self, method: str, path: str, *, body: dict[str, Any] | None = None
    ) -> Any:
        """Call ``path`` and return the decoded JSON, or raise a typed error.

        Fail-closed at every step: transport failure -> ``ApiUnavailableError``; a non-JSON body
        (the 2xx-HTML-login-page class the ward portal closed) -> ``ApiResponseError``; a
        token-shaped field anywhere in the body -> ``TokenInResponseError`` (AC11).
        """
        base_url = self._endpoint.base_url
        try:
            response = await self._http.request(method, f"{base_url}{path}", json=body)
        except httpx.HTTPError as error:  # ConnectError, TimeoutException, ...
            raise ApiUnavailableError(self._endpoint.service, base_url, error) from error

        payload = self._decode(response, path)
        leaked = _token_shaped_keys(payload)
        if leaked:
            raise TokenInResponseError(self._endpoint.service, base_url, leaked)

        if response.status_code >= 400:
            raise self._failure(response.status_code, payload)
        return payload

    def _decode(self, response: httpx.Response, path: str) -> Any:
        content_type = response.headers.get("content-type", "")
        if not content_type.lower().startswith(JSON_CONTENT_TYPE):
            # A proxy or an OIDC challenge can answer with HTML at any status — never data.
            raise ApiResponseError(
                self._endpoint.service,
                self._endpoint.base_url,
                f"expected {JSON_CONTENT_TYPE} for {path}, "
                f"got '{content_type or 'no content-type'}'",
            )
        try:
            return response.json()
        except ValueError as error:
            raise ApiResponseError(
                self._endpoint.service,
                self._endpoint.base_url,
                f"body for {path} was not valid JSON: {error}",
            ) from error

    def _failure(self, status_code: int, payload: Any) -> AuthServiceError | ApiResponseError:
        code = _string(payload.get("error")) if isinstance(payload, Mapping) else None
        detail = _string(payload.get("detail")) if isinstance(payload, Mapping) else None

        if code and code in _AUTH_ERROR_TYPES:
            return _AUTH_ERROR_TYPES[code](code, detail)

        described = f"HTTP {status_code}" + (f": {code}" if code else "")
        return ApiResponseError(
            self._endpoint.service,
            self._endpoint.base_url,
            described + (f" ({detail})" if detail else ""),
        )

    def _as_object(self, payload: Any) -> Mapping[str, Any]:
        if not isinstance(payload, Mapping):
            raise ApiResponseError(
                self._endpoint.service,
                self._endpoint.base_url,
                f"expected a JSON object, got {type(payload).__name__}",
            )
        return payload
