using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace SchoolCollab.Core.Auth;

/// <summary>
/// Forwards the tenant of the CURRENT inbound request onto outgoing service-to-service
/// HttpClient calls via the <c>x-tenant-id</c> header.
/// </summary>
/// <remarks>
/// <para>
/// This is the API→API counterpart to <see cref="TenantPropagationDelegatingHandler"/>.
/// That handler reads the dev selection from <see cref="IDevTenantSelection"/> and is only
/// correct when the caller is the <b>originator</b> of the selection (the admin shell).
/// On an API host the tenant was already resolved for the inbound request — by
/// <see cref="TestAuthHandler"/> from the <c>x-tenant-id</c> header in dev, or from the
/// OIDC <c>tenant_id</c> claim in production — and this handler forwards THAT resolved
/// identity instead of re-reading shared state:
/// <list type="bullet">
/// <item>Prefer the inbound <c>x-tenant-id</c> header (exactly what
///       <see cref="TestAuthHandler"/> consumed, so forwarding reproduces the receiver's
///       resolution verbatim).</item>
/// <item>Fall back to the authenticated principal's <c>tenant_id</c> claim.</item>
/// <item>No context, or nothing resolvable → no header stamped (a background/dispatch
///       caller stays unscoped, and in production the receiving
///       <see cref="TestAuthHandler"/>-less host ignores the header anyway).</item>
/// </list>
/// </para>
/// </remarks>
public sealed class TenantForwardingDelegatingHandler(IHttpContextAccessor httpContextAccessor)
    : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is not null && TryResolveTenantId(httpContext, out var tenantId))
        {
            request.Headers.Remove("x-tenant-id");
            request.Headers.Add("x-tenant-id", tenantId.ToString());
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static bool TryResolveTenantId(HttpContext httpContext, out Guid tenantId)
    {
        // 1) Inbound header (dev/TestAuth path — set by the admin shell's
        //    TenantPropagationDelegatingHandler on StudentsApiClient et al).
        var header = httpContext.Request.Headers["x-tenant-id"].ToString();
        if (Guid.TryParse(header, out tenantId) && tenantId != Guid.Empty)
        {
            return true;
        }

        // 2) Authenticated principal claim (set by TestAuthHandler after its own
        //    resolution, or by TenantClaimsTransformation from Keycloak in prod).
        var claim = httpContext.User?.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(claim, out tenantId) && tenantId != Guid.Empty;
    }
}
