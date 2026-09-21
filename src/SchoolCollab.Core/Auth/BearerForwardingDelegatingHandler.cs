using System.Net.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.Features;

namespace SchoolCollab.Core.Auth;

/// <summary>
/// Forwards the inbound request's <c>Authorization</c> header (a Keycloak bearer token)
/// onto outgoing cross-context HTTP calls so a real-auth (<c>FEATURE:DisableOIDCAuth=false</c>)
/// API host authenticates its downstream API hops with the caller's identity (ar-24). In
/// TestAuth/dev mode (<c>FEATURE:DisableOIDCAuth=true</c>) it is a strict no-op: there is no
/// bearer to forward and the receiving TestAuth handlers accept anonymous/tenant-header calls.
/// </summary>
/// <remarks>
/// <para>Registered as <b>transient</b> (see the <c>ModuleServices.cs:32-34</c> note — a
/// delegating handler instance is created per outgoing request via
/// <c>IHttpClientFactory</c>, so it must not capture per-request state). It reads the current
/// request through <see cref="IHttpContextAccessor"/>; in a Blazor interactive circuit
/// <c>HttpContext</c> is null, so the header copy silently does nothing there — the interactive
/// clients are therefore a documented residual, not covered by this handler.</para>
/// <para>Never throws when the header is absent: a missing/blank <c>Authorization</c> simply
/// sends the downstream request without it (the receiving group then rejects with 401).</para>
/// </remarks>
public sealed class BearerForwardingDelegatingHandler(
    IFeatureFlagService featureFlags,
    IHttpContextAccessor httpContextAccessor,
    ILogger<BearerForwardingDelegatingHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Real-auth only (dev bypass no-op): when DisableOIDCAuth is enabled the caller has no
        // bearer, so copy nothing.
        if (!await featureFlags.IsEnabledAsync(FeatureFlagKeys.DisableOIDCAuth, cancellationToken))
        {
            var authorization =
                httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
            if (!string.IsNullOrWhiteSpace(authorization)
                && !request.Headers.Contains("Authorization"))
            {
                request.Headers.TryAddWithoutValidation("Authorization", authorization);
                logger.LogDebug(
                    "BearerForwardingDelegatingHandler: forwarded the inbound Authorization header to {Method} {Uri}",
                    request.Method, request.RequestUri);
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
