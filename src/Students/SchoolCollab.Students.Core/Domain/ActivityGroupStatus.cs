namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// Lifecycle status of an <see cref="ActivityGroup"/>, independent of
/// <see cref="PeriodStatus"/> (spec activity-group-enrollment.md FR-3).
/// A group MAY be created as <see cref="Active"/> with no active period.
/// </summary>
public enum ActivityGroupStatus
{
    Active = 0,
    Suspended = 1,
    Archived = 2
}
