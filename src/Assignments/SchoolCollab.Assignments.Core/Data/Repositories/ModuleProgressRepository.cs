using Microsoft.EntityFrameworkCore;
using SchoolCollab.Assignments.Core.Domain;

namespace SchoolCollab.Assignments.Core.Data.Repositories;

internal sealed class ModuleProgressRepository(AssignmentsDbContext db) : IModuleProgressRepository
{
    public Task<ModuleProgress?> GetAsync(Guid assignmentId, Guid studentId, Guid contentModuleId, CancellationToken ct = default) =>
        db.ModuleProgress.FirstOrDefaultAsync(
            p => p.AssignmentId == assignmentId
                && p.StudentId == studentId
                && p.ContentModuleId == contentModuleId, ct);

    public Task<List<ModuleProgress>> ListProgressForAssignmentStudentAsync(
        Guid assignmentId, Guid studentId, CancellationToken ct = default) =>
        db.ModuleProgress
            .AsNoTracking()
            .Where(p => p.AssignmentId == assignmentId && p.StudentId == studentId)
            .ToListAsync(ct);

    public void Add(ModuleProgress progress) => db.ModuleProgress.Add(progress);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
