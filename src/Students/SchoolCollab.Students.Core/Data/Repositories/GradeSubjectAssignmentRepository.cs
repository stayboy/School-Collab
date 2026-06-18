using Microsoft.EntityFrameworkCore;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

internal sealed class GradeSubjectAssignmentRepository(StudentsDbContext db) : IGradeSubjectAssignmentRepository
{
    public Task<GradeSubjectAssignment?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.GradeSubjectAssignments.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task AddAsync(GradeSubjectAssignment assignment, CancellationToken cancellationToken = default)
    {
        await db.GradeSubjectAssignments.AddAsync(assignment, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(GradeSubjectAssignment assignment, CancellationToken cancellationToken = default)
    {
        db.GradeSubjectAssignments.Remove(assignment);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<GradeSubjectAssignmentDto[]> ListByPeriodAsync(Guid periodId, CancellationToken cancellationToken = default) =>
        await db.GradeSubjectAssignments
            .AsNoTracking()
            .Where(x => x.PeriodId == periodId)
            .OrderBy(x => x.GradeLevelId).ThenBy(x => x.SubjectId)
            .Select(x => new GradeSubjectAssignmentDto(
                x.Id, x.GradeLevelId, x.SubjectId, x.PeriodId,
                x.CreatedAt, x.UpdatedAt))
            .ToArrayAsync(cancellationToken);

    public async Task<GradeSubjectAssignmentDto[]> ListByGradeLevelAsync(Guid gradeLevelId, Guid periodId, CancellationToken cancellationToken = default) =>
        await db.GradeSubjectAssignments
            .AsNoTracking()
            .Where(x => x.GradeLevelId == gradeLevelId && x.PeriodId == periodId)
            .OrderBy(x => x.SubjectId)
            .Select(x => new GradeSubjectAssignmentDto(
                x.Id, x.GradeLevelId, x.SubjectId, x.PeriodId,
                x.CreatedAt, x.UpdatedAt))
            .ToArrayAsync(cancellationToken);
}