using SchoolCollab.Assignments.Core.Domain;

namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// The assignment's POLICY-scope derivation — the single rule every policy-resolution seam uses now
/// that the author-maintained primary grade is gone (round <c>drop-primary-grade</c>, replacing the
/// retired D-2).
///
/// <para><b>The rule:</b> exactly ONE distinct grade target ⇒ that grade (the grade's effective
/// policy applies); zero or two-or-more grade targets ⇒ <see langword="null"/> (the tenant-default
/// policy). It is a pure function of the authored <see cref="AssignmentTarget"/> rows, never an
/// authored field, so the publish path, the schedule path, the read handlers, the sweep reads and
/// the recipient preview can never resolve a different grade for the same assignment.</para>
///
/// <para>Deliberately NOT the teacher-scope rule: visibility widens per grade target (any overlap),
/// where the policy scope narrows to a single grade. See
/// <c>documents/solution/adr-cross-module-calls.md</c> for the round's cross-module bearing.</para>
/// </summary>
public static class AssignmentPolicyScope
{
    /// <summary>The assignment's distinct grade-target ids, in persisted order (empty when the set
    /// carries no grade target).</summary>
    public static IReadOnlyList<Guid> GradeTargetIds(Assignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        return assignment.Targets
            .Where(t => t.Kind == TargetKind.GradeLevel && t.RefId.HasValue)
            .Select(t => t.RefId!.Value)
            .Distinct()
            .ToList();
    }

    /// <summary>The policy-scope grade for an assignment's grade-target rows — see the type's
    /// summary for the one-distinct-grade rule.</summary>
    public static Guid? DeriveGrade(Assignment assignment) =>
        DeriveGrade(GradeTargetIds(assignment));

    /// <summary>The policy-scope grade for a grade-target id set — see the type's summary for the
    /// one-distinct-grade rule.</summary>
    public static Guid? DeriveGrade(IReadOnlyList<Guid> gradeTargetIds)
    {
        ArgumentNullException.ThrowIfNull(gradeTargetIds);

        var distinct = gradeTargetIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        return distinct.Length == 1 ? distinct[0] : null;
    }
}
