"""The teacher surface's Prefab component trees (round ``portal-teacher-surface``, D3).

Four states, all pure functions of data — the split ``views/ward.py`` established:

* :func:`build_teacher_list_view` — the assignment list the drill-down starts from.
* :func:`build_review_queue_view` — one assignment's review queue.
* :func:`build_submission_view` — one submission: version history (where the ward's
  answers live) plus the teacher review.
* :func:`build_teacher_error_view` — the degraded state shared by all three, rendered
  from a typed portal error. The surface never shows a raw 500 or a traceback.

Round ``portal-session-adoption`` (D19/D5) adds the session's own rendering:
:func:`build_teacher_session_card` — the AC10 card a dead or unusable portal session gets —
and the session affordances every teacher page carries through :class:`TeacherSurfaceLinks`
(signed-in identity from the D18 read, logout, or the sign-in links when the environment
supplied them, never an invented URL).

This is a **flat module**, not a ``views/teacher/`` package (round-2 grill Q5, which
supersedes the plan's ``views/teacher/`` wording): one module per surface is the shape
``views/ward.py`` proved, and a one-module package would add a folder node to the
solution view for no reader benefit.

Component variants stay within the two the spike proved (``default`` / ``success``).
"""

from __future__ import annotations

from dataclasses import dataclass

from prefab_ui.app import PrefabApp
from prefab_ui.components import (
    Badge,
    Card,
    CardContent,
    Column,
    DataTable,
    DataTableColumn,
    H3,
    Link,
    Muted,
    Row,
)

from api import (
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


def build_submission_view(
    *,
    assignment_id: str,
    student_id: str,
    submission: SubmissionDetailRow,
    endpoint: ServiceEndpoint,
    status_code: int,
    session: SessionData | None = None,
    links: TeacherSurfaceLinks | None = None,
) -> PrefabApp:
    """One submission: its version history (the answers) and its teacher review."""
    with PrefabApp(title="Teacher portal — submission", css_class="p-6") as application:
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
