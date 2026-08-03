namespace SchoolCollab.Students.Core.Domain.Exceptions;

/// <summary>
/// Thrown when an active <see cref="ActivityGroupMembership"/> is expected but
/// none is found for the given (student, group) pair (spec
/// activity-group-enrollment.md FR-14). This covers the Remove and Exit
/// operations when the student is not currently an active member.
/// Maps to HTTP 404 Not Found.
/// </summary>
public sealed class MembershipNotFoundException : Exception
{
    public Guid ActivityGroupId { get; }
    public Guid StudentId { get; }

    public MembershipNotFoundException(Guid activityGroupId, Guid studentId)
        : base($"No active membership found for student '{studentId}' in activity group '{activityGroupId}'.")
    {
        ActivityGroupId = activityGroupId;
        StudentId = studentId;
    }
}
