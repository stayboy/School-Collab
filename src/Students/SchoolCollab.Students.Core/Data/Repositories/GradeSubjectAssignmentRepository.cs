using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

internal sealed class GradeSubjectAssignmentRepository(StudentsDbContext db)
    : RepositoryBase<GradeSubjectAssignment, StudentsDbContext>(db), IGradeSubjectAssignmentRepository
{
    public async Task<GradeSubjectAssignmentDto[]> ListByPeriodAsync(Guid periodId, CancellationToken cancellationToken = default) =>
        await Db.GradeSubjectAssignments
            .AsNoTracking()
            .Where(x => x.PeriodId == periodId)
            .OrderBy(x => x.GradeLevelId).ThenBy(x => x.SubjectId)
            .Select(x => new GradeSubjectAssignmentDto(
                x.Id, x.GradeLevelId, x.SubjectId, x.PeriodId,
                x.SubjectStrandId, x.SubjectLessonId,
                x.CreatedAt, x.UpdatedAt))
            .ToArrayAsync(cancellationToken);

    public async Task<GradeSubjectAssignmentDto[]> ListByGradeLevelAsync(Guid gradeLevelId, Guid periodId, CancellationToken cancellationToken = default) =>
        await Db.GradeSubjectAssignments
            .AsNoTracking()
            .Where(x => x.GradeLevelId == gradeLevelId && x.PeriodId == periodId)
            .OrderBy(x => x.SubjectId)
            .Select(x => new GradeSubjectAssignmentDto(
                x.Id, x.GradeLevelId, x.SubjectId, x.PeriodId,
                x.SubjectStrandId, x.SubjectLessonId,
                x.CreatedAt, x.UpdatedAt))
            .ToArrayAsync(cancellationToken);
}
