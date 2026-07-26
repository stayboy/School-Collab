using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Contacts.Queries.ListContacts;

public sealed class ListContactsHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListContacts, ContactDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<ContactDto[]> HandleAsync(ListContacts query, CancellationToken cancellationToken = default)
    {
        var tenantId = db.CurrentTenantId;

        return await cache.GetOrCreateAsync(
            $"contacts:list:{tenantId}:{(int)query.OwnerType}:{query.OwnerId}",
            (db, tenantId, query.OwnerType, query.OwnerId),
            static async (state, ct) =>
            {
                var (db, tenantId, ownerType, ownerId) = state;
                var results = await db.Contacts
                    .IgnoreQueryFilters(["Tenant"])
                    .Where(c => c.TenantId == tenantId && c.OwnerType == ownerType && c.OwnerId == ownerId)
                    // Spec §4.9: order by DisplayOrder first (preferred contact
                    // has the lowest order), then by Channel and Value for a
                    // stable secondary sort.
                    .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Channel).ThenBy(c => c.Value)
                    .ToArrayAsync(ct);

                return results.Select(c => new ContactDto(
                    c.Id, c.OwnerType, c.OwnerId, c.Channel, c.Value, c.Label, c.IsPrimary, c.IsVerified, c.IsDeleted,
                    c.CreatedAt, c.UpdatedAt)
                {
                    CountryCode = c.CountryCode,
                    DisplayOrder = c.DisplayOrder
                }).ToArray();
            },
            CacheOptions,
            tags: ["contacts"],
            cancellationToken: cancellationToken);
    }
}
