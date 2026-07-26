using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;
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
        var search = (query.Search ?? string.Empty).Trim();

        return await cache.GetOrCreateAsync(
            $"guardians:list:{tenantId}:{search}",
            (db, tenantId, search),
            static async (state, ct) =>
            {
                var (db, tenantId, search) = state;
                var q = db.Guardians
                    .IgnoreQueryFilters(["Tenant"])
                    .Where(g => g.TenantId == tenantId);
                if (search.Length > 0)
                {
                    q = q.Where(g => g.FirstName.Contains(search)
                                 || g.LastName.Contains(search)
                                 || (g.DisplayName != null && g.DisplayName.Contains(search))
                                 || db.Contacts.Any(c => c.OwnerType == ContactOwnerType.Guardian
                                                      && c.OwnerId == g.Id
                                                      && c.Value.Contains(search)));
                }
                var results = await q.OrderBy(g => g.LastName).ThenBy(g => g.FirstName)
                    .ToArrayAsync(ct);

                // Load the primary (or first non-deleted) contact for each
                // guardian in a single round-trip so list UIs can show how to
                // reach the guardian without N+1 queries. A guardian may own
                // multiple contacts (spec §4.4); we surface the IsPrimary one,
                // falling back to the first contact when none is marked primary.
                var guardianIds = results.Select(g => g.Id).ToArray();
                var contacts = guardianIds.Length == 0
                    ? new List<Contact>()
                    : await db.Contacts
                        .IgnoreQueryFilters(["Tenant"])
                        .Where(c => c.TenantId == tenantId
                                 && c.OwnerType == ContactOwnerType.Guardian
                                 && guardianIds.Contains(c.OwnerId)
                                 && !c.IsDeleted)
                        .ToListAsync(ct);
                var contactsByOwner = contacts
                    // Spec §4.9: the "primary contact" is now the contact
                    // with the lowest DisplayOrder. IsPrimary is still
                    // honored as a tiebreaker for the additive phase.
                    .GroupBy(c => c.OwnerId)
                    .ToDictionary(g => g.Key, g => g.OrderBy(c => c.DisplayOrder).ThenByDescending(c => c.IsPrimary).First());

                return results.Select(g =>
                {
                    var c = contactsByOwner.TryGetValue(g.Id, out var pc) ? pc : null;
                    return new GuardianDto(
                        g.Id, g.TitleCodedValueId, g.FirstName, g.LastName, g.DisplayName, g.Address, g.CommunityId,
                        g.IsDeleted, g.CreatedAt, g.UpdatedAt,
                        c?.Channel, c?.Value, c?.CountryCode);
                }).ToArray();
            },
            CacheOptions,
            tags: ["guardians"],
            cancellationToken: cancellationToken);
    }
}
