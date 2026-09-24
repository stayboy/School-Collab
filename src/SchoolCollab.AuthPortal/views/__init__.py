"""Prefab UI component trees for the auth portal surfaces.

Views are pure functions: data in (DTOs + endpoint metadata), ``PrefabApp``
out. No HTTP, no environment reads, no cookie handling — that keeps prefab-ui's
0.x churn and the route layer's concerns isolated from each other (the same
split ``src/SchoolCollab.Portals/views`` uses).

* :mod:`views.login` — the password form, the passkey handoff button and the
  in-tab navigation the renderer performs after a submit.
* :mod:`views.errors` — the fail-closed AC10 cards (D18 ``session ended``, an
  invalid ``return_uri``, and every typed auth-service failure).
"""

from views.errors import (
    build_error_card,
    build_invalid_return_uri_card,
    build_session_ended_card,
    card_copy,
    code_for_error,
)
from views.login import (
    ANTIFORGERY_FIELD,
    LOGIN_PATH,
    PASSWORD_FIELD,
    RETURN_URI_FIELD,
    USERNAME_FIELD,
    LoginFormModel,
    build_login_view,
)

__all__ = [
    "ANTIFORGERY_FIELD",
    "LOGIN_PATH",
    "PASSWORD_FIELD",
    "RETURN_URI_FIELD",
    "USERNAME_FIELD",
    "LoginFormModel",
    "build_error_card",
    "build_invalid_return_uri_card",
    "build_login_view",
    "build_session_ended_card",
    "card_copy",
    "code_for_error",
]
