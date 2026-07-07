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

        // Query students enrolled in the specified grade for the specified period
        var students = await db.StudentEnrollments
            .AsNoTracking()
            .Where(se => se.GradeLevelId == query.GradeLevelId
                      && se.PeriodId == periodId.Value
                      && se.Status == EnrollmentStatus.Active)
            .Join(db.Students,
                se => se.StudentId,
                s => s.Id,
                (se, s) => new StudentDto(
                    s.Id,
                    s.StudentNumber,
                    s.FirstName,
                    s.LastName,
                    s.DateOfBirth,
                    s.GenderCodedValueId,
                    s.ContactEmail,
                    s.ContactPhone,
                    s.IsDeleted,
                    s.CreatedAt,
                    s.UpdatedAt))
            .OrderBy(s => s.LastName)
            .ThenBy(s => s.FirstName)
            .ToArrayAsync(cancellationToken);

        return students;
    }
}