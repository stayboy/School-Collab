using SchoolCollab.Core.Features;
using SchoolCollab.Settings.Api.Endpoints;

namespace SchoolCollab.Settings.Api;

public static class ConfigEndpoints
{
    /// <summary>
    /// Maps the FeatureFlag aggregate endpoints under <c>/api/config</c> (CRUD,
    /// audit, tenant overrides) plus the consumer-facing resolve routes at
    /// <c>/api/features/{global|tenantId}</c>. In OIDC-disabled dev, write
    /// endpoints skip the role policy (TestAuth has no role). See
    /// documents/solution/settings-context-merge-spec.md §8.
    /// </summary>
    public static WebApplication MapConfigEndpoints(this WebApplication app, IFeatureFlagService featureFlags)
    {
        var oidcEnabled = !featureFlags.IsEnabled(FeatureFlagKeys.DisableOIDCAuth);
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
            .MapConfigAcademicYearDivisionRoutes(requireFlagAdmin)
            .MapConfigAuditRoutes();

        app.MapConfigResolveRoutes(oidcEnabled);

        return app;
    }
}
