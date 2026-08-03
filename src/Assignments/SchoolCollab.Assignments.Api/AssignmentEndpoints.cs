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

        // Phase 3 (spec activity-group-enrollment.md §7.3): assignment ↔ group
        // link endpoints + the FR-6 delete-guard query. Gated behind
        // FEATURE:EnableActivityGroups (flag OFF by default — dark launch).
        // Mounted at the root so /activity-groups/{id}/assignments matches the
        // Students API's group route namespace.
        if (featureFlags.IsEnabled(FeatureFlagKeys.EnableActivityGroups))
        {
            var activityGroupsGroup = app.MapGroup("");
            if (!featureFlags.IsEnabled(FeatureFlagKeys.DisableOIDCAuth))
            {
                activityGroupsGroup.RequireAuthorization();
            }
            activityGroupsGroup.MapActivityGroupLinkRoutes();
        }

        return app;
    }
}
