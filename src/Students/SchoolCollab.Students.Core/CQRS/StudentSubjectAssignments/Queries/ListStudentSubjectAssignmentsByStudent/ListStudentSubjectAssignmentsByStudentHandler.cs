using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.StudentSubjectAssignments.Queries.ListStudentSubjectAssignmentsByStudent;

public sealed class ListStudentSubjectAssignmentsByStudentHandler(
    StudentsDbContext db,
    HybridCache cache) : IQueryHandler<ListStudentSubjectAssignmentsByStudent, StudentSubjectAssignmentDto[]>
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<StudentSubjectAssignmentDto[]> HandleAsync(
        ListStudentSubjectAssignmentsByStudent query,
        CancellationToken cancellationToken = default)
    {
        return await cache.GetOrCreateAsync(
            $"student:{query.StudentId}:period:{query.PeriodId}:student-subject-assignments",
            (db, query.StudentId, query.PeriodId),
            static async (state, ct) =>
            {
                var (db, studentId, periodId) = state;
                var results = await db.StudentSubjectAssignments
                    .AsNoTracking()
                    .Where(x => x.StudentId == studentId && x.PeriodId == periodId)
                    .OrderBy(x => x.SubjectId)
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