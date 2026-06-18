using Microsoft.EntityFrameworkCore;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

internal sealed class StudentSubjectAssignmentRepository(StudentsDbContext db) : IStudentSubjectAssignmentRepository
{
    public Task<StudentSubjectAssignment?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.StudentSubjectAssignments.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task AddAsync(StudentSubjectAssignment assignment, CancellationToken cancellationToken = default)
    {
        await db.StudentSubjectAssignments.AddAsync(assignment, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(StudentSubjectAssignment assignment, CancellationToken cancellationToken = default)
    {
        db.StudentSubjectAssignments.Remove(assignment);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<StudentSubjectAssignmentDto[]> ListByStudentAsync(Guid studentId, Guid periodId, CancellationToken cancellationToken = default) =>
        await db.StudentSubjectAssignments
            .AsNoTracking()
            .Where(x => x.StudentId == studentId && x.PeriodId == periodId)
            .OrderBy(x => x.SubjectId)
            .Select(x => new StudentSubjectAssignmentDto(
                x.Id, x.StudentId, x.SubjectId, x.PeriodId,
                x.IsOverride, x.SourceType.ToString(),
                x.CreatedAt, x.UpdatedAt))
            .ToArrayAsync(cancellationToken);

    public async Task<StudentSubjectAssignmentDto[]> ListByPeriodAsync(Guid periodId, CancellationToken cancellationToken = default) =>
        await db.StudentSubjectAssignments
            .AsNoTracking()
            .Where(x => x.PeriodId == periodId)
            .OrderBy(x => x.StudentId).ThenBy(x => x.SubjectId)
            .Select(x => new StudentSubjectAssignmentDto(
                x.Id, x.StudentId, x.SubjectId, x.PeriodId,
                x.IsOverride, x.SourceType.ToString(),
                x.CreatedAt, x.UpdatedAt))
            .ToArrayAsync(cancellationToken);
}