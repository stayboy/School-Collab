"""Tolerant DTOs for the auth service's portal-facing contract.

The auth service is the authority on shape, so these frozen dataclasses accept missing or
renamed fields instead of exploding: a shape drift should degrade one line on a page, not the
whole page. Views consume attributes, never raw dictionaries.

**No DTO here carries a token, a secret or a signing key** — that is AC11's whole point, and
``tests/test_auth_api_client.py`` asserts it structurally over every dataclass in this module.
The only identity material the portal ever holds is the opaque session id (D12).
"""

from __future__ import annotations

from collections.abc import Mapping
from dataclasses import dataclass
from typing import Any


def _pick(payload: Mapping[str, Any], *names: str) -> Any:
    """The first present, non-empty value among ``names``.

    Both spellings are accepted for each field (the auth service serializes .NET records as
    camelCase — ``sessionId`` — while the claim names the realm mappers emit are snake_case —
    ``tenant_id``), so a serialization-policy change cannot silently blank a page.
    """
    for name in names:
        value = payload.get(name)
        if value is not None and value != "":
            return value
    return None


def _text(value: Any) -> str | None:
    """Coerce an API value to display text, tolerating nulls and non-strings."""
    if value is None:
        return None
    return value if isinstance(value, str) else str(value)


def _int(value: Any) -> int | None:
    """Coerce an API value to an int, or ``None`` when it is absent or unusable."""
    if value is None:
        return None
    try:
        return int(value)
    except (TypeError, ValueError):
        return None


def _text_list(value: Any) -> tuple[str, ...]:
    """Coerce a multivalued claim to a tuple of strings.

    The realm mapper emits ``roles`` as a JSON array (``ClaimSetFactory`` pins that), but a
    single string or a comma-separated string is tolerated rather than dropping the roles —
    a page must never claim a user has no roles because the shape moved.
    """
    if value is None:
        return ()
    if isinstance(value, str):
        return tuple(part.strip() for part in value.split(",") if part.strip())
    if isinstance(value, (list, tuple, set)):
        return tuple(text for item in value if (text := _text(item)) is not None and text)
    return (_text(value) or "",)


@dataclass(frozen=True)
class ExchangeResult:
    """A successful ``POST /auth/exchange``: the single-use handshake code plus the portal's
    opaque session id (spec §5.2 step 4). Token-free by contract (D6/AC11)."""

    code: str
    session_id: str
    expires_in_seconds: int | None = None

    @classmethod
    def from_payload(cls, payload: Mapping[str, Any]) -> ExchangeResult:
        return cls(
            code=_text(_pick(payload, "code")) or "",
            session_id=_text(_pick(payload, "session_id", "sessionId")) or "",
            expires_in_seconds=_int(_pick(payload, "expires_in_seconds", "expiresInSeconds")),
        )


@dataclass(frozen=True)
class BootstrapRedemption:
    """A successful passkey-path bootstrap redemption (D16): the portal's session id, plus the
    D6 continuation when the sign-in began at a gated Blazor page — that app's callback and the
    one-time code the portal redirects onward with. Never a token or an authorization code
    belonging to the portal (AC12)."""

    session_id: str
    app_redirect_uri: str | None = None
    app_code: str | None = None

    @classmethod
    def from_payload(cls, payload: Mapping[str, Any]) -> BootstrapRedemption:
        return cls(
            session_id=_text(_pick(payload, "session_id", "sessionId")) or "",
            app_redirect_uri=_text(_pick(payload, "app_redirect_uri", "appRedirectUri")),
            app_code=_text(_pick(payload, "app_code", "appCode")),
        )


#: The realm role the admin surface requires (``school-collab-realm.json``; spec §8, D11).
USER_ADMIN_ROLE = "user-admin"


@dataclass(frozen=True)
class SessionData:
    """A session read (D18): the claim set **as data** — tenant, teacher id, roles — never tokens.

    ``expires_at_utc`` / ``expires_in_seconds`` are optional because the read is a claim-set read
    first; the identity fields are what a page renders.
    """

    session_id: str | None = None
    expires_at_utc: str | None = None
    expires_in_seconds: int | None = None
    tenant_id: str | None = None
    tenant_name: str | None = None
    tenant_type: str | None = None
    teacher_id: str | None = None
    roles: tuple[str, ...] = ()

    @classmethod
    def from_payload(cls, payload: Mapping[str, Any]) -> SessionData:
        return cls(
            session_id=_text(_pick(payload, "session_id", "sessionId")),
            expires_at_utc=_text(_pick(payload, "expires_at_utc", "expiresAtUtc")),
            expires_in_seconds=_int(_pick(payload, "expires_in_seconds", "expiresInSeconds")),
            tenant_id=_text(_pick(payload, "tenant_id", "tenantId")),
            tenant_name=_text(_pick(payload, "tenant_name", "tenantName")),
            tenant_type=_text(_pick(payload, "tenant_type", "tenantType")),
            teacher_id=_text(_pick(payload, "teacher_id", "teacherId")),
            roles=_text_list(_pick(payload, "roles")),
        )

    @property
    def is_admin(self) -> bool:
        """Whether the session carries the admin realm role (the admin surface's gate, D8/D11).

        A routing hint only: the auth service still authorizes every admin call (D12).
        """
        return USER_ADMIN_ROLE in self.roles


@dataclass(frozen=True)
class SessionRevocation:
    """The result of ``DELETE /auth/session/{id}`` (D13): the fully-built ``end_session`` URL the
    portal redirects the browser to. No refresh token, no id token — the auth service revokes
    server-side and keeps the tokens in C# custody."""

    end_session_url: str | None = None

    @classmethod
    def from_payload(cls, payload: Mapping[str, Any]) -> SessionRevocation:
        return cls(
            end_session_url=_text(_pick(payload, "end_session_url", "endSessionUrl")),
        )
