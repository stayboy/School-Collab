using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Core.Auth;

/// <summary>
/// Default implementation of <see cref="IClaimsTransformation"/> that maps Keycloak-issued
/// tenant claims into <see cref="TenantContext"/> via <see cref="TenantProvider"/>.
///
/// Expected claims (from ID token / user info):
/// - tenant_id: GUID string identifying the tenant.
/// - tenant_name: human-readable tenant name.
/// - tenant_type: optional logical type (School, Organization, Team). Defaults to School.
/// </summary>
public sealed class TenantClaimsTransformation : IClaimsTransformation
{
    private readonly TenantProvider _tenantProvider;

    public TenantClaimsTransformation(TenantProvider tenantProvider)
    {
        _tenantProvider = tenantProvider;
    }

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity)
        {
            // No claims identity — leave the tenant context unchanged.
            return Task.FromResult(principal);
        }

        var tenantIdClaim = principal.FindFirst("tenant_id")?.Value;
        var tenantNameClaim = principal.FindFirst("tenant_name")?.Value;
        var tenantTypeClaim = principal.FindFirst("tenant_type")?.Value;

        if (Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            var type = Enum.TryParse<TenantType>(tenantTypeClaim, true, out var parsedType)
                ? parsedType
                : TenantType.School;

            var context = new TenantContext(tenantId, tenantNameClaim ?? "Unknown", type);
            _tenantProvider.SetTenant(context);
        }

        return Task.FromResult(principal);
    }
}
