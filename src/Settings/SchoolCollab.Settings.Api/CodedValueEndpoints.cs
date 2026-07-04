using SchoolCollab.Core.Features;
using SchoolCollab.Settings.Api.Endpoints;

namespace SchoolCollab.Settings.Api;

public static class CodedValueEndpoints
{
    /// <summary>
    /// Maps the CodedValues aggregate endpoints (search, lookup, CRUD,
    /// lifecycle, attribute set/remove) under <c>/coded-values</c>. Carries over
    /// the legacy route prefix verbatim so the Admin UI and any external
    /// callers continue to work after the Settings merge. See
    /// documents/solution/settings-context-merge-spec.md §8.
    /// </summary>
    public static WebApplication MapCodedValueEndpoints(this WebApplication app, IFeatureFlagService featureFlags)
    {
        var group = app.MapGroup("/coded-values");

        if (!featureFlags.IsEnabled("FEATURE:DisableOIDCAuth"))
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
