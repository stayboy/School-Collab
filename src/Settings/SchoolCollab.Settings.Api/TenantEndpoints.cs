using Microsoft.AspNetCore.Authorization;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Features;
using SchoolCollab.Settings.Api.Endpoints;

namespace SchoolCollab.Settings.Api;

public static class TenantEndpoints
{
    /// <summary>
    /// Maps the read-only tenant registry endpoint under <c>/api/tenants</c>.
    /// Used by the dev tenant switcher (auth-disabled) to populate its dropdown.
    /// Cookie-gated when OIDC is on; open under TestAuth in dev, matching
    /// <c>MapCodedValueEndpoints</c>.
    /// </summary>
    public static WebApplication MapTenantEndpoints(this WebApplication app, IFeatureFlagService featureFlags)
    {
        var group = app.MapGroup("/api/tenants");

        if (!featureFlags.IsEnabled(FeatureFlagKeys.DisableOIDCAuth))
        {
            group.RequireAuthorization(policy => policy
                .RequireAuthenticatedUser()
                .AddAuthenticationSchemes(AuthTenancyExtensions.BearerScheme));
        }

        group.MapTenantRoutes();

        return app;
    }
}