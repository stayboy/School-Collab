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
        var tenantId = db.CurrentTenantId;

        return await cache.GetOrCreateAsync(
            $"activity-group:{query.ActivityGroupId}:effective:{query.EffectiveDate:yyyyMMdd}:topic-assignments",
            (db, query.ActivityGroupId, query.EffectiveDate, tenantId),
            static async (state, ct) =>
            {
                var (db, activityGroupId, effectiveDate, tenantId) = state;
                var results = await db.ActivityGroupTopicAssignments
                    .IgnoreQueryFilters(["Tenant"])
                    .Where(x => x.ActivityGroupId == activityGroupId && x.TenantId == tenantId
                        && x.StartDate <= effectiveDate
                        && (x.EndDate == null || x.EndDate >= effectiveDate))
                    .OrderBy(x => x.TopicId)
                    .ToArrayAsync(ct);

                return results.Select(a => new TopicAssignmentDto(
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
