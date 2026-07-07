using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
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
/// delegating to <see cref="GetCodedValueByIdHandler"/>. Invalidates the
/// <c>coded-values</c> and <c>tenant:{tenantId}</c> cache tags so subsequent reads
/// observe the new override. See documents/specs/grade-level-setup.md §5.1.
///
/// <para><b>Default-tenant branch.</b> When the current tenant is the sentinel
/// "default" tenant (no real tenant in scope — e.g. the dev tenant switcher's
/// "(default tenant)" entry, or a background worker), there is no per-tenant
/// override table to write to: the override concept is meaningless without a
/// real tenant. In that case we update the <b>global</b> <see cref="CodedValue"/>
/// directly so the wizard's "Override name" action still has a visible effect
/// (renames the blueprint every caller sees). Real tenants continue to get
/// per-tenant <see cref="TenantCodedValueOverride"/> rows.</para>
/// </summary>
public sealed class UpsertCodedValueOverrideHandler(
    SettingsDbContext db,
    ITenantProvider tenantProvider,
    HybridCache cache,
    IQueryHandler<GetCodedValueById, CodedValueDto?> resolver) : ICommandHandler<UpsertCodedValueOverride, CodedValueDto>
{
    public async Task<CodedValueDto> HandleAsync(UpsertCodedValueOverride command, CancellationToken ct = default)
    {
        var tenantContext = tenantProvider.GetTenantContext();
        var tenantId = tenantContext.TenantId;

        // Reject overrides for non-existent coded values up front (don't create an
        // orphan override row, and don't update a row that doesn't exist).
        var codedValue = await db.CodedValues
            .SingleOrDefaultAsync(x => x.Id == command.GlobalCodedValueId, ct);
        if (codedValue is null)
            throw new CodedValueNotFoundException(command.GlobalCodedValueId);

        if (tenantContext.IsDefault)
        {
            // No real tenant — the "override" rewrites the global blueprint.
            // DisplayOrder is metadata, not part of the override, so we preserve
            // the existing value rather than letting the caller clear it.
            codedValue.Update(command.Name, command.Description, codedValue.DisplayOrder);
        }
        else
        {
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
        }

        await db.SaveChangesAsync(ct);

        await InvalidateCacheAsync(tenantId, ct);

        // Return the fully-resolved DTO (override applied, attributes/definitions
        // populated) by re-reading through the existing query handler. The cache
        // was just invalidated, so this repopulates it with the fresh override.
        var resolved = await resolver.HandleAsync(new GetCodedValueById(command.GlobalCodedValueId), ct)
            ?? throw new CodedValueNotFoundException(command.GlobalCodedValueId);

        return resolved;
    }

    private async Task InvalidateCacheAsync(Guid tenantId, CancellationToken ct)
    {
        // `coded-values` covers the per-coded-value read cache (GetCodedValueById);
        // `tenant:{tenantId}` covers the (future) tenant-scoped landing caches.
        // RemoveByTagAsync on a tag with no entries is a no-op, so the tenant tag
        // is safe to evict even before PR 3 introduces entries tagged with it.
        await cache.RemoveByTagAsync("coded-values", ct);
        await cache.RemoveByTagAsync($"tenant:{tenantId}", ct);
    }
}
