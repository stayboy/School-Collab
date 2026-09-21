"""Client tests driven by ``httpx.MockTransport`` — no server, no Docker.

This is the Python counterpart of the .NET hosts' scripted
``HttpMessageHandler`` / container-free ``TestServer`` approach, and it is what
isolating the service calls in their own class buys the portal.

The client is async, so each case owns its transport through an
``async with`` block (the same lifespan-owned-client discipline as the app).
"""

from __future__ import annotations

from collections.abc import AsyncIterator, Callable
from contextlib import asynccontextmanager

import httpx
import pytest

from api import (
    ApiResponseError,
    ApiUnavailableError,
    AssignmentsApiClient,
    ServiceEndpoint,
)

ENDPOINT = ServiceEndpoint(
    service="assignments-api", base_url="http://assignments-api.test", env_var="test"
)

Handler = Callable[[httpx.Request], httpx.Response]


@asynccontextmanager
async def _client(handler: Handler) -> AsyncIterator[AssignmentsApiClient]:
    async with httpx.AsyncClient(transport=httpx.MockTransport(handler)) as http:
        yield AssignmentsApiClient(http, ENDPOINT)


async def test_list_assignments_hits_the_expected_url_and_parses_rows() -> None:
    seen: dict[str, str] = {}

    def handler(request: httpx.Request) -> httpx.Response:
        seen["url"] = str(request.url)
        return httpx.Response(
            200,
            json=[
                {
                    "id": "a1",
                    "title": "Reading response",
                    "status": "Published",
                    "dueDate": "2026-10-01",
                    "createdAt": "2026-09-21T09:00:00Z",
                }
            ],
        )

    async with _client(handler) as client:
        result = await client.list_assignments()

    assert seen["url"] == "http://assignments-api.test/assignments"
    assert result.status_code == 200
    assert result.row_count == 1
    assert result.rows[0].title == "Reading response"
    assert result.rows[0].as_table_row()["dueDate"] == "2026-10-01"


async def test_empty_database_is_a_success_not_a_failure() -> None:
    """The dev database legitimately answers ``[]`` (ar-21/23 data-visibility note)."""
    handler: Handler = lambda request: httpx.Response(200, json=[])  # noqa: E731

    async with _client(handler) as client:
        result = await client.list_assignments()

    assert result.row_count == 0


async def test_tolerates_an_envelope_and_skips_non_object_rows() -> None:
    handler: Handler = lambda request: httpx.Response(  # noqa: E731
        200, json={"items": [{"id": "a2"}, "not-an-object"]}
    )

    async with _client(handler) as client:
        result = await client.list_assignments()

    assert result.row_count == 1
    assert result.rows[0].id == "a2"
    assert result.rows[0].title is None


async def test_html_login_page_is_rejected_as_data() -> None:
    """A real-auth OIDC challenge can answer 200 with an HTML login page."""
    handler: Handler = lambda request: httpx.Response(  # noqa: E731
        200, text="<html>login</html>", headers={"content-type": "text/html"}
    )

    async with _client(handler) as client:
        with pytest.raises(ApiResponseError) as failure:
            await client.list_assignments()

    assert "text/html" in str(failure.value)


async def test_error_status_is_reported_with_its_code() -> None:
    handler: Handler = lambda request: httpx.Response(500, json={"error": "boom"})  # noqa: E731

    async with _client(handler) as client:
        with pytest.raises(ApiResponseError) as failure:
            await client.list_assignments()

    assert "HTTP 500" in str(failure.value)


async def test_transport_failure_is_reported_as_unavailable() -> None:
    def handler(request: httpx.Request) -> httpx.Response:
        raise httpx.ConnectError("connection refused", request=request)

    async with _client(handler) as client:
        with pytest.raises(ApiUnavailableError) as failure:
            await client.list_assignments()

    assert "unreachable" in str(failure.value)
    assert failure.value.base_url == "http://assignments-api.test"
