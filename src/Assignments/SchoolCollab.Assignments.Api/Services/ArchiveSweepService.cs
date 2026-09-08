using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ArchiveAssignmentCommand;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Api.Services;

/// <summary>BackgroundService that drives the archive sweep
/// (WS-A2 / spec §7 Q6 — archive Published/Closed assignments after
/// <c>DueDate + ArchiveGraceDays</c>). Loops every 24 hours. Mirrors
/// the <see cref="ScheduledPublishSweepService"/> shape (first sweep
/// immediately; per-candidate error isolation; cancellation-aware
/// shutdown).</summary>
public sealed class ArchiveSweepService(
    IServiceScopeFactory scopeFactory,
    ILogger<ArchiveSweepService> logger) : BackgroundService
{
    /// <summary>The sweep interval. Once a day is plenty — archive is
    /// a coarse retention transition (the grace window is already
    /// days-long).</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Archive sweep starting — interval {IntervalHours}h",
            Interval.TotalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Archive sweep failed");
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

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAssignmentRepository>();
        var archiveHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ArchiveAssignmentCommand>>();
        var tenantAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();

        var nowUtc = DateTimeOffset.UtcNow;
        var candidates = await repository.ListDueForArchiveAsync(nowUtc, cancellationToken);
        if (candidates.Count == 0)
        {
            return;
        }

        logger.LogInformation("Archive sweep: {Count} candidate(s) due", candidates.Count);
        var processed = await ArchiveSweeper.ArchiveDueAsync(
            candidates, archiveHandler, tenantAccessor, logger, cancellationToken);
        logger.LogInformation("Archive sweep: {Processed} of {Count} dispatched", processed, candidates.Count);
    }
}
