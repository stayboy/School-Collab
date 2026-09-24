"""The admin surface — tenant users, their claims and their credential (spec §8, D8/D10/D11).

Two pages and one card:

* :func:`build_users_view` — the users list (`DataTable`) plus the **create** form: a
  ``Form.from_model`` tree whose fields (username, initial credential, the D10 claim attributes)
  come from :class:`AdminUserFormModel`, with the **tenant `Select`** and the **searchable teacher
  `Combobox`** inserted between them. Both pickers are rendered from the auth service's **mediated**
  reads (B7: ``GET /auth/pickers/tenants`` / ``/auth/pickers/teachers``) — the portal never calls
  settings-api or students-api itself (D17, AC11). The list's own rows carry each user's Edit and
  Roles actions, so a user is reachable from the list exactly once, and its Tenant/Teacher cells
  are resolved through those same picker rows rather than printed as the raw claim ids (B11).
* :func:`build_user_edit_view` — the same form pre-filled for an existing user (enable/disable, the
  claim attributes, and — when the credential field is filled — a credential reset), plus the link
  to that user's roles page.
* :func:`build_admin_card` — the admin surface's fail-closed page (**HTTP 200**, AC10): the
  ``user-admin`` gate refusal, a dead session, the auth service being unreachable, and Keycloak's
  own refusals. It is the same card shape as ``views.errors``' and shares its copy table for the
  codes that table already names; the admin-only copy lives here because ``views/errors.py``
  belongs to the sign-in surface (B8) and is outside this pass's file set.

**Both mutations go through this portal's own FastAPI routes** — the form's built-in
``Fetch.post``/``Fetch.put`` to ``/admin/users``, never to the auth service and never to Keycloak.
The route answers HTTP 200 ``{"redirect_uri": …}`` and the same in-tab navigation handler the
sign-in surface registers moves the browser (prefab's ``Fetch`` has no redirect option), so a
mutation is a page at HTTP 200 either way (AC10).

``tenant_id`` and ``teacher_id`` are **always** picker values (D8: a user is bound to an existing
tenant and an existing teacher row — the admin never types an id and never writes a tenant or
teacher row); ``tenant_name``/``tenant_type`` are the remaining D10 claim attributes and stay
editable text. No credential ever enters Python state: a submitted credential travels straight
through to the auth service's ``reset-password`` call and is never held, logged or echoed here.
"""

from __future__ import annotations

from collections.abc import Sequence
from typing import Any
from urllib.parse import quote

from pydantic import BaseModel, SecretStr
from pydantic import Field as ModelField
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
    Combobox,
    ComboboxOption,
    DataTable,
    DataTableColumn,
    Form,
    H3,
    Label,
    Muted,
    Row,
    Select,
    SelectOption,
    defer,
    insert,
)

from api.admin_api_client import AdminUser, PickerTeacher, PickerTenant
from api.dto import USER_ADMIN_ROLE
from views.errors import card_copy
from views.login import NAVIGATE_HANDLER, NAVIGATE_HANDLER_JS, NAVIGATE_URL_ARGUMENT

#: This portal's own admin routes. app.py imports these, so the route and the action that calls it
#: are one definition (the B8 ``LOGIN_PATH`` precedent). The roles page is nested under the users
#: prefix, so its path is defined here too — one definition per route, and the direction of the
#: import between the two admin view modules stays acyclic.
ADMIN_PATH_PREFIX = "/admin"
ADMIN_USERS_PATH = "/admin/users"
ADMIN_USER_EDIT_PATH = "/admin/users/{user_id}/edit"
ADMIN_USER_ROLES_PATH = "/admin/users/{user_id}/roles"

#: The create/update body's field names — the portal's own JSON contract, read by the app.py
#: routes. The claim attributes keep the realm's own snake_case names (D10), and ``tenant_id`` /
#: ``teacher_id`` are the two picker state keys, so a picker's selection has exactly one spelling.
MODEL_USERNAME_FIELD = "username"
MODEL_CREDENTIAL_FIELD = "initial_password"
MODEL_EMAIL_FIELD = "email"
MODEL_TENANT_NAME_FIELD = "tenant_name"
MODEL_TENANT_TYPE_FIELD = "tenant_type"
MODEL_ENABLED_FIELD = "enabled"
TENANT_FIELD = "tenant_id"
TEACHER_FIELD = "teacher_id"

#: Carried by an update only: the auth service's ``PUT`` is a full-representation replace keyed on
#: the user id.
USER_ID_FIELD = "user_id"

#: The gate refusals this surface renders its own copy for (AC10 cards at HTTP 200).
ADMIN_ROLE_REQUIRED_CODE = "admin_role_required"
ADMIN_SIGN_IN_REQUIRED_CODE = "admin_sign_in_required"

#: Bounded success codes the mutation routes redirect back with (``?notice=…``).
NOTICE_COPY: dict[str, tuple[str, str]] = {
    "created": (
        "User created",
        "The realm user was created and bound to the picked tenant and teacher row.",
    ),
    "updated": (
        "User updated",
        "The realm user's representation and claim attributes were replaced.",
    ),
}

#: Admin-surface copy, consulted before ``views.errors``' shared table (which the dead-session and
#: upstream codes use, so a user sees one vocabulary across the whole portal).
_ADMIN_CARD_COPY: dict[str, tuple[str, str]] = {
    ADMIN_ROLE_REQUIRED_CODE: (
        "Administrator role required",
        f"This portal's admin surface is for sessions carrying the {USER_ADMIN_ROLE} realm role, "
        "and this session does not carry it. Ask a realm administrator to grant it.",
    ),
    ADMIN_SIGN_IN_REQUIRED_CODE: (
        "Sign in required",
        "This portal's admin surface needs a signed-in session. Sign in and try again.",
    ),
    "forbidden": (
        "Admin operation refused",
        "Keycloak refused the admin operation: the auth service's admin service account does not "
        "carry the realm-management role it needs. Nothing was changed.",
    ),
    "conflict": (
        "User already exists",
        "The realm already has a user with this username. Nothing was changed.",
    ),
    "not_found": (
        "User not found",
        "The realm user this page names does not exist (any more).",
    ),
}

#: What a cell renders when the read carried no value a person can interpret — a deliberate em
#: dash, never an id (B11: the Tenant/Teacher columns must not print a raw ``tenant_id`` etc.).
UNRESOLVED_CELL = "—"

#: The users table's actions column: the cell that carries this row's Edit/Roles buttons.
ROW_ACTIONS_COLUMN_KEY = "actions"

#: The users table's columns (spec §8's list; the identity context of D8/D10 alongside it, and the
#: per-row actions that make the table itself the one place a user is reachable from).
_TABLE_COLUMNS: tuple[tuple[str, str, bool], ...] = (
    ("username", "Username", True),
    ("email", "Email", True),
    ("tenant", "Tenant", False),
    ("teacher", "Teacher", False),
    ("roles", "Roles", False),
    ("enabled", "Enabled", False),
    (ROW_ACTIONS_COLUMN_KEY, "Actions", False),
)


class AdminUserFormModel(BaseModel):
    """The tenant-user form's fields and constraints (spec §8, D8/D10).

    ``SecretStr`` is what makes ``Form.from_model`` emit ``Input(input_type="password")`` for the
    credential, and each field's ``title``/bounds are the form's single definition. ``tenant_id``
    and ``teacher_id`` are deliberately **not** model fields: the tenant ``Select`` and the teacher
    ``Combobox`` own those state keys, so a picked row is the only way either id enters the form.
    """

    username: str = ModelField(
        title="Username",
        description="School-Collab username",
        min_length=1,
        max_length=256,
    )
    initial_password: SecretStr | None = ModelField(
        default=None,
        title="Credential",
        description="Sets the credential; leave blank to keep the current one",
        max_length=256,
    )
    email: str | None = ModelField(
        default=None,
        title="Email",
        description="Optional",
        max_length=256,
    )
    tenant_name: str | None = ModelField(
        default=None,
        title="Tenant name",
        description="The tenant_name claim attribute",
        max_length=256,
    )
    tenant_type: str | None = ModelField(
        default=None,
        title="Tenant type",
        description="The tenant_type claim attribute",
        max_length=128,
    )
    enabled: bool = ModelField(default=True, title="Enabled")


def admin_card_copy(code: str | None) -> tuple[str, str]:
    """The admin surface's card copy: its own table first, then the portal's shared one."""
    return _ADMIN_CARD_COPY.get(code or "", card_copy(code))


def build_admin_card(
    *,
    code: str | None,
    detail: str | None = None,
    endpoint_label: str | None = None,
) -> PrefabApp:
    """The admin surface's fail-closed page — HTTP 200, never a 500, a 403 page or a redirect.

    The shape mirrors ``views.errors.build_error_card`` (the portal renders one kind of card); only
    the copy table differs, for the states the sign-in table cannot name.
    """
    title, message = admin_card_copy(code)

    with PrefabApp(
        title=f"School-Collab auth portal - {title}", css_class="p-6"
    ) as application:
        with Column(gap=4):
            H3("School-Collab auth portal - admin")
            with Row(gap=2):
                Badge(title, variant="default")
                if code:
                    Badge(code, variant="default")
            with Card():
                with CardContent():
                    with Column(gap=2):
                        Muted(message)
                        if detail:
                            Muted(detail)
                        if endpoint_label:
                            Muted(f"Auth service: {endpoint_label}")
    return application


def build_users_view(
    *,
    users: Sequence[AdminUser],
    tenants: Sequence[PickerTenant],
    teachers: Sequence[PickerTeacher],
    notice: str | None = None,
    error: str | None = None,
) -> PrefabApp:
    """The users list plus the create form (spec §8's "Users list" and "Create user" screens).

    The list is the single surface a user is reachable from: its rows carry the Edit and Roles
    actions (no second per-user card re-listing the same users), and its Tenant/Teacher cells name
    the tenant and teacher through the **same** mediated picker rows the create form's pickers are
    built from — never through a direct upstream call (D17) and never by printing a claim id.
    """
    tenant_labels = _picker_labels(tenants)
    teacher_labels = _picker_labels(teachers)

    with PrefabApp(
        title="School-Collab auth portal - users",
        css_class="p-6",
        js_actions={NAVIGATE_HANDLER: NAVIGATE_HANDLER_JS},
    ) as application:
        with Column(gap=4):
            H3("Realm users")
            Muted(
                "Identity lives in Keycloak: this portal reads and writes realm users through the "
                "auth service and holds no admin credential of its own."
            )
            _outcome(notice=notice, error=error)
            with Card():
                with CardContent():
                    DataTable(
                        columns=[
                            DataTableColumn(key=key, header=header, sortable=sortable)
                            for key, header, sortable in _TABLE_COLUMNS
                        ],
                        rows=[
                            _table_row(
                                user,
                                tenant_labels=tenant_labels,
                                teacher_labels=teacher_labels,
                            )
                            for user in users
                        ],
                        search=True,
                    )
            _create_section(tenants=tenants, teachers=teachers)
    return application


def build_user_edit_view(
    *,
    user: AdminUser,
    tenants: Sequence[PickerTenant],
    teachers: Sequence[PickerTeacher],
    roles_path: str,
    notice: str | None = None,
    error: str | None = None,
) -> PrefabApp:
    """The edit form for one realm user (spec §8's "Edit user" screen).

    The form is the create form pre-filled and bound to ``PUT /admin/users``: enabling/disabling
    (a checkbox), the claim attributes, and — when the credential field is filled — a credential
    reset. ``roles_path`` is that user's roles page, the other half of the admin surface.
    """
    with PrefabApp(
        title=f"School-Collab auth portal - edit {user.display_name}",
        css_class="p-6",
        js_actions={NAVIGATE_HANDLER: NAVIGATE_HANDLER_JS},
    ) as application:
        with Column(gap=4):
            H3(f"Edit {user.display_name}")
            Muted("Realm roles are assigned on the roles screen; role definitions live in the realm.")
            _outcome(notice=notice, error=error)
            with Row(gap=2):
                Button(
                    "Roles",
                    variant="outline",
                    on_click=CallHandler(
                        NAVIGATE_HANDLER, arguments={NAVIGATE_URL_ARGUMENT: roles_path}
                    ),
                )
                Button(
                    "Back to users",
                    variant="outline",
                    on_click=CallHandler(
                        NAVIGATE_HANDLER, arguments={NAVIGATE_URL_ARGUMENT: ADMIN_USERS_PATH}
                    ),
                )
            with Card():
                with CardContent():
                    with Form(on_submit=_submit_action(method="PUT", user_id=user.id)):
                        _form_fields(
                            tenants=tenants,
                            teachers=teachers,
                            defaults=_edit_defaults(user),
                        )
                        Button("Save changes")
    return application


def _outcome(*, notice: str | None, error: str | None) -> None:
    """The alert a redirected mutation outcome renders — bounded codes only (AC10 at 200)."""
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


_GENERIC_NOTICE: tuple[str, str] = (
    "Done",
    "The auth service accepted the operation.",
)


def _picker_labels(rows: Sequence[PickerTenant] | Sequence[PickerTeacher]) -> dict[str, str]:
    """The mediated picker rows as ``id -> display label`` (the label each picker also shows).

    A user's ``tenant_id``/``teacher_id`` claim is a picker ``value``, so the list resolves it
    through the very rows the form's pickers offer (D8's rows, B7's mediated reads). Both
    ``PickerTenant.label`` and ``PickerTeacher.label`` fall back to the row's own id when the read
    carried nothing a person can read; that fallback is dropped here, because a cell shows a
    readable name or the deliberate em dash — never an identifier (B11).
    """
    return {row.id: row.label for row in rows if row.id and row.label != row.id}


def _create_section(
    *, tenants: Sequence[PickerTenant], teachers: Sequence[PickerTeacher]
) -> None:
    """The create form: the model's fields with the two mediated pickers between them."""
    with Card():
        with CardContent():
            with Column(gap=4):
                H3("Create a tenant user")
                Muted(
                    "A new user is bound to an existing tenant and an existing teacher row "
                    "(identity only): the portal never writes tenant or teacher data."
                )
                if not tenants or not teachers:
                    # Fail-closed and visible: without both picker sources there is nothing valid
                    # to bind a user to (D8), so the form is not rendered at all rather than
                    # rendered with an empty picker that could submit a half-bound user.
                    Muted(_empty_picker_reason(tenants=tenants, teachers=teachers))
                    return
                with Form(on_submit=_submit_action(method="POST")):
                    _form_fields(tenants=tenants, teachers=teachers, defaults=None)
                    Button("Create user")


def _empty_picker_reason(
    *, tenants: Sequence[PickerTenant], teachers: Sequence[PickerTeacher]
) -> str:
    """Why the create form is not rendered (D8 needs both picker rows to bind a user)."""
    missing = [
        name
        for name, rows in (("tenants", tenants), ("teachers", teachers))
        if not rows
    ]
    return (
        "The create form is unavailable: the mediated "
        + " and ".join(missing)
        + " picker read returned no rows, and a tenant user must be bound to both."
    )


def _form_fields(
    *,
    tenants: Sequence[PickerTenant],
    teachers: Sequence[PickerTeacher],
    defaults: dict[str, Any] | None,
) -> None:
    """The model's fields in order, with the tenant and teacher pickers next to the attributes.

    ``Form.from_model(..., fields_only=True)`` generates each labeled input (label, placeholder,
    constraints, password type) from :class:`AdminUserFormModel`; they are generated detached so
    they can be placed one by one, which is what lets the two picker fields — the components the
    model deliberately does not describe — sit between the username/credential and the attributes.
    """
    with defer():
        model_defaults = {
            name: value
            for name, value in (defaults or {}).items()
            if name in AdminUserFormModel.model_fields
        }
        generated = Form.from_model(
            AdminUserFormModel, fields_only=True, defaults=model_defaults or None
        )

    selected_tenant = (defaults or {}).get(TENANT_FIELD)
    selected_teacher = (defaults or {}).get(TEACHER_FIELD)

    for name, component in zip(AdminUserFormModel.model_fields, generated, strict=True):
        if name == MODEL_TENANT_NAME_FIELD:
            _tenant_picker(tenants, selected=selected_tenant)
            _teacher_picker(teachers, selected=selected_teacher)
        insert(component)


def _tenant_picker(tenants: Sequence[PickerTenant], *, selected: str | None) -> None:
    """The tenant ``Select``, one option per mediated tenant row (its id is the option value)."""
    with Column(gap=2):
        Label("Tenant")
        with Select(
            name=TENANT_FIELD,
            value=selected,
            placeholder="Select a tenant",
            required=True,
        ):
            for tenant in tenants:
                SelectOption(value=tenant.id, label=tenant.label)


def _teacher_picker(teachers: Sequence[PickerTeacher], *, selected: str | None) -> None:
    """The searchable teacher ``Combobox``, one option per mediated teacher row."""
    with Column(gap=2):
        Label("Teacher")
        with Combobox(
            name=TEACHER_FIELD,
            value=selected,
            placeholder="Select a teacher",
            search_placeholder="Search teachers",
        ):
            for teacher in teachers:
                ComboboxOption(label=teacher.label, value=teacher.id)


def _submit_action(*, method: str, user_id: str | None = None) -> Fetch:
    """The form's submit: ``Fetch`` to this portal's own ``/admin/users`` route.

    The body names the state keys the renderer binds (the model's fields plus the two picker
    state keys), and the success action navigates the current tab to the route's
    ``redirect_uri`` — the same in-tab navigation the sign-in surface registers, because prefab's
    ``Fetch`` follows redirects and has no redirect option.
    """
    body: dict[str, Any] = {
        MODEL_USERNAME_FIELD: _template(MODEL_USERNAME_FIELD),
        MODEL_CREDENTIAL_FIELD: _template(MODEL_CREDENTIAL_FIELD),
        MODEL_EMAIL_FIELD: _template(MODEL_EMAIL_FIELD),
        MODEL_TENANT_NAME_FIELD: _template(MODEL_TENANT_NAME_FIELD),
        MODEL_TENANT_TYPE_FIELD: _template(MODEL_TENANT_TYPE_FIELD),
        MODEL_ENABLED_FIELD: _template(MODEL_ENABLED_FIELD),
        TENANT_FIELD: _template(TENANT_FIELD),
        TEACHER_FIELD: _template(TEACHER_FIELD),
    }
    if user_id:
        body[USER_ID_FIELD] = user_id

    submit = Fetch.post if method == "POST" else Fetch.put
    return submit(
        ADMIN_USERS_PATH,
        body=body,
        on_success=CallHandler(
            NAVIGATE_HANDLER,
            arguments={NAVIGATE_URL_ARGUMENT: _template("$result.redirect_uri")},
        ),
        on_error=ShowToast(_template("$error"), variant="error"),
    )


def _edit_defaults(user: AdminUser) -> dict[str, Any]:
    """The edit form's per-render defaults, keyed by the model's fields and the picker state keys."""
    return {
        MODEL_USERNAME_FIELD: user.username,
        MODEL_EMAIL_FIELD: user.email,
        MODEL_TENANT_NAME_FIELD: user.tenant_name,
        MODEL_TENANT_TYPE_FIELD: user.tenant_type,
        MODEL_ENABLED_FIELD: user.enabled,
        TENANT_FIELD: user.tenant_id,
        TEACHER_FIELD: user.teacher_id,
    }


def admin_user_edit_path(user_id: str) -> str:
    """This user's edit page URL (the id is quoted: it is an upstream identifier, not a path)."""
    return ADMIN_USER_EDIT_PATH.format(user_id=quote(user_id, safe=""))


def admin_user_roles_path(user_id: str) -> str:
    """This user's roles page URL — the route the roles view's assign/unassign actions call."""
    return ADMIN_USER_ROLES_PATH.format(user_id=quote(user_id, safe=""))


def _table_row(
    user: AdminUser,
    *,
    tenant_labels: dict[str, str],
    teacher_labels: dict[str, str],
) -> dict[str, Any]:
    """One ``DataTable`` row: display text, the resolved picker labels and this user's actions."""
    return {
        "username": user.display_name,
        "email": user.email or UNRESOLVED_CELL,
        "tenant": tenant_labels.get(user.tenant_id or "", UNRESOLVED_CELL),
        "teacher": teacher_labels.get(user.teacher_id or "", UNRESOLVED_CELL),
        "roles": ", ".join(user.roles) or UNRESOLVED_CELL,
        "enabled": "yes" if user.enabled else "no",
        ROW_ACTIONS_COLUMN_KEY: _row_actions(user),
    }


def _row_actions(user: AdminUser) -> Row | str:
    """This row's Edit and Roles buttons — the list's own route to the user's two pages.

    prefab's ``DataTable`` renders a cell whose value is a component, so the actions live in the
    row itself and the table stays the one list of users (B11's finding 2). They are built inside
    ``defer()``: the cell owns this tree, not the page around the table.
    """
    if not user.id:
        return UNRESOLVED_CELL
    with defer(), Row(gap=2) as actions:
        Button(
            "Edit",
            variant="outline",
            on_click=CallHandler(
                NAVIGATE_HANDLER,
                arguments={NAVIGATE_URL_ARGUMENT: admin_user_edit_path(user.id)},
            ),
        )
        Button(
            "Roles",
            variant="outline",
            on_click=CallHandler(
                NAVIGATE_HANDLER,
                arguments={NAVIGATE_URL_ARGUMENT: admin_user_roles_path(user.id)},
            ),
        )
    return actions


def _template(expression: str) -> str:
    """A prefab template for ``expression`` — ``{{ expression }}`` (the B8 helper, same rule)."""
    return "{{ " + expression + " }}"
