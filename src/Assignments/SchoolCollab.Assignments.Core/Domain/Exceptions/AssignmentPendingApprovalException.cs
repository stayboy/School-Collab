namespace SchoolCollab.Assignments.Core.Domain.Exceptions;

/// <summary>Raised when an authoring write path is attempted on an assignment whose
/// <see cref="Domain.ApprovalStatus"/> is <see cref="Domain.ApprovalStatus.Pending"/> — the
/// pending-approval edit hold (round <c>assignment-approval-workflow</c> / spec §3.2, Q9). The API
/// maps it to HTTP <b>409</b>: the request is legal, the row is simply locked until an approver
/// approves, rejects or denies it. Duplicating the row stays allowed (a copy is a fresh Draft with
/// no approval state), so the author always has a way forward.</summary>
public sealed class AssignmentPendingApprovalException : Exception
{
    public AssignmentPendingApprovalException(Guid assignmentId)
        : base("This assignment is awaiting approval; editing is locked until an approver decides.")
    {
        AssignmentId = assignmentId;
    }

    /// <summary>The locked assignment.</summary>
    public Guid AssignmentId { get; }
}
