"""Ward portal — Phase-0 prefab-UI spike (ar-23).

Pure HTTP consumer of the existing assignments-api: no backend changes, no
direct database access. Hosted solely by the Aspire AppHost through
``AddUvicornApp`` (see documents/specs/teachers-ward-portal-prefab-plan.md, Q5).

This module is deliberately thin — it owns only the FastAPI app, the
service-call dependency, and the routes:

* service calls live in ``api/`` (typed client + discovery + DTOs + errors)
* Prefab component trees live in ``views/``

See ``documents/solution/portals-service-client-pattern.md`` for the pattern.

Routes:
    GET /       -> the ward view, rendered by Prefab UI from a LIVE
                   assignments-api call made at request time.
    GET /health -> spike diagnostics: the resolved API base URL, the env var
                   that supplied it, and the last fetch result.
"""

from __future__ import annotations

import logging
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from dataclasses import dataclass, field
from typing import Any

import httpx
from fastapi import Depends, FastAPI, Request
from fastapi.responses import HTMLResponse

from api import (
    AssignmentsApiClient,
    PortalApiError,
    ServiceDiscoveryError,
    ServiceEndpoint,
    resolve_service_endpoint,
)
from views.ward import build_error_view, build_ward_view

logger = logging.getLogger("portals")

ASSIGNMENTS_API = "assignments-api"
HTTP_TIMEOUT_SECONDS = 10.0


@dataclass
class PortalState:
    """Per-process portal state — no module-level mutable globals."""

    http: httpx.Client | None = None
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
    the ward route renders an error card — far more useful in a dev loop than an
    app that refuses to start.
    """
    state = PortalState()
    state.endpoint, state.discovery_error = _try_resolve()
    if state.discovery_error:
        logger.error("service discovery failed at startup: %s", state.discovery_error)
    else:
        logger.info("portals wired to %s", state.endpoint.label)  # type: ignore[union-attr]

    state.http = httpx.Client(timeout=HTTP_TIMEOUT_SECONDS)
    application.state.portal = state
    try:
        yield
    finally:
        state.http.close()


app = FastAPI(title="School-Collab ward portal (prefab spike)", lifespan=lifespan)


def _portal_state(request: Request) -> PortalState:
    """The process state, created lazily if the lifespan never ran."""
    state: PortalState | None = getattr(request.app.state, "portal", None)
    if state is None:
        state = PortalState(http=httpx.Client(timeout=HTTP_TIMEOUT_SECONDS))
        request.app.state.portal = state
    return state


def get_assignments_client(request: Request) -> AssignmentsApiClient:
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


@app.get("/", response_class=HTMLResponse)
def ward_view(
    request: Request,
    client: AssignmentsApiClient = Depends(get_assignments_client),
) -> HTMLResponse:
    """Render the ward view from a live assignments-api call."""
    state = _portal_state(request)
    try:
        result = client.list_assignments()
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


@app.exception_handler(ServiceDiscoveryError)
async def on_discovery_error(request: Request, error: ServiceDiscoveryError) -> HTMLResponse:
    """A missing service endpoint is a page-level degraded state, never a 500."""
    state = _portal_state(request)
    state.discovery_error = str(error)
    state.last_fetch = {"error": str(error)}
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
