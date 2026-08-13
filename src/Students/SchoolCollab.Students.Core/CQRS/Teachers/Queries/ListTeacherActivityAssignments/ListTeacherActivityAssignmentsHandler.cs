using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Queries.ListTeacherActivityAssignments;

/// <summary>
/// Teacher↔activity assignments for a teacher (v4 spec §3.5). Tenant-scoped and
/// cached under the "teachers" tag.
/// </summary>
public sealed class ListTeacherActivityAssignmentsHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListTeacherActivityAssignments, TeacherActivityAssignmentDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<TeacherActivityAssignmentDto[]> HandleAsync(ListTeacherActivityAssignments query, CancellationToken cancellationToken = default)
    {
        var tenantId = db.CurrentTenantId;

        return await cache.GetOrCreateAsync(
            $"teachers:{query.TeacherId}:activity-assignments:{tenantId}",
            (db, query, tenantId),
            static async (state, ct) =>
            {
                var (db, query, tenantId) = state;

                var assignments = await db.TeacherActivityAssignments
                    .IgnoreQueryFilters(["Tenant"])
                    .Include(a => a.Grades)
                    .Where(a => a.TenantId == tenantId && a.TeacherId == query.TeacherId)
                    .OrderBy(a => a.ActivityGroupId)
                    .ToArrayAsync(ct);

                var activityIds = assignments.Select(a => a.ActivityGroupId).ToArray();
                var names = await db.ActivityGroups.IgnoreQueryFilters(["Tenant"])
                    .Where(ag => ag.TenantId == tenantId && activityIds.Contains(ag.Id))
                    .ToDictionaryAsync(ag => ag.Id, ag => ag.Name, ct);

                return assignments
                    .Select(a => new TeacherActivityAssignmentDto(
                        a.Id,
                        a.ActivityGroupId,
                        names.TryGetValue(a.ActivityGroupId, out var name) ? name : string.Empty,
                        a.RoleCodedValueId,
                        a.Grades.Select(g => g.GradeLevelId).ToArray()))
                    .ToArray();
            },
            CacheOptions,
            tags: ["teachers"],
            cancellationToken: cancellationToken);
    }
}
