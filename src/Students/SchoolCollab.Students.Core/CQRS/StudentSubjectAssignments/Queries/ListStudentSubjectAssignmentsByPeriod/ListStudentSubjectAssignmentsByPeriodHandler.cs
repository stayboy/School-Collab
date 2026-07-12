using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.StudentSubjectAssignments.Queries.ListStudentSubjectAssignmentsByPeriod;

public sealed class ListStudentSubjectAssignmentsByPeriodHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListStudentSubjectAssignmentsByPeriod, StudentSubjectAssignmentDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<StudentSubjectAssignmentDto[]> HandleAsync(
        ListStudentSubjectAssignmentsByPeriod query,
        CancellationToken cancellationToken = default)
    {
        // Capture the tenant in the request scope: db.CurrentTenantId is lost
        // inside the HybridCache factory, so the global "Tenant" filter would
        // resolve to Guid.Empty and hide every row. Scope the query explicitly.
        var tenantId = db.CurrentTenantId;

        return await cache.GetOrCreateAsync(
            $"period:{query.PeriodId}:student-subject-assignments",
            (db, query.PeriodId, tenantId),
            static async (state, ct) =>
            {
                var (db, periodId, tenantId) = state;
                var results = await db.StudentSubjectAssignments
                    .IgnoreQueryFilters(["Tenant"])
                    .Where(x => x.PeriodId == periodId && x.TenantId == tenantId)
                    .OrderBy(x => x.StudentId)
                    .ThenBy(x => x.SubjectId)
                    .ToArrayAsync(ct);

                return results.Select(a => new StudentSubjectAssignmentDto(
                    a.Id,
                    a.StudentId,
                    a.SubjectId,
                    a.PeriodId,
                    a.IsOverride,
                    a.SourceType.ToString(),
                    a.CreatedAt,
                    a.UpdatedAt)).ToArray();
            },
            CacheOptions,
            tags: ["students"],
            cancellationToken: cancellationToken);
    }
}
