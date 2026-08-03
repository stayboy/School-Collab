using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Repositories;

/// <summary>
/// Base contract for a <see cref="TopicAssignment"/> (TPH root) bridge row.
/// Shared by the grade and activity-group subtypes; audience-specific listing
/// lives on the derived interfaces.
/// </summary>
public interface ITopicAssignmentRepository
{
    Task<TopicAssignment?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(TopicAssignment assignment, CancellationToken cancellationToken = default);
    Task EndAsync(TopicAssignment assignment, DateOnly endDate, CancellationToken cancellationToken = default);
}
