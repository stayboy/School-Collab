using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.DTOs;
using SchoolCollab.Settings.Core.Services;

namespace SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTenantNotificationTemplates;

/// <summary>
/// Loads every template visible to the caller's scope - its own rows plus the system blueprint - through
/// the hybrid tenant query filter, with constraints and owner eagerly resolved. This is also the
/// cross-context candidate read (round notification-templates-b2c): the Assignments side filters it to
/// one (kind, channel) and hands the rows to its applicability matcher, so there is no second Settings
/// route for candidates.
/// <para>
/// <see cref="GetTenantNotificationTemplates.IncludeTenantOwned"/> additionally reads other tenants'
/// rows, and only in system scope (redesign spec 3.6). That is the one sanctioned cross-tenant read in
/// this feature; a tenant session passing the flag still sees its own scope only.
/// </para>
/// </summary>
public sealed class GetTenantNotificationTemplatesHandler(SettingsDbContext db, ITenantProvider tenantProvider)
    : IQueryHandler<GetTenantNotificationTemplates, IReadOnlyList<TenantNotificationTemplateDto>>
{
    public async Task<IReadOnlyList<TenantNotificationTemplateDto>> HandleAsync(
        GetTenantNotificationTemplates query, CancellationToken ct = default)
    {
        var isSystemScope = NotificationTemplateOwnership.IsSystemScope(
            tenantProvider.GetTenantContext().TenantId);

        var source = db.TenantNotificationTemplates
            .Include(x => x.Constraints)
            .AsNoTracking();

        if (query.IncludeTenantOwned && isSystemScope)
        {
            // System scope promoting a tenant row (spec 3.8). The soft-delete filter is not part of the
            // "Tenant" filter, so it still applies when the tenant filter is ignored.
            source = source.IgnoreQueryFilters(["Tenant"]);
        }

        var templates = await source.ToListAsync(ct);
        var tenantNames = await NotificationTemplateOwnership.ResolveOwnerNamesAsync(db, ct);

        return templates
            .Select(t => NotificationTemplateOwnership.ToDto(
                t, NotificationTemplateOwnership.OwnerName(t.TenantId, tenantNames)))
            .ToList();
    }
}