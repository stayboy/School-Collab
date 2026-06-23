using Microsoft.EntityFrameworkCore;
using SchoolCollab.Students.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Queries.ListSubjectLessons;

public sealed record ListSubjectLessons(Guid SubjectId, Guid? StrandId = null) : IQuery<SubjectLessonDto[]>;

public sealed class ListSubjectLessonsHandler(StudentsDbContext db) : IQueryHandler<ListSubjectLessons, SubjectLessonDto[]>
{
    public async Task<SubjectLessonDto[]> HandleAsync(ListSubjectLessons query, CancellationToken ct = default)
    {
        var q = db.SubjectLessons
            .AsNoTracking()
            .Where(x => x.SubjectId == query.SubjectId);

        if (query.StrandId.HasValue)
        {
            q = q.Where(x => x.StrandId == query.StrandId);
        }

        return await q
            .OrderBy(x => x.DisplayOrder)
            .Select(l => new SubjectLessonDto(
                l.Id,
                l.SubjectId,
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