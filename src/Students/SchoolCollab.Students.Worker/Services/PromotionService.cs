using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Events;

namespace SchoolCollab.Students.Worker.Services;

/// <summary>
/// Nightly background service that:
/// 1. Auto-completes active periods whose EndDate has passed.
/// 2. Promotes students from completed periods to their designated next period
///    (creating new enrollments, copying grade-subject and student-subject assignments).
/// </summary>
/// <remarks>
/// <para><b>Tenancy (global-tenant-filter.md FR-16 / AC-12).</b> This service runs as a
/// background host with no ambient tenant. Every entity it touches (Period,
/// StudentEnrollment, GradeSubjectAssignment, StudentSubjectAssignment) is strict
/// tenant-scoped, so the service MUST enumerate tenants via
/// <see cref="ITenantDirectory"/> and run the promotion body per tenant inside
/// <see cref="ITenantContextAccessor.RunWithExplicitTenantAsync"/>. A fresh DI scope
/// (and thus a fresh <see cref="StudentsDbContext"/>) is created per tenant so the
/// change tracker never crosses tenant boundaries. The "at most one current period"
/// invariant is automatically per-tenant because the Period query is tenant-filtered.</para>
/// </remarks>
public sealed class PromotionService(
    IServiceScopeFactory scopeFactory,
    IOptions<PromotionOptions> options,
    ILogger<PromotionService> logger) : BackgroundService
{
    private readonly PromotionOptions _options = options.Value;

    // Default promotion rule: advance one grade level if a higher level
    // exists for the tenant, otherwise repeat at the same grade level (FR-A4).
    private readonly IPromotionRule _promotionRule = new DefaultPromotionRule();

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

        // FR-16: enumerate tenants via the cross-context directory (NOT db.Tenants —
        // that DbSet is on SettingsDbContext, unavailable here). A fresh scope per
        // tenant gives a clean StudentsDbContext so the change tracker never mixes
        // tenants. The tenant directory read itself uses its own SettingsDbContext.
        IReadOnlyList<Guid> tenantIds;
        using (var dirScope = scopeFactory.CreateScope())
        {
            var directory = dirScope.ServiceProvider.GetRequiredService<ITenantDirectory>();
            tenantIds = await directory.GetAllTenantIdsAsync(ct);
        }

        if (tenantIds.Count == 0)
        {
            logger.LogWarning("PromotionService found no tenants; skipping cycle");
            return;
        }

        logger.LogDebug("PromotionService running for {Count} tenants", tenantIds.Count);

        var tenantContextAccessor = scopeFactory.CreateScope()
            .ServiceProvider.GetRequiredService<ITenantContextAccessor>();

        var totalCompleted = 0;
        var totalPromoted = 0;

        foreach (var tenantId in tenantIds)
        {
            try
            {
                var (completed, promoted) = await tenantContextAccessor.RunWithExplicitTenantAsync(
                    tenantId,
                    innerCt => RunForTenantAsync(tenantId, innerCt),
                    ct);

                totalCompleted += completed;
                totalPromoted += promoted;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Promotion cycle failed for tenant {TenantId}; continuing with next tenant", tenantId);
            }
        }

        logger.LogInformation(
            "Promotion cycle complete across {TenantCount} tenants: {Completed} periods completed, {Promoted} students promoted",
            tenantIds.Count, totalCompleted, totalPromoted);
    }

    /// <summary>
    /// Runs the promotion body for a single tenant. The caller has already set the
    /// tenant context via <see cref="ITenantContextAccessor.RunWithExplicitTenantAsync"/>;
    /// a fresh scope + DbContext is created here so the change tracker is tenant-pure.
    /// </summary>
    private async Task<(int completed, int promoted)> RunForTenantAsync(Guid tenantId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var eventPublisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Step 1: Auto-complete this tenant's active periods whose EndDate has passed.
        // (Per-tenant — the Period query is tenant-filtered by the current context.)
        var expiredPeriods = await dbContext.Periods
            .Where(p => p.Status == PeriodStatus.Active && p.EndDate < today)
            .ToListAsync(ct);

        foreach (var period in expiredPeriods)
        {
            logger.LogInformation(
                "Auto-completing expired period {PeriodId} ({Name}) for tenant {TenantId}",
                period.Id, period.Name, tenantId);
            period.Complete();
        }

        if (expiredPeriods.Count > 0)
        {
            await dbContext.SaveChangesAsync(ct);
            logger.LogInformation(
                "Auto-completed {Count} expired periods for tenant {TenantId}",
                expiredPeriods.Count, tenantId);
        }

        // Step 2: Promote students from this tenant's completed periods with a NextPeriodId.
        var completedWithNext = await dbContext.Periods
            .Where(p => p.Status == PeriodStatus.Completed && p.NextPeriodId != null)
            .ToListAsync(ct);

        var promoted = 0;

        foreach (var fromPeriod in completedWithNext)
        {
            var toPeriodId = fromPeriod.NextPeriodId!.Value;

            // Look up the next period through the tenant-filtered query (NOT FindAsync,
            // which can surface a change-tracked row from another tenant). Per-tenant,
            // the next period must belong to the same tenant.
            var toPeriod = await dbContext.Periods
                .FirstOrDefaultAsync(p => p.Id == toPeriodId, ct);

            if (toPeriod is null)
            {
                logger.LogWarning(
                    "Next period {NextPeriodId} not found for period {PeriodId} (tenant {TenantId}); skipping promotion",
                    toPeriodId, fromPeriod.Id, tenantId);
                continue;
            }

            if (toPeriod.Status != PeriodStatus.Active)
            {
                logger.LogWarning(
                    "Next period {NextPeriodId} is not active (status={Status}, tenant {TenantId}); skipping promotion",
                    toPeriodId, toPeriod.Status, tenantId);
                continue;
            }

            promoted += await PromoteStudentsAsync(
                dbContext, eventPublisher, fromPeriod.Id, toPeriodId, tenantId, ct);
        }

        if (promoted > 0)
        {
            logger.LogInformation(
                "Promoted {Count} students for tenant {TenantId}", promoted, tenantId);
        }

        return (expiredPeriods.Count, promoted);
    }

    private async Task<int> PromoteStudentsAsync(
        StudentsDbContext dbContext,
        IIntegrationEventPublisher eventPublisher,
        Guid fromPeriodId,
        Guid toPeriodId,
        Guid tenantId,
        CancellationToken ct)
    {
        // All queries below are tenant-filtered by the current (per-tenant) context.
        var activeEnrollments = await dbContext.StudentEnrollments
            .Where(e => e.PeriodId == fromPeriodId && e.Status == EnrollmentStatus.Active)
            .ToListAsync(ct);

        if (activeEnrollments.Count == 0) return 0;

        // Check which students already have an enrollment in the target period.
        var existingStudentIds = await dbContext.StudentEnrollments
            .Where(e => e.PeriodId == toPeriodId)
            .Select(e => e.StudentId)
            .ToHashSetAsync(ct);

        var newEnrollments = new List<StudentEnrollment>();
        var domainEvents = new List<IDomainEvent>();

        // Resolve the tenant's grade levels once so the promotion rule can decide,
        // per enrollment, whether to advance a grade or repeat (FR-A4).
        var gradeLevels = await dbContext.GradeLevels.ToListAsync(ct);
        var gradeLevelById = gradeLevels.ToDictionary(g => g.Id);

        foreach (var enrollment in activeEnrollments)
        {
            if (existingStudentIds.Contains(enrollment.StudentId)) continue;

            // Promotion vs. repetition: target the next grade level if one exists
            // for the tenant, else stay at the same grade level.
            var targetGradeLevelId = gradeLevelById.TryGetValue(enrollment.GradeLevelId, out var fromGradeLevel)
                ? _promotionRule.Resolve(fromGradeLevel, gradeLevels)
                : enrollment.GradeLevelId;

            // A target equal to the current grade level means the rule repeated the
            // student (no higher level exists); otherwise they were promoted.
            var outcome = targetGradeLevelId == enrollment.GradeLevelId
                ? PromotionOutcome.Repeated
                : PromotionOutcome.Promoted;

            var newEnrollment = StudentEnrollment.Create(
                enrollment.StudentId,
                toPeriodId,
                targetGradeLevelId,
                promotionOutcome: outcome);

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

        // Enqueue integration events (will be dispatched by OutboxDispatcher under
        // this tenant's context — the publisher stamps OutboxMessage.TenantId).
        foreach (var domainEvent in domainEvents)
        {
            await eventPublisher.EnqueueAsync(domainEvent, ct);
        }

        await eventPublisher.EnqueueAsync(
            new StudentsPromotedEvent(fromPeriodId, toPeriodId, newEnrollments.Count), ct);

        return newEnrollments.Count;
    }
}
