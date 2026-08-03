using SchoolCollab.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Repositories;

/// <summary>
/// Concrete base repository for <see cref="TopicAssignment"/> (TPH root) that
/// shares the generic Get/Add/End behaviour across the grade and activity-group
/// subtypes.
/// </summary>
internal class TopicAssignmentRepository(StudentsDbContext db)
    : RepositoryBase<TopicAssignment, StudentsDbContext>(db), ITopicAssignmentRepository
{
    /// <summary>
    /// Ends the assignment's effective period on <paramref name="endDate"/> (block
    /// / archive). The row is retained for audit; it simply stops being effective.
    /// </summary>
    public async Task EndAsync(TopicAssignment assignment, DateOnly endDate, CancellationToken cancellationToken = default)
    {
        assignment.End(endDate);
        await UpdateAsync(assignment, cancellationToken);
    }
}
