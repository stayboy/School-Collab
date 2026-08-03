using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

internal sealed class GradeSubjectAssignmentRepository(StudentsDbContext db)
    : RepositoryBase<GradeSubjectAssignment, StudentsDbContext>(db), IGradeSubjectAssignmentRepository
{
    public async Task<GradeSubjectAssignmentDto[]> ListByGradeLevelAsync(Guid gradeLevelId, DateOnly effectiveDate, CancellationToken cancellationToken = default) =>
        await Db.GradeSubjectAssignments
            .AsNoTracking()
            .Where(x => x.GradeLevelId == gradeLevelId
                && x.StartDate <= effectiveDate
                && (x.EndDate == null || x.EndDate >= effectiveDate))
            .OrderBy(x => x.TopicId)
            .Select(x => new GradeSubjectAssignmentDto(
                x.Id, x.GradeLevelId, x.ActivityGroupId, x.TopicId,
                x.StartDate, x.EndDate,
                x.TopicStrandId, x.TopicLessonId,
                x.CreatedAt, x.UpdatedAt))
            .ToArrayAsync(cancellationToken);

    /// <summary>
    /// Ends the assignment's effective period on <paramref name="endDate"/> (block
    /// / archive). The row is retained for audit; it simply stops being effective.
    /// </summary>
    public async Task EndAsync(GradeSubjectAssignment assignment, DateOnly endDate, CancellationToken cancellationToken = default)
    {
        assignment.End(endDate);
        await UpdateAsync(assignment, cancellationToken);
    }
}
