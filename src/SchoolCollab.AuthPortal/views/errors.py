"""The auth portal's fail-closed error cards (AC10, D18 — spec §5.2 step 6, §5.4, §14).

Every degraded state is a Prefab page rendered at **HTTP 200**: the auth service or Keycloak
being down, a session whose refresh failed, a rejected ``return_uri``, a refused handoff code.
The portal never shows a raw 500, a traceback or a half-working page — and it never redirects a
browser to an address it could not validate.

Three named builders, one shared copy table:

* :func:`build_error_card` — the general AC10 card, keyed by a **bounded** failure code;
* :func:`build_session_ended_card` — the D18 card a failed token refresh gets (the portal clears
  its cookie and renders this instead of a page that would fail every call);
* :func:`build_invalid_return_uri_card` — the card a ``return_uri`` outside
  ``AuthPortal:AppCallbackPrefixes`` gets, so a crafted login link is a page and never an open
  redirect (spec §14, AC13).

The copy is keyed by code, never by upstream text: :func:`code_for_error` reduces a typed portal
error to one of this table's keys and everything else to ``unusable_response``, so an
unrecognized failure can never echo a body, a token or an attacker-supplied string into a page.

**These cards are for sign-in outcomes, not for malformed requests.** A request the portal refuses
to read at all — a non-JSON content type, an unparseable body, a missing or already-spent
antiforgery token — is answered by the route with a non-2xx status and a machine code (415/400)
instead, because there is no user-visible sign-in state to describe and no page to render for a
caller that is not the form.
"""

from __future__ import annotations

from prefab_ui.app import PrefabApp
from prefab_ui.components import Badge, Card, CardContent, Column, H3, Muted, Row

from api import ApiResponseError, AuthServiceError, PortalApiError

#: The card every unrecognized failure degrades to.
UNUSABLE_RESPONSE_CODE = "unusable_response"

#: The portal's own refusal of a ``return_uri`` outside its app-callback allowlist (spec §14).
RETURN_URI_NOT_ALLOWED_CODE = "return_uri_not_allowed"

#: The D18 code a failed token refresh surfaces as (``SessionEndpoints``, 410 Gone).
SESSION_ENDED_CODE = "session_ended"

#: Code -> (card title, card copy). One table, so a code and its wording cannot drift apart.
CARD_COPY: dict[str, tuple[str, str]] = {
    "invalid_credentials": (
        "Sign-in failed",
        "The username or password was not recognized.",
    ),
    "credentials_missing": (
        "Sign-in incomplete",
        "Enter both a username and a password.",
    ),
    "disabled_user": (
        "Account disabled",
        "This account is disabled. Ask an administrator to re-enable it.",
    ),
    "keycloak_unreachable": (
        "Identity provider unavailable",
        "Keycloak could not be reached, so nothing was signed in. Try again in a moment.",
    ),
    "upstream_unreachable": (
        "Identity provider unavailable",
        "Keycloak could not be reached, so nothing was signed in. Try again in a moment.",
    ),
    RETURN_URI_NOT_ALLOWED_CODE: (
        "Return address refused",
        "This portal signs in apps whose callback is on its allowlist, and this return address "
        "is not one of them. No sign-in code was issued - start again from the app you were "
        "signing in to.",
    ),
    "redirect_uri_not_allowed": (
        "Return address refused",
        "The auth service refused this return address at code issuance, so no sign-in code "
        "exists. Start again from the app you were signing in to.",
    ),
    "redirect_uri_required": (
        "Return address missing",
        "No app callback was supplied, so no sign-in code could be issued.",
    ),
    SESSION_ENDED_CODE: (
        "Session ended",
        "Your sign-in expired or was revoked. Sign in again to continue.",
    ),
    "session_not_found": (
        "Session no longer valid",
        "This portal's session cookie was already stale - it has been cleared. Sign in again "
        "to continue.",
    ),
    "invalid_code": (
        "Sign-in handoff rejected",
        "The one-time sign-in code was already used, or it has expired.",
    ),
    "code_expired": (
        "Sign-in handoff rejected",
        "The one-time sign-in code has expired.",
    ),
    "redirect_uri_mismatch": (
        "Sign-in handoff rejected",
        "The one-time sign-in code was issued for a different return address.",
    ),
    "bootstrap_unconfigured": (
        "Passkey sign-in unavailable",
        "This portal's own browser-facing base URL is not configured, so the passkey handoff "
        "cannot be completed.",
    ),
    "bootstrap_code_missing": (
        "Sign-in handoff rejected",
        "The browser arrived without a sign-in handoff code.",
    ),
    UNUSABLE_RESPONSE_CODE: (
        "Sign-in unavailable",
        "The auth service answered with something this portal could not use. Nothing was "
        "signed in.",
    ),
}

#: The generic copy, used for any code outside :data:`CARD_COPY` — including a hand-edited
#: ``?error=`` query value on the login page.
_GENERIC_COPY: tuple[str, str] = (
    "Sign-in unavailable",
    "The auth portal could not complete this step. Nothing was signed in.",
)


def card_copy(code: str | None) -> tuple[str, str]:
    """The card title and copy for a bounded failure code (unknown codes degrade generically)."""
    return CARD_COPY.get(code or "", _GENERIC_COPY)


def code_for_error(error: PortalApiError) -> str:
    """The bounded card code for a typed portal failure.

    An auth-service failure carries its own ``error`` code; anything else (a transport failure, a
    non-JSON or token-shaped body, service discovery) is the portal's ``unusable_response``.

    ``ApiResponseError`` is the one case that needs care: the client builds its ``detail`` itself
    as ``HTTP <status>: <code>`` from the body's ``error`` field — never from a raw upstream body
    — so scanning that bounded text for one of our own codes is safe, and it is what surfaces
    B5b's ``redirect_uri_not_allowed`` (the auth service's own refusal, which a caller outside
    this portal's validation can still hit) as its own card instead of a generic one.
    """
    if isinstance(error, AuthServiceError):
        return error.code
    if isinstance(error, ApiResponseError):
        for code in CARD_COPY:
            if code in error.detail:
                return code
    return UNUSABLE_RESPONSE_CODE


def build_error_card(
    *,
    code: str | None,
    detail: str | None = None,
    endpoint_label: str | None = None,
) -> PrefabApp:
    """The AC10 card for a failure code — a page at HTTP 200, never a 500 and never a redirect.

    ``detail`` is the typed error's own ``str()``: a class name plus the portal's bounded message
    (no token, no upstream body), kept visible for the dev loop the way the ward portal's error
    view keeps it. ``endpoint_label`` names the auth service the portal looked for.
    """
    title, message = card_copy(code)

    with PrefabApp(title=f"School-Collab auth portal - {title}", css_class="p-6") as application:
        with Column(gap=4):
            H3("School-Collab auth portal")
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


def build_session_ended_card(
    *,
    detail: str | None = None,
    endpoint_label: str | None = None,
) -> PrefabApp:
    """The D18 card a session read renders once the refresh is known to have failed.

    The route clears the session cookie alongside this card: the failure means the cookie can
    only produce a broken page, and hiding that is the "signed in but nothing works" state D18
    rules out.
    """
    return build_error_card(
        code=SESSION_ENDED_CODE, detail=detail, endpoint_label=endpoint_label
    )


def build_invalid_return_uri_card(*, detail: str | None = None) -> PrefabApp:
    """The card an app callback outside ``AuthPortal:AppCallbackPrefixes`` gets (spec §14, AC13).

    The rejected value is deliberately **not** echoed: it is attacker-chosen text, and the auth
    service's own rejection bodies do not echo it either.
    """
    return build_error_card(code=RETURN_URI_NOT_ALLOWED_CODE, detail=detail)
