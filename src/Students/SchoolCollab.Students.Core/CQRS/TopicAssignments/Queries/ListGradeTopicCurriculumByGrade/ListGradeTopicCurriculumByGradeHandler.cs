using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.TopicAssignments.Queries.ListGradeTopicCurriculumByGrade;

/// <summary>
/// Returns the grade's currently-assigned topics padded with their strand and
/// lesson counts (grade-detail-rich-grids-plan.md §4). Strands/lessons are
/// topic-scoped, so counts are the topic's totals. Tenant-scoped and cached
/// under the "students" tag.
/// </summary>
public sealed class ListGradeTopicCurriculumByGradeHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListGradeTopicCurriculumByGrade, GradeTopicCurriculumDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<GradeTopicCurriculumDto[]> HandleAsync(
        ListGradeTopicCurriculumByGrade query,
        CancellationToken cancellationToken = default)
    {
        // Capture the tenant in the request scope: db.CurrentTenantId is lost
        // inside the HybridCache factory, so the global "Tenant" filter would
        // resolve to Guid.Empty and hide every row.
        var tenantId = db.CurrentTenantId;

        return await cache.GetOrCreateAsync(
            $"grade-level:{query.GradeLevelId}:effective:{query.EffectiveDate:yyyyMMdd}:curriculum",
            (db, query.GradeLevelId, query.EffectiveDate, tenantId),
            static async (state, ct) =>
            {
                var (db, gradeLevelId, effectiveDate, tenantId) = state;
                var tenantFilter = new[] { "Tenant" };

                var topics = await (
                    from a in db.GradeTopicAssignments.IgnoreQueryFilters(tenantFilter)
                    join t in db.Topics.IgnoreQueryFilters(tenantFilter) on a.TopicId equals t.Id
                    where a.GradeLevelId == gradeLevelId
                          && a.TenantId == tenantId
                          && t.TenantId == tenantId
                          && a.StartDate <= effectiveDate
                          && (a.EndDate == null || a.EndDate >= effectiveDate)
                    orderby t.DisplayOrder, t.Name
                    select new { t.Id, t.Name, t.Code }).ToArrayAsync(ct);

                if (topics.Length == 0) return Array.Empty<GradeTopicCurriculumDto>();

                var topicIds = topics.Select(x => x.Id).ToArray();

                var strandCounts = await db.TopicStrands.IgnoreQueryFilters(tenantFilter)
                    .Where(s => s.TenantId == tenantId && topicIds.Contains(s.TopicId))
                    .GroupBy(s => s.TopicId)
                    .Select(g => new { TopicId = g.Key, Count = g.Count() })
                    .ToArrayAsync(ct);

                var lessonCounts = await db.TopicLessons.IgnoreQueryFilters(tenantFilter)
                    .Where(l => l.TenantId == tenantId && topicIds.Contains(l.TopicId))
                    .GroupBy(l => l.TopicId)
                    .Select(g => new { TopicId = g.Key, Count = g.Count() })
                    .ToArrayAsync(ct);

                var strandMap = strandCounts.ToDictionary(x => x.TopicId, x => x.Count);
                var lessonMap = lessonCounts.ToDictionary(x => x.TopicId, x => x.Count);

                return topics.Select(x => new GradeTopicCurriculumDto(
                    x.Id,
                    x.Name,
                    x.Code,
                    strandMap.TryGetValue(x.Id, out var sc) ? sc : 0,
                    lessonMap.TryGetValue(x.Id, out var lc) ? lc : 0)).ToArray();
            },
            CacheOptions,
            tags: ["students"],
            cancellationToken: cancellationToken);
    }
}
