using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Students.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Queries.ListSubjects;

public sealed class ListSubjectsHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListSubjects, SubjectDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<SubjectDto[]> HandleAsync(
        ListSubjects query,
        CancellationToken cancellationToken = default)
    {
        return await cache.GetOrCreateAsync(
            "subjects:list",
            db,
            static async (db, ct) =>
            {
                var results = await db.Subjects
                    .AsNoTracking()
                    .OrderBy(x => x.DisplayOrder)
                    .ThenBy(x => x.Name)
                    .ToArrayAsync(ct);

                return results.Select(s => new SubjectDto(
                    s.Id,
                    s.CodedValueId,
                    s.Code,
                    s.Name,
                    s.DisplayOrder,
                    s.CreatedAt,
                    s.UpdatedAt)).ToArray();
            },
            CacheOptions,
            tags: ["students"],
            cancellationToken: cancellationToken);
    }
}