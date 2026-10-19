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
        // The ward id selects WHICH guardian-linked student's name is enriched, so it is part
        // of the cache identity (§3.4) — a guardian with two wards must not serve ward A's name
        // for a ward B resolution.
        var wardKey = query.StudentId?.ToString() ?? "all";

        return await cache.GetOrCreateAsync(
            $"contacts:subscribed:{tenantId}:{(int)query.OwnerType}:{query.OwnerId?.ToString() ?? "all"}:{scopeKey}:{wardKey}",
            (db, tenantId, query.OwnerType, query.OwnerId, query.Scope, query.StudentId),
            static async (state, ct) =>
            {
                var (db, tenantId, ownerType, ownerId, scope, wardStudentId) = state;
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

                // §3.4 name enrichment — read from this context's own tables, where the owner
                // rows are already in scope, so the Assignments resolver needs no second hop for
                // a display name. Both degrade to null (= unknown) and never throw.
                var contactName = ownerType == ContactOwnerType.Guardian && ownerId is not null
                    ? (await db.Guardians.IgnoreQueryFilters(["Tenant"])
                        .Where(g => g.TenantId == tenantId && g.Id == ownerId)
                        .Select(g => g.DisplayName)
                        .FirstOrDefaultAsync(ct))
                    : null;

                // The ward: a student-owned contact is its own ward; a guardian-owned contact
                // carries the caller's ward (query.StudentId). Teacher contacts have none.
                var studentId = ownerType == ContactOwnerType.Student ? ownerId : wardStudentId;
                string? studentName = null;
                if (studentId is not null)
                {
                    var ward = await db.Students.IgnoreQueryFilters(["Tenant"])
                        .Where(s => s.TenantId == tenantId && s.Id == studentId)
                        .Select(s => new { s.FirstName, s.LastName })
                        .FirstOrDefaultAsync(ct);
                    studentName = ward is null
                        ? null
                        : $"{ward.FirstName} {ward.LastName}".Trim();
                }

                return contacts
                    .Select(c => new SubscribedContactDto(
                        c.Id, c.Channel, c.Value, role,
                        string.IsNullOrWhiteSpace(contactName) ? null : contactName,
                        string.IsNullOrWhiteSpace(studentName) ? null : studentName))
                    .ToArray();
            },
            CacheOptions,
            tags: ["contacts"],
            cancellationToken: cancellationToken);
    }
}
