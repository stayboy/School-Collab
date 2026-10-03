"""Tolerant row DTOs for the Assignments API.

The API is the authority on shape, so these dataclasses accept missing or
renamed fields instead of exploding: a shape drift should degrade one cell, not
the whole page (the dev database legitimately answers ``[]`` — see the ar-21/23
data-visibility note). Views consume attributes, never raw dictionaries.

The ward surface reads :class:`AssignmentRow`; the teacher surface adds the
review-queue row and the submission detail (with its version history and review).
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


# The teacher surface renders two enums the Assignments API serializes as **ints**:
# `Assignments.Api/Program.cs` registers a JsonStringEnumConverter for every DTO enum
# except these two, so the wire value is 0/1/2. The maps also pass a string through
# unchanged, so a later converter flip degrades to today's display rather than to a blank.
_REVIEW_STATE_NAMES = {"0": "Pending", "1": "Reviewed", "2": "Graded"}
_SUBMISSION_SOURCE_NAMES = {"0": "Student", "1": "Guardian"}


def _enum_name(value: Any, names: Mapping[str, str]) -> str | None:
    """Map an int-serialized enum value to its display name, tolerating either form."""
    text = _text(value)
    return names.get(text, text) if text is not None else None


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


@dataclass(frozen=True)
class SubmissionForReviewRow:
    """One row of ``GET /{id}/submissions/review-queue`` — what the queue view tabulates.

    Mirrors the API's ``SubmissionForReviewDto``.
    """

    submission_id: str | None = None
    assignment_id: str | None = None
    assignment_title: str | None = None
    student_id: str | None = None
    current_version_number: str | None = None
    review_state: str | None = None
    last_submitted_at: str | None = None

    @classmethod
    def from_payload(cls, payload: Mapping[str, Any]) -> SubmissionForReviewRow:
        """Build a row from the API's JSON object, tolerating alternate names."""
        return cls(
            submission_id=_text(payload.get("submissionId")),
            assignment_id=_text(payload.get("assignmentId")),
            assignment_title=_text(payload.get("assignmentTitle") or payload.get("title")),
            student_id=_text(payload.get("studentId")),
            current_version_number=_text(
                payload.get("currentVersionNumber") or payload.get("versionNumber")
            ),
            review_state=_enum_name(payload.get("reviewState"), _REVIEW_STATE_NAMES),
            last_submitted_at=_text(payload.get("lastSubmittedAt")),
        )

    def as_table_row(self) -> dict[str, Any]:
        """The column-keyed mapping the Prefab ``DataTable`` addresses."""
        return {
            "submissionId": self.submission_id,
            "assignmentId": self.assignment_id,
            "assignmentTitle": self.assignment_title,
            "studentId": self.student_id,
            "currentVersionNumber": self.current_version_number,
            "reviewState": self.review_state,
            "lastSubmittedAt": self.last_submitted_at,
        }


@dataclass(frozen=True)
class SubmissionVersionRow:
    """One entry of a submission's version history (``SubmissionVersionDto``).

    The version's ``content`` is where the ward's answer lives — the detail response
    carries no separate answers array.
    """

    id: str | None = None
    version_number: str | None = None
    source: str | None = None
    content: str | None = None
    submitted_at: str | None = None
    score: str | None = None
    passed: str | None = None

    @classmethod
    def from_payload(cls, payload: Mapping[str, Any]) -> SubmissionVersionRow:
        """Build a version row from the API's JSON object, tolerating alternate names."""
        return cls(
            id=_text(payload.get("id")),
            version_number=_text(payload.get("versionNumber")),
            source=_enum_name(payload.get("source"), _SUBMISSION_SOURCE_NAMES),
            content=_text(payload.get("content")),
            submitted_at=_text(payload.get("submittedAt")),
            score=_text(payload.get("score")),
            passed=_text(payload.get("passed")),
        )

    def as_table_row(self) -> dict[str, Any]:
        """The column-keyed mapping the Prefab ``DataTable`` addresses."""
        return {
            "versionNumber": self.version_number,
            "source": self.source,
            "submittedAt": self.submitted_at,
            "score": self.score,
            "passed": self.passed,
            "content": self.content,
        }


@dataclass(frozen=True)
class SubmissionReviewRow:
    """The teacher review/grade attached to a submission (``SubmissionReviewDto``)."""

    teacher_id: str | None = None
    score: str | None = None
    grade: str | None = None
    comments: str | None = None
    created_at: str | None = None

    @classmethod
    def from_payload(cls, payload: Mapping[str, Any]) -> SubmissionReviewRow:
        """Build the review row from the API's JSON object, tolerating alternate names."""
        return cls(
            teacher_id=_text(payload.get("teacherId")),
            score=_text(payload.get("score")),
            grade=_text(payload.get("grade")),
            comments=_text(payload.get("comments")),
            created_at=_text(payload.get("createdAt")),
        )


@dataclass(frozen=True)
class SubmissionDetailRow:
    """``GET /{id}/students/{studentId}/submission`` — one submission (``SubmissionDetailDto``)."""

    submission_id: str | None = None
    assignment_id: str | None = None
    student_id: str | None = None
    current_version_number: str | None = None
    review_state: str | None = None
    last_submitted_at: str | None = None
    sign_off_state: str | None = None
    versions: tuple[SubmissionVersionRow, ...] = ()
    review: SubmissionReviewRow | None = None

    @classmethod
    def from_payload(cls, payload: Mapping[str, Any]) -> SubmissionDetailRow:
        """Build the detail from the API's JSON object, tolerating shape drift per field."""
        raw_versions = payload.get("versions")
        versions = (
            tuple(
                SubmissionVersionRow.from_payload(item)
                for item in raw_versions
                if isinstance(item, Mapping)
            )
            if isinstance(raw_versions, list)
            else ()
        )

        raw_review = payload.get("review")
        return cls(
            submission_id=_text(payload.get("submissionId")),
            assignment_id=_text(payload.get("assignmentId")),
            student_id=_text(payload.get("studentId")),
            current_version_number=_text(payload.get("currentVersionNumber")),
            review_state=_enum_name(payload.get("reviewState"), _REVIEW_STATE_NAMES),
            last_submitted_at=_text(payload.get("lastSubmittedAt")),
            sign_off_state=_text(payload.get("signOffState")),
            versions=versions,
            review=(
                SubmissionReviewRow.from_payload(raw_review)
                if isinstance(raw_review, Mapping)
                else None
            ),
        )
