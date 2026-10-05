"""Typed HTTP client for the auth service — the teacher portal's session half.

The Python counterpart of the repo's one-client-per-surface rule (see
``documents/solution/portals-service-client-pattern.md``): one class, one method per endpoint,
DTOs in and out, transport failures translated into typed portal errors. The teacher portal
holds **no credential of any kind** — its cookie is the opaque session id the auth service
issued (D12) — so its calls present that id and nothing else.

The client is deliberately **base-URL agnostic** — it is handed a resolved
:class:`AuthServiceEndpoint` rather than reading the environment itself — so it can be driven by
any transport, including ``httpx.MockTransport``: that is what keeps the portal's session tests
container-free. It is **async** (``httpx.AsyncClient``): created once in the FastAPI lifespan and
awaited from async route handlers.

Endpoint paths this client pins (the auth service must serve exactly these):

* ``POST /auth/handshake/session`` — a D6 handshake code plus its bound redirect URI in, this
  portal's opaque session id out (round ``portal-session-adoption`` D3). Without it the D6 code
  could only be redeemed for *claims* (``/auth/redeem``), never for a session id.
* ``GET /auth/session/{session_id}`` — the claim set **as data** plus the remaining lifetime
  (D18); ``410 session_ended`` and ``404 session_not_found`` are the two dead-session states the
  portal must distinguish from a degraded read.
* ``DELETE /auth/session/{session_id}`` — server-side revocation, the ``end_session`` URL out
  (D13); the portal holds neither token and only redirects the browser to that opaque URL.

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

from api.dto import SessionData
from api.errors import (
    ApiResponseError,
    ApiUnavailableError,
    AuthServiceError,
    AuthUpstreamError,
    HandshakeCodeRejectedError,
    ServiceDiscoveryError,
    SessionEndedError,
    SessionNotFoundError,
    TokenInResponseError,
    token_shaped_keys,
)

#: The Aspire resource name of the auth service (the AppHost's ``WithReference(auth)`` on the
#: ``portals`` resource injects its discovery variable).
AUTH_SERVICE = "auth"

JSON_CONTENT_TYPE = "application/json"

REDEEM_HANDSHAKE_PATH = "/auth/handshake/session"
SESSION_PATH = "/auth/session/{session_id}"

#: The request header carrying the portal's opaque session id — ``PortalSessionAuthenticationHandler
#: .SessionHeaderName`` (spec D12). Pinned here as a literal because the auth service and the
#: Assignments API are the only other halves of the contract: the value is part of the wire
#: contract, so a rename on either side must break loudly (the auth portal's ``admin_api_client``
#: pins it for the same reason).
SESSION_HEADER_NAME = "X-Portal-Session"

#: The AC11 token-shape scan and its key set are shared by every portal client: they live in
#: ``api/errors.py``, beside the ``TokenInResponseError`` they raise, so a client never imports
#: another client's internals (round ``portal-submission-grade`` review P2-1). ``token_shaped_keys``
#: is imported above.

#: The auth service's typed failure codes -> the portal's exception taxonomy (fail-closed: an
#: unrecognized code degrades as an unusable response, never as "probably fine").
_AUTH_ERROR_TYPES: dict[str, type[AuthServiceError]] = {
    "session_ended": SessionEndedError,
    "session_not_found": SessionNotFoundError,
    "invalid_code": HandshakeCodeRejectedError,
    "code_expired": HandshakeCodeRejectedError,
    "redirect_uri_mismatch": HandshakeCodeRejectedError,
    "redirect_uri_required": HandshakeCodeRejectedError,
    "upstream_unreachable": AuthUpstreamError,
    "keycloak_unreachable": AuthUpstreamError,
    "claim_set_incomplete": AuthServiceError,
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

    Lives beside the client rather than in ``service_discovery.py``: that module quarantines the
    Assignments API's discovery for the ward surface, and this is the same Aspire ".NET-ism"
    applied to the teacher surface's second upstream. Raising
    :class:`ServiceDiscoveryError` is what lets a caller that *needs* the auth service render its
    degraded state, and a caller that only *offers* a link (the passkey sign-in affordance)
    simply omit it.
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


def _string(value: Any) -> str | None:
    """A non-empty string, or ``None`` — the shape every id and URL here must have."""
    return value if isinstance(value, str) and value else None


class AuthApiClient:
    """The teacher portal's view of the auth service's session surface."""

    def __init__(self, http: httpx.AsyncClient, endpoint: AuthServiceEndpoint) -> None:
        self._http = http
        self._endpoint = endpoint

    @property
    def endpoint(self) -> AuthServiceEndpoint:
        """The resolved endpoint, for diagnostics (``/health``, error cards)."""
        return self._endpoint

    async def redeem_handshake_code(self, code: str, redirect_uri: str) -> str:
        """``POST /auth/handshake/session`` — the D6 handshake code in, the session id out.

        ``redirect_uri`` is this portal's own callback URI — the URI the code was bound to when
        the exchange minted it, so the auth service compares it exactly. The response carries no
        token: the session's tokens stay in custody (AC11).
        """
        payload = await self._request_json(
            "POST",
            REDEEM_HANDSHAKE_PATH,
            body={"code": code, "redirectUri": redirect_uri},
        )
        session_id = _string(_field(payload, "sessionId", "session_id"))
        if not session_id:
            # A "success" without a session id cannot sign anyone in — and must not be turned
            # into a cookie carrying nothing (the DTOs tolerate a shape drift, this refusal is
            # what keeps that tolerance from becoming a silent blank session).
            raise ApiResponseError(
                self._endpoint.service,
                self._endpoint.base_url,
                "the handshake redemption returned no session id",
            )
        return session_id

    async def read_session(self, session_id: str) -> SessionData:
        """``GET /auth/session/{id}`` — the claim set as data (D18).

        Raises :class:`SessionEndedError` when the auth service reports the session ended and
        :class:`SessionNotFoundError` when it does not know the id: the two dead-session states
        the portal clears its cookie for, as opposed to a degraded read where the session's state
        is simply unknown.
        """
        payload = await self._request_json(
            "GET", SESSION_PATH.format(session_id=quote(session_id, safe=""))
        )
        return SessionData.from_payload(self._as_object(payload))

    async def revoke_session(self, session_id: str) -> str:
        """``DELETE /auth/session/{id}`` — revocation, the ``end_session`` URL back (D13).

        The auth service revokes the refresh token itself and builds that URL because the portal
        holds neither the id token nor the ability to sign one (D12/AC11). The URL is **opaque**:
        the caller 302s the browser to it and never parses, logs or persists it.
        """
        payload = await self._request_json(
            "DELETE", SESSION_PATH.format(session_id=quote(session_id, safe=""))
        )
        end_session_url = _string(_field(payload, "endSessionUrl", "end_session_url"))
        if not end_session_url:
            raise ApiResponseError(
                self._endpoint.service,
                self._endpoint.base_url,
                "the session revocation returned no end-session URL",
            )
        return end_session_url

    async def _request_json(
        self, method: str, path: str, *, body: dict[str, Any] | None = None
    ) -> Any:
        """Call ``path`` and return the decoded JSON, or raise a typed error.

        Fail-closed at every step: transport failure -> ``ApiUnavailableError``; a non-JSON body
        (a proxy or an OIDC challenge answering HTML at any status) -> ``ApiResponseError``; a
        token-shaped field anywhere in the body -> ``TokenInResponseError`` (AC11); a 4xx/5xx ->
        the typed error its ``error`` code names.
        """
        base_url = self._endpoint.base_url
        try:
            response = await self._http.request(method, f"{base_url}{path}", json=body)
        except httpx.HTTPError as error:  # ConnectError, TimeoutException, ...
            raise ApiUnavailableError(self._endpoint.service, base_url, error) from error

        payload = self._decode(response, path)
        leaked = token_shaped_keys(payload)
        if leaked:
            raise TokenInResponseError(self._endpoint.service, base_url, leaked)

        if response.status_code >= 400:
            raise self._failure(response.status_code, payload)
        return payload

    def _decode(self, response: httpx.Response, path: str) -> Any:
        content_type = response.headers.get("content-type", "")
        if not content_type.lower().startswith(JSON_CONTENT_TYPE):
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


def _field(payload: Any, *names: str) -> Any:
    """The first present, non-empty field among ``names`` (the .NET spelling first)."""
    if not isinstance(payload, Mapping):
        return None
    for name in names:
        value = payload.get(name)
        if value is not None and value != "":
            return value
    return None
