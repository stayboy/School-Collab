using Microsoft.EntityFrameworkCore;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Core.Data.Repositories;

public sealed class AssignmentRepository : IAssignmentRepository
{
    private readonly AssignmentsDbContext _db;
    private readonly ITenantProvider _tenantProvider;

    public AssignmentRepository(AssignmentsDbContext db, ITenantProvider tenantProvider)
    {
        _db = db;
        _tenantProvider = tenantProvider;
    }

    public async Task<Assignment?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var tenantId = _tenantProvider.GetTenantContext().TenantId;
        return await _db.Assignments
            .SingleOrDefaultAsync(a => a.Id == id && a.TenantId == tenantId, ct);
    }

    public async Task<Assignment?> GetIncludingDeletedAsync(Guid id, CancellationToken ct = default)
    {
        var tenantId = _tenantProvider.GetTenantContext().TenantId;
        return await _db.Assignments
            .SingleOrDefaultAsync(a => a.Id == id && a.TenantId == tenantId, ct);
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

    public async Task DeleteAsync(Assignment assignment, CancellationToken ct = default)
    {
        _db.Assignments.Remove(assignment);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<AssignmentSummary>> ListAsync(AssignmentStatus? status, CancellationToken ct = default)
    {
        var tenantId = _tenantProvider.GetTenantContext().TenantId;

        var query = _db.Assignments
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId);

        if (status.HasValue)
            query = query.Where(a => a.Status == status.Value);

        return await query
            .OrderByDescending(a => a.UpdatedAt)
            .Select(a => new AssignmentSummary(
                a.Id, a.Title, a.Description, a.AssignmentType, a.GradingFormat, a.TargetAudienceType,
                a.SubjectCodedValueId, a.GradeCodedValueId, a.Status, a.DueDate, a.MaxScore,
                a.CreatedByTeacherId, a.CreatedAt, a.UpdatedAt))
            .ToListAsync(ct);
    }
}