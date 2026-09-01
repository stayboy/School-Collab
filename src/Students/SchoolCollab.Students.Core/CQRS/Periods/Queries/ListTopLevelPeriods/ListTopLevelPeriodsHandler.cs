using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Periods.Queries.ListTopLevelPeriods;

public sealed class ListTopLevelPeriodsHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListTopLevelPeriods, PeriodLandingDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<PeriodLandingDto[]> HandleAsync(
        ListTopLevelPeriods query,
        CancellationToken cancellationToken = default)
    {
        // Same tenant-capture discipline as ListPeriodsHandler: inside the
        // HybridCache factory the execution context (HttpContext / AsyncLocal)
        // is lost, so db.CurrentTenantId would resolve to Guid.Empty and the
        // "Tenant" query filter would hide every row. Capture the tenant once
        // here and scope the query explicitly so the cached result is
        // tenant-correct.
        var tenantId = db.CurrentTenantId;

        return await cache.GetOrCreateAsync(
            $"periods:top-level:{tenantId}",
            (db, tenantId),
            static async (state, ct) =>
            {
                var (db, tenantId) = state;

                // One pass over the tenant's periods: project the fields needed
                // for the landing row, then derive top-level rows and their
                // sub-period counts in memory (sub-period volume is small).
                var rows = await db.Periods
                    // Drop only the "Tenant" filter (which relies on the lost
                    // CurrentTenantId) and keep the "SoftDelete" filter.
                    .IgnoreQueryFilters(["Tenant"])
                    .Where(p => p.TenantId == tenantId)
                    .AsNoTracking()
                    .Select(p => new
                    {
                        p.Id,
                        p.Name,
                        p.StartDate,
                        p.EndDate,
                        p.Status,
                        p.Division,
                        p.ParentPeriodId,
                        p.CreatedAt,
                        p.UpdatedAt
                    })
                    .ToArrayAsync(ct);

                return rows
                    .Where(p => p.ParentPeriodId is null)
                    .OrderByDescending(p => p.StartDate)
                    .Select(p => new PeriodLandingDto(
                        p.Id,
                        p.Name,
                        p.StartDate,
                        p.EndDate,
                        p.Status.ToString(),
                        p.Division.ToString(),
                        rows.Count(c => c.ParentPeriodId == p.Id),
                        rows.Count(c => c.ParentPeriodId == p.Id && c.Status == PeriodStatus.Draft),
                        p.CreatedAt,
                        p.UpdatedAt))
                    .ToArray();
            },
            CacheOptions,
            tags: ["students"],
            cancellationToken);
    }
}