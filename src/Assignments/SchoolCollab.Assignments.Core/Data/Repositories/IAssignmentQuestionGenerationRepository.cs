using SchoolCollab.Assignments.Core.Domain;

namespace SchoolCollab.Assignments.Core.Data.Repositories;

/// <summary>
/// R3 (D4/P1-2) — append-only store for <see cref="AssignmentQuestionGeneration"/> headers. A
/// dedicated repository (rather than the assignment aggregate's) because the header is a standalone
/// tenant entity written at generate time, outside any aggregate mutation — the
/// <c>IModuleProgressRepository</c> posture.
/// </summary>
public interface IAssignmentQuestionGenerationRepository
{
    /// <summary>Stages one header row for insertion.</summary>
    void Add(AssignmentQuestionGeneration generation);

    /// <summary>Persists the staged row.</summary>
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
