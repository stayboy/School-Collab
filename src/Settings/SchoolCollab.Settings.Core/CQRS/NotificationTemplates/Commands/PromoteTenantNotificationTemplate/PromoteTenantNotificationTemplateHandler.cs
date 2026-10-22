using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.EntityCodes;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Exceptions;
using SchoolCollab.Settings.Core.DTOs;
using SchoolCollab.Settings.Core.Services;

namespace SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.PromoteTenantNotificationTemplate;

/// <summary>
/// Copies a tenant's template into the system blueprint (redesign spec 3.8) so every tenant sees it
/// until one owns an override. The source is never modified and the copy is a snapshot, not a live
/// link. Audited as <see cref="TemplateChangeKind.Created"/> with <c>TenantId = null</c>.
/// <para>
/// The copy keeps the title and description and mints its <b>own</b> code from the
/// <see cref="TenantNotificationTemplate.CodeRule"/> rule, so the source row and the blueprint stay
/// separately traceable (naming spec D7).
/// </para>
/// <para>
/// This is the <b>one cross-tenant read</b> in the feature: a system-scope session ignores the "Tenant"
/// filter to reach the source row, and that read is wrapped in
/// <see cref="ITenantContextAccessor.SuppressTenantGuard"/> (the sanctioned bypass). It is gated by
/// scope, not by a claim - a tenant session is refused here (403), so it can never reach another
/// tenant's row. An identical system-side constraint set is a conflict (409).
/// </para>
/// </summary>
public sealed class PromoteTenantNotificationTemplateHandler(
    SettingsDbContext db,
    ITenantProvider tenantProvider,
    ITenantContextAccessor tenantContextAccessor,
    IEntityCodeGenerator entityCodeGenerator,
    TemplateAuditRecorder auditor)
    : ICommandHandler<PromoteTenantNotificationTemplate, TenantNotificationTemplateDto>
{
    public async Task<TenantNotificationTemplateDto> HandleAsync(
        PromoteTenantNotificationTemplate command, CancellationToken ct = default)
    {
        var currentTenantId = tenantProvider.GetTenantContext().TenantId;

        if (!NotificationTemplateOwnership.IsSystemScope(currentTenantId))
        {
            throw new TenantNotificationTemplateScopeException(
                "Publishing a template portal-wide requires the system scope; only the blueprint editor "
                + "can promote a tenant's template.");
        }

        // Sanctioned cross-tenant read: the "Tenant" filter is ignored for ONE row, by id, in system
        // scope. The named-soft-delete filter still applies, so a tombstoned row cannot be promoted.
        using var guard = tenantContextAccessor.SuppressTenantGuard();

        var source = await db.TenantNotificationTemplates
            .IgnoreQueryFilters(["Tenant"])
            .Include(x => x.Constraints)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == command.TemplateId, ct)
            ?? throw new TenantNotificationTemplateNotFoundException(command.TemplateId);

        if (source.IsSystem)
        {
            throw new TenantNotificationTemplateConflictException(
                "This template is already part of the system blueprint.");
        }

        var sourceIdentity = TemplateConstraintSummary.Identity(source.Constraints);
        var systemRows = await db.TenantNotificationTemplates
            .Include(x => x.Constraints)
            .AsNoTracking()
            .Where(x => x.TenantId == null && x.Kind == source.Kind && x.Channel == source.Channel)
            .ToListAsync(ct);

        if (systemRows.Any(r => TemplateConstraintSummary.Identity(r.Constraints) == sourceIdentity))
        {
            throw new TenantNotificationTemplateConflictException(
                "The system blueprint already has a template with the identical constraint set for this "
                + "type and contact mode.");
        }

        var copy = TenantNotificationTemplate.Create(
            tenantId: null, source.Kind, source.Channel,
            source.Title, source.Description,
            await entityCodeGenerator.GenerateAsync(TenantNotificationTemplate.CodeRule, ct),
            source.Subject, source.Body,
            source.Constraints.Select(c => (c.Dimension, c.Value)).ToList());

        db.TenantNotificationTemplates.Add(copy);

        auditor.Record(
            db, tenantId: null, copy.Id, copy.TemplateKey, TemplateChangeKind.Created,
            previousSubject: null, newSubject: copy.Subject,
            previousBody: null, newBody: copy.Body,
            previousConstraints: null,
            newConstraints: TemplateConstraintSummary.Serialize(copy.Constraints),
            reason: command.Reason);

        await db.SaveChangesAsync(ct);

        return NotificationTemplateOwnership.ToDto(copy, NotificationTemplateOwnership.SystemOwnerName);
    }
}