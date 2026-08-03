using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.GradeLevels.Queries.ListGradeLevels;

public sealed class ListGradeLevelsHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListGradeLevels, GradeLevelDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<GradeLevelDto[]> HandleAsync(
        ListGradeLevels query,
        CancellationToken cancellationToken = default)
    {
        // Capture the tenant in the request scope: db.CurrentTenantId is lost
        // inside the HybridCache factory, so the global "Tenant" filter would
        // hide every row (and zero the correlated counts). Scope the query
        // explicitly instead.
        var tenantId = db.CurrentTenantId;

        return await cache.GetOrCreateAsync(
            $"grade-levels:list:{tenantId}",
            (db, tenantId),
            static async (state, ct) =>
            {
                var (db, tenantId) = state;
                var results = await db.GradeLevels
                    .IgnoreQueryFilters(["Tenant"])
                    .Where(gl => gl.TenantId == tenantId)
                    .AsNoTracking()
                    .OrderBy(x => x.Level)
                    .Select(gl => new
                    {
                        gl.Id,
                        gl.CodedValueId,
                        gl.Level,
                        gl.Name,
                        gl.DisplayOrder,
                        gl.MinAge,
                        gl.MaxAge,
                        gl.AllowedGenderCodedValueId,
                        gl.CreatedAt,
                        gl.UpdatedAt,
                        TopicCount = db.GradeTopicAssignments
                            .IgnoreQueryFilters(new[] { "Tenant" })
                            .Count(ga => ga.GradeLevelId == gl.Id && ga.TenantId == tenantId),
                        StudentCount = db.StudentEnrollments
                            .IgnoreQueryFilters(new[] { "Tenant" })
                            .Count(se => se.GradeLevelId == gl.Id && se.TenantId == tenantId)
                    })
                    .ToArrayAsync(ct);

                return results.Select(gl => new GradeLevelDto(
                    gl.Id,
                    gl.CodedValueId,
                    gl.Level,
                    gl.Name,
                    gl.DisplayOrder,
                    gl.TopicCount,
                    gl.StudentCount,
                    gl.CreatedAt,
                    gl.UpdatedAt,
                    gl.MinAge,
                    gl.MaxAge,
                    gl.AllowedGenderCodedValueId)).ToArray();
            },
            CacheOptions,
            tags: ["students"],
            cancellationToken: cancellationToken);
    }
}
