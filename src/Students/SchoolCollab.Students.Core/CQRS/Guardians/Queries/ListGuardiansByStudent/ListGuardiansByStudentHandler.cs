using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
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
                var results = await (
                    from l in db.StudentGuardians.IgnoreQueryFilters(filters)
                    join g in db.Guardians.IgnoreQueryFilters(filters) on l.GuardianId equals g.Id
                    where l.TenantId == tenantId && g.TenantId == tenantId
                          && l.StudentId == studentId && !g.IsDeleted
                    orderby g.LastName, g.FirstName
                    select new StudentGuardianViewDto(
                        l.GuardianId, l.StudentId, l.Role, l.RelationshipCodedValueId,
                        l.IsEmergencyContact, g.FirstName, g.LastName, g.DisplayName)).ToArrayAsync(ct);

                return results;
            },
            CacheOptions,
            tags: ["guardians"],
            cancellationToken: cancellationToken);
    }
}
