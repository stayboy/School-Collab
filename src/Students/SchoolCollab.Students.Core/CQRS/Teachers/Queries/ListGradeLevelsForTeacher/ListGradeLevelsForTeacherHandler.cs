using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Queries.ListGradeLevelsForTeacher;

public sealed class ListGradeLevelsForTeacherHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListGradeLevelsForTeacher, GradeLevelDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<GradeLevelDto[]> HandleAsync(ListGradeLevelsForTeacher query, CancellationToken cancellationToken = default)
    {
        var tenantId = db.CurrentTenantId;

        return await cache.GetOrCreateAsync(
            $"teachers:{query.TeacherId}:grade-levels:{tenantId}",
            (db, query, tenantId),
            static async (state, ct) =>
            {
                var (db, query, tenantId) = state;
                var results = await (from tg in db.TeacherGradeLevels.IgnoreQueryFilters(["Tenant"])
                                     join gl in db.GradeLevels.IgnoreQueryFilters(["Tenant"]) on tg.GradeLevelId equals gl.Id
                                     where tg.TenantId == tenantId && tg.TeacherId == query.TeacherId && gl.TenantId == tenantId
                                     orderby gl.Level
                                     select new GradeLevelDto(
                                         gl.Id, gl.CodedValueId, gl.Level, gl.Name, gl.DisplayOrder,
                                         db.GradeSubjectAssignments.IgnoreQueryFilters(new[] { "Tenant" })
                                             .Count(gsa => gsa.GradeLevelId == gl.Id && gsa.TenantId == tenantId),
                                         db.StudentEnrollments.IgnoreQueryFilters(new[] { "Tenant" })
                                             .Count(se => se.GradeLevelId == gl.Id && se.TenantId == tenantId),
                                         gl.CreatedAt, gl.UpdatedAt))
                    .ToArrayAsync(ct);
                return results;
            },
            CacheOptions,
            tags: ["teachers"],
            cancellationToken: cancellationToken);
    }
}
