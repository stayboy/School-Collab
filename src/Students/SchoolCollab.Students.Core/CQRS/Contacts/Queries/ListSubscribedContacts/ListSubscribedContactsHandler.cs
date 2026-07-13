using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Contacts.Queries.ListSubscribedContacts;

public sealed class ListSubscribedContactsHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListSubscribedContacts, SubscribedContactDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<SubscribedContactDto[]> HandleAsync(ListSubscribedContacts query, CancellationToken cancellationToken = default)
    {
        var tenantId = db.CurrentTenantId;
        var scopeKey = query.Scope.HasValue ? ((int)query.Scope.Value).ToString() : "all";

        return await cache.GetOrCreateAsync(
            $"contacts:subscribed:{tenantId}:{(int)query.OwnerType}:{query.OwnerId?.ToString() ?? "all"}:{scopeKey}",
            (db, tenantId, query.OwnerType, query.OwnerId, query.Scope),
            static async (state, ct) =>
            {
                var (db, tenantId, ownerType, ownerId, scope) = state;
                var contacts = await db.Contacts
                    .IgnoreQueryFilters(["Tenant"])
                    .Where(c => c.TenantId == tenantId && c.OwnerType == ownerType && (ownerId == null || c.OwnerId == ownerId))
                    .Join(db.ContactSubscriptions.IgnoreQueryFilters(["Tenant"]),
                        c => c.Id, s => s.ContactId,
                        (c, s) => new { c, s })
                    .Where(x => x.s.TenantId == tenantId
                        && x.s.Status == SubscriptionStatus.Subscribed
                        && (!scope.HasValue || x.s.Scope == scope.Value))
                    .Select(x => x.c)
                    .OrderBy(c => c.Channel).ThenBy(c => c.Value)
                    .ToArrayAsync(ct);

                GuardianRole? role = null;
                if (ownerType == ContactOwnerType.Guardian)
                {
                    var links = await db.StudentGuardians.IgnoreQueryFilters(["Tenant"])
                        .Where(l => l.TenantId == tenantId && l.GuardianId == ownerId)
                        .ToArrayAsync(ct);
                    role = links.Length == 0
                        ? null
                        : (links.Any(l => l.Role == GuardianRole.Primary) ? GuardianRole.Primary : GuardianRole.CC);
                }

                return contacts.Select(c => new SubscribedContactDto(c.Id, c.Channel, c.Value, role)).ToArray();
            },
            CacheOptions,
            tags: ["contacts"],
            cancellationToken: cancellationToken);
    }
}
