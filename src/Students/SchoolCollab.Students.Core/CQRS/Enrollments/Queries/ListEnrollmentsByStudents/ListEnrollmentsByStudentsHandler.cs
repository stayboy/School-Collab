using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Caching;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Enrollments.Queries.ListEnrollmentsByStudents;

public sealed class ListEnrollmentsByStudentsHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListEnrollmentsByStudents, StudentEnrollmentDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<StudentEnrollmentDto[]> HandleAsync(
        ListEnrollmentsByStudents query,
        CancellationToken cancellationToken = default)
    {
        if (query.StudentIds.Length == 0) return [];

        var distinctSorted = query.StudentIds.Distinct().OrderBy(id => id).ToArray();

        // Capture the tenant in the request scope: db.CurrentTenantId is lost
        // inside the HybridCache factory, so the global "Tenant" filter would
        // resolve to Guid.Empty and hide every row. Scope the query explicitly.
        var tenantId = db.CurrentTenantId;

        var cacheKey = $"students:{CacheKeyHelper.Hash(string.Join(",", distinctSorted))}:enrollments";

        return await cache.GetOrCreateAsync(
            cacheKey,
            (db, distinctSorted, tenantId),
            static async (state, ct) =>
            {
                var (db, studentIds, tenantId) = state;
                var results = await db.StudentEnrollments
                    .IgnoreQueryFilters(["Tenant"])
                    .Where(x => studentIds.Contains(x.StudentId) && x.TenantId == tenantId)
                    .OrderByDescending(x => x.EnrolledOn)
                    .ToArrayAsync(ct);

                return results.Select(e => new StudentEnrollmentDto(
                    e.Id,
                    e.StudentId,
                    e.PeriodId,
                    e.GradeLevelId,
                    e.GradeStrandCodedValueId,
                    e.EnrolledOn,
                    e.ExitDate,
                    e.Status.ToString(),
                    e.CreatedAt,
                    e.UpdatedAt)).ToArray();
            },
            CacheOptions,
            tags: ["students"],
            cancellationToken: cancellationToken);
    }
}
