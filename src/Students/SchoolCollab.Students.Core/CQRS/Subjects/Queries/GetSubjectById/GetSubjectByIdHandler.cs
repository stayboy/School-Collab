using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Subjects.Queries.GetSubjectById;

public sealed class GetSubjectByIdHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<GetSubjectById, SubjectDto?>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<SubjectDto?> HandleAsync(
        GetSubjectById query,
        CancellationToken cancellationToken = default)
    {
        return await cache.GetOrCreateAsync(
            $"subject:{query.Id}",
            (db, query.Id),
            static async (state, ct) =>
            {
                var (db, id) = state;
                var subject = await db.Subjects
                    .IgnoreQueryFilters(["Tenant"])
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == id, ct);

                if (subject is null)
                    return null;

                return new SubjectDto(
                    subject.Id,
                    subject.CodedValueId,
                    subject.Code,
                    subject.Name,
                    subject.DisplayOrder,
                    subject.CreatedAt,
                    subject.UpdatedAt);
            },
            CacheOptions,
            tags: ["students"],
            cancellationToken: cancellationToken);
    }
}