using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTenantNotificationTemplate;

/// <summary>
/// Loads the current tenant's <b>default</b> template for
/// <see cref="GetTenantNotificationTemplate.Kind"/> × <see cref="GetTenantNotificationTemplate.Channel"/>
/// via the tenant query filter, or <see langword="null"/> when none is configured.
/// <para>
/// The default is the live row with <b>no constraint rows</b> (spec Q15), so the read filters on the
/// child set instead of assuming there is exactly one row for the slot — the pre-B2c
/// <c>SingleOrDefaultAsync</c> would throw the moment a constrained candidate existed beside the
/// default.
/// </para>
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
            .Where(x => x.Kind == query.Kind && x.Channel == query.Channel && !x.Constraints.Any())
            .FirstOrDefaultAsync(ct);

        if (template is null)
            return null;

        return new TenantNotificationTemplateDto(
            template.Id, template.Kind, template.Channel, template.Subject, template.Body,
            template.Constraints.Select(c => new TemplateConstraintDto(c.Id, c.Dimension, c.Value)).ToList(),
            template.CreatedAt, template.UpdatedAt);
    }
}
