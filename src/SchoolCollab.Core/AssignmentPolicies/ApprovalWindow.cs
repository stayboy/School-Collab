namespace SchoolCollab.Core.AssignmentPolicies;

/// <summary>
/// The approval-window arithmetic (round <c>assignment-approval-workflow</c> / spec §3.3–§3.4), in
/// one place because three consumers must agree on it: the two assignment read projections
/// (<c>WindowExpiresAt</c>), the approval sweep (auto-return at expiry) and the UI countdown that
/// renders the projected value.
///
/// <para><b>Q7 range rule at the consuming end.</b> A stored non-positive
/// <see cref="AssignmentPolicyFields.ApprovalWindowDays"/> is treated as "no window" — the
/// <c>MaxNotifications</c> defensive posture. The write seams reject a non-positive value (400), so
/// this only covers a row that predates the guard or was written around it; without it, one bad row
/// would auto-return every pending assignment on the next sweep tick.</para>
/// </summary>
public static class ApprovalWindow
{
    /// <summary>The window in days that actually applies: the stored value when positive, otherwise
    /// <see langword="null"/> (no window).</summary>
    public static int? EffectiveDays(int? windowDays) => windowDays is > 0 ? windowDays : null;

    /// <summary>The moment a pending submission expires: <c>SubmittedAt + effective days</c>, and
    /// <see langword="null"/> when either side is null (never submitted, or no window configured) —
    /// a fail-open policy resolve can therefore never invent a deadline.</summary>
    public static DateTimeOffset? ExpiresAt(DateTimeOffset? submittedAt, int? windowDays)
    {
        var days = EffectiveDays(windowDays);
        return submittedAt.HasValue && days.HasValue
            ? submittedAt.Value.AddDays(days.Value)
            : null;
    }
}
