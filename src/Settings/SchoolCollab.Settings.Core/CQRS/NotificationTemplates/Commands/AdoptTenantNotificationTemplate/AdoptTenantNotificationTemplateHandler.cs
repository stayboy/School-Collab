using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.EntityCodes;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Exceptions;
using SchoolCollab.Settings.Core.DTOs;
using SchoolCollab.Settings.Core.Services;

namespace SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.AdoptTenantNotificationTemplate;

/// <summary>
/// Copies a system-blueprint template into the caller's tenant (redesign spec 3.8). The copy is a
/// one-way snapshot - title, description, subject, body and every constraint row - and the source row is
/// never touched, so a later system edit does not reach an adopted copy. Audited as
/// <see cref="TemplateChangeKind.Created"/>.
/// <para>
/// The readable name travels (the operator adopted <i>that</i> template); the code does not - the copy
/// mints its own from the <see cref="TenantNotificationTemplate.CodeRule"/> rule, so two live rows never
/// share an identifier (naming spec D7).
/// </para>
/// <para>
/// Refusals: a system-scope caller has no tenant to adopt into (403); an already-tenant-owned source is
/// not adoptable (409); and an identical constraint set already present in the tenant is a conflict, not
/// a duplicate (409 - re-adopting is explicit and must not fork the tenant's candidate list).
/// </para>
/// </summary>
public sealed class AdoptTenantNotificationTemplateHandler(
    SettingsDbContext db,
    ITenantProvider tenantProvider,
    IEntityCodeGenerator entityCodeGenerator,
    TemplateAuditRecorder auditor)
    : ICommandHandler<AdoptTenantNotificationTemplate, TenantNotificationTemplateDto>
{
    public async Task<TenantNotificationTemplateDto> HandleAsync(
        AdoptTenantNotificationTemplate command, CancellationToken ct = default)
    {
        var tenantId = tenantProvider.GetTenantContext().TenantId;

        if (NotificationTemplateOwnership.IsSystemScope(tenantId))
        {
            throw new TenantNotificationTemplateScopeException(
                "Adopting a template requires a tenant session - the system scope has no tenant to copy "
                + "into.");
        }

        var source = await db.TenantNotificationTemplates
            .Include(x => x.Constraints)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == command.TemplateId, ct)
            ?? throw new TenantNotificationTemplateNotFoundException(command.TemplateId);

        if (!source.IsSystem)
        {
            throw new TenantNotificationTemplateConflictException(
                "Only a system-blueprint template can be adopted; this template already belongs to a "
                + "tenant.");
        }

        var sourceIdentity = TemplateConstraintSummary.Identity(source.Constraints);
        var tenantRows = await db.TenantNotificationTemplates
            .Include(x => x.Constraints)
            .AsNoTracking()
            .Where(x => x.TenantId != null && x.Kind == source.Kind && x.Channel == source.Channel)
            .ToListAsync(ct);

        if (tenantRows.Any(r => TemplateConstraintSummary.Identity(r.Constraints) == sourceIdentity))
        {
            throw new TenantNotificationTemplateConflictException(
                "This tenant already has a template with the identical constraint set for this type and "
                + "contact mode - edit that copy instead of adopting again.");
        }

        var copy = TenantNotificationTemplate.Create(
            tenantId, source.Kind, source.Channel,
            source.Title, source.Description,
            await entityCodeGenerator.GenerateAsync(TenantNotificationTemplate.CodeRule, ct),
            source.Subject, source.Body,
            source.Constraints.Select(c => (c.Dimension, c.Value)).ToList());

        db.TenantNotificationTemplates.Add(copy);

        auditor.Record(
            db, tenantId, copy.Id, copy.TemplateKey, TemplateChangeKind.Created,
            previousSubject: null, newSubject: copy.Subject,
            previousBody: null, newBody: copy.Body,
            previousConstraints: null,
            newConstraints: TemplateConstraintSummary.Serialize(copy.Constraints),
            reason: command.Reason);

        await db.SaveChangesAsync(ct);

        var ownerName = await NotificationTemplateOwnership.ResolveOwnerNameAsync(db, tenantId, ct);
        return NotificationTemplateOwnership.ToDto(copy, ownerName);
    }
}