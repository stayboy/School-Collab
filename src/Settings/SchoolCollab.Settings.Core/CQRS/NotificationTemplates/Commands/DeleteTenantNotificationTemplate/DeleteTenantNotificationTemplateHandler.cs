using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Services;

namespace SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.DeleteTenantNotificationTemplate;

/// <summary>
/// Resets the current tenant's template for Kind × Channel to the built-in default
/// (spec §3.10 "Reset deletes the row"). The template row is soft-deleted and its
/// constraint rows are removed; a <see cref="TemplateAuditEntry"/> of kind
/// <see cref="TemplateChangeKind.Deleted"/> is written in the same transaction.
/// Idempotent: deleting an already-absent template is a no-op and writes no audit row.
/// </summary>
public sealed class DeleteTenantNotificationTemplateHandler(
    SettingsDbContext db,
    ITenantProvider tenantProvider,
    TemplateAuditRecorder auditor) : ICommandHandler<DeleteTenantNotificationTemplate>
{
    public async Task HandleAsync(DeleteTenantNotificationTemplate command, CancellationToken ct = default)
    {
        var tenantId = tenantProvider.GetTenantContext().TenantId;

        var existing = await db.TenantNotificationTemplates
            .Include(x => x.Constraints)
            .SingleOrDefaultAsync(x => x.Kind == command.Kind && x.Channel == command.Channel, ct);

        if (existing is null)
            return;

        auditor.Record(
            db, tenantId, existing.Id, existing.TemplateKey, TemplateChangeKind.Deleted,
            previousSubject: existing.Subject,
            newSubject: null,
            previousBody: existing.Body,
            newBody: null,
            previousConstraints: TemplateConstraintSummary.Serialize(existing.Constraints),
            newConstraints: null,
            reason: command.Reason);

        // The template is soft-deleted; the constraint rows have no soft-delete
        // column, so they are removed outright. The unique (tenant, kind, channel)
        // index is filtered on is_deleted = false, so the template can be recreated.
        db.TemplateConstraints.RemoveRange(existing.Constraints.ToList());
        existing.MarkAsDeleted();

        await db.SaveChangesAsync(ct);
    }
}
