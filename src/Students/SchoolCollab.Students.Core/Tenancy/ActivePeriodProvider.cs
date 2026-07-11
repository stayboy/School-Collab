using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Tenancy;

/// <summary>
/// Students.Core implementation of <see cref="IActivePeriodProvider"/>. Reads the
/// current tenant via the repository's tenant-filtered query and resolves the
/// active/current period. Registered scoped (see Extensions.AddStudentsCore) so it
/// is per-request and respects <c>RunWithExplicitTenantAsync</c> for workers.
/// </summary>
/// <remarks>
/// The active period is cached per tenant (HybridCache tag "students", already
/// invalidated by the Activate/Complete handlers) so the frequent ambient lookups
/// from other modules do not re-query the database on every resolution. The tenant
/// id comes from <see cref="ITenantProvider"/> and forms the cache key, so the
/// cached value is always scoped to the current tenant — including inside workers
/// running under <c>RunWithExplicitTenantAsync</c>.
/// </remarks>
public sealed class ActivePeriodProvider(
    IPeriodRepository periodRepository,
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
    {
        var tenantId = tenantProvider.GetTenantContext().TenantId;
        return await cache.GetOrCreateAsync(
            $"active-period:{tenantId}",
            periodRepository,
            static (repo, token) => MapAsync(repo, token),
            CacheOptions,
            tags: [CacheTag],
            cancellationToken: ct);
    }

    public async Task<ActivePeriod?> GetCurrentPeriodAsync(CancellationToken ct = default)
    {
        var tenantId = tenantProvider.GetTenantContext().TenantId;
        return await cache.GetOrCreateAsync(
            $"current-period:{tenantId}",
            periodRepository,
            static (repo, token) => MapCurrentAsync(repo, token),
            CacheOptions,
            tags: [CacheTag],
            cancellationToken: ct);
    }

    private static ValueTask<ActivePeriod?> MapAsync(IPeriodRepository repo, CancellationToken token) =>
        MapCore(repo.GetActivePeriodAsync(cancellationToken: token));

    private static ValueTask<ActivePeriod?> MapCurrentAsync(IPeriodRepository repo, CancellationToken token) =>
        MapCore(repo.GetCurrentPeriodAsync(token));

    private static async ValueTask<ActivePeriod?> MapCore(Task<Period?> periodTask)
    {
        var period = await periodTask;
        return period is null ? null : ToActivePeriod(period);
    }

    private static ActivePeriod ToActivePeriod(Period p) =>
        new(p.Id, p.Name, p.StartDate, p.EndDate, p.Status.ToString());
}
