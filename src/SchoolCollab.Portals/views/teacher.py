"""The teacher surface's Prefab component trees (round ``portal-teacher-surface``, D3).

Four states, all pure functions of data — the split ``views/ward.py`` established:

* :func:`build_teacher_list_view` — the assignment list the drill-down starts from.
* :func:`build_review_queue_view` — one assignment's review queue.
* :func:`build_submission_view` — one submission: version history (where the ward's
  answers live) plus the teacher review.
* :func:`build_teacher_error_view` — the degraded state shared by all three, rendered
  from a typed portal error. The surface never shows a raw 500 or a traceback.

This is a **flat module**, not a ``views/teacher/`` package (round-2 grill Q5, which
supersedes the plan's ``views/teacher/`` wording): one module per surface is the shape
``views/ward.py`` proved, and a one-module package would add a folder node to the
solution view for no reader benefit.

Component variants stay within the two the spike proved (``default`` / ``success``).
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
    Link,
    Muted,
    Row,
)

from api import (
    AssignmentRow,
    PortalApiError,
    ServiceEndpoint,
    SubmissionDetailRow,
    SubmissionForReviewRow,
)

ColumnSpec = tuple[str, str, bool]

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
    *, rows: list[AssignmentRow], endpoint: ServiceEndpoint, status_code: int
) -> PrefabApp:
    """The teacher portal's entry page, from a live assignments-api response."""
    with PrefabApp(title="Teacher portal — assignments", css_class="p-6") as application:
        with Column(gap=4):
            H3("Teacher portal — assignments")
            Muted(f"Live data from {endpoint.label}.")
            with Row(gap=2):
                Badge(f"HTTP {status_code}", variant="success")
                Badge(f"{len(rows)} assignments", variant="default")
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
