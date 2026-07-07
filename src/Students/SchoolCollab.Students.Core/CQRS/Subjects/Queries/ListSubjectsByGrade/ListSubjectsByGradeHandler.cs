using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Subjects.Queries.ListSubjectsByGrade;

/// <summary>
/// Returns subjects assigned to a grade level.
///
/// <para>Subjects are <b>global</b> entities — a subject is not "owned by" a
/// period. What is period-scoped is the <see cref="Domain.GradeSubjectAssignment"/>
/// that links a subject to a grade. So:</para>
/// <list type="bullet">
///   <item>When <c>periodId</c> is <b>provided</b>, only subjects with a
///         <c>GradeSubjectAssignment</c> for that exact period are returned.</item>
///   <item>When <c>periodId</c> is <b>omitted</b>, all subjects ever assigned
///         to the grade (across every period) are returned. This keeps the
///         Subjects landing page useful when no current period exists — the
///         page should show the grade's subjects, not an empty grid.</item>
/// </list>
///
/// <para>The wizard uses this query without a <c>periodId</c> to populate the
/// subject-assignment grid, then calls <c>CreateSubjectForGrade</c> (which
/// does require a current period — it creates a new period-scoped assignment)
/// to wire new subjects into the current period.</para>
/// </summary>
public sealed class ListSubjectsByGradeHandler(StudentsDbContext db)
    : IQueryHandler<ListSubjectsByGrade, SubjectDto[]>
{
    public async Task<SubjectDto[]> HandleAsync(
        ListSubjectsByGrade query,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Domain.GradeSubjectAssignment> assignments = db.GradeSubjectAssignments
            .AsNoTracking()
            .Where(ga => ga.GradeLevelId == query.GradeLevelId);

        if (query.PeriodId is { } periodId)
        {
            assignments = assignments.Where(ga => ga.PeriodId == periodId);
        }

        var subjects = await assignments
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
            .Distinct()
            .OrderBy(s => s.DisplayOrder)
            .ToArrayAsync(cancellationToken);

        return subjects;
    }
}
