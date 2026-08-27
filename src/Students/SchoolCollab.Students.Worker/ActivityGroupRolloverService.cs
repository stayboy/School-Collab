using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.RolloverActivityGroup;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Worker;

/// <summary>
/// Scheduled DateRange rollover sweep (spec activity-group-enrollment.md FR-54):
/// periodically rolls every group whose enrollment window has ended, using the
/// same <see cref="RolloverActivityGroupHandler"/> as the admin-forced command.
/// Interval configurable via <c>Rollover:IntervalMinutes</c> (default daily).
/// </summary>
public sealed class ActivityGroupRolloverService(
    IServiceScopeFactory scopeFactory,
    ILogger<ActivityGroupRolloverService> logger) : BackgroundService
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = DefaultInterval;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // A failed sweep must not crash the worker; retry next interval.
                logger.LogError(ex, "Activity-group rollover sweep failed");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
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
        var groups = scope.ServiceProvider.GetRequiredService<IActivityGroupRepository>();
        var rollover = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<RolloverActivityGroup>>();

        var due = await groups.GetGroupsDueForRolloverAsync(
            DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);
        if (due.Length == 0)
            return;

        logger.LogInformation("Rolling over {Count} DateRange activity group(s)", due.Length);

        foreach (var id in due)
        {
            try
            {
                await rollover.HandleAsync(new RolloverActivityGroup(id), cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Rollover failed for activity group {Id}", id);
            }
        }
    }
}