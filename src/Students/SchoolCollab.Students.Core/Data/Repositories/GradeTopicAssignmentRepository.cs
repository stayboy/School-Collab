using Microsoft.EntityFrameworkCore;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

internal sealed class GradeTopicAssignmentRepository(StudentsDbContext db)
    : TopicAssignmentRepository(db), IGradeTopicAssignmentRepository
{
    public async Task<TopicAssignmentDto[]> ListByGradeLevelAsync(Guid gradeLevelId, DateOnly effectiveDate, CancellationToken cancellationToken = default) =>
        await Db.GradeTopicAssignments
            .AsNoTracking()
            .Where(x => x.GradeLevelId == gradeLevelId
                && x.StartDate <= effectiveDate
                && (x.EndDate == null || x.EndDate >= effectiveDate))
            .OrderBy(x => x.TopicId)
            .Select(x => new TopicAssignmentDto(
                x.Id, "grade", x.GradeLevelId, null, x.TopicId,
                x.StartDate, x.EndDate,
                x.TopicStrandId,
                x.PeriodId,
                x.CreatedAt, x.UpdatedAt))
            .ToArrayAsync(cancellationToken);
}
