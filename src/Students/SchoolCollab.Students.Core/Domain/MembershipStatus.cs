namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// Status of an <see cref="ActivityGroupMembership"/> (spec
/// activity-group-enrollment.md FR-8). Memberships use status transitions
/// (<see cref="Active"/> → <see cref="Exited"/>|<see cref="Removed"/>) plus
/// <see cref="ActivityGroupMembership.ExitedOn"/>; rows are not hard-deleted in
/// normal operation, preserving history (NFR-8).
/// </summary>
public enum MembershipStatus
{
    Active = 0,
    Exited = 1,
    Removed = 2
}
