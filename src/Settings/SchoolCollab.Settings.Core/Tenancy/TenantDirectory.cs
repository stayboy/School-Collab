using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.Data;

namespace SchoolCollab.Settings.Core.Tenancy;

/// <summary>
/// Default <see cref="ITenantDirectory"/> implementation. Reads the tenant registry
/// from <see cref="SettingsDbContext.Tenants"/> — the only context that owns the
/// <c>Tenants</c> DbSet — so cross-context workers (e.g. the Students
/// <c>PromotionService</c>) can enumerate tenants without a Settings dependency
/// at the DbContext level. <see cref="Tenant"/> is a global entity (no tenant
/// filter), so no guard suppression is required for the read.
/// </summary>
/// <remarks>
/// Uses <see cref="IDbContextFactory{TContext}"/> (singleton-safe) rather than a
/// scoped <c>SettingsDbContext</c> so this directory can be registered as a
/// singleton without a captive-dependency anti-pattern. A short-lived context is
/// created and disposed per call. See <c>global-tenant-filter.md</c> §8.4 / FR-16.
/// </remarks>
public sealed class TenantDirectory(IDbContextFactory<SettingsDbContext> dbFactory) : ITenantDirectory
{
    public async Task<IReadOnlyList<Guid>> GetAllTenantIdsAsync(CancellationToken ct = default)
    {
        // Tenants is global (allow-list) — no "Tenant" filter to bypass. Read all.
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Tenants
            .AsNoTracking()
            .Select(t => t.Id)
            .ToListAsync(ct);
    }
}
