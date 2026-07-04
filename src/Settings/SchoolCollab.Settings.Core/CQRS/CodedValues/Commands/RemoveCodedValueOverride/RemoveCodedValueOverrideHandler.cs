using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.RemoveCodedValueOverride;

public sealed class RemoveCodedValueOverrideHandler(
    SettingsDbContext db,
    ITenantProvider tenantProvider) : ICommandHandler<RemoveCodedValueOverride>
{
    public async Task HandleAsync(RemoveCodedValueOverride command, CancellationToken ct = default)
    {
        var tenantId = tenantProvider.GetTenantContext().TenantId;
        var existing = await db.TenantCodedValueOverrides
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.GlobalCodedValueId == command.GlobalCodedValueId, ct);
            
        if (existing != null)
        {
            db.TenantCodedValueOverrides.Remove(existing);
            await db.SaveChangesAsync(ct);
        }
    }
}