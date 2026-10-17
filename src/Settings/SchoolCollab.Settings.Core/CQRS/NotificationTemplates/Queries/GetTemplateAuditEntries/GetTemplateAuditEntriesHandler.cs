using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTemplateAuditEntries;

/// <summary>
/// Loads the audit entries for the current tenant's template, most recent first.
/// </summary>
/// <remarks>
/// Matched on the stored <c>TemplateKey</c> ("{kind}:{channel}") rather than a
/// subquery over <c>TenantNotificationTemplates</c>: that table is soft-deleted, so
/// an id-based join would make a template's whole audit trail disappear the moment
/// an admin resets it to the built-in default (spec §3.9 — the trail must survive
/// the reset). The audit table is not tenant-filtered (global allow-list), so the
/// tenant scope is applied explicitly.
/// </remarks>
public sealed class GetTemplateAuditEntriesHandler(SettingsDbContext db, ITenantProvider tenantProvider)
    : IQueryHandler<GetTemplateAuditEntries, IReadOnlyList<TemplateAuditEntryDto>>
{
    public async Task<IReadOnlyList<TemplateAuditEntryDto>> HandleAsync(
        GetTemplateAuditEntries query, CancellationToken ct = default)
    {
        var tenantId = tenantProvider.GetTenantContext().TenantId;
        var templateKey = $"{query.Kind}:{query.Channel}";

        var entries = await db.TemplateAuditEntries
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.TemplateKey == templateKey)
            .OrderByDescending(x => x.OccurredAt)
            .ToListAsync(ct);

        return entries.Select(e => new TemplateAuditEntryDto(
            e.Id, e.ChangeKind, e.PreviousSubject, e.NewSubject, e.PreviousBody, e.NewBody,
            e.PreviousConstraints, e.NewConstraints,
            e.Reason, e.ActorId, e.ActorDisplayName, e.OccurredAt)).ToList();
    }
}