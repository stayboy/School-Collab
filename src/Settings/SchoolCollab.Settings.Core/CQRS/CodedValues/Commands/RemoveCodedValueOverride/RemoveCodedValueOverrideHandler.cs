using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.Data;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.RemoveCodedValueOverride;

/// <summary>
/// Removes the current tenant's override for a global coded value (idempotent: a
/// no-op if no override exists). See documents/specs/grade-level-setup.md §5.1.
///
/// <para><b>Tenancy isolation.</b> Overrides are stored per-tenant (including a
/// dedicated row for the default sentinel tenant id <see cref="Guid.Empty"/>),
/// so this handler simply removes the row keyed by the current tenant id. Real
/// tenants can never see or remove the default tenant's override, and vice
/// versa, because the tenant id is part of the key.</para>
/// </summary>
public sealed class RemoveCodedValueOverrideHandler(
    SettingsDbContext db,
    ITenantProvider tenantProvider,
    ITenantContextAccessor tenantContextAccessor,
    HybridCache cache) : ICommandHandler<RemoveCodedValueOverride>
{
    public async Task HandleAsync(RemoveCodedValueOverride command, CancellationToken ct = default)
    {
        var tenantId = tenantProvider.GetTenantContext().TenantId;

        var existing = await db.TenantCodedValueOverrides
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.GlobalCodedValueId == command.GlobalCodedValueId, ct);

        if (existing is null)
            return; // idempotent — DELETE returns 204 regardless.

        db.TenantCodedValueOverrides.Remove(existing);

        // FR-8/FR-10: suppress the strict save-guard for the default/dev tenant's
        // Guid.Empty row on delete (sanctioned bypass). Real-tenant deletes satisfy
        // the guard and are not suppressed.
        if (tenantId == Guid.Empty)
        {
            using (tenantContextAccessor.SuppressTenantGuard())
            {
                await db.SaveChangesAsync(ct);
            }
        }
        else
        {
            await db.SaveChangesAsync(ct);
        }

        // Invalidate the coded-values cache so the dropdown lists (by-parent,
        // by-code, search, etc.) refresh promptly after an override is removed.
        await cache.RemoveByTagAsync("coded-values", ct);
        await cache.RemoveByTagAsync($"tenant:{tenantId}", ct);
    }
}
