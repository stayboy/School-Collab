namespace SchoolCollab.Students.Core.Domain.Exceptions;

/// <summary>
/// Thrown when attempting to delete an <see cref="ActivityGroup"/> that has
/// dependent records — either any <see cref="ActivityGroupMembership"/> row
/// (any status; the <c>activity_group_id</c> FK is <c>ON DELETE RESTRICT</c>,
/// preserving membership history — NFR-8) or a <c>Draft</c>/<c>Published</c>
/// assignment that references the group (FR-6, EC-1). To retire a referenced
/// group, use <see cref="ActivityGroup.Archive"/> (FR-3, Q3).
/// Mirrors <see cref="GradeLevelReferencedException"/>.
/// </summary>
public sealed class ActivityGroupReferencedException : Exception
{
    public Guid ActivityGroupId { get; }
    public string[] References { get; }

    public ActivityGroupReferencedException(Guid activityGroupId, string[] references)
        : base($"Activity group '{activityGroupId}' cannot be deleted because it is referenced by: {string.Join(", ", references)}.")
    {
        ActivityGroupId = activityGroupId;
        References = references;
    }
}
