using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Authorization;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Features;
using SchoolCollab.Settings.Api.Endpoints;

namespace SchoolCollab.Settings.Api;

public static class AssignmentAiPromptEndpoints
{
    /// <summary>
    /// Maps the per-tenant organization AI prompt under
    /// <c>/api/settings/assignment-ai-prompt</c>. Cookie-gated when OIDC is on;
    /// open under TestAuth in dev (the tenant context is still enforced by the
    /// tenant query filter, so reads/writes stay scoped to the caller's tenant).
    /// WS-B2 / spec §3.4.
    /// </summary>
    public static WebApplication MapAssignmentAiPromptEndpoints(this WebApplication app, IFeatureFlagService featureFlags)
    {
        var group = app.MapGroup("/api/settings");

        if (!featureFlags.IsEnabled(FeatureFlagKeys.DisableOIDCAuth))
        {
            group.RequireAuthorization(policy => policy
                .RequireAuthenticatedUser()
                .AddAuthenticationSchemes(AuthTenancyExtensions.BearerScheme));
        }

        group.MapAssignmentAiPromptRoutes();

        return app;
    }
}
