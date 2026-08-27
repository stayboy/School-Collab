using Microsoft.EntityFrameworkCore;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

internal sealed class ActivityGroupTopicAssignmentRepository(StudentsDbContext db)
    : TopicAssignmentRepository(db), IActivityGroupTopicAssignmentRepository
{
    public async Task<TopicAssignmentDto[]> ListByActivityGroupAsync(Guid activityGroupId, DateOnly effectiveDate, CancellationToken cancellationToken = default) =>
        await Db.ActivityGroupTopicAssignments
            .AsNoTracking()
            .Where(x => x.ActivityGroupId == activityGroupId
                && x.StartDate <= effectiveDate
                && (x.EndDate == null || x.EndDate >= effectiveDate))
            .OrderBy(x => x.TopicId)
            .Select(x => new TopicAssignmentDto(
                x.Id, "activity_group", null, x.ActivityGroupId, x.TopicId,
                x.StartDate, x.EndDate,
                x.TopicStrandId,
                x.PeriodId,
                x.CreatedAt, x.UpdatedAt))
            .ToArrayAsync(cancellationToken);
}
