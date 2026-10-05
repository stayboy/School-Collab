"""The teacher surface's Prefab component trees (round ``portal-teacher-surface``, D3).

Four states, all pure functions of data — the split ``views/ward.py`` established:

* :func:`build_teacher_list_view` — the assignment list the drill-down starts from.
* :func:`build_review_queue_view` — one assignment's review queue.
* :func:`build_submission_view` — one submission: version history (where the ward's
  answers live), the teacher review, and the grade form.
* :func:`build_teacher_error_view` — the degraded state shared by all three, rendered
  from a typed portal error. The surface never shows a raw 500 or a traceback.

Round ``portal-session-adoption`` (D19/D5) adds the session's own rendering:
:func:`build_teacher_session_card` — the AC10 card a dead or unusable portal session gets —
and the session affordances every teacher page carries through :class:`TeacherSurfaceLinks`
(signed-in identity from the D18 read, logout, or the sign-in links when the environment
supplied them, never an invented URL).

Round ``portal-submission-grade`` (D4) adds the **write half**: the grade form inside
:func:`build_submission_view` — the login form's chain mirrored (a model-driven ``Form``,
a submit-driven reactive error state, and a ``Fetch.post`` to *this portal's own* route,
never to the API and never to the auth service) — with every failure rendered from
:data:`GRADE_COPY`, a bounded code table. The route the form posts to, and the drill-down
that re-renders it, are defined here as :data:`REVIEW_PATH` / :data:`SUBMISSION_PATH` so the
view and the app layer cannot drift.

This is a **flat module**, not a ``views/teacher/`` package (round-2 grill Q5, which
supersedes the plan's ``views/teacher/`` wording): one module per surface is the shape
``views/ward.py`` proved, and a one-module package would add a folder node to the
solution view for no reader benefit.

Component variants stay within the two the spike proved (``default`` / ``success``).
"""

from __future__ import annotations

import re
from dataclasses import dataclass
from urllib.parse import quote

from pydantic import BaseModel
from pydantic import Field as ModelField
from prefab_ui.actions import Action, CallHandler, Fetch, SetState, ShowToast
from prefab_ui.app import PrefabApp
from prefab_ui.components import (
    Alert,
    AlertDescription,
    AlertTitle,
    Badge,
    Button,
    Card,
    CardContent,
    Column,
    DataTable,
    DataTableColumn,
    Field,
    FieldError,
    Form,
    H3,
    Link,
    Muted,
    Row,
    defer,
    insert,
)
from prefab_ui.rx import Rx

from api import (
    ApiResponseError,
    AssignmentRow,
    AuthServiceError,
    PortalApiError,
    ServiceEndpoint,
    SessionData,
    SubmissionDetailRow,
    SubmissionForReviewRow,
    TokenInResponseError,
)

ColumnSpec = tuple[str, str, bool]

#: The bounded failure codes the teacher session surface renders as an AC10 card. One table, so a
#: code and its wording cannot drift apart — and so an unrecognized failure can never echo an
#: upstream body, a token or attacker-supplied text into a page.
SESSION_COPY: dict[str, tuple[str, str]] = {
    "session_ended": (
        "Session ended",
        "Your sign-in expired or was revoked. Sign in again to continue.",
    ),
    "session_not_found": (
        "Session no longer valid",
        "This portal's session cookie was already stale - it has been cleared. Sign in again "
        "to continue.",
    ),
    "invalid_code": (
        "Sign-in handoff rejected",
        "The one-time sign-in code was already used, or it has expired.",
    ),
    "code_expired": (
        "Sign-in handoff rejected",
        "The one-time sign-in code has expired.",
    ),
    "redirect_uri_mismatch": (
        "Sign-in handoff rejected",
        "The one-time sign-in code was issued for a different return address.",
    ),
    "redirect_uri_required": (
        "Sign-in handoff rejected",
        "The handoff carried no return address, so nothing could be redeemed.",
    ),
    "handshake_code_missing": (
        "Sign-in handoff rejected",
        "The browser arrived without a sign-in handoff code.",
    ),
    "auth_service_unconfigured": (
        "Sign-in service unavailable",
        "This portal has no auth-service endpoint configured, so it cannot read or establish a "
        "session.",
    ),
    "upstream_unreachable": (
        "Sign-in service unavailable",
        "The identity provider could not be reached, so the session's state is unknown. Nothing "
        "was changed.",
    ),
    "claim_set_incomplete": (
        "Session unusable",
        "The session's claim set was incomplete, so no identity could be rendered.",
    ),
    "token_in_response": (
        "Response refused",
        "The auth service answered with a token-shaped field. This portal holds no credential, "
        "so the response was refused.",
    ),
    "unusable_response": (
        "Session unavailable",
        "The auth service answered with something this portal could not use. Nothing was "
        "changed.",
    ),
}

#: The card every unrecognized session failure degrades to.
UNUSABLE_RESPONSE_CODE = "unusable_response"

_GENERIC_SESSION_COPY: tuple[str, str] = (
    "Session unavailable",
    "The auth service could not complete this step. Nothing was changed.",
)

#: The drill-down page's own route template, and the grade form's POST target beneath it.
#: ``REVIEW_PATH`` is ALSO what the app's outcome handler matches on the matched route's
#: parameterized pattern — never a raw path, because the ids are request-specific.
SUBMISSION_PATH = "/teacher/assignments/{assignment_id}/students/{student_id}"
REVIEW_PATH = f"{SUBMISSION_PATH}/review"

#: The drill-down's bounded failure query parameter: a refused grade answers the fetch with
#: this page plus the code, and the page renders the code's copy (the auth portal's
#: ``_login_failure`` shape).
ERROR_CODE_PARAMETER = "error_code"

#: The JSON body fields ``POST …/review`` parses (the app layer reads exactly these, and the
#: form's submit body is built from the same names).
ANTIFORGERY_FIELD = "antiforgery_token"
GRADE_SCORE_FIELD = "score"
GRADE_GRADE_FIELD = "grade"
GRADE_COMMENTS_FIELD = "comments"

#: The renderer JS action that navigates the current tab. THIS module's own spelling: the auth
#: portal's handler is that app's, and prefab's ``Fetch`` follows redirects with no redirect
#: option, so a mutation's ``{redirect_uri}`` answer has to move the tab explicitly.
NAVIGATE_HANDLER = "navigate"
NAVIGATE_HANDLER_JS = "(args) => { window.location.assign(args.arguments.url); }"
NAVIGATE_URL_ARGUMENT = "url"

#: The client-state key that makes the grade fields' error state **submit-driven** (the sign-in
#: form's ``ATTEMPTED_STATE`` pattern). It is part of the page's initial state (``False``) and is
#: set by the submit chain and by nothing else, so a page the teacher has not submitted renders
#: with no error state at all. The initial value is load-bearing: prefab's renderer resolves a
#: whole-attribute template to ``undefined`` and falls back to the raw (truthy) template string,
#: so an ``invalid`` expression reading an undeclared key would paint every field invalid on
#: first paint.
GRADE_ATTEMPTED_STATE = "grade_attempted"

#: The bounded codes the grade path renders. Named here, beside the copy table, so the app
#: layer and the table cannot drift apart.
GRADE_FORM_REJECTED_CODE = "grade_form_rejected"
GRADE_ANTIFORGERY_REJECTED_CODE = "grade_antiforgery_rejected"
GRADE_SESSION_REQUIRED_CODE = "grade_session_required"
GRADE_IDENTITY_MISSING_CODE = "grade_identity_missing"
GRADE_TEACHER_UNCONFIGURED_CODE = "grade_teacher_unconfigured"
GRADE_SCORE_INVALID_CODE = "grade_score_invalid"
GRADE_SUBMISSION_MISSING_CODE = "grade_submission_missing"
GRADE_REFUSED_CODE = "grade_refused"
GRADE_UNAVAILABLE_CODE = "grade_unavailable"
GRADE_RESPONSE_REFUSED_CODE = "grade_response_refused"

#: The bounded failure codes the grade path renders, one table (beside :data:`SESSION_COPY`) so a
#: code and its wording cannot drift apart — and so an unrecognized failure can never echo an
#: upstream body, a Problem ``detail``, a token or attacker-supplied text into a page.
GRADE_COPY: dict[str, tuple[str, str]] = {
    GRADE_FORM_REJECTED_CODE: (
        "Grade not submitted",
        "The grade form's own request was not a JSON form submission, so nothing was graded.",
    ),
    GRADE_ANTIFORGERY_REJECTED_CODE: (
        "Grade form expired",
        "The form's one-time token was missing or already used, so nothing was graded. Reload "
        "this page and submit again.",
    ),
    GRADE_SESSION_REQUIRED_CODE: (
        "Grade not recorded",
        "This portal could not confirm your session, so nothing was graded.",
    ),
    GRADE_IDENTITY_MISSING_CODE: (
        "Grade not recorded",
        "The session carried no teacher identity, so the grade was not sent to the assignments "
        "service.",
    ),
    GRADE_TEACHER_UNCONFIGURED_CODE: (
        "Grade not recorded",
        "This portal has no dev teacher id configured (PORTAL_DEV_TEACHER_ID), so it has no "
        "teacher to attribute a grade to.",
    ),
    GRADE_SCORE_INVALID_CODE: (
        "Score not a number",
        "The score that was submitted is not a number, so nothing was graded.",
    ),
    GRADE_SUBMISSION_MISSING_CODE: (
        "Submission not found",
        "The assignments service has no submission for this student on this assignment, so "
        "nothing was graded.",
    ),
    GRADE_REFUSED_CODE: (
        "Grade refused",
        "The assignments service refused this grade. Only the teacher who created the "
        "assignment can grade its submissions, in their own tenant.",
    ),
    GRADE_UNAVAILABLE_CODE: (
        "Grade not recorded",
        "The assignments service could not be reached, so nothing was graded.",
    ),
    GRADE_RESPONSE_REFUSED_CODE: (
        "Response refused",
        "The assignments service answered with something this portal will not ingest, so "
        "nothing was graded.",
    ),
}

#: The alert every unrecognized grade failure degrades to.
_GENERIC_GRADE_COPY: tuple[str, str] = (
    "Grade not recorded",
    "The grade could not be completed. Nothing was changed.",
)

#: The status a typed client refusal names in its own bounded ``detail`` (``HTTP 404 for …``).
_UPSTREAM_STATUS = re.compile(r"HTTP (\d{3})")


def grade_failure_code(error: PortalApiError) -> str:
    """The bounded grade code for a typed client failure — never raw upstream text.

    Mirrors :func:`session_card_code`: a token-shaped body is this portal's own refusal (AC11);
    a client refusal that names an upstream status maps it (``404`` is the assignment/student
    pair having no submission, ``403`` is the creating-teacher / tenant / claim refusal);
    anything else — a transport failure, a non-JSON body, an unrecognized status — is
    ``grade_unavailable``.
    """
    if isinstance(error, TokenInResponseError):
        return GRADE_RESPONSE_REFUSED_CODE
    if isinstance(error, ApiResponseError):
        match = _UPSTREAM_STATUS.search(error.detail)
        if match:
            status = int(match.group(1))
            if status == 404:
                return GRADE_SUBMISSION_MISSING_CODE
            if status == 403:
                return GRADE_REFUSED_CODE
    return GRADE_UNAVAILABLE_CODE


def submission_path(assignment_id: str, student_id: str) -> str:
    """The drill-down URL for one (assignment, student) — the page the grade form lives on."""
    return SUBMISSION_PATH.format(
        assignment_id=quote(assignment_id, safe=""), student_id=quote(student_id, safe="")
    )


def review_path(assignment_id: str, student_id: str) -> str:
    """This (assignment, student) submission's grade route — this portal's own URL."""
    return REVIEW_PATH.format(
        assignment_id=quote(assignment_id, safe=""), student_id=quote(student_id, safe="")
    )


@dataclass(frozen=True)
class TeacherSurfaceLinks:
    """The session affordances' URLs, built from the environment by the app layer.

    Every field is optional because the environment may not configure it: this module reads no
    environment (the pattern's rule 3 — views are pure functions of their inputs), so the app
    layer passes what it could resolve and a missing value means the affordance is rendered as
    its honest absence. A link is never invented, so a page never points at a URL the portal
    could not vouch for.
    """

    sign_in_url: str | None = None
    passkey_url: str | None = None
    logout_url: str | None = None


def session_card_code(error: PortalApiError) -> str:
    """The bounded card code for a typed session failure — never raw upstream text.

    An auth-service failure carries its own ``error`` code when that code is one this surface
    renders; anything else — a transport failure, a non-JSON or token-shaped body, an
    unrecognized code — is the portal's own ``unusable_response``. A missing auth-service endpoint
    is not an error object at all: the app layer names that state itself
    (``auth_service_unconfigured``), because the portal knows it before any call.
    """
    if isinstance(error, TokenInResponseError):
        return "token_in_response"
    if isinstance(error, AuthServiceError) and error.code in SESSION_COPY:
        return error.code
    return UNUSABLE_RESPONSE_CODE


def _session_affordances(
    session: SessionData | None,
    links: TeacherSurfaceLinks | None,
    *,
    signed_out: bool = True,
) -> None:
    """The signed-in identity, or the honest signed-out state — never an invented URL.

    Signed in (D19's rule 1): the identity the D18 read returned, rendered **as data**, plus the
    logout link. Signed out (rule 2 — the dev posture, no redirect target): an honest "no portal
    session" badge, with the sign-in links only when the environment supplied them.

    ``signed_out`` is False for the degraded states that deliberately **keep** the cookie: the
    browser may still be signed in (only the auth service could say otherwise), so the "No portal
    session" badge would contradict the card's own "state unknown" copy. The sign-in links stay —
    they are the way forward either way — so only the false claim is dropped.
    """
    configured = links or TeacherSurfaceLinks()

    with Row(gap=2):
        if session is None:
            if signed_out:
                Badge("No portal session", variant="default")
            if configured.sign_in_url:
                Link("Sign in", href=configured.sign_in_url, target="_self")
            if configured.passkey_url:
                Link("Sign in with a passkey", href=configured.passkey_url, target="_self")
            if not configured.sign_in_url and not configured.passkey_url:
                Muted(
                    "No sign-in URL is configured (PORTALS_LOGIN_URL), so this portal cannot "
                    "send you to one."
                )
        else:
            Badge(f"Signed in as teacher {_shown(session.teacher_id)}", variant="success")
            Muted(
                f"Tenant {_shown(session.tenant_name)} ({_shown(session.tenant_type)}) · "
                f"roles: {', '.join(session.roles) if session.roles else 'none'}"
            )
            if configured.logout_url:
                Link("Sign out", href=configured.logout_url, target="_self")


_ASSIGNMENT_COLUMNS: tuple[ColumnSpec, ...] = (
    ("id", "Assignment id", False),
    ("title", "Title", True),
    ("status", "Status", True),
    ("dueDate", "Due", False),
    ("createdAt", "Created", False),
)

_QUEUE_COLUMNS: tuple[ColumnSpec, ...] = (
    ("studentId", "Student id", False),
    ("assignmentTitle", "Assignment", True),
    ("currentVersionNumber", "Version", True),
    ("reviewState", "Review state", True),
    ("lastSubmittedAt", "Last submitted", True),
    ("submissionId", "Submission id", False),
    ("assignmentId", "Assignment id", False),
)

_VERSION_COLUMNS: tuple[ColumnSpec, ...] = (
    ("versionNumber", "Version", True),
    ("source", "Source", True),
    ("submittedAt", "Submitted", True),
    ("score", "Score", True),
    ("passed", "Passed", True),
    ("content", "Answer", False),
)


def _columns(spec: tuple[ColumnSpec, ...]) -> list[DataTableColumn]:
    """The Prefab column definitions for a table spec."""
    return [
        DataTableColumn(key=key, header=header, sortable=sortable)
        for key, header, sortable in spec
    ]


def _shown(value: str | None) -> str:
    """A value the API did not send, spelled rather than rendered as ``None``."""
    return value if value else "—"


def build_teacher_list_view(
    *,
    rows: list[AssignmentRow],
    endpoint: ServiceEndpoint,
    status_code: int,
    session: SessionData | None = None,
    links: TeacherSurfaceLinks | None = None,
) -> PrefabApp:
    """The teacher portal's entry page, from a live assignments-api response."""
    with PrefabApp(title="Teacher portal — assignments", css_class="p-6") as application:
        with Column(gap=4):
            H3("Teacher portal — assignments")
            Muted(f"Live data from {endpoint.label}.")
            with Row(gap=2):
                Badge(f"HTTP {status_code}", variant="success")
                Badge(f"{len(rows)} assignments", variant="default")
            _session_affordances(session, links)
            if not rows:
                Muted(
                    "No assignments came back for this teacher. An empty result is a "
                    "success, not a failure (the dev database may legitimately be empty)."
                )
            with Card():
                with CardContent():
                    DataTable(
                        columns=_columns(_ASSIGNMENT_COLUMNS),
                        rows=[row.as_table_row() for row in rows],
                        search=True,
                    )
            Muted("Open a review queue at /teacher/assignments/{assignmentId}.")
    return application


def build_review_queue_view(
    *,
    assignment_id: str,
    rows: list[SubmissionForReviewRow],
    endpoint: ServiceEndpoint,
    status_code: int,
    session: SessionData | None = None,
    links: TeacherSurfaceLinks | None = None,
) -> PrefabApp:
    """One assignment's review queue, from a live assignments-api response."""
    with PrefabApp(title="Teacher portal — review queue", css_class="p-6") as application:
        with Column(gap=4):
            H3("Teacher portal — review queue")
            Muted(f"Assignment {assignment_id} · live data from {endpoint.label}.")
            with Row(gap=2):
                Badge(f"HTTP {status_code}", variant="success")
                Badge(f"{len(rows)} submissions", variant="default")
                Link("← All assignments", href="/teacher", target="_self")
            _session_affordances(session, links)
            if not rows:
                Muted(
                    "No submissions are waiting for review on this assignment. An empty "
                    "queue is a success, not a failure."
                )
            with Card():
                with CardContent():
                    DataTable(
                        columns=_columns(_QUEUE_COLUMNS),
                        rows=[row.as_table_row() for row in rows],
                        search=True,
                    )
            Muted(
                "Open one submission at "
                "/teacher/assignments/{assignmentId}/students/{studentId}."
            )
    return application


class GradeSubmissionModel(BaseModel):
    """The grade form's fields and their constraints.

    All three are optional because the Assignments API's ``ReviewSubmissionRequest`` is: a
    review with no score and no grade records ``Reviewed``, and one with either records
    ``Graded``. ``score`` therefore binds a number input with its bounds, ``grade`` a bounded
    text input, and ``comments`` a textarea — the API's own field names, one definition.
    """

    score: float | None = ModelField(
        default=None,
        title="Score",
        description="Numeric score",
        ge=0,
        le=1000,
    )
    grade: str | None = ModelField(
        default=None,
        title="Grade",
        description="Letter or short label",
        max_length=32,
    )
    comments: str | None = ModelField(
        default=None,
        title="Comments",
        description="Feedback for the ward",
        max_length=2000,
        json_schema_extra={"ui": {"type": "textarea", "rows": 3}},
    )


def _grade_blank_expression() -> str:
    """The one client-side rule the optional grade fields have: a submit must carry *something*.

    The API accepts a review with no outcome at all, so no single field is required — the
    reactive ``invalid`` state is submit-driven (like the sign-in form's) and says only that a
    blank submit is a submit the teacher did not mean. It is display-only: the route sends
    whatever the API accepts, and the server is the authority.
    """
    return " && ".join(f"!{name}" for name in GradeSubmissionModel.model_fields)


#: The `FieldError` message on an all-blank submit: the rule is about the FORM, not one field, so
#: every optional field carries the same sentence (the sign-in form's per-field "Enter your …"
#: wording would name a field that is not required).
_GRADE_BLANK_MESSAGE = "Enter a score, a grade or some comments before submitting."


def _grade_notice(error_code: str) -> None:
    """The alert a refused or failed grade renders above the form (bounded code, HTTP 200)."""
    title, message = GRADE_COPY.get(error_code, _GENERIC_GRADE_COPY)
    with Alert(variant="destructive"):
        AlertTitle(title)
        AlertDescription(message)


def _grade_section(
    *, antiforgery_token: str, assignment_id: str, student_id: str, error_code: str | None
) -> None:
    """The grade Card: the login form's chain, mirrored.

    A model-driven ``Form`` whose submit marks the form attempted and then ``Fetch.post``s to
    *this portal's own* route (``REVIEW_PATH``) — never the Assignments API directly, never the
    auth service. The route answers ``{"redirect_uri": …}`` at HTTP 200, and the success action
    navigates the current tab there, so the page re-reads the submission through the drill-down
    GET — which is where the cookie lifecycle and the re-gate run (the POST never touches the
    cookie itself).
    """
    with Card():
        with CardContent():
            with Column(gap=2):
                Muted(
                    "Record a grade or review for this submission. A score or a grade marks it "
                    "graded; feedback on its own records it as reviewed."
                )
                if error_code is not None:
                    _grade_notice(error_code)
                with Form(
                    on_submit=_grade_submit_action(
                        antiforgery_token, assignment_id, student_id
                    )
                ):
                    _grade_fields()
                    Button("Record grade")


def _grade_submit_action(
    antiforgery_token: str, assignment_id: str, student_id: str
) -> list[Action]:
    """The form's submit: mark the form attempted, then ``Fetch.post`` to this portal's route.

    The renderer runs a list of actions in order and stops at the first failure, so the leading
    ``SetState`` has already flipped :data:`GRADE_ATTEMPTED_STATE` by the time the request goes
    out — the moment the fields' ``invalid`` expressions can first become true.
    """
    return [
        SetState(GRADE_ATTEMPTED_STATE, True),
        Fetch.post(
            review_path(assignment_id, student_id),
            body={
                GRADE_SCORE_FIELD: _template(GRADE_SCORE_FIELD),
                GRADE_GRADE_FIELD: _template(GRADE_GRADE_FIELD),
                GRADE_COMMENTS_FIELD: _template(GRADE_COMMENTS_FIELD),
                ANTIFORGERY_FIELD: antiforgery_token,
            },
            on_success=CallHandler(
                NAVIGATE_HANDLER,
                arguments={NAVIGATE_URL_ARGUMENT: _template("$result.redirect_uri")},
            ),
            on_error=ShowToast(_template("$error"), variant="error"),
        ),
    ]


def _grade_fields() -> None:
    """The model's fields, each in a reactive ``Field`` with its ``FieldError``.

    ``Form.from_model(..., fields_only=True)`` generates the labeled inputs (label, placeholder,
    number bounds, textarea) from :class:`GradeSubmissionModel`; they are generated detached so
    each one can be adopted by a ``Field``, whose reactive ``invalid`` expression reads the
    submit-driven :data:`GRADE_ATTEMPTED_STATE`.
    """
    with defer():
        generated = Form.from_model(GradeSubmissionModel, fields_only=True)

    blank = _grade_blank_expression()
    # The zip's strict alignment is the point: ``fields_only=True`` returns one component per
    # model field in order, so a prefab or pydantic change that broke that alignment fails here
    # instead of silently mis-placing a field (the sign-in form's own guard).
    for _name, component in zip(GradeSubmissionModel.model_fields, generated, strict=True):
        with Field(invalid=Rx(f"{GRADE_ATTEMPTED_STATE} && {blank}")):
            insert(component)
            FieldError(_GRADE_BLANK_MESSAGE)


def _template(expression: str) -> str:
    """A prefab template for ``expression`` — ``{{ expression }}``.

    The renderer resolves it against the page's component state and the action context
    (``$result``/``$error``), which is how the submit body reads the inputs and how the success
    action learns where to navigate.
    """
    return "{{ " + expression + " }}"


def build_submission_view(
    *,
    assignment_id: str,
    student_id: str,
    submission: SubmissionDetailRow,
    endpoint: ServiceEndpoint,
    status_code: int,
    session: SessionData | None = None,
    links: TeacherSurfaceLinks | None = None,
    antiforgery_token: str | None = None,
    error_code: str | None = None,
) -> PrefabApp:
    """One submission: its version history, its teacher review, and the grade form.

    ``antiforgery_token`` is the one-time token the route just issued and stored server-side;
    it travels into the submit body, so the server can require the exact page it handed out.
    Without one (the view-level render) the grade form is omitted rather than rendered with a
    body it could not submit. ``error_code`` is a bounded failure code a refused grade
    redirected back with, rendered as an alert above the form — the page the fetch lands on.
    """
    with PrefabApp(
        title="Teacher portal — submission",
        css_class="p-6",
        state={GRADE_ATTEMPTED_STATE: False},
        js_actions={NAVIGATE_HANDLER: NAVIGATE_HANDLER_JS},
    ) as application:
        with Column(gap=4):
            H3("Teacher portal — submission detail")
            Muted(
                f"Assignment {assignment_id} · student {student_id} · "
                f"live data from {endpoint.label}."
            )
            with Row(gap=2):
                Badge(f"HTTP {status_code}", variant="success")
                Badge(f"Version {_shown(submission.current_version_number)}", variant="default")
                Badge(f"Review: {_shown(submission.review_state)}", variant="default")
                Badge(f"Sign-off: {_shown(submission.sign_off_state)}", variant="default")
                Link(
                    "← Review queue",
                    href=f"/teacher/assignments/{assignment_id}",
                    target="_self",
                )
            _session_affordances(session, links)
            Muted(
                f"Submission {_shown(submission.submission_id)} · last submitted "
                f"{_shown(submission.last_submitted_at)}."
            )
            with Card():
                with CardContent():
                    with Column(gap=2):
                        Muted("Version history — each version carries the answer as submitted.")
                        if not submission.versions:
                            Muted("No versions recorded for this submission.")
                        DataTable(
                            columns=_columns(_VERSION_COLUMNS),
                            rows=[version.as_table_row() for version in submission.versions],
                            search=True,
                        )
            with Card():
                with CardContent():
                    with Column(gap=2):
                        Muted("Teacher review")
                        if submission.review is None:
                            Muted("This submission has not been reviewed yet.")
                        else:
                            Muted(
                                f"Score: {_shown(submission.review.score)} · "
                                f"grade: {_shown(submission.review.grade)}"
                            )
                            Muted(f"Comments: {_shown(submission.review.comments)}")
                            Muted(
                                f"Recorded by teacher {_shown(submission.review.teacher_id)} "
                                f"at {_shown(submission.review.created_at)}."
                            )
            if antiforgery_token is not None:
                _grade_section(
                    antiforgery_token=antiforgery_token,
                    assignment_id=assignment_id,
                    student_id=student_id,
                    error_code=error_code,
                )
    return application


def build_teacher_session_card(
    *,
    code: str,
    detail: str | None = None,
    endpoint_label: str | None = None,
    links: TeacherSurfaceLinks | None = None,
    signed_out: bool = True,
) -> PrefabApp:
    """The AC10 card a dead or unusable portal session renders — a page at **HTTP 200**.

    Mirrors the auth portal's ``require_admin_session`` card shape, and is used for the
    dead-session states only (D18's ``session_ended`` / ``session_not_found``, and a handoff code
    the auth service refused): the portal never makes it a hard entry gate, because a session's
    state is either dead (card) or unknown (degraded card) — never "probably signed out".

    ``detail`` is the typed error's own ``str()``: a class name plus the portal's bounded message
    (no token, no upstream body), kept visible for the dev loop the way the ward portal's error
    view keeps it. ``links`` offers the sign-in affordances when the environment configured them;
    a route that clears the cookie alongside this card renders the same links.

    ``signed_out`` is passed through to the affordances: a caller whose card means "the session is
    gone" leaves it True, while the degraded states that keep the cookie pass False — asserting
    "no portal session" there would contradict the card's own "state unknown" copy.
    """
    title, message = SESSION_COPY.get(code, _GENERIC_SESSION_COPY)

    with PrefabApp(title=f"Teacher portal — {title}", css_class="p-6") as application:
        with Column(gap=4):
            H3("Teacher portal")
            with Row(gap=2):
                Badge(title, variant="default")
                Badge(code, variant="default")
            with Card():
                with CardContent():
                    with Column(gap=2):
                        Muted(message)
                        if detail:
                            Muted(detail)
                        if endpoint_label:
                            Muted(f"Auth service: {endpoint_label}")
            _session_affordances(None, links, signed_out=signed_out)
    return application


def build_teacher_error_view(
    *, error: PortalApiError, endpoint: ServiceEndpoint | None, surface: str
) -> PrefabApp:
    """The degraded teacher page: which surface failed, how, and where the portal looked."""
    with PrefabApp(
        title="Teacher portal — degraded", css_class="p-6"
    ) as application:
        with Column(gap=4):
            H3("Teacher portal — degraded")
            with Row(gap=2):
                Badge(f"{surface} degraded", variant="default")
                Badge(type(error).__name__, variant="default")
            with Card():
                with CardContent():
                    with Column(gap=2):
                        Muted(str(error))
                        Muted(
                            "Resolved endpoint: "
                            + (
                                endpoint.label
                                if endpoint
                                else "not resolved (service discovery failed)"
                            )
                        )
    return application
