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
/// </summary>
public sealed class UpsertCodedValueOverrideHandler(
    SettingsDbContext db,
    ITenantProvider tenantProvider,
    HybridCache cache,
    IQueryHandler<GetCodedValueById, CodedValueDto?> resolver) : ICommandHandler<UpsertCodedValueOverride, CodedValueDto>
{
    public async Task<CodedValueDto> HandleAsync(UpsertCodedValueOverride command, CancellationToken ct = default)
    {
        var tenantId = tenantProvider.GetTenantContext().TenantId;

        // Reject overrides for non-existent coded values up front (don't create an
        // orphan override row). Returns 404 via the endpoint's catch.
        var codedValueExists = await db.CodedValues
            .AnyAsync(x => x.Id == command.GlobalCodedValueId, ct);
        if (!codedValueExists)
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