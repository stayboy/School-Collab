"""Aspire service discovery, read the way Aspire actually injects it.

``WithReference(...)`` injects the .NET-style ``services__<name>__http__0``
environment variable — **not** the simplified ``<NAME>_HTTP`` form (spike
finding, ar-23: the simplified form was absent at Aspire.Hosting 13.4.5). Both
names are still checked, in that order, and whichever matched is reported so
``/health`` can show it rather than hiding the wiring.
"""

from __future__ import annotations

import os
from dataclasses import dataclass

from api.errors import ServiceDiscoveryError

# Substrings that make an environment variable "discovery-shaped" — used only to
# build a helpful diagnostic when resolution fails.
_DISCOVERY_HINTS = ("ASSIGN", "STUDENT", "SETTING", "SERVICE", "PORTAL")


@dataclass(frozen=True)
class ServiceEndpoint:
    """Where a service lives, and which environment variable said so."""

    service: str
    base_url: str
    env_var: str

    @property
    def label(self) -> str:
        return f"{self.service} at {self.base_url} (via {self.env_var})"


def candidate_env_vars(service: str) -> tuple[str, ...]:
    """The environment-variable names Aspire may have used, in priority order."""
    return (f"services__{service}__http__0", f"{service.upper().replace('-', '_')}_HTTP")


def resolve_service_endpoint(service: str) -> ServiceEndpoint:
    """Resolve ``service``'s base URL from the AppHost-injected environment."""
    tried = candidate_env_vars(service)
    for name in tried:
        value = os.environ.get(name)
        if value:
            return ServiceEndpoint(service=service, base_url=value.rstrip("/"), env_var=name)

    present = {
        key: value
        for key, value in os.environ.items()
        if key.lower().startswith("services") or any(hint in key.upper() for hint in _DISCOVERY_HINTS)
    }
    raise ServiceDiscoveryError(service, tried, present)
