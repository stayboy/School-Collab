using Microsoft.EntityFrameworkCore;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Data.Repositories;

namespace SchoolCollab.Assignments.Core.Data.Repositories;

internal sealed class AssignmentRepository(AssignmentsDbContext db)
    : RepositoryBase<Assignment, AssignmentsDbContext>(db), IAssignmentRepository
{
    public async Task<List<AssignmentSummary>> ListAsync(AssignmentStatus? status, CancellationToken ct = default)
    {
        var query = Db.Assignments.AsNoTracking();

        if (status.HasValue)
            query = query.Where(a => a.Status == status.Value);

        return await query
            .OrderByDescending(a => a.UpdatedAt)
            .Select(a => new AssignmentSummary(
                a.Id, a.Title, a.Description, a.AssignmentType, a.GradingFormat, a.TargetAudienceType,
                a.TopicId, a.GradeLevelId, a.Status, a.DueDate, a.MaxScore, a.MandatoryReview,
                a.CreatedByTeacherId, a.CreatedAt, a.UpdatedAt))
            .ToListAsync(ct);
    }

    public void DetectChanges() => Db.ChangeTracker.DetectChanges();
}
