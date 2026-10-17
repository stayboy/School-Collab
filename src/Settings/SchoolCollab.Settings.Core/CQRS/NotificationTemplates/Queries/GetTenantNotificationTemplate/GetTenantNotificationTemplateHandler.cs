using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTenantNotificationTemplate;

/// <summary>
/// Loads the current tenant's template for <see cref="GetTenantNotificationTemplate.Kind"/>
/// × <see cref="GetTenantNotificationTemplate.Channel"/> via the tenant query filter,
/// or <see langword="null"/> when none is configured.
/// </summary>
public sealed class GetTenantNotificationTemplateHandler(SettingsDbContext db)
    : IQueryHandler<GetTenantNotificationTemplate, TenantNotificationTemplateDto?>
{
    public async Task<TenantNotificationTemplateDto?> HandleAsync(
        GetTenantNotificationTemplate query, CancellationToken ct = default)
    {
        var template = await db.TenantNotificationTemplates
            .Include(x => x.Constraints)
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Kind == query.Kind && x.Channel == query.Channel, ct);

        if (template is null)
            return null;

        return new TenantNotificationTemplateDto(
            template.Id, template.Kind, template.Channel, template.Subject, template.Body,
            template.Constraints.Select(c => new TemplateConstraintDto(c.Id, c.Dimension, c.Value)).ToList(),
            template.UpdatedAt);
    }
}