using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Services;

/// <summary>
/// Local read access to replicated coded values — the write-off of the
/// Students→settings sync hop on the enroll path
/// (adr-cross-module-calls.md Phase 1). Same shape as
/// <see cref="ICodedValuesApiClient"/> so the flag-gated swap is a strategy
/// switch.
/// </summary>
public interface ILocalCodedValueRepository
{
    /// <summary>Resolves the effective coded value for the current tenant. Null when unknown/deleted.</summary>
    Task<StreamCodedValueDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
}

public sealed class LocalCodedValueRepository(
    IDbContextFactory<StudentsDbContext> dbFactory,
    ITenantProvider tenantProvider,
    HybridCache cache) : ILocalCodedValueRepository
{
    // IDbContextFactory (not a scoped context): this repository runs inside the
    // projection consumer's background scope and inside HybridCache factory
    // bodies, which may outlive any request scope (see Settings hybrid-cache
    // DbContext-lifetime lesson).

    public async Task<StreamCodedValueDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var tenantKey = tenantProvider.GetTenantContext().TenantId; // Guid.Empty = default tenant

        return await cache.GetOrCreateAsync(
            $"coded-values:{tenantKey}:{id}",
            async token =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(token);

                var rows = await db.LocalCodedValues
                    .AsNoTracking()
                    .Where(x => x.Id == id && (x.TenantId == null || x.TenantId == tenantKey))
                    .ToListAsync(token);

                return Resolve(rows);
            },
            tags: ["coded-values"],
            cancellationToken: ct);
    }

    /// <summary>
    /// Mirrors GetCodedValueByIdHandler's resolution: global row + tenant row
    /// merged (tenant overlay wins for non-null Name/Description/Code); a
    /// tenant-owned row with no global row stands alone; deleted values are
    /// not-found.
    /// </summary>
    internal static StreamCodedValueDto? Resolve(IReadOnlyList<LocalCodedValue> rows)
    {
        var global = rows.FirstOrDefault(r => r.TenantId == null && !r.IsDeleted);
        var tenant = rows.FirstOrDefault(r => r.TenantId != null);

        if (tenant is null && global is null)
            return null;

        if (global is null)
        {
            // Tenant-owned standalone value.
            return tenant!.IsDeleted ? null : ToDto(tenant, tenant);
        }

        return ToDto(global, tenant ?? global);
    }

    private static StreamCodedValueDto ToDto(LocalCodedValue source, LocalCodedValue overlay) => new(
        source.Id,
        overlay.Code ?? source.Code,
        overlay.Name ?? source.Name,
        overlay.Description ?? source.Description,
        source.ParentId,
        source.ParentCode,
        source.IsDisabled,
        source.DisplayOrder,
        source.CreatedAt,
        source.UpdatedAt,
        [.. source.Attributes.Select(a => new StreamAttributeDto(a.Key, a.Value))]);
}
