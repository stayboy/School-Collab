using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

/// <summary>
/// Repository for activity-group topic assignments (TPH subtype
/// <see cref="Domain.ActivityGroupTopicAssignment"/>).
/// </summary>
public interface IActivityGroupTopicAssignmentRepository : ITopicAssignmentRepository
{
    Task<TopicAssignmentDto[]> ListByActivityGroupAsync(Guid activityGroupId, DateOnly effectiveDate, CancellationToken cancellationToken = default);
}
