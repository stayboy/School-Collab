namespace SchoolCollab.Assignments.Core.Domain;

/// <summary>
/// The kind of an <see cref="AssignmentTarget"/> constraint
/// (documents/specs/assignment-authoring-compartments.md §7.1 TGT-1). Persisted as the
/// enum's name (the repo's enum wire/DB convention), so the backfill SQL and the partial
/// indexes can filter on the literal string <c>'AllStudents'</c>.
/// </summary>
public enum TargetKind
{
    /// <summary>TGT-2 — the whole tenant cohort. Carries no <c>RefId</c> and is mutually
    /// exclusive with every other kind.</summary>
    AllStudents = 0,

    /// <summary>TGT-4 — one grade level; resolves through active enrollments in the
    /// current period.</summary>
    GradeLevel = 1,

    /// <summary>TGT-5 — one grade stream (<c>StreamCodedValueId</c>); resolved
    /// grade-agnostically against active enrollments.</summary>
    Stream = 2,

    /// <summary>TGT-6 — one individual student; must be active and not soft-deleted.</summary>
    Student = 3,

    /// <summary>TGT-7 — one activity group; resolves to its active members, archived
    /// groups excluded (EC-4).</summary>
    ActivityGroup = 4
}
