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
                // Resolve the active academic year first, then scope the sub-period
                // lookup to it. A tenant can have one active year AND one active
                // sub-period (Term/Semester) at the same time, so the sub-period
                // must be parent-scoped to the year to be unambiguous.
                var activeYearId = await db.Periods
                    .IgnoreQueryFilters(["Tenant"])
                    .Where(p => p.TenantId == tenantId
                        && p.Status == PeriodStatus.Active
                        && p.PeriodType == PeriodType.AcademicYear)
                    .Select(p => (Guid?)p.Id)
                    .FirstOrDefaultAsync(ct);
                if (activeYearId is null) return null;

                // Deterministic: prefer Term over Semester (PeriodType order), then
                // earliest start date, so the result is stable when both a Term and
                // a Semester are active under the same year.
                var period = await db.Periods
                    .IgnoreQueryFilters(["Tenant"])
                    .Where(p => p.TenantId == tenantId
                        && p.ParentPeriodId == activeYearId
                        && p.Status == PeriodStatus.Active
                        && p.PeriodType != PeriodType.AcademicYear)
                    .OrderBy(p => p.PeriodType)
                    .ThenBy(p => p.StartDate)
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