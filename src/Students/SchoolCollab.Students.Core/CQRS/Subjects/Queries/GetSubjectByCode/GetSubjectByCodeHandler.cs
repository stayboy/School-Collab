using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Subjects.Queries.GetSubjectByCode;

public sealed class GetSubjectByCodeHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<GetSubjectByCode, SubjectDto?>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<SubjectDto?> HandleAsync(
        GetSubjectByCode query,
        CancellationToken cancellationToken = default)
    {
        var normalisedCode = query.Code.Trim().ToUpperInvariant();
        var cacheKey = $"subject:code:{normalisedCode}";

        return await cache.GetOrCreateAsync(
            cacheKey,
            (db, normalisedCode),
            static async (state, ct) =>
            {
                var (db, code) = state;
                var subject = await db.Subjects
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Code == code, ct);

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