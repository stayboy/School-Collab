using SchoolCollab.Core.Features;
using SchoolCollab.Students.Api.Endpoints;

namespace SchoolCollab.Students.Api;

public static class StudentEndpoints
{
    public static WebApplication MapStudentEndpoints(this WebApplication app, IFeatureFlagService featureFlags)
    {
        var studentsGroup = app.MapGroup("/students");
        if (!featureFlags.IsEnabled("FEATURE:DisableOIDCAuth"))
        {
            studentsGroup.RequireAuthorization();
        }

        // Each specialty owns its own routes + request records. Endpoints keep the
        // same URIs as before — only the source layout changed.
        studentsGroup
            .MapStudentRoutes()
            .MapGradeLevelRoutes()
            .MapSubjectRoutes()
            .MapPeriodRoutes()
            .MapEnrollmentRoutes()
            .MapGradeSubjectAssignmentRoutes()
            .MapStudentSubjectAssignmentRoutes()
            .MapStudentGuardianRoutes();   // G2: inherits RequireAuthorization from studentsGroup

        // Phase 3 (spec §9): guardians + contacts + subscriptions.
        // G2: management endpoints are admin/teacher-only (not student/anonymous) —
        // they require authorization. Role-based gating (Primary vs CC) is a later
        // refinement once role claims are issued by the IdP.
        var guardiansGroup = app.MapGroup("/guardians");
        if (!featureFlags.IsEnabled("FEATURE:DisableOIDCAuth"))
        {
            guardiansGroup.RequireAuthorization();
        }
        guardiansGroup.MapGuardianRoutes();

        var contactsGroup = app.MapGroup("/contacts");
        if (!featureFlags.IsEnabled("FEATURE:DisableOIDCAuth"))
        {
            contactsGroup.RequireAuthorization();
        }
        contactsGroup.MapContactRoutes();
        contactsGroup.MapSubscriptionRoutes();

        // Phase 8 (spec §4.12): teacher onboarding + subject/grade links.
        // G2: admin/teacher-only (RequireAuthorization inherited from the group).
        var teachersGroup = app.MapGroup("/teachers");
        if (!featureFlags.IsEnabled("FEATURE:DisableOIDCAuth"))
        {
            teachersGroup.RequireAuthorization();
        }
        teachersGroup.MapTeacherRoutes();

        return app;
    }
}
