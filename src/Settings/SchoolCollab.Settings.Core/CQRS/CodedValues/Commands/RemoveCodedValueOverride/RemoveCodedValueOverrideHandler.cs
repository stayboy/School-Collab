using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.Data;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.RemoveCodedValueOverride;

/// <summary>
/// Removes the current tenant's override for a global coded value (idempotent: a
/// no-op if no override exists). Invalidates the <c>coded-values</c> and
/// <c>tenant:{tenantId}</c> cache tags when an override is actually removed so
/// subsequent reads fall back to the global blueprint name. See
/// documents/specs/grade-level-setup.md §5.1.
/// </summary>
public sealed class RemoveCodedValueOverrideHandler(
    SettingsDbContext db,
    ITenantProvider tenantProvider,
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
        await db.SaveChangesAsync(ct);

        await cache.RemoveByTagAsync("coded-values", ct);
        await cache.RemoveByTagAsync($"tenant:{tenantId}", ct);
    }
}