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
                        l.IsEmergencyContact, g.FirstName, g.LastName, g.DisplayName, g.TitleCodedValueId
                    }).ToArrayAsync(ct);

                // Batch-load each linked guardian's top contacts (up to
                // three) so the per-student list can show how to reach each
                // guardian without N+1 queries. Ordered by DisplayOrder.
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
                    .ToDictionary(
                        g => g.Key,
                        g => new
                        {
                            // Top 3 by display order for the inline C1/C2/C3 cells.
                            Top = g.OrderBy(c => c.DisplayOrder)
                                   .Take(3)
                                   .Select(c => new GuardianContactViewDto(c.Channel, c.Value, c.CountryCode))
                                   .ToList(),
                            // All non-deleted contacts for this guardian (NOT capped
                            // at 3). Drives the "View all (N) contacts" anchor in the
                            // student-view grid (shown only when Total > 3). Computed
                            // from the already-materialized group, so no extra query.
                            Total = g.Count(),
                        });

                return rows.Select(r =>
                {
                    var entry = contactsByOwner.TryGetValue(r.GuardianId, out var e) ? e : null;
                    var list = entry?.Top;
                    return new StudentGuardianViewDto(
                        r.GuardianId, r.StudentId, r.Role, r.RelationshipCodedValueId,
                        r.IsEmergencyContact, r.FirstName, r.LastName, r.DisplayName, r.TitleCodedValueId)
                    {
                        Contacts = (IReadOnlyList<GuardianContactViewDto>?)list?.AsReadOnly()
                                   ?? System.Array.Empty<GuardianContactViewDto>(),
                        TotalContactCount = entry?.Total ?? 0,
                    };
                }).ToArray();
            },
            CacheOptions,
            tags: ["guardians"],
            cancellationToken: cancellationToken);
    }
}
