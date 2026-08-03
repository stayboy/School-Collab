namespace SchoolCollab.Students.Core.Domain.Exceptions;

/// <summary>
/// Thrown when attempting to add a student to an <see cref="ActivityGroup"/> when
/// the student already holds an active membership in that group (spec
/// activity-group-enrollment.md FR-10). The partial unique index on
/// (tenant_id, student_id, activity_group_id) WHERE status = 0 backs this at the
/// DB level; this exception is the handler-level pre-check.
/// Maps to HTTP 409 Conflict.
/// </summary>
public sealed class DuplicateActiveMembershipException : Exception
{
    public Guid StudentId { get; }
    public Guid ActivityGroupId { get; }

    public DuplicateActiveMembershipException(Guid studentId, Guid activityGroupId)
        : base($"Student '{studentId}' is already an active member of activity group '{activityGroupId}'.")
    {
        StudentId = studentId;
        ActivityGroupId = activityGroupId;
    }
}
