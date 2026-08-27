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
                var period = await db.Periods
                    .IgnoreQueryFilters(["Tenant"])
                    .Where(p => p.TenantId == tenantId
                        && p.Status == PeriodStatus.Active
                        && p.PeriodType != PeriodType.AcademicYear)
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
                var period = await db.Periods
                    .IgnoreQueryFilters(["Tenant"])
                    .Where(p => p.TenantId == tenantId && p.StartDate <= today && p.EndDate >= today)
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
