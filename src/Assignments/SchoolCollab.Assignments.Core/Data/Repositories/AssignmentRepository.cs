using Microsoft.EntityFrameworkCore;
using SchoolCollab.Assignments.Core.Domain;

namespace SchoolCollab.Assignments.Core.Data.Repositories;

public sealed class AssignmentRepository : IAssignmentRepository
{
    private readonly AssignmentsDbContext _db;

    public AssignmentRepository(AssignmentsDbContext db) => _db = db;

    public async Task<Assignment?> GetAsync(Guid id, CancellationToken ct = default) =>
        await _db.Assignments.FindAsync([id], ct);

    public async Task<Assignment?> GetIncludingDeletedAsync(Guid id, CancellationToken ct = default)
    {
        // No query filter for soft-delete yet; just return directly
        return await _db.Assignments.FindAsync([id], ct);
    }

    public async Task AddAsync(Assignment assignment, CancellationToken ct = default)
    {
        await _db.Assignments.AddAsync(assignment, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Assignment assignment, CancellationToken ct = default)
    {
        _db.Assignments.Update(assignment);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<AssignmentSummary>> ListAsync(AssignmentStatus? status, CancellationToken ct = default)
    {
        var query = _db.Assignments.AsNoTracking();

        if (status.HasValue)
            query = query.Where(a => a.Status == status.Value);

        return await query
            .OrderByDescending(a => a.UpdatedAt)
            .Select(a => new AssignmentSummary(
                a.Id, a.Title, a.Description, a.AssignmentType, a.SubjectCodedValueId,
                a.GradeCodedValueId, a.Status, a.DueDate, a.MaxScore,
                a.CreatedByTeacherId, a.CreatedAt, a.UpdatedAt))
            .ToListAsync(ct);
    }
}