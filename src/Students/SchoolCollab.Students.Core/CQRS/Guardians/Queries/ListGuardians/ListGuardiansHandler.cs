using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Guardians.Queries.ListGuardians;

public sealed class ListGuardiansHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListGuardians, GuardianDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<GuardianDto[]> HandleAsync(ListGuardians query, CancellationToken cancellationToken = default)
    {
        // Capture the tenant: db.CurrentTenantId is lost inside the HybridCache factory.
        var tenantId = db.CurrentTenantId;

        return await cache.GetOrCreateAsync(
            $"guardians:list:{tenantId}",
            (db, tenantId),
            static async (state, ct) =>
            {
                var (db, tenantId) = state;
                var results = await db.Guardians
                    .IgnoreQueryFilters(["Tenant"])
                    .Where(g => g.TenantId == tenantId)
                    .OrderBy(g => g.LastName).ThenBy(g => g.FirstName)
                    .ToArrayAsync(ct);

                return results.Select(g => new GuardianDto(
                    g.Id, g.TitleCodedValueId, g.FirstName, g.LastName, g.DisplayName, g.Address, g.CommunityId,
                    g.IsDeleted, g.CreatedAt, g.UpdatedAt)).ToArray();
            },
            CacheOptions,
            tags: ["guardians"],
            cancellationToken: cancellationToken);
    }
}
