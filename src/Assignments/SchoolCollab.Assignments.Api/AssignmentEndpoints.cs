using Microsoft.AspNetCore.Authorization;
using SchoolCollab.Assignments.Api.Endpoints;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Constants;
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

        // ── Teacher-portal reads (round teacher-scope-auth D4 / [P1-1]) ──────────
        // The reader policy is applied to this NESTED SUB-GROUP — never to the whole
        // /assignments group. The group also serves the Families ward/guardian routes, the
        // create-wizard reads (signature-default, ai-prompt-policy, recipient-preview, …) and
        // every create/edit/publish/approve write, all reachable by principals that carry no
        // role claim at all; a group-wide policy would 403 all of them. The sub-group carries
        // exactly the six GETs in the Covered table, mapped by MapAssignmentReaderRoutes.
        var readerGroup = group.MapGroup(string.Empty);
        if (!featureFlags.IsEnabled(FeatureFlagKeys.DisableOIDCAuth))
        {
            // [P1-2] the policy lands behind FEATURE:DisableOIDCAuth: with the flag ON there
            // is no policy and today's behaviour is unchanged. Rollout prerequisite: assign
            // teacher/staff (or an admin role) to every existing assignment-reading user in
            // Keycloak BEFORE the flag is flipped, or the policy 403s them.
            readerGroup.RequireAuthorization(RequireAssignmentReader);
        }
        readerGroup.MapAssignmentReaderRoutes();

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

    /// <summary>
    /// The one disjunctive teacher-portal reader policy: <c>teacher</c> ∨ <c>staff</c> ∨
    /// <c>user-admin</c> ∨ <c>platform-admin</c>, on the bearer scheme and authenticated. Built
    /// inline (the <c>AuthEndpointGroup.ConfigurePortalFacingAdminPolicy</c> precedent) rather
    /// than registered as a named policy: the hosts that compose this pipeline in tests call
    /// <c>AddAuthAndTenancy</c> without Assignments' <c>Program.cs</c>, and an unresolvable named
    /// policy throws out of <c>AuthorizationPolicy.Combine</c> on every request to a route that
    /// names it. The scheme and authenticated-user requirements are restated here so the policy
    /// is self-sufficient for the routes it decorates.
    /// </summary>
    internal static void RequireAssignmentReader(AuthorizationPolicyBuilder policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        policy
            .AddAuthenticationSchemes(AuthTenancyExtensions.BearerScheme)
            .RequireAuthenticatedUser()
            .RequireRole(
                RealmRoleNames.Teacher,
                RealmRoleNames.Staff,
                RealmRoleNames.UserAdmin,
                RealmRoleNames.PlatformAdmin);
    }
}
