using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.AutoReturnAssignmentCommand;

/// <summary>
/// Returns a pending assignment for redo on the approval sweep's behalf (round
/// <c>assignment-approval-workflow</c> / spec §3.4, Q11) — the auto-return leg of the approval
/// window and of the "due date passed while pending" rule.
///
/// <para><b>Why a dedicated command instead of <c>RejectAssignmentCommand</c>.</b> The reject path
/// is not dispatchable from a Worker sweep: it carries the ar-20 principal guard (which throws
/// <c>MissingTeacherPrincipalException</c> for any Worker caller under real auth), and this round
/// deliberately does not give the Worker an <c>ICurrentUser</c>. This command therefore mirrors the
/// dependency-free <c>ArchiveAssignmentCommandHandler</c> shape — repository → domain method → save →
/// cache eviction, with no principal and no command-level policy resolve (the sweeper has already
/// resolved the effective window before dispatching).</para>
///
/// <para><see cref="ApproverId"/> is the documented system sentinel
/// (<c>ApprovalSweeper.SystemApproverId</c>): the domain refuses <see cref="Guid.Empty"/>, and
/// attributing the auto-return to a stable system id keeps the decision auditable.</para>
/// </summary>
public sealed record AutoReturnAssignmentCommand(Guid AssignmentId, Guid ApproverId, string Reason) : ICommand;
