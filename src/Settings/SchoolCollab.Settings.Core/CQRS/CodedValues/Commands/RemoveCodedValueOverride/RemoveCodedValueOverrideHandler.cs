using Microsoft.EntityFrameworkCore;
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
    ITenantProvider tenantProvider) : ICommandHandler<RemoveCodedValueOverride>
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
    }
}
