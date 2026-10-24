using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.EntityCodes;
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
/// <para>
/// The row's generated code is minted here, once, from the in-context
/// <see cref="TenantNotificationTemplate.CodeRule"/> rule (naming spec D5/D6): Settings owns both the
/// rule and the row, so this is a generator call, not a cross-context hop. Nothing on the update path
/// can rewrite it.
/// </para>
/// </summary>
public sealed class CreateTenantNotificationTemplateHandler(
    SettingsDbContext db,
    ITenantProvider tenantProvider,
    IEntityCodeGenerator entityCodeGenerator,
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

        // A blank title is rejected by the domain, so the 400 arm of the route covers it.
        var code = await entityCodeGenerator.GenerateAsync(TenantNotificationTemplate.CodeRule, ct);

        // Create builds the constraint rows itself so each carries the template's generated id and
        // scope (callers cannot know it before the row exists). Every argument is named because `code`
        // and `subject` are both strings: a positional swap would compile.
        var template = TenantNotificationTemplate.Create(
            tenantId: ownerId,
            kind: request.Kind,
            channel: request.Channel,
            title: request.Title,
            description: request.Description,
            code: code,
            subject: request.Subject,
            body: request.Body,
            constraints: requestedConstraints.Select(c => (c.Dimension, c.Value)).ToList());

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

        return NotificationTemplateOwnership.ToDto(template, ownerName);
    }
}
