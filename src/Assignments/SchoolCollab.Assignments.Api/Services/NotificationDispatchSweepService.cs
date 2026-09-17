using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.Services;

namespace SchoolCollab.Assignments.Api.Services;

/// <summary>
/// BackgroundService that drives the WS-E2 (ar-16) notification delivery drain. Loops
/// every <see cref="Interval"/> and delegates to
/// <see cref="NotificationDispatchService.DispatchPendingAsync"/> in its own DI scope.
/// Mirrors <see cref="ArchiveSweepService"/> exactly (first pass immediately;
/// per-candidate error isolation inside the Core service; cancellation-aware shutdown).
/// Decision (a): the drain lives in the API host for E2 — the Assignments worker project
/// is E3's deliverable, so this moves with <see cref="ArchiveSweepService"/> then.
/// </summary>
public sealed class NotificationDispatchSweepService(
    IServiceScopeFactory scopeFactory,
    ILogger<NotificationDispatchSweepService> logger) : BackgroundService
{
    /// <summary>The drain interval. Short: retry timing comes from the stored
    /// <c>NextRetryAt</c>, so the loop only decides how promptly a due row is picked up.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Notification dispatch drain starting — interval {IntervalMinutes}m",
            Interval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<NotificationDispatchService>();
                var sent = await dispatcher.DispatchPendingAsync(stoppingToken);
                if (sent > 0)
                {
                    logger.LogInformation("Notification dispatch drain: {Sent} row(s) sent", sent);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Notification dispatch drain failed");
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
}
