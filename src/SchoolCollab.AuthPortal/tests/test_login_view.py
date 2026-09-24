"""The prefab sign-in surface's own contract: the form model, the reactive fields and the two
actions the page hands the renderer (B8; spec §5.2/§5.3, §14; the submit-driven error state is
B11's finding 1).

These assertions are made against the **tree the renderer receives** — the ``prefab:initial-data``
JSON the page embeds — not against Python objects, so a component's serialized field names
(``onSubmit``, ``inputType``, ``invalid``, ``inputType=password``) are what is pinned.
"""

from __future__ import annotations

import json
import re
from collections.abc import Iterator
from typing import Any

from pydantic import SecretStr

from views.errors import card_copy
from views.login import (
    ANTIFORGERY_FIELD,
    ATTEMPTED_STATE,
    LOGIN_PATH,
    NAVIGATE_HANDLER,
    PASSWORD_FIELD,
    RETURN_URI_FIELD,
    USERNAME_FIELD,
    LoginFormModel,
    build_login_view,
)

CALLBACK = "http://localhost:5300/signin-handshake?ReturnUrl=%2Fadmin"
TOKEN = "antiforgery-token-1"
PASSKEY_URL = (
    "http://localhost:5301/auth/passkey/login"
    "?app_redirect_uri=http%3A%2F%2Flocalhost%3A5400%2Fbootstrap"
)

_DATA_PATTERN = re.compile(
    r'<script id="prefab:initial-data" type="application/json">(.*?)</script>', re.S
)


def _page(**overrides: Any) -> str:
    arguments: dict[str, Any] = {
        "antiforgery_token": TOKEN,
        "return_uri": CALLBACK,
        "passkey_login_url": PASSKEY_URL,
    }
    arguments.update(overrides)
    return build_login_view(**arguments).html()


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
    return [
        component
        for component in _components(_wire(page))
        if component["type"] == component_type
    ]


def _credential_field_expressions(page: str) -> list[str]:
    """The two `Field.invalid` expressions, sorted — evaluated by the renderer, not by Python."""
    return sorted(component["invalid"] for component in _of_type(page, "Field"))


#: What the renderer evaluates each field's `invalid` expression to on a page the user has not
#: submitted: `ATTEMPTED_STATE` is declared `False` in the page's own initial state.
_UNSUBMITTED_FIELD_EXPRESSIONS = sorted(
    f"{{{{ {ATTEMPTED_STATE} && !{name} }}}}" for name in (USERNAME_FIELD, PASSWORD_FIELD)
)


def test_login_form_model_pins_the_credential_constraints() -> None:
    fields = LoginFormModel.model_fields

    assert set(fields) == {USERNAME_FIELD, PASSWORD_FIELD}
    assert fields[USERNAME_FIELD].is_required()
    assert fields[PASSWORD_FIELD].is_required()
    # SecretStr is what makes Form.from_model emit a password input rather than a text field.
    assert fields[PASSWORD_FIELD].annotation is SecretStr
    # The bounds the inputs carry come from the model, so the model is the one definition.
    assert fields[USERNAME_FIELD].metadata and fields[PASSWORD_FIELD].metadata


def test_login_form_renders_a_password_input_with_reactive_field_errors() -> None:
    page = _page()

    password_inputs = [
        component
        for component in _of_type(page, "Input")
        if component["inputType"] == "password"
    ]
    assert len(password_inputs) == 1
    assert password_inputs[0]["name"] == PASSWORD_FIELD
    assert password_inputs[0]["required"] is True
    assert password_inputs[0]["minLength"] == 1
    assert password_inputs[0]["maxLength"] == 256

    # One reactive Field per credential, each with the FieldError the renderer reveals when the
    # field's invalid expression holds — the label/control/error grouping prefab documents.
    assert _credential_field_expressions(page) == _UNSUBMITTED_FIELD_EXPRESSIONS
    assert len(_of_type(page, "FieldError")) == 2


def test_login_form_shows_no_error_state_on_a_page_the_user_has_not_submitted() -> None:
    page = _page()

    # The submit gate is part of the page, and it starts off: the renderer resolves each field's
    # `invalid` expression against this state, so a blank input is not an error yet (B11).
    assert _wire(page)["state"] == {ATTEMPTED_STATE: False}
    # Nothing is invalid merely because the bound state is empty — every field reads the gate.
    assert _credential_field_expressions(page) == _UNSUBMITTED_FIELD_EXPRESSIONS
    assert len(_of_type(page, "FieldError")) == 2


def test_login_form_marks_the_submit_attempt_before_it_posts() -> None:
    page = _page()

    submit = _of_type(page, "Form")[0]["onSubmit"]

    # The renderer runs a list of actions in order: the gate flips first, so the fields can only
    # become invalid on an actual submit attempt — the other half of the clean first paint.
    assert submit[0] == {"action": "setState", "key": ATTEMPTED_STATE, "value": True}
    assert submit[1]["action"] == "fetch"


def test_login_form_submits_json_to_the_portals_own_route_with_the_one_time_token() -> None:
    page = _page()

    forms = _of_type(page, "Form")
    assert len(forms) == 1
    # `onSubmit` is the submit chain: the leading SetState is pinned by its own test above, so
    # this one reads the request that follows it in the chain.
    submit = forms[0]["onSubmit"][1]

    assert submit["action"] == "fetch"
    assert submit["method"] == "POST"
    # The portal's own route, never the auth service and never Keycloak (D6/D7).
    assert submit["url"] == LOGIN_PATH
    assert submit["body"] == {
        USERNAME_FIELD: f"{{{{ {USERNAME_FIELD} }}}}",
        PASSWORD_FIELD: f"{{{{ {PASSWORD_FIELD} }}}}",
        ANTIFORGERY_FIELD: TOKEN,
        RETURN_URI_FIELD: CALLBACK,
    }
    # The response's redirect_uri is what moves the browser: `Fetch` follows redirects, so the
    # route answers 200 JSON and this handler navigates the current tab.
    assert submit["onSuccess"] == {
        "action": "callHandler",
        "handler": NAVIGATE_HANDLER,
        "arguments": {"url": "{{ $result.redirect_uri }}"},
    }
    assert submit["onError"]["action"] == "showToast"


def test_login_page_registers_the_single_in_tab_navigation_handler() -> None:
    page = _page()

    assert NAVIGATE_HANDLER in page, "the renderer needs the handler the actions name"
    assert "window.location.assign" in page


def test_login_page_hands_the_passkey_button_to_the_auth_service_handoff() -> None:
    page = _page()

    passkey_buttons = [
        component
        for component in _of_type(page, "Button")
        if component["label"] == "Sign in with passkey"
    ]
    assert len(passkey_buttons) == 1
    assert passkey_buttons[0]["onClick"] == {
        "action": "callHandler",
        "handler": NAVIGATE_HANDLER,
        "arguments": {"url": PASSKEY_URL},
    }


def test_login_page_omits_the_passkey_button_without_the_portals_base_url() -> None:
    page = _page(passkey_login_url=None)

    assert "Sign in with passkey" not in page
    assert "AuthPortal:PublicBaseUrl" in page, "the page says why passkey sign-in is unavailable"


def test_login_page_renders_the_bounded_failure_copy_for_a_refused_submit() -> None:
    page = _page(error_code="invalid_credentials")

    title, message = card_copy("invalid_credentials")
    alerts = _of_type(page, "Alert")
    assert len(alerts) == 1
    assert alerts[0]["variant"] == "destructive"
    assert [component["content"] for component in _of_type(page, "AlertTitle")] == [title]
    assert [component["content"] for component in _of_type(page, "AlertDescription")] == [message]


def test_login_page_shows_the_alert_alone_after_a_failed_submit() -> None:
    page = _page(error_code="invalid_credentials")

    # The card a refused submit lands on is a **fresh render**: the submit gate is off again, so
    # the credential fields carry no error state and the alert is the page's single error layer
    # (B11 — the two layers must never show at once).
    assert _wire(page)["state"] == {ATTEMPTED_STATE: False}
    assert _credential_field_expressions(page) == _UNSUBMITTED_FIELD_EXPRESSIONS
    assert len(_of_type(page, "Alert")) == 1


def test_login_page_never_renders_an_unknown_error_code() -> None:
    page = _page(error_code="<script>alert(1)</script>")

    # The code is a dictionary key, never page content: an unknown one degrades to the generic
    # copy instead of being echoed.
    assert "alert(1)" not in page
    title, message = card_copy(None)
    assert [component["content"] for component in _of_type(page, "AlertTitle")] == [title]
    assert [component["content"] for component in _of_type(page, "AlertDescription")] == [message]
