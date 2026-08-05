using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Guardians.Queries.ListStudentsForGuardian;

public sealed class ListStudentsForGuardianHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListStudentsForGuardian, StudentDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<StudentDto[]> HandleAsync(ListStudentsForGuardian query, CancellationToken cancellationToken = default)
    {
        var tenantId = db.CurrentTenantId;
        var filters = new[] { "Tenant" };

        return await cache.GetOrCreateAsync(
            $"students:for-guardian:{tenantId}:{query.GuardianId}",
            (db, tenantId, query.GuardianId, filters),
            static async (state, ct) =>
            {
                var (db, tenantId, guardianId, filters) = state;
                var results = await (
                    from s in db.Students.IgnoreQueryFilters(filters)
                    join l in db.StudentGuardians.IgnoreQueryFilters(filters) on s.Id equals l.StudentId
                    where s.TenantId == tenantId && l.TenantId == tenantId && l.GuardianId == guardianId && !s.IsDeleted
                    orderby s.LastName, s.FirstName
                    select s).ToArrayAsync(ct);

                return results.Select(s => new StudentDto(
                    s.Id, s.StudentNumber, s.TitleCodedValueId, s.FirstName, s.LastName, s.DateOfBirth,
                    s.GenderCodedValueId, s.IsDeleted, s.CreatedAt, s.UpdatedAt)).ToArray();
            },
            CacheOptions,
            tags: ["guardians"],
            cancellationToken: cancellationToken);
    }
}
