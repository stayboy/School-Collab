using Microsoft.EntityFrameworkCore;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.DTOs;

namespace SchoolCollab.Assignments.Core.Data.Repositories;

internal sealed class ModuleProgressRepository(AssignmentsDbContext db) : IModuleProgressRepository
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
