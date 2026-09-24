"""School-Collab auth portal — the prefab login/logout/challenge and admin surfaces (spec §3, D1).

The Python counterpart of ``src/SchoolCollab.Portals`` (see
``documents/solution/portals-service-client-pattern.md``): a deliberately thin FastAPI app that
owns only the app object, the lifespan-owned HTTP client, the dependency seam and the routes.
Service calls live in ``api/`` (typed auth-service client + DTOs + errors); Prefab component
trees live in ``views/`` (the sign-in surface and the fail-closed cards).

The portal talks to **one** upstream — the auth service — and holds **no credential of any kind**:
no Keycloak token, no API token, no cookie-signing key, no client secret (D12/D17, AC11). Its
session cookie is an opaque id the auth service validates server-side on every identity-bearing
call, and every identity read is a mediated auth-service call whose response is data only.

Routes:
    GET /        -> the portal index: the resolved auth-service endpoint, the session state (a
                    mediated session read, D18) and this portal's live route table. Always HTTP 200
                    (AC10 — a page, never a traceback).
    GET /login   -> the prefab sign-in page for an **allowlisted** ``return_uri`` (spec §5.2,
                    AC13): a ``return_uri`` outside ``AuthPortal:AppCallbackPrefixes`` renders
                    the AC10 card instead. Each render issues a one-time antiforgery token.
    POST /login  -> the form's own route: one-time antiforgery token + JSON-only body, then
                    credentials -> ``/auth/exchange`` -> the opaque session cookie -> HTTP 200
                    ``{"redirect_uri": "<validated return_uri>?code=…"}``, which the prefab
                    renderer navigates the current tab to. A refusal or a failure answers with
                    the same shape pointing at this portal's own error-card page (AC10).
    GET /bootstrap -> the D16 passkey landing: redeems the single-use bootstrap code for this
                    portal's session cookie and 302s onward (the app's callback when a Blazor
                    page originated the sign-in, else the portal landing).
    GET /logout  -> D13: revoke the session at the auth service, clear the cookie, and redirect
                    the browser to the returned ``end_session`` URL.
    GET /admin/users        -> the admin surface (spec §8): the realm-user list and the create form
                    (tenant ``Select`` + searchable teacher ``Combobox``, both fed by B7's
                    mediated reads). Gated on a session whose read carries ``user-admin``.
    POST /admin/users       -> create the realm user (identity only, D8) and set its credential.
    GET /admin/users/{userId}/edit -> the edit form: enable/disable, the D10 claim attributes and
                    an optional credential reset.
    PUT /admin/users        -> the full-representation update the auth service's admin PUT takes.
    GET /admin/users/{userId}/roles -> the realm-role assignment page (existing roles only, D11).
    POST /admin/users/{userId}/roles -> assign one realm role; DELETE unassigns one.
    GET /health  -> JSON diagnostics: the resolved base URL and the env var that supplied it, the
                    last auth-service call's outcome, and the discovery error when there is one.

The admin surface is gated **here** as well as at the auth service (D8): a request needs a
session cookie whose D18 claim-set read carries the ``user-admin`` realm role, and every refusal —
no session, a dead session, a non-admin session, the auth service being unreachable, the gate
passing but Keycloak refusing an operation — renders the surface's AC10 card at **HTTP 200**, never
a 403 page and never a redirect (so there is no redirect loop to land in). The auth service still
authorizes every admin call on its own (B5's portal-facing ``user-admin`` policy), because the
portal's gate is a routing hint, not a security boundary.

The portal holds **no credential** on any of these paths: the session cookie is an opaque id the
auth service validates server-side, the antiforgery token is a random string this process stores
itself (no signing key, D12), and every identity read is a mediated auth-service call whose
response is data only. ``lifespan`` tolerates a missing auth-service endpoint at boot: the process
starts, ``/health`` explains the state, and each route renders a degraded card — far more useful in
a dev loop than an app that refuses to start.
"""

from __future__ import annotations

import html
import json
import logging
import os
import secrets
from collections.abc import AsyncIterator, Mapping, Sequence
from contextlib import asynccontextmanager
from dataclasses import dataclass, field
from datetime import datetime, timedelta, timezone
from typing import Any
from urllib.parse import quote, urlencode, urlsplit

import httpx
from fastapi import Depends, FastAPI, Request, Response
from fastapi.responses import HTMLResponse, JSONResponse, RedirectResponse
from fastapi.routing import APIRoute

from api import (
    AuthApiClient,
    AuthServiceEndpoint,
    PortalApiError,
    ServiceDiscoveryError,
    SessionData,
    SessionEndedError,
    SessionNotFoundError,
    resolve_auth_service_endpoint,
)

# Imported from their modules directly: ``api/__init__.py`` and ``views/errors.py`` are B2's/B8's
# frozen export surfaces, and this pass adds files beside them rather than editing them.
from api.admin_api_client import AdminApiClient, AdminRole, user_representation
from views.admin_roles import build_roles_view
from views.admin_users import (
    ADMIN_PATH_PREFIX,
    ADMIN_ROLE_REQUIRED_CODE,
    ADMIN_SIGN_IN_REQUIRED_CODE,
    ADMIN_USER_EDIT_PATH,
    ADMIN_USER_ROLES_PATH,
    ADMIN_USERS_PATH,
    MODEL_CREDENTIAL_FIELD,
    MODEL_EMAIL_FIELD,
    MODEL_ENABLED_FIELD,
    MODEL_TENANT_NAME_FIELD,
    MODEL_TENANT_TYPE_FIELD,
    MODEL_USERNAME_FIELD,
    TEACHER_FIELD,
    TENANT_FIELD,
    USER_ID_FIELD,
    admin_user_roles_path,
    build_admin_card,
    build_user_edit_view,
    build_users_view,
)
from views.errors import (
    RETURN_URI_NOT_ALLOWED_CODE,
    build_error_card,
    build_invalid_return_uri_card,
    build_session_ended_card,
    code_for_error,
)
from views.login import (
    ANTIFORGERY_FIELD,
    LOGIN_PATH,
    PASSWORD_FIELD,
    RETURN_URI_FIELD,
    USERNAME_FIELD,
    build_login_view,
)

logger = logging.getLogger("auth-portal")

HTTP_TIMEOUT_SECONDS = 10.0

#: The opaque portal session cookie (D12). An unguessable id and nothing else — no signature, no
#: key material in Python, and never logged or echoed.
SESSION_COOKIE_NAME = "school_collab_portal_session"

#: The auth service's D16 passkey handoff (B6's ``PasskeyEndpoints.LoginPath``) — the button on
#: the login page navigates the browser here, so the ceremony happens in Keycloak with the auth
#: service as the relying party and no code or token can land in Python (AC12).
PASSKEY_LOGIN_PATH = "/auth/passkey/login"

#: The query parameter carrying the app callback on the passkey handoff (B6's pin).
PASSKEY_APP_REDIRECT_PARAMETER = "app_redirect_uri"

#: This portal's D16 bootstrap landing — the URI the auth service binds its bootstrap code to
#: (``Auth:Portal:BootstrapRedirectUrl`` = ``{portal endpoint}/bootstrap``, B3/B6). The portal
#: sends the identical, normalized spelling when it redeems the code.
BOOTSTRAP_PATH = "/bootstrap"

#: D13 logout (spec §9): revoke at the auth service, then send the browser to its end-session URL.
LOGOUT_PATH = "/logout"

#: This portal's own browser-facing base URL and its app-callback allowlist — both fanned out by
#: the AppHost (B3/B5b). The allowlist copy deliberately excludes this portal's bootstrap URI:
#: it is not a ``return_uri`` the portal itself should accept.
PUBLIC_BASE_URL_KEY = "AuthPortal__PublicBaseUrl"
APP_CALLBACK_PREFIXES_KEY = "AuthPortal__AppCallbackPrefixes"

#: How long an issued login-form antiforgery token stays usable, and how many may live at once
#: (spec §14). The cap keeps an anonymous page-render flood from growing the process unbounded.
ANTIFORGERY_TTL_SECONDS = 600
MAX_LIVE_ANTIFORGERY_TOKENS = 512

#: The only schemes an app callback may use — a configured ``ftp://``/``file://`` entry is
#: malformed, not an alternative transport (the auth service's matcher draws the same line).
_HTTP_SCHEMES = {"http": 80, "https": 443}

# FastAPI's own documentation routes: the route table reports the portal's routes, not these.
_DOC_ROUTE_PATHS = frozenset({"/openapi.json", "/docs", "/docs/oauth2-redirect", "/redoc"})


class AntiforgeryTokenStore:
    """The login form's one-time tokens, held in this process's memory only (spec §14).

    Each login-page render mints a random token and stores it here; ``POST /login`` requires one
    and consumes it, so a body replayed from a page the user never loaded — or a second submit of
    the same page — cannot sign anyone in. Nothing is signed: the token is a random string this
    process happens to remember, which is why there is **no signing key in Python** (D12).

    Expiry and a live cap keep the store bounded: the issuing page is anonymous, and an
    unbounded dictionary fed by unauthenticated GETs is a memory DoS.
    """

    def __init__(self, *, ttl_seconds: int = ANTIFORGERY_TTL_SECONDS) -> None:
        self._ttl = timedelta(seconds=ttl_seconds)
        self._tokens: dict[str, datetime] = {}

    def issue(self) -> str:
        """Mint and store a token for the page about to render."""
        self._sweep()
        token = secrets.token_urlsafe(32)
        self._tokens[token] = datetime.now(timezone.utc) + self._ttl
        return token

    def consume(self, token: str | None) -> bool:
        """Whether ``token`` was issued and is unexpired — consuming it either way.

        Single use by construction: the token is removed before its validity is judged, so a
        replay of a *valid* token is rejected just like an unknown one.
        """
        if not token:
            return False
        expires_at = self._tokens.pop(token, None)
        return expires_at is not None and expires_at > datetime.now(timezone.utc)

    def _sweep(self) -> None:
        now = datetime.now(timezone.utc)
        for token, expires_at in list(self._tokens.items()):
            if expires_at <= now:
                del self._tokens[token]

        # Insertion order is age order: the oldest tokens go first once the cap is reached.
        for token in list(self._tokens)[: max(0, len(self._tokens) - MAX_LIVE_ANTIFORGERY_TOKENS)]:
            del self._tokens[token]


@dataclass(frozen=True)
class AppCallbackPrefix:
    """One configured app callback: scheme, host, port and a normalized path."""

    scheme: str
    host: str
    port: int
    path: str


@dataclass
class AuthPortalState:
    """Per-process portal state — no module-level mutable globals."""

    http: httpx.AsyncClient | None = None
    endpoint: AuthServiceEndpoint | None = None
    last_call: dict[str, Any] = field(default_factory=dict)
    discovery_error: str | None = None
    antiforgery: AntiforgeryTokenStore = field(default_factory=AntiforgeryTokenStore)


def _describe(error: PortalApiError) -> str:
    """A card-ready description that keeps the error's class name visible."""
    return f"{type(error).__name__}: {error}"


def _try_resolve() -> tuple[AuthServiceEndpoint | None, str | None]:
    try:
        return resolve_auth_service_endpoint(), None
    except ServiceDiscoveryError as error:
        return None, _describe(error)


def _resolved_endpoint(state: AuthPortalState) -> AuthServiceEndpoint:
    """The auth-service endpoint, retrying discovery when startup resolution failed.

    Raises :class:`ServiceDiscoveryError` when the AppHost has not injected the variable.
    """
    if state.endpoint is None:
        state.endpoint = resolve_auth_service_endpoint()
        state.discovery_error = None
    return state.endpoint


def _record_call(
    state: AuthPortalState,
    *,
    call: str,
    endpoint: AuthServiceEndpoint,
    error: PortalApiError | None = None,
) -> None:
    """Record what the last auth-service call was and how it went (the ``/health`` diagnostic)."""
    state.last_call = {
        "status": "ok" if error is None else "error",
        "call": call,
        "base_url": endpoint.base_url,
        "env_var": endpoint.env_var,
    }
    if error is not None:
        state.last_call["error"] = _describe(error)


def _public_base_url() -> str | None:
    """This portal's own browser-facing base URL, or ``None`` when it is not configured."""
    configured = os.environ.get(PUBLIC_BASE_URL_KEY)
    return configured.strip().rstrip("/") if configured and configured.strip() else None


def portal_bootstrap_uri() -> str | None:
    """The URI the auth service binds its D16 bootstrap code to (B6's normalization).

    ``Auth:Portal:BootstrapRedirectUrl`` is the portal's Aspire endpoint plus ``/bootstrap``, and
    the redemption's ``redirectUri`` must be that exact spelling — so this is built from the same
    base URL the AppHost fanned out, not from the request.
    """
    base_url = _public_base_url()
    return f"{base_url}{BOOTSTRAP_PATH}" if base_url else None


def parse_app_callback_prefixes(configured: str | None) -> tuple[AppCallbackPrefix, ...]:
    """Parse a semicolon-separated app-callback allowlist (the B5b spelling).

    The portal's Python mirror of the auth service's ``AppCallbackAllowlist``: an entry that is
    blank, unparseable, not http(s), hostless, carrying userinfo, or carrying a query/fragment of
    its own grants nothing and is dropped. An empty result denies every candidate — fail-closed,
    so an unconfigured portal renders the card rather than redirecting anywhere.
    """
    if not configured or not configured.strip():
        return ()

    prefixes: list[AppCallbackPrefix] = []
    for entry in configured.split(";"):
        entry = entry.strip()
        if not entry:
            continue
        # A configured entry carrying a query or fragment could never be matched (matching
        # ignores both), so it is malformed rather than a wider rule.
        split = urlsplit(entry)
        if split.query or split.fragment:
            continue
        uri = _split_uri(entry)
        if uri is not None:
            scheme, host, port, path = uri
            prefixes.append(AppCallbackPrefix(scheme, host, port, _normalized_path(path)))
    return tuple(prefixes)


def configured_app_callback_prefixes() -> tuple[AppCallbackPrefix, ...]:
    """The portal's own copy of the allowlist, read from the AppHost-injected environment."""
    return parse_app_callback_prefixes(os.environ.get(APP_CALLBACK_PREFIXES_KEY))


def is_allowed_app_callback(
    redirect_uri: str | None, prefixes: Sequence[AppCallbackPrefix]
) -> bool:
    """Whether ``redirect_uri`` may receive a sign-in code (spec §14, AC13).

    An absolute http(s) URI without userinfo whose scheme, host and port equal a configured
    prefix and whose path is that prefix's path exactly or a ``/``-delimited extension of it.
    The query and fragment are ignored: the Blazor callbacks are minted as
    ``{scheme}://{host}{pathBase}/signin-handshake?ReturnUrl=…``, so the nested ``ReturnUrl``
    must not break the match while the destination stays pinned. Anything else — a relative
    path, an unparseable value, a blank one, an empty allowlist — returns ``False``.
    """
    candidate = _split_uri(redirect_uri)
    if candidate is None:
        return False

    scheme, host, port, path = candidate
    return any(
        prefix.scheme == scheme
        and prefix.host == host
        and prefix.port == port
        and (path == prefix.path or path.startswith(prefix.path + "/"))
        for prefix in prefixes
    )


def _split_uri(value: str | None) -> tuple[str, str, int, str] | None:
    """``(scheme, host, port, path)`` for a usable http(s) URI, else ``None``.

    Component-wise on a parsed URI, never a substring test on the caller's text:
    ``http://evil.example/?next=http://localhost:5300/signin-handshake`` contains an allowlisted
    prefix but reaches an attacker host. The port is the scheme's default when it is omitted, so
    ``http://host/x`` and ``http://host:80/x`` are one destination.

    The path is compared **as written** (no percent-decoding) and a path carrying a ``.``/``..``
    segment is refused outright. Both rules are the fail-closed half of the same point: a spelling
    a downstream decoder resolves differently must not borrow a prefix's trust. Measured against
    .NET (the C# copy's parser): ``Uri.AbsolutePath`` keeps ``%2F``/``%73`` escaped — so the C#
    matcher also refuses those — but it *resolves* dot segments, so it sees
    ``/signin-handshake/../evil`` as ``/evil`` and refuses it too. Refusing both spellings here is
    the equivalent outcome without re-implementing RFC 3986 ``remove_dot_segments``; the only
    spelling this refuses that the C# copy would resolve-and-accept is ``/signin-handshake/./x``,
    which is a narrower (never a wider) target set.
    """
    if not value or not value.strip():
        return None

    try:
        split = urlsplit(value.strip())
        scheme = split.scheme.lower()
        if scheme not in _HTTP_SCHEMES:
            return None
        host = split.hostname
        if not host or split.username is not None or split.password is not None:
            return None
        port = split.port if split.port is not None else _HTTP_SCHEMES[scheme]
    except ValueError:
        # A malformed port, an unbracketed IPv6 literal, ...: malformed matches nothing.
        return None

    path = split.path or "/"
    if any(segment in (".", "..") for segment in path.split("/")):
        return None

    return scheme, host, port, path


def _normalized_path(path: str) -> str:
    """A configured entry's path without a trailing slash (``/x/`` and ``/x`` are one prefix)."""
    return path.rstrip("/") if path.rstrip("/") else "/"


def _with_code(uri: str, code: str) -> str:
    """``uri`` plus a one-time code as a query parameter.

    The app callbacks already carry a ``ReturnUrl`` query (B1/B4), so the separator has to be
    chosen rather than assumed — the same rule the auth service applies when it 302s here.
    """
    separator = "&" if "?" in uri else "?"
    return f"{uri}{separator}code={quote(code, safe='')}"


def _set_session_cookie(response: Response, session_id: str) -> None:
    """Set the opaque D12 session cookie: an id, ``HttpOnly``, ``SameSite=Lax`` and — over HTTPS
    — ``Secure``.

    ``Secure`` follows the scheme of this portal's configured browser-facing base URL
    (``AuthPortal:PublicBaseUrl``, the AppHost's fan-out) rather than being hardcoded either way:
    D12 pins ``HttpOnly`` + ``SameSite=Lax``, the dev endpoints are plain ``http`` where a
    ``Secure`` cookie would never be sent, and a production deployment reached over ``https``
    must not ship without it. An unset base URL leaves it off, like dev.
    """
    response.set_cookie(
        SESSION_COOKIE_NAME,
        session_id,
        path="/",
        httponly=True,
        samesite="lax",
        secure=_public_base_url_is_https(),
    )


def _public_base_url_is_https() -> bool:
    """Whether this portal is configured to be reached over HTTPS (the ``Secure`` flag's rule)."""
    base_url = _public_base_url()
    return bool(base_url) and urlsplit(base_url).scheme.lower() == "https"


def _clear_session_cookie(response: Response) -> None:
    """Drop the portal's session cookie (logout, and every state where it is already dead)."""
    response.delete_cookie(SESSION_COOKIE_NAME, path="/")


def _signed_out_response(location: str) -> RedirectResponse:
    """Send the browser onward with the session cookie cleared."""
    response = RedirectResponse(location, status_code=302)
    _clear_session_cookie(response)
    return response


def _text_field(payload: Mapping[str, Any], name: str) -> str | None:
    """One string field of a JSON request body (``None`` when absent or not a string)."""
    value = payload.get(name)
    return value.strip() if isinstance(value, str) and value.strip() else None


@asynccontextmanager
async def lifespan(application: FastAPI) -> AsyncIterator[None]:
    """Create the pooled HTTP client once, and tolerate a missing endpoint at boot."""
    state = AuthPortalState()
    state.endpoint, state.discovery_error = _try_resolve()
    if state.discovery_error:
        logger.error("auth service discovery failed at startup: %s", state.discovery_error)
    else:
        logger.info("auth portal wired to %s", state.endpoint.label)  # type: ignore[union-attr]

    state.http = httpx.AsyncClient(timeout=HTTP_TIMEOUT_SECONDS)
    application.state.auth_portal = state
    try:
        yield
    finally:
        await state.http.aclose()


app = FastAPI(title="School-Collab auth portal", lifespan=lifespan)


def _portal_state(request: Request) -> AuthPortalState:
    """The process state, created lazily if the lifespan never ran."""
    state: AuthPortalState | None = getattr(request.app.state, "auth_portal", None)
    if state is None:
        state = AuthPortalState(http=httpx.AsyncClient(timeout=HTTP_TIMEOUT_SECONDS))
        request.app.state.auth_portal = state
    return state


async def get_auth_api_client(request: Request) -> AuthApiClient:
    """FastAPI dependency: the typed client for the auth service.

    Discovery is retried here when it failed at startup (the AppHost may inject the variable
    later), and this is the seam the tests override with a stubbed transport via
    ``app.dependency_overrides``. The routes that must map a discovery failure to their own card
    call this function directly instead of depending on it.
    """
    state = _portal_state(request)
    endpoint = _resolved_endpoint(state)  # raises ServiceDiscoveryError
    assert state.http is not None  # created in the lifespan (or lazily above)
    return AuthApiClient(state.http, endpoint)


def _route_table(application: FastAPI) -> list[tuple[str, str]]:
    """This portal's live routes, read from the app itself — never a hand-maintained list."""
    routes = {
        (method, route.path)
        for route in application.routes
        if isinstance(route, APIRoute) and route.path not in _DOC_ROUTE_PATHS
        for method in route.methods
    }
    return sorted(routes, key=lambda entry: (entry[1], entry[0]))


def _render_index(
    *,
    endpoint: AuthServiceEndpoint | None,
    discovery_error: str | None,
    session: SessionData | None,
    session_error: str | None,
    route_table: list[tuple[str, str]],
) -> str:
    """The portal index: wiring, session state and the route table, escaped and card-friendly."""
    if discovery_error:
        service_line = f"<strong>unresolved</strong> — {html.escape(discovery_error)}"
    elif endpoint is not None:
        service_line = html.escape(endpoint.label)
    else:
        service_line = "not resolved"

    if session is not None:
        roles = ", ".join(session.roles) or "none"
        session_line = (
            f"Signed in as {html.escape(session.teacher_id or 'unknown')} "
            f"({html.escape(session.tenant_name or 'no tenant')}) — roles: {html.escape(roles)}"
        )
    elif session_error:
        session_line = html.escape(session_error)
    else:
        session_line = "Not signed in."

    route_rows = "\n".join(
        f"      <li><code>{html.escape(method)} {html.escape(path)}</code></li>"
        for method, path in route_table
    )

    return f"""<!DOCTYPE html>
<html lang="en">
  <head>
    <meta charset="utf-8" />
    <title>School-Collab auth portal</title>
  </head>
  <body>
    <h1>School-Collab auth portal</h1>
    <p>This process holds no credential: credentials and tokens stay in the auth service (AC11).</p>
    <h2>Auth service</h2>
    <p>{service_line}</p>
    <h2>Session</h2>
    <p>{session_line}</p>
    <h2>Routes</h2>
    <ul>
{route_rows}
    </ul>
  </body>
</html>
"""


@app.get("/", response_class=HTMLResponse)
async def portal_index(
    request: Request,
    client: AuthApiClient = Depends(get_auth_api_client),
) -> HTMLResponse:
    """The portal index, including the session state read from the auth service (D18)."""
    state = _portal_state(request)
    session_id = request.cookies.get(SESSION_COOKIE_NAME)
    session: SessionData | None = None
    session_error: str | None = None

    if session_id:
        try:
            session = await client.read_session(session_id)
            _record_call(state, call="read_session", endpoint=client.endpoint)
        except SessionEndedError as error:
            # D18: a failed refresh is not a degraded page, it is a *dead* session. Clearing the
            # cookie and rendering the card is the whole point — leaving the cookie in place only
            # produces the "signed in but nothing works" state D18 rules out.
            _record_call(state, call="read_session", endpoint=client.endpoint, error=error)
            logger.warning("portal index: the session ended; clearing the cookie")
            response = HTMLResponse(
                build_session_ended_card(
                    detail=_describe(error), endpoint_label=client.endpoint.label
                ).html()
            )
            _clear_session_cookie(response)
            return response
        except SessionNotFoundError as error:
            # The same treatment for the code the auth service uses for a cookie it does not know
            # (revoked elsewhere, or a restarted service): B5 documents it as "already logged
            # out", and the cookie can only fail every subsequent call.
            _record_call(state, call="read_session", endpoint=client.endpoint, error=error)
            logger.warning("portal index: unknown session id; clearing the cookie")
            response = HTMLResponse(
                build_error_card(
                    code="session_not_found",
                    detail=_describe(error),
                    endpoint_label=client.endpoint.label,
                ).html()
            )
            _clear_session_cookie(response)
            return response
        except PortalApiError as error:
            # A degraded session read (auth service/Keycloak down) is page state, never a 500
            # (AC10): the index still renders, with the failure named.
            session_error = _describe(error)
            _record_call(state, call="read_session", endpoint=client.endpoint, error=error)
            logger.warning("portal index degraded: %s", error)

    return HTMLResponse(
        _render_index(
            endpoint=client.endpoint,
            discovery_error=state.discovery_error,
            session=session,
            session_error=session_error,
            route_table=_route_table(request.app),
        )
    )


@app.get(LOGIN_PATH, response_class=HTMLResponse)
async def login_page(
    request: Request,
    return_uri: str | None = None,
    error: str | None = None,
) -> HTMLResponse:
    """The prefab sign-in page, or the AC10 card for a ``return_uri`` the portal will not honor.

    The ``return_uri`` is validated **before the form renders** (spec §14, AC13): a crafted
    ``/login?return_uri=attacker`` link must be a page, not a redirect, and the value must never
    reach the submit body. ``error`` is a bounded code from a refused or failed submit — the
    page the login form navigates back to (see :func:`login_submit`), so a failure is a real
    card at HTTP 200 rather than a lost fetch response.
    """
    state = _portal_state(request)

    if not is_allowed_app_callback(return_uri, configured_app_callback_prefixes()):
        logger.warning("login page refused a return_uri outside the app-callback allowlist")
        return HTMLResponse(build_invalid_return_uri_card().html())

    return HTMLResponse(
        build_login_view(
            antiforgery_token=state.antiforgery.issue(),
            return_uri=return_uri,
            passkey_login_url=_passkey_login_url(state),
            error_code=error,
        ).html()
    )


@app.post(LOGIN_PATH)
async def login_submit(
    request: Request,
    client: AuthApiClient = Depends(get_auth_api_client),
) -> Response:
    """The form's own route: antiforgery, then credentials -> ``/auth/exchange`` -> the session.

    Fail-closed, in this order and for these reasons (spec §14):

    * **JSON only.** A cross-site page cannot POST ``application/json`` without a CORS preflight,
      which this portal never answers (no CORS policy for foreign origins) — so the media type
      is the first guard, ahead of any parsing.
    * **A one-time antiforgery token**, minted with the page render and consumed here. It is
      required and single-use, and there is no signing key anywhere in Python (D12).
    * **The ``return_uri`` is re-validated against the portal's own allowlist** before it is used
      at all; an invalid one is refused without an upstream call and never appears in the reply.

    Success answers HTTP 200 ``{"redirect_uri": "<validated return_uri>?code=…"}`` and sets the
    opaque session cookie; the renderer navigates the current tab to that URL. A refusal or a
    failure answers the same shape pointing at this portal's own card page — the browser must
    never be sent to a URL this route could not vouch for, and the prefab `Fetch` action cannot
    consume a 302 (it would follow it cross-origin, dropping cookies and burning the code).
    """
    state = _portal_state(request)

    # TRANSPORT GUARDS, deliberately not AC10 cards. These three refusals (wrong media type,
    # unparseable body, missing/spent antiforgery token) happen *before* anything the portal
    # could describe as a sign-in outcome: they are a malformed or forged request being turned
    # away, so they answer with a non-2xx status and a machine code, not with the page-shaped
    # failure the AC10 cards render. A caller that gets one of these is not a user looking at a
    # form — it is a request the portal will not read. Everything from the credential exchange
    # onward *is* a user-visible outcome and answers 200 with the card page (see `_login_failure`).
    if not _is_json_request(request):
        logger.warning("login POST refused: content type was not application/json")
        return JSONResponse({"error": "unsupported_media_type"}, status_code=415)

    payload = await _json_object(request)
    if payload is None:
        return JSONResponse({"error": "invalid_body"}, status_code=400)

    if not state.antiforgery.consume(_text_field(payload, ANTIFORGERY_FIELD)):
        logger.warning("login POST refused: missing or already-used antiforgery token")
        return JSONResponse({"error": "antiforgery_rejected"}, status_code=400)

    return_uri = _text_field(payload, RETURN_URI_FIELD)
    if not is_allowed_app_callback(return_uri, configured_app_callback_prefixes()):
        logger.warning("login POST refused a return_uri outside the app-callback allowlist")
        return _login_failure(RETURN_URI_NOT_ALLOWED_CODE, return_uri=None)

    username = _text_field(payload, USERNAME_FIELD)
    password = _text_field(payload, PASSWORD_FIELD)
    if not username or not password:
        # The form's own `required`/model constraints cover the browser path; a hand-made call
        # gets the same card without costing an upstream round trip.
        return _login_failure("credentials_missing", return_uri=return_uri)

    try:
        # The identical string goes to the auth service, which binds the one-time code to it
        # (round A's redemption compares it exactly, and the app redeems with its own spelling).
        result = await client.exchange(username, password, return_uri)
    except PortalApiError as error:
        logger.warning("login POST failed: %s", error)
        _record_call(state, call="exchange", endpoint=client.endpoint, error=error)
        return _login_failure(code_for_error(error), return_uri=return_uri)

    if not result.code or not result.session_id:
        # A "success" without a code or a session id cannot sign anyone in, and must not be
        # turned into a redirect that carries nothing (the DTOs tolerate a shape drift).
        logger.error("login POST: the exchange returned no code or session id")
        return _login_failure("unusable_response", return_uri=return_uri)

    _record_call(state, call="exchange", endpoint=client.endpoint)
    logger.info("login POST: exchanged credentials for a portal session")
    response = JSONResponse({"redirect_uri": _with_code(return_uri, result.code)})
    _set_session_cookie(response, result.session_id)
    return response


@app.get(BOOTSTRAP_PATH)
async def bootstrap_redemption(
    request: Request,
    client: AuthApiClient = Depends(get_auth_api_client),
    code: str | None = None,
) -> Response:
    """The D16 passkey landing: redeem the single-use bootstrap code for this portal's session.

    Reached by a **top-level browser navigation** (the auth service's 302 after the WebAuthn
    ceremony), so this is a real 302 onward — unlike the form's fetch-submitted route. The
    ``redirectUri`` sent is this portal's bootstrap URI in the auth service's own normalized
    spelling, which is what the code was bound to; the response carries no token (AC12).

    A Blazor-originated sign-in also brings the D6 continuation (that app's callback plus a
    one-time code), which the portal hands onward. Those values are re-validated against this
    portal's allowlist before anything redirects — the auth service validated the persisted
    value at issuance, and this is the portal's own half of the same rule.
    """
    state = _portal_state(request)
    bootstrap_uri = portal_bootstrap_uri()
    if bootstrap_uri is None:
        logger.error("bootstrap redemption refused: %s is not configured", PUBLIC_BASE_URL_KEY)
        return HTMLResponse(build_error_card(code="bootstrap_unconfigured").html())

    if not code:
        return HTMLResponse(build_error_card(code="bootstrap_code_missing").html())

    try:
        redemption = await client.redeem_bootstrap_code(code, bootstrap_uri)
    except PortalApiError as error:
        logger.warning("bootstrap redemption failed: %s", error)
        _record_call(state, call="redeem_bootstrap_code", endpoint=client.endpoint, error=error)
        return _bootstrap_card(code_for_error(error), endpoint=client.endpoint, error=error)

    app_redirect_uri = redemption.app_redirect_uri
    app_code = redemption.app_code

    if app_redirect_uri is None and app_code is None:
        # A sign-in that began at the portal itself needs only its session id.
        target = "/"
    elif app_redirect_uri and app_code:
        if not is_allowed_app_callback(app_redirect_uri, configured_app_callback_prefixes()):
            logger.error("bootstrap redemption refused a non-allowlisted continuation target")
            return _bootstrap_card(RETURN_URI_NOT_ALLOWED_CODE, endpoint=client.endpoint)
        target = _with_code(app_redirect_uri, app_code)
    else:
        # Half a continuation cannot sign the app in, and must not be redirected to as if it
        # could: the auth service issues the D6 code and callback together (D16).
        logger.error("bootstrap redemption: the continuation was incomplete")
        return _bootstrap_card("unusable_response", endpoint=client.endpoint)

    _record_call(state, call="redeem_bootstrap_code", endpoint=client.endpoint)
    logger.info("bootstrap redemption: portal session established")
    response = RedirectResponse(target, status_code=302)
    _set_session_cookie(response, redemption.session_id)
    return response


@app.get(LOGOUT_PATH)
async def logout(
    request: Request,
    client: AuthApiClient = Depends(get_auth_api_client),
) -> Response:
    """D13 (spec §9): revoke the session server-side, clear the cookie, go to Keycloak's logout.

    The auth service revokes the refresh token itself and answers with the fully-built
    ``end_session`` URL, because the portal holds neither the id token nor the ability to sign
    one (D12/AC11). Note that under the parent's option (iii) the auth service may later perform
    this browser-facing redirect itself, with ``id_token_hint`` in a ``Location`` header; this
    route then redirects to the auth service's logout endpoint instead of to a URL it received.

    Every outcome clears the portal's cookie — a logout that leaves a cookie behind is the one
    thing this route must never do. A failed revocation still clears it and says so: the local
    sign-in is over, and the Keycloak session's fate must not be hidden.
    """
    state = _portal_state(request)
    session_id = request.cookies.get(SESSION_COOKIE_NAME)

    if not session_id:
        # Nothing to revoke: the browser is signed out already.
        return _signed_out_response("/")

    try:
        revocation = await client.revoke_session(session_id)
    except SessionNotFoundError as error:
        # The auth service already forgot this session: locally signed out, nothing to redirect to.
        _record_call(state, call="revoke_session", endpoint=client.endpoint, error=error)
        return _signed_out_response("/")
    except PortalApiError as error:
        logger.warning("logout failed: %s", error)
        _record_call(state, call="revoke_session", endpoint=client.endpoint, error=error)
        return _logout_card(
            code_for_error(error), detail=_describe(error), endpoint=client.endpoint
        )

    if not revocation.end_session_url:
        logger.error("logout: the revocation returned no end_session URL")
        return _logout_card("unusable_response", endpoint=client.endpoint)

    _record_call(state, call="revoke_session", endpoint=client.endpoint)
    logger.info("logout: session revoked; redirecting the browser to Keycloak's end-session URL")
    return _signed_out_response(revocation.end_session_url)


def _login_failure(code: str, *, return_uri: str | None) -> JSONResponse:
    """A refused or failed `POST /login`: HTTP 200 and the portal's own error-card URL.

    AC10 makes a sign-in failure a **page** at HTTP 200, and the renderer can only get there by
    navigating — so the body names this portal's ``/login`` with the bounded code, which renders
    the card. ``return_uri`` is only carried when it already passed validation, so a rejected
    address is never reflected back into a URL the browser will visit.
    """
    parameters = {"error": code}
    if return_uri:
        parameters[RETURN_URI_FIELD] = return_uri
    return JSONResponse({"redirect_uri": f"{LOGIN_PATH}?{urlencode(parameters)}"})


def _bootstrap_card(
    code: str,
    *,
    endpoint: AuthServiceEndpoint | None,
    error: PortalApiError | None = None,
) -> HTMLResponse:
    """The card a bootstrap redemption renders — before any cookie is set and any 302 emitted."""
    return HTMLResponse(
        build_error_card(
            code=code,
            detail=_describe(error) if error else None,
            endpoint_label=endpoint.label if endpoint else None,
        ).html()
    )


def _logout_card(
    code: str,
    *,
    detail: str | None = None,
    endpoint: AuthServiceEndpoint | None = None,
) -> HTMLResponse:
    """The card a failed logout renders, with the cookie cleared: the local sign-out stands."""
    response = HTMLResponse(
        build_error_card(
            code=code, detail=detail, endpoint_label=endpoint.label if endpoint else None
        ).html()
    )
    _clear_session_cookie(response)
    return response


def _passkey_login_url(state: AuthPortalState) -> str | None:
    """The auth service's D16 passkey handoff for a portal-initiated sign-in (B6's pin).

    The ``app_redirect_uri`` is this portal's bootstrap URI: the one portal URL the auth
    service's allowlist carries (B3), and — sharing the bootstrap URL's origin — the value B6
    recognises as the portal's *own* sign-in, so no D6 continuation is issued for it. Without a
    configured base URL (or a resolvable auth service) there is nothing allowlistable to hand
    off to, so the login page omits the button.
    """
    bootstrap_uri = portal_bootstrap_uri()
    if bootstrap_uri is None:
        return None

    try:
        endpoint = _resolved_endpoint(state)
    except ServiceDiscoveryError:
        return None

    return (
        f"{endpoint.base_url}{PASSKEY_LOGIN_PATH}"
        f"?{PASSKEY_APP_REDIRECT_PARAMETER}={quote(bootstrap_uri, safe='')}"
    )


def _is_json_request(request: Request) -> bool:
    """Whether the request declares a JSON body (spec §14's first CSRF guard)."""
    content_type = request.headers.get("content-type", "")
    media_type = content_type.split(";", 1)[0].strip().lower()
    return media_type == "application/json"


async def _json_object(request: Request) -> Mapping[str, Any] | None:
    """The request body as a JSON object, or ``None`` when it is not one."""
    try:
        payload = json.loads(await request.body())
    except (ValueError, UnicodeDecodeError):
        return None
    return payload if isinstance(payload, Mapping) else None


@app.exception_handler(ServiceDiscoveryError)
async def on_discovery_error(request: Request, error: ServiceDiscoveryError) -> Response:
    """A missing auth-service endpoint is a page-level degraded state, never a 500.

    The failure arrives while a route's client dependency is being resolved — before any route
    body runs — so mapping it to that route's own card has to happen here, by path. The index
    keeps its established degraded page; the sign-in routes each answer in the shape their
    caller expects (the form's fetch needs the JSON hand-off, not an HTML page).
    """
    state = _portal_state(request)
    state.discovery_error = _describe(error)
    state.last_call = {"error": state.discovery_error}
    logger.error("auth portal degraded: %s", error)
    code = code_for_error(error)

    if request.url.path == LOGIN_PATH and request.method == "POST":
        return _login_failure(code, return_uri=None)

    if request.url.path == BOOTSTRAP_PATH:
        return HTMLResponse(build_error_card(code=code, detail=state.discovery_error).html())

    if request.url.path == LOGOUT_PATH:
        if not request.cookies.get(SESSION_COOKIE_NAME):
            # Nothing to revoke, so nothing to report: the browser is signed out already.
            return _signed_out_response("/")
        return _logout_card(code, detail=state.discovery_error)

    if request.url.path.startswith(ADMIN_PATH_PREFIX):
        # The admin surface has no index of its own to degrade to: its every page is the card, and
        # the gate's own refusal is the same card (never a redirect, never a 500).
        return HTMLResponse(
            build_admin_card(code=code, detail=state.discovery_error).html()
        )

    return HTMLResponse(
        _render_index(
            endpoint=None,
            discovery_error=state.discovery_error,
            session=None,
            session_error=None,
            route_table=_route_table(request.app),
        )
    )


@app.get("/health")
def health(request: Request) -> dict[str, Any]:
    """Diagnostics: the resolved endpoint, the last auth-service call, and any discovery error."""
    state = _portal_state(request)
    return {
        "status": "ok" if state.endpoint else "degraded",
        "auth_base_url": state.endpoint.base_url if state.endpoint else None,
        "auth_env_var": state.endpoint.env_var if state.endpoint else None,
        "last_call": state.last_call,
        "discovery_error": state.discovery_error,
    }


# -------------------------------------------------------------------------------------------------
# The admin surface (spec §8, D8/D10/D11). Gated on an authenticated session whose D18 claim-set
# read carries the ``user-admin`` realm role; every refusal and every degraded read is the
# surface's AC10 card at HTTP 200 (never a 403 page, never a redirect, never a redirect loop).
# -------------------------------------------------------------------------------------------------


class AdminAccessRefused(Exception):
    """The admin gate's refusal — rendered as an AC10 card at HTTP 200, never as a status page.

    ``sign_out`` marks the refusals that mean the presented session is already dead (D18), which
    clear the session cookie exactly as the index's session-ended branch does: a cookie that can
    only fail every subsequent call must not be left behind.
    """

    def __init__(self, code: str, *, detail: str | None = None, sign_out: bool = False) -> None:
        self.code = code
        self.detail = detail
        self.sign_out = sign_out
        super().__init__(f"{code}{f': {detail}' if detail else ''}")


async def require_admin_session(
    request: Request,
    client: AuthApiClient = Depends(get_auth_api_client),
) -> SessionData:
    """FastAPI dependency: the authenticated, ``user-admin``-carrying session, or a card.

    The role comes from the **D18 session read** — the claim set as data — because the portal holds
    no token to inspect and must never hold one (AC11). The auth service still enforces the same
    role on every ``/auth/admin/*`` call (B5's portal-facing policy); this gate exists so a
    non-admin sees a card instead of a 403 page, and so no admin call is even attempted.
    """
    state = _portal_state(request)
    session_id = request.cookies.get(SESSION_COOKIE_NAME)
    if not session_id:
        logger.info("admin surface refused: no portal session cookie")
        raise AdminAccessRefused(ADMIN_SIGN_IN_REQUIRED_CODE)

    try:
        session = await client.read_session(session_id)
    except (SessionEndedError, SessionNotFoundError) as error:
        _record_call(state, call="read_session", endpoint=client.endpoint, error=error)
        logger.warning("admin surface refused: the session is gone; clearing the cookie")
        raise AdminAccessRefused(
            code_for_error(error), detail=_describe(error), sign_out=True
        ) from error
    except PortalApiError as error:
        # A degraded session read is page state, not a 500 (AC10): the surface renders its card.
        _record_call(state, call="read_session", endpoint=client.endpoint, error=error)
        logger.warning("admin surface degraded: %s", error)
        raise AdminAccessRefused(code_for_error(error), detail=_describe(error)) from error

    if not session.is_admin:
        logger.warning("admin surface refused: the session carries no admin role")
        raise AdminAccessRefused(ADMIN_ROLE_REQUIRED_CODE)

    _record_call(state, call="read_session", endpoint=client.endpoint)
    return session


async def get_admin_api_client(
    request: Request,
    _session: SessionData = Depends(require_admin_session),
) -> AdminApiClient:
    """FastAPI dependency: the typed admin client, presenting the portal session id (D12).

    The gate dependency is the point of the signature: the client is only ever built for a session
    that passed it, and the id it presents is read from the same opaque cookie the gate validated —
    never echoed into a page and never logged.
    """
    state = _portal_state(request)
    endpoint = _resolved_endpoint(state)  # raises ServiceDiscoveryError
    session_id = request.cookies.get(SESSION_COOKIE_NAME)
    if not session_id:
        # Unreachable through the gate above; kept fail-closed so the client can never be built
        # without a session to present.
        raise AdminAccessRefused(ADMIN_SIGN_IN_REQUIRED_CODE)

    assert state.http is not None  # created in the lifespan (or lazily)
    return AdminApiClient(state.http, endpoint, session_id)


@app.exception_handler(AdminAccessRefused)
async def on_admin_access_refused(request: Request, error: AdminAccessRefused) -> Response:
    """Render the gate's refusal as the surface's card at HTTP 200, clearing a dead cookie."""
    logger.warning("admin surface refused: %s", error)
    response = HTMLResponse(build_admin_card(code=error.code, detail=error.detail).html())
    if error.sign_out:
        _clear_session_cookie(response)
    return response


@app.get(ADMIN_USERS_PATH, response_class=HTMLResponse)
async def admin_users(
    request: Request,
    client: AdminApiClient = Depends(get_admin_api_client),
    notice: str | None = None,
    error: str | None = None,
) -> Response:
    """The realm-user list and the create form, from the admin surface plus B7's picker reads."""
    state = _portal_state(request)
    try:
        # One degraded read degrades the page, rather than rendering a half-usable surface: the
        # create form cannot bind a user without both mediated picker reads (D8).
        users = await client.list_users()
        tenants = await client.list_picker_tenants()
        teachers = await client.list_picker_teachers()
    except PortalApiError as failure:
        return _admin_degraded(state, client=client, call="list_users", error=failure)

    _record_call(state, call="list_users", endpoint=client.endpoint)
    return HTMLResponse(
        build_users_view(
            users=users, tenants=tenants, teachers=teachers, notice=notice, error=error
        ).html()
    )


@app.get(ADMIN_USER_EDIT_PATH, response_class=HTMLResponse)
async def admin_user_edit(
    request: Request,
    user_id: str,
    client: AdminApiClient = Depends(get_admin_api_client),
    notice: str | None = None,
    error: str | None = None,
) -> Response:
    """The edit form for one realm user: attributes, enable/disable and a credential reset."""
    state = _portal_state(request)
    try:
        user = await client.get_user(user_id)
        tenants = await client.list_picker_tenants()
        teachers = await client.list_picker_teachers()
    except PortalApiError as failure:
        return _admin_degraded(state, client=client, call="get_user", error=failure)

    _record_call(state, call="get_user", endpoint=client.endpoint)
    return HTMLResponse(
        build_user_edit_view(
            user=user,
            tenants=tenants,
            teachers=teachers,
            roles_path=admin_user_roles_path(user_id),
            notice=notice,
            error=error,
        ).html()
    )


@app.get(ADMIN_USER_ROLES_PATH, response_class=HTMLResponse)
async def admin_user_roles(
    request: Request,
    user_id: str,
    client: AdminApiClient = Depends(get_admin_api_client),
    notice: str | None = None,
    error: str | None = None,
) -> Response:
    """The realm-role assignment page: the **existing** role set, assignable and revocable (D11)."""
    state = _portal_state(request)
    try:
        roles = await client.list_realm_roles()
    except PortalApiError as failure:
        return _admin_degraded(state, client=client, call="list_realm_roles", error=failure)

    _record_call(state, call="list_realm_roles", endpoint=client.endpoint)
    return HTMLResponse(
        build_roles_view(user_id=user_id, roles=roles, notice=notice, error=error).html()
    )


@app.post(ADMIN_USERS_PATH)
async def admin_create_user(
    request: Request,
    client: AdminApiClient = Depends(get_admin_api_client),
) -> Response:
    """Create the realm user (identity only, D8) and set its initial credential.

    Two auth-service calls, because the admin surface's create body carries **no credential** and
    its response names no user id (round A's contract): the user is created, resolved by its exact
    username, and given the credential through ``reset-password``. A create that cannot be resolved
    still reports what happened — the user exists with no credential — rather than pretending it
    worked. No credential value is logged, echoed or retained here.
    """
    state = _portal_state(request)

    # TRANSPORT GUARDS (the B8 precedent): a body this route will not read — wrong media type or
    # unparseable — is a malformed request, so it is refused with a machine code rather than given
    # a page-shaped AC10 outcome. SameSite=Lax already keeps the session cookie off a cross-site
    # POST; requiring JSON is the cheap second half of that guard.
    if not _is_json_request(request):
        logger.warning("admin create refused: content type was not application/json")
        return JSONResponse({"error": "unsupported_media_type"}, status_code=415)

    payload = await _json_object(request)
    if payload is None:
        return JSONResponse({"error": "invalid_body"}, status_code=400)

    username = _text_field(payload, MODEL_USERNAME_FIELD)
    if not username:
        return _admin_outcome(ADMIN_USERS_PATH, error="username_required")

    credential = _text_field(payload, MODEL_CREDENTIAL_FIELD)
    try:
        await client.create_user(
            user_representation(
                username=username,
                enabled=_bool_field(payload, MODEL_ENABLED_FIELD),
                email=_text_field(payload, MODEL_EMAIL_FIELD),
                attributes=_admin_attributes(payload),
            )
        )
        credential_set = credential is None or await _set_credential(
            client, username=username, credential=credential
        )
    except PortalApiError as failure:
        logger.warning("admin create failed: %s", failure)
        _record_call(state, call="create_user", endpoint=client.endpoint, error=failure)
        return _admin_outcome(
            ADMIN_USERS_PATH, error=code_for_error(failure), detail=_describe(failure)
        )

    _record_call(state, call="create_user", endpoint=client.endpoint)
    if not credential_set:
        logger.error("admin create: the created user could not be resolved to set its credential")
        return _admin_outcome(ADMIN_USERS_PATH, error="credential_not_set")

    logger.info("admin create: realm user created and its credential set")
    return _admin_outcome(ADMIN_USERS_PATH, notice="created")


@app.put(ADMIN_USERS_PATH)
async def admin_update_user(
    request: Request,
    client: AdminApiClient = Depends(get_admin_api_client),
) -> Response:
    """The full-representation update the auth service's admin ``PUT`` takes, plus an optional
    credential reset (the same call the create path uses, keyed on the id the body carries)."""
    state = _portal_state(request)

    if not _is_json_request(request):
        logger.warning("admin update refused: content type was not application/json")
        return JSONResponse({"error": "unsupported_media_type"}, status_code=415)

    payload = await _json_object(request)
    if payload is None:
        return JSONResponse({"error": "invalid_body"}, status_code=400)

    user_id = _text_field(payload, USER_ID_FIELD)
    if not user_id:
        return _admin_outcome(ADMIN_USERS_PATH, error="user_id_required")

    username = _text_field(payload, MODEL_USERNAME_FIELD)
    if not username:
        return _admin_outcome(ADMIN_USERS_PATH, error="username_required")

    credential = _text_field(payload, MODEL_CREDENTIAL_FIELD)
    try:
        await client.update_user(
            user_representation(
                username=username,
                enabled=_bool_field(payload, MODEL_ENABLED_FIELD),
                email=_text_field(payload, MODEL_EMAIL_FIELD),
                attributes=_admin_attributes(payload),
                user_id=user_id,
            )
        )
        if credential:
            await client.reset_password(user_id, credential)
    except PortalApiError as failure:
        logger.warning("admin update failed: %s", failure)
        _record_call(state, call="update_user", endpoint=client.endpoint, error=failure)
        return _admin_outcome(
            ADMIN_USERS_PATH, error=code_for_error(failure), detail=_describe(failure)
        )

    _record_call(state, call="update_user", endpoint=client.endpoint)
    logger.info("admin update: realm user representation replaced")
    return _admin_outcome(ADMIN_USERS_PATH, notice="updated")


@app.post(ADMIN_USER_ROLES_PATH)
async def admin_assign_role(
    request: Request,
    user_id: str,
    client: AdminApiClient = Depends(get_admin_api_client),
) -> Response:
    """Grant one existing realm role to one user (a role *definition* is never touched, D11)."""
    return await _change_role(request, user_id=user_id, client=client, assign=True)


@app.delete(ADMIN_USER_ROLES_PATH)
async def admin_unassign_role(
    request: Request,
    user_id: str,
    client: AdminApiClient = Depends(get_admin_api_client),
) -> Response:
    """Revoke one existing realm role from one user (D11: definitions stay in the realm import)."""
    return await _change_role(request, user_id=user_id, client=client, assign=False)


async def _change_role(
    request: Request, *, user_id: str, client: AdminApiClient, assign: bool
) -> Response:
    """The assign/unassign body both role routes share: one ``{id, name}`` pair, or a refusal."""
    state = _portal_state(request)
    page = admin_user_roles_path(user_id)

    if not _is_json_request(request):
        logger.warning("admin role change refused: content type was not application/json")
        return JSONResponse({"error": "unsupported_media_type"}, status_code=415)

    payload = await _json_object(request)
    if payload is None:
        return JSONResponse({"error": "invalid_body"}, status_code=400)

    role = _role_from_payload(payload)
    if role is None:
        # The role-mappings endpoint keys on role ids, so a name alone is not enough to act on.
        return _admin_outcome(page, error="role_required")

    call = "assign_realm_role" if assign else "unassign_realm_role"
    try:
        if assign:
            await client.assign_realm_role(user_id, role)
        else:
            await client.unassign_realm_role(user_id, role)
    except PortalApiError as failure:
        logger.warning("admin %s failed: %s", call, failure)
        _record_call(state, call=call, endpoint=client.endpoint, error=failure)
        return _admin_outcome(page, error=code_for_error(failure), detail=_describe(failure))

    _record_call(state, call=call, endpoint=client.endpoint)
    logger.info("admin role change: %s applied for the realm user", call)
    return _admin_outcome(page, notice="assigned" if assign else "unassigned")


async def _set_credential(
    client: AdminApiClient, *, username: str, credential: str
) -> bool:
    """Set a newly created user's credential: resolve its id by exact username, then reset.

    Returns whether the credential was set. The admin surface's create response carries no user id
    (round A's contract), so the created user is looked up by its exact username — the same
    ``GET /auth/admin/users`` read the list page uses. A missing (or id-less) match is reported to
    the caller instead of being swallowed: the realm user exists at that point, and saying so is
    the honest outcome.
    """
    matches = await client.list_users(username=username)
    target = next(
        (user for user in matches if user.username == username and user.id is not None), None
    )
    if target is None or target.id is None:
        return False

    await client.reset_password(target.id, credential)
    return True


def _admin_attributes(payload: Mapping[str, Any]) -> dict[str, str]:
    """The D10 claim attributes the form carries (blank text is not written as an attribute)."""
    values = {
        TENANT_FIELD: _text_field(payload, TENANT_FIELD),
        MODEL_TENANT_NAME_FIELD: _text_field(payload, MODEL_TENANT_NAME_FIELD),
        MODEL_TENANT_TYPE_FIELD: _text_field(payload, MODEL_TENANT_TYPE_FIELD),
        TEACHER_FIELD: _text_field(payload, TEACHER_FIELD),
    }
    return {name: value for name, value in values.items() if value}


def _bool_field(payload: Mapping[str, Any], name: str) -> bool:
    """A checkbox's value: the form's checkbox binds a boolean, and an absent one means enabled."""
    value = payload.get(name)
    return value if isinstance(value, bool) else True


def _role_from_payload(payload: Mapping[str, Any]) -> AdminRole | None:
    """The ``{id, name}`` role pair a role-mapping body carries, or ``None`` when unusable."""
    roles = payload.get("roles")
    if not isinstance(roles, list) or not roles or not isinstance(roles[0], Mapping):
        return None

    role = roles[0]
    role_id = role.get("id")
    name = role.get("name")
    if not isinstance(role_id, str) or not role_id.strip():
        return None
    return AdminRole(
        id=role_id.strip(), name=name.strip() if isinstance(name, str) else ""
    )


def _admin_outcome(
    page: str, *, notice: str | None = None, error: str | None = None, detail: str | None = None
) -> JSONResponse:
    """A mutation's answer: HTTP 200 and this portal's own page, which renders the outcome.

    The same shape the sign-in form uses (B8): ``Fetch`` follows redirects and has no redirect
    option, so the route answers 200 with ``{"redirect_uri": …}`` and the page's navigation
    handler moves the browser. The target is always this portal's own page — never a foreign URL —
    and a failure carries a **bounded** code, never an upstream body.
    """
    parameters: dict[str, str] = {}
    if notice:
        parameters["notice"] = notice
    if error:
        parameters["error"] = error
    query = f"?{urlencode(parameters)}" if parameters else ""
    if detail:
        logger.warning("admin outcome %s: %s", error, detail)
    return JSONResponse({"redirect_uri": f"{page}{query}"})


def _admin_degraded(
    state: AuthPortalState, *, client: AdminApiClient, call: str, error: PortalApiError
) -> HTMLResponse:
    """A failed admin read: the surface's AC10 card at HTTP 200, never a 500 (AC10)."""
    _record_call(state, call=call, endpoint=client.endpoint, error=error)
    logger.warning("admin surface degraded during %s: %s", call, error)
    return HTMLResponse(
        build_admin_card(
            code=code_for_error(error),
            detail=_describe(error),
            endpoint_label=client.endpoint.label,
        ).html()
    )
