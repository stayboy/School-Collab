using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.GradeLevels.Queries.GetGradeLevelById;

public sealed class GetGradeLevelByIdHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<GetGradeLevelById, GradeLevelDto?>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<GradeLevelDto?> HandleAsync(
        GetGradeLevelById query,
        CancellationToken cancellationToken = default)
    {
        return await cache.GetOrCreateAsync(
            $"grade-level:{query.Id}",
            (db, query.Id),
            static async (state, ct) =>
            {
                var (db, id) = state;
                var gradeLevel = await db.GradeLevels
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == id, ct);

                if (gradeLevel is null)
                    return null;

                return new GradeLevelDto(
                    gradeLevel.Id,
                    gradeLevel.CodedValueId,
                    gradeLevel.Level,
                    gradeLevel.Name,
                    gradeLevel.DisplayOrder,
                    0,
                    0,
                    gradeLevel.CreatedAt,
                    gradeLevel.UpdatedAt);
            },
            CacheOptions,
            tags: ["students"],
            cancellationToken: cancellationToken);
    }
}