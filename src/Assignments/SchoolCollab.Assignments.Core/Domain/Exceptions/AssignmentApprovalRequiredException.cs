namespace SchoolCollab.Assignments.Core.Domain.Exceptions;

/// <summary>Raised when a publish (or schedule) is attempted while the
/// <c>FEATURE:RequireAssignmentApproval</c> flag is enabled and the
/// assignment's <see cref="Domain.ApprovalStatus"/> is not
/// <see cref="Domain.ApprovalStatus.Approved"/> (spec §7 Q2). The
/// API surface maps this to HTTP 400 via the catch block on the
/// schedule + publish routes.</summary>
public sealed class AssignmentApprovalRequiredException : Exception
{
    public AssignmentApprovalRequiredException(string message) : base(message) { }
}
