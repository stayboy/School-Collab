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
            .MapStudentSubjectAssignmentRoutes();

        return app;
    }
}
