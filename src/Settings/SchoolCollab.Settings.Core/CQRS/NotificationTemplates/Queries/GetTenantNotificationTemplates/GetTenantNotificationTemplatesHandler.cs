using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTenantNotificationTemplates;

/// <summary>
/// Loads all notification templates for the current tenant via the tenant query
/// filter. Constraints are eagerly loaded.
/// </summary>
public sealed class GetTenantNotificationTemplatesHandler(SettingsDbContext db)
    : IQueryHandler<GetTenantNotificationTemplates, IReadOnlyList<TenantNotificationTemplateDto>>
{
    public async Task<IReadOnlyList<TenantNotificationTemplateDto>> HandleAsync(
        GetTenantNotificationTemplates query, CancellationToken ct = default)
    {
        var templates = await db.TenantNotificationTemplates
            .Include(x => x.Constraints)
            .AsNoTracking()
            .ToListAsync(ct);

        return templates
            .Select(t => new TenantNotificationTemplateDto(
                t.Id, t.Kind, t.Channel, t.Subject, t.Body,
                t.Constraints.Select(c => new TemplateConstraintDto(c.Id, c.Dimension, c.Value)).ToList(),
                t.UpdatedAt))
            .ToList();
    }
}