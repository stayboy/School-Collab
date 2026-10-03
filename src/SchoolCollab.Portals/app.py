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
    GET /health -> spike diagnostics: the resolved API base URL, the env var
                   that supplied it, and the last fetch result.

Every surface renders its degraded state as a typed error page — a page, never
a raw 500 or a traceback.
"""

from __future__ import annotations

import logging
import os
import uuid
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from dataclasses import dataclass, field
from typing import Any

import httpx
from fastapi import Depends, FastAPI, Request
from fastapi.responses import HTMLResponse

from api import (
    AssignmentsApiClient,
    MissingConfigurationError,
    PortalApiError,
    ServiceDiscoveryError,
    ServiceEndpoint,
    resolve_service_endpoint,
)
from views.teacher import (
    build_review_queue_view,
    build_submission_view,
    build_teacher_error_view,
    build_teacher_list_view,
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
) -> HTMLResponse:
    """Render the teacher assignment list from a live assignments-api call."""
    state = _portal_state(request)
    try:
        result = await client.list_assignments()
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
            rows=result.rows, endpoint=client.endpoint, status_code=result.status_code
        ).html()
    )


@app.get("/teacher/assignments/{assignment_id}", response_class=HTMLResponse)
async def teacher_review_queue(
    request: Request,
    assignment_id: str,
    client: AssignmentsApiClient = Depends(get_assignments_client),
) -> HTMLResponse:
    """Render one assignment's review queue from a live assignments-api call."""
    state = _portal_state(request)
    try:
        teacher_id = resolve_dev_teacher_id()
        result = await client.list_review_queue(assignment_id, teacher_id)
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
) -> HTMLResponse:
    """Render one submission (version history + review) from a live assignments-api call."""
    state = _portal_state(request)
    try:
        result = await client.get_submission(assignment_id, student_id)
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
        ).html()
    )


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
