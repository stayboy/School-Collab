using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.DTOs;
using SchoolCollab.Settings.Core.Services;

namespace SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTenantNotificationTemplateById;

/// <summary>
/// Reads one template (with its constraint rows and owner) by id. A row outside the caller's scope is
/// invisible to the hybrid query filter, so "another tenant's row" and "no such row" are the same 404 -
/// the detail route never confirms the existence of a foreign template. A system-blueprint row IS
/// visible to a tenant session (it can be adopted), which is why that session can open it read-only.
/// </summary>
public sealed class GetTenantNotificationTemplateByIdHandler(SettingsDbContext db)
    : IQueryHandler<GetTenantNotificationTemplateById, TenantNotificationTemplateDto?>
{
    public async Task<TenantNotificationTemplateDto?> HandleAsync(
        GetTenantNotificationTemplateById query, CancellationToken ct = default)
    {
        var template = await db.TenantNotificationTemplates
            .Include(x => x.Constraints)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == query.Id, ct);

        if (template is null)
            return null;

        var ownerName = await NotificationTemplateOwnership.ResolveOwnerNameAsync(db, template.TenantId, ct);

        return NotificationTemplateOwnership.ToDto(template, ownerName);
    }
}