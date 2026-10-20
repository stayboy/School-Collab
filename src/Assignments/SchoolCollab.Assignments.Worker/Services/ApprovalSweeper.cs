using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.AutoReturnAssignmentCommand;
using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Worker.Services;

/// <summary>
/// Pure dispatch core for the approval sweep (round <c>assignment-approval-workflow</c> / spec §3.4,
/// §6). For each candidate — an assignment that is both <c>Pending</c> and <c>Draft</c> — resolves
/// the effective assignment policy for the row's derived grade inside the candidate's <b>explicit
/// tenant context</b> (the ambient tenant in a BackgroundService is empty / system, so the
/// tenant-filtered reads would resolve nothing), applies the §3.4 order, and dispatches
/// <see cref="AutoReturnAssignmentCommand"/> when a rule fires:
///
/// <list type="number">
///   <item><b>Due date passed wins</b> — an approved-but-overdue assignment is pointless, so this
///   rule outranks the window and fires even when no window is configured.</item>
///   <item><b>Approval window expired</b> — <c>SubmittedAt + effective ApprovalWindowDays ≤ now</c>.</item>
///   <item><b>No window and no passed due date ⇒ skip</b> — a fail-open resolve (or the built-in
///   null default) must never invent a deadline.</item>
/// </list>
///
/// Per-candidate try/catch isolates failures — one bad row (or a row that changed since the read,
/// where the domain's Pending guard now refuses the reject) never crashes the sweep. Mirrors
/// <see cref="OverdueSweeper"/>'s shape.
/// </summary>
public static class ApprovalSweeper
{
    /// <summary>
    /// The system approver the sweep attributes its auto-returns to. A fixed, non-empty id because
    /// <c>Assignment.Reject</c> refuses <see cref="Guid.Empty"/> and identity wiring for a machine
    /// actor is the documented placeholder (round assignment-approval-workflow; the ar-20
    /// <c>teacher_id</c> seam is human-only). Not a real user: it exists so the decision is
    /// attributable in the log and the reason is stored, never so the row looks human-approved.
    /// </summary>
    public static readonly Guid SystemApproverId = new("00000000-0000-0000-0000-0000000000A5");

    /// <summary>The reason stored and shown to the author when the approval window expires.</summary>
    public const string WindowExpiredReason = "approval window expired";

    /// <summary>The reason stored and shown to the author when the due date passes while pending.</summary>
    public const string DueDatePassedReason = "due date passed";

    /// <summary>Returns every candidate whose §3.4 rule has fired, reporting how many were returned.</summary>
    public static async Task<int> ReturnExpiredAsync(
        IReadOnlyList<AssignmentApprovalSweepCandidate> candidates,
        IAssignmentPolicyResolver policyResolver,
        ICommandHandler<AutoReturnAssignmentCommand> autoReturnHandler,
        ITenantContextAccessor tenantAccessor,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(policyResolver);
        ArgumentNullException.ThrowIfNull(autoReturnHandler);
        ArgumentNullException.ThrowIfNull(tenantAccessor);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        var nowUtc = timeProvider.GetUtcNow();
        var returned = 0;

        foreach (var candidate in candidates)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                var reason = await tenantAccessor.RunWithExplicitTenantAsync<object?>(
                    candidate.TenantId,
                    async ct =>
                    {
                        // §3.4 row 2 first: the due date outranks the window (and applies even when
                        // the effective policy configures no window at all).
                        if (candidate.DueDate is { } dueDate && dueDate <= nowUtc)
                        {
                            await autoReturnHandler.HandleAsync(
                                new AutoReturnAssignmentCommand(candidate.AssignmentId, SystemApproverId, DueDatePassedReason),
                                ct);
                            return (object?)DueDatePassedReason;
                        }

                        var policy = await policyResolver.ResolveAsync(candidate.PolicyGradeId, ct);
                        var expiresAt = ApprovalWindow.ExpiresAt(candidate.SubmittedAt, policy.ApprovalWindowDays);
                        // §3.4 row 3: no window (or no submission stamp) ⇒ nothing to enforce.
                        if (expiresAt is null || expiresAt.Value > nowUtc)
                        {
                            return null;
                        }

                        await autoReturnHandler.HandleAsync(
                            new AutoReturnAssignmentCommand(candidate.AssignmentId, SystemApproverId, WindowExpiredReason),
                            ct);
                        return (object?)WindowExpiredReason;
                    },
                    cancellationToken);

                if (reason is string appliedReason)
                {
                    returned++;
                    logger.LogInformation(
                        "Approval sweep returned assignment {Id} to the author for redo ({Reason})",
                        candidate.AssignmentId, appliedReason);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Approval return failed for assignment {Id}", candidate.AssignmentId);
            }
        }

        return returned;
    }
}
