namespace SchoolCollab.Students.Core.Domain.Exceptions;

/// <summary>
/// Thrown when attempting to add a member to an <see cref="ActivityGroup"/> whose
/// <see cref="ActivityGroup.Status"/> is <see cref="ActivityGroupStatus.Archived"/>
/// (spec activity-group-enrollment.md FR-12). Archived groups are read-only —
/// to retire a group use <see cref="ActivityGroup.Archive"/>; to re-enable
/// suspended groups use <see cref="ActivityGroup.Reactivate"/>.
/// Maps to HTTP 422 Unprocessable Entity.
/// </summary>
public sealed class ArchivedGroupException : Exception
{
    public Guid ActivityGroupId { get; }

    public ArchivedGroupException(Guid activityGroupId)
        : base($"Activity group '{activityGroupId}' is archived and cannot accept new members.")
        => ActivityGroupId = activityGroupId;
}
