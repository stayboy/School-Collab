namespace SchoolCollab.Assignments.Core.Domain;

/// <summary>The approval status of an assignment when the
/// <c>FEATURE:RequireAssignmentApproval</c> flag is enabled
/// (spec §3.5 / §7 Q2). Persisted as a nullable column on
/// <see cref="Assignment"/> — <c>null</c> means the assignment has
/// not been submitted for approval yet (the default state). The EF
/// configuration stores the member by its integer value (no
/// <c>HasConversion</c>), so adding a member at the END is additive.</summary>
public enum ApprovalStatus
{
    /// <summary>Submitted and awaiting an approver's decision.</summary>
    Pending = 0,

    /// <summary>Approved — publish / schedule are released (Q13: approval never publishes by
    /// itself).</summary>
    Approved = 1,

    /// <summary>Sent back for redo (Q10): editable, resubmittable. Carries the approver's optional
    /// reason in <see cref="Assignment.ApprovalReason"/>.</summary>
    Rejected = 2,

    /// <summary>Denied (round assignment-approval-workflow / spec §3.1, Q10) — <b>terminal</b>: the
    /// edit hold is released, but the row can never be resubmitted or published; duplicating it is
    /// the escape hatch.</summary>
    Denied = 3
}
