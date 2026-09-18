using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Assignments.Core.Services.Delivery;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Worker.Services;

/// <summary>
/// E3 (ar-19) hosted loop that drives the reminder + completion-to-guardian sweeps
/// every 15 minutes (first sweep immediately). Dispatches the pure
/// <see cref="ReminderSweeper"/> cores against repository-provided cross-tenant
/// candidates; <see cref="SweepAsync"/> is also called once per
/// <c>AssignmentPublishedIntegrationEvent</c> so the first reminder evaluation does not
/// wait a full sweep interval (idempotent by construction). Mirrors the
/// <c>ArchiveSweepService</c> loop shape (per-candidate isolation, cancellation-aware).
/// </summary>
public sealed class ReminderSweepService(
    IServiceScopeFactory scopeFactory,
    ILogger<ReminderSweepService> logger) : BackgroundService
{
    /// <summary>The sweep interval. 15 min is acceptable because reminder cadence
    /// granularity is hours (ReminderIntervalHours); completion latencies are similar.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    /// <summary>P2-1: serialises sweep passes. A published-event kick may run
    /// concurrently with the timer pass and nothing else backstops Reminder/Completion/
    /// Overdue uniqueness; wait-or-skip prevents the two from interleaving.</summary>
    private readonly SemaphoreSlim _sweepGate = new(1, 1);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Reminder sweep starting — interval {IntervalMinutes}m",
            Interval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Reminder sweep failed");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break; // shutting down
            }
        }
    }

    /// <summary>Runs one reminder + completion sweep pass. Safe to call from the message
    /// handler (idempotent by construction — a recipient is only queued once the current
    /// cycle has settled). If another pass is already running (timer + published-event
    /// kick overlap), this call returns immediately without sweeping.</summary>
    public async Task SweepAsync(CancellationToken cancellationToken = default)
    {
        // P2-1: wait-or-skip gate. TryWait(0) — if an earlier pass holds the gate the
        // kick is dropped; no queuing backlog, no interleaved writes.
        if (!_sweepGate.Wait(0))
        {
            return;
        }

        try
        {
            await SweepOnceAsync(cancellationToken);
        }
        finally
        {
            _sweepGate.Release();
        }
    }

    private async Task SweepOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssignmentsDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IAssignmentRepository>();
        var tenantAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        var policyResolver = scope.ServiceProvider.GetRequiredService<INotificationPolicyResolver>();
        var addressResolver = scope.ServiceProvider.GetRequiredService<IContactAddressResolver>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        var reminderCandidates = await repository.ListReminderSweepCandidatesAsync(cancellationToken);
        if (reminderCandidates.Count > 0)
        {
            var logState = await repository.ListReminderLogStateAsync(cancellationToken);
            var logStateMap = logState.ToDictionary(s => s.RecipientId);

            var sentReady = await ReminderSweeper.QueueRemindersAsync(
                reminderCandidates, logStateMap, db, tenantAccessor, policyResolver,
                addressResolver, timeProvider, logger, cancellationToken);
            logger.LogInformation("Reminder sweep queued {Queued} reminder(s) for {Total} candidate(s)",
                sentReady, reminderCandidates.Count);
        }

        var completionCandidates = await repository.ListCompletionSweepCandidatesAsync(cancellationToken);
        if (completionCandidates.Count > 0)
        {
            var completions = await ReminderSweeper.QueueCompletionsAsync(
                completionCandidates, db, tenantAccessor, addressResolver,
                timeProvider, logger, cancellationToken);
            logger.LogInformation("Completion sweep queued {Queued} completion(s) for {Total} candidate(s)",
                completions, completionCandidates.Count);
        }
    }
}
