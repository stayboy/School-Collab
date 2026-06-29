using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.GradeSubjectAssignments.Queries.ListGradeSubjectAssignmentsByPeriod;

public sealed class ListGradeSubjectAssignmentsByPeriodHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListGradeSubjectAssignmentsByPeriod, GradeSubjectAssignmentDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<GradeSubjectAssignmentDto[]> HandleAsync(
        ListGradeSubjectAssignmentsByPeriod query,
        CancellationToken cancellationToken = default)
    {
        return await cache.GetOrCreateAsync(
            $"period:{query.PeriodId}:grade-subject-assignments",
            (db, query.PeriodId),
            static async (state, ct) =>
            {
                var (db, periodId) = state;
                var results = await db.GradeSubjectAssignments
                    .AsNoTracking()
                    .Where(x => x.PeriodId == periodId)
                    .OrderBy(x => x.GradeLevelId)
                    .ThenBy(x => x.SubjectId)
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