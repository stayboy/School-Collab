"""Typed HTTP clients over the existing School-Collab REST APIs.

The portal is a pure HTTP consumer: no backend change, no database access
(plan Q3). Every service call lives behind this package so the view layer
(``views/``) never touches transport, and the transport layer never touches
Prefab components — the two layers churn for different reasons (prefab-ui is
0.x, and the plan's risk table asks for exactly this isolation).

Two upstreams now: the Assignments API (the ward list and the teacher
drill-down) and the auth service (the teacher surface's D19 session path — the
handshake redemption, the D18 session read and the D13 revocation). This module
re-exports both clients' public surface, per the pattern's own rule: adding a
client means growing the re-export list with it.
"""

from api.assignments_api_client import (
    AssignmentsApiClient,
    FetchResult,
    ReviewQueueResult,
    SubmissionResult,
)
from api.auth_api_client import (
    SESSION_HEADER_NAME,
    AuthApiClient,
    AuthServiceEndpoint,
    resolve_auth_service_endpoint,
)
from api.dto import (
    AssignmentRow,
    SessionData,
    SubmissionDetailRow,
    SubmissionForReviewRow,
    SubmissionReviewRow,
    SubmissionVersionRow,
)
from api.errors import (
    ApiResponseError,
    ApiUnavailableError,
    AuthServiceError,
    AuthUpstreamError,
    HandshakeCodeRejectedError,
    MissingConfigurationError,
    PortalApiError,
    ServiceDiscoveryError,
    SessionEndedError,
    SessionNotFoundError,
    TokenInResponseError,
)
from api.service_discovery import ServiceEndpoint, resolve_service_endpoint

__all__ = [
    "SESSION_HEADER_NAME",
    "ApiResponseError",
    "ApiUnavailableError",
    "AssignmentRow",
    "AssignmentsApiClient",
    "AuthApiClient",
    "AuthServiceEndpoint",
    "AuthServiceError",
    "AuthUpstreamError",
    "FetchResult",
    "HandshakeCodeRejectedError",
    "MissingConfigurationError",
    "PortalApiError",
    "ReviewQueueResult",
    "ServiceDiscoveryError",
    "ServiceEndpoint",
    "SessionData",
    "SessionEndedError",
    "SessionNotFoundError",
    "SubmissionDetailRow",
    "SubmissionForReviewRow",
    "SubmissionResult",
    "SubmissionReviewRow",
    "SubmissionVersionRow",
    "TokenInResponseError",
    "resolve_auth_service_endpoint",
    "resolve_service_endpoint",
]
