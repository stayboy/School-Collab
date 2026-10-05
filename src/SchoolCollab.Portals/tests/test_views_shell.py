"""The shared page chrome's own tests (round ``portal-shell-and-ward-retirement``, B3).

Three parts, all about the *chrome* rather than one page's content:

* the chrome is really shared — the same document root and the same brand bar appear on more
  than one rendered page;
* the refactor is real — ``PrefabApp`` is constructed exactly once in this package, in
  :mod:`views.shell`, so a future page cannot quietly hand-roll its own app again (a
  source-inspection assertion, in the spirit of ``PortalsSolutionItemsArchitectureTests``);
* the document-level attributes a page carries beyond the chrome (the submission page's grade
  form state and navigate handler) are forwarded, and omitted when a page carries neither.
"""

from __future__ import annotations

import json
import re
from pathlib import Path
from typing import Any

from api import ServiceEndpoint
from views.shell import shell
from views.teacher import (
    GRADE_ATTEMPTED_STATE,
    NAVIGATE_HANDLER,
    NAVIGATE_HANDLER_JS,
    build_review_queue_view,
    build_teacher_list_view,
)

ENDPOINT = ServiceEndpoint(
    service="assignments-api", base_url="http://assignments-api.test", env_var="test"
)

ASSIGNMENT_ID = "11111111-1111-1111-1111-111111111111"

_INITIAL_DATA = re.compile(
    r'<script id="prefab:initial-data" type="application/json">(.*?)</script>', re.S
)


def _document(html: str) -> dict[str, Any]:
    """A rendered page's wire envelope, parsed out of its initial-data script.

    Takes the rendered HTML rather than the app: ``PrefabApp.html()`` stamps the root class on
    first render, so rendering twice would nest a second wrapper around the same document.
    """
    match = _INITIAL_DATA.search(html)
    assert match is not None, "every rendered page carries its initial-data envelope"
    return json.loads(match.group(1))


def _chrome(html: str) -> tuple[str, dict[str, Any]]:
    """A page's document-root class and the shared column the chrome and its content live in."""
    view = _document(html)["view"]
    return view["cssClass"], view["children"][0]


def test_the_shared_chrome_renders_on_more_than_one_page() -> None:
    """One chrome, several pages: the same root and brand bar, each page's own heading in it."""
    pages = [
        (
            build_teacher_list_view(rows=[], endpoint=ENDPOINT, status_code=200),
            "Teacher portal — assignments",
        ),
        (
            build_review_queue_view(
                assignment_id=ASSIGNMENT_ID, rows=[], endpoint=ENDPOINT, status_code=200
            ),
            "Teacher portal — review queue",
        ),
    ]

    for application, heading in pages:
        html = application.html()
        assert f"<title>{heading}</title>" in html  # the document title
        root_class, column = _chrome(html)
        assert root_class == "pf-app-root p-6"
        assert column["cssClass"] == "gap-4"
        # The brand bar is the shared column's first child on every page. A page whose visible
        # heading differs from its document title passes ``heading=`` explicitly, which is what
        # keeps every page's rendered strings exactly as they were before the shell existed.
        assert column["children"][0] == {"content": heading, "type": "H3"}


def test_the_teacher_pages_do_not_construct_their_own_prefab_app() -> None:
    """AC3: the builders go through the shell — ``teacher.py`` names no ``PrefabApp`` at all.

    A rendered page cannot show that no *other* page bypasses the shell, and the bypass is
    exactly the regression this round exists to prevent — so it is pinned by source
    inspection, and across every module of the package rather than this one file.
    """
    sites = {
        path.name: path.read_text(encoding="utf-8").count("PrefabApp(")
        for path in sorted(_package_directory().glob("*.py"))
    }
    teacher_source = _view_source("teacher.py")

    assert teacher_source, "anti-vacuity: the module read must not be empty"
    assert "PrefabApp(" not in teacher_source, (
        "every teacher page is rendered through views.shell.shell"
    )
    assert {name for name, count in sites.items() if count} == {"shell.py"}, (
        f"only shell.py may construct the page document: {sites}"
    )
    assert sites["shell.py"] == 1, f"the shell's one construction site: {sites}"


def test_the_shell_forwards_a_pages_document_level_attributes() -> None:
    """B1's pass-through: state and js_actions reach the document; a ``None`` is omitted.

    ``build_submission_view`` is the one page that carries them (its grade form's submit-driven
    attempted state and its navigate handler). The omission half is the load-bearing one: every
    other page's document must stay byte-identical to the one it assembled for itself.
    """
    with shell(
        title="Teacher portal — submission",
        state={GRADE_ATTEMPTED_STATE: False},
        js_actions={NAVIGATE_HANDLER: NAVIGATE_HANDLER_JS},
    ) as application:
        pass  # no page content: this is about the document the shell assembles

    rendered = application.html()
    assert _document(rendered)["state"] == {GRADE_ATTEMPTED_STATE: False}
    assert NAVIGATE_HANDLER_JS in rendered

    with shell(title="Teacher portal — assignments") as plain:
        pass

    plain_html = plain.html()
    assert "state" not in _document(plain_html)
    assert NAVIGATE_HANDLER_JS not in plain_html


def _package_directory() -> Path:
    """The ``views`` package's own directory."""
    import views

    assert views.__file__ is not None
    return Path(views.__file__).resolve().parent


def _view_source(name: str) -> str:
    """One module of the ``views`` package, as source text."""
    return (_package_directory() / name).read_text(encoding="utf-8")
