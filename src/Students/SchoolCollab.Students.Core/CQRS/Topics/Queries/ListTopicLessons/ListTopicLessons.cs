using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Topics.Queries.ListTopicLessons;

public sealed record ListTopicLessons(Guid TopicId, Guid? StrandId = null) : IQuery<TopicLessonDto[]>;

public sealed class ListTopicLessonsHandler(StudentsDbContext db) : IQueryHandler<ListTopicLessons, TopicLessonDto[]>
{
    public async Task<TopicLessonDto[]> HandleAsync(ListTopicLessons query, CancellationToken ct = default)
    {
        // A lesson is a strand with a parent (strand-lesson-unification-plan.md).
        var q = db.TopicStrands
            .AsNoTracking()
            .Where(x => x.TopicId == query.TopicId && x.ParentStrandId != null);

        if (query.StrandId.HasValue)
        {
            q = q.Where(x => x.ParentStrandId == query.StrandId);
        }

        return await q
            .OrderBy(x => x.DisplayOrder)
            .Select(s => new TopicLessonDto(
                s.Id,
                s.TopicId,
                s.ParentStrandId,
                s.Name,
                s.Description,
                s.StartDate,
                s.EndDate,
                s.IsOpenEnded,
                s.DisplayOrder,
                s.CreatedAt,
                s.UpdatedAt))
            .ToArrayAsync(ct);
    }
}
