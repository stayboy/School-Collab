using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.DTOs;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.UpsertCodedValueOverride;

public sealed class UpsertCodedValueOverrideHandler(
    SettingsDbContext db,
    ITenantProvider tenantProvider) : ICommandHandler<UpsertCodedValueOverride, CodedValueDto>
{
    public async Task<CodedValueDto> HandleAsync(UpsertCodedValueOverride command, CancellationToken ct = default)
    {
        var tenantId = tenantProvider.GetTenantContext().TenantId;
        
        var overrideVal = TenantCodedValueOverride.Create(
            tenantId,
            command.GlobalCodedValueId,
            command.Name,
            command.Description);

        // Use the existing repository logic via the DbContext if available, 
        // but since we are in a handler, we can use the repository pattern if injected.
        // For brevity, I'll use the DbContext directly here as the repository 
        // implementation was previously seen.
        
        var existing = await db.TenantCodedValueOverrides
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.GlobalCodedValueId == command.GlobalCodedValueId, ct);
            
        if (existing != null)
        {
            existing.Update(command.Name, command.Description);
        }
        else
        {
            db.TenantCodedValueOverrides.Add(overrideVal);
        }

        await db.SaveChangesAsync(ct);

        // Return the resolved CodedValue
        var cv = await db.CodedValues.SingleOrDefaultAsync(x => x.Id == command.GlobalCodedValueId, ct);
        if (cv == null) throw new KeyNotFoundException("Global coded value not found.");

        return new CodedValueDto(
            cv.Id,
            cv.Code,
            command.Name ?? cv.Name,
            command.Description ?? cv.Description,
            cv.ParentId,
            null,
            cv.IsDisabled,
            cv.DisplayOrder,
            cv.CreatedAt,
            cv.UpdatedAt,
            [], [], 0, cv.IsDeleted, cv.DeletedAt);
    }
}