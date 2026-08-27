using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.CQRS.Periods.Queries.GetSubPeriodCount;

public sealed class GetSubPeriodCountHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<GetSubPeriodCount, SubPeriodCountDto>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<SubPeriodCountDto> HandleAsync(
        GetSubPeriodCount query,
        CancellationToken cancellationToken = default)
    {
        var tenantId = db.CurrentTenantId;

        return await cache.GetOrCreateAsync(
            $"periods:sub-period-count:{tenantId}",
            (db, tenantId),
            static async (state, ct) =>
            {
                var (db, tenantId) = state;
                var count = await db.Periods
                    .IgnoreQueryFilters(["Tenant"])
                    .CountAsync(p => p.TenantId == tenantId
                        && p.PeriodType != PeriodType.AcademicYear
                        && (p.Status == PeriodStatus.Draft || p.Status == PeriodStatus.Active),
                        ct);
                return new SubPeriodCountDto(count);
            },
            CacheOptions,
            tags: ["students"],
            cancellationToken);
    }
}