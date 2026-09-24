"""Typed HTTP client over the School-Collab auth service.

The portal is a pure HTTP consumer of the auth service — no Keycloak call, no data-API call, no
credential of any kind (D6/D7/D17, AC11). Every portal-facing operation lives behind this package
so the view layer (``views/``) never touches transport, and the transport layer never touches
Prefab components — the two layers churn for different reasons (prefab-ui is 0.x, and the view
trees are the churn-prone surface).
"""

from api.auth_api_client import (
    AUTH_SERVICE,
    AuthApiClient,
    AuthServiceEndpoint,
    candidate_env_vars,
    resolve_auth_service_endpoint,
)
from api.dto import (
    USER_ADMIN_ROLE,
    BootstrapRedemption,
    ExchangeResult,
    SessionData,
    SessionRevocation,
)
from api.errors import (
    ApiResponseError,
    ApiUnavailableError,
    AuthCredentialsRejectedError,
    AuthServiceError,
    AuthUpstreamError,
    AuthUserDisabledError,
    BootstrapCodeRejectedError,
    PortalApiError,
    ServiceDiscoveryError,
    SessionEndedError,
    SessionNotFoundError,
    TokenInResponseError,
)

__all__ = [
    "AUTH_SERVICE",
    "USER_ADMIN_ROLE",
    "ApiResponseError",
    "ApiUnavailableError",
    "AuthApiClient",
    "AuthCredentialsRejectedError",
    "AuthServiceEndpoint",
    "AuthServiceError",
    "AuthUpstreamError",
    "AuthUserDisabledError",
    "BootstrapCodeRejectedError",
    "BootstrapRedemption",
    "ExchangeResult",
    "PortalApiError",
    "ServiceDiscoveryError",
    "SessionData",
    "SessionEndedError",
    "SessionNotFoundError",
    "SessionRevocation",
    "TokenInResponseError",
    "candidate_env_vars",
    "resolve_auth_service_endpoint",
]
