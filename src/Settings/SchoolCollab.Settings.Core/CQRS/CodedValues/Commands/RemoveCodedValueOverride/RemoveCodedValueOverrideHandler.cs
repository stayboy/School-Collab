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
///
/// <para><b>Default-tenant branch.</b> When the current tenant is the sentinel
/// "default" tenant there is no per-tenant override to remove — the wizard's
/// "Reset to default" action is meaningless in that context (the "override" was
/// a direct update of the global blueprint, not a separate override row). We
/// therefore return without touching the database.</para>
/// </summary>
public sealed class RemoveCodedValueOverrideHandler(
    SettingsDbContext db,
    ITenantProvider tenantProvider,
    HybridCache cache) : ICommandHandler<RemoveCodedValueOverride>
{
    public async Task HandleAsync(RemoveCodedValueOverride command, CancellationToken ct = default)
    {
        var tenantContext = tenantProvider.GetTenantContext();

        // No real tenant → no override row to remove. The "override" was a
        // direct update of the global coded value, which the caller can revert
        // manually if needed. Idempotent: matches the DELETE 204 contract even
        // when there is nothing to do.
        if (tenantContext.IsDefault)
            return;

        var tenantId = tenantContext.TenantId;
        var existing = await db.TenantCodedValueOverrides
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.GlobalCodedValueId == command.GlobalCodedValueId, ct);

        if (existing is null)
            return; // idempotent — DELETE returns 204 regardless.

        db.TenantCodedValueOverrides.Remove(existing);
        await db.SaveChangesAsync(ct);

        // Remove the specific GetCodedValueById cache entry by key. This is
        // the authoritative invalidation — RemoveByTagAsync alone has been
        // observed not to clear the L1 in-memory layer in some HybridCache
        // versions, which then serves the stale (pre-delete) override on the
        // next read. Removing the exact key is unambiguous.
        await cache.RemoveAsync($"coded-value:{command.GlobalCodedValueId}", ct);

        // Belt-and-braces: also evict by tag for any other entries that share
        // the tag (e.g. future list queries tagged "coded-values").
        await cache.RemoveByTagAsync("coded-values", ct);
        await cache.RemoveByTagAsync($"tenant:{tenantId}", ct);
    }
}
