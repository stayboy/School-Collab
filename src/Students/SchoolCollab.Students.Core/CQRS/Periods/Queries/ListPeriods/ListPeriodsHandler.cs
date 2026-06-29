using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Periods.Queries.ListPeriods;

public sealed class ListPeriodsHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListPeriods, PeriodDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<PeriodDto[]> HandleAsync(
        ListPeriods query,
        CancellationToken cancellationToken = default)
    {
        return await cache.GetOrCreateAsync(
            "periods:list",
            db,
            static async (db, ct) =>
            {
                var results = await db.Periods
                    .AsNoTracking()
                    .OrderByDescending(x => x.StartDate)
                    .ToArrayAsync(ct);

                return results.Select(p => new PeriodDto(
                    p.Id,
                    p.Name,
                    p.StartDate,
                    p.EndDate,
                    p.Status.ToString(),
                    p.AllowSubjectOverrides,
                    p.NextPeriodId,
                    p.CreatedAt,
                    p.UpdatedAt)).ToArray();
            },
            CacheOptions,
            tags: ["students"],
            cancellationToken: cancellationToken);
    }
}