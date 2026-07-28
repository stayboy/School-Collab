using SchoolCollab.Core.Features;
using SchoolCollab.Settings.Api.Endpoints;

namespace SchoolCollab.Settings.Api;

public static class EntityCodeRuleEndpoints
{
    /// <summary>
    /// Maps the EntityCodeRule admin endpoints under <c>/api/entity-code-rules</c>
    /// (list/get/create/update/delete/activate). Spec §4.7. Authorization
    /// mirrors the CodedValues routes (admin-only).
    /// </summary>
    public static WebApplication MapEntityCodeRuleEndpoints(this WebApplication app, IFeatureFlagService featureFlags)
    {
        var group = app.MapGroup("/api/entity-code-rules");

        if (!featureFlags.IsEnabled(FeatureFlagKeys.DisableOIDCAuth))
        {
            group.RequireAuthorization();
        }

        group.MapEntityCodeRuleRoutes();
        return app;
    }
}