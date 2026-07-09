namespace SchoolCollab.Core.Tenancy;

/// <summary>
/// Cross-context directory of tenant ids, for background workers that need to
/// enumerate tenants but cannot reach the <c>Tenants</c> DbSet (which lives on
/// <c>SettingsDbContext</c>, unavailable to e.g. the Students worker).
/// </summary>
/// <remarks>
/// The interface lives in Core so every context can depend on it. The
/// implementation (<c>TenantDirectory</c>) lives in Settings, where
/// <c>SettingsDbContext.Tenants</c> is available, and is registered in DI for
/// workers. See <c>global-tenant-filter.md</c> §8.4 / FR-16.
/// </remarks>
public interface ITenantDirectory
{
    /// <summary>
    /// Returns the ids of all tenants in the registry, for per-tenant worker
    /// loops. Runs under a suppressed tenant guard (<c>Tenant</c> is global).
    /// </summary>
    Task<IReadOnlyList<Guid>> GetAllTenantIdsAsync(CancellationToken ct = default);
}
