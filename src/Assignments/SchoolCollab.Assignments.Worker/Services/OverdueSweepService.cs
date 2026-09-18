using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Services.Delivery;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Worker.Services;

/// <summary>
/// E3 (ar-19) hosted loop that drives the overdue sweep every 60 minutes (first sweep
/// immediately). Dispatches the pure <see cref="OverdueSweeper"/> core. Mirrors the
/// <see cref="ReminderSweepService"/> loop shape.
/// </summary>
public sealed class OverdueSweepService(
    IServiceScopeFactory scopeFactory,
    ILogger<OverdueSweepService> logger) : BackgroundService
{
    /// <summary>The sweep interval. Overdue is a coarse daily-grade signal — hourly is ample.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(60);

    /// <summary>P2-1: serialises sweep passes (a published-event reminder kick never
    /// touches the overdue path, but two sweeps could still race on shared state).
    /// Wait-or-skip keeps runs non-overlapping.</summary>
    private readonly SemaphoreSlim _sweepGate = new(1, 1);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Overdue sweep starting — interval {IntervalMinutes}m",
            Interval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Overdue sweep failed");
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

    /// <summary>Runs one overdue sweep pass. If another pass is already running, this
    /// call returns immediately without sweeping.</summary>
    public async Task SweepAsync(CancellationToken cancellationToken = default)
    {
        // P2-1: wait-or-skip gate.
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
        var addressResolver = scope.ServiceProvider.GetRequiredService<IContactAddressResolver>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        var candidates = await repository.ListOverdueSweepCandidatesAsync(
            timeProvider.GetUtcNow(), cancellationToken);
        if (candidates.Count == 0)
        {
            return;
        }

        var sentReady = await OverdueSweeper.QueueOverdueAsync(
            candidates, db, tenantAccessor, addressResolver,
            timeProvider, logger, cancellationToken);
        logger.LogInformation("Overdue sweep queued {Queued} overdue notification(s) for {Total} candidate(s)",
            sentReady, candidates.Count);
    }
}
