using SchoolCollab.Students.Application.Services;

namespace SchoolCollab.Students.Application.Services;

/// <summary>
/// One activity group as the student surface sees it: the group, whether the student already belongs
/// to it, and — when they do not — whether they <b>may</b> join, with the reason when they may not.
/// </summary>
public sealed record ActivityGroupOption(
    ActivityGroupDto Group,
    bool IsMember,
    bool IsValid,
    string? InvalidReason)
{
    /// <summary>True when the option is addable: not already joined and every rule passes.</summary>
    public bool CanAdd => !IsMember && IsValid;
}

/// <summary>
/// The <b>display mirror</b> of the server's membership rules, for the student create/edit rail and its
/// group picker. The server remains the authority — <c>AddMembershipHandler</c> throws
/// <c>InactiveGroupException</c> / <c>GradeNotEligibleException</c> /
/// <c>EnrollmentWindowClosedException</c> / <c>GroupAtCapacityException</c> and the API maps each to a 422
/// or 409 — but a picker has to show <i>why</i> an option is unavailable before the user commits to it,
/// and round-tripping every group to learn that is worse than mirroring four deterministic checks over
/// data the list endpoint already returns (<see cref="ActivityGroupDto"/> carries
/// <c>IsActive</c>, <c>EligibleGradeIds</c>, <c>Capacity</c>, <c>ActiveMemberCount</c> and the window
/// dates).
///
/// <para><b>Capacity is the one approximation.</b> The server counts per <c>(group, period)</c> for
/// period-aligned spans and group-wide otherwise; the DTO only carries the group-wide
/// <c>ActiveMemberCount</c>, so a period-aligned group can read as full slightly early here. That errs
/// toward showing an option as unavailable — never toward a submit the server rejects — and the server
/// still decides.</para>
///
/// <para>The mirror MUST stay in step with the handler. When a rule moves, change both and extend
/// <c>ActivityGroupEligibilityTests</c> — a drift here shows an option as addable that the server then
/// rejects, which is exactly the "looks usable, fails on submit" defect the panel pattern's §6 forbids.</para>
/// </summary>
public static class ActivityGroupEligibility
{
    /// <summary>
    /// Evaluates every group for one student.
    /// </summary>
    /// <param name="groups">The candidate groups (all of them, or a subset — order is preserved).</param>
    /// <param name="studentGradeLevelId">The student's grade level, from their <b>active</b> enrollment.
    /// Null when the student has no active enrollment — then no group is addable, because every
    /// membership is period- and grade-scoped.</param>
    /// <param name="memberGroupIds">The groups the student already holds an active membership in.</param>
    /// <param name="today">The date the window is tested against (injected so it is testable).</param>
    public static IReadOnlyList<ActivityGroupOption> Evaluate(
        IEnumerable<ActivityGroupDto> groups,
        Guid? studentGradeLevelId,
        IReadOnlyCollection<Guid> memberGroupIds,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(memberGroupIds);

        var results = new List<ActivityGroupOption>();
        foreach (var group in groups)
        {
            var isMember = memberGroupIds.Contains(group.Id);
            var reason = isMember ? null : RejectionReason(group, studentGradeLevelId, today);
            results.Add(new ActivityGroupOption(group, isMember, reason is null, reason));
        }

        return results;
    }

    /// <summary>The reason a student may not join <paramref name="group"/>, or null when they may.</summary>
    public static string? RejectionReason(ActivityGroupDto group, Guid? studentGradeLevelId, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(group);

        if (!group.IsActive) return "Group is switched off";

        // An empty eligible-grade set means "any grade" (repository contract, FR-40).
        if (group.EligibleGradeIds.Length > 0
            && (studentGradeLevelId is not { } gradeId || !group.EligibleGradeIds.Contains(gradeId)))
        {
            return studentGradeLevelId is null
                ? "Not enrolled in a grade yet"
                : "Not offered to this grade";
        }

        if (group.EnrollmentStartDate is { } opens && today < opens)
        {
            return $"Enrolment opens {opens:d}";
        }

        if (group.EnrollmentEndDate is { } closes && today > closes)
        {
            return $"Enrolment window closed ({closes:d})";
        }

        if (group.Capacity is { } capacity && group.ActiveMemberCount >= capacity)
        {
            return $"Full ({group.ActiveMemberCount}/{capacity})";
        }

        return null;
    }
}
