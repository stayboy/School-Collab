"""The ward surface's Prefab component trees (plan: MVP 1).

Two states, both pure functions of data:

* :func:`build_ward_view` — the live table, rendered from rows the client parsed.
* :func:`build_error_view` — the degraded state, rendered from a typed portal
  error. The surface never shows a raw 500 or a traceback.

Component variants here are limited to the two the spike proved
(``default`` / ``success``); colour polish is a later MVP concern.
"""

from __future__ import annotations

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

from api import AssignmentRow, PortalApiError, ServiceEndpoint

_TABLE_COLUMNS: tuple[tuple[str, str, bool], ...] = (
    ("id", "Assignment id", False),
    ("title", "Title", True),
    ("status", "Status", True),
    ("dueDate", "Due", False),
    ("createdAt", "Created", False),
)


def build_ward_view(
    *, rows: list[AssignmentRow], endpoint: ServiceEndpoint, status_code: int
) -> PrefabApp:
    """The ward portal page, rendered from a live assignments-api response."""
    with PrefabApp(title="Ward portal — prefab spike", css_class="p-6") as application:
        with Column(gap=4):
            H3("Ward portal — Phase-0 prefab spike")
            Muted(f"Live data from {endpoint.label}.")
            with Row(gap=2):
                Badge(f"HTTP {status_code}", variant="success")
                Badge(f"{len(rows)} assignments", variant="default")
            with Card():
                with CardContent():
                    DataTable(
                        columns=[
                            DataTableColumn(key=key, header=header, sortable=sortable)
                            for key, header, sortable in _TABLE_COLUMNS
                        ],
                        rows=[row.as_table_row() for row in rows],
                        search=True,
                    )
    return application


def build_error_view(*, error: PortalApiError, endpoint: ServiceEndpoint | None) -> PrefabApp:
    """The degraded ward page: what went wrong, and where the portal looked."""
    with PrefabApp(title="Ward portal — prefab spike (degraded)", css_class="p-6") as application:
        with Column(gap=4):
            H3("Ward portal — Phase-0 prefab spike")
            with Row(gap=2):
                Badge("assignments-api unavailable", variant="default")
                Badge(type(error).__name__, variant="default")
            with Card():
                with CardContent():
                    with Column(gap=2):
                        Muted(str(error))
                        Muted(
                            "Resolved endpoint: "
                            + (endpoint.label if endpoint else "not resolved (service discovery failed)")
                        )
    return application
