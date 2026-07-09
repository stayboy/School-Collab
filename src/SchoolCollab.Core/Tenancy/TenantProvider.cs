using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Http;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Core.Tenancy;

public class TenantProvider : ITenantProvider
{
    private readonly AsyncLocal<TenantContext> _currentTenant = new();
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public TenantProvider(IHttpContextAccessor? httpContextAccessor = null)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void SetTenant(TenantContext context)
    {
        _currentTenant.Value = context;
    }

    public TenantContext GetTenantContext()
    {
        // 1. Explicit override (set by TenantContextAccessor.RunWithExplicitTenantAsync,
        //    or by TenantClaimsTransformation from the auth claim). Highest precedence.
        var explicitContext = _currentTenant.Value;
        if (explicitContext is not null)
        {
            return explicitContext;
        }

        // 2. Fall back to the authenticated principal's tenant claim. This is the
        //    reliable source of truth for HTTP requests: the tenant_id claim is set on
        //    HttpContext.User by TestAuthHandler (dev) or by Keycloak (production) for
        //    every request, so it always reflects the tenant the UI selected even when
        //    the AsyncLocal seeded by IClaimsTransformation does not flow into this async
        //    scope (the classic "RequireTenantContext doesn't see the tenant the UI
        //    selected" dev bug). Topology-independent: works whether the tenant arrives
        //    via the x-tenant-id header or the shared dev-selection cache.
        var user = _httpContextAccessor?.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var tenantIdClaim = user.FindFirst("tenant_id")?.Value;
            if (Guid.TryParse(tenantIdClaim, out var tenantId) && tenantId != Guid.Empty)
            {
                var tenantName = user.FindFirst("tenant_name")?.Value ?? "Unknown";
                var tenantType = Enum.TryParse<TenantType>(
                    user.FindFirst("tenant_type")?.Value, ignoreCase: true, out var parsedType)
                    ? parsedType
                    : TenantType.School;

                return new TenantContext(tenantId, tenantName, tenantType);
            }
        }

        // 3. No tenant in scope → default 'System' context (avoids nulls downstream).
        return new TenantContext(Guid.Empty, "System", TenantType.Organization);
    }

    public void Clear()
    {
        _currentTenant.Value = null;
    }
}
