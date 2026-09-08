using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.PublishAssignmentCommand;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Api.Services;

/// <summary>BackgroundService that drives the scheduled-publish
/// sweep (WS-A2 / spec §3.5 step 8). Loops every 15 minutes —
/// publish is time-sensitive. Mirrors
/// <see cref="StagedFileSweepService"/>'s loop shape (first sweep
/// immediately; per-candidate error isolation; cancellation-aware
/// shutdown). The pure dispatch logic lives in
/// <see cref="ScheduledPublishSweeper"/>.</summary>
public sealed class ScheduledPublishSweepService(
    IServiceScopeFactory scopeFactory,
    ILogger<ScheduledPublishSweepService> logger) : BackgroundService
{
    /// <summary>The sweep interval. Publish is time-sensitive — every
    /// 15 minutes. The choice is a code-level constant; a future
    /// round may surface this via an AppHost parameter.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Scheduled-publish sweep starting — interval {IntervalMinutes}m",
            Interval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduled-publish sweep failed");
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
        var publishHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<PublishAssignmentCommand>>();
        var tenantAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();

        var nowUtc = DateTimeOffset.UtcNow;
        var candidates = await repository.ListScheduledForAutoPublishAsync(nowUtc, cancellationToken);
        if (candidates.Count == 0)
        {
            return;
        }

        logger.LogInformation("Scheduled-publish sweep: {Count} candidate(s) due", candidates.Count);
        var processed = await ScheduledPublishSweeper.PublishDueAsync(
            candidates, publishHandler, tenantAccessor, logger, cancellationToken);
        logger.LogInformation("Scheduled-publish sweep: {Processed} of {Count} dispatched", processed, candidates.Count);
    }
}
