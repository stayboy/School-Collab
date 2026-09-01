using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Periods.Queries.ListSubPeriods;

public sealed class ListSubPeriodsHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListSubPeriods, PeriodDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<PeriodDto[]> HandleAsync(
        ListSubPeriods query,
        CancellationToken cancellationToken = default)
    {
        var tenantId = db.CurrentTenantId;

        return await cache.GetOrCreateAsync(
            $"periods:sub-periods:{tenantId}:{query.AcademicYearId}",
            (db, tenantId, query.AcademicYearId),
            static async (state, ct) =>
            {
                var (db, tenantId, academicYearId) = state;
                var results = await db.Periods
                    .IgnoreQueryFilters(["Tenant"])
                    .Where(p => p.TenantId == tenantId
                        && p.ParentPeriodId == academicYearId)
                    .AsNoTracking()
                    .OrderBy(x => x.StartDate)
                    .ToArrayAsync(ct);

                return results.Select(p => new PeriodDto(
                    p.Id, p.Name, p.StartDate, p.EndDate,
                    p.Status.ToString(),
                    p.ParentPeriodId, p.NextPeriodId,
                    p.Division.ToString(), p.ActivationToleranceDays,
                    p.CreatedAt, p.UpdatedAt)).ToArray();
            },
            CacheOptions,
            tags: ["students"],
            cancellationToken);
    }
}