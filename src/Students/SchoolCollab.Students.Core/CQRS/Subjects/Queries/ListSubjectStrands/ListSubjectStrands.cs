using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Subjects.Queries.ListSubjectStrands;

public sealed record ListSubjectStrands(Guid SubjectId) : IQuery<SubjectStrandDto[]>;

public sealed class ListSubjectStrandsHandler(StudentsDbContext db) : IQueryHandler<ListSubjectStrands, SubjectStrandDto[]>
{
    public async Task<SubjectStrandDto[]> HandleAsync(ListSubjectStrands query, CancellationToken ct = default)
    {
        return await db.SubjectStrands
            .AsNoTracking()
            .Where(x => x.SubjectId == query.SubjectId)
            .OrderBy(x => x.DisplayOrder)
            .Select(s => new SubjectStrandDto(
                s.Id,
                s.SubjectId,
                s.Name,
                s.Description,
                s.DisplayOrder,
                s.CreatedAt,
                s.UpdatedAt))
            .ToArrayAsync(ct);
    }
}