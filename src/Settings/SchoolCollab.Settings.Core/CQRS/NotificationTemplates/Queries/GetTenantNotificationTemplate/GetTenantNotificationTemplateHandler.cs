using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.DTOs;
using SchoolCollab.Settings.Core.Services;

namespace SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTenantNotificationTemplate;

/// <summary>
/// Loads the caller's <b>default</b> template for
/// <see cref="GetTenantNotificationTemplate.Kind"/> x <see cref="GetTenantNotificationTemplate.Channel"/>,
/// or <see langword="null"/> when none is configured.
/// <para>
/// The default is the live row with <b>no constraint rows</b> (spec Q15), so the read filters on the
/// child set instead of assuming there is exactly one row for the slot - the pre-B2c
/// <c>SingleOrDefaultAsync</c> would throw the moment a constrained candidate existed beside the
/// default. A tenant row wins over the system blueprint for the same slot (spec 3.9 scope rank), so the
/// ordering puts tenant-owned rows first.
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
            .OrderBy(x => x.TenantId == null)
            .ThenBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (template is null)
            return null;

        var ownerName = await NotificationTemplateOwnership.ResolveOwnerNameAsync(db, template.TenantId, ct);

        return NotificationTemplateOwnership.ToDto(template, ownerName);
    }
}