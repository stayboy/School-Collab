using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Topics.Queries.ListTopicStrands;

public sealed record ListTopicStrands(Guid TopicId, Guid? ParentStrandId = null) : IQuery<TopicStrandDto[]>;

public sealed class ListTopicStrandsHandler(StudentsDbContext db) : IQueryHandler<ListTopicStrands, TopicStrandDto[]>
{
    public async Task<TopicStrandDto[]> HandleAsync(ListTopicStrands query, CancellationToken ct = default)
    {
        var q = db.TopicStrands
            .AsNoTracking()
            .Where(x => x.TopicId == query.TopicId);

        // Optional filter to a parent's children (lessons under a strand).
        if (query.ParentStrandId.HasValue)
        {
            q = q.Where(x => x.ParentStrandId == query.ParentStrandId);
        }

        return await q
            .OrderBy(x => x.DisplayOrder)
            .Select(s => TopicStrandDto.FromStrand(s))
            .ToArrayAsync(ct);
    }
}
