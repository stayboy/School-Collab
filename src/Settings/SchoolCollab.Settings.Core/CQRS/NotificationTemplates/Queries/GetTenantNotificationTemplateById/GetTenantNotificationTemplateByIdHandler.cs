using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTenantNotificationTemplateById;

/// <summary>
/// Reads one template (with its constraint rows) by id. A row outside the caller's tenant is invisible to
/// the tenant query filter, so "another tenant's row" and "no such row" are the same 404 — the detail
/// route never confirms the existence of a foreign template.
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

        return new TenantNotificationTemplateDto(
            template.Id, template.Kind, template.Channel, template.Subject, template.Body,
            template.Constraints.Select(c => new TemplateConstraintDto(c.Id, c.Dimension, c.Value)).ToList(),
            template.CreatedAt, template.UpdatedAt);
    }
}