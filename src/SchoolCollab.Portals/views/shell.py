"""The portal's shared page chrome (round ``portal-shell-and-ward-retirement``, B1).

:func:`shell` is the one place that assembles a page's document, so ``PrefabApp`` is
constructed exactly once in the whole ``views/`` package and a page cannot quietly
hand-roll a second chrome. It is the Prefab analogue of Blazor's ``MainLayout``
**outcome** — one chrome for every page — not its mechanism: Prefab has no router,
no outlet and no ``@layout`` selection, so the shared layout is a Python composition
function that every page calls.

The chrome stays minimal and static: the document title, the page title and the
brand bar. A page's own affordances (the session badge and links, the breadcrumbs)
stay with the page, because they are pure functions of that page's data — the shell
reads none.
"""

from __future__ import annotations

from collections.abc import Iterator, Mapping
from contextlib import contextmanager
from typing import Any

from prefab_ui.app import PrefabApp
from prefab_ui.components import Column, H3


@contextmanager
def shell(
    *,
    title: str,
    heading: str | None = None,
    state: Mapping[str, Any] | None = None,
    js_actions: Mapping[str, str] | None = None,
) -> Iterator[PrefabApp]:
    """The portal's shared page chrome: document title, page title, brand bar.

    The Prefab analogue of Blazor's ``MainLayout`` **outcome** (one chrome for every page),
    not its mechanism: Prefab has no router, no outlet and no ``@layout`` selection, so the
    shared layout is a Python composition function every page calls.

    ``heading`` defaults to the document title, and a page whose visible ``H3`` differs from
    its document title passes it explicitly — that is what keeps every page's rendered
    strings exactly as they were when each page assembled its own document.

    ``state`` and ``js_actions`` are the two document-level attributes a page carries beyond
    the chrome (``build_submission_view``'s grade form: its client-side
    attempted-state key and its navigate handler). They are forwarded untouched, and a
    ``None`` is omitted so ``PrefabApp`` applies its own field default — the pages that
    carry neither render byte-identically to before this round.
    """
    document: dict[str, Any] = {"title": title, "css_class": "p-6"}
    if state is not None:
        document["state"] = state
    if js_actions is not None:
        document["js_actions"] = js_actions

    with PrefabApp(**document) as application:
        with Column(gap=4):
            H3(heading or title)
            yield application
