"""Route-level tests for the teacher surface, wired to a stubbed transport (D6).

FastAPI's ``TestClient`` (no uvicorn) plus ``app.dependency_overrides`` — the same
container-free shape as ``test_app_routes.py``, covering route -> client -> Prefab
render for all three teacher routes and every degraded case (transport failure, a
non-2xx from the gated reads, a body that is not JSON) with no server, no Docker and
no browser (round-2 grill Q3: pytest + ``httpx.MockTransport``; Playwright deferred).

D5 gets its own cases: the review-queue route needs a **configured** dev teacher id,
so the unset, malformed and all-zero values must render an honest page and must not
put a request on the wire at all.
"""

from __future__ import annotations

from collections.abc import Callable, Iterator
from contextlib import contextmanager
from typing import Any

import httpx
import pytest
from fastapi.testclient import TestClient

import app as portal_app
from api import AssignmentsApiClient, ServiceEndpoint

Handler = Callable[[httpx.Request], httpx.Response]

STUB_ENDPOINT = ServiceEndpoint(
    service="assignments-api", base_url="http://assignments-api.test", env_var="test"
)

ASSIGNMENT_ID = "11111111-1111-1111-1111-111111111111"
STUDENT_ID = "22222222-2222-2222-2222-222222222222"
SUBMISSION_ID = "44444444-4444-4444-4444-444444444444"
DEV_TEACHER_ID = "33333333-3333-3333-3333-333333333333"
QUEUE_PATH = f"/teacher/assignments/{ASSIGNMENT_ID}"
SUBMISSION_PATH = f"{QUEUE_PATH}/students/{STUDENT_ID}"

# ReviewStateDto has no JsonStringEnumConverter on the Assignments API, so it arrives
# as an int; SubmissionSourceDto likewise. The DTO maps both to their display names.
QUEUE_PAYLOAD: list[dict[str, Any]] = [
    {
        "submissionId": SUBMISSION_ID,
        "assignmentId": ASSIGNMENT_ID,
        "assignmentTitle": "Reading response",
        "studentId": STUDENT_ID,
        "currentVersionNumber": 2,
        "reviewState": 0,
        "lastSubmittedAt": "2026-10-02T09:00:00+00:00",
    }
]

DETAIL_PAYLOAD: dict[str, Any] = {
    "submissionId": SUBMISSION_ID,
    "assignmentId": ASSIGNMENT_ID,
    "studentId": STUDENT_ID,
    "currentVersionNumber": 2,
    "reviewState": 1,
    "lastSubmittedAt": "2026-10-02T09:00:00+00:00",
    "signOffState": "Signed",
    "versions": [
        {
            "id": "v2",
            "versionNumber": 2,
            "source": 0,
            "content": "My second answer",
            "submittedAt": "2026-10-02T09:00:00+00:00",
            "score": 7.5,
            "passed": True,
        },
        {
            "id": "v1",
            "versionNumber": 1,
            "source": 1,
            "content": "My first answer",
            "submittedAt": "2026-10-01T09:00:00+00:00",
        },
    ],
    "review": {
        "id": "r1",
        "submissionId": SUBMISSION_ID,
        "teacherId": DEV_TEACHER_ID,
        "score": 8,
        "grade": "B",
        "comments": "Good work",
        "createdAt": "2026-10-02T10:00:00+00:00",
    },
}


def _client_override(handler: Handler):
    """A dependency override serving every request from ``handler``.

    Declared ``async`` so FastAPI builds the client inside the running event loop, and
    deliberately left unclosed — ``httpx.MockTransport`` holds no resources.
    """

    async def override() -> AssignmentsApiClient:
        return AssignmentsApiClient(
            httpx.AsyncClient(transport=httpx.MockTransport(handler)), STUB_ENDPOINT
        )

    return override


@contextmanager
def _portal(handler: Handler) -> Iterator[TestClient]:
    """A ``TestClient`` whose assignments client is stubbed with ``handler``."""
    portal_app.app.dependency_overrides[portal_app.get_assignments_client] = _client_override(
        handler
    )
    try:
        with TestClient(portal_app.app) as client:
            yield client
    finally:
        portal_app.app.dependency_overrides.clear()


def _json(payload: Any, status: int = 200) -> Handler:
    return lambda request: httpx.Response(status, json=payload)


def _status_only(status: int) -> Handler:
    """A gated read's rejection (404 out of scope / 403 missing teacher principal)."""
    return lambda request: httpx.Response(status, json={"detail": "rejected"})


def _unreachable() -> Handler:
    def handler(request: httpx.Request) -> httpx.Response:
        raise httpx.ConnectError("connection refused", request=request)

    return handler


def _html_login_page() -> Handler:
    """A real-auth OIDC challenge can answer 200 with an HTML login page."""
    return lambda request: httpx.Response(
        200, text="<html>login</html>", headers={"content-type": "text/html"}
    )


def _recording_json(payload: Any) -> tuple[Handler, list[str]]:
    """A JSON handler plus the URLs it saw, so a test can pin the request it made."""
    seen: list[str] = []

    def handler(request: httpx.Request) -> httpx.Response:
        seen.append(str(request.url))
        return httpx.Response(200, json=payload)

    return handler, seen


def _dev_teacher_id(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv(portal_app.DEV_TEACHER_ID_KEY, DEV_TEACHER_ID)


def _no_dev_teacher_id(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.delenv(portal_app.DEV_TEACHER_ID_KEY, raising=False)


# ── GET /teacher — the assignment list ─────────────────────────────────────────────


def test_teacher_list_renders_rows_through_the_injected_client() -> None:
    with _portal(
        _json(
            [
                {
                    "id": ASSIGNMENT_ID,
                    "title": "Reading response",
                    "status": "Published",
                    "dueDate": "2026-10-01",
                    "createdAt": "2026-09-21",
                }
            ]
        )
    ) as client:
        response = client.get("/teacher")

    assert response.status_code == 200
    assert "Reading response" in response.text
    assert "1 assignments" in response.text


def test_teacher_list_renders_the_empty_state() -> None:
    with _portal(_json([])) as client:
        response = client.get("/teacher")

    assert response.status_code == 200
    assert "0 assignments" in response.text
    assert "An empty result is a success, not a failure" in response.text


def test_teacher_list_degrades_when_the_api_is_unreachable() -> None:
    with _portal(_unreachable()) as client:
        response = client.get("/teacher")

    assert response.status_code == 200  # a page, never a traceback
    assert "assignment list degraded" in response.text
    assert "ApiUnavailableError" in response.text


# ── GET /teacher/assignments/{id} — the review queue ───────────────────────────────


def test_review_queue_passes_the_configured_dev_teacher_id(monkeypatch: pytest.MonkeyPatch) -> None:
    _dev_teacher_id(monkeypatch)
    handler, seen = _recording_json(QUEUE_PAYLOAD)

    with _portal(handler) as client:
        response = client.get(QUEUE_PATH)

    assert response.status_code == 200
    assert seen == [
        f"http://assignments-api.test/{ASSIGNMENT_ID}/submissions/review-queue"
        f"?teacherId={DEV_TEACHER_ID}"
    ]
    assert "Reading response" in response.text
    assert "Pending" in response.text  # ReviewStateDto 0, mapped from the API's int
    assert "1 submissions" in response.text


def test_review_queue_renders_the_empty_state(monkeypatch: pytest.MonkeyPatch) -> None:
    _dev_teacher_id(monkeypatch)

    with _portal(_json([])) as client:
        response = client.get(QUEUE_PATH)

    assert response.status_code == 200
    assert "0 submissions" in response.text
    assert "No submissions are waiting for review" in response.text


def test_review_queue_tolerates_a_row_missing_its_optional_fields(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Shape drift degrades a cell, not the page (the tolerant-DTO contract)."""
    _dev_teacher_id(monkeypatch)

    with _portal(_json([{"submissionId": SUBMISSION_ID}, "not-an-object"])) as client:
        response = client.get(QUEUE_PATH)

    assert response.status_code == 200
    assert SUBMISSION_ID in response.text
    assert "1 submissions" in response.text


def test_review_queue_degrades_when_the_dev_teacher_id_is_unset(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """D5: unset cannot mean "ask for a placeholder" — and must not mean an empty queue."""
    _no_dev_teacher_id(monkeypatch)
    handler, seen = _recording_json(QUEUE_PAYLOAD)

    with _portal(handler) as client:
        response = client.get(QUEUE_PATH)

    assert response.status_code == 200
    assert "review queue degraded" in response.text
    assert "MissingConfigurationError" in response.text
    assert portal_app.DEV_TEACHER_ID_KEY in response.text
    assert "no dev teacher id was injected" in response.text
    assert seen == []  # the queue read is never attempted without a configured teacher
    assert "0 submissions" not in response.text


@pytest.mark.parametrize(
    "configured",
    ["not-a-guid", "00000000-0000-0000-0000-000000000000", "   "],
    ids=["malformed", "all-zero-guid", "blank"],
)
def test_review_queue_refuses_an_unusable_dev_teacher_id(
    monkeypatch: pytest.MonkeyPatch, configured: str
) -> None:
    """A zero GUID identifies no teacher; a blank/malformed value is not a teacher id."""
    monkeypatch.setenv(portal_app.DEV_TEACHER_ID_KEY, configured)
    handler, seen = _recording_json(QUEUE_PAYLOAD)

    with _portal(handler) as client:
        response = client.get(QUEUE_PATH)

    assert response.status_code == 200
    assert "MissingConfigurationError" in response.text
    assert seen == []


def test_review_queue_degrades_on_a_404_from_the_visibility_gate(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    _dev_teacher_id(monkeypatch)

    with _portal(_status_only(404)) as client:
        response = client.get(QUEUE_PATH)

    assert response.status_code == 200
    assert "review queue degraded" in response.text
    assert "ApiResponseError" in response.text
    assert "HTTP 404" in response.text


def test_review_queue_degrades_on_a_403_from_the_teacher_gate(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """403 is what the queue route answers for a missing teacher principal."""
    _dev_teacher_id(monkeypatch)

    with _portal(_status_only(403)) as client:
        response = client.get(QUEUE_PATH)

    assert response.status_code == 200
    assert "review queue degraded" in response.text
    assert "HTTP 403" in response.text


def test_review_queue_degrades_when_the_api_is_unreachable(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    _dev_teacher_id(monkeypatch)

    with _portal(_unreachable()) as client:
        response = client.get(QUEUE_PATH)

    assert response.status_code == 200
    assert "review queue degraded" in response.text
    assert "ApiUnavailableError" in response.text


# ── GET /teacher/assignments/{id}/students/{studentId} — the submission detail ─────


def test_submission_renders_versions_and_review_through_the_injected_client() -> None:
    handler, seen = _recording_json(DETAIL_PAYLOAD)

    with _portal(handler) as client:
        response = client.get(SUBMISSION_PATH)

    assert response.status_code == 200
    assert seen == [
        f"http://assignments-api.test/{ASSIGNMENT_ID}/students/{STUDENT_ID}/submission"
    ]
    assert "My second answer" in response.text  # the submission's answer, per version
    assert "Guardian" in response.text  # SubmissionSourceDto 1, mapped from the API's int
    assert "Good work" in response.text
    assert "Reviewed" in response.text  # ReviewStateDto 1
    assert "Sign-off: Signed" in response.text


def test_submission_renders_an_unreviewed_submission() -> None:
    payload = {**DETAIL_PAYLOAD, "versions": [], "review": None}

    with _portal(_json(payload)) as client:
        response = client.get(SUBMISSION_PATH)

    assert response.status_code == 200
    assert "This submission has not been reviewed yet" in response.text
    assert "No versions recorded for this submission" in response.text


def test_submission_degrades_on_a_404() -> None:
    with _portal(_status_only(404)) as client:
        response = client.get(SUBMISSION_PATH)

    assert response.status_code == 200
    assert "submission detail degraded" in response.text
    assert "HTTP 404" in response.text


def test_submission_rejects_an_html_body_as_data() -> None:
    """A 2xx HTML login page is not a submission (ar-24's fail-open class)."""
    with _portal(_html_login_page()) as client:
        response = client.get(SUBMISSION_PATH)

    assert response.status_code == 200
    assert "submission detail degraded" in response.text
    assert "ApiResponseError" in response.text
    assert "text/html" in response.text


# ── a pre-route failure names the teacher surface, not the ward one ───────────────


@pytest.mark.parametrize(
    ("path", "surface"),
    [
        ("/teacher", "assignment list"),
        (QUEUE_PATH, "review queue"),
        (SUBMISSION_PATH, "submission detail"),
    ],
    ids=["list", "queue", "submission"],
)
def test_missing_service_discovery_names_the_requested_surface(
    monkeypatch: pytest.MonkeyPatch, path: str, surface: str
) -> None:
    """Discovery fails while resolving the dependency, before any route body runs."""
    monkeypatch.delenv("services__assignments-api__http__0", raising=False)
    monkeypatch.delenv("ASSIGNMENTS_API_HTTP", raising=False)

    with TestClient(portal_app.app) as client:
        response = client.get(path)

    assert response.status_code == 200
    assert f"{surface} degraded" in response.text
    assert "ServiceDiscoveryError" in response.text
    assert "Ward portal" not in response.text
