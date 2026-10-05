"""The teacher portal's write half — the grade form's route (round ``portal-submission-grade``).

FastAPI's ``TestClient`` plus ``app.dependency_overrides`` — the container-free shape
``test_teacher_session.py`` established, with both upstreams stubbed by ``httpx.MockTransport``.
No server, no Docker, no Keycloak, no browser.

The wire contract *is* the assertion: the ``X-Portal-Session`` header the grade POST presents on
the assignments-api call, the **camelCase** body it sends (``teacherId``/``score``/``grade``/
``comments``), the bounded code every refusal answers with, and — above all — that the
assignments-api grade POST is never fired without a live session and an unspent one-time form
token. Every stubbed request is recorded: a header or a body that is *claimed* to be threaded is
not a request on the wire.

Covers AC3 (happy path), AC4 (antiforgery + JSON-only refusal, the API never called), AC5 (the
D19 gate on the POST, in the fetch's shape), AC6 (bounded failure mapping, no upstream text in a
page) and AC8 (no credential in Python).
"""

from __future__ import annotations

import json
import os
import re
from collections.abc import Callable, Iterator
from contextlib import contextmanager
from typing import Any
from urllib.parse import quote

import httpx
import pytest
from fastapi.testclient import TestClient

import app as portal_app
from api import (
    AssignmentsApiClient,
    AuthApiClient,
    AuthServiceEndpoint,
    ServiceEndpoint,
)
from views.teacher import (
    GRADE_ANTIFORGERY_REJECTED_CODE,
    GRADE_FORM_REJECTED_CODE,
    GRADE_IDENTITY_MISSING_CODE,
    GRADE_REFUSED_CODE,
    GRADE_RESPONSE_REFUSED_CODE,
    GRADE_SCORE_INVALID_CODE,
    GRADE_SESSION_REQUIRED_CODE,
    GRADE_TEACHER_UNCONFIGURED_CODE,
    GRADE_UNAVAILABLE_CODE,
)

Handler = Callable[[httpx.Request], httpx.Response]

ASSIGNMENTS_ENDPOINT = ServiceEndpoint(
    service="assignments-api", base_url="http://assignments-api.test", env_var="test"
)
AUTH_ENDPOINT = AuthServiceEndpoint(service="auth", base_url="http://auth.test", env_var="test")

SESSION_COOKIE = portal_app.SESSION_COOKIE_NAME
SESSION_ID = "opaque-session-id-7"

ASSIGNMENT_ID = "11111111-1111-1111-1111-111111111111"
STUDENT_ID = "22222222-2222-2222-2222-222222222222"
SUBMISSION_ID = "44444444-4444-4444-4444-444444444444"
CONFIGURED_DEV_TEACHER_ID = "33333333-3333-3333-3333-333333333333"
SESSION_TEACHER_ID = "55555555-5555-5555-5555-555555555555"

QUEUE_PATH = f"/teacher/assignments/{ASSIGNMENT_ID}"
SUBMISSION_PATH = f"{QUEUE_PATH}/students/{STUDENT_ID}"
REVIEW_PATH = f"{SUBMISSION_PATH}/review"

PORTAL_BASE_URL = "http://portals.test"
CALLBACK_URI = f"{PORTAL_BASE_URL}/auth/callback"
SIGN_IN_URL = "http://auth-portal.test/login"

#: The one-time form token as it lands in the rendered drill-down (the auth portal's test pin).
_TOKEN_PATTERN = re.compile(r'"antiforgery_token":"([^"]+)"')

#: The D18 session read's body, camelCase as the auth service serializes its record.
SESSION_PAYLOAD: dict[str, Any] = {
    "sessionId": SESSION_ID,
    "tenantId": "00000000-0000-0000-0000-000000000002",
    "tenantName": "Dev School",
    "tenantType": "School",
    "teacherId": SESSION_TEACHER_ID,
    "roles": ["teacher"],
    "expiresInSeconds": 3600,
}

SUBMISSION_PAYLOAD: dict[str, Any] = {
    "submissionId": SUBMISSION_ID,
    "assignmentId": ASSIGNMENT_ID,
    "studentId": STUDENT_ID,
    "currentVersionNumber": 2,
    "reviewState": 1,
    "lastSubmittedAt": "2026-10-02T09:00:00+00:00",
    "signOffState": "AwaitingSignature",
    "versions": [],
    "review": None,
}


# ── stub transports ────────────────────────────────────────────────────────────────


def _assignments_override(handler: Handler):
    async def override() -> AssignmentsApiClient:
        return AssignmentsApiClient(
            httpx.AsyncClient(transport=httpx.MockTransport(handler)), ASSIGNMENTS_ENDPOINT
        )

    return override


def _auth_override(handler: Handler):
    async def override() -> AuthApiClient:
        return AuthApiClient(
            httpx.AsyncClient(transport=httpx.MockTransport(handler)), AUTH_ENDPOINT
        )

    return override


def _session_ok(request: httpx.Request) -> httpx.Response:
    return httpx.Response(200, json=SESSION_PAYLOAD)


def _unreachable() -> Handler:
    def handler(request: httpx.Request) -> httpx.Response:
        raise httpx.ConnectError("connection refused", request=request)

    return handler


def _auth_transport(
    seen: list[httpx.Request],
    *,
    sessions: list[Handler] | None = None,
) -> Handler:
    """A recording transport for the auth service's session read.

    ``sessions`` is a queue consumed one answer per read (default: a live session), so one test
    can render the drill-down under a live session and then grade under a dead one — the exact
    ordering the two routes' cookie handling depends on.
    """
    queue = list(sessions or [])

    def handler(request: httpx.Request) -> httpx.Response:
        seen.append(request)
        path = request.url.path
        if path.startswith("/auth/session/"):
            assert request.method == "GET", f"unexpected method for {path}: {request.method}"
            return (queue.pop(0) if queue else _session_ok)(request)
        raise AssertionError(f"the portal called an un-stubbed auth route: {request.method} {path}")

    return handler


def _assignments_transport(
    seen: list[httpx.Request],
    *,
    submission: Any = SUBMISSION_PAYLOAD,
    review: Handler | None = None,
) -> Handler:
    """A recording transport for the two assignments-api routes the grade flow touches."""

    def handler(request: httpx.Request) -> httpx.Response:
        seen.append(request)
        path = request.url.path
        if path.endswith("/submission") and request.method == "GET":
            return httpx.Response(200, json=submission)
        if path.endswith("/submission/review") and request.method == "POST":
            return review(request) if review is not None else httpx.Response(204)
        raise AssertionError(
            f"the portal called an un-stubbed assignments route: {request.method} {path}"
        )

    return handler


@contextmanager
def _portal(
    *, assignments: Handler | None = None, auth: Handler | None = None
) -> Iterator[TestClient]:
    """A ``TestClient`` with one or both upstreams stubbed by a ``MockTransport``."""
    if assignments is not None:
        portal_app.app.dependency_overrides[portal_app.get_assignments_client] = (
            _assignments_override(assignments)
        )
    if auth is not None:
        portal_app.app.dependency_overrides[portal_app.get_auth_client] = _auth_override(auth)
    try:
        with TestClient(portal_app.app) as client:
            yield client
    finally:
        portal_app.app.dependency_overrides.clear()


# ── helpers ───────────────────────────────────────────────────────────────────────


def _session_headers() -> dict[str, str]:
    """Present the portal's session cookie as a request header (never through the cookie jar)."""
    return {"cookie": f"{SESSION_COOKIE}={SESSION_ID}"}


def _no_sign_in_target(monkeypatch: pytest.MonkeyPatch) -> None:
    """Rule 2's posture: no ``PORTALS_LOGIN_URL``, so the gate does not redirect."""
    monkeypatch.delenv(portal_app.LOGIN_URL_KEY, raising=False)
    assert os.environ.get(portal_app.LOGIN_URL_KEY) is None


def _no_dev_teacher_id(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.delenv(portal_app.DEV_TEACHER_ID_KEY, raising=False)


def _drill_down_token(client: TestClient, *, headers: dict[str, str] | None = None) -> str:
    """Render the drill-down and read the one-time token out of it (each render mints one)."""
    match = _TOKEN_PATTERN.search(client.get(SUBMISSION_PATH, headers=headers).text)
    assert match is not None, "the drill-down must render the grade form's one-time token"
    return match.group(1)


def _minted_token(client: TestClient) -> str:
    """A one-time token taken straight from the process store.

    For the states where the drill-down GET cannot render one — rule 3's redirect to the sign-in
    page, a pre-route discovery failure — the guard's own ordering is what these tests are about,
    so the token is issued rather than scraped.
    """
    return portal_app.app.state.portal.antiforgery.issue()


def _form_body(
    token: str, *, score: Any = "8.5", grade: Any = "B", comments: Any = "Good work"
) -> dict[str, Any]:
    return {
        "score": score,
        "grade": grade,
        "comments": comments,
        "antiforgery_token": token,
    }


def _review_requests(seen: list[httpx.Request]) -> list[httpx.Request]:
    """The assignments-api grade POSTs the portal actually put on the wire."""
    return [request for request in seen if request.method == "POST"]


def _refusal(code: str) -> dict[str, str]:
    return {"redirect_uri": f"{SUBMISSION_PATH}?error_code={code}"}


def _set_cookie(response: httpx.Response) -> str:
    return response.headers.get("set-cookie", "")


# ── AC3: the happy path, asserted at the mock transport ───────────────────────────


def test_grade_happy_path_presents_the_session_and_posts_the_camel_case_body(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """AC3: the fetch POST presents ``X-Portal-Session`` and answers the drill-down at 200."""
    _no_sign_in_target(monkeypatch)
    assignments_seen: list[httpx.Request] = []
    auth_seen: list[httpx.Request] = []

    with _portal(
        assignments=_assignments_transport(assignments_seen),
        auth=_auth_transport(auth_seen),
    ) as client:
        token = _drill_down_token(client, headers=_session_headers())
        response = client.post(
            REVIEW_PATH, json=_form_body(token), headers=_session_headers()
        )

    assert response.status_code == 200
    assert response.headers["content-type"].startswith("application/json")
    assert response.json() == {"redirect_uri": SUBMISSION_PATH}

    review = _review_requests(assignments_seen)
    assert len(review) == 1
    assert str(review[0].url) == (
        f"http://assignments-api.test/{ASSIGNMENT_ID}/students/{STUDENT_ID}/submission/review"
    )
    # The header the write path lives or dies on: the portal session, on the wire.
    assert review[0].headers.get("X-Portal-Session") == SESSION_ID
    assert "authorization" not in review[0].headers

    # The body is exactly the API's camelCase binding spelling, and nothing else.
    wire = json.loads(review[0].content)
    assert wire == {
        "teacherId": SESSION_TEACHER_ID,
        "score": 8.5,
        "grade": "B",
        "comments": "Good work",
    }
    assert set(wire) == {"teacherId", "score", "grade", "comments"}

    # The identity came from the session's claim read, not the dev bypass: once for the
    # drill-down render, once for the grade POST.
    assert [request.url.path for request in auth_seen] == [
        f"/auth/session/{SESSION_ID}",
        f"/auth/session/{SESSION_ID}",
    ]


def test_a_blank_outcome_still_grades_as_a_review(monkeypatch: pytest.MonkeyPatch) -> None:
    """The API's own contract: a review with no score and no grade records ``Reviewed``."""
    _no_sign_in_target(monkeypatch)
    assignments_seen: list[httpx.Request] = []

    with _portal(
        assignments=_assignments_transport(assignments_seen),
        auth=_auth_transport([]),
    ) as client:
        token = _drill_down_token(client, headers=_session_headers())
        response = client.post(
            REVIEW_PATH,
            json=_form_body(token, score="", grade="", comments=""),
            headers=_session_headers(),
        )

    assert response.json() == {"redirect_uri": SUBMISSION_PATH}
    assert json.loads(_review_requests(assignments_seen)[0].content) == {
        "teacherId": SESSION_TEACHER_ID,
        "score": None,
        "grade": None,
        "comments": None,
    }


# ── AC4: the transport guards, before anything else ───────────────────────────────


def test_a_post_without_a_json_content_type_is_refused_and_no_upstream_is_called(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """AC4: the media type is the first guard — neither upstream is even reached."""
    _no_sign_in_target(monkeypatch)
    assignments_seen: list[httpx.Request] = []
    auth_seen: list[httpx.Request] = []

    with _portal(
        assignments=_assignments_transport(assignments_seen),
        auth=_auth_transport(auth_seen),
    ) as client:
        response = client.post(
            REVIEW_PATH,
            content=f"score=8.5&grade=B&antiforgery_token={SESSION_ID}",
            headers={**_session_headers(), "content-type": "application/x-www-form-urlencoded"},
        )

    assert response.status_code == 200
    assert response.headers["content-type"].startswith("application/json")
    assert response.json() == _refusal(GRADE_FORM_REJECTED_CODE)
    assert assignments_seen == []
    assert auth_seen == []  # the gate's session read is never spent on a body we will not read


def test_a_missing_token_is_refused_without_reading_the_session(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """AC4: the one-time token is required, and it is checked before the gate."""
    _no_sign_in_target(monkeypatch)
    assignments_seen: list[httpx.Request] = []
    auth_seen: list[httpx.Request] = []

    with _portal(
        assignments=_assignments_transport(assignments_seen),
        auth=_auth_transport(auth_seen),
    ) as client:
        response = client.post(
            REVIEW_PATH, json=_form_body("not-a-token"), headers=_session_headers()
        )

    assert response.json() == _refusal(GRADE_ANTIFORGERY_REJECTED_CODE)
    assert assignments_seen == []
    assert auth_seen == []


def test_a_spent_token_cannot_be_replayed(monkeypatch: pytest.MonkeyPatch) -> None:
    """AC4: single use by construction — and the replay costs no upstream call either."""
    _no_sign_in_target(monkeypatch)
    assignments_seen: list[httpx.Request] = []
    auth_seen: list[httpx.Request] = []

    with _portal(
        assignments=_assignments_transport(assignments_seen),
        auth=_auth_transport(auth_seen),
    ) as client:
        token = _drill_down_token(client, headers=_session_headers())
        body = _form_body(token)
        first = client.post(REVIEW_PATH, json=body, headers=_session_headers())
        reviews_after_first = len(_review_requests(assignments_seen))
        reads_after_first = len(auth_seen)
        replay = client.post(REVIEW_PATH, json=body, headers=_session_headers())

    assert first.json() == {"redirect_uri": SUBMISSION_PATH}
    assert replay.json() == _refusal(GRADE_ANTIFORGERY_REJECTED_CODE)
    assert len(_review_requests(assignments_seen)) == reviews_after_first
    assert len(auth_seen) == reads_after_first


# ── AC5: the gate, on the POST, in the fetch's shape ──────────────────────────────


@pytest.mark.parametrize("status", [410, 404], ids=["session-ended", "session-not-found"])
def test_a_dead_session_is_answered_in_the_fetch_shape_and_the_api_never_fires(
    monkeypatch: pytest.MonkeyPatch, status: int
) -> None:
    """AC5: the POST never clears the cookie — the drill-down GET re-gates and does."""
    _no_sign_in_target(monkeypatch)
    cardinality = "session_ended" if status == 410 else "session_not_found"
    assignments_seen: list[httpx.Request] = []
    auth_seen: list[httpx.Request] = []

    with _portal(
        assignments=_assignments_transport(assignments_seen),
        auth=_auth_transport(
            auth_seen,
            sessions=[
                _session_ok,  # the drill-down render: a live session, so a token exists
                lambda request: httpx.Response(status, json={"error": cardinality}),
                # The GET the grade POST answered with re-gates — same dead state.
                lambda request: httpx.Response(status, json={"error": cardinality}),
            ],
        ),
    ) as client:
        token = _drill_down_token(client, headers=_session_headers())
        response = client.post(
            REVIEW_PATH, json=_form_body(token), headers=_session_headers()
        )
        # The one place the cookie lifecycle runs: the GET the fetch just navigated to.
        drill_down = client.get(SUBMISSION_PATH, headers=_session_headers())

    assert response.headers["content-type"].startswith("application/json")
    assert response.json() == _refusal(GRADE_SESSION_REQUIRED_CODE)
    assert _set_cookie(response) == ""  # the POST touches no cookie
    assert _review_requests(assignments_seen) == []  # the grade POST never fired

    assert drill_down.status_code == 200
    assert cardinality in drill_down.text
    cleared = _set_cookie(drill_down).lower()
    assert f'{SESSION_COOKIE}=""' in cleared and "max-age=0" in cleared


def test_a_degraded_session_read_is_answered_in_the_fetch_shape_and_keeps_the_cookie(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """AC5: the session's state is unknown, so nothing is graded and nothing is discarded."""
    _no_sign_in_target(monkeypatch)
    assignments_seen: list[httpx.Request] = []

    with _portal(
        assignments=_assignments_transport(assignments_seen),
        auth=_auth_transport([], sessions=[_session_ok, _unreachable(), _unreachable()]),
    ) as client:
        token = _drill_down_token(client, headers=_session_headers())
        response = client.post(
            REVIEW_PATH, json=_form_body(token), headers=_session_headers()
        )
        drill_down = client.get(SUBMISSION_PATH, headers=_session_headers())

    assert response.json() == _refusal(GRADE_SESSION_REQUIRED_CODE)
    assert _set_cookie(response) == ""
    assert _review_requests(assignments_seen) == []
    # The drill-down GET re-gates, so the degraded card is rendered there — cookie kept.
    assert "unusable_response" in drill_down.text
    assert _set_cookie(drill_down) == ""


def test_a_session_less_post_navigates_to_the_configured_sign_in(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """AC5: rule 3's gate answer, translated into the shape the fetch can act on."""
    monkeypatch.setenv(portal_app.LOGIN_URL_KEY, SIGN_IN_URL)
    monkeypatch.setenv(portal_app.PUBLIC_BASE_URL_KEY, PORTAL_BASE_URL)
    assignments_seen: list[httpx.Request] = []
    auth_seen: list[httpx.Request] = []

    with _portal(
        assignments=_assignments_transport(assignments_seen),
        auth=_auth_transport(auth_seen),
    ) as client:
        response = client.post(REVIEW_PATH, json=_form_body(_minted_token(client)))

    assert response.status_code == 200
    assert response.headers["content-type"].startswith("application/json")
    assert response.json() == {
        "redirect_uri": f"{SIGN_IN_URL}?return_uri={quote(CALLBACK_URI, safe='')}"
    }
    assert assignments_seen == []
    assert auth_seen == []


def test_the_dev_path_grade_proceeds_with_the_configured_dev_teacher_id(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """AC5: no session, no login target — today's dev posture, header-less, as the queue read."""
    _no_sign_in_target(monkeypatch)
    monkeypatch.setenv(portal_app.DEV_TEACHER_ID_KEY, CONFIGURED_DEV_TEACHER_ID)
    assignments_seen: list[httpx.Request] = []

    with _portal(assignments=_assignments_transport(assignments_seen)) as client:
        token = _drill_down_token(client)
        response = client.post(REVIEW_PATH, json=_form_body(token))

    assert response.json() == {"redirect_uri": SUBMISSION_PATH}
    review = _review_requests(assignments_seen)
    assert len(review) == 1
    assert review[0].headers.get("X-Portal-Session") is None
    assert json.loads(review[0].content)["teacherId"] == CONFIGURED_DEV_TEACHER_ID


def test_the_dev_path_grade_refuses_without_a_configured_dev_teacher_id(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """AC5: the dev id's refusal is intact — a grade is never attributed to a placeholder."""
    _no_sign_in_target(monkeypatch)
    _no_dev_teacher_id(monkeypatch)
    assignments_seen: list[httpx.Request] = []

    with _portal(assignments=_assignments_transport(assignments_seen)) as client:
        token = _drill_down_token(client)
        response = client.post(REVIEW_PATH, json=_form_body(token))

    assert response.json() == _refusal(GRADE_TEACHER_UNCONFIGURED_CODE)
    assert _review_requests(assignments_seen) == []


def test_a_session_without_a_teacher_identity_refuses_the_grade_pre_flight(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """AC5: no usable ``teacherId`` in the claim read means nothing to attribute — no call."""
    _no_sign_in_target(monkeypatch)
    assignments_seen: list[httpx.Request] = []
    identity_less = {key: value for key, value in SESSION_PAYLOAD.items() if key != "teacherId"}

    with _portal(
        assignments=_assignments_transport(assignments_seen),
        auth=_auth_transport(
            [],
            sessions=[
                lambda request: httpx.Response(200, json=identity_less),
                lambda request: httpx.Response(200, json=identity_less),
            ],
        ),
    ) as client:
        token = _drill_down_token(client, headers=_session_headers())
        response = client.post(
            REVIEW_PATH, json=_form_body(token), headers=_session_headers()
        )

    assert response.json() == _refusal(GRADE_IDENTITY_MISSING_CODE)
    assert _review_requests(assignments_seen) == []


# ── AC6: bounded failure mapping, no upstream text in a page ──────────────────────


def _problem(status: int, detail: str) -> Handler:
    return lambda request: httpx.Response(
        status,
        json={"status": status, "detail": detail},
        headers={"content-type": "application/problem+json"},
    )


@pytest.mark.parametrize(
    ("review", "code"),
    [
        (_problem(404, "no submission for that student"), "grade_submission_missing"),
        (_problem(403, "only the creating teacher can review"), "grade_refused"),
        (_problem(500, "boom: internal path C:/secret"), "grade_unavailable"),
        (_unreachable(), "grade_unavailable"),
    ],
    ids=["api-404", "api-403", "api-500", "transport"],
)
def test_an_upstream_failure_redirects_back_with_its_bounded_code(
    monkeypatch: pytest.MonkeyPatch, review: Handler, code: str
) -> None:
    """AC6: the status is mapped; the Problem ``detail`` text goes nowhere."""
    _no_sign_in_target(monkeypatch)
    assignments_seen: list[httpx.Request] = []

    with _portal(
        assignments=_assignments_transport(assignments_seen, review=review),
        auth=_auth_transport([]),
    ) as client:
        token = _drill_down_token(client, headers=_session_headers())
        response = client.post(
            REVIEW_PATH, json=_form_body(token), headers=_session_headers()
        )

    assert response.json() == _refusal(code)
    assert "secret" not in response.text
    assert "creating teacher" not in response.text


def test_the_drill_down_renders_the_bounded_refusal_copy(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """AC6: the page the fetch lands on renders copy keyed by the code, nothing upstream."""
    _no_sign_in_target(monkeypatch)

    with _portal(assignments=_assignments_transport([]), auth=_auth_transport([])) as client:
        response = client.get(
            SUBMISSION_PATH,
            params={"error_code": GRADE_REFUSED_CODE},
            headers=_session_headers(),
        )

    assert response.status_code == 200
    assert "Grade refused" in response.text
    assert "Only the teacher who created the assignment" in response.text


def test_an_unknown_error_code_degrades_to_the_generic_copy(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """A code this portal does not know is a generic bounded message — never the raw string."""
    _no_sign_in_target(monkeypatch)

    with _portal(assignments=_assignments_transport([]), auth=_auth_transport([])) as client:
        response = client.get(
            SUBMISSION_PATH,
            params={"error_code": "<script>alert(1)</script>"},
            headers=_session_headers(),
        )

    assert response.status_code == 200
    assert "Nothing was changed" in response.text
    assert "<script>alert(1)</script>" not in response.text


def test_a_missing_assignments_endpoint_answers_the_grade_post_in_the_fetch_shape(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """A pre-route discovery failure is still a fetch answer, not an HTML page."""
    _no_sign_in_target(monkeypatch)
    monkeypatch.delenv("services__assignments-api__http__0", raising=False)
    monkeypatch.delenv("ASSIGNMENTS_API_HTTP", raising=False)

    with TestClient(portal_app.app) as client:
        response = client.post(
            REVIEW_PATH, json=_form_body(_minted_token(client)), headers=_session_headers()
        )

    assert response.status_code == 200
    assert response.headers["content-type"].startswith("application/json")
    assert response.json() == _refusal(GRADE_UNAVAILABLE_CODE)


# ── AC8: no credential ever reaches Python ────────────────────────────────────────


def test_a_token_shaped_failure_body_is_refused(monkeypatch: pytest.MonkeyPatch) -> None:
    """AC8: a decoded body carrying a token is refused, and its value never surfaces."""
    _no_sign_in_target(monkeypatch)
    leaked = {"error": "refused", "accessToken": "header.payload.signature"}
    assignments_seen: list[httpx.Request] = []

    with _portal(
        assignments=_assignments_transport(
            assignments_seen,
            review=lambda request: httpx.Response(200, json=leaked),
        ),
        auth=_auth_transport([]),
    ) as client:
        token = _drill_down_token(client, headers=_session_headers())
        response = client.post(
            REVIEW_PATH, json=_form_body(token), headers=_session_headers()
        )

    assert response.json() == _refusal(GRADE_RESPONSE_REFUSED_CODE)
    assert "header.payload.signature" not in response.text


def test_the_grade_post_carries_no_credential(monkeypatch: pytest.MonkeyPatch) -> None:
    """AC8/AC9: only the opaque session id travels — the body's ``teacherId`` is claim data."""
    _no_sign_in_target(monkeypatch)
    assignments_seen: list[httpx.Request] = []
    auth_seen: list[httpx.Request] = []

    with _portal(
        assignments=_assignments_transport(assignments_seen),
        auth=_auth_transport(auth_seen),
    ) as client:
        token = _drill_down_token(client, headers=_session_headers())
        client.post(REVIEW_PATH, json=_form_body(token), headers=_session_headers())

    review = _review_requests(assignments_seen)[0]
    header_values = " ".join(value for _, value in review.headers.items())
    assert SESSION_ID in review.headers.get("X-Portal-Session", "")
    # No Authorization header, no token-shaped header, on the write path.
    assert "authorization" not in review.headers
    assert "token" not in header_values.lower().replace("x-portal-session", "")

    # The identity read is the only other call per request, and it carried the opaque id.
    assert [request.url.path for request in auth_seen] == [
        f"/auth/session/{SESSION_ID}",
        f"/auth/session/{SESSION_ID}",
    ]


def test_a_non_numeric_score_is_refused_and_nothing_is_sent(monkeypatch: pytest.MonkeyPatch) -> None:
    """A hand-made body cannot smuggle a string the API's ``decimal?`` binding would reject."""
    _no_sign_in_target(monkeypatch)
    assignments_seen: list[httpx.Request] = []

    with _portal(
        assignments=_assignments_transport(assignments_seen),
        auth=_auth_transport([]),
    ) as client:
        token = _drill_down_token(client, headers=_session_headers())
        response = client.post(
            REVIEW_PATH,
            json=_form_body(token, score="eight and a half"),
            headers=_session_headers(),
        )

    assert response.json() == _refusal(GRADE_SCORE_INVALID_CODE)
    assert _review_requests(assignments_seen) == []
