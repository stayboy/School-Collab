using Microsoft.AspNetCore.Authorization;
using SchoolCollab.Assignments.Api.Endpoints;
using SchoolCollab.Core.Auth;
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
            // ar-20 real-auth mode: assignments API groups authenticate via the bearer JWT
            // scheme (Keycloak access tokens). DefaultScheme stays Cookie for the Admin/
            // Families browser flows; this group opts into Bearer explicitly (decision 2/3).
            group.RequireAuthorization(policy => policy
                .RequireAuthenticatedUser()
                .AddAuthenticationSchemes(AuthTenancyExtensions.BearerScheme));
        }

        group.MapAssignmentRoutes();

        // WS-A5 (spec §3.3): the ward assignment list, mounted under a sibling
        // /students group so the resource path is GET /students/{studentId}/assignments
        // (not under /assignments). Same auth posture as the assignments group.
        var wardGroup = app.MapGroup("/students");
        if (!featureFlags.IsEnabled(FeatureFlagKeys.DisableOIDCAuth))
        {
            wardGroup.RequireAuthorization(policy => policy
                .RequireAuthenticatedUser()
                .AddAuthenticationSchemes(AuthTenancyExtensions.BearerScheme));
        }
        wardGroup.MapWardAssignmentRoutes();

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
                activityGroupsGroup.RequireAuthorization(policy => policy
                    .RequireAuthenticatedUser()
                    .AddAuthenticationSchemes(AuthTenancyExtensions.BearerScheme));
            }
            activityGroupsGroup.MapActivityGroupLinkRoutes();
        }

        return app;
    }
}
