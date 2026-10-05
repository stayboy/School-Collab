"""Portal — the teacher surface over the existing assignments-api.

Pure HTTP consumer of the existing assignments-api: no backend changes, no
direct database access. Hosted solely by the Aspire AppHost through
``AddUvicornApp``.

This module is deliberately thin — it owns only the FastAPI app, the
service-call dependency, and the routes:

* service calls live in ``api/`` (typed client + discovery + DTOs + errors)
* Prefab component trees live in ``views/``

The HTTP client is async (``httpx.AsyncClient``) and lifespan-owned, so request
handlers never block the event loop.

See ``documents/solution/portals-service-client-pattern.md`` for the pattern.

Routes:
    GET /       -> a 302 redirect to the teacher assignment list, which is the
                   only surface this portal serves.
    GET /teacher -> the teacher assignment list, the entry point of the
                   read-only teacher drill-down.
    GET /teacher/assignments/{assignmentId} -> that assignment's review queue.
                   The dev bypass's teacher id comes from the portal's own
                   environment (``PORTAL_DEV_TEACHER_ID``, D5) — never a default.
    GET /teacher/assignments/{assignmentId}/students/{studentId} -> the
                   submission detail (version history + review + the grade form).
    POST /teacher/assignments/{assignmentId}/students/{studentId}/review ->
                   the grade form's OWN route: the JSON-only and one-time-antiforgery
                   guards, then the D19 gate, then the existing assignments-api grade
                   POST. It answers {"redirect_uri": …} at HTTP 200 (the fetch's own
                   shape); every refusal is a bounded code, and the assignments API is
                   never called without a live session and an unspent form token.
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
routes behave exactly as before this round. The grade POST depends on the same
gate, and translates its page-shaped outcome into the fetch's shape, because a
``Fetch`` action cannot consume a card and must never be handed a 302.
Every surface renders its degraded state as a typed error page — a page, never
a raw 500 or a traceback.
"""

from __future__ import annotations

import json
import logging
import os
import secrets
import uuid
from collections.abc import AsyncIterator, Mapping
from contextlib import asynccontextmanager
from dataclasses import dataclass, field
from datetime import datetime, timedelta, timezone
from typing import Any
from urllib.parse import quote, urlencode

import httpx
from fastapi import Depends, FastAPI, Request
from fastapi.responses import HTMLResponse, JSONResponse, RedirectResponse, Response

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
    ANTIFORGERY_FIELD,
    ERROR_CODE_PARAMETER,
    GRADE_ANTIFORGERY_REJECTED_CODE,
    GRADE_COMMENTS_FIELD,
    GRADE_FORM_REJECTED_CODE,
    GRADE_GRADE_FIELD,
    GRADE_IDENTITY_MISSING_CODE,
    GRADE_SCORE_FIELD,
    GRADE_SCORE_INVALID_CODE,
    GRADE_SESSION_REQUIRED_CODE,
    GRADE_TEACHER_UNCONFIGURED_CODE,
    REVIEW_PATH,
    TeacherSurfaceLinks,
    build_review_queue_view,
    build_submission_view,
    build_teacher_error_view,
    build_teacher_list_view,
    build_teacher_session_card,
    grade_failure_code,
    session_card_code,
    submission_path,
)

logger = logging.getLogger("portals")

ASSIGNMENTS_API = "assignments-api"
HTTP_TIMEOUT_SECONDS = 10.0

#: How long an issued grade-form antiforgery token stays usable, and how many may live at once
#: (the auth portal's ``ANTIFORGERY_TTL_SECONDS`` / ``MAX_LIVE_ANTIFORGERY_TOKENS``, spec §14).
#: The cap keeps an anonymous drill-down render flood from growing the process unbounded.
ANTIFORGERY_TTL_SECONDS = 600
MAX_LIVE_ANTIFORGERY_TOKENS = 512

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


class AntiforgeryTokenStore:
    """The grade form's one-time tokens, held in this process's memory only (spec §14).

    Each drill-down render mints a random token and stores it here; ``POST …/review`` requires
    one and consumes it, so a body replayed from a page the teacher never loaded — or a second
    submit of the same page — cannot grade anything. Nothing is signed: the token is a random
    string this process happens to remember, which is why there is **no signing key in Python**
    (D12). Transplanted from the auth portal's store so the two portals' antiforgery mechanics
    are the same mechanism, not two spellings of one.

    Expiry and a live cap keep the store bounded: the issuing page is a session-bearing teacher
    page, and an unbounded dictionary is a memory DoS either way.
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


@dataclass
class PortalState:
    """Per-process portal state — no module-level mutable globals."""

    http: httpx.AsyncClient | None = None
    endpoint: ServiceEndpoint | None = None
    last_fetch: dict[str, Any] = field(default_factory=dict)
    discovery_error: str | None = None
    antiforgery: AntiforgeryTokenStore = field(default_factory=AntiforgeryTokenStore)


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
    assignments-api call — so a view never sees a header. ``data`` is the claim set the auth
    service returned, rendered as data and never as a credential (AC11). On rule 2 (no cookie at
    all) both are absent and the routes behave exactly as they did before this round.
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


class GradeFormRefused(Exception):
    """The grade POST's own transport refusal: a request this portal will not read.

    Deliberately distinct from the gate's outcome: nothing about a sign-in state is being
    described here (a forged or malformed request is simply turned away), so it carries one of
    the grade path's bounded codes and the route answers it in the fetch's own shape rather
    than with a page.
    """

    def __init__(self, code: str) -> None:
        self.code = code
        super().__init__(f"the grade form's request was refused ({code})")


def _grade_target(request: Request) -> tuple[str, str]:
    """The (assignment, student) a grade request is about, from the matched route's params."""
    return (
        str(request.path_params.get("assignment_id", "")),
        str(request.path_params.get("student_id", "")),
    )


def _grade_failure(request: Request, code: str) -> JSONResponse:
    """A refused grade in the fetch's own shape: HTTP 200, the drill-down URL, a bounded code.

    Mirrors the auth portal's ``_login_failure``: the browser is only ever sent back to this
    portal's own page, which renders the code's bounded copy — never to a URL carrying an
    upstream body, a Problem ``detail`` or an attacker-supplied string.
    """
    assignment_id, student_id = _grade_target(request)
    location = (
        f"{submission_path(assignment_id, student_id)}"
        f"?{urlencode({ERROR_CODE_PARAMETER: code})}"
    )
    return JSONResponse({"redirect_uri": location})


def _is_grade_review_post(request: Request) -> bool:
    """Whether this request is the grade form's own POST, matched on the route's PATTERN.

    The matched route's template — never a raw path: the assignment and student ids are
    request-specific, so a raw-path comparison would silently miss the parameterized route.
    """
    route = request.scope.get("route")
    return request.method == "POST" and getattr(route, "path", None) == REVIEW_PATH


def _grade_gate_answer(request: Request, outcome: TeacherSessionOutcome) -> Response:
    """The D19 gate's outcome, in the shape the grade form's fetch can actually consume.

    The gate's rule-3 answer is a 302 to the configured sign-in target: the fetch is told to
    navigate there (``Fetch`` follows redirects, so handing it the 302 itself would make the
    browser fetch the sign-in page cross-origin, dropping cookies). Its other answers are cards
    for a dead, degraded or unconfigured session: the fetch is sent to the **drill-down**, whose
    GET re-gates and renders the truth — the POST never touches the session cookie, because the
    GET is the one place the cookie lifecycle runs.
    """
    location = outcome.response.headers.get("location")
    if location:
        return JSONResponse({"redirect_uri": location})
    return _grade_failure(request, GRADE_SESSION_REQUIRED_CODE)


@app.exception_handler(GradeFormRefused)
async def on_grade_form_refused(request: Request, refusal: GradeFormRefused) -> Response:
    """A grade form refusal the route never read: bounded code, fetch shape, no upstream call."""
    return _grade_failure(request, refusal.code)


@app.exception_handler(TeacherSessionOutcome)
async def on_teacher_session_outcome(
    request: Request, outcome: TeacherSessionOutcome
) -> Response:
    """Answer with what the gate built — the response is already the whole page/redirect.

    One exception: the grade POST, which is matched on its route's parameterized pattern (see
    :func:`_is_grade_review_post`) and answered in the fetch's shape instead.
    """
    if _is_grade_review_post(request):
        return _grade_gate_answer(request, outcome)
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
    on this gate, and acting only when there is no cookie **and**
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


async def require_grade_form(request: Request) -> Mapping[str, Any]:
    """The grade POST's two transport guards, resolved **before** the D19 gate.

    A FastAPI dependency — not the route body — so the guards run before
    :func:`require_teacher_session` is reached (dependencies resolve in declaration order): a
    body this portal will not read is refused without spending the gate's auth-service session
    read, and the assignments API is never called either way. The order is the whole point
    (spec §14):

    * **JSON only.** A cross-site page cannot POST ``application/json`` without a CORS
      preflight, which this portal never answers — so the media type is the first guard, ahead
      of any parsing.
    * **A one-time antiforgery token**, minted with the drill-down render and consumed here.
      It is required and single-use, and there is no signing key anywhere in Python (D12).

    A refusal is a :class:`GradeFormRefused` — a bounded code, never an upstream body — and the
    guards are deliberately not the gate's outcome type: nothing about a sign-in state is being
    described by a malformed request.
    """
    if not _is_json_request(request):
        logger.warning("grade POST refused: content type was not application/json")
        raise GradeFormRefused(GRADE_FORM_REJECTED_CODE)

    payload = await _json_object(request)
    if payload is None:
        logger.warning("grade POST refused: the body was not a JSON object")
        raise GradeFormRefused(GRADE_FORM_REJECTED_CODE)

    if not _portal_state(request).antiforgery.consume(_text_field(payload, ANTIFORGERY_FIELD)):
        logger.warning("grade POST refused: missing or already-used antiforgery token")
        raise GradeFormRefused(GRADE_ANTIFORGERY_REJECTED_CODE)

    return payload


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


def _text_field(payload: Mapping[str, Any], name: str) -> str | None:
    """One string field of a JSON request body (``None`` when absent or not a string)."""
    value = payload.get(name)
    return value.strip() if isinstance(value, str) and value.strip() else None


def _grade_score(payload: Mapping[str, Any]) -> float | None:
    """The submitted score as a JSON number, or ``None`` when it was left blank.

    The form's number input carries text, while the Assignments API binds the request's
    ``decimal? Score`` from a JSON number — so a numeric string becomes a number here. A blank
    or absent value is ``None`` (the API records a review with no outcome), and anything that is
    not a number is refused with a bounded code rather than silently dropped.
    """
    raw = payload.get(GRADE_SCORE_FIELD)
    if raw is None:
        return None
    if isinstance(raw, bool):
        raise GradeFormRefused(GRADE_SCORE_INVALID_CODE)
    if isinstance(raw, (int, float)):
        return float(raw)
    text = raw.strip() if isinstance(raw, str) else ""
    if not text:
        return None
    try:
        return float(text)
    except ValueError as error:
        raise GradeFormRefused(GRADE_SCORE_INVALID_CODE) from error


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


@app.get("/", include_in_schema=False)
async def root() -> RedirectResponse:
    """The portal's entry point: the teacher surface is now the only one it serves."""
    return RedirectResponse(url="/teacher", status_code=302)


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
    error_code: str | None = None,
    client: AssignmentsApiClient = Depends(get_assignments_client),
    session: TeacherSession = Depends(require_teacher_session),
) -> HTMLResponse:
    """Render one submission (version history + review + the grade form) from a live API read.

    Each render mints the grade form's one-time antiforgery token (spec §14), and
    ``error_code`` carries back a bounded failure the grade POST refused with — this GET is the
    one place a refused grade's outcome becomes a page.
    """
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
            antiforgery_token=state.antiforgery.issue(),
            error_code=error_code,
        ).html()
    )


@app.post(REVIEW_PATH)
async def teacher_grade_submission(
    request: Request,
    assignment_id: str,
    student_id: str,
    form: Mapping[str, Any] = Depends(require_grade_form),
    client: AssignmentsApiClient = Depends(get_assignments_client),
    session: TeacherSession = Depends(require_teacher_session),
) -> Response:
    """The grade form's own route: transport guards, then the D19 gate, then the grade POST.

    The form's ``Fetch.post`` targets this route — never the Assignments API directly and never
    the auth service (mirroring the auth portal's ``POST /login``). Every answer is the fetch's
    shape: HTTP 200 ``{"redirect_uri": …}``. Success names the drill-down, whose GET re-reads
    the submission (now ``Graded``); a refusal names that same page with a bounded ``error_code``
    the GET renders as an alert.

    The opaque session id travels in ``X-Portal-Session`` (D19's rule 1) and nothing else does:
    no token, secret or refresh reaches Python (AC9/AC11). The body's ``teacherId`` is the D18
    claim set's data under real auth — where the API's claim-wins rule overrides it anyway —
    and the dev fallback under the dev bypass, exactly as the review-queue read behaves.
    """
    state = _portal_state(request)

    if session.data is not None:
        # Rule 1: the session's own claim data is the identity, and a session that carries no
        # usable teacher id has nothing to attribute a grade to — refused before any call.
        teacher_id = session.data.teacher_id
        if not teacher_id:
            logger.warning("grade POST refused: the session carried no teacher identity")
            return _grade_failure(request, GRADE_IDENTITY_MISSING_CODE)
    else:
        # Rule 2 (today's dev posture): the same dev fallback the review queue reads, with its
        # refusal intact, and no session header — ``session.headers`` is empty on this path.
        try:
            teacher_id = resolve_dev_teacher_id()
        except MissingConfigurationError as error:
            logger.warning("grade POST refused: %s", error)
            return _grade_failure(request, GRADE_TEACHER_UNCONFIGURED_CODE)

    try:
        status_code = await client.review_submission(
            assignment_id,
            student_id,
            teacher_id=teacher_id,
            score=_grade_score(form),
            grade=_text_field(form, GRADE_GRADE_FIELD),
            comments=_text_field(form, GRADE_COMMENTS_FIELD),
            headers=session.headers,
        )
    except PortalApiError as error:
        logger.warning("grade POST failed: %s", error)
        state.last_fetch = {
            "error": str(error),
            "base_url": client.endpoint.base_url,
            "env_var": client.endpoint.env_var,
        }
        return _grade_failure(request, grade_failure_code(error))

    logger.info(
        "grade POST: submission for assignment %s student %s recorded (HTTP %s)",
        assignment_id,
        student_id,
        status_code,
    )
    state.last_fetch = {
        "status": status_code,
        "base_url": client.endpoint.base_url,
        "env_var": client.endpoint.env_var,
    }
    return JSONResponse({"redirect_uri": submission_path(assignment_id, student_id)})


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
    """The teacher surface a pre-route failure belongs to; ``None`` when the path is not one.

    A service-discovery failure is raised while resolving the dependency, so it never
    reaches a route body — this keeps the page it renders honest about which surface
    was asked for, instead of showing one surface's degraded page for another's URL.
    """
    if path.rstrip("/") == "/teacher":
        return "assignment list"
    if path.startswith("/teacher/assignments/"):
        return "submission detail" if "/students/" in path else "review queue"
    return None


@app.exception_handler(ServiceDiscoveryError)
async def on_discovery_error(request: Request, error: ServiceDiscoveryError) -> Response:
    """A missing service endpoint is a page-level degraded state, never a 500.

    The grade POST is the one exception: its caller is a ``Fetch`` action, which cannot consume
    an HTML page any more than it can consume the gate's card — so it gets the same bounded-code
    answer, and the drill-down it lands on is where an HTML page belongs.
    """
    state = _portal_state(request)
    state.discovery_error = str(error)
    state.last_fetch = {"error": str(error)}

    if _is_grade_review_post(request):
        logger.error("grade POST degraded: %s", error)
        return _grade_failure(request, grade_failure_code(error))

    surface = _teacher_surface_for(request.url.path)
    if surface is not None:
        logger.error("teacher %s degraded: %s", surface, error)
        return HTMLResponse(
            build_teacher_error_view(
                error=error, endpoint=state.endpoint, surface=surface
            ).html()
        )

    logger.error("portal degraded: %s", error)
    return HTMLResponse(
        build_teacher_error_view(error=error, endpoint=state.endpoint, surface="portal").html()
    )


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
