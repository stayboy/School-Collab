using SchoolCollab.Core.Features;
using SchoolCollab.Settings.Api.Endpoints;

namespace SchoolCollab.Settings.Api;

public static class CodedValueEndpoints
{
    /// <summary>
    /// Maps the CodedValues aggregate endpoints (search, lookup, CRUD,
    /// lifecycle, attribute set/remove) under <c>/api/coded-values</c>. The
    /// <c>/api/</c> prefix matches the Config aggregate's <c>/api/config</c>
    /// and <c>/api/features</c> routes so the unified Settings API uses one
    /// consistent URL convention across both aggregates. See
    /// documents/solution/settings-context-merge-spec.md §8.
    /// </summary>
    public static WebApplication MapCodedValueEndpoints(this WebApplication app, IFeatureFlagService featureFlags)
    {
        var group = app.MapGroup("/api/coded-values");

        if (!featureFlags.IsEnabled(FeatureFlagKeys.DisableOIDCAuth))
        {
            group.RequireAuthorization();
        }

        group
            .MapCodedValueRoutes()
            .MapCodedValueAttributeRoutes()
            .MapCodedValueAttributeDefinitionRoutes();

        return app;
    }
}
