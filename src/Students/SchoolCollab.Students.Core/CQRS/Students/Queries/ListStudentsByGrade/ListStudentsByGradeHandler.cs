using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Students.Queries.ListStudentsByGrade;

/// <summary>
/// Returns all students enrolled in a specific grade level for a given period.
/// Students are tenant-scoped via the Student global query filter.
/// </summary>
public sealed class ListStudentsByGradeHandler(
    StudentsDbContext db,
    ITenantProvider tenantProvider) : IQueryHandler<ListStudentsByGrade, StudentDto[]>
{
    public async Task<StudentDto[]> HandleAsync(
        ListStudentsByGrade query,
        CancellationToken cancellationToken = default)
    {
        Guid? periodId = query.PeriodId;

        // Derive current period if not provided
        if (periodId is null)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var currentPeriod = await db.Periods
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.StartDate <= today && p.EndDate >= today, cancellationToken);
            periodId = currentPeriod?.Id;
        }

        if (periodId is null)
        {
            // No current period → no students
            return [];
        }

        // Query students enrolled in the specified grade for the specified period.
        // Order on an anonymous projection FIRST, then project into the DTO LAST:
        // EF Core's relational provider cannot translate OrderBy/ThenBy applied to a
        // custom-type (StudentDto) projection — it treats that as a terminal client
        // projection and throws InvalidOperationException ("could not be translated")
        // at runtime, even though the InMemory provider (used by unit tests) evaluates
        // it client-side and passes. Ordering on the anonymous type keeps the ORDER BY
        // in SQL; the final Select to StudentDto is the last, translatable step.
        var students = await db.StudentEnrollments
            .AsNoTracking()
            .Where(se => se.GradeLevelId == query.GradeLevelId
                      && se.PeriodId == periodId.Value
                      && se.Status == EnrollmentStatus.Active)
            .Join(db.Students,
                se => se.StudentId,
                s => s.Id,
                (se, s) => new
                {
                    s.Id,
                    s.StudentNumber,
                    s.TitleCodedValueId,
                    s.FirstName,
                    s.LastName,
                    s.DateOfBirth,
                    s.GenderCodedValueId,
                    s.IsDeleted,
                    s.CreatedAt,
                    s.UpdatedAt
                })
            .OrderBy(x => x.LastName)
            .ThenBy(x => x.FirstName)
            .Select(x => new StudentDto(
                x.Id,
                x.StudentNumber,
                x.TitleCodedValueId,
                x.FirstName,
                x.LastName,
                x.DateOfBirth,
                x.GenderCodedValueId,
                x.IsDeleted,
                x.CreatedAt,
                x.UpdatedAt))
            .ToArrayAsync(cancellationToken);

        return students;
    }
}