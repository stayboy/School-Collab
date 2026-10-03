"""Typed HTTP clients over the existing School-Collab REST APIs.

The portal is a pure HTTP consumer: no backend change, no database access
(plan Q3). Every service call lives behind this package so the view layer
(``views/``) never touches transport, and the transport layer never touches
Prefab components — the two layers churn for different reasons (prefab-ui is
0.x, and the plan's risk table asks for exactly this isolation).
"""

from api.assignments_api_client import (
    AssignmentsApiClient,
    FetchResult,
    ReviewQueueResult,
    SubmissionResult,
)
from api.dto import (
    AssignmentRow,
    SubmissionDetailRow,
    SubmissionForReviewRow,
    SubmissionReviewRow,
    SubmissionVersionRow,
)
from api.errors import (
    ApiResponseError,
    ApiUnavailableError,
    MissingConfigurationError,
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
    "MissingConfigurationError",
    "PortalApiError",
    "ReviewQueueResult",
    "ServiceDiscoveryError",
    "ServiceEndpoint",
    "SubmissionDetailRow",
    "SubmissionForReviewRow",
    "SubmissionResult",
    "SubmissionReviewRow",
    "SubmissionVersionRow",
    "resolve_service_endpoint",
]
