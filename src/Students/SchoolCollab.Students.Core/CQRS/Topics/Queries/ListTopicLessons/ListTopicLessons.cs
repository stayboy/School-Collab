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
        var q = db.TopicLessons
            .AsNoTracking()
            .Where(x => x.TopicId == query.TopicId);

        if (query.StrandId.HasValue)
        {
            q = q.Where(x => x.StrandId == query.StrandId);
        }

        return await q
            .OrderBy(x => x.DisplayOrder)
            .Select(l => new TopicLessonDto(
                l.Id,
                l.TopicId,
                l.StrandId,
                l.Name,
                l.Description,
                l.StartDate,
                l.EndDate,
                l.IsOpenEnded,
                l.DisplayOrder,
                l.CreatedAt,
                l.UpdatedAt))
            .ToArrayAsync(ct);
    }
}