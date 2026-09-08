using Microsoft.EntityFrameworkCore;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.DTOs;
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
                a.CreatedByTeacherId, a.CreatedAt, a.UpdatedAt,
                a.AvailableFromUtc, a.ArchiveGraceDays, a.ApprovalStatus, a.ApprovedBy, a.ApprovedAt))
            .ToListAsync(ct);
    }

    /// <summary>Sanctioned cross-tenant read for the scheduled-publish
    /// sweep (WS-A2 / spec §3.5 step 8). <c>IgnoreQueryFilters(["Tenant"])</c>
    /// is the same opt-out the staged-file sweep uses — the sweep
    /// performs NO writes here, only projects id + tenant id for the
    /// per-candidate explicit-tenant dispatch.</summary>
    public Task<List<AssignmentSweepCandidate>> ListScheduledForAutoPublishAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        return Db.Assignments
            .IgnoreQueryFilters(["Tenant"])
            .AsNoTracking()
            .Where(a => a.Status == AssignmentStatus.Scheduled
                && a.AvailableFromUtc != null
                && a.AvailableFromUtc <= nowUtc)
            .Select(a => new AssignmentSweepCandidate(a.Id, a.TenantId))
            .ToListAsync(ct);
    }

    /// <summary>Sanctioned cross-tenant read for the archive sweep
    /// (WS-A2 / spec §7 Q6 — <c>DueDate + ArchiveGraceDays &lt;= now</c>).
    /// Same opt-out posture as the scheduled-publish query above.</summary>
    public Task<List<AssignmentSweepCandidate>> ListDueForArchiveAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        return Db.Assignments
            .IgnoreQueryFilters(["Tenant"])
            .AsNoTracking()
            .Where(a => (a.Status == AssignmentStatus.Published || a.Status == AssignmentStatus.Closed)
                && a.DueDate != null
                && a.DueDate!.Value.AddDays(a.ArchiveGraceDays) <= nowUtc)
            .Select(a => new AssignmentSweepCandidate(a.Id, a.TenantId))
            .ToListAsync(ct);
    }

    public void DetectChanges() => Db.ChangeTracker.DetectChanges();
}
