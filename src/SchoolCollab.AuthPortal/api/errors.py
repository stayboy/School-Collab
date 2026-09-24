"""Typed errors raised by the portal's calls to the auth service.

The Python counterpart of the repo's "typed domain exceptions" rule, and the fail-closed half of
AC10: the transport layer raises these, the route layer decides what the user sees — a Prefab
error card at HTTP 200, never a raw traceback and never a half-working page (D18).

Two families:

* **transport/plumbing** — ``ServiceDiscoveryError``, ``ApiUnavailableError``,
  ``ApiResponseError``: the auth service could not be reached, or answered with something that is
  not the JSON contract. Same vocabulary as ``src/SchoolCollab.Portals/api/errors.py`` so both
  portals degrade the same way.
* **the auth-service taxonomy** — ``AuthServiceError`` and its subclasses, built from the typed
  ``error`` code in the auth service's own failure bodies (invalid credentials, disabled user,
  session ended, rejected handoff code, Keycloak unreachable). ``SessionEndedError`` is the D18
  status the portal reacts to by clearing its session cookie and rendering an error card.

``TokenInResponseError`` is the AC11 tripwire: the portal refuses to ingest a response body that
carries a token-shaped field, so a token can never enter Python state even if the auth service
regresses.
"""

from __future__ import annotations


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
    """The auth service could not be reached at all (refused, timeout, DNS)."""

    def __init__(self, service: str, base_url: str, cause: Exception) -> None:
        self.service = service
        self.base_url = base_url
        self.cause = cause
        super().__init__(f"{service} at {base_url} is unreachable: {cause}")


class ApiResponseError(PortalApiError):
    """The auth service answered, but not with the JSON the client asked for."""

    def __init__(self, service: str, base_url: str, detail: str) -> None:
        self.service = service
        self.base_url = base_url
        self.detail = detail
        super().__init__(f"{service} at {base_url} returned an unusable response: {detail}")


class AuthServiceError(PortalApiError):
    """Base for a failure the auth service **reported** — a typed ``error`` code plus, when
    Keycloak supplied one, its bounded ``detail`` text (never a token, never a raw body).

    Subclasses set :attr:`message` to the card-ready sentence; ``code`` and ``detail`` stay
    available so a route can log precisely and a card can show what happened.
    """

    message = "the auth service reported a failure"

    def __init__(self, code: str, detail: str | None = None) -> None:
        self.code = code
        self.detail = detail
        suffix = f" ({detail})" if detail else ""
        super().__init__(f"{self.message} [{code}]{suffix}")


class AuthCredentialsRejectedError(AuthServiceError):
    """The credentials were rejected (``invalid_credentials``)."""

    message = "the credentials were rejected"


class AuthUserDisabledError(AuthServiceError):
    """The account is disabled (``disabled_user``)."""

    message = "the account is disabled"


class AuthUpstreamError(AuthServiceError):
    """The auth service could not reach Keycloak (``keycloak_unreachable`` / ``upstream_unreachable``)."""

    message = "the auth service could not reach Keycloak"


class SessionEndedError(AuthServiceError):
    """The session's refresh failed — revoked or expired (D18, ``session_ended``).

    Distinct from :class:`SessionNotFoundError` on purpose: the portal clears its cookie and
    renders the sign-in-again card rather than a generic failure."""

    message = "the session ended — sign in again"


class SessionNotFoundError(AuthServiceError):
    """The auth service does not know this session id (``session_not_found``)."""

    message = "the session is unknown or already gone"


class BootstrapCodeRejectedError(AuthServiceError):
    """The passkey-path handoff code was rejected (D16: single-use, short TTL, URI-bound)."""

    message = "the sign-in handoff code was rejected"


class TokenInResponseError(PortalApiError):
    """AC11 tripwire: a portal-bound response carried a token-shaped field.

    The portal must never hold a token, so the client refuses to ingest the body rather than
    parse a token into Python state. Fail-closed: the route renders a degraded card.
    """

    def __init__(self, service: str, base_url: str, keys: tuple[str, ...]) -> None:
        self.service = service
        self.base_url = base_url
        self.keys = keys
        super().__init__(
            f"{service} at {base_url} returned a token-shaped field ({', '.join(keys)}); "
            "refusing to ingest it — credentials and tokens stay in the auth service (AC11)."
        )
