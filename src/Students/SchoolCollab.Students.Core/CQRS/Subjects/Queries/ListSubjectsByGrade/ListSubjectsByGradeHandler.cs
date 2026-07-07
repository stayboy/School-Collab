using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Subjects.Queries.ListSubjectsByGrade;

/// <summary>
/// Returns all subjects assigned to a grade level for a given period.
/// If periodId is null, derives the current period server-side.
/// </summary>
public sealed class ListSubjectsByGradeHandler(StudentsDbContext db)
    : IQueryHandler<ListSubjectsByGrade, SubjectDto[]>
{
    public async Task<SubjectDto[]> HandleAsync(
        ListSubjectsByGrade query,
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
            // No current period → no subjects
            return [];
        }

        // Join GradeSubjectAssignments → Subjects to get subjects for this grade+period
        var subjects = await db.GradeSubjectAssignments
            .AsNoTracking()
            .Where(ga => ga.GradeLevelId == query.GradeLevelId && ga.PeriodId == periodId.Value)
            .Join(
                db.Subjects.AsNoTracking(),
                ga => ga.SubjectId,
                s => s.Id,
                (ga, s) => new SubjectDto(
                    s.Id,
                    s.CodedValueId,
                    s.Code,
                    s.Name,
                    s.DisplayOrder,
                    s.CreatedAt,
                    s.UpdatedAt))
            .OrderBy(s => s.DisplayOrder)
            .ToArrayAsync(cancellationToken);

        return subjects;
    }
}