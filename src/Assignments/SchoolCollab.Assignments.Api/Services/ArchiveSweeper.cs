using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ArchiveAssignmentCommand;
using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Api.Services;

/// <summary>Pure dispatch core for the archive sweep (WS-A2 /
/// spec §7 Q6). Dispatches <see cref="ArchiveAssignmentCommand"/>
/// for each candidate; the dispatch wraps the candidate's tenant
/// context (mirror of <see cref="ScheduledPublishSweeper"/>). The
/// archive command itself is sweep-only — no public API route.</summary>
public static class ArchiveSweeper
{
    /// <summary>Dispatches <see cref="ArchiveAssignmentCommand"/>
    /// for each candidate that arrived due. Returns the number of
    /// candidates processed successfully.</summary>
    public static async Task<int> ArchiveDueAsync(
        IReadOnlyList<AssignmentSweepCandidate> candidates,
        ICommandHandler<ArchiveAssignmentCommand> archiveHandler,
        ITenantContextAccessor tenantAccessor,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(archiveHandler);
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
                        await archiveHandler.HandleAsync(
                            new ArchiveAssignmentCommand(candidate.Id),
                            ct);
                        return null;
                    },
                    cancellationToken);
                processed++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Archive failed for assignment {Id}", candidate.Id);
            }
        }

        return processed;
    }
}
