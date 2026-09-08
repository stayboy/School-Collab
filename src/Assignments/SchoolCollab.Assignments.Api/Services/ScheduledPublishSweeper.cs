using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.PublishAssignmentCommand;
using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Api.Services;

/// <summary>Pure dispatch core for the scheduled-publish sweep
/// (WS-A2 / spec §3.5 step 8). For each candidate, wraps the
/// publish-handler dispatch in the candidate's explicit tenant
/// context (the ambient tenant in a BackgroundService is empty /
/// system, so the tenant-filtered GetAsync would resolve nothing).
/// Per-candidate try/catch isolates failures — one bad row never
/// crashes the sweep (mirrors <see cref="StagedFileSweeper"/>'s
/// error-isolation posture).</summary>
public static class ScheduledPublishSweeper
{
    /// <summary>Dispatches <see cref="PublishAssignmentCommand"/> for
    /// each candidate that arrived due. Returns the number of
    /// candidates processed successfully.</summary>
    public static async Task<int> PublishDueAsync(
        IReadOnlyList<AssignmentSweepCandidate> candidates,
        ICommandHandler<PublishAssignmentCommand> publishHandler,
        ITenantContextAccessor tenantAccessor,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(publishHandler);
        ArgumentNullException.ThrowIfNull(tenantAccessor);
        ArgumentNullException.ThrowIfNull(logger);

        var processed = 0;
        foreach (var candidate in candidates)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                await tenantAccessor.RunWithExplicitTenantAsync<object?>(
                    candidate.TenantId,
                    async ct =>
                    {
                        await publishHandler.HandleAsync(
                            new PublishAssignmentCommand(candidate.Id, null),
                            ct);
                        return null;
                    },
                    cancellationToken);
                processed++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Auto-publish failed for assignment {Id}", candidate.Id);
            }
        }

        return processed;
    }
}
