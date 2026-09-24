"""The prefab sign-in surface — the password form, the passkey handoff and the in-tab handoff
(spec §5.2, §5.3, §5.4, §14).

One page, three things a browser needs:

* a **model-driven form** (`Form.from_model`) whose fields come from :class:`LoginFormModel`, so
  the credential input's type, label and constraints have exactly one definition;
* the **reactive `Field`/`FieldError`** pattern on each field (prefab's documented validation
  display), gated on a **submit-driven** client state, so a username/password left blank is
  explained where the input is — but only after the user has actually submitted, and only ever by
  this one layer (the failure card's alert is the other);
* the **"Sign in with passkey"** button, which hands off to the auth service's D16 relying-party
  ceremony (the portal has no OIDC client, so no code or token can land in Python — AC12).

**Submission.** The form posts JSON to *this portal's* `POST /login` with the built-in
`Fetch.post` action — never to the auth service, never to Keycloak. The route answers with HTTP
200 and `{"redirect_uri": "<where the browser must go next>"}` (the validated `return_uri` plus
the one-time code on success; the portal's own error-card page on a refusal), and the renderer
then moves the browser **in the tab the user started in**.

Why the JSON hand-off instead of a 302: `Fetch` follows redirects and has no redirect option, so
a 302 to the app callback would be fetched as a cross-origin request — no cookies, `Set-Cookie`
dropped, CORS-blocked — consuming the one-time code without signing anyone in. The in-tab
navigation is performed by one registered renderer handler (`js_actions`), which both the form
and the passkey button use so there is a single navigation mechanism on the page.

**Every value that reaches the browser's address bar is server-validated first**: the
`return_uri` is checked against `AuthPortal:AppCallbackPrefixes` before this tree renders, and
the failure body is a same-origin error page.
"""

from __future__ import annotations

from pydantic import BaseModel, SecretStr
from pydantic import Field as ModelField
from prefab_ui.actions import Action, CallHandler, Fetch, SetState, ShowToast
from prefab_ui.app import PrefabApp
from prefab_ui.components import (
    Alert,
    AlertDescription,
    AlertTitle,
    Button,
    Card,
    CardContent,
    Column,
    Field,
    FieldError,
    Form,
    H3,
    Muted,
    Separator,
    defer,
    insert,
)
from prefab_ui.rx import Rx

from views.errors import card_copy

#: The portal's own login route. The form's `Fetch.post` target and the FastAPI route in
#: ``app.py`` are one contract, so the path is defined once — here — and the route imports it.
LOGIN_PATH = "/login"

#: The JSON body field names `POST /login` parses (app.py reads exactly these).
USERNAME_FIELD = "username"
PASSWORD_FIELD = "password"
ANTIFORGERY_FIELD = "antiforgery_token"
RETURN_URI_FIELD = "return_uri"

#: The one renderer JS action that navigates the current tab. prefab-ui 0.20.2's built-in
#: actions cover state, toasts and fetch, and its only navigation is `openLink` — which opens a
#: *new* tab, leaving the tab the user started in without the session this flow just created.
NAVIGATE_HANDLER = "navigate"
NAVIGATE_HANDLER_JS = "(args) => { window.location.assign(args.arguments.url); }"

#: The argument name the handler reads (`CallHandler.arguments`).
NAVIGATE_URL_ARGUMENT = "url"

#: The client-state key that makes the credential fields' error state **submit-driven**.
#:
#: It is part of the page's initial state (``False``) and is set by the submit action chain and by
#: nothing else, so a form the user has not submitted renders with no error state at all — and the
#: failure card a refused submit navigates to is a fresh render, which starts it ``False`` again.
#: That is what keeps the alert and the field errors from ever competing: after a refused submit
#: the card shows the alert alone, because no field has been marked attempted on that page.
#:
#: The initial value is load-bearing: prefab's renderer resolves a whole-attribute template to
#: `undefined` and then falls back to the raw template string, which is truthy — so an `invalid`
#: expression reading an undeclared key would paint the field as invalid on first paint.
ATTEMPTED_STATE = "attempted"


class LoginFormModel(BaseModel):
    """The credential form's fields and their constraints (spec §5.2).

    ``SecretStr`` is what makes ``Form.from_model`` emit ``Input(input_type="password")``, so the
    credential can never render as a plain text field; ``min_length`` becomes the input's own
    constraint and the form model's fields are the single definition of both labels and bounds.
    """

    username: str = ModelField(
        title="Username",
        description="School-Collab username",
        min_length=1,
        max_length=256,
    )
    password: SecretStr = ModelField(
        title="Password",
        description="School-Collab password",
        min_length=1,
        max_length=256,
    )


def build_login_view(
    *,
    antiforgery_token: str,
    return_uri: str,
    passkey_login_url: str | None,
    error_code: str | None = None,
) -> PrefabApp:
    """The sign-in page for one render.

    ``antiforgery_token`` is the one-time token the route just issued and stored server-side;
    it travels into the submit body, so the server can require the exact page it handed out.
    ``error_code`` is a bounded failure code (see ``views.errors.card_copy``) rendered as an
    alert above the form — the card a refused or failed submit lands on. On that render the alert
    is the **only** error layer: the credential fields' error state is submit-driven, and a fresh
    render carries no submit attempt.
    """
    with PrefabApp(
        title="Sign in - School-Collab",
        css_class="p-6",
        state={ATTEMPTED_STATE: False},
        js_actions={NAVIGATE_HANDLER: NAVIGATE_HANDLER_JS},
    ) as application:
        with Column(gap=4):
            H3("Sign in to School-Collab")
            Muted(
                "Your credentials go to the auth service through this portal, which holds no "
                "token of yours in return."
            )
            if error_code is not None:
                _notice(error_code)
            with Card():
                with CardContent():
                    with Form(on_submit=_submit_action(antiforgery_token, return_uri)):
                        _credential_fields()
                        Button("Sign in")
            _passkey_section(passkey_login_url)
    return application


def _notice(error_code: str) -> None:
    """The alert a refused or failed sign-in renders above the form (AC10 at HTTP 200)."""
    title, message = card_copy(error_code)
    with Alert(variant="destructive"):
        AlertTitle(title)
        AlertDescription(message)


def _submit_action(antiforgery_token: str, return_uri: str) -> list[Action]:
    """The form's submit: mark the form attempted, then `Fetch.post` to the portal's own route.

    The renderer runs a list of actions in order and stops at the first failure, so the leading
    ``SetState`` has already flipped :data:`ATTEMPTED_STATE` by the time the request goes out:
    that is the moment the credential fields' ``invalid`` expressions can first become true.

    The body names the two inputs by the state keys the renderer binds them to, carries the
    one-time antiforgery token and the validated ``return_uri`` verbatim (the route sends the
    identical string to the auth service, which binds the handshake code to it).
    """
    return [
        SetState(ATTEMPTED_STATE, True),
        _login_request(antiforgery_token, return_uri),
    ]


def _login_request(antiforgery_token: str, return_uri: str) -> Fetch:
    """The chain's request: `Fetch.post` to this portal's own route, then navigate to its answer."""
    return Fetch.post(
        LOGIN_PATH,
        body={
            USERNAME_FIELD: _template(USERNAME_FIELD),
            PASSWORD_FIELD: _template(PASSWORD_FIELD),
            ANTIFORGERY_FIELD: antiforgery_token,
            RETURN_URI_FIELD: return_uri,
        },
        on_success=CallHandler(
            NAVIGATE_HANDLER,
            arguments={NAVIGATE_URL_ARGUMENT: _template("$result.redirect_uri")},
        ),
        on_error=ShowToast(_template("$error"), variant="error"),
    )


def _credential_fields() -> None:
    """The model's fields, each in a reactive `Field` with its `FieldError`.

    ``Form.from_model(..., fields_only=True)`` generates the labeled inputs (label, placeholder,
    constraints, password type) from :class:`LoginFormModel`; they are generated detached so each
    one can be adopted by a ``Field``, whose reactive ``invalid`` expression turns the label and
    input red and reveals the ``FieldError`` — but only for a field left blank on a **submit
    attempt** (:data:`ATTEMPTED_STATE`), never merely because the input is empty.
    """
    with defer():
        generated = Form.from_model(LoginFormModel, fields_only=True)

    for name, component in zip(LoginFormModel.model_fields, generated, strict=True):
        with Field(invalid=Rx(f"{ATTEMPTED_STATE} && !{name}")):
            insert(component)
            FieldError(_field_error_text(name))


def _field_error_text(name: str) -> str:
    """The `FieldError` message for a field left blank on a submit attempt, named from the model."""
    title = LoginFormModel.model_fields[name].title or name
    return f"Enter your {title.lower()}."


def _passkey_section(passkey_login_url: str | None) -> None:
    """The D16 passkey handoff (spec §5.3) — or, without the portal's own base URL, why not.

    The button hands off to the auth service's ceremony with *this portal's* bootstrap URL as the
    ``app_redirect_uri``: it is the one portal URL the auth service's allowlist carries, and a
    target sharing the bootstrap URL's origin is recognised there as the portal's own sign-in, so
    no D6 continuation is issued for it. Without a configured base URL there is no allowlistable
    target to hand off to, so the button is omitted rather than pointed at a URL that must fail.
    """
    if passkey_login_url is None:
        Muted(
            "Passkey sign-in is unavailable: this portal's own base URL "
            "(AuthPortal:PublicBaseUrl) is not configured."
        )
        return

    Separator()
    Button(
        "Sign in with passkey",
        variant="outline",
        on_click=CallHandler(
            NAVIGATE_HANDLER, arguments={NAVIGATE_URL_ARGUMENT: passkey_login_url}
        ),
    )
    Muted(
        "Passkeys run in Keycloak's own page: this portal never sees the credential, and the "
        "auth service completes the ceremony."
    )


def _template(expression: str) -> str:
    """A prefab template for ``expression`` — ``{{ expression }}``.

    The renderer resolves it against the page's component state and the action context
    (``$result``/``$error``), which is how the submit body reads the inputs and how the success
    action learns where to navigate.
    """
    return "{{ " + expression + " }}"
