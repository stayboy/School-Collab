"""Prefab UI component trees for the portal surfaces.

Views are pure functions: data in (DTOs + endpoint metadata), ``PrefabApp``
out. No HTTP, no environment reads — that keeps prefab-ui's 0.x churn isolated
from transport (plan section 6 risk row).
"""

from views.ward import build_error_view, build_ward_view

__all__ = ["build_error_view", "build_ward_view"]
