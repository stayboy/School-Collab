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
///
/// <para>Ordered by the <b>bridge row's</b> <c>DisplayOrder</c> (the ordering
/// authority for the grade's subject list); <c>Topic.DisplayOrder, Topic.Name</c>
/// remain as the tiebreak for any duplicate the migration backfill left behind.
/// Every row also carries its bridge <c>AssignmentId</c>, which is what the
/// reorder endpoint targets.</para>
///
/// <para><b>Exceptions do NOT filter this list</b> (subject-period-exception-model.md
/// v3 §5.2 rows 3-4 / AC-9). This is the grade detail's <i>management</i> read: the
/// Subjects card and the View-all dialog must list an excepted subject so they can
/// render its <c>n exceptions</c> badge and the kebab that navigates to the
/// exceptions page — an administrator must see a subject to manage its exceptions.
/// The availability filter belongs to the two FR-58 publish/feed readers
/// (<c>ListGradeTopicAssignmentsHandler</c>, <c>ListActivityGroupTopicAssignmentsHandler</c>).
/// The bridge-effectiveness date test below is unrelated to exceptions and stays.</para>
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

        // Date-only key — the exception predicate depends on the date alone, so there
        // is no active-period segment (subject-period-exception-model.md v3 §0 decision 9).
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
                    orderby a.DisplayOrder, t.DisplayOrder, t.Name
                    select new { AssignmentId = a.Id, TopicId = t.Id, t.Name, t.Code }).ToArrayAsync(ct);

                if (topics.Length == 0) return Array.Empty<GradeTopicCurriculumDto>();

                // Deliberately no exception filter here — see the type doc comment.
                // An excepted topic stays in the management read (spec §5.2/AC-9).
                var topicIds = topics.Select(x => x.TopicId).ToArray();

                var strandCounts = await db.TopicStrands.IgnoreQueryFilters(tenantFilter)
                    .Where(s => s.TenantId == tenantId && topicIds.Contains(s.TopicId) && s.ParentStrandId == null)
                    .GroupBy(s => s.TopicId)
                    .Select(g => new { TopicId = g.Key, Count = g.Count() })
                    .ToArrayAsync(ct);

                var lessonCounts = await db.TopicStrands.IgnoreQueryFilters(tenantFilter)
                    .Where(s => s.TenantId == tenantId && topicIds.Contains(s.TopicId) && s.ParentStrandId != null)
                    .GroupBy(s => s.TopicId)
                    .Select(g => new { TopicId = g.Key, Count = g.Count() })
                    .ToArrayAsync(ct);

                var strandMap = strandCounts.ToDictionary(x => x.TopicId, x => x.Count);
                var lessonMap = lessonCounts.ToDictionary(x => x.TopicId, x => x.Count);

                return topics.Select(x => new GradeTopicCurriculumDto(
                    x.AssignmentId,
                    x.TopicId,
                    x.Name,
                    x.Code,
                    strandMap.TryGetValue(x.TopicId, out var sc) ? sc : 0,
                    lessonMap.TryGetValue(x.TopicId, out var lc) ? lc : 0)).ToArray();
            },
            CacheOptions,
            tags: ["students"],
            cancellationToken: cancellationToken);
    }
}
