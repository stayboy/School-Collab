using Microsoft.EntityFrameworkCore;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.DTOs;

namespace SchoolCollab.Assignments.Core.Data.Repositories;

/// <summary>
/// WS-A5 — the ward assignment candidate-set projection (the read-only
/// aggregation behind <see cref="ListWardAssignmentsHandler"/>). Moved here from
/// the module-progress repository in slice 2b (ar-13 A-1 cohesion fix) so
/// module-progress persistence is no longer also the ward-list read surface.
/// </summary>
internal sealed class WardAssignmentProjectionRepository(AssignmentsDbContext db) : IWardAssignmentProjectionRepository
{
    public Task<List<AssignmentSummary>> ListWardAssignmentsAsync(Guid studentId, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        return db.Assignments.AsNoTracking()
            .Where(a =>
                (a.Status == AssignmentStatus.Published
                 || (a.Status == AssignmentStatus.Scheduled && a.AvailableFromUtc != null && a.AvailableFromUtc <= nowUtc))
                && db.AssignmentRecipients.Any(r => r.WardStudentId == studentId && r.AssignmentId == a.Id))
            .OrderByDescending(a => a.DueDate ?? DateTimeOffset.MaxValue)
            .Select(a => new AssignmentSummary(
                a.Id, a.Title, a.Description, a.AssignmentType, a.GradingFormat, a.TargetAudienceType,
                a.TopicId, a.GradeLevelId, a.Status, a.DueDate, a.MaxScore, a.MandatoryReview,
                a.CreatedByTeacherId, a.CreatedAt, a.UpdatedAt,
                a.AvailableFromUtc, a.ArchiveGraceDays, a.ApprovalStatus, a.ApprovedBy, a.ApprovedAt,
                a.PassScore, a.MaxAttempts,
                a.RequiresSignature,
                a.DifficultyEasyCount, a.DifficultyMediumCount, a.DifficultyHardCount))
            .ToListAsync(ct);
    }
}
