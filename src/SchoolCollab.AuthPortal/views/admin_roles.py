"""The admin surface — realm-role assignment (spec §8's "Roles" screen, D11).

One page: :func:`build_roles_view` lists the realm's **existing** role set (``GET /auth/admin/roles``
through the auth service; ``{id, name}`` pairs, because Keycloak's role-mappings endpoint keys on
role ids) with an **Assign** and an **Unassign** action per role.

Two boundaries this page holds:

* **Role definitions are never edited at runtime** (D11): the realm import is their only home, and
  the page says so. Neither this module nor ``api/admin_api_client.py`` has a create/update/delete
  for a role *definition* — only the user↔role mapping.
* Both actions go through **this portal's own route** (``POST``/``DELETE /admin/users/{id}/roles``)
  with prefab's ``Fetch``, then navigate the current tab to the returned ``redirect_uri``: the route
  answers HTTP 200 and a card/alert, never a redirect chain and never a 403 page (AC10).

The copy table for degraded states is :func:`views.admin_users.admin_card_copy` (one card
vocabulary for the whole admin surface) and the navigation handler is the sign-in surface's, so
there is exactly one in-tab navigation mechanism on either page.
"""

from __future__ import annotations

from collections.abc import Sequence
from typing import Any

from prefab_ui.actions import CallHandler, Fetch, ShowToast
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
    H3,
    Muted,
    Row,
)

from api.admin_api_client import AdminRole
from views.admin_users import ADMIN_USERS_PATH, admin_card_copy, admin_user_roles_path
from views.login import NAVIGATE_HANDLER, NAVIGATE_HANDLER_JS, NAVIGATE_URL_ARGUMENT

#: Bounded success codes the assign/unassign routes redirect back with (``?notice=…``).
NOTICE_COPY: dict[str, tuple[str, str]] = {
    "assigned": ("Role assigned", "The realm role is now mapped to this user."),
    "unassigned": ("Role unassigned", "The realm role is no longer mapped to this user."),
}

_GENERIC_NOTICE: tuple[str, str] = (
    "Done",
    "The auth service accepted the role-mapping change.",
)


def build_roles_view(
    *,
    user_id: str,
    roles: Sequence[AdminRole],
    notice: str | None = None,
    error: str | None = None,
) -> PrefabApp:
    """The realm-role assignment page for one user (spec §8's "Roles" screen)."""
    with PrefabApp(
        title="School-Collab auth portal - realm roles",
        css_class="p-6",
        js_actions={NAVIGATE_HANDLER: NAVIGATE_HANDLER_JS},
    ) as application:
        with Column(gap=4):
            H3("Realm roles")
            Muted(f"User: {user_id}")
            Muted(
                "Role definitions are declared in the realm import and are never editable here "
                "(D11) - this page assigns and revokes roles that already exist in Keycloak."
            )
            _outcome(notice=notice, error=error)
            with Row(gap=2):
                Button(
                    "Back to users",
                    variant="outline",
                    on_click=CallHandler(
                        NAVIGATE_HANDLER, arguments={NAVIGATE_URL_ARGUMENT: ADMIN_USERS_PATH}
                    ),
                )
            with Card():
                with CardContent():
                    with Column(gap=2):
                        if not roles:
                            Muted(
                                "The realm role set could not be read (it came back empty), so "
                                "there is nothing to assign."
                            )
                        for role in roles:
                            _role_row(role, user_id=user_id)
    return application


def _outcome(*, notice: str | None, error: str | None) -> None:
    """The alert a redirected assign/unassign outcome renders — bounded codes only (AC10 at 200)."""
    if error is not None:
        title, message = admin_card_copy(error)
        with Alert(variant="destructive"):
            AlertTitle(title)
            AlertDescription(message)
    elif notice is not None:
        title, message = NOTICE_COPY.get(notice, _GENERIC_NOTICE)
        with Alert(variant="success"):
            AlertTitle(title)
            AlertDescription(message)


def _role_row(role: AdminRole, *, user_id: str) -> None:
    """One realm role with its two actions, or an explanation when it cannot be mapped.

    A role without an id cannot be named to Keycloak's role-mappings endpoint, so the row says so
    instead of offering a button that must fail.
    """
    with Row(gap=2):
        Badge(role.name or role.id)
        if not role.id:
            Muted("no role id in the read, so this role cannot be mapped")
            return
        target = admin_user_roles_path(user_id)
        body: dict[str, Any] = {"roles": [{"id": role.id, "name": role.name}]}
        Button(
            "Assign",
            variant="outline",
            on_click=Fetch.post(
                target,
                body=body,
                on_success=_navigate_to_result(),
                on_error=ShowToast(_template("$error"), variant="error"),
            ),
        )
        Button(
            "Unassign",
            variant="outline",
            on_click=Fetch.delete(
                target,
                body=body,
                on_success=_navigate_to_result(),
                on_error=ShowToast(_template("$error"), variant="error"),
            ),
        )


def _navigate_to_result() -> CallHandler:
    """Move the current tab to the route's ``redirect_uri`` (the one navigation mechanism)."""
    return CallHandler(
        NAVIGATE_HANDLER,
        arguments={NAVIGATE_URL_ARGUMENT: _template("$result.redirect_uri")},
    )


def _template(expression: str) -> str:
    """A prefab template for ``expression`` — ``{{ expression }}``."""
    return "{{ " + expression + " }}"
