using Microsoft.AspNetCore.Authorization;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Features;
using SchoolCollab.Students.Api.Endpoints;

namespace SchoolCollab.Students.Api;

public static class StudentEndpoints
{
    public static WebApplication MapStudentEndpoints(this WebApplication app, IFeatureFlagService featureFlags)
    {
        var studentsGroup = app.MapGroup("/students");
        if (!featureFlags.IsEnabled(FeatureFlagKeys.DisableOIDCAuth))
        {
            // ar-24: receive forwarded bearer tokens from API-to-API hops, mirroring
            // AssignmentEndpoints.cs (real-auth DefaultScheme is Cookie; opt into Bearer).
            studentsGroup.RequireAuthorization(policy => policy
                .RequireAuthenticatedUser()
                .AddAuthenticationSchemes(AuthTenancyExtensions.BearerScheme));
        }

        // Each specialty owns its own routes + request records. Endpoints keep the
        // same URIs as before — only the source layout changed.
        studentsGroup
            .MapStudentRoutes()
            .MapGradeLevelRoutes()
            .MapTopicRoutes()
            .MapPeriodRoutes()
            .MapEnrollmentRoutes()
            .MapTopicAssignmentRoutes()
            .MapStudentTopicAssignmentRoutes()
            .MapStudentGuardianRoutes();   // G2: inherits RequireAuthorization from studentsGroup

        // Phase 2 (spec activity-group-enrollment.md §7.1/§7.2): activity-group
        // CRUD + membership endpoints. Gated behind FEATURE:EnableActivityGroups
        // (flag OFF by default — dark launch). Routes are root-level
        // (/activity-groups/*) except the student→groups query which is
        // /students/{id}/activity-groups.
        if (featureFlags.IsEnabled(FeatureFlagKeys.EnableActivityGroups))
        {
            var activityGroupsGroup = app.MapGroup("");
            if (!featureFlags.IsEnabled(FeatureFlagKeys.DisableOIDCAuth))
            {
                activityGroupsGroup.RequireAuthorization(policy => policy
                    .RequireAuthenticatedUser()
                    .AddAuthenticationSchemes(AuthTenancyExtensions.BearerScheme));
            }
            activityGroupsGroup.MapActivityGroupRoutes();
        }

        // Phase 3 (spec §9): guardians + contacts + subscriptions.
        // G2: management endpoints are admin/teacher-only (not student/anonymous) —
        // they require authorization. Role-based gating (Primary vs CC) is a later
        // refinement once role claims are issued by the IdP.
        var guardiansGroup = app.MapGroup("/guardians");
        if (!featureFlags.IsEnabled(FeatureFlagKeys.DisableOIDCAuth))
        {
            guardiansGroup.RequireAuthorization(policy => policy
                .RequireAuthenticatedUser()
                .AddAuthenticationSchemes(AuthTenancyExtensions.BearerScheme));
        }
        guardiansGroup.MapGuardianRoutes();

        var contactsGroup = app.MapGroup("/contacts");
        if (!featureFlags.IsEnabled(FeatureFlagKeys.DisableOIDCAuth))
        {
            contactsGroup.RequireAuthorization(policy => policy
                .RequireAuthenticatedUser()
                .AddAuthenticationSchemes(AuthTenancyExtensions.BearerScheme));
        }
        contactsGroup.MapContactRoutes();
        contactsGroup.MapSubscriptionRoutes();

        // Phase 8 (spec §4.12): teacher onboarding + subject/grade links.
        // G2: admin/teacher-only (RequireAuthorization inherited from the group).
        var teachersGroup = app.MapGroup("/teachers");
        if (!featureFlags.IsEnabled(FeatureFlagKeys.DisableOIDCAuth))
        {
            teachersGroup.RequireAuthorization(policy => policy
                .RequireAuthenticatedUser()
                .AddAuthenticationSchemes(AuthTenancyExtensions.BearerScheme));
        }
        teachersGroup.MapTeacherRoutes();

        return app;
    }
}
