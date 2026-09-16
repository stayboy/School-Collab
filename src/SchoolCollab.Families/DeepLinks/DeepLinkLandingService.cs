using Microsoft.Extensions.Logging;
using SchoolCollab.Core.DeepLinks;
using SchoolCollab.Core.Features;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Families.Services;

namespace SchoolCollab.Families.DeepLinks;

/// <summary>Outcome of a deep-link landing attempt (WS-E1 / ar-14-deep-links).</summary>
public enum DeepLinkLandingOutcome
{
    /// <summary>Token valid + the tenant's <c>EnableDeepLinks</c> flag is on → redirect.</summary>
    Success,
    /// <summary>Token expired, tampered, malformed, or tenant-mismatched → the friendly expired page.</summary>
    Expired,
    /// <summary>Token structurally valid but the runtime flag is off (dark launch) → expired page, no sign-in.</summary>
    Dark
}

/// <summary>The public landing's decision for a deep-link token.</summary>
public sealed record DeepLinkLandingResult(
    DeepLinkLandingOutcome Outcome,
    string? RedirectUrl,
    Guid? TenantId = null,
    Guid? ContactId = null);

/// <summary>
/// Pure validate → flag → redirect logic for the public deep-link landing
/// (WS-E1 / ar-14-deep-links, decision (c)). Unprotects the bearer token, rejects
/// expired / tampered / tenant-mismatched links, establishes the payload's tenant
/// context explicitly (<see cref="ITenantContextAccessor.RunWithExplicitTenantAsync{T}"/>),
/// honours the tenant's <c>FEATURE:EnableDeepLinks</c> dark-launch flag, stamps the
/// recipient's <c>OpenedAt</c> server-side via <see cref="FamiliesApiClient"/>, and
/// resolves the ward redirect target (decision (g)). Never throws for a bad token —
/// any invalid link becomes <see cref="DeepLinkLandingOutcome.Expired"/>.
/// </summary>
public sealed class DeepLinkLandingService(
    DeepLinkProtector protector,
    ITenantContextAccessor tenantAccessor,
    IFeatureFlagService featureFlags,
    FamiliesApiClient familiesApi,
    ILogger<DeepLinkLandingService> logger)
{
    /// <summary>Validates <paramref name="token"/> and resolves the landing outcome.</summary>
    /// <param name="token">The protected bearer token from the public <c>/deeplink</c> URL.</param>
    /// <param name="expectedTenantId">Optional tenant the token must match. When null
    /// (the normal landing path) the payload's minted tenant is authoritative and the
    /// tenant context is established from it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="DeepLinkLandingResult"/>: <see cref="DeepLinkLandingOutcome.Success"/>
    /// with the ward redirect URL, or <c>Expired</c> / <c>Dark</c> with no redirect.</returns>
    public async Task<DeepLinkLandingResult> HandleAsync(
        string token,
        Guid? expectedTenantId = null,
        CancellationToken ct = default)
    {
        var payload = protector.TryUnprotect(token);
        if (payload is null || payload.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            logger.LogInformation("Deep-link landing rejected: missing, malformed, or expired token");
            return new(DeepLinkLandingOutcome.Expired, null);
        }

        // Tenant-mismatch guard: a token minted for another tenant must never be
        // honoured when the caller requires a specific tenant.
        if (expectedTenantId is { } expected && payload.TenantId != expected)
        {
            logger.LogInformation(
                "Deep-link landing rejected: payload tenant {TenantId} differs from expected {Expected}",
                payload.TenantId, expected);
            return new(DeepLinkLandingOutcome.Expired, null);
        }

        // Establish tenant context explicitly from the payload (the ar-5 sweep
        // precedent) so the runtime flag resolution and the OpenedAt stamp are
        // tenant-correct even though this public route has no pre-existing auth.
        return await tenantAccessor.RunWithExplicitTenantAsync(
            payload.TenantId,
            async _ =>
            {
                if (!await featureFlags.IsEnabledAsync(FeatureFlagKeys.EnableDeepLinks, ct))
                {
                    logger.LogInformation(
                        "Deep-link landing: EnableDeepLinks is OFF for tenant {TenantId}", payload.TenantId);
                    return new DeepLinkLandingResult(DeepLinkLandingOutcome.Dark, null);
                }

                // Idempotent first-visit stamp on the Assignments API (no-op when OpenedAt
                // already set). Best-effort: a stamp failure must not break the redirect, so
                // the call is guarded — a transport/API fault is logged and swallowed.
                // Classification (P1 / ar-14): FamiliesApiClient bounds the stamp to a ~3s
                // per-call deadline via a linked CTS, so a timed-out stamp surfaces as
                // OperationCanceledException with the CALLER's ct NOT cancelled. The filter
                // here therefore only rethrows when the CALLER cancelled — a timeout
                // cancellation falls through to the generic catch below and degrades to the
                // logged warning, letting the redirect proceed promptly.
                try
                {
                    await familiesApi.MarkRecipientOpenedAsync(
                        payload.AssignmentId, payload.ContactId, payload.TenantId, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "Deep-link landing: OpenedAt stamp failed for assignment {AssignmentId}/contact {ContactId}; " +
                        "continuing with the redirect", payload.AssignmentId, payload.ContactId);
                }

                // Redirect rule (decision (g)): single-ward (WardStudentId set) lands on
                // that ward's list; multi-ward guardian (no WardStudentId) lands on /ward.
                var redirectUrl = payload.WardStudentId is { } wardStudentId
                    ? $"/ward/{wardStudentId}"
                    : "/ward";

                return new DeepLinkLandingResult(
                    DeepLinkLandingOutcome.Success, redirectUrl, payload.TenantId, payload.ContactId);
            },
            ct);
    }
}
