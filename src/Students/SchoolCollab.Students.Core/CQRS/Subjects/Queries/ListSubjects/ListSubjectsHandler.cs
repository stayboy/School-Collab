using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Subjects.Queries.ListSubjects;

public sealed class ListSubjectsHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListSubjects, SubjectDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<SubjectDto[]> HandleAsync(
        ListSubjects query,
        CancellationToken cancellationToken = default)
    {
        // Capture the tenant in the request scope: db.CurrentTenantId is lost
        // inside the HybridCache factory, so the global "Tenant" filter would
        // hide every row. Scope the query explicitly instead.
        var tenantId = db.CurrentTenantId;

        return await cache.GetOrCreateAsync(
            $"subjects:list:{tenantId}",
            (db, tenantId),
            static async (state, ct) =>
            {
                var (db, tenantId) = state;
                var results = await db.Subjects
                    .IgnoreQueryFilters(["Tenant"])
                    .Where(s => s.TenantId == tenantId)
                    .AsNoTracking()
                    .OrderBy(x => x.DisplayOrder)
                    .ThenBy(x => x.Name)
                    .ToArrayAsync(ct);

                return results.Select(s => new SubjectDto(
                    s.Id,
                    s.CodedValueId,
                    s.Code,
                    s.Name,
                    s.DisplayOrder,
                    s.CreatedAt,
                    s.UpdatedAt)).ToArray();
            },
            CacheOptions,
            tags: ["students"],
            cancellationToken: cancellationToken);
    }
}
