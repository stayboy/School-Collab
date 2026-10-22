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
/// This is the <b>one cross-tenant read</b> in the feature: a system-scope session drops the "Tenant"
/// filter to reach the source row, and that read is the sanctioned <b>named</b> suppression
/// (<c>IgnoreQueryFilters(["Tenant"])</c>). It is gated by scope, not by a claim - a tenant session is
/// refused here (403), so it can never reach another tenant's row. An identical system-side constraint
/// set is a conflict (409).
/// </para>
/// <para>
/// <see cref="ITenantContextAccessor.SuppressTenantGuard"/> wraps the <b>insert</b>, not the read: it is
/// a save-time guard (<c>ModuleDbContext.PrepareChanges</c>), so only a write can ever be the reason for
/// it. It scopes the one write this handler makes outside the session's own scope — the blueprint copy
/// (<c>TenantId = null</c>) — and nothing else.
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

        // Sanctioned cross-tenant read: the "Tenant" filter is dropped for ONE row, by id, in system
        // scope. The named-soft-delete filter still applies, so a tombstoned row cannot be promoted.
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

        // Every argument is named because `code` and `subject` are both strings: a positional swap
        // would compile.
        var copy = TenantNotificationTemplate.Create(
            tenantId: null,
            kind: source.Kind,
            channel: source.Channel,
            title: source.Title,
            description: source.Description,
            code: await entityCodeGenerator.GenerateAsync(TenantNotificationTemplate.CodeRule, ct),
            subject: source.Subject,
            body: source.Body,
            constraints: source.Constraints.Select(c => (c.Dimension, c.Value)).ToList());

        db.TenantNotificationTemplates.Add(copy);

        auditor.Record(
            db, tenantId: null, copy.Id, copy.TemplateKey, TemplateChangeKind.Created,
            previousSubject: null, newSubject: copy.Subject,
            previousBody: null, newBody: copy.Body,
            previousConstraints: null,
            newConstraints: TemplateConstraintSummary.Serialize(copy.Constraints),
            reason: command.Reason);

        // The sanctioned bypass wraps the WRITE that needs it — the blueprint copy's insert
        // (TenantId = null), the one row this session writes outside its own scope. Wrapping the read
        // above would be the no-op dotnet-best-practices.md forbids: the guard is consulted at save time
        // only, and the cross-tenant read is the named filter suppression.
        using (tenantContextAccessor.SuppressTenantGuard())
        {
            await db.SaveChangesAsync(ct);
        }

        return NotificationTemplateOwnership.ToDto(copy, NotificationTemplateOwnership.SystemOwnerName);
    }
}