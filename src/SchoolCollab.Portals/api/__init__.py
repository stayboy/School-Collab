"""Typed HTTP clients over the existing School-Collab REST APIs.

The portal is a pure HTTP consumer: no backend change, no database access
(plan Q3). Every service call lives behind this package so the view layer
(``views/``) never touches transport, and the transport layer never touches
Prefab components — the two layers churn for different reasons (prefab-ui is
0.x, and the plan's risk table asks for exactly this isolation).
"""

from api.assignments_api_client import AssignmentsApiClient, FetchResult
from api.dto import AssignmentRow
from api.errors import (
    ApiResponseError,
    ApiUnavailableError,
    PortalApiError,
    ServiceDiscoveryError,
)
from api.service_discovery import ServiceEndpoint, resolve_service_endpoint

__all__ = [
    "ApiResponseError",
    "ApiUnavailableError",
    "AssignmentRow",
    "AssignmentsApiClient",
    "FetchResult",
    "PortalApiError",
    "ServiceDiscoveryError",
    "ServiceEndpoint",
    "resolve_service_endpoint",
]
