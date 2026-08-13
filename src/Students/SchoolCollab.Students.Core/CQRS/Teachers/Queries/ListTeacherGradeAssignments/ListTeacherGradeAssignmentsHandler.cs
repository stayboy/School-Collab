using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Queries.ListTeacherGradeAssignments;

/// <summary>
/// Grade-scoped teaching assignments for a teacher (v4 spec §3.5). Tenant-scoped
/// and cached under the "teachers" tag.
/// </summary>
public sealed class ListTeacherGradeAssignmentsHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListTeacherGradeAssignments, TeacherGradeAssignmentDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<TeacherGradeAssignmentDto[]> HandleAsync(ListTeacherGradeAssignments query, CancellationToken cancellationToken = default)
    {
        var tenantId = db.CurrentTenantId;

        return await cache.GetOrCreateAsync(
            $"teachers:{query.TeacherId}:grade-assignments:{tenantId}",
            (db, query, tenantId),
            static async (state, ct) =>
            {
                var (db, query, tenantId) = state;
                var rows = await (
                    from tg in db.TeacherGradeLevels.IgnoreQueryFilters(["Tenant"])
                    join gl in db.GradeLevels.IgnoreQueryFilters(["Tenant"]) on tg.GradeLevelId equals gl.Id
                    join t in db.Topics.IgnoreQueryFilters(["Tenant"]) on tg.TopicId equals (Guid?)t.Id into subjects
                    from s in subjects.DefaultIfEmpty()
                    where tg.TenantId == tenantId && tg.TeacherId == query.TeacherId && gl.TenantId == tenantId
                    orderby gl.Level, s.DisplayOrder, s.Name
                    select new TeacherGradeAssignmentDto(
                        tg.Id,
                        gl.Id,
                        gl.Name,
                        gl.Level,
                        tg.TopicId,
                        s != null ? s.Name : null,
                        s != null ? s.Code : null,
                        tg.TeacherRoleCodedValueId))
                    .ToArrayAsync(ct);
                return rows;
            },
            CacheOptions,
            tags: ["teachers"],
            cancellationToken: cancellationToken);
    }
}
