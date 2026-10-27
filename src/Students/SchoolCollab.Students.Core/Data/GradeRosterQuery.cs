using Microsoft.EntityFrameworkCore;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data;

/// <summary>
/// The "grade roster" read shared by the eligibility endpoint
/// (<c>GET /students/topic-enrollment</c>) and the <c>RequiresEnrollment</c> switch's
/// grandfather: the students with an <see cref="EnrollmentStatus.Active"/> enrolment in the
/// grade. Tenant scoping comes from the ambient global query filter on
/// <see cref="StudentEnrollment"/> — a grade (and its students) belonging to another tenant
/// yields nothing.
/// </summary>
internal static class GradeRosterQuery
{
    /// <summary>
    /// Active students in <paramref name="gradeLevelId"/>, ordered by student id so the
    /// answer is deterministic. When <paramref name="academicYearId"/> is supplied the
    /// roster is that academic year's (the normal case — enrolment is per-year, FR-ER-3);
    /// a <c>null</c> year (no active academic year resolved) falls back to the grade's
    /// active enrolments across periods, so the audience the publish filter subtracts from
    /// stays populated while nothing counts as enrolled.
    /// </summary>
    public static async Task<Guid[]> ActiveGradeRosterAsync(
        this StudentsDbContext db,
        Guid gradeLevelId,
        Guid? academicYearId,
        CancellationToken cancellationToken = default)
    {
        var query = db.StudentEnrollments
            .AsNoTracking()
            .Where(x => x.GradeLevelId == gradeLevelId && x.Status == EnrollmentStatus.Active);

        if (academicYearId is { } yearId)
            query = query.Where(x => x.PeriodId == yearId);

        return await query
            .Select(x => x.StudentId)
            .Distinct()
            .OrderBy(x => x)
            .ToArrayAsync(cancellationToken);
    }
}
