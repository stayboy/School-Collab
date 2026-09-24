"""The portal's copy of the app-callback allowlist — the attack-vector suite (spec §14, AC13).

The portal holds a second, independent copy of the matcher the auth service enforces at code
issuance (B5b's ``AppCallbackAllowlist``): the same rule written twice, in two languages, against
two failure modes. This module is that copy's **proof**, and it deliberately walks the *same
vectors* as the C# suite (``tests/SchoolCollab.Auth.Tests.Unit/AppCallbackAllowlistTests.cs``) so
the two implementations can be diffed row by row: the test names mirror the C# ones
(``IsAllowed_RejectsASuffixAppendedToAnAllowlistedPath`` →
``test_is_allowed_rejects_a_suffix_appended_to_an_allowlisted_path``), and each vector class the
C# suite pins has a counterpart here.

The axes are the ones the security fix turns on: an allowlisted callback passes (including the
Blazor hosts' nested ``ReturnUrl`` query), scheme + host + port + path must **all** match (a
substring or loose-prefix implementation cannot pass this suite), an attacker URI in any spelling
is denied, malformed input on either side grants nothing, and an empty allowlist denies everything
rather than failing open.

Two vectors are additions of this copy, both in the fail-closed direction, and both labelled where
they appear: a percent-encoded path spelling and a dot-segment path spelling. ``.NET``'s
``Uri.AbsolutePath`` keeps percent escapes (so the C# matcher also refuses them) but *resolves* dot
segments (so it refuses ``/signin-handshake/../evil`` as ``/evil``); refusing both spellings here
reaches the same outcome without re-implementing RFC 3986 ``remove_dot_segments``.
"""

from __future__ import annotations

import pytest

from app import is_allowed_app_callback, parse_app_callback_prefixes

#: The admin host's handshake callback — the spelling B1's challenge handler and B4's
#: ``BuildCallbackUri`` mint (launch profile ``http://localhost:5300``).
ADMIN_CALLBACK = "http://localhost:5300/signin-handshake"

#: The admin host's https scheme spelling (``https://localhost:7300``).
ADMIN_CALLBACK_HTTPS = "https://localhost:7300/signin-handshake"

#: The portal's own D16 bootstrap redemption URI — the second kind of allowlisted target the
#: AppHost appends (derived from the portal's Aspire endpoint, never hardcoded there). Note it is
#: in the *auth service's* copy only; the portal's copy must not treat it as a ``return_uri``.
PORTAL_BOOTSTRAP = "http://localhost:9091/bootstrap"

CONFIGURED = f"{ADMIN_CALLBACK};{ADMIN_CALLBACK_HTTPS};{PORTAL_BOOTSTRAP}"


def _allowed(candidate: str | None, configured: str | None = CONFIGURED) -> bool:
    """Whether ``candidate`` may receive a code, under ``configured`` (both parsed as one rule)."""
    return is_allowed_app_callback(candidate, parse_app_callback_prefixes(configured))


def test_is_allowed_accepts_an_allowlisted_callback_ignoring_the_query() -> None:
    # The Blazor hosts mint `{scheme}://{host}{pathBase}/signin-handshake?ReturnUrl=<escaped>`
    # (B1's challenge handler and B4's BuildCallbackUri share the spelling), so the query half —
    # including a nested ReturnUrl that itself looks like a URL — must be ignored, not treated as
    # a mismatch. This is the shape B8 hands to the auth service and to the browser verbatim.
    assert _allowed(f"{ADMIN_CALLBACK}?ReturnUrl=%2Fstudents%3Fpage%3D2")


def test_is_allowed_accepts_an_allowlisted_callback_without_any_query() -> None:
    assert _allowed(ADMIN_CALLBACK)


def test_is_allowed_accepts_the_portals_bootstrap_redemption_uri() -> None:
    assert _allowed(PORTAL_BOOTSTRAP)


def test_is_allowed_accepts_a_path_extension_of_an_allowlisted_path() -> None:
    # Path PREFIX matching at a segment boundary: scheme/host/port bind the code's destination,
    # and a deeper path on an allowlisted origin stays on that origin.
    assert _allowed(f"{ADMIN_CALLBACK}/continuation")


def test_is_allowed_accepts_a_trailing_slash_spelling_of_an_allowlisted_path() -> None:
    # `/signin-handshake/` and `/signin-handshake` are the same resource, in both directions: the
    # entry's trailing slash is normalized, so it cannot build a `//` comparison that matches
    # nothing.
    assert _allowed(f"{ADMIN_CALLBACK}/")

    trailing_slash_entry = f"{ADMIN_CALLBACK}/"
    assert _allowed(ADMIN_CALLBACK, trailing_slash_entry)
    assert _allowed(f"{ADMIN_CALLBACK}/continuation", trailing_slash_entry)


def test_is_allowed_rejects_a_suffix_appended_to_an_allowlisted_path() -> None:
    # DISCRIMINATION: a plain `path.startswith(prefix.path)` accepts every one of these — `-evil`
    # and `X` are appended with no `/` boundary, so they are DIFFERENT resources on the
    # allowlisted origin that would otherwise inherit the callback's trust.
    assert not _allowed(f"{ADMIN_CALLBACK}-evil")
    assert not _allowed(f"{ADMIN_CALLBACK}X")
    assert not _allowed(f"{ADMIN_CALLBACK}-evil?ReturnUrl=%2F")
    assert not _allowed(f"{PORTAL_BOOTSTRAP}-evil")


def test_is_allowed_rejects_userinfo_on_the_candidate() -> None:
    # `http://attacker@localhost:5300/signin-handshake` parses with host=localhost and a userinfo
    # part the browser hides, so a userinfo-carrying target is never the allowlisted callback even
    # though its scheme/host/port/path all match.
    assert not _allowed("http://attacker@localhost:5300/signin-handshake")
    assert not _allowed("http://attacker:secret@localhost:5300/signin-handshake")
    assert not _allowed("http://attacker@localhost:5300/signin-handshake?ReturnUrl=%2F")


def test_is_allowed_skips_configured_entries_carrying_userinfo() -> None:
    # DISCRIMINATION, entry direction: the entry's userinfo is not one of the compared components,
    # so an entry that were merely *kept* would still grant the clean candidate host=localhost,
    # port=5300, path=/signin-handshake. Dropping it is what makes the list below deny.
    userinfo_only = "http://attacker@localhost:5300/signin-handshake"
    assert not _allowed(ADMIN_CALLBACK, userinfo_only)
    assert not _allowed(ADMIN_CALLBACK_HTTPS, userinfo_only)

    # ... and dropping it keeps the other entries working, without becoming a host-wide grant.
    mixed = f"{userinfo_only};{ADMIN_CALLBACK}"
    assert _allowed(ADMIN_CALLBACK, mixed)
    assert not _allowed(ADMIN_CALLBACK_HTTPS, mixed)


def test_is_allowed_rejects_a_host_that_merely_contains_an_allowlisted_prefix() -> None:
    # Each of these contains an allowlisted prefix as a substring but delivers the code somewhere
    # else — a loose-substring implementation would accept every one of them.
    assert not _allowed("http://attacker.example/?next=http://localhost:5300/signin-handshake")
    assert not _allowed("http://attacker.example#http://localhost:5300/signin-handshake")
    assert not _allowed("http://localhost.attacker.example:5300/signin-handshake")
    assert not _allowed("http://attacker.example/signin-handshake")


def test_is_allowed_requires_the_exact_scheme_host_and_port() -> None:
    assert not _allowed("https://localhost:5300/signin-handshake")  # the admin callback is http
    assert not _allowed("http://localhost:5400/signin-handshake")  # another app's port
    assert not _allowed("http://localhost/signin-handshake")  # implicit 80 is not 5300
    assert not _allowed("http://localhost:5301/signin-handshake")  # one port off is another origin


def test_is_allowed_requires_an_allowlisted_path() -> None:
    assert not _allowed("http://localhost:5300/login")  # only the handshake callback takes a code
    assert not _allowed("http://localhost:5300/")  # the origin alone is not a callback
    assert not _allowed("http://localhost:9091/session")  # the portal's path is /bootstrap


def test_is_allowed_treats_the_explicit_default_port_as_the_absent_one() -> None:
    # A URI normalizes the default port, so an entry written without one still matches a candidate
    # that spells it out — and still cannot match a non-default port. Both schemes, both ways.
    assert _allowed("http://callback.example:80/signin-handshake", "http://callback.example/signin-handshake")
    assert not _allowed("http://callback.example:8080/signin-handshake", "http://callback.example/signin-handshake")
    assert _allowed("https://callback.example:443/signin-handshake", "https://callback.example/signin-handshake")
    assert not _allowed("https://callback.example:8443/signin-handshake", "https://callback.example/signin-handshake")


def test_is_allowed_is_case_insensitive_for_scheme_and_host_but_not_for_the_path() -> None:
    # The scheme and the host are folded (both `Uri` and `urlsplit` lowercase them); the path is
    # NOT — a case-insensitive path compare would widen the target set to spellings the app does
    # not serve.
    assert _allowed("HTTP://LOCALHOST:5300/signin-handshake")
    assert not _allowed("http://localhost:5300/SIGNIN-HANDSHAKE")


def test_is_allowed_rejects_a_bare_origin_entry_for_a_deeper_path() -> None:
    # A configured entry with no path is the root path (`/`), and the boundary rule applies to it
    # like every other entry: it matches the origin's root and nothing deeper.
    root_entry = "http://localhost:5300"
    assert _allowed("http://localhost:5300", root_entry)
    assert _allowed("http://localhost:5300/", root_entry)
    assert not _allowed(ADMIN_CALLBACK, root_entry)


@pytest.mark.parametrize(
    "candidate",
    [
        None,
        "",
        "   ",
        "/signin-handshake",
        "not a uri at all",
        "ftp://localhost:5300/signin-handshake",
        "file:///signin-handshake",
        "http:///signin-handshake",
        "http://localhost:notaport/signin-handshake",
    ],
)
def test_is_allowed_denies_malformed_candidates(candidate: str | None) -> None:
    """An unparseable, relative, hostless, portless-by-typo or non-http(s) target never passes."""
    assert not _allowed(candidate)


def test_is_allowed_skips_malformed_entries_without_widening_or_disabling_the_list() -> None:
    # The malformed entries (an unparseable token, a blank entry, a non-http(s) scheme, and an
    # entry carrying its own query — which could never match, since the candidate's query is
    # ignored) grant nothing; the valid entry still works.
    mixed = f"not a uri; ;ftp://localhost:5300/signin-handshake;{ADMIN_CALLBACK}?ReturnUrl=x;{ADMIN_CALLBACK}"

    assert _allowed(ADMIN_CALLBACK, mixed)
    assert _allowed(f"{ADMIN_CALLBACK}?ReturnUrl=x", mixed)
    assert not _allowed("ftp://localhost:5300/signin-handshake", mixed)
    assert not _allowed("http://attacker.example/signin-handshake", mixed)


@pytest.mark.parametrize("configured", [None, "", "   ", ";;", "; ;"])
def test_is_allowed_denies_everything_when_the_allowlist_is_empty_or_blank(
    configured: str | None,
) -> None:
    # DISCRIMINATION (the fail-open defect this control exists to prevent): an allowlist that
    # resolves to no usable entry must deny every candidate. An implementation that skips the
    # match when nothing is configured would mint a code for ANY caller-supplied URI — the crafted
    # `return_uri=attacker` link would work again.
    assert not _allowed(ADMIN_CALLBACK, configured)
    assert not _allowed("http://attacker.example/signin-handshake", configured)
    assert not _allowed(f"{ADMIN_CALLBACK}?ReturnUrl=%2Fstudents", configured)


def test_is_allowed_rejects_a_percent_encoded_path_spelling() -> None:
    # This copy's addition. `%2F` would decode to a path separator and `%73` to an `s`, so an
    # implementation that decodes before comparing could match a spelling whose effective
    # destination differs from the allowlisted path. Neither is compared decoded: the raw path is
    # used, so both are refused (measured: .NET's Uri keeps both escapes in AbsolutePath, so the
    # C# copy refuses them too — this is parity, not extra strictness).
    assert not _allowed(f"{ADMIN_CALLBACK}%2Fevil")
    assert not _allowed("http://localhost:5300/%73ignin-handshake")


def test_is_allowed_rejects_a_dot_segment_path_spelling() -> None:
    # This copy's addition, and the fail-closed replacement for .NET's dot-segment resolution:
    # measured, `Uri.AbsolutePath` for `/signin-handshake/../evil` is `/evil`, which the C# matcher
    # refuses. Python's `urlsplit` leaves the raw path, and a plain boundary check would read
    # `/signin-handshake/../evil` as a deeper path under the prefix — accepting a spelling whose
    # resolved destination is a *different* resource on the allowlisted origin. Refusing any
    # dot-segment spelling lands on the C# outcome without re-implementing RFC 3986.
    assert not _allowed(f"{ADMIN_CALLBACK}/../evil")
    assert not _allowed(f"{ADMIN_CALLBACK}/./continuation")
    assert not _allowed("http://localhost:5300/../signin-handshake")
