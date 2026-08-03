using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

internal sealed class StudentTopicAssignmentRepository(StudentsDbContext db)
    : RepositoryBase<StudentTopicAssignment, StudentsDbContext>(db), IStudentTopicAssignmentRepository
{
    public async Task<StudentTopicAssignmentDto[]> ListByStudentAsync(Guid studentId, Guid periodId, CancellationToken cancellationToken = default) =>
        await Db.StudentTopicAssignments
            .AsNoTracking()
            .Where(x => x.StudentId == studentId && x.PeriodId == periodId)
            .OrderBy(x => x.TopicId)
            .Select(x => new StudentTopicAssignmentDto(
                x.Id, x.StudentId, x.TopicId, x.PeriodId,
                x.IsOverride, x.SourceType.ToString(),
                x.CreatedAt, x.UpdatedAt))
            .ToArrayAsync(cancellationToken);

    public async Task<StudentTopicAssignmentDto[]> ListByPeriodAsync(Guid periodId, CancellationToken cancellationToken = default) =>
        await Db.StudentTopicAssignments
            .AsNoTracking()
            .Where(x => x.PeriodId == periodId)
            .OrderBy(x => x.StudentId).ThenBy(x => x.TopicId)
            .Select(x => new StudentTopicAssignmentDto(
                x.Id, x.StudentId, x.TopicId, x.PeriodId,
                x.IsOverride, x.SourceType.ToString(),
                x.CreatedAt, x.UpdatedAt))
            .ToArrayAsync(cancellationToken);
}
