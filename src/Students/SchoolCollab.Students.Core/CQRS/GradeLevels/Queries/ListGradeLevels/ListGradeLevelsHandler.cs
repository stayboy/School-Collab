using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.GradeLevels.Queries.ListGradeLevels;

public sealed class ListGradeLevelsHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListGradeLevels, GradeLevelDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<GradeLevelDto[]> HandleAsync(
        ListGradeLevels query,
        CancellationToken cancellationToken = default)
    {
        return await cache.GetOrCreateAsync(
            "grade-levels:list",
            db,
            static async (db, ct) =>
            {
                var results = await db.GradeLevels
                    .AsNoTracking()
                    .OrderBy(x => x.Level)
                    .Select(gl => new
                    {
                        gl.Id,
                        gl.CodedValueId,
                        gl.Level,
                        gl.Name,
                        gl.DisplayOrder,
                        gl.CreatedAt,
                        gl.UpdatedAt,
                        SubjectCount = db.GradeSubjectAssignments.Count(ga => ga.GradeLevelId == gl.Id),
                        StudentCount = db.StudentEnrollments.Count(se => se.GradeLevelId == gl.Id)
                    })
                    .ToArrayAsync(ct);

                return results.Select(gl => new GradeLevelDto(
                    gl.Id,
                    gl.CodedValueId,
                    gl.Level,
                    gl.Name,
                    gl.DisplayOrder,
                    gl.SubjectCount,
                    gl.StudentCount,
                    gl.CreatedAt,
                    gl.UpdatedAt)).ToArray();
            },
            CacheOptions,
            tags: ["students"],
            cancellationToken: cancellationToken);
    }
}