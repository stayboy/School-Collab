using SchoolCollab.Core.Features;
using SchoolCollab.Settings.Api.Endpoints;

namespace SchoolCollab.Settings.Api;

public static class AssignmentPolicyEndpoints
{
    /// <summary>
    /// Maps the per-tenant default guardian-signature policy under
    /// <c>/api/settings/assignment-policy</c>. Cookie-gated when OIDC is on; open
    /// under TestAuth in dev (the tenant context is still enforced by the tenant
    /// query filter, so reads/writes stay scoped to the caller's tenant).
    /// </summary>
    public static WebApplication MapAssignmentPolicyEndpoints(this WebApplication app, IFeatureFlagService featureFlags)
    {
        var group = app.MapGroup("/api/settings");

        if (!featureFlags.IsEnabled(FeatureFlagKeys.DisableOIDCAuth))
        {
            group.RequireAuthorization();
        }

        group.MapAssignmentPolicyRoutes();

        return app;
    }
}