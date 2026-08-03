using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.StudentTopicAssignments.Queries.ListStudentTopicAssignmentsByPeriod;

public sealed class ListStudentTopicAssignmentsByPeriodHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListStudentTopicAssignmentsByPeriod, StudentTopicAssignmentDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<StudentTopicAssignmentDto[]> HandleAsync(
        ListStudentTopicAssignmentsByPeriod query,
        CancellationToken cancellationToken = default)
    {
        // Capture the tenant in the request scope: db.CurrentTenantId is lost
        // inside the HybridCache factory, so the global "Tenant" filter would
        // resolve to Guid.Empty and hide every row. Scope the query explicitly.
        var tenantId = db.CurrentTenantId;

        return await cache.GetOrCreateAsync(
            $"period:{query.PeriodId}:student-topic-assignments",
            (db, query.PeriodId, tenantId),
            static async (state, ct) =>
            {
                var (db, periodId, tenantId) = state;
                var results = await db.StudentTopicAssignments
                    .IgnoreQueryFilters(["Tenant"])
                    .Where(x => x.PeriodId == periodId && x.TenantId == tenantId)
                    .OrderBy(x => x.StudentId)
                    .ThenBy(x => x.TopicId)
                    .ToArrayAsync(ct);

                return results.Select(a => new StudentTopicAssignmentDto(
                    a.Id,
                    a.StudentId,
                    a.TopicId,
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
