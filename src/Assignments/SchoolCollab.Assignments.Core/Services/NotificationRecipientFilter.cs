using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// Applies the effective notification policy — plus the effective assignment policy's
/// per-role contact caps — to a resolved recipient set at publish time
/// (notification-delivery-plan.md §3; documents/solution/assignment-policy-fields.md
/// §5): drops recipients whose channel is blocked, orders by preferred-channel order,
/// caps Primary-guardian and other-guardian ("copy") recipients per sendout, and caps the
/// whole sendout at <c>MaxNotifications</c> (0 = no recipients are broadcast). Pure and
/// deterministic so it is unit-testable. <see cref="ContactChannel"/> (Students) and
/// <see cref="NotificationChannel"/> (Core) enums align by int value (Email=0, SMS=1,
/// WhatsApp=2), so channels are compared numerically.
///
/// <para><b>Role caps (D4/D5/Q7).</b> The bucket is keyed on
/// <see cref="AssignmentRecipient.OwnerType"/>, the authoritative discriminator: a
/// <see cref="ContactOwnerType.Student"/>-owned recipient carries no guardian role by
/// construction (<c>StudentGuardian.Role</c> is a non-nullable
/// <see cref="GuardianRole"/>, so only a student owner can produce a null role) and is
/// therefore exempt from both caps, exactly as D4 scopes the caps to guardian contacts.
/// The null-role test is kept as a redundant net. A cap applies only when it is
/// <c>&gt;= 1</c>; a stored non-positive cap is defensively treated as <i>uncapped</i>
/// (Q7 — the same posture as the <c>MaxNotifications is >= 0</c> check below), because
/// the write path rejects non-positive caps but rows written before that rule exist.
/// <see cref="EffectiveNotificationPolicy.MaxNotifications"/> is applied afterwards and
/// unchanged (D5), so the smaller of the two caps wins for the sendout as a whole.</para>
/// </summary>
public static class NotificationRecipientFilter
{
    public static IReadOnlyList<AssignmentRecipient> Apply(
        IReadOnlyList<AssignmentRecipient> recipients,
        EffectiveNotificationPolicy policy,
        EffectiveAssignmentPolicy assignmentPolicy)
    {
        if (recipients.Count == 0)
            return recipients;

        var blocked = policy.BlockedChannels.Select(c => (int)c).ToHashSet();
        var preferredIndex = policy.PreferredChannelOrder.Length == 0
            ? new Dictionary<int, int>()
            : policy.PreferredChannelOrder
                .Select((c, i) => new KeyValuePair<int, int>((int)c, i))
                .ToDictionary(kv => kv.Key, kv => kv.Value);

        var ordered = recipients
            .Where(r => !blocked.Contains((int)r.Channel))
            .OrderBy(r => preferredIndex.TryGetValue((int)r.Channel, out var idx) ? idx : int.MaxValue)
            .ThenBy(r => (int)r.Channel)
            .ToList();

        // D4 — per-role contact caps. Non-positive is defensively uncapped (Q7).
        var maxPrimary = assignmentPolicy.MaxPrimaryContacts is >= 1 ? assignmentPolicy.MaxPrimaryContacts : null;
        var maxCopy = assignmentPolicy.MaxCopyContacts is >= 1 ? assignmentPolicy.MaxCopyContacts : null;
        if (maxPrimary is not null || maxCopy is not null)
            ordered = ApplyRoleCaps(ordered, maxPrimary, maxCopy);

        if (policy.MaxNotifications is >= 0)
            ordered = ordered.Take(policy.MaxNotifications.Value).ToList();

        return ordered;
    }

    /// <summary>
    /// Keeps at most <paramref name="maxPrimary"/> Primary-guardian and
    /// <paramref name="maxCopy"/> other-guardian recipients, preserving the channel order
    /// already established. Student-owned (and, redundantly, role-less) rows are exempt.
    /// </summary>
    private static List<AssignmentRecipient> ApplyRoleCaps(
        List<AssignmentRecipient> ordered, int? maxPrimary, int? maxCopy)
    {
        var primaryTaken = 0;
        var copyTaken = 0;
        var kept = new List<AssignmentRecipient>(ordered.Count);

        foreach (var recipient in ordered)
        {
            if (recipient.OwnerType == ContactOwnerType.Student || recipient.Role is null)
            {
                kept.Add(recipient);
                continue;
            }

            if (recipient.Role == GuardianRole.Primary)
            {
                if (maxPrimary is null || primaryTaken < maxPrimary)
                {
                    kept.Add(recipient);
                    primaryTaken++;
                }
            }
            else if (maxCopy is null || copyTaken < maxCopy)
            {
                kept.Add(recipient);
                copyTaken++;
            }
        }

        return kept;
    }
}
