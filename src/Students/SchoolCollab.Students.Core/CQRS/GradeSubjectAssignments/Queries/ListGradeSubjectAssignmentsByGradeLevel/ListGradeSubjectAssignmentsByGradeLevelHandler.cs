using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.GradeSubjectAssignments.Queries.ListGradeSubjectAssignmentsByGradeLevel;

public sealed class ListGradeSubjectAssignmentsByGradeLevelHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListGradeSubjectAssignmentsByGradeLevel, GradeSubjectAssignmentDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<GradeSubjectAssignmentDto[]> HandleAsync(
        ListGradeSubjectAssignmentsByGradeLevel query,
        CancellationToken cancellationToken = default)
    {
        // Capture the tenant in the request scope: db.CurrentTenantId is lost
        // inside the HybridCache factory, so the global "Tenant" filter would
        // resolve to Guid.Empty and hide every row. Scope the query explicitly.
        var tenantId = db.CurrentTenantId;

        return await cache.GetOrCreateAsync(
            $"grade-level:{query.GradeLevelId}:effective:{query.EffectiveDate:yyyyMMdd}:grade-subject-assignments",
            (db, query.GradeLevelId, query.EffectiveDate, tenantId),
            static async (state, ct) =>
            {
                var (db, gradeLevelId, effectiveDate, tenantId) = state;
                var results = await db.GradeSubjectAssignments
                    .IgnoreQueryFilters(["Tenant"])
                    .Where(x => x.GradeLevelId == gradeLevelId && x.TenantId == tenantId
                        && x.StartDate <= effectiveDate
                        && (x.EndDate == null || x.EndDate >= effectiveDate))
                    .OrderBy(x => x.TopicId)
                    .ToArrayAsync(ct);

                return results.Select(a => new GradeSubjectAssignmentDto(
                    a.Id,
                    a.GradeLevelId,
                    a.ActivityGroupId,
                    a.TopicId,
                    a.StartDate,
                    a.EndDate,
                    a.TopicStrandId,
                    a.TopicLessonId,
                    a.CreatedAt,
                    a.UpdatedAt)).ToArray();
            },
            CacheOptions,
            tags: ["students"],
            cancellationToken: cancellationToken);
    }
}
