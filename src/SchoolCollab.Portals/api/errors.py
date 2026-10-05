"""Typed errors raised by the portal's service calls.

The Python counterpart of the repo's "typed domain exceptions" rule for C#:
the transport layer raises these, and the view/route layer decides what the
user sees — an error card, never a raw traceback. Keeping them in one place
lets a route catch ``PortalApiError`` and stay honest about the failure.
"""

from __future__ import annotations

from collections.abc import Mapping


class PortalApiError(RuntimeError):
    """Base class for every failure raised by the portal's API clients."""


class ServiceDiscoveryError(PortalApiError):
    """The AppHost-injected base URL for a service could not be resolved."""

    def __init__(self, service: str, tried: tuple[str, ...], present: dict[str, str]) -> None:
        self.service = service
        self.tried = tried
        self.present = present
        super().__init__(
            f"No base URL for '{service}' was injected. Tried: {', '.join(tried)}. "
            f"Discovery-shaped environment variables actually present: {present}"
        )


class ApiUnavailableError(PortalApiError):
    """The service could not be reached at all (refused, timeout, DNS)."""

    def __init__(self, service: str, base_url: str, cause: Exception) -> None:
        self.service = service
        self.base_url = base_url
        self.cause = cause
        super().__init__(f"{service} at {base_url} is unreachable: {cause}")


class ApiResponseError(PortalApiError):
    """The service answered, but not with the JSON the client asked for."""

    def __init__(self, service: str, base_url: str, detail: str) -> None:
        self.service = service
        self.base_url = base_url
        self.detail = detail
        super().__init__(f"{service} at {base_url} returned an unusable response: {detail}")


class MissingConfigurationError(PortalApiError):
    """A configuration value the portal needs was not supplied, or is unusable.

    Distinct from :class:`ServiceDiscoveryError`, which reports a *service base URL* the
    AppHost did not inject and carries the discovery-shaped environment for diagnostics.
    This one names a single value the portal must have — the first is the dev-bypass
    teacher id the teacher surface's review-queue read requires — and why its absence
    blocks the call, so the route can say so instead of rendering an empty result.
    """

    def __init__(self, key: str, detail: str) -> None:
        self.key = key
        self.detail = detail
        super().__init__(f"{key} cannot be used: {detail}")


class AuthServiceError(PortalApiError):
    """The auth service answered with one of its typed failure codes.

    The teacher portal's session taxonomy (round ``portal-session-adoption``): the code and
    optional detail travel as attributes so a route can key its D18 handling on the state
    (``session_ended`` vs ``session_not_found``) rather than on an HTTP status.
    """

    def __init__(self, code: str, detail: str | None = None) -> None:
        self.code = code
        self.detail = detail
        super().__init__(f"the auth service refused ({code})" + (f": {detail}" if detail else ""))


class AuthUpstreamError(AuthServiceError):
    """The auth service's own identity provider could not be reached; session state is unknown."""


class SessionEndedError(AuthServiceError):
    """The session's refresh token was rejected — the session is over (D18 ``session_ended``).

    A route that sees this must clear the session cookie: the session is gone for good.
    """


class SessionNotFoundError(AuthServiceError):
    """No live session exists under the presented id (D18 ``session_not_found``).

    Treated like :class:`SessionEndedError` for cookie handling (both are dead sessions), but
    it means "stale cookie", not "revoked by the identity provider".
    """


class HandshakeCodeRejectedError(AuthServiceError):
    """A one-time handshake code was refused (replay, TTL expiry, redirect-URI mismatch).

    A callback route that sees this must NOT set its cookie: nothing was redeemed.
    """


class TokenInResponseError(PortalApiError):
    """A service answered with a token-shaped field in its body (AC11) — refused, never ingested."""

    def __init__(self, service: str, base_url: str, keys: tuple[str, ...]) -> None:
        self.service = service
        self.base_url = base_url
        self.keys = keys
        super().__init__(
            f"{service} at {base_url} returned a body carrying token-shaped fields {list(keys)} "
            "— the portal never holds a credential, so the response was refused"
        )


#: Fields that must never appear in a portal-bound body (AC11). Every decoded response is scanned
#: — nested objects and arrays included — and a body carrying one is refused, never ingested.
#: Shared by every portal client: one spelling of the key set, owned by no single client, so a new
#: client adopts the scan without importing another client's internals (round
#: ``portal-submission-grade`` review P2-1).
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


def token_shaped_keys(payload: object) -> tuple[str, ...]:
    """Every token-shaped key in a decoded body, at any depth (AC11)."""
    found: set[str] = set()
    stack: list[object] = [payload]
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
