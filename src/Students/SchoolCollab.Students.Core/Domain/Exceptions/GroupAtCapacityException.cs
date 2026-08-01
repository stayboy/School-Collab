namespace SchoolCollab.Students.Core.Domain.Exceptions;

/// <summary>
/// Thrown when adding a member to an <see cref="ActivityGroup"/> whose
/// <see cref="ActivityGroup.Capacity"/> is set and the count of active members
/// is already at or above the capacity (spec activity-group-enrollment.md
/// FR-13, AC-9). When <see cref="ActivityGroup.Capacity"/> is <c>null</c>, no
/// limit is enforced (AC-10).
/// </summary>
public sealed class GroupAtCapacityException : Exception
{
    public Guid ActivityGroupId { get; }
    public int Capacity { get; }
    public int ActiveCount { get; }

    public GroupAtCapacityException(Guid activityGroupId, int capacity, int activeCount)
        : base($"Activity group '{activityGroupId}' is at capacity ({activeCount}/{capacity}). Cannot add more members.")
    {
        ActivityGroupId = activityGroupId;
        Capacity = capacity;
        ActiveCount = activeCount;
    }
}
