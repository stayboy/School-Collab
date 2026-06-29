using SchoolCollab.CodedValues.Api.Endpoints;
using SchoolCollab.Core.Features;

namespace SchoolCollab.CodedValues.Api;

public static class CodedValueEndpoints
{
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
