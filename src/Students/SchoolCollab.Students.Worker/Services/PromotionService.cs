using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Events;
using SchoolCollab.Core.Messaging;

namespace SchoolCollab.Students.Worker.Services;

/// <summary>
/// Nightly background service that:
/// 1. Auto-completes active periods whose EndDate has passed.
/// 2. Promotes students from completed periods to their designated next period
///    (creating new enrollments, copying grade-subject and student-subject assignments).
/// </summary>
public sealed class PromotionService(
    IServiceScopeFactory scopeFactory,
    IOptions<PromotionOptions> options,
    ILogger<PromotionService> logger) : BackgroundService
{
    private readonly PromotionOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "PromotionService started; schedule={Schedule}", _options.CronExpression);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.PollInterval, stoppingToken);
                await RunPromotionCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "PromotionService cycle failed; will retry after delay");
                await Task.Delay(_options.ErrorDelay, stoppingToken);
            }
        }

        logger.LogInformation("PromotionService stopping");
    }

    private async Task RunPromotionCycleAsync(CancellationToken ct)
    {
        logger.LogDebug("Running promotion cycle");

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var eventPublisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Step 1: Auto-complete active periods whose EndDate has passed
        var expiredPeriods = await dbContext.Periods
            .Where(p => p.Status == PeriodStatus.Active && p.EndDate < today)
            .ToListAsync(ct);

        foreach (var period in expiredPeriods)
        {
            logger.LogInformation("Auto-completing expired period {PeriodId} ({Name})", period.Id, period.Name);
            period.Complete();
        }

        if (expiredPeriods.Count > 0)
        {
            await dbContext.SaveChangesAsync(ct);
            logger.LogInformation("Auto-completed {Count} expired periods", expiredPeriods.Count);
        }

        // Step 2: Promote students from completed periods with a NextPeriodId
        var completedWithNext = await dbContext.Periods
            .Where(p => p.Status == PeriodStatus.Completed && p.NextPeriodId != null)
            .ToListAsync(ct);

        var totalPromoted = 0;

        foreach (var fromPeriod in completedWithNext)
        {
            var toPeriodId = fromPeriod.NextPeriodId!.Value;

            // Verify next period exists and is active
            var toPeriod = await dbContext.Periods.FindAsync([toPeriodId], ct);
            if (toPeriod is null)
            {
                logger.LogWarning(
                    "Next period {NextPeriodId} not found for period {PeriodId}; skipping promotion",
                    toPeriodId, fromPeriod.Id);
                continue;
            }

            if (toPeriod.Status != PeriodStatus.Active)
            {
                logger.LogWarning(
                    "Next period {NextPeriodId} is not active (status={Status}); skipping promotion",
                    toPeriodId, toPeriod.Status);
                continue;
            }

            var promoted = await PromoteStudentsAsync(
                dbContext, eventPublisher, fromPeriod.Id, toPeriodId, ct);

            if (promoted > 0)
            {
                totalPromoted += promoted;
                logger.LogInformation(
                    "Promoted {Count} students from period {FromId} to {ToId}",
                    promoted, fromPeriod.Id, toPeriodId);
            }
        }

        if (totalPromoted > 0)
        {
            logger.LogInformation("Promotion cycle complete; {Total} students promoted total", totalPromoted);
        }
        else
        {
            logger.LogDebug("No students promoted this cycle");
        }
    }

    private async Task<int> PromoteStudentsAsync(
        StudentsDbContext dbContext,
        IIntegrationEventPublisher eventPublisher,
        Guid fromPeriodId,
        Guid toPeriodId,
        CancellationToken ct)
    {
        // Get active enrollments in the completed period
        var activeEnrollments = await dbContext.StudentEnrollments
            .Where(e => e.PeriodId == fromPeriodId && e.Status == EnrollmentStatus.Active)
            .ToListAsync(ct);

        if (activeEnrollments.Count == 0) return 0;

        // Check which students already have an enrollment in the target period
        var existingStudentIds = await dbContext.StudentEnrollments
            .Where(e => e.PeriodId == toPeriodId)
            .Select(e => e.StudentId)
            .ToHashSetAsync(ct);

        var newEnrollments = new List<StudentEnrollment>();
        var domainEvents = new List<IDomainEvent>();

        foreach (var enrollment in activeEnrollments)
        {
            if (existingStudentIds.Contains(enrollment.StudentId)) continue;

            var newEnrollment = StudentEnrollment.Create(
                enrollment.StudentId,
                toPeriodId,
                enrollment.GradeLevelId);

            newEnrollments.Add(newEnrollment);
            domainEvents.AddRange(newEnrollment.DomainEvents);
            newEnrollment.ClearDomainEvents();
        }

        if (newEnrollments.Count == 0) return 0;

        dbContext.StudentEnrollments.AddRange(newEnrollments);

        // Copy GradeSubjectAssignments to next period (skip duplicates)
        var gradeSubjectAssignments = await dbContext.GradeSubjectAssignments
            .Where(a => a.PeriodId == fromPeriodId)
            .ToListAsync(ct);

        var existingGradeSubjects = await dbContext.GradeSubjectAssignments
            .Where(a => a.PeriodId == toPeriodId)
            .Select(a => new { a.GradeLevelId, a.SubjectId })
            .ToListAsync(ct);

        var existingGradeSubjectSet = existingGradeSubjects
            .Select(g => (g.GradeLevelId, g.SubjectId))
            .ToHashSet();

        var promotedGradeIds = newEnrollments.Select(e => e.GradeLevelId).ToHashSet();

        foreach (var assignment in gradeSubjectAssignments)
        {
            if (!promotedGradeIds.Contains(assignment.GradeLevelId)) continue;
            if (existingGradeSubjectSet.Contains((assignment.GradeLevelId, assignment.SubjectId))) continue;

            var newAssignment = GradeSubjectAssignment.Create(
                assignment.GradeLevelId, assignment.SubjectId, toPeriodId);

            dbContext.GradeSubjectAssignments.Add(newAssignment);
        }

        // Copy StudentSubjectAssignments for promoted students (skip duplicates)
        var promotedStudentIds = newEnrollments.Select(e => e.StudentId).ToHashSet();

        var studentSubjectAssignments = await dbContext.StudentSubjectAssignments
            .Where(a => a.PeriodId == fromPeriodId && promotedStudentIds.Contains(a.StudentId))
            .ToListAsync(ct);

        var existingStudentSubjects = await dbContext.StudentSubjectAssignments
            .Where(a => a.PeriodId == toPeriodId && promotedStudentIds.Contains(a.StudentId))
            .Select(a => new { a.StudentId, a.SubjectId })
            .ToListAsync(ct);

        var existingStudentSubjectSet = existingStudentSubjects
            .Select(s => (s.StudentId, s.SubjectId))
            .ToHashSet();

        foreach (var assignment in studentSubjectAssignments)
        {
            if (existingStudentSubjectSet.Contains((assignment.StudentId, assignment.SubjectId))) continue;

            var newAssignment = StudentSubjectAssignment.Create(
                assignment.StudentId,
                assignment.SubjectId,
                toPeriodId,
                assignment.IsOverride,
                assignment.SourceType);

            dbContext.StudentSubjectAssignments.Add(newAssignment);
        }

        await dbContext.SaveChangesAsync(ct);

        // Enqueue integration events (will be dispatched by OutboxDispatcher)
        foreach (var domainEvent in domainEvents)
        {
            await eventPublisher.EnqueueAsync(domainEvent, ct);
        }

        await eventPublisher.EnqueueAsync(
            new StudentsPromotedEvent(fromPeriodId, toPeriodId, newEnrollments.Count), ct);

        return newEnrollments.Count;
    }
}