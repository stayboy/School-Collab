using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Queries.ListTeachersForGradeLevel;

/// <summary>
/// Inverse of <see cref="ListGradeLevelsForTeacherHandler"/>: teachers linked
/// to a grade level, each carrying their coded-value role on that grade and the
/// topics they teach (grade-level-detail-view-plan.md §3.1). Tenant-scoped and
/// cached under the "teachers" tag.
/// </summary>
public sealed class ListTeachersForGradeLevelHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListTeachersForGradeLevel, TeacherWithRoleDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<TeacherWithRoleDto[]> HandleAsync(ListTeachersForGradeLevel query, CancellationToken cancellationToken = default)
    {
        var tenantId = db.CurrentTenantId;

        return await cache.GetOrCreateAsync(
            $"grade-levels:{query.GradeLevelId}:teachers:{tenantId}",
            (db, query, tenantId),
            static async (state, ct) =>
            {
                var (db, query, tenantId) = state;

                // Teachers linked to the grade level. The Teachers DbSet has a
                // global SoftDelete query filter, so soft-deleted teachers are
                // excluded automatically; the join table (teacher_grade_levels)
                // is not soft-deletable, so only the Tenant filter must be
                // bypassed. Mirrors the ListGradeLevelsForTeacher inverse query.
                var teachers = await (
                    from tg in db.TeacherGradeLevels.IgnoreQueryFilters(["Tenant"])
                    join t in db.Teachers on tg.TeacherId equals t.Id
                    where tg.TenantId == tenantId
                          && tg.GradeLevelId == query.GradeLevelId
                          && t.TenantId == tenantId
                    orderby t.LastName, t.FirstName
                    select new TeacherWithRoleDto(
                        t.Id,
                        t.TitleCodedValueId,
                        t.FirstName,
                        t.LastName,
                        t.DisplayName,
                        t.GenderCodedValueId,
                        t.DateOfBirth,
                        t.LevelOfEducationCodedValueId,
                        (from q in db.TeacherQualifications.IgnoreQueryFilters(new[] { "Tenant" })
                         where q.TeacherId == t.Id && q.TenantId == tenantId
                         orderby q.CreatedAt
                         select q.CodedValueId).ToArray(),
                        t.IsDeleted,
                        tg.TeacherRoleCodedValueId,
                        // v4: subjects are grade-scoped (TeacherGradeLevel.TopicId); the
                        // standalone TeacherTopic link is removed.
                        Array.Empty<TopicDto>(),
                        t.CreatedAt,
                        t.UpdatedAt))
                    .ToArrayAsync(ct);

                return teachers;
            },
            CacheOptions,
            tags: ["teachers"],
            cancellationToken: cancellationToken);
    }
}