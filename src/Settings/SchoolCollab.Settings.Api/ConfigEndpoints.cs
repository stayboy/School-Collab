using Microsoft.AspNetCore.Authorization;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Features;
using SchoolCollab.Settings.Api.Endpoints;

namespace SchoolCollab.Settings.Api;

public static class ConfigEndpoints
{
    /// <summary>
    /// Maps the FeatureFlag aggregate endpoints under <c>/api/config</c> (CRUD,
    /// audit, tenant overrides) plus the consumer-facing resolve routes at
    /// <c>/api/features/{global|tenantId}</c>. When OIDC is enabled, the whole
    /// <c>/api/config</c> group — including the flag-write and tenant-override
    /// routes — is protected by the group-level bearer opt-in below; under
    /// TestAuth (dev) the group stays open. See
    /// documents/solution/settings-context-merge-spec.md §8.
    /// </summary>
    public static WebApplication MapConfigEndpoints(this WebApplication app, IFeatureFlagService featureFlags)
    {
        var oidcEnabled = !featureFlags.IsEnabled(FeatureFlagKeys.DisableOIDCAuth);

        var group = app.MapGroup("/api/config");

        // Reads and writes (flags, audit, tenant overrides) are bearer-gated when OIDC is on;
        // open under TestAuth in dev.
        if (oidcEnabled)
        {
            group.RequireAuthorization(policy => policy
                .RequireAuthenticatedUser()
                .AddAuthenticationSchemes(AuthTenancyExtensions.BearerScheme));
        }

        group
            .MapConfigFlagRoutes()
            .MapConfigTenantOverrideRoutes()
            .MapConfigAuditRoutes();

        app.MapConfigResolveRoutes(oidcEnabled);

        return app;
    }
}
