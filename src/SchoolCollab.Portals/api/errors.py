"""Typed errors raised by the portal's service calls.

The Python counterpart of the repo's "typed domain exceptions" rule for C#:
the transport layer raises these, and the view/route layer decides what the
user sees — an error card, never a raw traceback. Keeping them in one place
lets a route catch ``PortalApiError`` and stay honest about the failure.
"""

from __future__ import annotations


class PortalApiError(RuntimeError):
    """Base class for every failure raised by the portal's API clients."""


class ServiceDiscoveryError(PortalApiError):
    """The AppHost-injected base URL for a service could not be resolved."""

    def __init__(self, service: str, tried: tuple[str, ...], present: dict[str, str]) -> None:
        self.service = service
        self.tried = tried
        self.present = present
        super().__init__(
            f"No base URL for '{service}' was injected. Tried: {', '.join(tried)}. "
            f"Discovery-shaped environment variables actually present: {present}"
        )


class ApiUnavailableError(PortalApiError):
    """The service could not be reached at all (refused, timeout, DNS)."""

    def __init__(self, service: str, base_url: str, cause: Exception) -> None:
        self.service = service
        self.base_url = base_url
        self.cause = cause
        super().__init__(f"{service} at {base_url} is unreachable: {cause}")


class ApiResponseError(PortalApiError):
    """The service answered, but not with the JSON the client asked for."""

    def __init__(self, service: str, base_url: str, detail: str) -> None:
        self.service = service
        self.base_url = base_url
        self.detail = detail
        super().__init__(f"{service} at {base_url} returned an unusable response: {detail}")
