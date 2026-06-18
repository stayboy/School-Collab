using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Students.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Queries.ListStudentSubjectAssignmentsByPeriod;

public sealed class ListStudentSubjectAssignmentsByPeriodHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListStudentSubjectAssignmentsByPeriod, StudentSubjectAssignmentDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<StudentSubjectAssignmentDto[]> HandleAsync(
        ListStudentSubjectAssignmentsByPeriod query,
        CancellationToken cancellationToken = default)
    {
        return await cache.GetOrCreateAsync(
            $"period:{query.PeriodId}:student-subject-assignments",
            (db, query.PeriodId),
            static async (state, ct) =>
            {
                var (db, periodId) = state;
                var results = await db.StudentSubjectAssignments
                    .AsNoTracking()
                    .Where(x => x.PeriodId == periodId)
                    .OrderBy(x => x.StudentId)
                    .ThenBy(x => x.SubjectId)
                    .ToArrayAsync(ct);

                return results.Select(a => new StudentSubjectAssignmentDto(
                    a.Id,
                    a.StudentId,
                    a.SubjectId,
                    a.PeriodId,
                    a.IsOverride,
                    a.SourceType.ToString(),
                    a.CreatedAt,
                    a.UpdatedAt)).ToArray();
            },
            CacheOptions,
            tags: ["students"],
            cancellationToken: cancellationToken);
    }
}