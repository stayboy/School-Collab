using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Core.Constants;
using SchoolCollab.Core.Features;

namespace SchoolCollab.Core.Auth;

/// <summary>
/// The single write-point for a minimal-API group's flag-conditional authorization policy.
/// Applies the policy exactly while real OIDC auth is on (<c>FEATURE:DisableOIDCAuth</c> off).
/// Guarded by <c>MinimalApiCompositionArchitectureTests</c>; pattern and rationale:
/// documents/solution/endpoint-organization-pattern.md §4.
/// </summary>
public static class EndpointGroupAuthExtensions
{
    /// <summary>Authenticated user on the Bearer scheme — the ar-24 receiving-side opt-in.</summary>
    public static RouteGroupBuilder RequireAuthIfOidc(
        this RouteGroupBuilder group, IFeatureFlagService? featureFlags)
        => group.RequireAuthIfOidc(featureFlags, static policy => policy
            .RequireAuthenticatedUser()
            .AddAuthenticationSchemes(AuthTenancyExtensions.BearerScheme));

    /// <summary>
    /// Caller-supplied policy (gateway-scheme role gates, the portal-facing admin policy).
    /// A null flag service means "gate on" — the production posture, never gate-open.
    /// </summary>
    public static RouteGroupBuilder RequireAuthIfOidc(
        this RouteGroupBuilder group,
        IFeatureFlagService? featureFlags,
        Action<AuthorizationPolicyBuilder> policy)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(policy);

        if (featureFlags is null || !featureFlags.IsEnabled(FeatureFlagKeys.DisableOIDCAuth))
        {
            group.RequireAuthorization(policy);
        }

        return group;
    }
}
