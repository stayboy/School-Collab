using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Topics.Queries.ListTopicsByGroup;

/// <summary>
/// Returns the shared topics assigned to an activity group on an effective date.
/// Mirrors <see cref="ListTopicsByGrade"/> for the activity-group owner side of the
/// <c>GradeSubjectAssignment</c> bridge (subject-to-topic-polymorphism.md §2.4).
/// </summary>
public sealed class ListTopicsByGroupHandler(StudentsDbContext db)
    : IQueryHandler<ListTopicsByGroup, TopicDto[]>
{
    public async Task<TopicDto[]> HandleAsync(
        ListTopicsByGroup query,
        CancellationToken cancellationToken = default)
    {
        var effectiveDate = query.EffectiveDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var topicIds = db.ActivityGroupTopicAssignments
            .AsNoTracking()
            .Where(a => a.ActivityGroupId == query.ActivityGroupId
                && a.StartDate <= effectiveDate
                && (a.EndDate == null || a.EndDate >= effectiveDate));

        var ids = await topicIds
            .Select(a => a.TopicId)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        var subjects = await db.Topics
            .AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .OrderBy(t => t.DisplayOrder)
            .ThenBy(t => t.Name)
            .ToArrayAsync(cancellationToken);

        return subjects
            .Select(s => new TopicDto(
                s.Id,
                s.CodedValueId,
                s.Code,
                s.Name,
                s.Description,
                s.DisplayOrder,
                s.CreatedAt,
                s.UpdatedAt))
            .ToArray();
    }
}
