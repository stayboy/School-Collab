using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Enrollments.Queries.ListEnrollmentsByStudent;

public sealed class ListEnrollmentsByStudentHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListEnrollmentsByStudent, StudentEnrollmentDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<StudentEnrollmentDto[]> HandleAsync(
        ListEnrollmentsByStudent query,
        CancellationToken cancellationToken = default)
    {
        // Capture the tenant in the request scope: db.CurrentTenantId is lost
        // inside the HybridCache factory, so the global "Tenant" filter would
        // resolve to Guid.Empty and hide every row. Scope the query explicitly.
        var tenantId = db.CurrentTenantId;

        return await cache.GetOrCreateAsync(
            $"student:{query.StudentId}:enrollments",
            (db, query.StudentId, tenantId),
            static async (state, ct) =>
            {
                var (db, studentId, tenantId) = state;
                var results = await db.StudentEnrollments
                    .IgnoreQueryFilters(["Tenant"])
                    .Where(x => x.StudentId == studentId && x.TenantId == tenantId)
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
