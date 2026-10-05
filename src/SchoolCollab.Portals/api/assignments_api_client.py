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

Adding an endpoint is one method. A read returns a parsed result::

    async def list_ward_assignments(self, student_id: str) -> FetchResult:
        status, payload = await self._get_json(f"/{student_id}/assignments")
        ...

and a write returns the status it was answered with::

    async def review_submission(self, ...) -> int:
        return await self._post_json(f"/{assignment_id}/students/{student_id}/submission/review", body)
"""

from __future__ import annotations

from collections.abc import Mapping
from dataclasses import dataclass
from typing import Any

import httpx

# The shared AC11 token-shape scan (``api/errors.py``, beside the ``TokenInResponseError`` it
# raises): every decoded body the portal ingests is scanned with ONE implementation of the token key
# set, never a second copy that could drift away from it — and never by importing another client's
# internals (round ``portal-submission-grade`` review P2-1).
from api.dto import AssignmentRow, SubmissionDetailRow, SubmissionForReviewRow
from api.errors import (
    ApiResponseError,
    ApiUnavailableError,
    TokenInResponseError,
    token_shaped_keys,
)
from api.service_discovery import ServiceEndpoint

JSON_CONTENT_TYPE = "application/json"

#: The two media types whose bodies this client decodes on a write: a success document
#: and the Assignments API's bounded problem document (a refusal).
PROBLEM_JSON_CONTENT_TYPE = "application/problem+json"


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

    async def review_submission(
        self,
        assignment_id: str,
        student_id: str,
        *,
        teacher_id: str,
        score: float | None,
        grade: str | None,
        comments: str | None,
        headers: Mapping[str, str] | None = None,
    ) -> int:
        """``POST /{id}/students/{studentId}/submission/review`` — record a teacher's grade.

        The body is the Assignments API's **camelCase** binding spelling
        (``teacherId``/``score``/``grade``/``comments``; ASP.NET's web defaults).
        ``teacher_id`` is the dev-bypass fallback the API reads
        (``currentUser.TeacherId ?? (isRealAuth ? throw : command.TeacherId)``), so it is
        inert under real auth — where the principal's claim wins — and the only teacher
        input under the dev bypass. On the session path ``headers`` carries the opaque
        session id (D19's rule 1), and ``teacher_id`` is the session's claim data, never a
        credential (AC9/AC11).

        Returns the API's 2xx status (``204 No Content`` for a recorded grade): the write
        has no response body to model, so there is no new result DTO to re-export.
        """
        return await self._post_json(
            f"/{assignment_id}/students/{student_id}/submission/review",
            {
                "teacherId": teacher_id,
                "score": score,
                "grade": grade,
                "comments": comments,
            },
            headers=headers,
        )

    async def _post_json(
        self,
        path: str,
        body: dict[str, Any],
        headers: Mapping[str, str] | None = None,
    ) -> int:
        """POST ``path`` with ``body`` and return its 2xx status, or raise a typed error.

        Fail-closed in ``_get_json``'s reason order, with one deliberate difference: any
        body is **decoded before the status is judged**, never cut short by
        ``raise_for_status()``. The Assignments API answers a refusal with
        ``application/problem+json``, and decoding first is what keeps that refusal
        classified here rather than turned into a raw transport exception — while the
        ``detail`` text itself is never carried into the error, so only the portal's own
        bounded code can reach a page. A 204 carries no body at all, so the grade's own
        success path is a status alone. Every decoded body is scanned for token-shaped
        fields at any depth before anything trusts it (AC11).
        """
        try:
            response = await self._http.post(
                f"{self._endpoint.base_url}{path}", json=body, headers=headers
            )
        except httpx.HTTPError as error:  # ConnectError, TimeoutException, ...
            raise ApiUnavailableError(
                self._endpoint.service, self._endpoint.base_url, error
            ) from error

        if response.content:
            decoded = self._decode(response, path)
            leaked = token_shaped_keys(decoded)
            if leaked:
                raise TokenInResponseError(
                    self._endpoint.service, self._endpoint.base_url, leaked
                )

        if response.status_code >= 400:
            raise ApiResponseError(
                self._endpoint.service,
                self._endpoint.base_url,
                f"HTTP {response.status_code} for {path}",
            )

        return response.status_code

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

    def _decode(self, response: httpx.Response, path: str) -> Any:
        """A POST body as JSON — a success document or a bounded refusal document.

        Only these two media types are read: the Assignments API answers a success with
        ``application/json`` and a refusal with ``application/problem+json``. Anything else
        — an HTML challenge page, a proxy error page — is not a document this client will
        ingest, and a body it will not read is a typed refusal, never data (the
        ``_get_json`` guard's reason, kept stricter there where a refusal document must
        not be mistaken for a row).
        """
        content_type = response.headers.get("content-type", "").lower()
        if not content_type.startswith((JSON_CONTENT_TYPE, PROBLEM_JSON_CONTENT_TYPE)):
            raise ApiResponseError(
                self._endpoint.service,
                self._endpoint.base_url,
                f"expected {JSON_CONTENT_TYPE}, got '{content_type or 'no content-type'}'",
            )

        try:
            return response.json()
        except ValueError as error:
            raise ApiResponseError(
                self._endpoint.service,
                self._endpoint.base_url,
                f"body for {path} was not valid JSON: {error}",
            ) from error
