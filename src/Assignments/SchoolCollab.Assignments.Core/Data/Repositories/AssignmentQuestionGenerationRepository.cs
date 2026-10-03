using SchoolCollab.Assignments.Core.Domain;

namespace SchoolCollab.Assignments.Core.Data.Repositories;

internal sealed class AssignmentQuestionGenerationRepository(AssignmentsDbContext db)
    : IAssignmentQuestionGenerationRepository
{
    public void Add(AssignmentQuestionGeneration generation) => db.AssignmentQuestionGenerations.Add(generation);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
