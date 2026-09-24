"""The admin surface's contract: the gate, the mediated pickers and the prefab trees (B9).

Two halves, the same way B8's tests split:

* **route/gate** assertions drive the FastAPI app with ``TestClient`` and
  ``app.dependency_overrides`` (no container, no Docker, no Keycloak). The stub records every
  request, because the wire contract *is* the assertion: the ``X-Portal-Session`` header, the
  auth-service path each read goes to (the mediated pickers — never settings-api or students-api),
  and the bodies the mutations send.
* **view** assertions read the ``prefab:initial-data`` tree the renderer receives, so a component's
  serialized field names (``onSubmit``, ``inputType``, ``name``, ``value``) are what is pinned.
"""

from __future__ import annotations

import json
import re
from collections.abc import Iterator
from typing import Any

import httpx
import pytest
from fastapi import Depends, Request
from fastapi.testclient import TestClient

import app as portal_app
from api import AuthApiClient, AuthServiceEndpoint, SessionData
from api.admin_api_client import (
    SESSION_HEADER_NAME,
    AdminApiClient,
    AdminRole,
    AdminUser,
    PickerTeacher,
    PickerTenant,
)
from views.errors import card_copy
from views.admin_users import (
    ADMIN_USER_EDIT_PATH,
    ADMIN_USER_ROLES_PATH,
    ADMIN_USERS_PATH,
    ADMIN_ROLE_REQUIRED_CODE,
    ADMIN_SIGN_IN_REQUIRED_CODE,
    MODEL_CREDENTIAL_FIELD,
    MODEL_ENABLED_FIELD,
    MODEL_TENANT_NAME_FIELD,
    MODEL_TENANT_TYPE_FIELD,
    MODEL_USERNAME_FIELD,
    ROW_ACTIONS_COLUMN_KEY,
    TEACHER_FIELD,
    TENANT_FIELD,
    UNRESOLVED_CELL,
    USER_ID_FIELD,
    admin_card_copy,
    build_users_view,
)

STUB_ENDPOINT = AuthServiceEndpoint(service="auth", base_url="http://auth.test", env_var="test")

SESSION_ID = "session-9"
ADMIN_SESSION = {
    "sessionId": SESSION_ID,
    "tenantId": "tenant-1",
    "tenantName": "Demo",
    "tenantType": "school",
    "teacherId": "teacher-1",
    "roles": ["user-admin"],
}
NON_ADMIN_SESSION = {**ADMIN_SESSION, "roles": ["platform-admin"]}

USER_ID = "user-1"
EDIT_PATH = f"/admin/users/{USER_ID}/edit"
ROLES_PATH = f"/admin/users/{USER_ID}/roles"

USERS_PAYLOAD = [
    {
        "id": USER_ID,
        "username": "teacher-7",
        "email": "teacher-7@example.test",
        "enabled": True,
        "attributes": {"tenant_id": ["tenant-1"], "teacher_id": ["teacher-1"]},
    },
    {"id": "user-2", "username": "teacher-8", "enabled": False},
]
TENANTS_PAYLOAD = [{"id": "tenant-1", "name": "Demo", "type": "school"}]
TEACHERS_PAYLOAD = [
    {"id": "teacher-1", "firstName": "Ada", "lastName": "Lovelace", "displayName": "Ada L."}
]
REALM_ROLES_PAYLOAD = [
    {"id": "role-1", "name": "user-admin"},
    {"id": "role-2", "name": "platform-admin"},
]

_DATA_PATTERN = re.compile(
    r'<script id="prefab:initial-data" type="application/json">(.*?)</script>', re.S
)


@pytest.fixture(autouse=True)
def _auth_service_environment(monkeypatch: pytest.MonkeyPatch) -> None:
    """The AppHost's fan-out for this portal (B3): the auth-service discovery variable.

    Without it every admin route would degrade on discovery, and the tests would be pinning the
    discovery card instead of the gate and the reads they name.
    """
    monkeypatch.setenv("services__auth__http__0", STUB_ENDPOINT.base_url)


def _json_response(payload: object, status_code: int = 200) -> httpx.Response:
    return httpx.Response(
        status_code, json=payload, headers={"content-type": "application/json"}
    )


def _empty_response(status_code: int = 204) -> httpx.Response:
    return httpx.Response(status_code)


class AuthStub:
    """A scripted auth service that records every request it receives.

    The session read is answered from ``session`` (``None`` means "this test expects no session
    read at all" — the handler raises if one happens, so a gate that skipped its check cannot pass
    by luck). Unscripted paths answer an empty list, which is a valid empty picker/role read.
    """

    def __init__(
        self,
        *,
        session: dict[str, Any] | None = ADMIN_SESSION,
        session_status: int = 200,
        raise_transport_error: bool = False,
    ) -> None:
        self.session = session
        self.session_status = session_status
        self.raise_transport_error = raise_transport_error
        self.seen: list[httpx.Request] = []
        self._responses: dict[tuple[str, str], httpx.Response] = {}

    def respond(self, method: str, path: str, payload: object, status_code: int = 200) -> AuthStub:
        self._responses[(method, path)] = _json_response(payload, status_code)
        return self

    def respond_empty(self, method: str, path: str, status_code: int = 204) -> AuthStub:
        self._responses[(method, path)] = _empty_response(status_code)
        return self

    def handler(self, request: httpx.Request) -> httpx.Response:
        self.seen.append(request)
        if self.raise_transport_error:
            raise httpx.ConnectError("auth service is down", request=request)

        if request.url.path.startswith("/auth/session/"):
            assert self.session is not None, "this test expects the gate to make no session read"
            return _json_response(self.session, self.session_status)

        return self._responses.get((request.method, request.url.path)) or _json_response([])

    @property
    def calls(self) -> list[tuple[str, str]]:
        """Every recorded request as ``(method, path)`` — the wire contract, in order."""
        return [(request.method, request.url.path) for request in self.seen]


def _override(stub: AuthStub):
    """The stub that answers the gate's session read (B8's seam, the same shape)."""

    async def override() -> AuthApiClient:
        return AuthApiClient(
            httpx.AsyncClient(transport=httpx.MockTransport(stub.handler)), STUB_ENDPOINT
        )

    return override


def _admin_override(stub: AuthStub):
    """The stub that answers the admin reads, behind the **real** gate (D12/D8).

    The override keeps ``Depends(require_admin_session)`` in its signature on purpose: it replaces
    only how the admin client reaches the auth service, not the gate. The session id it presents
    comes from the request's cookie, exactly as the production dependency reads it.
    """

    async def override(
        request: Request, _session: SessionData = Depends(portal_app.require_admin_session)
    ) -> AdminApiClient:
        return AdminApiClient(
            httpx.AsyncClient(transport=httpx.MockTransport(stub.handler)),
            STUB_ENDPOINT,
            request.cookies.get(portal_app.SESSION_COOKIE_NAME, ""),
        )

    return override


def _get(client: TestClient, path: str, **kwargs: Any) -> httpx.Response:
    """A request as a signed-in browser makes it: the opaque session cookie and nothing else."""
    headers = dict(kwargs.pop("headers", {}))
    headers.setdefault("cookie", f"{portal_app.SESSION_COOKIE_NAME}={SESSION_ID}")
    return client.get(path, headers=headers, **kwargs)


def _admin_session(**extra: str) -> dict[str, str]:
    """The cookie header a mutation's browser sends (plus any explicit extra header)."""
    headers = {"cookie": f"{portal_app.SESSION_COOKIE_NAME}={SESSION_ID}"}
    headers.update(extra)
    return headers


def _users_stub(**kwargs: Any) -> AuthStub:
    """A stub serving B9's reads: the user list and both mediated picker reads."""
    return (
        AuthStub(**kwargs)
        .respond("GET", "/auth/admin/users", USERS_PAYLOAD)
        .respond("GET", "/auth/pickers/tenants", TENANTS_PAYLOAD)
        .respond("GET", "/auth/pickers/teachers", TEACHERS_PAYLOAD)
    )


def _with_stub(stub: AuthStub, call):
    portal_app.app.dependency_overrides[portal_app.get_auth_api_client] = _override(stub)
    portal_app.app.dependency_overrides[portal_app.get_admin_api_client] = _admin_override(stub)
    try:
        with TestClient(portal_app.app) as client:
            return call(client)
    finally:
        portal_app.app.dependency_overrides.clear()


# ------------------------------------------------------------------------------------------------
# The gate: an authenticated session whose D18 read carries ``user-admin``
# ------------------------------------------------------------------------------------------------


def test_users_page_lists_users_and_fills_both_pickers_from_the_mediated_reads() -> None:
    stub = _users_stub()

    response = _with_stub(stub, lambda client: _get(client, ADMIN_USERS_PATH))

    assert response.status_code == 200
    # The reads, in order, all on the auth service: the session read (D18), the realm users, and
    # B7's two **mediated** picker endpoints. Nothing here reaches settings-api or students-api.
    assert stub.calls == [
        ("GET", f"/auth/session/{SESSION_ID}"),
        ("GET", "/auth/admin/users"),
        ("GET", "/auth/pickers/tenants"),
        ("GET", "/auth/pickers/teachers"),
    ]
    assert {request.url.host for request in stub.seen} == {"auth.test"}
    # D12: the portal's only identity material is its opaque session id, presented as a header on
    # every admin and picker call. The session read itself carries it in the path instead (B5's
    # session contract), so the first request legitimately has no header.
    assert [request.headers.get(SESSION_HEADER_NAME) for request in stub.seen] == [
        None,
        SESSION_ID,
        SESSION_ID,
        SESSION_ID,
    ]
    assert "teacher-7" in response.text and "teacher-8" in response.text


def test_users_page_requires_a_session_and_never_calls_the_auth_service() -> None:
    stub = _users_stub(session=None)

    response = _with_stub(stub, lambda client: client.get(ADMIN_USERS_PATH))

    # A page at HTTP 200, never a redirect (the portal's own /login would refuse a
    # return_uri of /admin/users, so a redirect here would be a loop) and never a 403.
    assert response.status_code == 200
    assert ADMIN_SIGN_IN_REQUIRED_CODE in response.text
    assert stub.calls == []


def test_users_page_refuses_a_session_without_the_admin_role() -> None:
    stub = _users_stub(session=NON_ADMIN_SESSION)

    response = _with_stub(stub, lambda client: _get(client, ADMIN_USERS_PATH))

    assert response.status_code == 200
    title, message = admin_card_copy(ADMIN_ROLE_REQUIRED_CODE)
    assert title in response.text and message in response.text
    # The role comes from the session read, and the refusal is *before* any admin call: the auth
    # service's own gate (B5) is never even asked, so the user sees a card and not a 403 page.
    assert stub.calls == [("GET", f"/auth/session/{SESSION_ID}")]
    assert "teacher-7" not in response.text


def test_roles_page_is_gated_by_the_same_session_check() -> None:
    stub = AuthStub(session=NON_ADMIN_SESSION).respond("GET", "/auth/admin/roles", REALM_ROLES_PAYLOAD)

    response = _with_stub(stub, lambda client: _get(client, ROLES_PATH))

    assert response.status_code == 200
    assert ADMIN_ROLE_REQUIRED_CODE in response.text
    assert stub.calls == [("GET", f"/auth/session/{SESSION_ID}")]


def test_admin_page_clears_the_cookie_when_the_session_ended() -> None:
    stub = _users_stub(
        session={"error": "session_ended"}, session_status=410
    )

    response = _with_stub(stub, lambda client: _get(client, ADMIN_USERS_PATH))

    assert response.status_code == 200
    assert "Session ended" in response.text  # views/errors.py's copy, shared with the sign-in surface
    cleared = response.headers.get("set-cookie", "")
    assert f'{portal_app.SESSION_COOKIE_NAME}=""' in cleared
    # No admin read was attempted with a dead session.
    assert stub.calls == [("GET", f"/auth/session/{SESSION_ID}")]


def test_users_page_renders_a_card_when_the_auth_service_is_unreachable() -> None:
    stub = AuthStub(raise_transport_error=True)

    response = _with_stub(stub, lambda client: _get(client, ADMIN_USERS_PATH))

    assert response.status_code == 200  # a page, never a 500 (AC10)
    assert "Sign-in unavailable" in response.text  # views/errors.py's generic copy
    assert "unreachable" in response.text
    # The gate's own session read was attempted and failed at the transport (the stub records the
    # attempt); no admin call followed it.
    assert stub.calls == [("GET", f"/auth/session/{SESSION_ID}")]


def test_a_degraded_admin_read_renders_the_card_at_200() -> None:
    stub = _users_stub().respond(
        "GET",
        "/auth/admin/users",
        {"error": "upstream_unreachable", "detail": "the admin call could not be completed"},
        502,
    )

    response = _with_stub(stub, lambda client: _get(client, ADMIN_USERS_PATH))

    assert response.status_code == 200
    assert "Identity provider unavailable" in response.text
    assert "teacher-7" not in response.text  # nothing was read, so nothing is listed


def test_a_token_shaped_admin_read_is_refused_and_nothing_of_it_is_rendered() -> None:
    stub = _users_stub().respond(
        "GET", "/auth/admin/users", {"users": [{"username": "t", "accessToken": "leak"}]}
    )

    response = _with_stub(stub, lambda client: _get(client, ADMIN_USERS_PATH))

    assert response.status_code == 200
    # AC11's tripwire: the client refuses the body rather than parsing a token into Python state,
    # and no value from it reaches the page (the *name* of the refused field is the client's own
    # bounded diagnostic, B2's message).
    assert "leak" not in response.text
    assert "unusable_response" in response.text


# ------------------------------------------------------------------------------------------------
# The views: the form the renderer receives
# ------------------------------------------------------------------------------------------------


def _wire(page: str) -> dict[str, Any]:
    match = _DATA_PATTERN.search(page)
    assert match is not None, "the page must embed the prefab tree it renders"
    return json.loads(match.group(1).replace("\\/", "/"))


def _components(node: Any) -> Iterator[dict[str, Any]]:
    if isinstance(node, dict):
        if isinstance(node.get("type"), str):
            yield node
        for value in node.values():
            yield from _components(value)
    elif isinstance(node, list):
        for item in node:
            yield from _components(item)


def _of_type(page: str, component_type: str) -> list[dict[str, Any]]:
    return [component for component in _components(_wire(page)) if component["type"] == component_type]


def _buttons_in(node: Any) -> dict[str, dict[str, Any]]:
    """The buttons anywhere inside one wire node, keyed by label (a row's actions cell, say)."""
    return {
        component["label"]: component
        for component in _components(node)
        if component["type"] == "Button"
    }


def _table_row(page: str, index: int = 0) -> dict[str, Any]:
    return _of_type(page, "DataTable")[0]["rows"][index]


def _users_page() -> str:
    return build_users_view(
        users=[
            AdminUser(
                id=USER_ID,
                username="teacher-7",
                email="teacher-7@example.test",
                enabled=True,
                tenant_id="tenant-1",
                teacher_id="teacher-1",
            )
        ],
        tenants=[PickerTenant(id="tenant-1", name="Demo", type="school")],
        teachers=[PickerTeacher(id="teacher-1", first_name="Ada", last_name="Lovelace")],
    ).html()


def test_create_form_is_model_driven_and_posts_to_the_portals_own_route() -> None:
    page = _users_page()

    forms = _of_type(page, "Form")
    assert len(forms) == 1
    submit = forms[0]["onSubmit"]

    # The portal's own route — never the auth service and never Keycloak (D6/D7/AC11).
    assert submit["url"] == ADMIN_USERS_PATH
    assert submit["method"] == "POST"
    assert submit["body"] == {
        MODEL_USERNAME_FIELD: "{{ username }}",
        MODEL_CREDENTIAL_FIELD: "{{ initial_password }}",
        MODEL_TENANT_NAME_FIELD: "{{ tenant_name }}",
        MODEL_TENANT_TYPE_FIELD: "{{ tenant_type }}",
        MODEL_ENABLED_FIELD: "{{ enabled }}",
        "email": "{{ email }}",
        TENANT_FIELD: "{{ tenant_id }}",
        TEACHER_FIELD: "{{ teacher_id }}",
    }
    # 200 + redirect_uri, moved in-tab by the navigation handler the sign-in surface registers.
    assert submit["onSuccess"] == {
        "action": "callHandler",
        "handler": "navigate",
        "arguments": {"url": "{{ $result.redirect_uri }}"},
    }


def test_create_form_carries_a_password_input_a_tenant_select_and_a_teacher_combobox() -> None:
    page = _users_page()

    passwords = [
        component for component in _of_type(page, "Input") if component["inputType"] == "password"
    ]
    assert len(passwords) == 1
    assert passwords[0]["name"] == MODEL_CREDENTIAL_FIELD
    # The claim-attribute inputs the D10 editor needs, model-generated like every other field.
    assert {component["name"] for component in _of_type(page, "Input")} == {
        MODEL_USERNAME_FIELD,
        MODEL_CREDENTIAL_FIELD,
        "email",
        MODEL_TENANT_NAME_FIELD,
        MODEL_TENANT_TYPE_FIELD,
    }

    selects = _of_type(page, "Select")
    assert [select["name"] for select in selects] == [TENANT_FIELD]
    assert [option["value"] for option in _of_type(page, "SelectOption")] == ["tenant-1"]

    comboboxes = _of_type(page, "Combobox")
    assert [combobox["name"] for combobox in comboboxes] == [TEACHER_FIELD]
    assert [option["value"] for option in _of_type(page, "ComboboxOption")] == ["teacher-1"]


def test_users_table_lists_the_realm_users_with_their_row_actions_and_omits_the_create_form_without_pickers() -> None:
    page = _users_page()

    table = _of_type(page, "DataTable")[0]
    assert [column["key"] for column in table["columns"]] == [
        "username",
        "email",
        "tenant",
        "teacher",
        "roles",
        "enabled",
        ROW_ACTIONS_COLUMN_KEY,
    ]
    assert [row["username"] for row in table["rows"]] == ["teacher-7"]

    # D8 needs an existing tenant AND an existing teacher row: with a picker empty the form is not
    # rendered at all, rather than rendered with an empty picker that could submit a half-bound user.
    empty = build_users_view(users=[], tenants=[], teachers=[]).html()
    assert _of_type(empty, "Form") == []
    assert "tenants and teachers picker read returned no rows" in empty


def test_users_list_reaches_each_user_once_through_the_tables_own_row_actions() -> None:
    page = _users_page()

    # One list of users (B11: the second per-user "Manage" card is gone), and its rows carry the
    # two navigation actions — prefab's DataTable renders a cell whose value is a component.
    assert len(_of_type(page, "DataTable")) == 1
    assert [component["content"] for component in _of_type(page, "H3")] == [
        "Realm users",
        "Create a tenant user",
    ]

    actions = _buttons_in(_table_row(page)[ROW_ACTIONS_COLUMN_KEY])
    assert actions["Edit"]["onClick"] == {
        "action": "callHandler",
        "handler": "navigate",
        "arguments": {"url": ADMIN_USER_EDIT_PATH.format(user_id=USER_ID)},
    }
    assert actions["Roles"]["onClick"] == {
        "action": "callHandler",
        "handler": "navigate",
        "arguments": {"url": ADMIN_USER_ROLES_PATH.format(user_id=USER_ID)},
    }
    # The actions live in the row and nowhere else: the page carries exactly one Edit and one Roles
    # button for its one user.
    page_buttons = [
        component["label"] for component in _of_type(page, "Button")
    ]
    assert page_buttons.count("Edit") == 1 and page_buttons.count("Roles") == 1


def test_users_table_shows_the_picker_labels_rather_than_the_raw_claim_ids() -> None:
    page = _users_page()

    # The form's pickers and the list's cells name a tenant and a teacher the same way: through
    # the mediated picker rows' own labels (B11's finding 3, D17).
    row = _table_row(page)
    assert row["tenant"] == "Demo (school)"
    assert row["teacher"] == "Ada Lovelace"
    assert row["roles"] == UNRESOLVED_CELL


def test_users_table_shows_the_deliberate_em_dash_for_an_unresolved_picker_value() -> None:
    page = build_users_view(
        users=[
            AdminUser(id="user-3", username="teacher-9", tenant_id="tenant-gone"),
            AdminUser(id="user-4", username="teacher-10", teacher_id="teacher-anonymous"),
        ],
        tenants=[PickerTenant(id="tenant-1", name="Demo", type="school")],
        # A picker row with no readable name at all: `PickerTeacher.label` falls back to the row's
        # own id, and a cell must not print that id.
        teachers=[PickerTeacher(id="teacher-anonymous")],
    ).html()

    rows = [row for row in _of_type(page, "DataTable")[0]["rows"]]
    assert [row["tenant"] for row in rows] == [UNRESOLVED_CELL, UNRESOLVED_CELL]
    assert [row["teacher"] for row in rows] == [UNRESOLVED_CELL, UNRESOLVED_CELL]
    # No opaque id reaches the table's own cells (the actions cell names only this portal's pages).
    assert "tenant-gone" not in json.dumps(rows)
    assert "teacher-anonymous" not in json.dumps(rows)


def test_admin_card_copy_degrades_an_unknown_code_to_the_portals_generic_copy() -> None:
    title, message = admin_card_copy("<script>alert(1)</script>")

    assert "alert(1)" not in title and "alert(1)" not in message
    # An unknown code is a dictionary miss, never page content.
    assert (title, message) == card_copy(None)


# ------------------------------------------------------------------------------------------------
# The mutations: create, update and the role mappings
# ------------------------------------------------------------------------------------------------


def test_create_posts_the_representation_then_sets_the_initial_credential() -> None:
    stub = (
        AuthStub()
        .respond_empty("POST", "/auth/admin/users", status_code=201)
        # The create response names no user id (round A's contract), so the portal resolves the
        # created user by its exact username before it can set the credential.
        .respond(
            "GET",
            "/auth/admin/users",
            [
                {"id": "user-other", "username": "teacher-9-old"},
                {"id": USER_ID, "username": "teacher-9"},
            ],
        )
        .respond_empty("PUT", f"/auth/admin/users/{USER_ID}/reset-password")
    )

    response = _with_stub(
        stub,
        lambda client: client.post(
            ADMIN_USERS_PATH,
            headers=_admin_session(),
            json={
                MODEL_USERNAME_FIELD: "teacher-9",
                MODEL_CREDENTIAL_FIELD: "hunter2",
                "email": "teacher-9@example.test",
                MODEL_TENANT_NAME_FIELD: "Demo",
                MODEL_TENANT_TYPE_FIELD: "school",
                MODEL_ENABLED_FIELD: True,
                TENANT_FIELD: "tenant-1",
                TEACHER_FIELD: "teacher-1",
            },
        ),
    )

    assert response.status_code == 200
    assert response.json() == {"redirect_uri": f"{ADMIN_USERS_PATH}?notice=created"}
    # The credential never appears in the answer (AC11).
    assert "hunter2" not in response.text

    assert stub.calls == [
        ("GET", f"/auth/session/{SESSION_ID}"),
        ("POST", "/auth/admin/users"),
        ("GET", "/auth/admin/users"),
        ("PUT", f"/auth/admin/users/{USER_ID}/reset-password"),
    ]
    created = json.loads(stub.seen[1].content)
    # Identity-only creation (D8) with the D10 attributes in Keycloak's own array shape; the
    # credential is deliberately NOT part of the create representation.
    assert created == {
        "username": "teacher-9",
        "enabled": True,
        "email": "teacher-9@example.test",
        "attributes": {
            "tenant_id": ["tenant-1"],
            "tenant_name": ["Demo"],
            "tenant_type": ["school"],
            "teacher_id": ["teacher-1"],
        },
    }
    assert json.loads(stub.seen[3].content) == {"newPassword": "hunter2"}


def test_create_reports_a_user_that_could_not_be_resolved_for_its_credential() -> None:
    stub = (
        AuthStub()
        .respond_empty("POST", "/auth/admin/users", status_code=201)
        .respond("GET", "/auth/admin/users", [])  # the created user is not there to be resolved
    )

    response = _with_stub(
        stub,
        lambda client: client.post(
            ADMIN_USERS_PATH,
            headers=_admin_session(),
            json={MODEL_USERNAME_FIELD: "teacher-9", MODEL_CREDENTIAL_FIELD: "hunter2"},
        ),
    )

    # The realm user exists at this point, so the outcome says the credential was not set instead
    # of claiming a clean create.
    assert response.status_code == 200
    assert response.json() == {"redirect_uri": f"{ADMIN_USERS_PATH}?error=credential_not_set"}


def test_update_puts_the_full_representation_and_resets_the_credential_when_filled() -> None:
    stub = AuthStub().respond_empty("PUT", "/auth/admin/users").respond_empty(
        "PUT", f"/auth/admin/users/{USER_ID}/reset-password"
    )

    response = _with_stub(
        stub,
        lambda client: client.put(
            ADMIN_USERS_PATH,
            headers=_admin_session(),
            json={
                USER_ID_FIELD: USER_ID,
                MODEL_USERNAME_FIELD: "teacher-7",
                MODEL_CREDENTIAL_FIELD: "new-secret",
                MODEL_ENABLED_FIELD: False,
                TENANT_FIELD: "tenant-1",
                TEACHER_FIELD: "teacher-1",
            },
        ),
    )

    assert response.status_code == 200
    assert response.json() == {"redirect_uri": f"{ADMIN_USERS_PATH}?notice=updated"}
    # The auth service's PUT is a full-representation replace keyed on the id the body carries.
    assert json.loads(stub.seen[1].content) == {
        "id": USER_ID,
        "username": "teacher-7",
        "enabled": False,
        "attributes": {"tenant_id": ["tenant-1"], "teacher_id": ["teacher-1"]},
    }
    assert json.loads(stub.seen[2].content) == {"newPassword": "new-secret"}


def test_a_refused_admin_operation_answers_a_page_shaped_200_with_a_bounded_code() -> None:
    stub = AuthStub().respond(
        "POST",
        "/auth/admin/users",
        {"error": "forbidden", "detail": "realm-management role missing"},
        403,
    )

    response = _with_stub(
        stub,
        lambda client: client.post(
            ADMIN_USERS_PATH,
            headers=_admin_session(),
            json={MODEL_USERNAME_FIELD: "teacher-9"},
        ),
    )

    assert response.status_code == 200
    # Fail-closed and bounded: the portal's own page carries the code, never Keycloak's body and
    # never a 403 page from the auth service.
    assert response.json() == {"redirect_uri": f"{ADMIN_USERS_PATH}?error=forbidden"}
    assert "realm-management" not in response.text


def test_a_non_json_mutation_body_is_refused_before_any_upstream_call() -> None:
    stub = AuthStub()

    response = _with_stub(
        stub,
        lambda client: client.post(
            ADMIN_USERS_PATH,
            # A cross-site form POST cannot set application/json, and SameSite=Lax keeps the
            # cookie off it anyway; the media type is the cheap second half of that guard.
            headers=_admin_session(**{"content-type": "application/x-www-form-urlencoded"}),
            content="username=teacher-9",
        ),
    )

    assert response.status_code == 415
    assert response.json() == {"error": "unsupported_media_type"}
    assert stub.calls == [("GET", f"/auth/session/{SESSION_ID}")]


def test_roles_page_lists_the_existing_realm_roles_with_both_actions() -> None:
    stub = AuthStub().respond("GET", "/auth/admin/roles", REALM_ROLES_PAYLOAD)

    response = _with_stub(stub, lambda client: _get(client, ROLES_PATH))

    assert response.status_code == 200
    assert stub.calls == [
        ("GET", f"/auth/session/{SESSION_ID}"),
        ("GET", "/auth/admin/roles"),
    ]
    page = response.text
    assert "Role definitions are declared in the realm import" in page
    # One assign and one unassign action per role, both against this portal's own route.
    buttons = {component["label"]: component for component in _of_type(page, "Button")}
    assert buttons["Assign"]["onClick"]["url"] == ROLES_PATH
    assert buttons["Assign"]["onClick"]["method"] == "POST"
    assert buttons["Unassign"]["onClick"]["url"] == ROLES_PATH
    assert buttons["Unassign"]["onClick"]["method"] == "DELETE"


def test_assigning_a_role_posts_exactly_the_id_and_name_pair() -> None:
    stub = AuthStub().respond_empty("POST", f"/auth/admin/users/{USER_ID}/role-mappings")

    response = _with_stub(
        stub,
        lambda client: client.post(
            ROLES_PATH,
            headers=_admin_session(),
            json={"roles": [{"id": "role-1", "name": "user-admin"}]},
        ),
    )

    assert response.status_code == 200
    assert response.json() == {"redirect_uri": f"{ROLES_PATH}?notice=assigned"}
    assert json.loads(stub.seen[1].content) == {"roles": [{"id": "role-1", "name": "user-admin"}]}


def test_unassigning_a_role_uses_delete_with_the_same_pair() -> None:
    stub = AuthStub().respond_empty("DELETE", f"/auth/admin/users/{USER_ID}/role-mappings")

    response = _with_stub(
        stub,
        lambda client: client.request(
            "DELETE",
            ROLES_PATH,
            headers=_admin_session(),
            json={"roles": [{"id": "role-2", "name": "platform-admin"}]},
        ),
    )

    assert response.status_code == 200
    assert response.json() == {"redirect_uri": f"{ROLES_PATH}?notice=unassigned"}
    assert json.loads(stub.seen[1].content) == {
        "roles": [{"id": "role-2", "name": "platform-admin"}]
    }


def test_a_role_change_without_a_role_id_is_refused_before_any_upstream_call() -> None:
    stub = AuthStub()

    response = _with_stub(
        stub,
        lambda client: client.post(
            ROLES_PATH, headers=_admin_session(), json={"roles": [{"name": "user-admin"}]}
        ),
    )

    # The role-mappings endpoint keys on role ids (round A's contract): a name alone cannot be
    # mapped, so the call is refused rather than sent to fail upstream.
    assert response.status_code == 200
    assert response.json() == {"redirect_uri": f"{ROLES_PATH}?error=role_required"}
    assert stub.calls == [("GET", f"/auth/session/{SESSION_ID}")]
