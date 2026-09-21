"""Typed HTTP client for the Assignments API.

The Python counterpart of ``FamiliesApiClient`` / ``AssignmentsApiClient`` in
the .NET hosts: one class per bounded context, one method per endpoint, DTOs in
and out, transport failures translated into typed portal errors.

The client is deliberately **base-URL agnostic** — it is handed a resolved
``ServiceEndpoint`` rather than reading the environment itself — so it can be
driven by any transport, including ``httpx.MockTransport``: that is what makes
the portal testable with no server and no Docker.

Adding an endpoint is one method, e.g. the plan's MVP-1 ward endpoints::

    def list_ward_assignments(self, student_id: str) -> FetchResult:
        status, payload = self._get_json(f"/{student_id}/assignments")
        ...
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any

import httpx

from api.dto import AssignmentRow
from api.errors import ApiResponseError, ApiUnavailableError
from api.service_discovery import ServiceEndpoint

JSON_CONTENT_TYPE = "application/json"


@dataclass(frozen=True)
class FetchResult:
    """A successful call: the HTTP status plus the parsed rows."""

    status_code: int
    rows: list[AssignmentRow]

    @property
    def row_count(self) -> int:
        return len(self.rows)


class AssignmentsApiClient:
    """The ward portal's view of the Assignments API."""

    def __init__(self, http: httpx.Client, endpoint: ServiceEndpoint) -> None:
        self._http = http
        self._endpoint = endpoint

    @property
    def endpoint(self) -> ServiceEndpoint:
        """The resolved endpoint, for diagnostics (``/health``, error cards)."""
        return self._endpoint

    def list_assignments(self) -> FetchResult:
        """``GET /assignments`` — the assignments the ward view tabulates."""
        status_code, payload = self._get_json("/assignments")

        # The API answers with a bare array; tolerate an envelope just in case.
        if isinstance(payload, dict):
            payload = payload.get("items") or payload.get("assignments") or []
        if not isinstance(payload, list):
            raise ApiResponseError(
                self._endpoint.service,
                self._endpoint.base_url,
                f"expected a JSON array (or {{items: [...]}}), got {type(payload).__name__}",
            )

        rows = [AssignmentRow.from_payload(item) for item in payload if isinstance(item, dict)]
        return FetchResult(status_code=status_code, rows=rows)

    def _get_json(self, path: str) -> tuple[int, Any]:
        """GET ``path`` and return ``(status, parsed_json)`` or raise a typed error."""
        try:
            response = self._http.get(f"{self._endpoint.base_url}{path}")
            response.raise_for_status()
        except httpx.HTTPStatusError as error:
            raise ApiResponseError(
                self._endpoint.service,
                self._endpoint.base_url,
                f"HTTP {error.response.status_code} for {path}",
            ) from error
        except httpx.HTTPError as error:  # ConnectError, TimeoutException, ...
            raise ApiUnavailableError(
                self._endpoint.service, self._endpoint.base_url, error
            ) from error

        content_type = response.headers.get("content-type", "")
        if not content_type.lower().startswith(JSON_CONTENT_TYPE):
            # A real-auth OIDC challenge can answer 2xx with an HTML login page —
            # the same fail-open class the .NET TeacherDirectoryHttpClient closed
            # with AllowAutoRedirect=false. Never treat that as data.
            raise ApiResponseError(
                self._endpoint.service,
                self._endpoint.base_url,
                f"expected {JSON_CONTENT_TYPE}, got '{content_type or 'no content-type'}'",
            )

        try:
            return response.status_code, response.json()
        except ValueError as error:
            raise ApiResponseError(
                self._endpoint.service, self._endpoint.base_url, f"body was not valid JSON: {error}"
            ) from error
