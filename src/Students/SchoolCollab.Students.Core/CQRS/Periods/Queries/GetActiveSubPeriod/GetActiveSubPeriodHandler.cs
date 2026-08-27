using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Periods.Queries.GetActiveSubPeriod;

public sealed class GetActiveSubPeriodHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<GetActiveSubPeriod, PeriodDto?>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<PeriodDto?> HandleAsync(
        GetActiveSubPeriod query,
        CancellationToken cancellationToken = default)
    {
        var tenantId = db.CurrentTenantId;

        return await cache.GetOrCreateAsync(
            $"periods:active-sub-period:{tenantId}",
            (db, tenantId),
            static async (state, ct) =>
            {
                var (db, tenantId) = state;
                var period = await db.Periods
                    .IgnoreQueryFilters(["Tenant"])
                    .Where(p => p.TenantId == tenantId
                        && p.Status == PeriodStatus.Active
                        && p.PeriodType != PeriodType.AcademicYear)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(ct);

                return period is null ? null : new PeriodDto(
                    period.Id, period.Name, period.StartDate, period.EndDate,
                    period.Status.ToString(), period.PeriodType.ToString(),
                    period.ParentPeriodId, period.NextPeriodId,
                    period.CreatedAt, period.UpdatedAt);
            },
            CacheOptions,
            tags: ["students"],
            cancellationToken);
    }
}