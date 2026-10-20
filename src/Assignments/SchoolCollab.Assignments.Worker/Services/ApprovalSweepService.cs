using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.AutoReturnAssignmentCommand;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Worker.Services;

/// <summary>
/// Hosted loop that drives the approval sweep (round <c>assignment-approval-workflow</c> / spec §3.4,
/// §6) every 60 minutes (first sweep immediately). The window is measured in <b>days</b>, so an
/// hourly tick is ample; the pure dispatch logic lives in <see cref="ApprovalSweeper"/>. Mirrors the
/// <see cref="OverdueSweepService"/> loop shape (single-flight gate, per-candidate error isolation,
/// cancellation-aware shutdown).
/// </summary>
public sealed class ApprovalSweepService(
    IServiceScopeFactory scopeFactory,
    ILogger<ApprovalSweepService> logger) : BackgroundService
{
    /// <summary>The sweep interval. The window unit is days — hourly is ample.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(60);

    /// <summary>Serialises sweep passes; a second concurrent pass is skipped (the
    /// <see cref="OverdueSweepService"/> posture).</summary>
    private readonly SemaphoreSlim _sweepGate = new(1, 1);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Approval sweep starting — interval {IntervalMinutes}m",
            Interval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Approval sweep failed");
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

    /// <summary>Runs one approval sweep pass. If another pass is already running, this call returns
    /// immediately without sweeping.</summary>
    public async Task SweepAsync(CancellationToken cancellationToken = default)
    {
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
        var repository = scope.ServiceProvider.GetRequiredService<IAssignmentRepository>();
        var policyResolver = scope.ServiceProvider.GetRequiredService<IAssignmentPolicyResolver>();
        var autoReturnHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<AutoReturnAssignmentCommand>>();
        var tenantAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        var candidates = await repository.ListPendingApprovalSweepCandidatesAsync(cancellationToken);
        if (candidates.Count == 0)
        {
            return;
        }

        var returned = await ApprovalSweeper.ReturnExpiredAsync(
            candidates, policyResolver, autoReturnHandler, tenantAccessor, timeProvider, logger, cancellationToken);

        logger.LogInformation(
            "Approval sweep returned {Returned} of {Total} pending assignment(s)", returned, candidates.Count);
    }
}
