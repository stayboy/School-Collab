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
        // Capture the tenant in the request scope, where the tenant context is
        // available. Inside the HybridCache factory the execution context has
        // usually been lost (no HttpContext / AsyncLocal), so db.CurrentTenantId
        // would resolve to Guid.Empty and the global "Tenant" query filter would
        // hide every row for the calling tenant - yielding an empty list while the
        // table was in fact populated (the originally-reported period bug). Capture
        // the tenant once here and scope the query explicitly so the cached result
        // is tenant-correct.
        var tenantId = db.CurrentTenantId;

        return await cache.GetOrCreateAsync(
            $"periods:list:{tenantId}",
            (db, tenantId),
            static async (state, ct) =>
            {
                var (db, tenantId) = state;
                var results = await db.Periods
                    // Drop only the "Tenant" filter (which relies on the lost
                    // CurrentTenantId) and keep the "SoftDelete" filter.
                    .IgnoreQueryFilters(["Tenant"])
                    .Where(p => p.TenantId == tenantId)
                    .AsNoTracking()
                    .OrderByDescending(x => x.StartDate)
                    .ToArrayAsync(ct);

                return results.Select(p => new PeriodDto(
                    p.Id,
                    p.Name,
                    p.StartDate,
                    p.EndDate,
                    p.Status.ToString(),
                    p.NextPeriodId,
                    p.CreatedAt,
                    p.UpdatedAt)).ToArray();
            },
            CacheOptions,
            tags: ["students"],
            cancellationToken);
    }
}
