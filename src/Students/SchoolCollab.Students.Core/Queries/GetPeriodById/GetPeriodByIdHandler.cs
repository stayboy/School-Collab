using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Students.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Queries.GetPeriodById;

public sealed class GetPeriodByIdHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<GetPeriodById, PeriodDto?>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<PeriodDto?> HandleAsync(
        GetPeriodById query,
        CancellationToken cancellationToken = default)
    {
        return await cache.GetOrCreateAsync(
            $"period:{query.Id}",
            (db, query.Id),
            static async (state, ct) =>
            {
                var (db, id) = state;
                var period = await db.Periods
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == id, ct);

                if (period is null)
                    return null;

                return new PeriodDto(
                    period.Id,
                    period.Name,
                    period.StartDate,
                    period.EndDate,
                    period.Status.ToString(),
                    period.AllowSubjectOverrides,
                    period.NextPeriodId,
                    period.CreatedAt,
                    period.UpdatedAt);
            },
            CacheOptions,
            tags: ["students"],
            cancellationToken: cancellationToken);
    }
}