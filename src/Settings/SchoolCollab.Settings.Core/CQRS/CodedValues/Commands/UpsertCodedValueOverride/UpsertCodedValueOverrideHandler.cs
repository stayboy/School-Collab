using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Queries.GetCodedValueById;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Exceptions;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.UpsertCodedValueOverride;

/// <summary>
/// Creates or updates the current tenant's override for a global coded value, then
/// returns the <b>resolved</b> <see cref="CodedValueDto"/> (override applied) by
/// delegating to <see cref="GetCodedValueByIdHandler"/>. See
/// documents/specs/grade-level-setup.md §5.1.
///
/// <para><b>Tenancy isolation.</b> Overrides are always stored as per-tenant
/// <see cref="TenantCodedValueOverride"/> rows keyed by the current tenant id —
/// the default tenant (sentinel <see cref="Guid.Empty"/>) included. The default
/// tenant therefore gets a dedicated override row that is invisible to any real
/// tenant (its tenant id is <see cref="Guid.Empty"/>, which no real tenant
/// shares) and the global <see cref="CodedValue"/> blueprint is never rewritten.
/// The wizard hides the override action for the default tenant (see
/// <c>IsRealTenant</c> in <c>GradeLevelWizard.razor</c>), but the handler still
/// accepts the call so dev/test workflows that bypass the UI keep working.</para>
/// </summary>
public sealed class UpsertCodedValueOverrideHandler(
    SettingsDbContext db,
    ITenantProvider tenantProvider,
    ITenantContextAccessor tenantContextAccessor,
    IQueryHandler<GetCodedValueById, CodedValueDto?> resolver,
    HybridCache cache,
    ILogger<UpsertCodedValueOverrideHandler> logger) : ICommandHandler<UpsertCodedValueOverride, CodedValueDto>
{
    public async Task<CodedValueDto> HandleAsync(UpsertCodedValueOverride command, CancellationToken ct = default)
    {
        var tenantContext = tenantProvider.GetTenantContext();
        var tenantId = tenantContext.TenantId; // Guid.Empty for the default tenant.

        // Reject overrides for non-existent coded values up front (don't create an
        // orphan override row, and don't update a row that doesn't exist).
        var codedValue = await db.CodedValues
            .SingleOrDefaultAsync(x => x.Id == command.GlobalCodedValueId, ct);
        if (codedValue is null)
            throw new CodedValueNotFoundException(command.GlobalCodedValueId);

        var existing = await db.TenantCodedValueOverrides
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.GlobalCodedValueId == command.GlobalCodedValueId, ct);

        if (existing is not null)
        {
            existing.Update(command.Name, command.Description);
        }
        else
        {
            db.TenantCodedValueOverrides.Add(TenantCodedValueOverride.Create(
                tenantId, command.GlobalCodedValueId, command.Name, command.Description));
        }
        logger.LogInformation(
            "Override upserted for tenant {TenantId} (isDefault={IsDefault}), coded value {Id}",
            tenantId, tenantContext.IsDefault, command.GlobalCodedValueId);

        // FR-8/FR-10: the default/dev tenant (Guid.Empty) stores its override as a
        // real row keyed by Guid.Empty. The strict save-guard rejects Guid.Empty on
        // Added/Modified, so suppress it for the dev affordance (sanctioned bypass).
        // Real-tenant saves satisfy the guard (TenantId == current) and are NOT
        // suppressed, preserving the mismatch defense.
        if (tenantId == Guid.Empty)
        {
            using (tenantContextAccessor.SuppressTenantGuard())
            {
                await db.SaveChangesAsync(ct);
            }
        }
        else
        {
            await db.SaveChangesAsync(ct);
        }

        // Return the fully-resolved DTO (override applied, attributes/definitions
        // populated) by re-reading through the existing query handler. No cache
        // is involved — GetCodedValueById reads directly from the DB.
        var resolved = await resolver.HandleAsync(new GetCodedValueById(command.GlobalCodedValueId), ct)
            ?? throw new CodedValueNotFoundException(command.GlobalCodedValueId);

        // Invalidate the coded-values cache so the dropdown lists (by-parent,
        // by-code, search, etc.) refresh promptly after an override change.
        await cache.RemoveByTagAsync("coded-values", ct);
        await cache.RemoveByTagAsync($"tenant:{tenantId}", ct);

        return resolved;
    }
}
