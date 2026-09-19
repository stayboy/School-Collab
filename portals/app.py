"""Ward portal — Phase-0 prefab-UI spike (ar-23).

Pure HTTP consumer of the existing assignments-api: no backend changes, no
direct database access. Hosted solely by the Aspire AppHost through
`AddUvicornApp` (see documents/specs/teachers-ward-portal-prefab-plan.md, Q5).

Routes:
    GET /       -> the ward view, rendered by Prefab UI from a LIVE
                   assignments-api call made at request time.
    GET /health -> spike diagnostics: the resolved API base URL, the env var
                   that supplied it, and the last fetch result.
"""

from __future__ import annotations

import logging
import os
from typing import Any

import httpx
from fastapi import FastAPI
from fastapi.responses import HTMLResponse
from prefab_ui.app import PrefabApp
from prefab_ui.components import (
    Badge,
    Card,
    CardContent,
    Column,
    DataTable,
    DataTableColumn,
    H3,
    Muted,
    Row,
)

logger = logging.getLogger("portals")

# Aspire service-discovery env vars, checked in priority order. The .NET-style
# key is what `WithReference` injects for every runtime; the simplified form is
# Python-flavoured. Which one actually arrives is itself a spike finding.
DISCOVERY_ENV_VARS = (
    "services__assignments-api__http__0",
    "ASSIGNMENTS_API_HTTP",
)

app = FastAPI(title="School-Collab ward portal (prefab spike)")

_last_fetch: dict[str, Any] = {}


def assignments_api_base_url() -> tuple[str, str]:
    """Resolve the assignments-api base URL and the env var that supplied it."""
    for name in DISCOVERY_ENV_VARS:
        value = os.environ.get(name)
        if value:
            return value.rstrip("/"), name

    discovery_shaped = {
        key: value
        for key, value in os.environ.items()
        if "ASSIGN" in key.upper() or key.lower().startswith("services")
    }
    raise RuntimeError(
        "No assignments-api base URL found. Tried: "
        + ", ".join(DISCOVERY_ENV_VARS)
        + f". Discovery-shaped env vars actually present: {discovery_shaped}"
    )


def fetch_assignments() -> tuple[int, list[dict[str, Any]], str, str]:
    """GET /assignments on the assignments-api (anonymous: the AppHost dev bypass)."""
    base_url, env_var = assignments_api_base_url()
    with httpx.Client(timeout=10.0) as client:
        response = client.get(f"{base_url}/assignments")
    response.raise_for_status()
    payload = response.json()
    rows = payload if isinstance(payload, list) else payload.get("items", [])
    return response.status_code, rows, base_url, env_var


def build_view(
    rows: list[dict[str, Any]], status: int, base_url: str, env_var: str
) -> PrefabApp:
    with PrefabApp(title="Ward portal — prefab spike", css_class="p-6") as application:
        with Column(gap=4):
            H3("Ward portal — Phase-0 prefab spike")
            Muted(f"Live data from assignments-api at {base_url} (via {env_var}).")
            with Row(gap=2):
                Badge(f"HTTP {status}", variant="success")
                Badge(f"{len(rows)} assignments", variant="default")
            with Card():
                with CardContent():
                    DataTable(
                        columns=[
                            DataTableColumn(key="id", header="Assignment id"),
                            DataTableColumn(key="title", header="Title", sortable=True),
                            DataTableColumn(key="status", header="Status", sortable=True),
                            DataTableColumn(key="dueDate", header="Due"),
                            DataTableColumn(key="createdAt", header="Created"),
                        ],
                        rows=rows,
                        search=True,
                    )
    return application


@app.get("/", response_class=HTMLResponse)
def ward_view() -> HTMLResponse:
    status, rows, base_url, env_var = fetch_assignments()
    _last_fetch.update(
        status=status, row_count=len(rows), base_url=base_url, env_var=env_var
    )
    logger.info(
        "ward view: fetched %s assignment(s) from %s (HTTP %s)",
        len(rows),
        base_url,
        status,
    )
    return HTMLResponse(build_view(rows, status, base_url, env_var).html())


@app.get("/health")
def health() -> dict[str, Any]:
    base_url, env_var = assignments_api_base_url()
    return {
        "status": "ok",
        "api_base_url": base_url,
        "api_env_var": env_var,
        "last_fetch": _last_fetch,
    }
