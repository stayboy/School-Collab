"""Typed client for the auth service's admin surface — the portal's downstream for the admin UI.

The Python counterpart of ``src/SchoolCollab.Portals``' one-client-per-surface rule (see
``documents/solution/portals-service-client-pattern.md``): the admin client is separate from
:class:`api.auth_api_client.AuthApiClient` because its transport differs in exactly one, load-bearing
way — **every call presents the portal's opaque session id in the X-Portal-Session header**
(``PortalSessionAuthenticationHandler.SessionHeaderName``, B5), which is how the auth service
resolves the acting principal and applies its own ``user-admin`` gate (D12). The sign-in client's
routes take a session id in the path instead, so this is not a shared transport.

The portal holds **no credential of any kind** (AC11): no Keycloak token, no admin service-account
secret, no API token. Every admin-privileged call is mediated by the auth service, which owns the
Admin REST client and its service account. Responses are **data only** — the AC11 token-shaped
scan runs on every body, nested objects included.

Endpoint paths this client pins (the auth service must serve exactly these):

* ``GET /auth/admin/users`` (with the optional ``username``/``email`` filters) and
  ``POST /auth/admin/users`` / ``PUT /auth/admin/users`` — the Keycloak user representations.
* ``GET /auth/admin/users/{userId}`` — one user representation.
* ``PUT /auth/admin/users/{userId}/reset-password`` — the credential reset; the value never
  enters a log line, an exception message or a response body.
* ``GET /auth/admin/roles`` — the realm roles as ``{id, name}`` pairs (the role-mappings endpoint
  keys on role ids, so an id must be resolved here first).
* ``POST`` / ``DELETE /auth/admin/users/{userId}/role-mappings`` — grant / revoke realm roles.
* ``GET /auth/pickers/tenants`` and ``GET /auth/pickers/teachers`` — B7's **mediated** reads, the
  admin surface's only source for its tenant and teacher pickers (D17): the portal never calls
  settings-api or students-api, and never calls Keycloak (D6/D7). Shapes are pinned by B7:
  ``[{"id","name","type"}]`` and ``[{"id","firstName","lastName","displayName"}]``, camelCase.

Failure bodies carry a typed ``error`` code plus, when Keycloak supplied one, a bounded ``detail``
(the auth service's own taxonomy); the client maps the code to a typed exception and, for an
unrecognized code, raises :class:`ApiResponseError` — never a half-working page.
"""

from __future__ import annotations

from collections.abc import Mapping
from dataclasses import dataclass
from typing import Any
from urllib.parse import quote

import httpx

# Package-internal reuse of the frozen sign-in client's pieces: the AC11 token-shape rule and the
# tolerant DTO coercions must have exactly ONE definition in the portal. Both B2 modules are
# outside this pass's file set, so nothing is copied rather than shared.
from api.auth_api_client import AuthServiceEndpoint, _token_shaped_keys
from api.dto import _pick, _text, _text_list
from api.errors import (
    ApiResponseError,
    ApiUnavailableError,
    AuthServiceError,
    AuthUpstreamError,
    SessionEndedError,
    SessionNotFoundError,
    TokenInResponseError,
)

#: The request header carrying the portal's opaque session id (B5's
#: ``PortalSessionAuthenticationHandler.SessionHeaderName``). Pinned here as a literal because the
#: auth service is the only other half of the contract: the value is part of the wire contract, so
#: a rename on either side must break loudly.
SESSION_HEADER_NAME = "X-Portal-Session"

JSON_CONTENT_TYPE = "application/json"

USERS_PATH = "/auth/admin/users"
USER_PATH = "/auth/admin/users/{user_id}"
RESET_PASSWORD_PATH = "/auth/admin/users/{user_id}/reset-password"
REALM_ROLES_PATH = "/auth/admin/roles"
ROLE_MAPPINGS_PATH = "/auth/admin/users/{user_id}/role-mappings"

#: B7's mediated picker reads (D17) — the admin views' only picker sources.
PICKER_TENANTS_PATH = "/auth/pickers/tenants"
PICKER_TEACHERS_PATH = "/auth/pickers/teachers"


class AdminForbiddenError(AuthServiceError):
    """Keycloak refused the Admin REST call (``forbidden``).

    The distinct 403 the auth service's own taxonomy keeps loud: the imported
    ``school-collab-auth-admin`` service account lacks a required ``realm-management`` role. The
    portal can only say so — it holds no admin credential to retry with (AC11).
    """

    message = "the identity provider refused the admin operation"


class AdminNotFoundError(AuthServiceError):
    """The admin surface reported an unknown target (``not_found``) — e.g. a stale user id."""

    message = "the admin target does not exist"


class AdminConflictError(AuthServiceError):
    """The admin surface reported a conflict (``conflict``) — e.g. a username already taken."""

    message = "the admin operation conflicts with existing realm state"


#: The auth service's typed admin failure codes -> the portal's exception taxonomy (fail-closed:
#: an unrecognized code degrades as an unusable response, never as "probably fine").
_ADMIN_ERROR_TYPES: dict[str, type[AuthServiceError]] = {
    "session_not_found": SessionNotFoundError,
    "session_ended": SessionEndedError,
    "upstream_unreachable": AuthUpstreamError,
    "keycloak_unreachable": AuthUpstreamError,
    "forbidden": AdminForbiddenError,
    "not_found": AdminNotFoundError,
    "conflict": AdminConflictError,
}


@dataclass(frozen=True)
class AdminUser:
    """A realm user as data (spec §13, D10) — never a credential and never a token.

    ``tenant_id`` / ``teacher_id`` / ``roles`` come from the representation's ``attributes`` and
    ``roles`` members **when the auth service's payload carries them**: Keycloak's user list omits
    role mappings, so the DTO tolerates their absence rather than failing the read, and a page
    renders what is actually there.
    """

    id: str | None = None
    username: str | None = None
    email: str | None = None
    enabled: bool = True
    first_name: str | None = None
    last_name: str | None = None
    tenant_id: str | None = None
    tenant_name: str | None = None
    tenant_type: str | None = None
    teacher_id: str | None = None
    roles: tuple[str, ...] = ()

    @classmethod
    def from_payload(cls, payload: Mapping[str, Any]) -> AdminUser:
        attributes = payload.get("attributes")
        attributes = attributes if isinstance(attributes, Mapping) else {}
        enabled = payload.get("enabled")
        return cls(
            id=_text(_pick(payload, "id")),
            username=_text(_pick(payload, "username")),
            email=_text(_pick(payload, "email")),
            enabled=enabled if isinstance(enabled, bool) else True,
            first_name=_text(_pick(payload, "first_name", "firstName")),
            last_name=_text(_pick(payload, "last_name", "lastName")),
            tenant_id=_first(_pick(attributes, "tenant_id", "tenantId")),
            tenant_name=_first(_pick(attributes, "tenant_name", "tenantName")),
            tenant_type=_first(_pick(attributes, "tenant_type", "tenantType")),
            teacher_id=_first(_pick(attributes, "teacher_id", "teacherId")),
            roles=_text_list(_pick(payload, "roles", "realmRoles")),
        )

    @property
    def display_name(self) -> str:
        """The row's display text: the username, which is the realm's unique user handle."""
        return self.username or self.id or "(unnamed)"


@dataclass(frozen=True)
class AdminRole:
    """A realm role as ``{id, name}`` (spec §13, D11) — the existing role set, read only.

    ``id`` is what the role-mappings endpoint resolves on, which is why the roles page lists the
    realm roles before it can assign one. Role *definitions* are never edited at runtime (D11):
    the realm import is their only home.
    """

    id: str
    name: str

    @classmethod
    def from_payload(cls, payload: Mapping[str, Any]) -> AdminRole:
        return cls(
            id=_text(_pick(payload, "id")) or "",
            name=_text(_pick(payload, "name")) or "",
        )


@dataclass(frozen=True)
class PickerTenant:
    """A tenant-picker row (B7: ``[{"id","name","type"}]``, camelCase) — read-only registry data."""

    id: str
    name: str
    type: str

    @classmethod
    def from_payload(cls, payload: Mapping[str, Any]) -> PickerTenant:
        return cls(
            id=_text(_pick(payload, "id")) or "",
            name=_text(_pick(payload, "name")) or "",
            type=_text(_pick(payload, "type")) or "",
        )

    @property
    def label(self) -> str:
        """What the picker shows: the tenant's name plus its type, when it has one."""
        return f"{self.name} ({self.type})" if self.name and self.type else (self.name or self.id)


@dataclass(frozen=True)
class PickerTeacher:
    """A teacher-picker row (B7: ``[{"id","firstName","lastName","displayName"}]``, camelCase)."""

    id: str
    first_name: str | None = None
    last_name: str | None = None
    display_name: str | None = None

    @classmethod
    def from_payload(cls, payload: Mapping[str, Any]) -> PickerTeacher:
        return cls(
            id=_text(_pick(payload, "id")) or "",
            first_name=_text(_pick(payload, "first_name", "firstName")),
            last_name=_text(_pick(payload, "last_name", "lastName")),
            display_name=_text(_pick(payload, "display_name", "displayName")),
        )

    @property
    def label(self) -> str:
        """What the picker shows: the upstream display name, else the teacher's full name."""
        full_name = " ".join(
            part for part in (self.first_name, self.last_name) if part
        )
        return self.display_name or full_name or self.id


def _first(value: Any) -> str | None:
    """The first string of a multivalued Keycloak attribute (its wire shape is a string array)."""
    values = _text_list(value)
    return values[0] if values else None


def user_representation(
    *,
    username: str,
    enabled: bool,
    email: str | None = None,
    first_name: str | None = None,
    last_name: str | None = None,
    attributes: Mapping[str, str] | None = None,
    user_id: str | None = None,
) -> dict[str, Any]:
    """The Keycloak user representation this portal sends (camelCase, Keycloak's wire shape).

    ``user_id`` is only present on an update: the auth service's ``PUT`` is a
    full-representation replace that resolves its target by ``id`` (round A's contract), while a
    create must not carry an ``id`` at all. Attributes are emitted in Keycloak's own
    ``{name: [value]}`` shape and only when non-blank, so an empty text field never writes an
    empty attribute.
    """
    body: dict[str, Any] = {"username": username, "enabled": enabled}
    if user_id:
        body["id"] = user_id
    for field, value in (
        ("email", email),
        ("firstName", first_name),
        ("lastName", last_name),
    ):
        if value:
            body[field] = value

    sent = {name: [value] for name, value in (attributes or {}).items() if value}
    if sent:
        body["attributes"] = sent
    return body


class AdminApiClient:
    """The portal's view of the auth service's admin surface (D8/D10/D11)."""

    def __init__(
        self, http: httpx.AsyncClient, endpoint: AuthServiceEndpoint, session_id: str
    ) -> None:
        self._http = http
        self._endpoint = endpoint
        self._session_id = session_id

    @property
    def endpoint(self) -> AuthServiceEndpoint:
        """The resolved endpoint, for diagnostics (``/health``, error cards)."""
        return self._endpoint

    async def list_users(
        self, *, username: str | None = None, email: str | None = None
    ) -> tuple[AdminUser, ...]:
        """``GET /auth/admin/users`` — the realm users, with Keycloak's own username/email filters."""
        payload = await self._request_json(
            "GET", USERS_PATH, params={"username": username, "email": email}
        )
        return tuple(
            AdminUser.from_payload(item) for item in _as_list(payload, self._endpoint)
        )

    async def get_user(self, user_id: str) -> AdminUser:
        """``GET /auth/admin/users/{id}`` — one realm user, for the edit page."""
        payload = await self._request_json("GET", USER_PATH.format(user_id=quote(user_id, safe="")))
        return AdminUser.from_payload(_as_object(payload, self._endpoint))

    async def create_user(self, representation: Mapping[str, Any]) -> None:
        """``POST /auth/admin/users`` — create the realm user (identity only, D8).

        ``representation`` is built by :func:`user_representation`, so a create can never carry an
        ``id`` and the attribute shape has one definition.
        """
        await self._request_json("POST", USERS_PATH, body=dict(representation))

    async def update_user(self, representation: Mapping[str, Any]) -> None:
        """``PUT /auth/admin/users`` — the full-representation update (the body carries the id)."""
        await self._request_json("PUT", USERS_PATH, body=dict(representation))

    async def reset_password(self, user_id: str, new_password: str) -> None:
        """``PUT /auth/admin/users/{id}/reset-password`` — set the credential (AC11).

        The value is deliberately **not** logged, not echoed and not stored here: it travels
        portal -> auth service inside the AppHost network and lands in Keycloak, which hashes it.
        """
        await self._request_json(
            "PUT",
            RESET_PASSWORD_PATH.format(user_id=quote(user_id, safe="")),
            body={"newPassword": new_password},
        )

    async def list_realm_roles(self) -> tuple[AdminRole, ...]:
        """``GET /auth/admin/roles`` — the existing realm role set, as ``{id, name}`` pairs."""
        payload = await self._request_json("GET", REALM_ROLES_PATH)
        return tuple(AdminRole.from_payload(item) for item in _as_list(payload, self._endpoint))

    async def assign_realm_role(self, user_id: str, role: AdminRole) -> None:
        """``POST /auth/admin/users/{id}/role-mappings`` — grant one realm role to one user."""
        await self._request_json(
            "POST",
            ROLE_MAPPINGS_PATH.format(user_id=quote(user_id, safe="")),
            body={"roles": [{"id": role.id, "name": role.name}]},
        )

    async def unassign_realm_role(self, user_id: str, role: AdminRole) -> None:
        """``DELETE /auth/admin/users/{id}/role-mappings`` — revoke one realm role from one user."""
        await self._request_json(
            "DELETE",
            ROLE_MAPPINGS_PATH.format(user_id=quote(user_id, safe="")),
            body={"roles": [{"id": role.id, "name": role.name}]},
        )

    async def list_picker_tenants(self) -> tuple[PickerTenant, ...]:
        """``GET /auth/pickers/tenants`` — the tenant picker's rows (B7's mediated read, D17)."""
        payload = await self._request_json("GET", PICKER_TENANTS_PATH)
        return tuple(
            PickerTenant.from_payload(item) for item in _as_list(payload, self._endpoint)
        )

    async def list_picker_teachers(self) -> tuple[PickerTeacher, ...]:
        """``GET /auth/pickers/teachers`` — the teacher picker's rows (B7's mediated read, D17)."""
        payload = await self._request_json("GET", PICKER_TEACHERS_PATH)
        return tuple(
            PickerTeacher.from_payload(item) for item in _as_list(payload, self._endpoint)
        )

    async def _request_json(
        self,
        method: str,
        path: str,
        *,
        body: dict[str, Any] | None = None,
        params: Mapping[str, str | None] | None = None,
    ) -> Any:
        """Call ``path`` with the portal session header and return the decoded JSON.

        Fail-closed at every step, exactly like the sign-in client: transport failure ->
        ``ApiUnavailableError``; a non-JSON body -> ``ApiResponseError``; a token-shaped field
        anywhere in the body -> ``TokenInResponseError`` (AC11); a 4xx/5xx -> the typed error its
        ``error`` code names.
        """
        query = {name: value for name, value in (params or {}).items() if value}
        try:
            response = await self._http.request(
                method,
                f"{self._endpoint.base_url}{path}",
                json=body,
                params=query,
                headers={SESSION_HEADER_NAME: self._session_id},
            )
        except httpx.HTTPError as error:  # ConnectError, TimeoutException, ...
            raise ApiUnavailableError(self._endpoint.service, self._endpoint.base_url, error) from error

        payload = self._decode(response, path)
        leaked = _token_shaped_keys(payload)
        if leaked:
            raise TokenInResponseError(self._endpoint.service, self._endpoint.base_url, leaked)

        if response.status_code >= 400:
            raise self._failure(response.status_code, payload)
        return payload

    def _decode(self, response: httpx.Response, path: str) -> Any:
        content_type = response.headers.get("content-type", "")
        if not response.content:
            # A 204 (the role-mapping and reset routes answer NoContent) has no body to decode —
            # and no body means nothing to scan for a token, so there is nothing to refuse.
            return None
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

        if code and code in _ADMIN_ERROR_TYPES:
            return _ADMIN_ERROR_TYPES[code](code, detail)

        described = f"HTTP {status_code}" + (f": {code}" if code else "")
        return ApiResponseError(
            self._endpoint.service,
            self._endpoint.base_url,
            described + (f" ({detail})" if detail else ""),
        )


def _string(value: Any) -> str | None:
    return value if isinstance(value, str) and value else None


def _as_list(payload: Any, endpoint: AuthServiceEndpoint) -> tuple[Mapping[str, Any], ...]:
    """A JSON array of objects, or an :class:`ApiResponseError` (never a silently empty page)."""
    if not isinstance(payload, list):
        raise ApiResponseError(
            endpoint.service,
            endpoint.base_url,
            f"expected a JSON array, got {type(payload).__name__}",
        )
    return tuple(item for item in payload if isinstance(item, Mapping))


def _as_object(payload: Any, endpoint: AuthServiceEndpoint) -> Mapping[str, Any]:
    if not isinstance(payload, Mapping):
        raise ApiResponseError(
            endpoint.service,
            endpoint.base_url,
            f"expected a JSON object, got {type(payload).__name__}",
        )
    return payload


__all__ = [
    "JSON_CONTENT_TYPE",
    "PICKER_TEACHERS_PATH",
    "PICKER_TENANTS_PATH",
    "REALM_ROLES_PATH",
    "RESET_PASSWORD_PATH",
    "ROLE_MAPPINGS_PATH",
    "SESSION_HEADER_NAME",
    "USERS_PATH",
    "USER_PATH",
    "AdminApiClient",
    "AdminConflictError",
    "AdminForbiddenError",
    "AdminNotFoundError",
    "AdminRole",
    "AdminUser",
    "PickerTeacher",
    "PickerTenant",
    "user_representation",
]
