namespace SchoolCollab.Assignments.Core.Domain;

/// <summary>The approval status of an assignment when the
/// <c>FEATURE:RequireAssignmentApproval</c> flag is enabled
/// (spec §3.5 / §7 Q2). Persisted as a nullable column on
/// <see cref="Assignment"/> — <c>null</c> means the assignment has
/// not been submitted for approval yet (the default state).</summary>
public enum ApprovalStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}
