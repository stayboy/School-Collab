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
        var excludeStudentId = query.ExcludeStudentId;
        var studentId = query.StudentId;

        return await cache.GetOrCreateAsync(
            // Partition the cache by the excluded student so a guardian list
            // that hides student S's already-linked guardians does not leak
            // into a different student's picker. null = no exclusion. Also
            // partition by the (optional) student filter so a student-scoped
            // guardians view does not share rows with the unfiltered list.
            $"guardians:list:{tenantId}:{search}:{excludeStudentId}:{studentId}",
            (db, tenantId, search, excludeStudentId, studentId),
            static async (state, ct) =>
            {
                var (db, tenantId, search, excludeStudentId, studentId) = state;
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
                // Exclude guardians already linked to the given student so the
                // guardian picker cannot offer a guardian that is already
                // linked (prevents double-linking). StudentGuardian has no
                // soft-delete (links are hard-deleted on unlink), so only the
                // Tenant filter is ignored — and we filter tenant explicitly
                // because db.CurrentTenantId is lost inside the cache factory.
                if (excludeStudentId is { } sid)
                {
                    q = q.Where(g => !db.StudentGuardians
                        .IgnoreQueryFilters(new[] { "Tenant" })
                        .Any(sg => sg.TenantId == tenantId
                                && sg.StudentId == sid
                                && sg.GuardianId == g.Id));
                }
                // Restrict to guardians linked to a specific student (e.g. the
                // student-scoped guardians view reached from the student landing
                // page's "N guardians" cell). Excludes soft-deleted links the
                // same way ListGuardiansByStudent does.
                if (studentId is { } sid2)
                {
                    q = q.Where(g => db.StudentGuardians
                        .IgnoreQueryFilters(new[] { "Tenant" })
                        .Any(sg => sg.TenantId == tenantId
                                && sg.StudentId == sid2
                                && sg.GuardianId == g.Id));
                }
                var results = await q.OrderBy(g => g.LastName).ThenBy(g => g.FirstName)
                    .ToArrayAsync(ct);

                // Load the guardian's top contacts in display order so
                // list UIs can show how to reach the guardian without N+1
                // queries. A guardian may own multiple contacts (spec §4.4);
                // we surface the first three ordered by DisplayOrder.
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
                    .GroupBy(c => c.OwnerId)
                    .ToDictionary(
                        g => g.Key,
                        g => g.OrderBy(c => c.DisplayOrder)
                              .Take(3)
                              .Select(c => new GuardianContactViewDto(c.Channel, c.Value, c.CountryCode))
                              .ToList());

                // When scoped to one student (?studentId=), also carry each
                // guardian's relationship coded value id from the student↔guardian
                // LINK so the landing page can render "name (relationship)". A
                // relationship is not a guardian property, so it is only populated
                // (and only meaningful) when the list is filtered to a single
                // student. The unfiltered tenant-level list leaves it null.
                var relationshipByGuardianId = new Dictionary<Guid, Guid?>();
                if (studentId is { } relStudentId && guardianIds.Length > 0)
                {
                    var links = await db.StudentGuardians
                        .IgnoreQueryFilters(["Tenant"])
                        .Where(sg => sg.TenantId == tenantId
                                 && sg.StudentId == relStudentId
                                 && guardianIds.Contains(sg.GuardianId))
                        .Select(sg => new { sg.GuardianId, sg.RelationshipCodedValueId })
                        .ToListAsync(ct);
                    relationshipByGuardianId = links
                        .GroupBy(l => l.GuardianId)
                        .ToDictionary(grp => grp.Key, grp => grp.First().RelationshipCodedValueId);
                }

                return results.Select(g =>
                {
                    var list = contactsByOwner.TryGetValue(g.Id, out var l) ? l : null;
                    return new GuardianDto(
                        g.Id, g.TitleCodedValueId, g.FirstName, g.LastName, g.DisplayName, g.DateOfBirth, g.GenderCodedValueId, g.Address, g.CommunityId,
                        g.IsDeleted, g.CreatedAt, g.UpdatedAt)
                    {
                        Contacts = (IReadOnlyList<GuardianContactViewDto>?)list?.AsReadOnly() ?? System.Array.Empty<GuardianContactViewDto>(),
                        RelationshipCodedValueId = relationshipByGuardianId.TryGetValue(g.Id, out var relId) ? relId : null
                    };
                }).ToArray();
            },
            CacheOptions,
            tags: ["guardians"],
            cancellationToken: cancellationToken);
    }
}
