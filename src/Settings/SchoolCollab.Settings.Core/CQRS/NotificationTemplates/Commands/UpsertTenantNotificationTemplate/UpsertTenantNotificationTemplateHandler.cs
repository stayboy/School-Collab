using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.DTOs;
using SchoolCollab.Settings.Core.Services;

namespace SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.UpsertTenantNotificationTemplate;

/// <summary>
/// Creates or replaces the current tenant's notification template for
/// Kind × Channel. Writes a TemplateAuditEntry (Created or Updated)
/// in the same transaction.
/// </summary>
public sealed class UpsertTenantNotificationTemplateHandler(
    SettingsDbContext db,
    ITenantProvider tenantProvider,
    TemplateAuditRecorder auditor) : ICommandHandler<UpsertTenantNotificationTemplate, TenantNotificationTemplateDto>
{
    public async Task<TenantNotificationTemplateDto> HandleAsync(
        UpsertTenantNotificationTemplate command, CancellationToken ct = default)
    {
        var tenantId = tenantProvider.GetTenantContext().TenantId;

        // JSON bodies can carry an explicit null for Constraints; treat it as "no
        // constraints" rather than throwing a NullReferenceException.
        var requestedConstraints = command.Request.Constraints ?? [];

        var existing = await db.TenantNotificationTemplates
            .Include(x => x.Constraints)
            .SingleOrDefaultAsync(x => x.Kind == command.Kind && x.Channel == command.Channel, ct);

        TenantNotificationTemplate template;
        TemplateChangeKind changeKind;
        string? prevSubject = null;
        string? prevBody = null;
        string? prevConstraints = null;

        if (existing is not null)
        {
            prevSubject = existing.Subject;
            prevBody = existing.Body;
            prevConstraints = TemplateConstraintSummary.Serialize(existing.Constraints);

            // Capture the tracked rows before Update() swaps the in-memory
            // collection, then make the replace deterministic for EF: delete the
            // old rows, insert the new ones (navigation-only replacement would
            // rely on severance semantics for a required FK).
            var oldRows = existing.Constraints.ToList();
            var newRows = requestedConstraints
                .Select(c => TemplateConstraint.Create(tenantId, existing.Id, c.Dimension, c.Value))
                .ToList();

            existing.Update(command.Request.Subject, command.Request.Body, newRows);

            db.TemplateConstraints.RemoveRange(oldRows);
            db.TemplateConstraints.AddRange(newRows);

            template = existing;
            changeKind = TemplateChangeKind.Updated;
        }
        else
        {
            // Create builds the constraint rows itself so each carries the
            // template's generated id (callers cannot know it beforehand).
            template = TenantNotificationTemplate.Create(
                tenantId, command.Kind, command.Channel,
                command.Request.Subject, command.Request.Body,
                requestedConstraints.Select(c => (c.Dimension, c.Value)).ToList());

            db.TenantNotificationTemplates.Add(template);
            changeKind = TemplateChangeKind.Created;
        }

        var newConstraints = TemplateConstraintSummary.Serialize(template.Constraints);

        auditor.Record(
            db, tenantId, template.Id, template.TemplateKey, changeKind,
            prevSubject, template.Subject,
            prevBody, template.Body,
            prevConstraints, newConstraints,
            command.Reason);

        await db.SaveChangesAsync(ct);

        return new TenantNotificationTemplateDto(
            template.Id, template.Kind, template.Channel, template.Subject, template.Body,
            template.Constraints.Select(c => new TemplateConstraintDto(c.Id, c.Dimension, c.Value)).ToList(),
            template.UpdatedAt);
    }
}