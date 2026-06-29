using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.GradeSubjectAssignments.Queries.ListGradeSubjectAssignmentsByGradeLevel;

public sealed class ListGradeSubjectAssignmentsByGradeLevelHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListGradeSubjectAssignmentsByGradeLevel, GradeSubjectAssignmentDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<GradeSubjectAssignmentDto[]> HandleAsync(
        ListGradeSubjectAssignmentsByGradeLevel query,
        CancellationToken cancellationToken = default)
    {
        return await cache.GetOrCreateAsync(
            $"grade-level:{query.GradeLevelId}:period:{query.PeriodId}:grade-subject-assignments",
            (db, query.GradeLevelId, query.PeriodId),
            static async (state, ct) =>
            {
                var (db, gradeLevelId, periodId) = state;
                var results = await db.GradeSubjectAssignments
                    .AsNoTracking()
                    .Where(x => x.GradeLevelId == gradeLevelId && x.PeriodId == periodId)
                    .OrderBy(x => x.SubjectId)
                    .ToArrayAsync(ct);

                return results.Select(a => new GradeSubjectAssignmentDto(
                    a.Id,
                    a.GradeLevelId,
                    a.SubjectId,
                    a.PeriodId,
                    a.SubjectStrandId,
                    a.SubjectLessonId,
                    a.CreatedAt,
                    a.UpdatedAt)).ToArray();
            },
            CacheOptions,
            tags: ["students"],
            cancellationToken: cancellationToken);
    }
}