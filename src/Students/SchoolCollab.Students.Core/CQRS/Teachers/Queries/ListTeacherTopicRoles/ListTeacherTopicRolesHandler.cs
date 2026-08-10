using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Queries.ListTeacherTopicRoles;

/// <summary>
/// Per-topic roles for a teacher (grade-detail-rich-grids-plan.md §5 / cg/6).
/// Tenant-scoped and cached under the "teachers" tag. Soft-deleted teachers are
/// excluded by the global SoftDelete query filter on <c>Teachers</c>.
/// </summary>
public sealed class ListTeacherTopicRolesHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListTeacherTopicRoles, TeacherTopicRoleDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<TeacherTopicRoleDto[]> HandleAsync(ListTeacherTopicRoles query, CancellationToken cancellationToken = default)
    {
        var tenantId = db.CurrentTenantId;

        return await cache.GetOrCreateAsync(
            $"teachers:{query.TeacherId}:topic-roles:{tenantId}",
            (db, query, tenantId),
            static async (state, ct) =>
            {
                var (db, query, tenantId) = state;
                var roles = await (from ts in db.TeacherTopics.IgnoreQueryFilters(["Tenant"])
                                   where ts.TenantId == tenantId && ts.TeacherId == query.TeacherId
                                   select new TeacherTopicRoleDto(
                                       ts.TopicId,
                                       ts.RoleCodedValueId,
                                       ts.StartDate,
                                       ts.EndDate))
                    .ToArrayAsync(ct);
                return roles;
            },
            CacheOptions,
            tags: ["teachers"],
            cancellationToken: cancellationToken);
    }
}
