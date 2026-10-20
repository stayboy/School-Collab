namespace SchoolCollab.Assignments.Core.Domain.Exceptions;

/// <summary>Raised when an approval transition is attempted from a state that can never reach it
/// (round <c>assignment-approval-workflow</c> / spec §3.1): resubmitting a
/// <see cref="Domain.ApprovalStatus.Denied"/> assignment (terminal — duplicate it instead), or
/// resubmitting one that is still <see cref="Domain.ApprovalStatus.Pending"/> or already
/// <see cref="Domain.ApprovalStatus.Approved"/> (a resubmit would re-stamp <c>SubmittedAt</c> and
/// reset the approval window, making the §3.4 auto-return author-defeatable).
///
/// <para>A typed exception rather than <see cref="InvalidOperationException"/> so the API maps it
/// explicitly to <b>400</b> with the rule's own message (the
/// <see cref="AssignmentApprovalRequiredException"/> precedent) instead of relying on the generic
/// status-guard mapping.</para></summary>
public sealed class AssignmentApprovalStateException : Exception
{
    public AssignmentApprovalStateException(string message) : base(message) { }
}
