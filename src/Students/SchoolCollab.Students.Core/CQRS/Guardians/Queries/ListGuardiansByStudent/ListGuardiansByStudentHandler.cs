using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Guardians.Queries.ListGuardiansByStudent;

public sealed class ListGuardiansByStudentHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListGuardiansByStudent, StudentGuardianViewDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<StudentGuardianViewDto[]> HandleAsync(ListGuardiansByStudent query, CancellationToken cancellationToken = default)
    {
        var tenantId = db.CurrentTenantId;
        var filters = new[] { "Tenant" };

        return await cache.GetOrCreateAsync(
            $"guardians:by-student:{tenantId}:{query.StudentId}",
            (db, tenantId, query.StudentId, filters),
            static async (state, ct) =>
            {
                var (db, tenantId, studentId, filters) = state;
                var rows = await (
                    from l in db.StudentGuardians.IgnoreQueryFilters(filters)
                    join g in db.Guardians.IgnoreQueryFilters(filters) on l.GuardianId equals g.Id
                    where l.TenantId == tenantId && g.TenantId == tenantId
                          && l.StudentId == studentId && !g.IsDeleted
                    orderby g.LastName, g.FirstName
                    select new
                    {
                        l.GuardianId, l.StudentId, l.Role, l.RelationshipCodedValueId,
                        l.IsEmergencyContact, g.FirstName, g.LastName, g.DisplayName
                    }).ToArrayAsync(ct);

                // Batch-load each linked guardian's primary (or first
                // non-deleted) contact so the per-student list can show how
                // to reach each guardian without N+1 queries. A guardian may
                // own multiple contacts (spec §4.4); we surface the IsPrimary
                // one, falling back to the first contact when none is primary.
                var guardianIds = rows.Select(r => r.GuardianId).Distinct().ToArray();
                var contacts = guardianIds.Length == 0
                    ? new List<Contact>()
                    : await db.Contacts
                        .IgnoreQueryFilters(filters)
                        .Where(c => c.TenantId == tenantId
                                 && c.OwnerType == ContactOwnerType.Guardian
                                 && guardianIds.Contains(c.OwnerId)
                                 && !c.IsDeleted)
                        .ToListAsync(ct);
                var contactsByOwner = contacts
                    .GroupBy(c => c.OwnerId)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.IsPrimary).First());

                return rows.Select(r =>
                {
                    var c = contactsByOwner.TryGetValue(r.GuardianId, out var pc) ? pc : null;
                    return new StudentGuardianViewDto(
                        r.GuardianId, r.StudentId, r.Role, r.RelationshipCodedValueId,
                        r.IsEmergencyContact, r.FirstName, r.LastName, r.DisplayName,
                        c?.Channel, c?.Value, c?.CountryCode);
                }).ToArray();
            },
            CacheOptions,
            tags: ["guardians"],
            cancellationToken: cancellationToken);
    }
}
