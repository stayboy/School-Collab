using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Students.Queries.ListStudents;

public sealed class ListStudentsHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListStudents, StudentDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<StudentDto[]> HandleAsync(
        ListStudents query,
        CancellationToken cancellationToken = default)
    {
        // Capture the tenant in the request scope: db.CurrentTenantId is lost
        // inside the HybridCache factory, so the global "Tenant" filter would
        // hide every row. Scope the query explicitly instead.
        var tenantId = db.CurrentTenantId;
        var search = (query.Search ?? string.Empty).Trim();

        return await cache.GetOrCreateAsync(
            $"students:list:{tenantId}:{search}",
            (db, tenantId, search),
            static async (state, ct) =>
            {
                var (db, tenantId, search) = state;
                var q = db.Students
                    .IgnoreQueryFilters(["Tenant"])
                    .Where(s => s.TenantId == tenantId);
                if (search.Length > 0)
                {
                    q = q.Where(s => s.StudentNumber.Contains(search)
                                 || s.FirstName.Contains(search)
                                 || s.LastName.Contains(search));
                }
                var results = await q.AsNoTracking()
                    .OrderBy(x => x.LastName)
                    .ThenBy(x => x.FirstName)
                    .ToArrayAsync(ct);

                return results.Select(s => new StudentDto(
                    s.Id,
                    s.StudentNumber,
                    s.TitleCodedValueId,
                    s.FirstName,
                    s.LastName,
                    s.DateOfBirth,
                    s.GenderCodedValueId,
                    s.IsDeleted,
                    s.CreatedAt,
                    s.UpdatedAt,
                    RowVersion: s.RowVersion)).ToArray();
            },
            CacheOptions,
            tags: ["students"],
            cancellationToken: cancellationToken);
    }
}
