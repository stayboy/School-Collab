using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.TopicAssignments.Queries.ListGradeTopicAssignments;

public sealed class ListGradeTopicAssignmentsHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListGradeTopicAssignments, TopicAssignmentDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<TopicAssignmentDto[]> HandleAsync(
        ListGradeTopicAssignments query,
        CancellationToken cancellationToken = default)
    {
        // Capture the tenant in the request scope: db.CurrentTenantId is lost
        // inside the HybridCache factory, so the global "Tenant" filter would
        // resolve to Guid.Empty and hide every row. Scope the query explicitly.
        var tenantId = db.CurrentTenantId;

        // Date-only key: availability is a single date test
        // (bridge effective on the date AND no exception containing the date), so
        // there is no active-period segment to key by any more — the failure mode the
        // v1 two-id set guarded against is structurally gone (v3 §0 decision 9).
        return await cache.GetOrCreateAsync(
            $"grade-level:{query.GradeLevelId}:effective:{query.EffectiveDate:yyyyMMdd}:grade-topic-assignments",
            (db, query.GradeLevelId, query.EffectiveDate, tenantId),
            static async (state, ct) =>
            {
                var (db, gradeLevelId, effectiveDate, tenantId) = state;

                // subject-period-exception-model.md §2.3: a bridge row means the
                // subject is offered; an exception whose span contains the date is the
                // exception. FR-58 (Assignments) rides this array unchanged — the lookup
                // client reduces with Any(d => d.TopicId == topicId).
                var exceptedTopicIds = await SubjectAvailability.ExceptedTopicIdsForGradeAsync(
                    db, tenantId, gradeLevelId, effectiveDate, ct);

                var results = await db.GradeTopicAssignments
                    .IgnoreQueryFilters(["Tenant"])
                    .Where(x => x.GradeLevelId == gradeLevelId && x.TenantId == tenantId
                        && x.StartDate <= effectiveDate
                        && (x.EndDate == null || x.EndDate >= effectiveDate))
                    .OrderBy(x => x.TopicId)
                    .ToArrayAsync(ct);

                return results
                    .Where(a => !exceptedTopicIds.Contains(a.TopicId))
                    .Select(a => new TopicAssignmentDto(
                        a.Id,
                        "grade",
                        a.GradeLevelId,
                        null,
                        a.TopicId,
                        a.StartDate,
                        a.EndDate,
                        a.TopicStrandId,
                        a.PeriodId,
                        a.CreatedAt,
                        a.UpdatedAt)).ToArray();
            },
            CacheOptions,
            tags: ["students"],
            cancellationToken: cancellationToken);
    }
}
