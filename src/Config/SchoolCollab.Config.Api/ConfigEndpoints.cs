using SchoolCollab.Config.Api.Endpoints;
using SchoolCollab.Core.Features;

namespace SchoolCollab.Config.Api;

public static class ConfigEndpoints
{
    public static WebApplication MapConfigEndpoints(this WebApplication app, IFeatureFlagService featureFlags)
    {
        var oidcEnabled = !featureFlags.IsEnabled("FEATURE:DisableOIDCAuth");
        var requireFlagAdmin = oidcEnabled; // skip the role policy in dev (TestAuth has no role)

        var group = app.MapGroup("/api/config");

        // Reads (flags, audit) are cookie-gated when OIDC is on; open under TestAuth in dev.
        if (oidcEnabled)
        {
            group.RequireAuthorization();
        }

        group
            .MapConfigFlagRoutes(requireFlagAdmin)
            .MapConfigTenantOverrideRoutes(requireFlagAdmin)
            .MapConfigAuditRoutes();

        app.MapConfigResolveRoutes(oidcEnabled);

        return app;
    }
}