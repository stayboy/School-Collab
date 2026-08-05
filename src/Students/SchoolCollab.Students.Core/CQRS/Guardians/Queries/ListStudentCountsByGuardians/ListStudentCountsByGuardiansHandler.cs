using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Caching;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Guardians.Queries.ListStudentCountsByGuardians;

public sealed class ListStudentCountsByGuardiansHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListStudentCountsByGuardians, StudentCountDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<StudentCountDto[]> HandleAsync(
        ListStudentCountsByGuardians query,
        CancellationToken cancellationToken = default)
    {
        if (query.GuardianIds.Length == 0) return [];

        var distinctSorted = query.GuardianIds.Distinct().OrderBy(id => id).ToArray();

        // Capture the tenant in the request scope: db.CurrentTenantId is lost
        // inside the HybridCache factory, so the global "Tenant" filter would
        // resolve to Guid.Empty and hide every row. Scope the query explicitly.
        var tenantId = db.CurrentTenantId;

        var cacheKey = $"guardians:{CacheKeyHelper.Hash(string.Join(",", distinctSorted))}:student-counts";

        return await cache.GetOrCreateAsync(
            cacheKey,
            (db, distinctSorted, tenantId),
            static async (state, ct) =>
            {
                var (db, guardianIds, tenantId) = state;

                // Count only NON-deleted students linked to each guardian, so
                // the count matches what ListStudentsForGuardian would return
                // (a soft-deleted student is hidden from the students list).
                var rows = await (
                    from sg in db.StudentGuardians.IgnoreQueryFilters(["Tenant"])
                    join s in db.Students.IgnoreQueryFilters(["Tenant"]) on sg.StudentId equals s.Id
                    where sg.TenantId == tenantId
                          && s.TenantId == tenantId
                          && guardianIds.Contains(sg.GuardianId)
                          && !s.IsDeleted
                    group sg by sg.GuardianId into grp
                    select new { GuardianId = grp.Key, Count = grp.Count() }).ToArrayAsync(ct);

                return rows.Select(r => new StudentCountDto(r.GuardianId, r.Count)).ToArray();
            },
            CacheOptions,
            tags: ["guardians"],
            cancellationToken: cancellationToken);
    }
}
