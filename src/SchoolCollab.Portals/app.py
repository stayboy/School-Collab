"""Portal — the ward and teacher surfaces over the existing assignments-api.

Pure HTTP consumer of the existing assignments-api: no backend changes, no
direct database access. Hosted solely by the Aspire AppHost through
``AddUvicornApp`` (see documents/specs/teachers-ward-portal-prefab-plan.md, Q5).

This module is deliberately thin — it owns only the FastAPI app, the
service-call dependency, and the routes:

* service calls live in ``api/`` (typed client + discovery + DTOs + errors)
* Prefab component trees live in ``views/``

The HTTP client is async (``httpx.AsyncClient``) and lifespan-owned, so request
handlers never block the event loop.

See ``documents/solution/portals-service-client-pattern.md`` for the pattern.

Routes:
    GET /       -> the ward view, rendered by Prefab UI from a LIVE
                   assignments-api call made at request time.
    GET /teacher -> the teacher assignment list, the entry point of the
                   read-only teacher drill-down.
    GET /teacher/assignments/{assignmentId} -> that assignment's review queue.
                   The dev bypass's teacher id comes from the portal's own
                   environment (``PORTAL_DEV_TEACHER_ID``, D5) — never a default.
    GET /teacher/assignments/{assignmentId}/students/{studentId} -> the
                   submission detail (version history + review).
    GET /auth/callback -> the teacher portal's OWN session bootstrap (round
                   ``portal-session-adoption``, D19): it redeems the single-use
                   D6 handshake code the auth portal handed the browser for
                   ``{this portal}/auth/callback``, sets the opaque session-id
                   cookie and 302s to ``/teacher``. No token reaches Python.
    GET /teacher/logout -> revoke the session server-side, clear the cookie,
                   and redirect the browser to the auth service's opaque
                   ``end_session`` URL (D13).
    GET /health -> spike diagnostics: the resolved API base URL, the env var
                   that supplied it, and the last fetch result.

Session posture (D19's rule ladder, one place — ``require_teacher_session``):
``/teacher*`` reads carry the opaque session id in the ``X-Portal-Session``
header on every assignments-api call; with no cookie at all the gate acts only
when a sign-in target is configured (``PORTALS_LOGIN_URL``), and otherwise the
routes behave exactly as before this round. Every surface renders its degraded
state as a typed error page — a page, never a raw 500 or a traceback.
"""

from __future__ import annotations

import logging
import os
import uuid
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from dataclasses import dataclass, field
from typing import Any
from urllib.parse import quote

import httpx
from fastapi import Depends, FastAPI, Request
from fastapi.responses import HTMLResponse, RedirectResponse, Response

from api import (
    SESSION_HEADER_NAME,
    AssignmentsApiClient,
    AuthApiClient,
    MissingConfigurationError,
    PortalApiError,
    ServiceDiscoveryError,
    ServiceEndpoint,
    SessionData,
    SessionEndedError,
    SessionNotFoundError,
    resolve_auth_service_endpoint,
    resolve_service_endpoint,
)
from views.teacher import (
    TeacherSurfaceLinks,
    build_review_queue_view,
    build_submission_view,
    build_teacher_error_view,
    build_teacher_list_view,
    build_teacher_session_card,
    session_card_code,
)
from views.ward import build_error_view, build_ward_view

logger = logging.getLogger("portals")

ASSIGNMENTS_API = "assignments-api"
HTTP_TIMEOUT_SECONDS = 10.0

# D5 (round ``portal-teacher-surface``): the dev-bypass teacher id the review queue
# needs. The portal carries no identity, and the assignments-api's queue route reads
# the ``teacherId`` query parameter only as its dev fallback
# (``currentUser.TeacherId ?? (isRealAuth ? throw : teacherId)``), so this has to come
# from configuration: unset (or unusable) means the queue surface says so instead of
# sending a placeholder that would answer an empty queue looking like a real result.
DEV_TEACHER_ID_KEY = "PORTAL_DEV_TEACHER_ID"

# The teacher portal's own opaque session cookie (D19/D12). It is named separately from the auth
# portal's cookie on purpose: the two portals are separate browser-facing apps, and a name is the
# only thing that keeps one portal's session out of the other's request handling. On ``localhost``
# cookies ignore ports, so the auth portal's cookie IS also sent here in dev — this portal reads
# only its own name and never relies on the sharing.
SESSION_COOKIE_NAME = "school_collab_teacher_session"

# This portal's session bootstrap (the D6 handshake code's bound redirect URI, appended to the
# auth service's allowlist and the auth portal's own copy by the AppHost — D6).
AUTH_CALLBACK_PATH = "/auth/callback"

# D13 logout: revoke at the auth service, then send the browser to its end-session URL.
LOGOUT_PATH = "/teacher/logout"

# The auth portal's login page for THIS portal's callback (the AppHost fan-out), the value D19's
# rule 3 keys its redirect on. Deliberately not ``Auth:Portal:LoginUrl``: that key's guard pins it
# to exactly two consumers as the browser-facing CHALLENGE surface, which this link is not.
LOGIN_URL_KEY = "PORTALS_LOGIN_URL"

# This portal's own browser-facing base URL (the callback URI's origin), fanned out by the AppHost
# from the resource's Aspire endpoint. Also decides the session cookie's ``Secure`` flag.
PUBLIC_BASE_URL_KEY = "PORTALS_PUBLIC_BASE_URL"

# The auth service's D16 passkey handoff. The ceremony itself happens in Keycloak with the auth
# service as the relying party, so the portal only links the browser there — no code and no token
# can land in Python (AC11/AC12).
PASSKEY_LOGIN_PATH = "/auth/passkey/login"
PASSKEY_APP_REDIRECT_PARAMETER = "app_redirect_uri"

# The query parameter the auth portal hands the one-time handoff code back on.
CODE_QUERY_PARAMETER = "code"

# The auth portal's login form field carrying the callback it must return to.
RETURN_URI_PARAMETER = "return_uri"


@dataclass
class PortalState:
    """Per-process portal state — no module-level mutable globals."""

    http: httpx.AsyncClient | None = None
    endpoint: ServiceEndpoint | None = None
    last_fetch: dict[str, Any] = field(default_factory=dict)
    discovery_error: str | None = None


def _try_resolve() -> tuple[ServiceEndpoint | None, str | None]:
    try:
        return resolve_service_endpoint(ASSIGNMENTS_API), None
    except ServiceDiscoveryError as error:
        return None, str(error)


@asynccontextmanager
async def lifespan(application: FastAPI) -> AsyncIterator[None]:
    """Create the pooled HTTP client once, and tolerate a missing endpoint at boot.

    A discovery failure must not stop the process: ``/health`` explains it and
    every surface route renders an error card — far more useful in a dev loop
    than an app that refuses to start.
    """
    state = PortalState()
    state.endpoint, state.discovery_error = _try_resolve()
    if state.discovery_error:
        logger.error("service discovery failed at startup: %s", state.discovery_error)
    else:
        logger.info("portals wired to %s", state.endpoint.label)  # type: ignore[union-attr]

    state.http = httpx.AsyncClient(timeout=HTTP_TIMEOUT_SECONDS)
    application.state.portal = state
    try:
        yield
    finally:
        await state.http.aclose()


app = FastAPI(title="School-Collab portal (prefab spike)", lifespan=lifespan)


def _portal_state(request: Request) -> PortalState:
    """The process state, created lazily if the lifespan never ran."""
    state: PortalState | None = getattr(request.app.state, "portal", None)
    if state is None:
        state = PortalState(http=httpx.AsyncClient(timeout=HTTP_TIMEOUT_SECONDS))
        request.app.state.portal = state
    return state


async def get_assignments_client(request: Request) -> AssignmentsApiClient:
    """FastAPI dependency: the typed client for the Assignments API.

    Discovery is retried here when it failed at startup (the AppHost may inject
    the variable later), and this is the seam tests override with a stubbed
    transport via ``app.dependency_overrides``.
    """
    state = _portal_state(request)
    if state.endpoint is None:
        state.endpoint = resolve_service_endpoint(ASSIGNMENTS_API)  # raises ServiceDiscoveryError
        state.discovery_error = None
    assert state.http is not None  # created in the lifespan (or lazily above)
    return AssignmentsApiClient(state.http, state.endpoint)


async def get_auth_client(request: Request) -> AuthApiClient | None:
    """FastAPI dependency: the typed client for the auth service's session surface (D19).

    Returns ``None`` — instead of raising — when the environment has not supplied an auth-service
    endpoint, because that state means something different on each path and the caller is the one
    that knows: the gate's rule 2 (no session cookie, the dev posture) must render today's views
    with no auth service anywhere in sight, while the session path and the callback render their
    degraded card and say so. Declared as a dependency (not called directly) so
    ``app.dependency_overrides`` remains the single test seam, which is how the tests drive this
    whole surface with ``httpx.MockTransport`` and no server.
    """
    state = _portal_state(request)
    try:
        endpoint = resolve_auth_service_endpoint()
    except ServiceDiscoveryError:
        return None

    assert state.http is not None  # created in the lifespan (or lazily above)
    return AuthApiClient(state.http, endpoint)


def _session_cookie(request: Request) -> str | None:
    """The opaque session id this request presents, or ``None`` — the gate's rule switch."""
    session_id = request.cookies.get(SESSION_COOKIE_NAME)
    return session_id.strip() if session_id and session_id.strip() else None


def _set_session_cookie(response: Response, session_id: str) -> None:
    """Set the opaque D12 session cookie: an id, ``HttpOnly``, ``SameSite=Lax`` and — over
    HTTPS — ``Secure``.

    ``Secure`` follows the scheme of this portal's configured browser-facing base URL
    (``PORTALS_PUBLIC_BASE_URL``, the AppHost's fan-out) rather than being hardcoded either way:
    the dev endpoints are plain ``http``, where a ``Secure`` cookie would never be sent, and a
    production deployment reached over ``https`` must not ship without it. An unset base URL
    leaves it off, like dev. Nothing else is in the cookie: no signature, no claim, no key
    material in Python (D12).
    """
    response.set_cookie(
        SESSION_COOKIE_NAME,
        session_id,
        path="/",
        httponly=True,
        samesite="lax",
        secure=_public_base_url_is_https(),
    )


def _clear_session_cookie(response: Response) -> None:
    """Drop the session cookie (logout, and every state where the session is already dead)."""
    response.delete_cookie(SESSION_COOKIE_NAME, path="/")


def _signed_out_response(location: str) -> RedirectResponse:
    """Send the browser onward with the session cookie cleared."""
    response = RedirectResponse(location, status_code=302)
    _clear_session_cookie(response)
    return response


def configured_public_base_url() -> str | None:
    """This portal's configured browser-facing base URL, or ``None`` when it was not injected."""
    return (os.environ.get(PUBLIC_BASE_URL_KEY) or "").strip() or None


def _public_base_url_is_https() -> bool:
    """Whether this portal is configured to be reached over HTTPS (the ``Secure`` flag's rule)."""
    base_url = configured_public_base_url()
    return bool(base_url) and base_url.lower().startswith("https://")


def browser_base_url(request: Request) -> str:
    """The origin the teacher portal's own URLs are built from.

    The AppHost fan-out (``PORTALS_PUBLIC_BASE_URL``) first — it is the value the auth service's
    allowlist carries. Its absence falls back to the origin **the browser actually reached this
    portal at** (``request.base_url``): that is the same address the configured fan-out resolves
    to in dev, so a callback URI is never invented — only observed. A value derived here is never
    trusted by this portal: the auth portal validates every ``return_uri`` against its own
    allowlist before it is honored, and the auth service validates the same URI again at issuance.
    """
    return (configured_public_base_url() or str(request.base_url)).rstrip("/")


def callback_uri(request: Request) -> str:
    """This portal's session callback URI — the D6 code's bound redirect target."""
    return f"{browser_base_url(request)}{AUTH_CALLBACK_PATH}"


def _login_url() -> str | None:
    """The configured auth-portal login page (``PORTALS_LOGIN_URL``) — D19 rule 3's target."""
    return (os.environ.get(LOGIN_URL_KEY) or "").strip() or None


def resolve_teacher_links(request: Request) -> TeacherSurfaceLinks:
    """The sign-in/passkey/logout affordances this environment can actually offer.

    Each link is built from a value the AppHost fanned out; when one is absent the affordance is
    simply left out (``None``), so a page never carries an invented URL. The passkey handoff needs
    the auth service's own endpoint — a discovery failure means the link is omitted rather than a
    page that cannot work.
    """
    login_url = _login_url()
    callback = callback_uri(request)

    try:
        endpoint = resolve_auth_service_endpoint()
        passkey_url: str | None = (
            f"{endpoint.base_url}{PASSKEY_LOGIN_PATH}"
            f"?{PASSKEY_APP_REDIRECT_PARAMETER}={quote(callback, safe='')}"
        )
    except ServiceDiscoveryError:
        # Without a resolvable auth service there is nothing allowlistable to hand off to, so the
        # affordance is omitted rather than pointed at a URL that cannot work.
        passkey_url = None

    return TeacherSurfaceLinks(
        sign_in_url=(
            f"{login_url}?{RETURN_URI_PARAMETER}={quote(callback, safe='')}" if login_url else None
        ),
        passkey_url=passkey_url,
        logout_url=LOGOUT_PATH,
    )


@dataclass(frozen=True)
class TeacherSession:
    """One request's portal-session outcome (D19): the session id and the D18 identity as data.

    ``session_id`` is the value the gate presents as ``X-Portal-Session`` on every
    assignments-api call — so a view never sees a header, and the ward route can never acquire
    one. ``data`` is the claim set the auth service returned, rendered as data and never as a
    credential (AC11). On rule 2 (no cookie at all) both are absent and the routes behave exactly
    as they did before this round.
    """

    session_id: str | None = None
    data: SessionData | None = None

    @property
    def headers(self) -> dict[str, str]:
        """The per-call headers the teacher routes pass to the assignments client."""
        return {SESSION_HEADER_NAME: self.session_id} if self.session_id else {}


class TeacherSessionOutcome(Exception):
    """The gate's non-session answer: a page (or a redirect) instead of the route body.

    Raised from :func:`require_teacher_session` so D19's rule ladder lives in exactly one place,
    and re-rendered unchanged by the handler below. It is a page-level outcome — a card at HTTP
    200, a 302 with the configured sign-in target — never a 500 and never a traceback.
    """

    def __init__(self, response: Response) -> None:
        self.response = response
        super().__init__("the teacher-session gate answered instead of the route")


@app.exception_handler(TeacherSessionOutcome)
async def on_teacher_session_outcome(
    request: Request, outcome: TeacherSessionOutcome
) -> Response:
    """Answer with what the gate built — the response is already the whole page/redirect."""
    return outcome.response


def _session_card(
    request: Request,
    *,
    code: str,
    detail: str | None = None,
    endpoint_label: str | None = None,
    clear_cookie: bool,
) -> HTMLResponse:
    """The AC10 card for a dead or unusable session — cleared only when the session is dead.

    ``clear_cookie`` is the D18 distinction this round turns on: ``session_ended`` /
    ``session_not_found`` mean the cookie can only produce a broken page (clear it), while a
    degraded read means the session's state is simply **unknown** (keep it — discarding a live
    session because the auth service blipped would sign a teacher out for no reason).
    """
    if clear_cookie:
        logger.warning("teacher session is dead; clearing the cookie (%s)", code)
    else:
        logger.warning("teacher session unavailable (state leaves the cookie in place) (%s)", code)

    response = HTMLResponse(
        build_teacher_session_card(
            code=code,
            detail=detail,
            endpoint_label=endpoint_label,
            links=resolve_teacher_links(request),
            # The kept cookie means the state is unknown: the card must not claim the browser is
            # signed out while the session may still be live (the badge would contradict the
            # "state unknown" copy above it).
            signed_out=clear_cookie,
        ).html()
    )
    if clear_cookie:
        _clear_session_cookie(response)
    return response


async def require_teacher_session(
    request: Request,
    client: AuthApiClient | None = Depends(get_auth_client),
) -> TeacherSession:
    """D19's rule ladder, in one place — the gate every ``/teacher*`` read route depends on.

    **Rule 1 — a session cookie is present.** The session path: the auth service is asked for the
    D18 claim set, and the route renders from it while presenting ``X-Portal-Session`` on every
    assignments call. ``session_ended`` (410) and ``session_not_found`` (404) are dead sessions:
    the cookie is cleared and this surface's AC10 card is rendered at HTTP 200 — never a redirect
    loop, which is what a gate that redirects on a dead cookie would produce. A degraded read
    (the auth service unreachable) renders the same card **without** clearing the cookie, because
    the session's state is unknown. ``resolve_dev_teacher_id`` is never invoked on this path: the
    session's own ``teacherId`` is the only identity, so a dev-env gap cannot degrade a valid
    session.

    **Rule 2 — no session cookie.** Today's route semantics, byte for byte: the dev id runs
    exactly where it already ran, ``MissingConfigurationError`` still refuses, and no header is
    sent (the property above is empty). The dev id is never this gate's trigger.

    **Rule 3 — the redirect is target-keyed, not dev-id-keyed.** Scoped to the routes that depend
    on this gate (the ward route is untouched), and acting only when there is no cookie **and**
    ``PORTALS_LOGIN_URL`` is configured: 302 to the auth portal's login page with this portal's
    callback as ``return_uri``. With no configured target (the dev posture) the gate does not act
    and the request continues under rule 2 — today's honest render, never an invented URL.

    A live-but-non-teacher session is deliberately not this portal's problem: the API is the
    security boundary, so its refusal renders as the degraded card and no role list is duplicated
    into Python.
    """
    session_id = _session_cookie(request)
    links = resolve_teacher_links(request)

    if session_id is None:
        # Rule 3 first, then rule 2: the redirect fires only when a sign-in target was configured
        # (``PORTALS_LOGIN_URL``), so an unconfigured dev posture renders today's views.
        if links.sign_in_url:
            raise TeacherSessionOutcome(_signed_out_response(links.sign_in_url))
        return TeacherSession()

    if client is None:
        # The session path needs an auth service; the cookie is kept (its state is unknown, and
        # only the auth service could say otherwise).
        raise TeacherSessionOutcome(
            _session_card(
                request, code="auth_service_unconfigured", clear_cookie=False
            )
        )

    try:
        data = await client.read_session(session_id)
    except (SessionEndedError, SessionNotFoundError) as error:
        raise TeacherSessionOutcome(
            _session_card(
                request,
                code=session_card_code(error),
                detail=str(error),
                endpoint_label=client.endpoint.label,
                clear_cookie=True,
            )
        ) from error
    except PortalApiError as error:
        raise TeacherSessionOutcome(
            _session_card(
                request,
                code=session_card_code(error),
                detail=str(error),
                endpoint_label=client.endpoint.label,
                clear_cookie=False,
            )
        ) from error

    logger.info("teacher session: identity read from the auth service")
    return TeacherSession(session_id=session_id, data=data)


def resolve_dev_teacher_id() -> str:
    """The configured dev-bypass teacher id, or raise when the portal cannot use one (D5).

    Refused rather than defaulted: a constant (or the all-zero GUID) would be a teacher
    the dev database does not have, and the queue would answer ``[]`` — a failed config
    wearing a successful result. The value is the same dev teacher row id the API's own
    ``TestAuth:TeacherId`` binds; under real auth the API prefers the principal's claim,
    so this parameter is inert there.
    """
    raw = (os.environ.get(DEV_TEACHER_ID_KEY) or "").strip()
    if not raw:
        raise MissingConfigurationError(
            DEV_TEACHER_ID_KEY,
            "no dev teacher id was injected, so the review queue cannot be read under the "
            "dev bypass; set it to the dev teacher row id the API's TestAuth:TeacherId uses",
        )

    try:
        parsed = uuid.UUID(raw)
    except ValueError as error:
        raise MissingConfigurationError(
            DEV_TEACHER_ID_KEY, f"the injected value is not a GUID: {error}"
        ) from error

    if parsed.int == 0:
        raise MissingConfigurationError(
            DEV_TEACHER_ID_KEY,
            "the injected value is the all-zero GUID, which identifies no teacher",
        )

    return raw


@app.get("/", response_class=HTMLResponse)
async def ward_view(
    request: Request,
    client: AssignmentsApiClient = Depends(get_assignments_client),
) -> HTMLResponse:
    """Render the ward view from a live assignments-api call."""
    state = _portal_state(request)
    try:
        result = await client.list_assignments()
    except PortalApiError as error:
        logger.error("ward view degraded: %s", error)
        state.last_fetch = {
            "error": str(error),
            "base_url": client.endpoint.base_url,
            "env_var": client.endpoint.env_var,
        }
        return HTMLResponse(build_error_view(error=error, endpoint=client.endpoint).html())

    logger.info(
        "ward view: %s row(s) from %s (HTTP %s)",
        result.row_count,
        client.endpoint.base_url,
        result.status_code,
    )
    state.last_fetch = {
        "status": result.status_code,
        "row_count": result.row_count,
        "base_url": client.endpoint.base_url,
        "env_var": client.endpoint.env_var,
    }
    return HTMLResponse(
        build_ward_view(
            rows=result.rows, endpoint=client.endpoint, status_code=result.status_code
        ).html()
    )


def _teacher_degraded(
    request: Request,
    *,
    client: AssignmentsApiClient,
    error: PortalApiError,
    surface: str,
) -> HTMLResponse:
    """One teacher surface's degraded state: logged, recorded, and rendered as a page."""
    state = _portal_state(request)
    logger.error("teacher %s degraded: %s", surface, error)
    state.last_fetch = {
        "error": str(error),
        "base_url": client.endpoint.base_url,
        "env_var": client.endpoint.env_var,
    }
    return HTMLResponse(
        build_teacher_error_view(
            error=error, endpoint=client.endpoint, surface=surface
        ).html()
    )


@app.get("/teacher", response_class=HTMLResponse)
async def teacher_assignment_list(
    request: Request,
    client: AssignmentsApiClient = Depends(get_assignments_client),
    session: TeacherSession = Depends(require_teacher_session),
) -> HTMLResponse:
    """Render the teacher assignment list from a live assignments-api call."""
    state = _portal_state(request)
    try:
        result = await client.list_assignments(headers=session.headers)
    except PortalApiError as error:
        return _teacher_degraded(
            request, client=client, error=error, surface="assignment list"
        )

    logger.info(
        "teacher assignment list: %s row(s) from %s (HTTP %s)",
        result.row_count,
        client.endpoint.base_url,
        result.status_code,
    )
    state.last_fetch = {
        "status": result.status_code,
        "row_count": result.row_count,
        "base_url": client.endpoint.base_url,
        "env_var": client.endpoint.env_var,
    }
    return HTMLResponse(
        build_teacher_list_view(
            rows=result.rows,
            endpoint=client.endpoint,
            status_code=result.status_code,
            session=session.data,
            links=resolve_teacher_links(request),
        ).html()
    )


@app.get("/teacher/assignments/{assignment_id}", response_class=HTMLResponse)
async def teacher_review_queue(
    request: Request,
    assignment_id: str,
    client: AssignmentsApiClient = Depends(get_assignments_client),
    session: TeacherSession = Depends(require_teacher_session),
) -> HTMLResponse:
    """Render one assignment's review queue from a live assignments-api call.

    Under rule 1 (a session) the queue read presents ``X-Portal-Session`` and the dev id is never
    consulted; under rule 2 the ``teacherId`` parameter is today's dev fallback, with the refusal
    intact.
    """
    state = _portal_state(request)
    try:
        teacher_id = session.data.teacher_id if session.data else resolve_dev_teacher_id()
        result = await client.list_review_queue(
            assignment_id, teacher_id, headers=session.headers
        )
    except PortalApiError as error:
        return _teacher_degraded(request, client=client, error=error, surface="review queue")

    logger.info(
        "teacher review queue: %s row(s) for assignment %s from %s (HTTP %s)",
        result.row_count,
        assignment_id,
        client.endpoint.base_url,
        result.status_code,
    )
    state.last_fetch = {
        "status": result.status_code,
        "row_count": result.row_count,
        "base_url": client.endpoint.base_url,
        "env_var": client.endpoint.env_var,
    }
    return HTMLResponse(
        build_review_queue_view(
            assignment_id=assignment_id,
            rows=result.rows,
            endpoint=client.endpoint,
            status_code=result.status_code,
            session=session.data,
            links=resolve_teacher_links(request),
        ).html()
    )


@app.get(
    "/teacher/assignments/{assignment_id}/students/{student_id}",
    response_class=HTMLResponse,
)
async def teacher_submission(
    request: Request,
    assignment_id: str,
    student_id: str,
    client: AssignmentsApiClient = Depends(get_assignments_client),
    session: TeacherSession = Depends(require_teacher_session),
) -> HTMLResponse:
    """Render one submission (version history + review) from a live assignments-api call."""
    state = _portal_state(request)
    try:
        result = await client.get_submission(
            assignment_id, student_id, headers=session.headers
        )
    except PortalApiError as error:
        return _teacher_degraded(
            request, client=client, error=error, surface="submission detail"
        )

    logger.info(
        "teacher submission: assignment %s student %s from %s (HTTP %s)",
        assignment_id,
        student_id,
        client.endpoint.base_url,
        result.status_code,
    )
    state.last_fetch = {
        "status": result.status_code,
        "row_count": len(result.submission.versions),
        "base_url": client.endpoint.base_url,
        "env_var": client.endpoint.env_var,
    }
    return HTMLResponse(
        build_submission_view(
            assignment_id=assignment_id,
            student_id=student_id,
            submission=result.submission,
            endpoint=client.endpoint,
            status_code=result.status_code,
            session=session.data,
            links=resolve_teacher_links(request),
        ).html()
    )


@app.get(AUTH_CALLBACK_PATH, response_class=HTMLResponse)
async def auth_callback(
    request: Request,
    code: str | None = None,
    client: AuthApiClient | None = Depends(get_auth_client),
) -> Response:
    """This portal's own session bootstrap (D19): redeem the handoff code for its cookie.

    Reachable by a **top-level browser navigation** from either sign-in path — the password path
    (the auth portal's ``POST /login`` mints the D6 code bound to this callback and 302s the
    browser here with it) and the passkey path (the auth service sees a non-portal-origin sign-in,
    mints the D6 continuation for this callback, and the auth portal's ``/bootstrap`` re-validates
    it against its own allowlist before 302ing here). Both land on this one redemption — the
    passkey path needs no route of its own.

    The ``redirectUri`` sent is this portal's callback URI in the auth service's own normalized
    spelling — the URI the code was bound to. A refusal (replayed/expired code, mismatched URI)
    or a degraded call renders a card at HTTP 200 and sets **no** cookie: nothing was redeemed, and
    a half-signed-in browser must not look signed in. The success response carries no token
    (AC11) and redirects to the teacher surface, where the session's identity is read back.
    """
    links = resolve_teacher_links(request)
    if not code:
        return HTMLResponse(
            build_teacher_session_card(code="handshake_code_missing", links=links).html()
        )

    if client is None:
        return HTMLResponse(
            build_teacher_session_card(code="auth_service_unconfigured", links=links).html()
        )

    try:
        session_id = await client.redeem_handshake_code(code, callback_uri(request))
    except PortalApiError as error:
        logger.warning("session bootstrap failed: %s", error)
        return HTMLResponse(
            build_teacher_session_card(
                code=session_card_code(error),
                detail=str(error),
                endpoint_label=client.endpoint.label,
                links=links,
            ).html()
        )

    logger.info("session bootstrap: portal session established")
    response = RedirectResponse("/teacher", status_code=302)
    _set_session_cookie(response, session_id)
    return response


@app.get(LOGOUT_PATH)
async def teacher_logout(
    request: Request,
    client: AuthApiClient | None = Depends(get_auth_client),
) -> Response:
    """D13: revoke the session server-side, clear the cookie, go to the opaque end-session URL.

    The auth service revokes the refresh token itself and answers with the fully-built
    ``end_session`` URL (``id_token_hint`` + ``post_logout_redirect_uri``), because this portal
    holds neither the id token nor the ability to sign one (D12/AC11). That URL is **opaque**: it
    is 302'd byte-for-byte into ``Location`` and is never parsed, re-encoded, logged or persisted
    (re-encoding would break Keycloak's exact match and turn a working logout into an
    unregistered-target rejection).

    Every outcome clears this portal's cookie — a logout that leaves one behind is the one thing
    this route must never do. A failed revocation still clears it and says so: the local sign-in
    is over, and the session's fate must not be hidden.
    """
    session_id = _session_cookie(request)
    if session_id is None:
        # Nothing to revoke: the browser is signed out already.
        return _signed_out_response("/")

    if client is None:
        return _logout_card(request, code="auth_service_unconfigured")

    try:
        end_session_url = await client.revoke_session(session_id)
    except SessionNotFoundError as error:
        # The auth service already forgot this session: locally signed out, nothing to redirect to.
        logger.warning("teacher logout: the session was already gone: %s", error)
        return _signed_out_response("/")
    except PortalApiError as error:
        logger.warning("teacher logout failed: %s", error)
        return _logout_card(
            request,
            code=session_card_code(error),
            detail=str(error),
            endpoint_label=client.endpoint.label,
        )

    logger.info("teacher logout: session revoked; redirecting to the opaque end-session URL")
    return _signed_out_response(end_session_url)


def _logout_card(
    request: Request,
    *,
    code: str,
    detail: str | None = None,
    endpoint_label: str | None = None,
) -> HTMLResponse:
    """The card a failed logout renders, with the cookie cleared: the local sign-out stands."""
    response = HTMLResponse(
        build_teacher_session_card(
            code=code,
            detail=detail,
            endpoint_label=endpoint_label,
            links=resolve_teacher_links(request),
        ).html()
    )
    _clear_session_cookie(response)
    return response


def _teacher_surface_for(path: str) -> str | None:
    """The teacher surface a pre-route failure belongs to; ``None`` for the ward page.

    A service-discovery failure is raised while resolving the dependency, so it never
    reaches a route body — this keeps the page it renders honest about which surface
    was asked for, instead of showing the ward page for a teacher URL.
    """
    if path.rstrip("/") == "/teacher":
        return "assignment list"
    if path.startswith("/teacher/assignments/"):
        return "submission detail" if "/students/" in path else "review queue"
    return None


@app.exception_handler(ServiceDiscoveryError)
async def on_discovery_error(request: Request, error: ServiceDiscoveryError) -> HTMLResponse:
    """A missing service endpoint is a page-level degraded state, never a 500."""
    state = _portal_state(request)
    state.discovery_error = str(error)
    state.last_fetch = {"error": str(error)}
    surface = _teacher_surface_for(request.url.path)
    if surface is not None:
        logger.error("teacher %s degraded: %s", surface, error)
        return HTMLResponse(
            build_teacher_error_view(
                error=error, endpoint=state.endpoint, surface=surface
            ).html()
        )

    logger.error("ward view degraded: %s", error)
    return HTMLResponse(build_error_view(error=error, endpoint=state.endpoint).html())


@app.get("/health")
def health(request: Request) -> dict[str, Any]:
    """Spike diagnostics: the resolved endpoint and the last fetch outcome."""
    state = _portal_state(request)
    return {
        "status": "ok" if state.endpoint else "degraded",
        "api_base_url": state.endpoint.base_url if state.endpoint else None,
        "api_env_var": state.endpoint.env_var if state.endpoint else None,
        "last_fetch": state.last_fetch,
        "discovery_error": state.discovery_error,
    }
