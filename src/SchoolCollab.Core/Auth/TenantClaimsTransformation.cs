using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
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
public sealed class TenantClaimsTransformation(
    TenantProvider tenantProvider,
    ILogger<TenantClaimsTransformation> logger) : IClaimsTransformation
{
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
            tenantProvider.SetTenant(context);
            
            // DEBUG: Log tenant context being set to diagnose tenant switch issues
            logger.LogDebug("TenantClaimsTransformation: SetTenant TenantId={TenantId}, TenantName={TenantName}, Type={Type}",
                context.TenantId, context.TenantName, context.Type);
        }
        else
        {
            // DEBUG: Log when tenant_id claim is missing or invalid
            logger.LogDebug("TenantClaimsTransformation: No valid tenant_id claim found (claim value: {TenantIdClaim})", tenantIdClaim ?? "null");
        }

        return Task.FromResult(principal);
    }
}
