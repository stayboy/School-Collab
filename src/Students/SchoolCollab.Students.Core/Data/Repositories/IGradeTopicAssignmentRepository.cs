using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

/// <summary>
/// Repository for grade-level topic assignments (TPH subtype
/// <see cref="Domain.GradeTopicAssignment"/>).
/// </summary>
public interface IGradeTopicAssignmentRepository : ITopicAssignmentRepository
{
    Task<TopicAssignmentDto[]> ListByGradeLevelAsync(Guid gradeLevelId, DateOnly effectiveDate, CancellationToken cancellationToken = default);
}
