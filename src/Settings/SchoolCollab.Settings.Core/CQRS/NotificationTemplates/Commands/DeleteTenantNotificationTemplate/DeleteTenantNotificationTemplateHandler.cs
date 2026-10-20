using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Exceptions;
using SchoolCollab.Settings.Core.Services;

namespace SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.DeleteTenantNotificationTemplate;

/// <summary>
/// Tombstones one template by id (spec §3.10 "Reset deletes the row"): the row is soft-deleted and
/// its constraint rows are removed, with a <see cref="TemplateAuditEntry"/> of kind
/// <see cref="TemplateChangeKind.Deleted"/> written in the same transaction. An absent row — or
/// another tenant's, which the tenant query filter hides — is a
/// <see cref="TenantNotificationTemplateNotFoundException"/> (404), never a silent no-op: unlike the
/// retired (kind, channel)-keyed reset there is no "the slot is already unset" state to be idempotent
/// about.
/// </summary>
public sealed class DeleteTenantNotificationTemplateHandler(
    SettingsDbContext db,
    ITenantProvider tenantProvider,
    TemplateAuditRecorder auditor) : ICommandHandler<DeleteTenantNotificationTemplate>
{
    public async Task HandleAsync(DeleteTenantNotificationTemplate command, CancellationToken ct = default)
    {
        var tenantId = tenantProvider.GetTenantContext().TenantId;

        var template = await db.TenantNotificationTemplates
            .Include(x => x.Constraints)
            .SingleOrDefaultAsync(x => x.Id == command.TemplateId, ct)
            ?? throw new TenantNotificationTemplateNotFoundException(command.TemplateId);

        auditor.Record(
            db, tenantId, template.Id, template.TemplateKey, TemplateChangeKind.Deleted,
            previousSubject: template.Subject,
            newSubject: null,
            previousBody: template.Body,
            newBody: null,
            previousConstraints: TemplateConstraintSummary.Serialize(template.Constraints),
            newConstraints: null,
            reason: command.Reason);

        // The template is soft-deleted; the constraint rows have no soft-delete column, so they are
        // removed outright.
        db.TemplateConstraints.RemoveRange(template.Constraints.ToList());
        template.MarkAsDeleted();

        await db.SaveChangesAsync(ct);
    }
}
