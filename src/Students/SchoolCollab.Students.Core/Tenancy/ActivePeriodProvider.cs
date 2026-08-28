using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Tenancy;

/// <summary>
/// Students.Core implementation of <see cref="IActivePeriodProvider"/>. Resolves the
/// active/current period for the current tenant and caches it per tenant so the
/// frequent ambient lookups from other modules do not re-query the database on
/// every resolution. Registered scoped (see Extensions.AddStudentsCore) so it is
/// per-request and respects <c>RunWithExplicitTenantAsync</c> for workers.
/// </summary>
/// <remarks>
/// The cache key embeds the tenant id, so the cached value is always scoped to the
/// current tenant - including inside workers running under
/// <c>RunWithExplicitTenantAsync</c>. The tenant context is lost inside the
/// HybridCache factory (no HttpContext / AsyncLocal), so the period query is scoped
/// explicitly by <c>TenantId</c> here rather than relying on the global "Tenant"
/// filter, which would otherwise resolve to Guid.Empty and return null.
/// </remarks>
public sealed class ActivePeriodProvider(
    StudentsDbContext db,
    ITenantProvider tenantProvider,
    HybridCache cache) : IActivePeriodProvider
{
    private const string CacheTag = "students";

    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<ActivePeriod?> GetActivePeriodAsync(CancellationToken ct = default)
        => await GetActiveAcademicYearAsync(ct);

    public async Task<ActivePeriod?> GetActiveAcademicYearAsync(CancellationToken ct = default)
    {
        var tenantId = tenantProvider.GetTenantContext().TenantId;
        return await cache.GetOrCreateAsync(
            $"active-academic-year:{tenantId}",
            (db, tenantId),
            static async (state, token) =>
            {
                var (db, tenantId) = state;
                var period = await db.Periods
                    .IgnoreQueryFilters(["Tenant"])
                    .Where(p => p.TenantId == tenantId
                        && p.Status == PeriodStatus.Active
                        && p.PeriodType == PeriodType.AcademicYear)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(token);
                return period is null ? null : ToActivePeriod(period);
            },
            CacheOptions,
            tags: [CacheTag],
            cancellationToken: ct);
    }

    public async Task<ActivePeriod?> GetActiveSubPeriodAsync(CancellationToken ct = default)
    {
        var tenantId = tenantProvider.GetTenantContext().TenantId;
        return await cache.GetOrCreateAsync(
            $"active-sub-period:{tenantId}",
            (db, tenantId),
            static async (state, token) =>
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
                    .FirstOrDefaultAsync(token);
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
                    .FirstOrDefaultAsync(token);
                return period is null ? null : ToActivePeriod(period);
            },
            CacheOptions,
            tags: [CacheTag],
            cancellationToken: ct);
    }

    public async Task<ActivePeriod?> GetCurrentPeriodAsync(CancellationToken ct = default)
    {
        var tenantId = tenantProvider.GetTenantContext().TenantId;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return await cache.GetOrCreateAsync(
            $"current-period:{tenantId}",
            (db, tenantId, today),
            static async (state, token) =>
            {
                var (db, tenantId, today) = state;
                // "Current" = the active period containing today. Prefer the more
                // specific sub-period (Term/Semester) over the AcademicYear when both
                // contain today, then Term over Semester, then earliest start — so the
                // display is deterministic under the two-active-rows hierarchy.
                var period = await db.Periods
                    .IgnoreQueryFilters(["Tenant"])
                    .Where(p => p.TenantId == tenantId
                        && p.Status == PeriodStatus.Active
                        && p.StartDate <= today && p.EndDate >= today)
                    .OrderBy(p => p.PeriodType == PeriodType.AcademicYear ? 1 : 0)
                    .ThenBy(p => p.PeriodType)
                    .ThenBy(p => p.StartDate)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(token);
                return period is null ? null : ToActivePeriod(period);
            },
            CacheOptions,
            tags: [CacheTag],
            cancellationToken: ct);
    }

    private static ActivePeriod ToActivePeriod(Period p) =>
        new(p.Id, p.Name, p.StartDate, p.EndDate, p.Status.ToString(), p.PeriodType.ToString(), p.ParentPeriodId);
}
