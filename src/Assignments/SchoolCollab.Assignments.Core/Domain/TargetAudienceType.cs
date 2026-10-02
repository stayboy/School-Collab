using System.ComponentModel;

namespace SchoolCollab.Assignments.Core.Domain;

/// <summary>
/// The **derived/compat** legacy single-choice audience
/// (documents/specs/assignment-authoring-compartments.md §7.4 TGT-15). R2 moves the authored
/// source of truth to the <see cref="AssignmentTarget"/> child rows and keeps this column for
/// one release, derived at every write point by
/// <see cref="Assignment.SyncDerivedTargeting"/>; the list DTO, the Admin list surface and the
/// ward projection keep reading it unchanged. It is dropped in a follow-up round.
/// </summary>
public enum TargetAudienceType
{
    [Description("Everyone")]
    AllStudents = 0,
    [Description("By Grade Level")]
    SelectedGrades = 1,
    [Description("By Group")]
    SelectedGroups = 2,

    /// <summary>D-1 (round <c>assignment-targeting-r2</c>): the only addition to this enum —
    /// the derived value for a target set whose rows are neither an <c>AllStudents</c> row nor
    /// include a <c>GradeLevel</c> or <c>ActivityGroup</c> row (a <c>Stream</c>/<c>Student</c>
    /// mix, or no rows at all).</summary>
    [Description("Mixed")]
    Mixed = 3
}
