using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Features;
using SchoolCollab.Students.Api.Endpoints;

namespace SchoolCollab.Students.Api;

public static class StudentEndpoints
{
    public static WebApplication MapStudentEndpoints(this WebApplication app, IFeatureFlagService featureFlags)
    {
        // ar-24: receive forwarded bearer tokens from API-to-API hops, mirroring
        // AssignmentEndpoints.cs (real-auth DefaultScheme is Cookie; opt into Bearer).
        var studentsGroup = app.MapGroup("/students");
        studentsGroup.RequireAuthIfOidc(featureFlags);

        // Each specialty owns its own routes + request records. Endpoints keep the
        // same URIs as before — only the source layout changed.
        studentsGroup
            .MapStudentRoutes()
            .MapGradeLevelRoutes()
            .MapTopicRoutes()
            .MapPeriodRoutes()
            .MapEnrollmentRoutes()
            .MapTopicAssignmentRoutes()
            .MapEnrollmentExceptionRoutes()
            .MapStudentTopicAssignmentRoutes()
            .MapStudentGuardianRoutes();   // G2: inherits RequireAuthorization from studentsGroup

        // Phase 2 (spec activity-group-enrollment.md §7.1/§7.2): activity-group CRUD + membership
        // endpoints. Routes are root-level (/activity-groups/*) except the student→groups query, which
        // is /students/{id}/activity-groups.
        //
        // ALWAYS MAPPED (owner, 2026-10-26). The previous FEATURE:EnableActivityGroups gate was
        // removed along with the flag: the feature is established, so the routes are part of the host
        // rather than an option. Removing the gate also removed a class of defect, because route
        // MAPPING is a startup decision taken with no tenant in scope — a tenant-scoped runtime flag
        // could never have unmasked these routes, and this host reads IConfiguration only
        // (issue #416).
        var activityGroupsGroup = app.MapGroup("");
        activityGroupsGroup.RequireAuthIfOidc(featureFlags);
        activityGroupsGroup.MapActivityGroupRoutes();

        // Phase 3 (spec §9): guardians + contacts + subscriptions.
        // G2: management endpoints are admin/teacher-only (not student/anonymous) —
        // they require authorization. Role-based gating (Primary vs CC) is a later
        // refinement once role claims are issued by the IdP.
        var guardiansGroup = app.MapGroup("/guardians");
        guardiansGroup.RequireAuthIfOidc(featureFlags);
        guardiansGroup.MapGuardianRoutes();

        var contactsGroup = app.MapGroup("/contacts");
        contactsGroup.RequireAuthIfOidc(featureFlags);
        contactsGroup.MapContactRoutes();
        contactsGroup.MapSubscriptionRoutes();

        // Phase 8 (spec §4.12): teacher onboarding + subject/grade links.
        // G2: admin/teacher-only (RequireAuthorization inherited from the group).
        var teachersGroup = app.MapGroup("/teachers");
        teachersGroup.RequireAuthIfOidc(featureFlags);
        teachersGroup.MapTeacherRoutes();

        return app;
    }
}
