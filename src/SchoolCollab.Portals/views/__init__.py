"""Prefab UI component trees for the portal surfaces.

Views are pure functions: data in (DTOs + endpoint metadata), ``PrefabApp``
out. No HTTP, no environment reads — that keeps prefab-ui's 0.x churn isolated
from transport (plan section 6 risk row).
"""

from views.teacher import (
    build_review_queue_view,
    build_submission_view,
    build_teacher_error_view,
    build_teacher_list_view,
)
from views.ward import build_error_view, build_ward_view

__all__ = [
    "build_error_view",
    "build_review_queue_view",
    "build_submission_view",
    "build_teacher_error_view",
    "build_teacher_list_view",
    "build_ward_view",
]
