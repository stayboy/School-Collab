using SchoolCollab.Assignments.Api.Endpoints;
using SchoolCollab.Core.Features;

namespace SchoolCollab.Assignments.Api;

public static class AssignmentEndpoints
{
    public static WebApplication MapAssignmentEndpoints(this WebApplication app, IFeatureFlagService featureFlags)
    {
        // All assignment endpoints require an authenticated user and a resolved TenantContext
        var group = app.MapGroup("/assignments");

        if (!featureFlags.IsEnabled(FeatureFlagKeys.DisableOIDCAuth))
        {
            group.RequireAuthorization();
        }

        group.MapAssignmentRoutes();

        return app;
    }
}
