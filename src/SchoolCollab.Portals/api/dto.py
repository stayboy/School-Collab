"""Tolerant row DTOs for the Assignments API.

The API is the authority on shape, so these dataclasses accept missing or
renamed fields instead of exploding: a shape drift should degrade one cell, not
the whole page (the dev database legitimately answers ``[]`` — see the ar-21/23
data-visibility note). Views consume attributes, never raw dictionaries.
"""

from __future__ import annotations

from collections.abc import Mapping
from dataclasses import dataclass
from typing import Any


def _text(value: Any) -> str | None:
    """Coerce an API value to display text, tolerating nulls and non-strings."""
    if value is None:
        return None
    return value if isinstance(value, str) else str(value)


@dataclass(frozen=True)
class AssignmentRow:
    """One row of ``GET /assignments`` — what the ward view tabulates."""

    id: str | None = None
    title: str | None = None
    status: str | None = None
    due_date: str | None = None
    created_at: str | None = None

    @classmethod
    def from_payload(cls, payload: Mapping[str, Any]) -> AssignmentRow:
        """Build a row from the API's JSON object, tolerating alternate names."""
        return cls(
            id=_text(payload.get("id")),
            title=_text(payload.get("title")),
            status=_text(payload.get("status") or payload.get("statusName")),
            due_date=_text(payload.get("dueDate")),
            created_at=_text(payload.get("createdAt")),
        )

    def as_table_row(self) -> dict[str, Any]:
        """The column-keyed mapping the Prefab ``DataTable`` addresses."""
        return {
            "id": self.id,
            "title": self.title,
            "status": self.status,
            "dueDate": self.due_date,
            "createdAt": self.created_at,
        }
