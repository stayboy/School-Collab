using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;

namespace SchoolCollab.Admin.Shared.Services;

/// <summary>
/// Resolves whether the signed-in user has a "visible tenancy" — i.e. a real
/// tenant context the Admin UI can operate within.
///
/// <para>Strict-tenant entities (<c>GradeLevel</c>, <c>Subject</c>, <c>Period</c>,
/// <c>Student</c>) cannot be created or queried against the system/default tenant
/// (<see cref="Guid.Empty"/>). When the user has no real tenant, the Admin UI must
/// disable those tools rather than trigger the server-side
/// <c>TenantContextRequiredException</c>.</para>
///
/// <para>The tenant is read from the authenticated user's <c>tenant_id</c> claim.
/// This deliberately does NOT use the server-side <c>ITenantProvider</c>: that
/// provider is backed by AsyncLocal and is not reliably available inside a Blazor
/// Server circuit (see <c>GradeLevelWizard</c> for the same reasoning).</para>
/// </summary>
public sealed record TenantScope(bool IsRealTenant, Guid? TenantId, string? TenantName);

public sealed class VisibleTenantService(
    AuthenticationStateProvider authenticationStateProvider,
    ILogger<VisibleTenantService> logger)
{
    /// <summary>
    /// One-shot read of the current tenant scope from the auth claims. A tenant is
    /// "real" when its <c>tenant_id</c> claim parses to a non-empty <see cref="Guid"/>.
    /// This is a pure UI/claim read — it never touches the server-side tenant provider.
    /// </summary>
    public async Task<TenantScope> GetScopeAsync()
    {
        try
        {
            var authState = await authenticationStateProvider.GetAuthenticationStateAsync();
            var user = authState.User;
            var tenantIdClaim = user.FindFirst("tenant_id")?.Value;

            if (Guid.TryParse(tenantIdClaim, out var tenantId) && tenantId != Guid.Empty)
            {
                var tenantName = user.FindFirst("tenant_name")?.Value;
                return new TenantScope(IsRealTenant: true, TenantId: tenantId, TenantName: tenantName);
            }

            logger.LogDebug(
                "VisibleTenantService: no real tenant (tenant_id claim = {Claim})",
                tenantIdClaim ?? "<missing>");
            return new TenantScope(IsRealTenant: false, TenantId: null, TenantName: null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "VisibleTenantService: failed to resolve tenant scope");
            return new TenantScope(IsRealTenant: false, TenantId: null, TenantName: null);
        }
    }
}
