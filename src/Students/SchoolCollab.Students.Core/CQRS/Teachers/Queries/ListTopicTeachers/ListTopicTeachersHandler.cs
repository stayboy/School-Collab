using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Queries.ListTopicTeachers;

/// <summary>
/// Inverse of <see cref="ListTopicsForTeacher"/>: teachers linked to a topic, each
/// carrying their coded-value role on that topic (grade-detail-rich-grids-plan.md §5).
/// Tenant-scoped and cached under the "teachers" tag. Soft-deleted teachers are
/// excluded by the global SoftDelete query filter on <c>Teachers</c>.
/// </summary>
public sealed class ListTopicTeachersHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListTopicTeachers, TopicTeacherDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<TopicTeacherDto[]> HandleAsync(ListTopicTeachers query, CancellationToken cancellationToken = default)
    {
        var tenantId = db.CurrentTenantId;

        return await cache.GetOrCreateAsync(
            $"topics:{query.TopicId}:teachers:{tenantId}",
            (db, query, tenantId),
            static async (state, ct) =>
            {
                var (db, query, tenantId) = state;

                var teachers = await (
                    from tt in db.TeacherTopics.IgnoreQueryFilters(["Tenant"])
                    join t in db.Teachers on tt.TeacherId equals t.Id
                    where tt.TenantId == tenantId
                          && tt.TopicId == query.TopicId
                          && t.TenantId == tenantId
                    orderby t.LastName, t.FirstName
                    select new TopicTeacherDto(
                        t.Id,
                        t.TitleCodedValueId,
                        t.FirstName,
                        t.LastName,
                        t.DisplayName,
                        tt.RoleCodedValueId))
                    .ToArrayAsync(ct);

                return teachers;
            },
            CacheOptions,
            tags: ["teachers"],
            cancellationToken: cancellationToken);
    }
}
