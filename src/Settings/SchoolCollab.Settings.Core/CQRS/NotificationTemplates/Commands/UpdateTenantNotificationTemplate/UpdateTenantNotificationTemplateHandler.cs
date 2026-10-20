using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Exceptions;
using SchoolCollab.Settings.Core.DTOs;
using SchoolCollab.Settings.Core.Services;

namespace SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.UpdateTenantNotificationTemplate;

/// <summary>
/// Replaces one template's subject, body and constraint set, addressed by id, and writes a
/// <see cref="TemplateAuditEntry"/> of kind <see cref="TemplateChangeKind.Updated"/> in the same
/// transaction. A row that is absent — or owned by another tenant, which the tenant query filter
/// makes indistinguishable — is a <see cref="TenantNotificationTemplateNotFoundException"/>. Clearing
/// the constraint set makes the row the tenant default, so an unconstrained update runs the
/// tenant-default invariant check before the write.
/// </summary>
public sealed class UpdateTenantNotificationTemplateHandler(
    SettingsDbContext db,
    ITenantProvider tenantProvider,
    TemplateAuditRecorder auditor) : ICommandHandler<UpdateTenantNotificationTemplate, TenantNotificationTemplateDto>
{
    public async Task<TenantNotificationTemplateDto> HandleAsync(
        UpdateTenantNotificationTemplate command, CancellationToken ct = default)
    {
        var tenantId = tenantProvider.GetTenantContext().TenantId;
        var request = command.Request;

        // JSON bodies can carry an explicit null for Constraints; treat it as "no constraints".
        var requestedConstraints = request.Constraints ?? [];

        var template = await db.TenantNotificationTemplates
            .Include(x => x.Constraints)
            .SingleOrDefaultAsync(x => x.Id == command.TemplateId, ct)
            ?? throw new TenantNotificationTemplateNotFoundException(command.TemplateId);

        if (requestedConstraints.Count == 0)
        {
            await TenantNotificationTemplateDefaults.EnsureNoCompetingDefaultAsync(
                db, template.Kind, template.Channel, excludingTemplateId: template.Id, ct);
        }

        var previousSubject = template.Subject;
        var previousBody = template.Body;
        var previousConstraints = TemplateConstraintSummary.Serialize(template.Constraints);

        // Capture the tracked rows before Update() swaps the in-memory collection, then make the
        // replace deterministic for EF: delete the old rows, insert the new ones (navigation-only
        // replacement would rely on severance semantics for a required FK).
        var oldRows = template.Constraints.ToList();
        var newRows = requestedConstraints
            .Select(c => TemplateConstraint.Create(tenantId, template.Id, c.Dimension, c.Value))
            .ToList();

        template.Update(request.Subject, request.Body, newRows);

        db.TemplateConstraints.RemoveRange(oldRows);
        db.TemplateConstraints.AddRange(newRows);

        auditor.Record(
            db, tenantId, template.Id, template.TemplateKey, TemplateChangeKind.Updated,
            previousSubject, template.Subject,
            previousBody, template.Body,
            previousConstraints, TemplateConstraintSummary.Serialize(template.Constraints),
            reason: command.Reason);

        await db.SaveChangesAsync(ct);

        return new TenantNotificationTemplateDto(
            template.Id, template.Kind, template.Channel, template.Subject, template.Body,
            template.Constraints.Select(c => new TemplateConstraintDto(c.Id, c.Dimension, c.Value)).ToList(),
            template.CreatedAt, template.UpdatedAt);
    }
}
