using SchoolCollab.Assignments.Core.Domain;

namespace SchoolCollab.Assignments.Core.Data.Repositories;

/// <summary>
/// Persistence for per-ward content-module progress (WS-D1 / spec §3.3):
/// <see cref="ModuleProgress"/> rows keyed by (assignment, student,
/// module). Entity reads are tracked (the upsert path mutates + saves);
/// progress is queried per assignment+student by the submission gate and
/// the ward-view query handler.
/// </summary>
public interface IModuleProgressRepository
{
    /// <summary>The single progress row for a (assignment, student, module)
    /// key — null when the ward has not reported any progress yet. Tracked,
    /// so <see cref="RecordModuleProgress"/> may mutate and save it.</summary>
    Task<ModuleProgress?> GetAsync(Guid assignmentId, Guid studentId, Guid contentModuleId, CancellationToken ct = default);

    /// <summary>All progress rows for a (assignment, student) — the submission
    /// gate and the ward view project against these.</summary>
    Task<List<ModuleProgress>> ListProgressForAssignmentStudentAsync(Guid assignmentId, Guid studentId, CancellationToken ct = default);

    void Add(ModuleProgress progress);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
