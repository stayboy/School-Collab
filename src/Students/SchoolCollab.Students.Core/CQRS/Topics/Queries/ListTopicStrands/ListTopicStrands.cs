using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Topics.Queries.ListTopicStrands;

public sealed record ListTopicStrands(Guid TopicId) : IQuery<TopicStrandDto[]>;

public sealed class ListTopicStrandsHandler(StudentsDbContext db) : IQueryHandler<ListTopicStrands, TopicStrandDto[]>
{
    public async Task<TopicStrandDto[]> HandleAsync(ListTopicStrands query, CancellationToken ct = default)
    {
        return await db.TopicStrands
            .AsNoTracking()
            .Where(x => x.TopicId == query.TopicId)
            .OrderBy(x => x.DisplayOrder)
            .Select(s => new TopicStrandDto(
                s.Id,
                s.TopicId,
                s.Name,
                s.Description,
                s.DisplayOrder,
                s.CreatedAt,
                s.UpdatedAt))
            .ToArrayAsync(ct);
    }
}