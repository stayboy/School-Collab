using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.PublishAssignmentCommand;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
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
/// error-isolation posture).
///
/// <para><b>Round <c>assignment-approval-workflow</c> (spec §3.5, Q12).</b>
/// <see cref="AssignmentApprovalRequiredException"/> is classified separately: a scheduled row
/// whose publish window arrived while it is still unapproved would otherwise throw and be
/// <b>error-logged every 15 minutes forever</b>. It is an expected posture, not a failure — the row
/// stays <c>Scheduled</c>, the skip is counted and logged once per pass at info level, and every
/// other exception keeps today's error handling.</para></summary>
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
        var awaitingApproval = 0;
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
            // §3.5 / Q12: an unapproved scheduled row is an EXPECTED skip, not an error —
            // counted below and logged at info level, so the log stops filling with the same
            // exception every tick.
            catch (AssignmentApprovalRequiredException ex)
            {
                awaitingApproval++;
                logger.LogDebug(
                    "Auto-publish skipped for assignment {Id} — awaiting approval: {Message}",
                    candidate.Id, ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Auto-publish failed for assignment {Id}", candidate.Id);
            }
        }

        if (awaitingApproval > 0)
        {
            logger.LogInformation(
                "Scheduled-publish sweep: {Count} candidate(s) skipped — awaiting approval",
                awaitingApproval);
        }

        return processed;
    }
}
