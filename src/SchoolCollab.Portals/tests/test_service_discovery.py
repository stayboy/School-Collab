"""Aspire service-discovery resolution — no server, no Docker."""

from __future__ import annotations

import pytest

from api import ServiceDiscoveryError, resolve_service_endpoint


def test_resolves_the_dotnet_style_discovery_key(monkeypatch: pytest.MonkeyPatch) -> None:
    """Aspire injects ``services__<name>__http__0`` (the ar-23 spike finding)."""
    monkeypatch.delenv("ASSIGNMENTS_API_HTTP", raising=False)
    monkeypatch.setenv("services__assignments-api__http__0", "http://localhost:5199/")

    endpoint = resolve_service_endpoint("assignments-api")

    assert endpoint.base_url == "http://localhost:5199"  # trailing slash trimmed
    assert endpoint.env_var == "services__assignments-api__http__0"
    assert endpoint.label == "assignments-api at http://localhost:5199 (via services__assignments-api__http__0)"


def test_falls_back_to_the_simplified_key(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.delenv("services__assignments-api__http__0", raising=False)
    monkeypatch.setenv("ASSIGNMENTS_API_HTTP", "http://localhost:5199")

    assert resolve_service_endpoint("assignments-api").env_var == "ASSIGNMENTS_API_HTTP"


def test_missing_configuration_reports_what_it_looked_for(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.delenv("services__assignments-api__http__0", raising=False)
    monkeypatch.delenv("ASSIGNMENTS_API_HTTP", raising=False)

    with pytest.raises(ServiceDiscoveryError) as failure:
        resolve_service_endpoint("assignments-api")

    message = str(failure.value)
    assert "services__assignments-api__http__0" in message
    assert "ASSIGNMENTS_API_HTTP" in message
