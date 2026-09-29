using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.TopicAssignments.Queries.ListActivityGroupTopicAssignments;

public sealed class ListActivityGroupTopicAssignmentsHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListActivityGroupTopicAssignments, TopicAssignmentDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<TopicAssignmentDto[]> HandleAsync(
        ListActivityGroupTopicAssignments query,
        CancellationToken cancellationToken = default)
    {
        // Tenant captured in the request scope (lost inside the cache factory).
        var tenantId = db.CurrentTenantId;

        // Date-only key — the exception predicate depends on the date alone
        // (subject-period-exception-model.md v3 §2.3), so no active-period segment.
        return await cache.GetOrCreateAsync(
            $"activity-group:{query.ActivityGroupId}:effective:{query.EffectiveDate:yyyyMMdd}:topic-assignments",
            (db, query.ActivityGroupId, query.EffectiveDate, tenantId),
            static async (state, ct) =>
            {
                var (db, activityGroupId, effectiveDate, tenantId) = state;

                // subject-period-exception-model.md §2.3 — same date test as the grade path.
                var exceptedTopicIds = await SubjectAvailability.ExceptedTopicIdsForGroupAsync(
                    db, tenantId, activityGroupId, effectiveDate, ct);

                var results = await db.ActivityGroupTopicAssignments
                    .IgnoreQueryFilters(["Tenant"])
                    .Where(x => x.ActivityGroupId == activityGroupId && x.TenantId == tenantId
                        && x.StartDate <= effectiveDate
                        && (x.EndDate == null || x.EndDate >= effectiveDate))
                    .OrderBy(x => x.TopicId)
                    .ToArrayAsync(ct);

                return results
                    .Where(a => !exceptedTopicIds.Contains(a.TopicId))
                    .Select(a => new TopicAssignmentDto(
                        a.Id,
                        "activity_group",
                        null,
                        a.ActivityGroupId,
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
