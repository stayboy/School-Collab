namespace SchoolCollab.Students.Core.Domain.Exceptions;

/// <summary>
/// Thrown when attempting to add a member to an <see cref="ActivityGroup"/>
/// whose <see cref="ActivityGroup.IsActive"/> is <c>false</c> (Rev. 2,
/// spec activity-group-enrollment.md FR-12). An inactive group is read-only
/// for new memberships; existing memberships are preserved. Use
/// <see cref="ActivityGroup.Activate"/> to re-enable it.
/// Maps to HTTP 422 Unprocessable Entity.
/// </summary>
public sealed class InactiveGroupException : Exception
{
    public Guid ActivityGroupId { get; }

    public InactiveGroupException(Guid activityGroupId)
        : base($"Activity group '{activityGroupId}' is inactive and cannot accept new members.")
        => ActivityGroupId = activityGroupId;
}