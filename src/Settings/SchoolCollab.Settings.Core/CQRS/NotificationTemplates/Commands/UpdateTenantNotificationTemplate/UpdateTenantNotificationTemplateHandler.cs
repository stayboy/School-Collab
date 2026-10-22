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
/// Replaces one template's title, description, subject, body and constraint set, addressed by id, and
/// writes a <see cref="TemplateAuditEntry"/> of kind <see cref="TemplateChangeKind.Updated"/> in the same
/// transaction. A row that is absent - or owned by another tenant, which the query filter makes
/// indistinguishable - is a <see cref="TenantNotificationTemplateNotFoundException"/>. A row the caller
/// can see but must not write (a tenant session editing a system-blueprint row) is a
/// <see cref="TenantNotificationTemplateScopeException"/> (403), never a silent stamp (spec section 6).
/// Clearing the constraint set makes the row its scope's default, so an unconstrained update runs the
/// per-scope default invariant check before the write.
/// <para>
/// The generated code is <b>not</b> part of this path (naming spec D6): a template's code identifies the
/// row for its whole life, so a rename changes the title and leaves the code alone.
/// </para>
/// </summary>
public sealed class UpdateTenantNotificationTemplateHandler(
    SettingsDbContext db,
    ITenantProvider tenantProvider,
    TemplateAuditRecorder auditor) : ICommandHandler<UpdateTenantNotificationTemplate, TenantNotificationTemplateDto>
{
    public async Task<TenantNotificationTemplateDto> HandleAsync(
        UpdateTenantNotificationTemplate command, CancellationToken ct = default)
    {
        var ownerId = NotificationTemplateOwnership.OwnerId(tenantProvider.GetTenantContext().TenantId);
        var request = command.Request;

        // JSON bodies can carry an explicit null for Constraints; treat it as "no constraints".
        var requestedConstraints = request.Constraints ?? [];

        var template = await db.TenantNotificationTemplates
            .Include(x => x.Constraints)
            .SingleOrDefaultAsync(x => x.Id == command.TemplateId, ct)
            ?? throw new TenantNotificationTemplateNotFoundException(command.TemplateId);

        // Writes never cross scope: the hybrid read filter shows a tenant session the system rows too
        // (they are readable so they can be adopted), so the guard has to be explicit here.
        if (template.TenantId != ownerId)
        {
            throw new TenantNotificationTemplateScopeException(
                template.TenantId is null
                    ? "This template belongs to the system blueprint and cannot be edited from a tenant "
                      + "session. Adopt it first, then edit your copy."
                    : "This template belongs to another scope and cannot be edited here.");
        }

        if (requestedConstraints.Count == 0)
        {
            await TenantNotificationTemplateDefaults.EnsureNoCompetingDefaultAsync(
                db, template.Kind, template.Channel, template.TenantId,
                excludingTemplateId: template.Id, ct);
        }

        // Capture the tracked rows before Update() swaps the in-memory collection, then make the
        // replace deterministic for EF: delete the old rows, insert the new ones (navigation-only
        // replacement would rely on severance semantics for a required FK).
        var oldRows = template.Constraints.ToList();
        var newRows = requestedConstraints
            .Select(c => TemplateConstraint.Create(template.TenantId, template.Id, c.Dimension, c.Value))
            .ToList();

        var previous = template.Update(request.Title, request.Description, request.Subject, request.Body, newRows);

        db.TemplateConstraints.RemoveRange(oldRows);
        db.TemplateConstraints.AddRange(newRows);

        auditor.Record(
            db, template.TenantId, template.Id, template.TemplateKey, TemplateChangeKind.Updated,
            previous.PreviousSubject, template.Subject,
            previous.PreviousBody, template.Body,
            TemplateConstraintSummary.Serialize(previous.PreviousConstraints),
            TemplateConstraintSummary.Serialize(template.Constraints),
            reason: command.Reason);

        await db.SaveChangesAsync(ct);

        var ownerName = await NotificationTemplateOwnership.ResolveOwnerNameAsync(db, template.TenantId, ct);

        return NotificationTemplateOwnership.ToDto(template, ownerName);
    }
}