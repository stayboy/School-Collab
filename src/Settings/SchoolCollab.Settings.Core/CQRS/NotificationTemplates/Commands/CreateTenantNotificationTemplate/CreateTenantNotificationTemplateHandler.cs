using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.DTOs;
using SchoolCollab.Settings.Core.Services;

namespace SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.CreateTenantNotificationTemplate;

/// <summary>
/// Creates one notification template row (plus its constraint rows) in the session's scope and writes
/// a <see cref="Domain.TemplateAuditEntry"/> of kind <see cref="Domain.TemplateChangeKind.Created"/> in
/// the same transaction. Scope (redesign spec 3.7): a real tenant owns the row, the system session
/// stores it as the blueprint (<c>NULL</c>) - never as <see cref="Guid.Empty"/>. An unconstrained
/// request is checked against the per-scope default invariant first
/// (<see cref="Services.TenantNotificationTemplateDefaults"/>) - a second default for the same
/// (kind, channel) in the same scope is a typed conflict, never a silent duplicate.
/// </summary>
public sealed class CreateTenantNotificationTemplateHandler(
    SettingsDbContext db,
    ITenantProvider tenantProvider,
    TemplateAuditRecorder auditor) : ICommandHandler<CreateTenantNotificationTemplate, TenantNotificationTemplateDto>
{
    public async Task<TenantNotificationTemplateDto> HandleAsync(
        CreateTenantNotificationTemplate command, CancellationToken ct = default)
    {
        var ownerId = NotificationTemplateOwnership.OwnerId(tenantProvider.GetTenantContext().TenantId);
        var request = command.Request;

        // JSON bodies can carry an explicit null for Constraints; treat it as "no constraints"
        // (= the scope default) rather than throwing on the null.
        var requestedConstraints = request.Constraints ?? [];

        if (requestedConstraints.Count == 0)
        {
            await TenantNotificationTemplateDefaults.EnsureNoCompetingDefaultAsync(
                db, request.Kind, request.Channel, ownerId, excludingTemplateId: null, ct);
        }

        // Create builds the constraint rows itself so each carries the template's generated id and
        // scope (callers cannot know it before the row exists).
        var template = TenantNotificationTemplate.Create(
            ownerId, request.Kind, request.Channel,
            request.Subject, request.Body,
            requestedConstraints.Select(c => (c.Dimension, c.Value)).ToList());

        db.TenantNotificationTemplates.Add(template);

        auditor.Record(
            db, ownerId, template.Id, template.TemplateKey, TemplateChangeKind.Created,
            previousSubject: null, newSubject: template.Subject,
            previousBody: null, newBody: template.Body,
            previousConstraints: null,
            newConstraints: TemplateConstraintSummary.Serialize(template.Constraints),
            reason: command.Reason);

        await db.SaveChangesAsync(ct);

        var ownerName = await NotificationTemplateOwnership.ResolveOwnerNameAsync(db, ownerId, ct);

        return new TenantNotificationTemplateDto(
            template.Id, template.Kind, template.Channel, template.Subject, template.Body,
            template.Constraints.Select(c => new TemplateConstraintDto(c.Id, c.Dimension, c.Value)).ToList(),
            template.CreatedAt, template.UpdatedAt, ownerId, ownerName);
    }
}