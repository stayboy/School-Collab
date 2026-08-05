using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Caching;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Guardians.Queries.ListGuardianCountsByStudents;

public sealed class ListGuardianCountsByStudentsHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListGuardianCountsByStudents, GuardianCountDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<GuardianCountDto[]> HandleAsync(
        ListGuardianCountsByStudents query,
        CancellationToken cancellationToken = default)
    {
        if (query.StudentIds.Length == 0) return [];

        var distinctSorted = query.StudentIds.Distinct().OrderBy(id => id).ToArray();

        // Capture the tenant in the request scope: db.CurrentTenantId is lost
        // inside the HybridCache factory, so the global "Tenant" filter would
        // resolve to Guid.Empty and hide every row. Scope the query explicitly.
        var tenantId = db.CurrentTenantId;

        var cacheKey = $"students:{CacheKeyHelper.Hash(string.Join(",", distinctSorted))}:guardian-counts";

        return await cache.GetOrCreateAsync(
            cacheKey,
            (db, distinctSorted, tenantId),
            static async (state, ct) =>
            {
                var (db, studentIds, tenantId) = state;

                // Count only NON-deleted guardians linked to each student, so
                // the count matches what ListGuardiansByStudent would return
                // (a soft-deleted guardian is hidden from the guardians list).
                var rows = await (
                    from sg in db.StudentGuardians.IgnoreQueryFilters(["Tenant"])
                    join g in db.Guardians.IgnoreQueryFilters(["Tenant"]) on sg.GuardianId equals g.Id
                    where sg.TenantId == tenantId
                          && g.TenantId == tenantId
                          && studentIds.Contains(sg.StudentId)
                          && !g.IsDeleted
                    group sg by sg.StudentId into grp
                    select new { StudentId = grp.Key, Count = grp.Count() }).ToArrayAsync(ct);

                return rows.Select(r => new GuardianCountDto(r.StudentId, r.Count)).ToArray();
            },
            CacheOptions,
            tags: ["guardians"],
            cancellationToken: cancellationToken);
    }
}
