using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.Data;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.RemoveCodedValueOverride;

/// <summary>
/// Removes the current tenant's override for a global coded value (idempotent: a
/// no-op if no override exists). See documents/specs/grade-level-setup.md §5.1.
///
/// <para><b>Default-tenant branch.</b> When the current tenant is the sentinel
/// "default" tenant there is no per-tenant override to remove — the wizard's
/// "Reset to default" action is meaningless in that context (the "override" was
/// a direct update of the global blueprint, not a separate override row). We
/// therefore return without touching the database.</para>
/// </summary>
public sealed class RemoveCodedValueOverrideHandler(
    SettingsDbContext db,
    ITenantProvider tenantProvider) : ICommandHandler<RemoveCodedValueOverride>
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
    }
}
