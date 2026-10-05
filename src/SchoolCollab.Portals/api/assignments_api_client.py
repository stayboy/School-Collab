"""Typed HTTP client for the Assignments API.

The Python counterpart of ``FamiliesApiClient`` / ``AssignmentsApiClient`` in
the .NET hosts: one class per bounded context, one method per endpoint, DTOs in
and out, transport failures translated into typed portal errors.

The client is deliberately **base-URL agnostic** — it is handed a resolved
``ServiceEndpoint`` rather than reading the environment itself — so it can be
driven by any transport, including ``httpx.MockTransport``: that is what makes
the portal testable with no server and no Docker.

The client is **async** (``httpx.AsyncClient``): it is created once in the
FastAPI lifespan and awaited from async route handlers, so request handling
never blocks the event loop on I/O.

The public teacher-route reads take an optional per-call ``headers`` mapping.
That is how the D19 portal session travels: the app layer presents the opaque
session id (``X-Portal-Session``) on teacher-route calls only, while the ward
route stays header-less — the header is passed per call, never defaulted onto
the client, so no surface can acquire it by accident.

Adding an endpoint is one method, e.g. the ward plan's next endpoints::

    async def list_ward_assignments(self, student_id: str) -> FetchResult:
        status, payload = await self._get_json(f"/{student_id}/assignments")
        ...
"""

from __future__ import annotations

from collections.abc import Mapping
from dataclasses import dataclass
from typing import Any

import httpx

from api.dto import AssignmentRow, SubmissionDetailRow, SubmissionForReviewRow
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


@dataclass(frozen=True)
class ReviewQueueResult:
    """A successful review-queue read: the HTTP status plus the parsed queue rows."""

    status_code: int
    rows: list[SubmissionForReviewRow]

    @property
    def row_count(self) -> int:
        return len(self.rows)


@dataclass(frozen=True)
class SubmissionResult:
    """A successful submission read: the HTTP status plus the parsed detail."""

    status_code: int
    submission: SubmissionDetailRow


class AssignmentsApiClient:
    """The portal's view of the Assignments API — the ward list and the teacher drill-down."""

    def __init__(self, http: httpx.AsyncClient, endpoint: ServiceEndpoint) -> None:
        self._http = http
        self._endpoint = endpoint

    @property
    def endpoint(self) -> ServiceEndpoint:
        """The resolved endpoint, for diagnostics (``/health``, error cards)."""
        return self._endpoint

    async def list_assignments(
        self, headers: Mapping[str, str] | None = None
    ) -> FetchResult:
        """``GET /assignments`` — the assignments the ward view tabulates.

        ``headers`` is the per-call portal-session header on the teacher routes; the ward
        route omits it, so the API's own posture for it is unchanged.
        """
        status_code, payload = await self._get_json("/assignments", headers=headers)

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

    async def list_review_queue(
        self,
        assignment_id: str,
        teacher_id: str,
        headers: Mapping[str, str] | None = None,
    ) -> ReviewQueueResult:
        """``GET /{id}/submissions/review-queue?teacherId=`` — one assignment's review queue.

        ``teacher_id`` is the dev-bypass fallback the API reads
        (``currentUser.TeacherId ?? (isRealAuth ? throw : teacherId)``), so it is inert
        under real auth — where the claim wins — and the only teacher input under the
        dev bypass, which is why the route refuses to call this without a configured
        value rather than sending a placeholder. ``headers`` carries the portal session
        on the session path (D19's rule 1).
        """
        status_code, payload = await self._get_json(
            f"/{assignment_id}/submissions/review-queue",
            params={"teacherId": teacher_id},
            headers=headers,
        )

        # The API answers with a bare array; tolerate an envelope just in case.
        if isinstance(payload, dict):
            payload = payload.get("items") or payload.get("submissions") or []
        if not isinstance(payload, list):
            raise ApiResponseError(
                self._endpoint.service,
                self._endpoint.base_url,
                f"expected a JSON array (or {{items: [...]}}), got {type(payload).__name__}",
            )

        rows = [
            SubmissionForReviewRow.from_payload(item) for item in payload if isinstance(item, dict)
        ]
        return ReviewQueueResult(status_code=status_code, rows=rows)

    async def get_submission(
        self,
        assignment_id: str,
        student_id: str,
        headers: Mapping[str, str] | None = None,
    ) -> SubmissionResult:
        """``GET /{id}/students/{studentId}/submission`` — one submission with its review."""
        status_code, payload = await self._get_json(
            f"/{assignment_id}/students/{student_id}/submission", headers=headers
        )
        if not isinstance(payload, Mapping):
            raise ApiResponseError(
                self._endpoint.service,
                self._endpoint.base_url,
                f"expected a JSON object, got {type(payload).__name__}",
            )

        return SubmissionResult(
            status_code=status_code, submission=SubmissionDetailRow.from_payload(payload)
        )

    async def _get_json(
        self,
        path: str,
        params: Mapping[str, str] | None = None,
        headers: Mapping[str, str] | None = None,
    ) -> tuple[int, Any]:
        """GET ``path`` and return ``(status, parsed_json)`` or raise a typed error."""
        try:
            response = await self._http.get(
                f"{self._endpoint.base_url}{path}", params=params, headers=headers
            )
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
